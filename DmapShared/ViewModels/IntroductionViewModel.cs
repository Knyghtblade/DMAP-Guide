namespace DmapShared.ViewModels;

/// <summary>
/// The "Introduction - What is DMAP?" page: a few paragraphs on what the show is. The text
/// is kept in the show-wide record, so it's edited on its own like the Cast page.
/// </summary>
public class IntroductionViewModel
{
    public MainViewModel Main { get; }

    public IntroductionViewModel(MainViewModel main) => Main = main;
}
