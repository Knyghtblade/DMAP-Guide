using DmapShared.Models;
using System.Collections.ObjectModel;

namespace DmapShared.ViewModels;

/// <summary>
/// The "Cast" page: the show's real people, across every campaign. The list is kept in
/// the show-wide record, so editing here saves with the Prologue's details.
/// </summary>
public class CastPageViewModel
{
    public MainViewModel Main { get; }

    public ObservableCollection<CastMember> DungeonMasters => Main.Data.DungeonMasters;
    public ObservableCollection<CastMember> Cast => Main.Data.Cast;
    public ObservableCollection<CastMember> Guests => Main.Data.Guests;

    public CastPageViewModel(MainViewModel main)
    {
        Main = main;
        RefreshAppearances();
    }

    /// <summary>Fill in each person's "Plays … in …" line from the campaigns' character lists.</summary>
    public void RefreshAppearances() => Main.Data.RefreshCastAppearances();
}
