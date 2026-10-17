using DmapShared.Api;
using DmapShared.Models;
using System.Text.Json;

namespace DmapShared.Storage;

/// <summary>
/// Keeps the guide on the shared DMAP server. Works like <see cref="FolderGuideStore"/>:
/// each page is one record on the server, and each record has a version that goes up
/// whenever someone saves it. A save names the version it was edited from, so the
/// server notices when someone else saved the page first and the editor is asked
/// whose version to keep. Other people's saves are fetched every so often.
/// </summary>
public sealed class ServerGuideStore : IGuideStore
{
    private readonly DmapApiClient _client;
    private readonly string _userName;
    private readonly string _imageCache;

    /// <summary>Each page as last read or written here (id → JSON as this app writes it).</summary>
    private readonly Dictionary<string, string> _known = new();

    /// <summary>The server version each page was last read or written at.</summary>
    private readonly Dictionary<string, long> _versions = new();

    private readonly Dictionary<string, string> _kinds = new();

    /// <summary>Other people's saves not shown yet: the page has unsaved edits here, or its season/campaign hasn't arrived.</summary>
    private readonly Dictionary<string, RecordEnvelope> _waiting = new();

    /// <summary>The guide version last caught up with.</summary>
    private long _guideVersion;

    private readonly HashSet<string> _downloading = new();
    private readonly object _downloadLock = new();

    public string Location => _client.BaseAddress.ToString();

    public event Action? ChangesWaiting { add { } remove { } }
    public event Action? ImagesArrived;

    /// <param name="imageCacheFolder">Where downloaded pictures are kept on this device.</param>
    public ServerGuideStore(DmapApiClient client, string userName, string imageCacheFolder)
    {
        _client = client;
        _userName = userName;
        _imageCache = imageCacheFolder;
    }

    // ============================================================
    // LOAD
    // ============================================================
    public async Task<GuideLoadResult> LoadAsync()
    {
        var info = await _client.GetInfoAsync();
        if (string.IsNullOrEmpty(info.Role))
            throw new DmapApiException(null,
                "The guide server is running an older version. Restart it from the updated DMAP Guide project, then try again.");

        var snapshot = await _client.GetGuideAsync();
        _guideVersion = snapshot.Version;

        if (snapshot.Records.Count == 0)
        {
            // A brand-new server: start it off with the same sample guide a new folder gets.
            var seed = ShowData.CreateSeed();
            await SaveAsync(seed, (_, _) => Task.FromResult(true));
            return new GuideLoadResult(seed, new());
        }

        var data = new ShowData();
        var result = new SyncResult();
        foreach (var record in snapshot.Records) _waiting[record.Id] = record;
        PlaceWaiting(data, result);
        return new GuideLoadResult(data, result.Unreadable);
    }

    // ============================================================
    // SAVE
    // ============================================================
    public async Task<int> SaveAsync(ShowData data, Func<object, string, Task<bool>> keepMine)
    {
        var records = GuideRecords.All(data).ToList();
        int written = 0;

        foreach (var (kind, id, parentId, record) in records)
        {
            string json = GuideRecords.Serialize(record);
            _known.TryGetValue(id, out var known);
            if (json == known) continue;

            long baseVersion = _versions.GetValueOrDefault(id);
            while (true)
            {
                var outcome = await _client.PutRecordAsync(kind, id, parentId, baseVersion, json);

                if (outcome.IsGone)
                {
                    if (baseVersion == 0)
                        throw new DmapApiException(System.Net.HttpStatusCode.NotFound,
                            "The server didn't accept the page. Check the server address points at an up-to-date DMAP guide server.");

                    // Someone deleted it while it was being edited here; saving puts it back.
                    baseVersion = 0;
                    continue;
                }

                var envelope = outcome.Record!;
                if (outcome.IsConflict)
                {
                    // Someone else saved this page since it was read here.
                    var theirs = GuideRecords.Parse(envelope.Kind, envelope.Data.GetRawText());
                    string who = envelope.UpdatedBy is { Length: > 0 } name ? name : "Someone else";
                    if (await keepMine(record, who))
                    {
                        baseVersion = envelope.Version;
                        continue;
                    }

                    GuideTree.CopyInto(theirs, record);
                    Remember(envelope, GuideRecords.Serialize(record));
                    _waiting.Remove(id);
                    break;
                }

                if (record is TreeNode node)
                {
                    node.LastEditedBy = envelope.UpdatedBy;
                    node.LastEdited = envelope.UpdatedAt;
                }
                Remember(envelope, GuideRecords.Serialize(record));
                _waiting.Remove(id);
                written++;
                break;
            }
        }

        // Pages deleted here: episodes first, so nothing is left without its campaign.
        var current = records.Select(r => r.Id).ToHashSet();
        foreach (var id in _known.Keys.Where(k => !current.Contains(k))
                     .OrderByDescending(k => GuideRecords.Rank(_kinds[k])).ToList())
        {
            await _client.DeleteRecordAsync(_kinds[id], id);
            Forget(id);
        }

        return written;
    }

