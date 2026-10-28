using DmapAndroid.Controls;
using DmapShared.Models;

namespace DmapAndroid.Pages;

public partial class ShootNotesView : ContentView
{
    public ShootNotesView() => InitializeComponent();

    private void OnBack(object? sender, EventArgs e) =>
        AppState.Main.CloseShootNotesCommand.Execute((ShootNotesPage)BindingContext);
}
