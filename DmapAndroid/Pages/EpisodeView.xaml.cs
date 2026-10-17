using DmapAndroid.Controls;
using DmapShared.Models;

namespace DmapAndroid.Pages;

public partial class EpisodeView : ContentView
{
    public EpisodeView() => InitializeComponent();

    private Episode Episode => (Episode)BindingContext;
    private static object? ItemOf(object sender) => ((BindableObject)sender).BindingContext;

    private void OnOpenRoster(object? sender, EventArgs e)
    {
        if (Episode.Campaign is Campaign c) AppState.Main.OpenRosterCommand.Execute(c);
    }

    private void OnAddGuest(object? sender, EventArgs e) => AppState.Main.AddGuestCommand.Execute(Episode);
    private void OnAddKeyMoment(object? sender, EventArgs e) => AppState.Main.AddKeyMomentCommand.Execute(Episode);
    private void OnAddAirDate(object? sender, EventArgs e) => AppState.Main.AddAirDateCommand.Execute(Episode);
    private void OnRemoveKeyMoment(object? sender, EventArgs e) => AppState.Main.RemoveKeyMomentCommand.Execute(ItemOf(sender!));
    private void OnRemoveAirDate(object? sender, EventArgs e) => AppState.Main.RemoveAirDateCommand.Execute(ItemOf(sender!));
}
