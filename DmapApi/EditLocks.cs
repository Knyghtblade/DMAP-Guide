using DmapShared.Api;
using DmapShared.Storage;
using System.Collections.Concurrent;

namespace DmapApi;

/// <summary>"Someone is editing this page" markers. Kept in memory: they only matter while people are editing.</summary>
public sealed class EditLocks
{
    private readonly ConcurrentDictionary<string, EditLockMessage> _locks = new();

    public EditLockMessage? Get(string id)
    {
        if (!_locks.TryGetValue(id, out var held)) return null;
        if (DateTime.UtcNow - held.Heartbeat < EditLock.StaleAfter) return held;
        _locks.TryRemove(new KeyValuePair<string, EditLockMessage>(id, held));
        return null;
    }

    public void Set(string id, EditLockMessage message)
    {
        message.Heartbeat = DateTime.UtcNow;
        _locks[id] = message;
    }

    /// <summary>Only the session that holds a lock can release it.</summary>
    public void Release(string id, Guid session)
    {
        if (_locks.TryGetValue(id, out var held) && held.Session == session)
            _locks.TryRemove(new KeyValuePair<string, EditLockMessage>(id, held));
    }
}
