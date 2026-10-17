using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DmapShared.ViewModels;

/// <summary>The Settings page: your name, and where the guide lives (a shared server, or a folder on the desktop).</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly GuideSettings _settings;

    public MainViewModel Main { get; }

    /// <summary>The desktop can also keep the guide in a folder; the phone always uses the server.</summary>
    public bool CanUseFolder => Main.Platform.SupportsFolders;

    [ObservableProperty] private string _userName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseFolder))]
    private bool _useServer;

    [ObservableProperty] private string _serverAddress;
    [ObservableProperty] private string _accessKey;
    [ObservableProperty] private string _problem = "";
    [ObservableProperty] private bool _isWorking;

    public string DataFolder => _settings.DataFolder;

    public bool UseFolder
    {
        get => !UseServer;
        set => UseServer = !value;
    }

    public SettingsViewModel(MainViewModel main, GuideSettings settings)
    {
        Main = main;
        _settings = settings;
        _userName = settings.UserName;
        _useServer = settings.UseServer || !main.Platform.SupportsFolders;
        _serverAddress = settings.ServerAddress;
        _accessKey = settings.AccessKey;
    }

    private GuideSettings Edited()
    {
        var next = _settings.Clone();
        next.UserName = UserName ?? "";
        next.UseServer = UseServer || !CanUseFolder;
        next.ServerAddress = ServerAddress ?? "";
        next.AccessKey = AccessKey ?? "";
        return next;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        Problem = "";
        IsWorking = true;
        try
        {
            Problem = await Main.ApplySettingsAsync(Edited()) ?? "";
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        Problem = "";
        IsWorking = true;
        try
        {
            Problem = await Main.ChooseFolderAsync(Edited()) ?? "";
        }
        finally
        {
            IsWorking = false;
        }
    }
}
