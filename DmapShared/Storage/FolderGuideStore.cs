using DmapShared.Models;
using System.Text.Json;

namespace DmapShared.Storage;

/// <summary>
/// Keeps the guide in a folder, with one small file per page so several people can
/// work on the guide at once (e.g. through a shared OneDrive folder) without
/// overwriting each other's pages:
///   guide.json            show-wide details (the show name)
///   seasons\{id}.json     one per season
///   campaigns\{id}.json   one per campaign, including its cast list
///   episodes\{id}.json    one per episode
///   locks\{id}.json       "someone is editing this page" markers
///   images\               pictures copied in from wherever the user picked them
/// Only pages that changed are written, and pages other people save are picked
/// up while the guide is open (see <see cref="SyncAsync"/>).
/// </summary>
public class FolderGuideStore : IGuideStore
{
    private const string GuideFile = "guide.json";
    private const string LegacyFileName = "dmap-guide.json";
    private static readonly string[] RecordFolders = { "seasons", "campaigns", "episodes" };

    public string Folder { get; }
    public string UserName { get; }
    public string Location => Folder;
    public string ImagesFolder => Path.Combine(Folder, "images");
    private string LocksFolder => Path.Combine(Folder, "locks");

    /// <summary>Each page as last read or written here (relative path → JSON as this app writes it).</summary>
    private readonly Dictionary<string, string> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each file's time and size when last read, so Sync only re-reads files that changed.</summary>
    private readonly Dictionary<string, (DateTime, long)> _seen = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public event Action? ChangesWaiting;
    public event Action? ImagesArrived { add { } remove { } }

    public FolderGuideStore(string folder, string userName)
    {
        Folder = folder;
        UserName = userName;
    }

    public static bool HasData(string folder) =>
        File.Exists(Path.Combine(folder, GuideFile)) || File.Exists(Path.Combine(folder, LegacyFileName));

    /// <summary>
    /// Whether this user may change the guide. A folder shared from OneDrive with "Can view"
    /// is read-only on disk; this checks by opening the guide file for writing, without
    /// writing anything (so nothing is changed and OneDrive has nothing to sync).
    /// </summary>
    public bool CanWrite()
    {
        try
        {
            using var _ = new FileStream(Path.Combine(Folder, GuideFile), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception)
        {
            return true; // missing or busy: let saving report any real problem
        }
    }

    // ============================================================
    // LOAD
    // ============================================================
    /// <summary>Load the guide. Pages that couldn't be read are listed and left untouched.</summary>
    public Task<GuideLoadResult> LoadAsync()
    {
        var unreadable = new List<string>();
        string legacy = Path.Combine(Folder, LegacyFileName);
        ShowData data;

        if (!File.Exists(Path.Combine(Folder, GuideFile)) && File.Exists(legacy))
        {
            data = MigrateFrom(legacy);
        }
        else if (!HasData(Folder))
        {
            data = ShowData.CreateSeed();
            Save(data, (_, _) => true);
        }
        else
        {
            data = new ShowData();
            unreadable = Sync(data).Unreadable;
        }

        return Task.FromResult(new GuideLoadResult(data, unreadable));
    }

    /// <summary>Earlier versions kept the whole guide in one file; split it into one file per page.</summary>
    private ShowData MigrateFrom(string legacy)
    {
        var data = JsonSerializer.Deserialize<ShowData>(File.ReadAllText(legacy), GuideRecords.JsonOptions) ?? new ShowData();
        data.LinkParents();
        Save(data, (_, _) => true, stamp: false);

        // Keep the old file, renamed so it isn't split again.
        File.Move(legacy, Path.Combine(Folder, "dmap-guide.before-sharing.json"), overwrite: true);
        return data;
    }

    // ============================================================
    // SAVE
    // ============================================================
    public async Task<int> SaveAsync(ShowData data, Func<object, string, Task<bool>> keepMine)
    {
        // Ask about conflicts one at a time, then write; the files themselves are small.
        var answers = new Dictionary<object, bool>(ReferenceEqualityComparer.Instance);
        foreach (var (path, record) in Records(data))
        {
            if (Conflict(path, record) is string who)
                answers[record] = await keepMine(record, who);
        }
        return Save(data, (record, _) => !answers.TryGetValue(record, out bool keep) || keep);
    }

    /// <summary>Who saved this page since it was read here, or null if nobody did (or it has no unsaved edits).</summary>
    private string? Conflict(string path, object record)
    {
        string json = GuideRecords.Serialize(record);
        if (!_known.TryGetValue(path, out var known) || json == known) return null;
        if (ReadOther(path, record.GetType()) is not { } other || other.Json == known) return null;
        return (other.Record as TreeNode)?.LastEditedBy is { Length: > 0 } name ? name : "Someone else";
    }

    private int Save(ShowData data, Func<object, string, bool> keepMine, bool stamp = true)
    {
        Directory.CreateDirectory(Folder);
        var records = Records(data).ToList();
        int written = 0;

        foreach (var (path, record) in records)
        {
            string json = GuideRecords.Serialize(record);
            _known.TryGetValue(path, out var known);
            if (json == known) continue;

            if (known != null && ReadOther(path, record.GetType()) is { } other && other.Json != known)
            {
                var (theirs, theirJson) = other;
                // Someone else saved this page since it was read here.
                string who = (theirs as TreeNode)?.LastEditedBy is { Length: > 0 } name ? name : "Someone else";
                if (!keepMine(record, who))
                {
                    GuideTree.CopyInto(theirs, record);
                    _known[path] = theirJson;
                    Remember(path);
                    continue;
                }
            }

            if (stamp && record is TreeNode node)
            {
                node.LastEditedBy = UserName;
                node.LastEdited = DateTime.UtcNow;
                json = GuideRecords.Serialize(record);
            }

            Write(path, json);
            written++;
        }

        // Pages deleted here.
        var current = records.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _known.Keys.Where(p => !current.Contains(p)).ToList())
        {
            File.Delete(Path.Combine(Folder, path));
            _known.Remove(path);
            _seen.Remove(path);
        }

        return written;
    }

