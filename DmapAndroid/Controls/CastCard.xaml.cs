namespace DmapAndroid.Controls;

public partial class CastCard : ContentView
{
    public CastCard() => InitializeComponent();

    private void OnChoosePortrait(object? sender, EventArgs e) => AppState.Main.ChoosePortraitCommand.Execute(BindingContext);
    private void OnRemove(object? sender, EventArgs e) => AppState.Main.RemoveCastCommand.Execute(BindingContext);
}