    // ============================================================
    // SYNC: pick up pages other people saved
    // ============================================================
    public async Task<SyncResult> SyncAsync(ShowData data)
    {
        var result = new SyncResult();
        var changes = await _client.GetChangesAsync(_guideVersion);

        foreach (var record in changes.Records)
            if (!_waiting.TryGetValue(record.Id, out var older) || older.Version <= record.Version)
                _waiting[record.Id] = record;

        // Pages someone else deleted.
        var index = GuideTree.Index(data);
        foreach (var gone in changes.Deleted)
        {
            _waiting.Remove(gone.Id);
            if (!Guid.TryParse(gone.Id, out var guid) || !index.TryGetValue(guid, out var node))
            {
                Forget(gone.Id);
                continue;
            }

            if (_known.TryGetValue(gone.Id, out var known) && GuideRecords.Serialize(node) != known)
            {
                // Being edited here; saving puts it back.
                _versions[gone.Id] = 0;
                continue;
            }

            GuideTree.Remove(data, node);
            result.Removed.Add(node);
            result.Changed = true;
            Forget(gone.Id);
        }

        PlaceWaiting(data, result);
        _guideVersion = changes.Version;
        return result;
    }

    /// <summary>Show waiting pages that can be shown now, parents before children.</summary>
    private void PlaceWaiting(ShowData data, SyncResult result)
    {
        var index = GuideTree.Index(data);
        foreach (var envelope in _waiting.Values.OrderBy(r => GuideRecords.Rank(r.Kind)).ToList())
        {
            if (!GuideRecords.IsKnownKind(envelope.Kind))
            {
                _waiting.Remove(envelope.Id);
                continue;
            }

            object incoming;
            string json;
            try
            {
                incoming = GuideRecords.Parse(envelope.Kind, envelope.Data.GetRawText());
                json = GuideRecords.Serialize(incoming);
            }
            catch (JsonException)
            {
                result.Unreadable.Add($"{envelope.Kind} {envelope.Id}");
                _waiting.Remove(envelope.Id);
                continue;
            }

            _known.TryGetValue(envelope.Id, out var known);
            if (json == known)
            {
                Remember(envelope, json);
                _waiting.Remove(envelope.Id);
                continue;
            }

            object? existing = incoming switch
            {
                ShowData => data,
                TreeNode n => index.GetValueOrDefault(n.Id),
                _ => null
            };

            // Unsaved edits here: leave them; Save asks whose version to keep.
            if (existing != null && known != null && GuideRecords.Serialize(existing) != known) continue;

            // Its season / campaign hasn't arrived yet; try again next time.
            if (!GuideTree.Place(data, incoming, existing, index)) continue;

            Remember(envelope, json);
            _waiting.Remove(envelope.Id);
            result.Changed = true;
        }
    }

    private void Remember(RecordEnvelope envelope, string json)
    {
        _known[envelope.Id] = json;
        _versions[envelope.Id] = envelope.Version;
        _kinds[envelope.Id] = envelope.Kind;
    }

    private void Forget(string id)
    {
        _known.Remove(id);
        _versions.Remove(id);
        _kinds.Remove(id);
    }

    public void StartWatching() { }

    // ============================================================
    // EDIT LOCKS: "someone is editing this page"
    // ============================================================
    public async Task<EditLock?> ReadLockAsync(Guid id)
    {
        try
        {
            var m = await _client.GetLockAsync(GuideRecords.IdText(id));
            return m == null ? null : new EditLock(m.User, m.Machine, m.Session, m.Since, m.Heartbeat);
        }
        catch (DmapApiException)
        {
            return null; // no warning is better than no editing
        }
    }

    public async Task WriteLockAsync(Guid id, DateTime since)
    {
        try
        {
            await _client.PutLockAsync(GuideRecords.IdText(id), new EditLockMessage
            {
                User = _userName,
                Machine = GuideSession.MachineName,
                Session = GuideSession.Id,
                Since = since,
                Heartbeat = DateTime.UtcNow
            });
        }
        catch (DmapApiException) { } // a missing lock only means no warning for others
    }

    public async Task ReleaseLockAsync(Guid id)
    {
        try
        {
            await _client.DeleteLockAsync(GuideRecords.IdText(id), GuideSession.Id);
        }
        catch (DmapApiException) { }
    }

    // ============================================================
    // PICTURES: kept on the server, cached on this device
    // ============================================================
    public async Task<string> ImportImageAsync(string sourceFile)
    {
        string stored;
        await using (var stream = File.OpenRead(sourceFile))
            stored = await _client.UploadImageAsync(stream, Path.GetExtension(sourceFile));

        Directory.CreateDirectory(_imageCache);
        File.Copy(sourceFile, CachePath(stored), overwrite: true);
        return stored;
    }

    public string? ResolveImage(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (Path.IsPathRooted(stored)) return File.Exists(stored) ? stored : null;

        string cached = CachePath(stored);
        if (File.Exists(cached)) return cached;

        string name = Path.GetFileName(stored);
        lock (_downloadLock)
        {
            if (!_downloading.Add(name)) return null;
        }
        _ = DownloadAsync(name, cached);
        return null;
    }

    private async Task DownloadAsync(string name, string cached)
    {
        try
        {
            Directory.CreateDirectory(_imageCache);
            if (await _client.DownloadImageAsync(name, cached).ConfigureAwait(false))
                ImagesArrived?.Invoke();
        }
        catch (Exception)
        {
            lock (_downloadLock) _downloading.Remove(name); // try again next time it's shown
        }
    }

    private string CachePath(string stored) => Path.Combine(_imageCache, Path.GetFileName(stored));

    public void Dispose() => _client.Dispose();
}
