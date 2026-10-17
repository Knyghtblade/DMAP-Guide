using DmapAndroid.Controls;

namespace DmapAndroid.Pages;

public partial class PrologueView : ContentView
{
    public PrologueView()
    {
        InitializeComponent();
        Logo.Source = TomeImages.Logo();
    }

    private void OnSettings(object? sender, EventArgs e) => AppState.Main.ShowSettingsCommand.Execute(null);
}
