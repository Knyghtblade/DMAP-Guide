namespace DmapAndroid.Controls;

public partial class GuestCard : ContentView
{
    public GuestCard() => InitializeComponent();

    private void OnOpenPerson(object? sender, EventArgs e) => AppState.Main.ShowPersonCommand.Execute(BindingContext);
    private void OnRemove(object? sender, EventArgs e) => AppState.Main.RemoveCastCommand.Execute(BindingContext);
}
