using DmapShared.Models;
using System.ComponentModel;

namespace DmapAndroid.Controls;

public partial class RosterCard : ContentView
{
    private CastMember? _member;

    public RosterCard()
    {
        InitializeComponent();
        new EditModeWatcher(this, _ => ShowPlayedBy());
    }

    private void OnChoosePortrait(object? sender, EventArgs e) => AppState.Main.ChoosePortraitCommand.Execute(BindingContext);
    private void OnRemovePortrait(object? sender, EventArgs e) => AppState.Main.RemovePortraitCommand.Execute(BindingContext);
    private void OnRemove(object? sender, EventArgs e) => AppState.Main.RemoveCastCommand.Execute(BindingContext);
    private void OnOpenActor(object? sender, EventArgs e) => AppState.Main.ShowPersonCommand.Execute(_member?.Actor);

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (_member != null) _member.PropertyChanged -= OnMemberChanged;
        _member = BindingContext as CastMember;
        if (_member != null) _member.PropertyChanged += OnMemberChanged;
        ShowPlayedBy();
    }

    private void OnMemberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CastMember.Actor)) ShowPlayedBy();
    }

    // The link while reading (when they're on the Cast page); the name to type otherwise.
    private void ShowPlayedBy()
    {
        bool link = _member?.Actor != null && AppState.Main?.IsEditing == false;
        ActorLink.IsVisible = link;
        PlayerEntry.IsVisible = !link;
    }
}
