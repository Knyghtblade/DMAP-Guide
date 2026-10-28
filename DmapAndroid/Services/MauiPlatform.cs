using DmapShared.ViewModels;

namespace DmapAndroid.Services;

/// <summary>Dialogs, pickers and timers supplied by the MAUI app.</summary>
public sealed class MauiPlatform : IPlatformServices
{
#if MACCATALYST
    public bool SupportsFolders => true;
    public bool SupportsServer => false;
#else
    public bool SupportsFolders => false;
    public bool SupportsServer => true;
#endif

    public string CacheFolder => FileSystem.CacheDirectory;

    public string AppVersion => AppInfo.Current.VersionString;

    public void OpenLink(string url) => _ = Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Uri(url));

    private static Page Page => Application.Current?.Windows.FirstOrDefault()?.Page
                                ?? throw new InvalidOperationException("The app has no window yet.");

    public Task AlertAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(() => Page.DisplayAlertAsync(title, message, "OK"));

    public Task<bool> AskAsync(string title, string message, string yes, string no) =>
        MainThread.InvokeOnMainThreadAsync(() => Page.DisplayAlertAsync(title, message, yes, no));

    public async Task<string?> PickImageAsync()
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose a picture",
            FileTypes = FilePickerFileType.Images
        });
        if (picked == null) return null;

        // Copy it somewhere this app can always read (the picker's file may be temporary).
        string extension = Path.GetExtension(picked.FileName);
        if (string.IsNullOrEmpty(extension)) extension = ".jpg";
        string copy = Path.Combine(FileSystem.CacheDirectory, $"picked-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        await using (var source = await picked.OpenReadAsync())
        await using (var target = File.Create(copy))
            await source.CopyToAsync(target);
        return copy;
    }

    public async Task<string?> PickFolderAsync(string title, string? startIn)
    {
#if MACCATALYST
        var picker = CommunityToolkit.Maui.Storage.FolderPicker.Default;
        var result = string.IsNullOrWhiteSpace(startIn)
            ? await picker.PickAsync(CancellationToken.None)
            : await picker.PickAsync(startIn, CancellationToken.None);
        // Not successful: cancelled, or the folder couldn't be opened. Either way nothing changes.
        return result.IsSuccessful ? result.Folder?.Path : null;
#else
        return null;
#endif
    }

    public void BeginInvoke(Action action) => MainThread.BeginInvokeOnMainThread(action);

    public void StartTimer(TimeSpan interval, Action tick) =>
        Application.Current!.Dispatcher.StartTimer(interval, () =>
        {
            tick();
            return true;
        });
}

/// <summary>Per-device settings. Mac Catalyst keeps the guide folder path in preferences.</summary>
public sealed class MauiSettings : ISettingsStore
{
    private const string UserNameKey = "dmap-user-name";
    private const string AddressKey = "dmap-api-address";
    private const string AccessKey = "dmap-api-key";
    private const string DataFolderKey = "dmap-data-folder";

    public async Task<GuideSettings> LoadAsync()
    {
        string key;
        string address = Preferences.Default.Get(AddressKey, "");
        bool emulator = DeviceInfo.Current.DeviceType == DeviceType.Virtual;
        try
        {
            key = await SecureStorage.Default.GetAsync(AccessKey) ?? "";
        }
        catch (Exception)
        {
            key = ""; // secure storage can be reset by the system; ask for the key again
        }

        if (emulator && address.Length == 0)
        {
            // The emulator reaches this computer's local test server at 10.0.2.2.
            address = "http://10.0.2.2:5080/";
            if (key.Length == 0) key = "local-dev-key";
        }

        return new GuideSettings
        {
#if MACCATALYST
            UseServer = false,
#else
            UseServer = true,
#endif
            UserName = Preferences.Default.Get(UserNameKey, DeviceInfo.Current.Name ?? ""),
            ServerAddress = address,
            AccessKey = key,
            DataFolder = Preferences.Default.Get(DataFolderKey, "")
        };
    }

    public async Task SaveAsync(GuideSettings settings)
    {
        Preferences.Default.Set(UserNameKey, settings.UserName);
        Preferences.Default.Set(AddressKey, settings.ServerAddress);
        Preferences.Default.Set(DataFolderKey, settings.DataFolder);
        await SecureStorage.Default.SetAsync(AccessKey, settings.AccessKey);
    }
}
