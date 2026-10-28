using DmapAndroid.Controls;

namespace DmapAndroid.Pages;

public partial class CastView : ContentView
{
    public CastView() => InitializeComponent();

    private void OnAddDungeonMaster(object? sender, EventArgs e) => AppState.Main.AddCastMemberCommand.Execute("DungeonMaster");
    private void OnAddPerson(object? sender, EventArgs e) => AppState.Main.AddCastMemberCommand.Execute("Cast");
    private void OnAddGuestStar(object? sender, EventArgs e) => AppState.Main.AddCastMemberCommand.Execute("Guest");
}
