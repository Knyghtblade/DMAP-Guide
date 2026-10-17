using DmapShared.Storage;
using DmapShared.ViewModels;
using System.IO;
using System.Text.Json;

namespace DmapGuide.Services;

/// <summary>Per-user settings, stored in %APPDATA%\DMAP Guide\settings.json.</summary>
public class AppSettings : ISettingsStore
{
    /// <summary>The file's layout. ApiBaseUrl / ApiKey keep their earlier names so existing files (and test packages) still work.</summary>
    private sealed class SettingsFile
    {
        public string? DataFolder { get; set; }
        public bool? UseServer { get; set; }
        public string? ApiBaseUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? UserName { get; set; }
    }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMAP Guide", "settings.json");

    public async Task<GuideSettings> LoadAsync()
    {
        SettingsFile? file = null;
        try
        {
            if (File.Exists(SettingsPath))
                file = JsonSerializer.Deserialize<SettingsFile>(await File.ReadAllTextAsync(SettingsPath));
        }
        catch (Exception) { }

        var settings = new GuideSettings
        {
            DataFolder = file?.DataFolder is { Length: > 0 } folder ? folder : DefaultDataFolder,
            ServerAddress = file?.ApiBaseUrl ?? "",
            AccessKey = file?.ApiKey ?? "",
            UseServer = file?.UseServer ?? !string.IsNullOrWhiteSpace(file?.ApiBaseUrl),
            UserName = file?.UserName is { Length: > 0 } name ? name : Environment.UserName
        };

        // Remember the folder chosen on first run, so it doesn't change later (e.g. when OneDrive is set up).
        if (file?.DataFolder is not { Length: > 0 })
        {
            try { await SaveAsync(settings); } catch (Exception) { }
        }
        return settings;
    }

    public async Task SaveAsync(GuideSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var file = new SettingsFile
        {
            DataFolder = settings.DataFolder,
            UseServer = settings.UseServer,
            ApiBaseUrl = settings.ServerAddress,
            ApiKey = settings.AccessKey,
            UserName = settings.UserName
        };
        await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// A guide already in Documents\DMAP Guide (where earlier versions kept it) stays there;
    /// otherwise a new guide goes in OneDrive if there is one, so it can be shared.
    /// </summary>
    public static string DefaultDataFolder
    {
        get
        {
            string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DMAP Guide");
            if (FolderGuideStore.HasData(documents))
                return documents;

            string? oneDrive = TryGetOneDriveRoot();
            if (!string.IsNullOrWhiteSpace(oneDrive))
                return Path.Combine(oneDrive, "DMAP Guide");

            return documents;
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
}
