using DmapAndroid.Controls;
using DmapShared.Models;

namespace DmapAndroid.Pages;

public partial class CampaignView : ContentView
{
    public CampaignView() => InitializeComponent();

    private Campaign Campaign => (Campaign)BindingContext;

    private void OnChooseCover(object? sender, EventArgs e) => AppState.Main.ChooseCoverCommand.Execute(Campaign);
    private void OnOpenRoster(object? sender, EventArgs e) => AppState.Main.OpenRosterCommand.Execute(Campaign);
    private void OnCharacterTapped(object? sender, EventArgs e) => AppState.Main.OpenRosterCommand.Execute(Campaign);
    private void OnNewEpisode(object? sender, EventArgs e) => AppState.Main.AddEpisodeCommand.Execute(Campaign);
}
