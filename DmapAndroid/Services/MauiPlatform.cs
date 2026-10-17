using DmapShared.ViewModels;

namespace DmapAndroid.Services;

/// <summary>The phone's dialogs, pickers and timers for the shared view model.</summary>
public sealed class MauiPlatform : IPlatformServices
{
    public bool SupportsFolders => false;

    public string CacheFolder => FileSystem.CacheDirectory;

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

    public Task<string?> PickFolderAsync(string title, string? startIn) => Task.FromResult<string?>(null);

    public void BeginInvoke(Action action) => MainThread.BeginInvokeOnMainThread(action);

    public void StartTimer(TimeSpan interval, Action tick) =>
        Application.Current!.Dispatcher.StartTimer(interval, () =>
        {
            tick();
            return true;
        });
}

/// <summary>Settings on the phone: the access key in secure storage, the rest in preferences.</summary>
public sealed class PhoneSettings : ISettingsStore
{
    private const string UserNameKey = "dmap-user-name";
    private const string AddressKey = "dmap-api-address";
    private const string AccessKey = "dmap-api-key";

    public async Task<GuideSettings> LoadAsync()
    {
        bool emulator = DeviceInfo.Current.DeviceType == DeviceType.Virtual;
        string key;
        try
        {
            key = await SecureStorage.Default.GetAsync(AccessKey) ?? "";
        }
        catch (Exception)
        {
            key = ""; // secure storage can be reset by the system; ask for the key again
        }

        string address = Preferences.Default.Get(AddressKey, "");
        if (emulator && address.Length == 0)
        {
            // The emulator reaches this computer's local test server at 10.0.2.2.
            address = "http://10.0.2.2:5080/";
            if (key.Length == 0) key = "local-dev-key";
        }

        return new GuideSettings
        {
            UseServer = true,
            UserName = Preferences.Default.Get(UserNameKey, DeviceInfo.Current.Name ?? ""),
            ServerAddress = address,
            AccessKey = key
        };
    }

    public async Task SaveAsync(GuideSettings settings)
    {
        Preferences.Default.Set(UserNameKey, settings.UserName);
        Preferences.Default.Set(AddressKey, settings.ServerAddress);
        await SecureStorage.Default.SetAsync(AccessKey, settings.AccessKey);
    }
}
