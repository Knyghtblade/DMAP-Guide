using DmapShared.Models;

namespace DmapAndroid.Controls;

public partial class PersonCard : ContentView
{
    public PersonCard() => InitializeComponent();

    private void OnChoosePortrait(object? sender, EventArgs e) => AppState.Main.ChoosePortraitCommand.Execute(BindingContext);
    private void OnRemovePortrait(object? sender, EventArgs e) => AppState.Main.RemovePortraitCommand.Execute(BindingContext);
    private void OnRemove(object? sender, EventArgs e) => AppState.Main.RemoveCastCommand.Execute(BindingContext);

    private void OnOpenCharacter(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CharacterRole role)
            AppState.Main.OpenRosterCommand.Execute(role.Character);
    }
}
