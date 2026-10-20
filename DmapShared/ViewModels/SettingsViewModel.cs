using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DmapShared.ViewModels;

/// <summary>The Settings page: your name, and where the guide lives (a shared server, or a folder on the desktop).</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly GuideSettings _settings;

    public MainViewModel Main { get; }

    /// <summary>"DMAP Guide version 1.0.4", so people can check they're on the same build.</summary>
    public string VersionText => $"DMAP Guide version {Main.AppVersion}";

    /// <summary>The desktop keeps the guide in a folder; the phone uses the server.</summary>
    public bool CanUseFolder => Main.Platform.SupportsFolders;
    public bool CanUseServer => Main.Platform.SupportsServer;

    /// <summary>Only an app that can do both gets the choice.</summary>
    public bool CanChooseWhere => CanUseFolder && CanUseServer;

    /// <summary>The guide was opened from the folder beside the app, so it can't be moved from here.</summary>
    public bool FolderIsFixed => Main.FolderIsFixed;

    [ObservableProperty] private string _userName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseFolder))]
    private bool _useServer;

    [ObservableProperty] private string _serverAddress;
    [ObservableProperty] private string _accessKey;

    /// <summary>Show the access key as text, to check it was typed correctly.</summary>
    [ObservableProperty] private bool _showKey;
    [ObservableProperty] private string _problem = "";
    [ObservableProperty] private bool _isWorking;

    public string DataFolder => Main.FolderIsFixed ? Main.Location : _settings.DataFolder;

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
        _useServer = (settings.UseServer && main.Platform.SupportsServer) || !main.Platform.SupportsFolders;
        _serverAddress = settings.ServerAddress;
        _accessKey = settings.AccessKey;
    }

    private GuideSettings Edited()
    {
        var next = _settings.Clone();
        next.UserName = UserName ?? "";
        next.UseServer = (UseServer && CanUseServer) || !CanUseFolder;
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