    /// <summary>Every page in the guide with the file it's saved in.</summary>
    private static IEnumerable<(string Path, object Record)> Records(ShowData data) =>
        GuideRecords.All(data).Select(r => (r.Kind == GuideRecords.Guide ? GuideFile : RecordPath(FolderFor(r.Kind), r.Id), r.Record));

    private static string FolderFor(string kind) => kind + "s";

    private static string RecordPath(string folder, string id) => Path.Combine(folder, $"{id}.json");

    /// <summary>The version of a page on disk, or null if it's missing or unreadable.</summary>
    private (object Record, string Json)? ReadOther(string path, Type type)
    {
        try
        {
            if (TryRead(path) is not string text) return null;
            var record = Parse(type, text);
            return (record, GuideRecords.Serialize(record));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Write(string path, string json)
    {
        string full = Path.Combine(Folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // Write to a temp file first so a crash mid-save can't corrupt the page.
        string tmp = full + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, full, overwrite: true);

        _known[path] = json;
        Remember(path);
    }

    // ============================================================
    // SYNC: pick up pages other people saved
    // ============================================================
    public Task<SyncResult> SyncAsync(ShowData data) => Task.FromResult(Sync(data));

    private SyncResult Sync(ShowData data)
    {
        var result = new SyncResult();
        var index = GuideTree.Index(data);

        try
        {
            SyncFile(data, GuideFile, index, result);

            // Parents before children, so a new episode's campaign is already in place.
            foreach (var folder in RecordFolders)
            {
                string dir = Path.Combine(Folder, folder);
                if (!Directory.Exists(dir)) continue;

                // Only "{id}.json"; this skips OneDrive's "{id}-PCNAME.json" conflict copies.
                foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                    if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out _))
                        SyncFile(data, Path.Combine(folder, Path.GetFileName(file)), index, result);
            }

            // Pages someone else deleted.
            foreach (var path in _known.Keys.Where(p => p != GuideFile).ToList())
            {
                if (File.Exists(Path.Combine(Folder, path))) continue;

                if (index.TryGetValue(Guid.Parse(Path.GetFileNameWithoutExtension(path)), out var node))
                {
                    if (GuideRecords.Serialize(node) != _known[path]) continue; // being edited here; saving puts it back
                    GuideTree.Remove(data, node);
                    result.Removed.Add(node);
                    result.Changed = true;
                }
                _known.Remove(path);
                _seen.Remove(path);
            }
        }
        catch (IOException) { }               // folder unavailable; try again next time
        catch (UnauthorizedAccessException) { }

        return result;
    }

