using DmapAndroid.Controls;
using DmapAndroid.Services;
using DmapShared.Storage;
using DmapShared.ViewModels;

namespace DmapAndroid;

public partial class App : Application
{
    private readonly MainViewModel _vm;
    private bool _started;

    public App()
    {
        InitializeComponent();
        GuideSession.MachineName = DeviceInfo.Current.Name ?? "Phone";
        _vm = new MainViewModel(new MauiPlatform(), new PhoneSettings());
        AppState.Main = _vm;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new TomePage(_vm));

        // Open the guide behind the cover as soon as the app is showing.
        window.Activated += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await _vm.StartAsync();
        };

        // Leaving the app: save any edits, as closing the desktop window does.
        window.Stopped += async (_, _) => await _vm.SaveAsync();
        window.Resumed += async (_, _) => await _vm.SyncNowAsync();
        return window;
    }
}
