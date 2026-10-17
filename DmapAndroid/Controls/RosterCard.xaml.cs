namespace DmapAndroid.Controls;

public partial class RosterCard : ContentView
{
    public RosterCard() => InitializeComponent();

    private void OnChoosePortrait(object? sender, EventArgs e) => AppState.Main.ChoosePortraitCommand.Execute(BindingContext);
    private void OnRemove(object? sender, EventArgs e) => AppState.Main.RemoveCastCommand.Execute(BindingContext);
}
