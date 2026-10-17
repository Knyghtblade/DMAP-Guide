using DmapShared.Models;
using System.Text.Json.Serialization;

namespace DmapShared.Storage;

/// <summary>
/// Where the guide is kept: a shared folder (<see cref="FolderGuideStore"/>) or the
/// shared server (<see cref="ServerGuideStore"/>). Both work the same way: one record
/// per page, only changed pages are written, someone else's newer save of a page is
/// noticed and the editor is asked whose version to keep, and other people's saves
/// are picked up while the guide is open.
/// </summary>
public interface IGuideStore : IDisposable
{
    /// <summary>Shown to the user: the folder path or the server address.</summary>
    string Location { get; }

    /// <summary>Raised (on any thread) when other people's changes may be waiting; call <see cref="SyncAsync"/>.</summary>
    event Action? ChangesWaiting;

    /// <summary>Raised (on any thread) when pictures finished downloading and can be shown.</summary>
    event Action? ImagesArrived;

    Task<GuideLoadResult> LoadAsync();

    /// <summary>
    /// Write the pages that changed since they were last read. If someone else saved
    /// one of them in the meantime, <paramref name="keepMine"/> (page, their name) decides
    /// whose version wins; false takes theirs, replacing the edits made here.
    /// Returns how many pages were written.
    /// </summary>
    Task<int> SaveAsync(ShowData data, Func<object, string, Task<bool>> keepMine);

    /// <summary>
    /// Bring <paramref name="data"/> up to date with other people's saves. Pages with
    /// unsaved edits here are left alone; <see cref="SaveAsync"/> sorts those out.
    /// </summary>
    Task<SyncResult> SyncAsync(ShowData data);

    void StartWatching();

    Task<EditLock?> ReadLockAsync(Guid id);

    /// <summary>Mark a page as being edited here (call again now and then to keep it fresh).</summary>
    Task WriteLockAsync(Guid id, DateTime since);

    Task ReleaseLockAsync(Guid id);

    /// <summary>Add a picture to the guide; returns the path to store on the page.</summary>
    Task<string> ImportImageAsync(string sourceFile);

    /// <summary>A picture's local file, or null if it's missing (or still downloading; see <see cref="ImagesArrived"/>).</summary>
    string? ResolveImage(string? stored);
}

public record GuideLoadResult(ShowData Data, List<string> Unreadable);

public class SyncResult
{
    public bool Changed { get; set; }
    public List<TreeNode> Removed { get; } = new();
    public List<string> Unreadable { get; } = new();
}

/// <summary>This run of the app, as other people see it in "someone is editing" warnings.</summary>
public static class GuideSession
{
    public static readonly Guid Id = Guid.NewGuid();

    /// <summary>Set at startup (e.g. the phone's name on Android, where the machine name isn't meaningful).</summary>
    public static string MachineName { get; set; } = Environment.MachineName;
}

/// <summary>Who is editing a page. Times are UTC.</summary>
public record EditLock(string User, string Machine, Guid Session, DateTime Since, DateTime Heartbeat)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Held by someone else, and still fresh (not left behind by a crash). A lock left by
    /// this same person on this same device (an earlier run that closed badly) doesn't count.
    /// </summary>
    public bool IsSomeoneElse(string myUserName) =>
        Session != GuideSession.Id &&
        !(string.Equals(Machine, GuideSession.MachineName, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(User, myUserName, StringComparison.OrdinalIgnoreCase)) &&
        DateTime.UtcNow - Heartbeat < StaleAfter;
}

/// <summary>Turns a stored picture path into a file to show; points at the guide that's open.</summary>
public static class GuideImages
{
    public static IGuideStore? Store { get; set; }

    public static string? Resolve(string? stored) => Store?.ResolveImage(stored);

    /// <summary>Raised on the UI thread when downloaded pictures can now be shown.</summary>
    public static event Action? Arrived;

    public static void RaiseArrived() => Arrived?.Invoke();
}
