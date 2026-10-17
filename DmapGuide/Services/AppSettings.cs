using System.IO;
using System.Text.Json;

namespace DmapGuide.Services;

/// <summary>Per-user settings, stored in %APPDATA%\DMAP Guide\settings.json.</summary>
public class AppSettings
{
    public string DataFolder { get; set; } = DefaultDataFolder;

    /// <summary>Optional API base URL. When set, the app tries the remote API before the local guide folder.</summary>
    public string ApiBaseUrl { get; set; } = "";

    /// <summary>Optional API key for local test endpoints.</summary>
    public string ApiKey { get; set; } = "local-dev-key";

    /// <summary>Optional API role used for local testing: viewer, editor, or admin.</summary>
    public string ApiRole { get; set; } = "editor";

    /// <summary>The name other people see on pages you edit.</summary>
    public string UserName { get; set; } = Environment.UserName;

    public static string DefaultDataFolder
    {
        get
        {
            string? oneDrive = TryGetOneDriveRoot();
            if (!string.IsNullOrWhiteSpace(oneDrive))
                return Path.Combine(oneDrive, "DMAP Guide");

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DMAP Guide");
        }
    }

    private static string? TryGetOneDriveRoot()
    {
        foreach (string envVar in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            string? value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                return value;
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string candidate in new[]
                 {
                     Path.Combine(profile, "OneDrive"),
                     Path.Combine(profile, "OneDrive - Personal"),
                     Path.Combine(profile, "OneDrive - Microsoft")
                 })
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMAP Guide", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
