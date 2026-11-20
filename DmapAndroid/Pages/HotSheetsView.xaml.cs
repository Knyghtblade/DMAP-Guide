using DmapAndroid.Controls;
using DmapShared.Models;

namespace DmapAndroid.Pages;

public partial class HotSheetsView : ContentView
{
    public HotSheetsView() => InitializeComponent();

    private HotSheetsPage Page => (HotSheetsPage)BindingContext;

    private void OnAdd(object? sender, EventArgs e) => AppState.Main.AddHotSheetsCommand.Execute(Page.Episode);
    private void OnClear(object? sender, EventArgs e) => AppState.Main.ClearHotSheetsCommand.Execute(Page.Episode);
    private void OnBack(object? sender, EventArgs e) => AppState.Main.CloseHotSheetsCommand.Execute(Page);
}