    private void SyncFile(ShowData data, string path, Dictionary<Guid, TreeNode> index, SyncResult result)
    {
        var info = new FileInfo(Path.Combine(Folder, path));
        if (!info.Exists) return;
        var stamp = (info.LastWriteTimeUtc, info.Length);
        if (_seen.TryGetValue(path, out var seen) && seen == stamp) return;

        object incoming;
        string json;
        try
        {
            if (TryRead(path) is not string text) return;
            incoming = Parse(TypeFor(path), text);
            json = GuideRecords.Serialize(incoming);
        }
        catch (JsonException)
        {
            result.Unreadable.Add(path);
            _seen[path] = stamp;
            return;
        }
        catch (IOException) { return; } // busy, e.g. OneDrive still downloading it
        catch (UnauthorizedAccessException) { return; }

        _known.TryGetValue(path, out var known);
        if (json == known)
        {
            _seen[path] = stamp;
            return;
        }

        object? existing = incoming switch
        {
            ShowData => data,
            TreeNode n => index.GetValueOrDefault(n.Id),
            _ => null
        };

        // Unsaved edits here: leave them; Save asks whose version to keep.
        if (existing != null && known != null && GuideRecords.Serialize(existing) != known) return;

        // Its season / campaign hasn't arrived yet; try again next time.
        if (!GuideTree.Place(data, incoming, existing, index)) return;

        _known[path] = json;
        _seen[path] = stamp;
        result.Changed = true;
    }

    // ============================================================
    // WATCHING
    // ============================================================
    /// <summary>Raise <see cref="ChangesWaiting"/> when anything in the folder changes.</summary>
    public void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            _debounce = new Timer(_ => ChangesWaiting?.Invoke());
            _watcher = new FileSystemWatcher(Folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            // A save touches several files; wait for them to settle.
            void Changed(object? s, EventArgs e) => _debounce?.Change(750, Timeout.Infinite);
            _watcher.Changed += Changed;
            _watcher.Created += Changed;
            _watcher.Deleted += Changed;
            _watcher.Renamed += Changed;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // Not every folder can be watched; the app also checks regularly.
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    // ============================================================
    // EDIT LOCKS: "someone is editing this page"
    // ============================================================
    private string LockPath(Guid id) => Path.Combine(LocksFolder, $"{id}.json");

    public Task<EditLock?> ReadLockAsync(Guid id) => Task.FromResult(ReadLock(id));

    private EditLock? ReadLock(Guid id)
    {
        try
        {
            return TryRead(Path.Combine("locks", $"{id}.json")) is string text
                ? JsonSerializer.Deserialize<EditLock>(text)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task WriteLockAsync(Guid id, DateTime since)
    {
        try
        {
            Directory.CreateDirectory(LocksFolder);
            var mine = new EditLock(UserName, GuideSession.MachineName, GuideSession.Id, since, DateTime.UtcNow);
            string path = LockPath(id);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(mine));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception) { } // a missing lock only means no warning for others
        return Task.CompletedTask;
    }

    public Task ReleaseLockAsync(Guid id)
    {
        try
        {
            if (ReadLock(id)?.Session == GuideSession.Id) File.Delete(LockPath(id));
        }
        catch (Exception) { }
        return Task.CompletedTask;
    }

    // ============================================================
    // PICTURES
    // ============================================================
    public Task<string> ImportImageAsync(string sourceFile)
    {
        Directory.CreateDirectory(ImagesFolder);
        string name = $"{Guid.NewGuid():N}{Path.GetExtension(sourceFile).ToLowerInvariant()}";
        File.Copy(sourceFile, Path.Combine(ImagesFolder, name));
        return Task.FromResult($"images/{name}");
    }

    public string? ResolveImage(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        string full = Path.IsPathRooted(stored) ? stored : Path.Combine(Folder, stored);
        return File.Exists(full) ? full : null;
    }

    /// <summary>Copy this guide (pages + images) into another folder.</summary>
    public void CopyTo(string folder, ShowData data)
    {
        using var target = new FolderGuideStore(folder, UserName);
        target.Save(data, (_, _) => true, stamp: false);

        if (Directory.Exists(ImagesFolder))
        {
            Directory.CreateDirectory(target.ImagesFolder);
            foreach (var f in Directory.GetFiles(ImagesFolder))
            {
                string dest = Path.Combine(target.ImagesFolder, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Copy(f, dest);
            }
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private static Type TypeFor(string path) =>
        path == GuideFile ? typeof(ShowData) : Path.GetDirectoryName(path) switch
        {
            "seasons" => typeof(Season),
            "campaigns" => typeof(Campaign),
            _ => typeof(Episode)
        };

    private static object Parse(Type type, string text) =>
        JsonSerializer.Deserialize(text, type, GuideRecords.JsonOptions) ?? throw new JsonException("Empty file");

    /// <summary>Read a file other programs (OneDrive) may have open; null if it doesn't exist.</summary>
    private string? TryRead(string path)
    {
        string full = Path.Combine(Folder, path);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) when (attempt < 3) { Thread.Sleep(100); }
        }
    }

    private void Remember(string path)
    {
        var info = new FileInfo(Path.Combine(Folder, path));
        if (info.Exists) _seen[path] = (info.LastWriteTimeUtc, info.Length);
    }
}
