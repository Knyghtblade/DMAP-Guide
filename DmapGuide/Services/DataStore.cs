using DmapGuide.Models;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmapGuide.Services;

/// <summary>
/// Loads / saves the guide. Everything lives in one data folder, with one small
/// file per page so several people can work on the guide at once (e.g. through a
/// shared OneDrive folder) without overwriting each other's pages:
///   guide.json            show-wide details (the show name)
///   seasons\{id}.json     one per season
///   campaigns\{id}.json   one per campaign, including its cast list
///   episodes\{id}.json    one per episode
///   locks\{id}.json       "someone is editing this page" markers
///   images\               pictures copied in from wherever the user picked them
/// Only pages that changed are written, and pages other people save are picked
/// up while the guide is open (see <see cref="Sync"/>).
/// </summary>
public class DataStore : IDisposable
{
    private const string GuideFile = "guide.json";
    private const string LegacyFileName = "dmap-guide.json";
    private static readonly string[] RecordFolders = { "seasons", "campaigns", "episodes" };

    /// <summary>Child lists live in their own files, so they're left out of a page's file.</summary>
    private static readonly string[] ChildLists = { "Seasons", "Campaigns", "Episodes" };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Identifies this run of the app in lock files.</summary>
    public static readonly Guid Session = Guid.NewGuid();

    public static DataStore Current { get; private set; } = null!;

    public string Folder { get; }
    public string UserName { get; }
    public string ImagesFolder => Path.Combine(Folder, "images");
    private string LocksFolder => Path.Combine(Folder, "locks");

    /// <summary>Each page as last read or written here (relative path → JSON as this app writes it).</summary>
    private readonly Dictionary<string, string> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each file's time and size when last read, so Sync only re-reads files that changed.</summary>
    private readonly Dictionary<string, (DateTime, long)> _seen = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>Raised (on a background thread) shortly after files in the folder change.</summary>
    public event Action? FilesChanged;

    public DataStore(string folder, string userName)
    {
        Folder = folder;
        UserName = userName;
        Current = this;
    }

    public static bool HasData(string folder) =>
        File.Exists(Path.Combine(folder, GuideFile)) || File.Exists(Path.Combine(folder, LegacyFileName));

    // ============================================================
    // LOAD
    // ============================================================
    /// <summary>Load the guide. Pages that couldn't be read are listed in <paramref name="unreadable"/> and left untouched.</summary>
    public ShowData Load(out List<string> unreadable)
    {
        unreadable = new();
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

        // Open the table of contents at the campaign that's on air.
        if (data.CurrentCampaign is Campaign current)
        {
            current.IsExpanded = true;
            if (current.Season != null) current.Season.IsExpanded = true;
        }
        return data;
    }

    /// <summary>Earlier versions kept the whole guide in one file; split it into one file per page.</summary>
    private ShowData MigrateFrom(string legacy)
    {
        var data = JsonSerializer.Deserialize<ShowData>(File.ReadAllText(legacy), JsonOptions) ?? new ShowData();
        data.LinkParents();
        Save(data, (_, _) => true, stamp: false);

        // Keep the old file, renamed so it isn't split again.
        File.Move(legacy, Path.Combine(Folder, "dmap-guide.before-sharing.json"), overwrite: true);
        return data;
    }

