using System.Text.Json;

namespace DmapShared.Api;

// Messages exchanged with the DMAP server (JSON, camelCase). Each page of the
// guide is one record; Data holds the page in the format of GuideRecords.

/// <summary>One page as the server keeps it.</summary>
public sealed class RecordEnvelope
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }

    /// <summary>Goes up by one each time the page is saved; a save names the version it was based on.</summary>
    public long Version { get; set; }

    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public JsonElement Data { get; set; }
}

/// <summary>The whole guide. <see cref="Version"/> counts every change made to the guide.</summary>
public sealed class GuideSnapshot
{
    public long Version { get; set; }
    public List<RecordEnvelope> Records { get; set; } = [];
}

/// <summary>What changed after a given guide version.</summary>
public sealed class GuideChanges
{
    public long Version { get; set; }
    public List<RecordEnvelope> Records { get; set; } = [];
    public List<DeletedRecord> Deleted { get; set; } = [];
}

public sealed class DeletedRecord
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
}

/// <summary>Save a page. BaseVersion is the version it was edited from; 0 creates a new page.</summary>
public sealed class RecordWrite
{
    public long BaseVersion { get; set; }
    public string? ParentId { get; set; }
    public JsonElement Data { get; set; }
}

/// <summary>Returned with 409 when someone else saved the page first.</summary>
public sealed class ConflictResponse
{
    public string Error { get; set; } = "";
    public RecordEnvelope? Current { get; set; }
}

public sealed class ErrorResponse
{
    public string Error { get; set; } = "";
}

public sealed class ServerInfo
{
    public string Status { get; set; } = "";
    public string Role { get; set; } = "";
    public long Version { get; set; }
}

public sealed class EditLockMessage
{
    public string User { get; set; } = "";
    public string Machine { get; set; } = "";
    public Guid Session { get; set; }
    public DateTime Since { get; set; }
    public DateTime Heartbeat { get; set; }
}

public sealed class ImageUploadResult
{
    public string Path { get; set; } = "";
}

public static class ApiRoles
{
    public const string Viewer = "viewer";
    public const string Editor = "editor";
    public const string Admin = "admin";

    public static bool IsKnown(string? role) => role?.Trim().ToLowerInvariant() is Viewer or Editor or Admin;

    public static bool CanEdit(string? role) => role?.Trim().ToLowerInvariant() is Editor or Admin;
}

public static class ApiHeaders
{
    public const string ApiKey = "X-Api-Key";

    /// <summary>The name shown on pages this person saves (URL-escaped so any name fits in a header).</summary>
    public const string UserName = "X-User-Name";
}
