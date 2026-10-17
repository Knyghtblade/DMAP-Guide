namespace DmapShared.ViewModels;

/// <summary>What the desktop and phone apps each provide in their own way.</summary>
public interface IPlatformServices
{
    /// <summary>False on the phone: it can only open the shared server, not a folder.</summary>
    bool SupportsFolders { get; }

    Task AlertAsync(string title, string message);

    /// <summary>Ask a two-way question; true for <paramref name="yes"/>.</summary>
    Task<bool> AskAsync(string title, string message, string yes, string no);

    /// <summary>Let the user choose a picture; returns a file this app can read, or null.</summary>
    Task<string?> PickImageAsync();

    /// <summary>Let the user choose a folder; null if cancelled (or not supported).</summary>
    Task<string?> PickFolderAsync(string title, string? startIn);

    /// <summary>Run <paramref name="action"/> on the UI thread.</summary>
    void BeginInvoke(Action action);

    /// <summary>Call <paramref name="tick"/> on the UI thread every <paramref name="interval"/>.</summary>
    void StartTimer(TimeSpan interval, Action tick);

    /// <summary>A folder on this device for downloaded pictures.</summary>
    string CacheFolder { get; }
}

/// <summary>Where the guide lives and who's using it. Saved per user, per device.</summary>
public class GuideSettings
{
    /// <summary>The name other people see on pages you edit.</summary>
    public string UserName { get; set; } = "";

    /// <summary>True: open the shared server. False: open <see cref="DataFolder"/>.</summary>
    public bool UseServer { get; set; }

    public string DataFolder { get; set; } = "";
    public string ServerAddress { get; set; } = "";
    public string AccessKey { get; set; } = "";

    public GuideSettings Clone() => (GuideSettings)MemberwiseClone();
}

public interface ISettingsStore
{
    Task<GuideSettings> LoadAsync();
    Task SaveAsync(GuideSettings settings);
}
