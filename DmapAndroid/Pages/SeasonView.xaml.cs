using DmapAndroid.Controls;

namespace DmapAndroid.Pages;

public partial class SeasonView : ContentView
{
    public SeasonView() => InitializeComponent();

    private void OnNewCampaign(object? sender, EventArgs e) => AppState.Main.AddCampaignCommand.Execute(null);
}
