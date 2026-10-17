using DmapShared.Api;

namespace DmapApi;

/// <summary>Where the server keeps its data and which access keys it accepts.</summary>
public sealed class ServerOptions
{
    /// <summary>Holds dmap.db and the images folder.</summary>
    public required string DataFolder { get; init; }

    public required IReadOnlyList<(string Key, string Role)> Keys { get; init; }

    public string DatabasePath => Path.Combine(DataFolder, "dmap.db");
    public string ImagesFolder => Path.Combine(DataFolder, "images");

    public const string DevelopmentKey = "local-dev-key";

    /// <summary>
    /// Keys come from Dmap:ApiKeys (each with Key and Role: viewer, editor or admin);
    /// the role belongs to the key, so nobody can choose their own. Only when running
    /// in Development with no keys configured, Dmap:ApiKey (default "local-dev-key")
    /// is accepted as an editor key for local testing.
    /// </summary>
    public static ServerOptions From(IConfiguration config, IHostEnvironment environment)
    {
        var keys = config.GetSection("Dmap:ApiKeys").GetChildren()
            .Select(section => (Key: section["Key"]?.Trim() ?? "", Role: section["Role"]?.Trim().ToLowerInvariant() ?? ""))
            .Where(entry => entry.Key.Length > 0)
            .ToList();

        if (keys.Any(entry => !ApiRoles.IsKnown(entry.Role)))
            throw new InvalidOperationException("Each Dmap:ApiKeys entry needs a Role of viewer, editor or admin.");
        if (keys.Any(entry => entry.Key.Length < 16))
            throw new InvalidOperationException("Each Dmap:ApiKeys entry needs a Key of at least 16 characters.");

        if (keys.Count == 0)
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "Configure Dmap:ApiKeys (each with Key and Role) before running outside Development.");
            keys.Add((config["Dmap:ApiKey"] is { Length: > 0 } devKey ? devKey : DevelopmentKey, ApiRoles.Editor));
        }

        string folder = config["Dmap:DataFolder"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMAP Guide Server");

        return new ServerOptions { DataFolder = Path.GetFullPath(folder), Keys = keys };
    }
}
