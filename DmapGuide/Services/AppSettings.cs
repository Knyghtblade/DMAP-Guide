using System.IO;
using System.Text.Json;

namespace DmapGuide.Services;

/// <summary>Per-user settings, stored in %APPDATA%\DMAP Guide\settings.json.</summary>
public class AppSettings
{
    public string DataFolder { get; set; } = DefaultDataFolder;

    /// <summary>The name other people see on pages you edit.</summary>
    public string UserName { get; set; } = Environment.UserName;

    public static string DefaultDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DMAP Guide");

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
