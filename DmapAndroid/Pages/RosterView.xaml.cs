using DmapAndroid.Controls;
using DmapShared.Models;

namespace DmapAndroid.Pages;

public partial class RosterView : ContentView
{
    public RosterView() => InitializeComponent();

    private void OnAddCharacter(object? sender, EventArgs e) =>
        AppState.Main.AddCharacterCommand.Execute(((CastRosterPage)BindingContext).Campaign);
}