    // ============================================================
    // SAVE
    // ============================================================
    /// <summary>
    /// Write the pages that changed since they were last read. If someone else saved
    /// one of them in the meantime, <paramref name="keepMine"/> (page, their name) decides
    /// whose version wins; false takes theirs, replacing the edits made here.
    /// Returns how many pages were written.
    /// </summary>
    public int Save(ShowData data, Func<object, string, bool> keepMine, bool stamp = true)
    {
        Directory.CreateDirectory(Folder);
        var records = Records(data).ToList();
        int written = 0;

        foreach (var (path, record) in records)
        {
            string json = Serialize(record);
            _known.TryGetValue(path, out var known);
            if (json == known) continue;

            if (known != null && ReadOther(path, record.GetType()) is { } other && other.Json != known)
            {
                var (theirs, theirJson) = other;
                // Someone else saved this page since it was read here.
                string who = (theirs as TreeNode)?.LastEditedBy is { Length: > 0 } name ? name : "Someone else";
                if (!keepMine(record, who))
                {
                    CopyInto(theirs, record);
                    _known[path] = theirJson;
                    Remember(path);
                    continue;
                }
            }

            if (stamp && record is TreeNode node)
            {
                node.LastEditedBy = UserName;
                node.LastEdited = DateTime.UtcNow;
                json = Serialize(record);
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
    private static IEnumerable<(string Path, object Record)> Records(ShowData data)
    {
        yield return (GuideFile, data);
        foreach (var s in data.Seasons)
        {
            yield return (RecordPath("seasons", s.Id), s);
            foreach (var c in s.Campaigns)
            {
                c.SeasonId = s.Id;
                yield return (RecordPath("campaigns", c.Id), c);
                foreach (var e in c.Episodes)
                {
                    e.CampaignId = c.Id;
                    yield return (RecordPath("episodes", e.Id), e);
                }
            }
        }
    }

    private static string RecordPath(string folder, Guid id) => Path.Combine(folder, $"{id}.json");

    /// <summary>The version of a page on disk, or null if it's missing or unreadable.</summary>
    private (object Record, string Json)? ReadOther(string path, Type type)
    {
        try
        {
            if (TryRead(path) is not string text) return null;
            var record = Parse(type, text);
            return (record, Serialize(record));
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
    /// <summary>
    /// Bring <paramref name="data"/> up to date with the files in the folder. Pages with
    /// unsaved edits here are left alone; <see cref="Save"/> sorts those out.
    /// </summary>
    public SyncResult Sync(ShowData data)
    {
        var result = new SyncResult();
        var index = Index(data);

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
                    if (Serialize(node) != _known[path]) continue; // being edited here; saving puts it back
                    Remove(data, node);
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
            json = Serialize(incoming);
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
        if (existing != null && known != null && Serialize(existing) != known) return;

        // Its season / campaign hasn't arrived yet; try again next time.
        if (!Place(data, incoming, existing, index)) return;

        _known[path] = json;
        _seen[path] = stamp;
        result.Changed = true;
    }

    /// <summary>Put a page read from disk into the guide, updating the copy already there if any.</summary>
    private static bool Place(ShowData data, object incoming, object? existing, Dictionary<Guid, TreeNode> index)
    {
        switch (incoming)
        {
            case ShowData:
                CopyInto(incoming, data);
                return true;

            case Season s:
            {
                var season = existing as Season ?? s;
                if (existing != null) CopyInto(s, season);
                else index[s.Id] = s;
                PlaceSorted(data.Seasons, season, x => x.Number);
                return true;
            }

            case Campaign c:
            {
                if (index.GetValueOrDefault(c.SeasonId) is not Season parent) return false;
                var campaign = existing as Campaign ?? c;
                if (existing != null) CopyInto(c, campaign);
                else index[c.Id] = c;

                if (campaign.Season != parent)
                {
                    campaign.Season?.Campaigns.Remove(campaign);
                    campaign.Season = parent;
                }
                PlaceSorted(parent.Campaigns, campaign, x => x.Number);

                campaign.RosterPage.RefreshAppearances();
                foreach (var ep in campaign.Episodes) ep.RefreshCast();
                return true;
            }

            case Episode e:
            {
                if (index.GetValueOrDefault(e.CampaignId) is not Campaign owner) return false;
                var episode = existing as Episode ?? e;
                if (existing != null) CopyInto(e, episode);
                else index[e.Id] = e;

                if (episode.Campaign != owner)
                {
                    episode.Campaign?.Episodes.Remove(episode);
                    episode.Campaign = owner;
                }
                PlaceSorted(owner.Episodes, episode, x => x.Number);

                episode.Attach();
                episode.RefreshCast();
                owner.RosterPage.RefreshAppearances();
                return true;
            }
        }
        return false;
    }

    private static void Remove(ShowData data, TreeNode node)
    {
        switch (node)
        {
            case Season s: data.Seasons.Remove(s); break;
            case Campaign c: c.Season?.Campaigns.Remove(c); break;
            case Episode e:
                e.Campaign?.Episodes.Remove(e);
                e.Campaign?.RosterPage.RefreshAppearances();
                break;
        }
    }

    /// <summary>Insert or move <paramref name="item"/> so the list stays in number order.</summary>
    private static void PlaceSorted<T>(System.Collections.ObjectModel.ObservableCollection<T> list, T item, Func<T, int> key)
    {
        int from = list.IndexOf(item);
        int to = list.Count(x => !ReferenceEquals(x, item) && key(x) <= key(item));
        if (from < 0) list.Insert(to, item);
        else if (from != to) list.Move(from, to);
    }

    private static Dictionary<Guid, TreeNode> Index(ShowData data)
    {
        var index = new Dictionary<Guid, TreeNode>();
        foreach (var s in data.Seasons)
        {
            index[s.Id] = s;
            foreach (var c in s.Campaigns)
            {
                index[c.Id] = c;
                foreach (var e in c.Episodes) index[e.Id] = e;
            }
        }
        return index;
    }

    // ============================================================
    // WATCHING
    // ============================================================
    /// <summary>Raise <see cref="FilesChanged"/> when anything in the folder changes.</summary>
    public void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            _debounce = new Timer(_ => FilesChanged?.Invoke());
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
            // Not every folder can be watched; the main window also checks regularly.
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

    public EditLock? ReadLock(Guid id)
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

    /// <summary>Mark a page as being edited here (call again now and then to keep it fresh).</summary>
    public void WriteLock(Guid id, DateTime since)
    {
        try
        {
            Directory.CreateDirectory(LocksFolder);
            var mine = new EditLock(UserName, Environment.MachineName, Session, since, DateTime.UtcNow);
            File.WriteAllText(LockPath(id), JsonSerializer.Serialize(mine));
        }
        catch (Exception) { } // a missing lock only means no warning for others
    }

    public void ReleaseLock(Guid id)
    {
        try
        {
            if (ReadLock(id)?.Session == Session) File.Delete(LockPath(id));
        }
        catch (Exception) { }
    }

    // ============================================================
    // PICTURES
    // ============================================================
    /// <summary>Copy a picture into the data folder; returns the path to store.</summary>
    public string ImportImage(string sourcePath)
    {
        Directory.CreateDirectory(ImagesFolder);
        string name = $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        File.Copy(sourcePath, Path.Combine(ImagesFolder, name));
        return $"images/{name}";
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
        using var target = new DataStore(folder, UserName);
        Current = this;
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
        JsonSerializer.Deserialize(text, type, JsonOptions) ?? throw new JsonException("Empty file");

    private static string Serialize(object record)
    {
        var node = JsonSerializer.SerializeToNode(record, record.GetType(), JsonOptions)!.AsObject();
        foreach (var list in ChildLists) node.Remove(list);
        return node.ToJsonString(JsonOptions);
    }

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

    private static readonly HashSet<string> NotCopied = new() { nameof(TreeNode.Id), "Seasons", "Campaigns", "Episodes" };

    /// <summary>Copy a page's saved fields onto the copy already on screen, so the tree and page update in place.</summary>
    private static void CopyInto(object from, object to)
    {
        foreach (var p in to.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0 || NotCopied.Contains(p.Name)) continue;
            if (p.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;

            var value = p.GetValue(from);
            if (p.GetValue(to) is IList list && value is IList items)
            {
                list.Clear();
                foreach (var item in items) list.Add(item);
            }
            else if (!Equals(p.GetValue(to), value))
            {
                p.SetValue(to, value);
            }
        }
    }
}

public class SyncResult
{
    public bool Changed { get; set; }
    public List<TreeNode> Removed { get; } = new();
    public List<string> Unreadable { get; } = new();
}

/// <summary>Who is editing a page. Times are UTC.</summary>
public record EditLock(string User, string Machine, Guid Session, DateTime Since, DateTime Heartbeat)
{
    /// <summary>Held by someone else on another computer, and still fresh (not left behind by a crash).</summary>
    [JsonIgnore]
    public bool IsSomeoneElse =>
        Session != DataStore.Session &&
        !string.Equals(Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
        DateTime.UtcNow - Heartbeat < TimeSpan.FromMinutes(10);
}
