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

    /// <summary>
    /// The guide that ships beside the app: a "guide" folder next to the .exe (the shared
    /// OneDrive folder layout). When it exists it's always the guide that opens, so nobody
    /// has to set anything up.
    /// </summary>
    public static string? GuideFolderBesideApp =>
        Directory.Exists(ExpectedGuideFolderBesideApp) ? ExpectedGuideFolderBesideApp : null;

    /// <summary>Where the "guide" folder beside the app should be, whether or not it's there.</summary>
    public static string ExpectedGuideFolderBesideApp => Path.Combine(AppContext.BaseDirectory, "guide");

    /// <summary>
    /// True for the app as shipped in the shared folder (built as one file by "Publish Shared
    /// App"). A single-file app has no separate .dll on disk, so its location is empty. The
    /// build run from the code folder (Run DMAP Guide.cmd) is not one file, and still uses the
    /// folder in Settings when there's no guide beside it.
    /// </summary>
#pragma warning disable IL3000 // the empty location is exactly what's being checked for
    public static bool IsPublishedAsOneFile => string.IsNullOrEmpty(typeof(AppSettings).Assembly.Location);
#pragma warning restore IL3000

    /// <summary>
    /// Why the shared app can't be used from <paramref name="guideFolder"/>: it isn't inside a
    /// folder that OneDrive keeps in sync on this PC, so nothing written there reaches anyone
    /// else. That's what a folder downloaded (or unzipped) from the OneDrive website looks like.
    /// Null when the folder is synced.
    /// </summary>
    public static string? NotSyncedProblem(string guideFolder)
    {
        var roots = OneDriveRoots();
        string full = Path.GetFullPath(guideFolder).TrimEnd(Path.DirectorySeparatorChar);
        if (roots.Any(root => full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            return null;

        string appFolder = Path.GetDirectoryName(full) ?? full;
        string where = roots.Count == 0
            ? "OneDrive isn't signed in on this PC, so no folder here is kept in sync."
            : "OneDrive on this PC keeps these folders in sync:\n" + string.Join("\n", roots);
        return "The folder this app was opened from isn't one that OneDrive keeps in sync on this PC. " +
               "Changes made here would stay on this PC, and nobody else's changes would ever arrive.\n\n" +
               "This usually means the shared folder was downloaded from the OneDrive website (or unzipped) instead of synced. " +
               "Delete this copy. Then, on the OneDrive website, open \"Shared\", find the \"DMAP Guide\" folder and choose " +
               "\"Add shortcut to My files\". Open File Explorer, click OneDrive on the left, open the \"DMAP Guide\" folder " +
               "that appears there, and double-click \"DMAP Guide.exe\" in it.\n\n" +
               $"The app was opened from:\n{appFolder}\n\n{where}";
    }

    /// <summary>Every folder OneDrive syncs on this PC (work and personal accounts).</summary>
    private static List<string> OneDriveRoots()
    {
        var roots = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
                !roots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
                roots.Add(Path.GetFullPath(path));
        }

        foreach (string envVar in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            Add(Environment.GetEnvironmentVariable(envVar));

        try
        {
            using var accounts = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            foreach (string name in accounts?.GetSubKeyNames() ?? [])
            {
                using var account = accounts!.OpenSubKey(name);
                Add(account?.GetValue("UserFolder") as string);
            }
        }
        catch (Exception) { }

        return roots;
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
