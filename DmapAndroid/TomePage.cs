using DmapAndroid.Controls;
using DmapAndroid.Pages;
using DmapShared.Models;
using DmapShared.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DmapAndroid;

/// <summary>
/// The tome on the phone: the same book as on the desktop. A cover that opens, then a
/// two-page spread: the table of contents on the left, the open page on the right.
/// All behaviour comes from the shared <see cref="MainViewModel"/>; this only draws it.
/// </summary>
public sealed class TomePage : ContentPage
{
    private readonly MainViewModel _vm;
    private Grid? _leftDoor;
    private Grid? _rightDoor;
    private bool _coverOpening;
    private bool _bookOpen;

    private readonly VerticalStackLayout _toc = new() { Spacing = 0 };
    private readonly ContentView _pageHost = new();
    private readonly ScrollView _pageScroller = new();
    private readonly Border _rightLeaf = new();
    private readonly Button _editButton = new TomeButton { Text = "✎  Edit Page", Margin = 0 };
    private readonly Grid _pageActions = new() { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 4, RowSpacing = 4 };

    public TomePage(MainViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "DMAP Guide";
        BackgroundColor = Tome.Desk;
        ShowCover();

        _vm.PropertyChanged += OnViewModelChanged;
        _vm.GuideChanged += () => ScheduleTocRebuild();
    }

    // ============================================================
    // COVER: swings open like two doors
    // ============================================================
    private void ShowCover()
    {
        _leftDoor = CoverDoor();
        _rightDoor = CoverDoor();
        _leftDoor.Children.Add(new Image { Source = TomeImages.Logo(), WidthRequest = 118, HeightRequest = 118, Opacity = 0.32, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center });

        var titlePlate = new Border
        {
            BackgroundColor = Color.FromArgb("#CC140408"),
            Stroke = Tome.Gold,
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(14, 12),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Image { Source = TomeImages.Logo(), WidthRequest = 270, HeightRequest = 110, Aspect = Aspect.AspectFit },
                    new Label { Text = "The Producer's Guide", FontFamily = "DmapDisplay", FontSize = 25, TextColor = Tome.GoldLight, HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "tap to open the tome", FontFamily = "DmapBodyItalic", FontSize = 14, TextColor = Tome.Gold, Opacity = 0.7, HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        };
        _rightDoor.Children.Add(titlePlate);

        var cover = new Grid
        {
            BackgroundColor = Tome.Desk,
            ColumnDefinitions = { new ColumnDefinition(new GridLength(0.36, GridUnitType.Star)), new ColumnDefinition(new GridLength(0.64, GridUnitType.Star)) },
            Children = { _leftDoor, _rightDoor }
        };
        Grid.SetColumn(_rightDoor, 1);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenTomeAsync();
        cover.GestureRecognizers.Add(tap);

        Content = Binding(cover, padding: 8);
    }

    private static Grid CoverDoor()
    {
        var door = new Grid { BackgroundColor = Tome.Leather, AnchorY = 0.5 };
        door.Children.Add(new Image { Source = TomeImages.Scales(), Aspect = Aspect.AspectFill, Opacity = 0.44 });
        door.Children.Add(new BoxView { Color = Color.FromArgb("#66000000") });
        door.Children.Add(new Border { Stroke = Tome.Gold, StrokeThickness = 1.5, Margin = new Thickness(7), StrokeShape = new RoundRectangle { CornerRadius = 8 }, InputTransparent = true });
        return door;
    }

    private async Task OpenTomeAsync()
    {
        if (_coverOpening || _bookOpen || _leftDoor == null || _rightDoor == null)
            return;
        _coverOpening = true;
        _leftDoor.AnchorX = 0;
        _rightDoor.AnchorX = 1;
        await Task.WhenAll(_leftDoor.ScaleXToAsync(0.01, 820, Easing.CubicInOut), _rightDoor.ScaleXToAsync(0.01, 820, Easing.CubicInOut));
        _bookOpen = true;
        BuildOpenBook();
    }

    /// <summary>The dragon-hide binding with its gold tooling around the pages.</summary>
    private static View Binding(View inside, double padding) => new Grid
    {
        Padding = new Thickness(6, 4),
        BackgroundColor = Tome.Desk,
        Children =
        {
            new Border
            {
                BackgroundColor = Tome.Leather,
                Stroke = Color.FromArgb("#1A0408"),
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Padding = padding,
                Content = new Border
                {
                    Stroke = Tome.Gold,
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 9 },
                    Padding = 5,
                    Content = inside
                }
            }
        }
    };

    // ============================================================
    // THE OPEN BOOK
    // ============================================================
    private void BuildOpenBook()
    {
        // ---------------- LEFT PAGE ----------------
        var header = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                new Image { Source = TomeImages.Logo(), WidthRequest = 110, HeightRequest = 40, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
                new Label { Text = "The Producer's Guide", FontFamily = "DmapDisplay", FontSize = 12, TextColor = Tome.Rubric, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Style = AppStyle("Flourish") },
                SideLink("❧", "Prologue", () => _vm.ShowPrologueCommand.Execute(null)),
                SideLink("☾", "Chronicle of Dates", () => _vm.ShowScheduleCommand.Execute(null)),
                SideLink("⚙", "Settings", () => _vm.ShowSettingsCommand.Execute(null)),
                new Label { Text = "TABLE OF CONTENTS", Style = AppStyle("FieldLabel"), Margin = new Thickness(6, 10, 0, 4) }
            }
        };

        _pageActions.Add(new TomeButton { Text = "+ Season", Command = _vm.AddSeasonCommand, HorizontalOptions = LayoutOptions.Fill }, 0, 0);
        _pageActions.Add(new TomeButton { Text = "+ Campaign", Command = _vm.AddCampaignCommand, HorizontalOptions = LayoutOptions.Fill }, 1, 0);
        _pageActions.Add(new TomeButton { Text = "+ Episode", Command = _vm.AddEpisodeCommand, HorizontalOptions = LayoutOptions.Fill }, 0, 1);
        _pageActions.Add(new TomeButton { Text = "Delete", Command = _vm.DeleteCurrentCommand, HorizontalOptions = LayoutOptions.Fill }, 1, 1);
        _pageActions.IsEnabled = _vm.CanChangePages;

        var leftPage = new Grid
        {
            Background = Parchment(),
            Padding = new Thickness(10, 8, 12, 8),
            RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) }
        };
        // A phone page is short: the heading scrolls away with the contents; the buttons stay put.
        leftPage.Add(new ScrollView { Content = new VerticalStackLayout { Children = { header, _toc } } }, 0, 0);
        leftPage.Add(new VerticalStackLayout { Spacing = 6, Children = { new BoxView { Style = AppStyle("Rule") }, _pageActions } }, 0, 1);

        // ---------------- RIGHT PAGE ----------------
        var breadcrumb = new Label { FontFamily = "DmapHeading", FontSize = 10, TextColor = Tome.InkFaded, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        breadcrumb.SetBinding(Label.TextProperty, nameof(MainViewModel.Breadcrumb));
        var status = new Label { FontFamily = "DmapBodyItalic", FontSize = 12, TextColor = Tome.InkFaded, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(6, 0) };
        status.SetBinding(Label.TextProperty, nameof(MainViewModel.StatusText));
        _editButton.Command = _vm.ToggleEditCommand;
        _editButton.SetBinding(IsVisibleProperty, nameof(MainViewModel.CanEdit));

        var pageHeader = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
            Margin = new Thickness(0, 0, 0, 4)
        };
        pageHeader.Add(breadcrumb, 0, 0);
        pageHeader.Add(status, 1, 0);
        pageHeader.Add(_editButton, 2, 0);

        _pageScroller.Content = _pageHost;
        var rightContent = new Grid
        {
            Padding = new Thickness(14, 8, 10, 4),
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        rightContent.Add(pageHeader, 0, 0);
        rightContent.Add(new BoxView { HeightRequest = 1, Color = Tome.Gold, Opacity = 0.5, Margin = new Thickness(0, 0, 0, 8) }, 0, 1);
        rightContent.Add(_pageScroller, 0, 2);
        rightContent.Add(new Label { Text = "— ❦ —", TextColor = Tome.Gold, Opacity = 0.8, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 4, 0, 0) }, 0, 3);

        _rightLeaf.Background = Parchment();
        _rightLeaf.StrokeThickness = 0;
        _rightLeaf.Content = rightContent;

        var spread = new Grid
        {
            ColumnDefinitions = { new(new GridLength(0.36, GridUnitType.Star)), new(new GridLength(6)), new(new GridLength(0.64, GridUnitType.Star)) },
            BackgroundColor = Color.FromArgb("#BFA273")
        };
        spread.Add(leftPage, 0, 0);
        spread.Add(new BoxView { Background = new LinearGradientBrush([new GradientStop(Color.FromArgb("#663A2410"), 0), new GradientStop(Color.FromArgb("#993A2410"), 0.5f), new GradientStop(Color.FromArgb("#663A2410"), 1)], new Point(0, 0), new Point(1, 0)) }, 1, 0);
        spread.Add(_rightLeaf, 2, 0);

        Content = Binding(spread, padding: 6);

        RebuildToc();
        ShowPage(_vm.CurrentPage, animate: false);
        ShowEditing();
    }

    private Button SideLink(string glyph, string text, Action action)
    {
        var button = new Button
        {
            Text = $"{glyph}   {text}",
            FontFamily = "DmapHeading",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Tome.Ink,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Start,
            Padding = new Thickness(6, 0),
            HeightRequest = 30
        };
        button.Clicked += (_, _) => action();
        return button;
    }

    private static Style AppStyle(string key) => (Style)Application.Current!.Resources[key];

    private static Brush Parchment() => new RadialGradientBrush(
        [new GradientStop(Color.FromArgb("#F7ECD2"), 0), new GradientStop(Color.FromArgb("#EEDDB6"), 0.55f), new GradientStop(Color.FromArgb("#DCC293"), 0.9f), new GradientStop(Color.FromArgb("#C9AA76"), 1)],
        new Point(0.5, 0.5), 0.75);

    // ============================================================
    // KEEPING UP WITH THE VIEW MODEL
    // ============================================================
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentPage):
                ShowPage(_vm.CurrentPage, animate: true);
                ScheduleTocRebuild();
                break;
            case nameof(MainViewModel.Data):
                ScheduleTocRebuild();
                break;
            case nameof(MainViewModel.IsEditing):
                ShowEditing();
                break;
            case nameof(MainViewModel.CanChangePages):
            case nameof(MainViewModel.IsConnected):
                _pageActions.IsEnabled = _vm.CanChangePages;
                break;
        }
    }

    private void ShowEditing()
    {
        _editButton.Text = _vm.IsEditing ? "✓  Done" : "✎  Edit Page";
        // Arcane glow around the page while writing.
        _rightLeaf.Stroke = _vm.IsEditing ? Tome.Arcane : Colors.Transparent;
        _rightLeaf.StrokeThickness = _vm.IsEditing ? 2.5 : 0;
    }

    private int _pageTurn;

    private async void ShowPage(object? page, bool animate)
    {
        if (!_bookOpen) return;
        int turn = ++_pageTurn;

        View view = page switch
        {
            Episode => new EpisodeView(),
            CastRosterPage => new RosterView(),
            Campaign => new CampaignView(),
            Season => new SeasonView(),
            ScheduleViewModel => new ScheduleView(),
            SettingsViewModel => new SettingsView(),
            GuideUnavailablePage => new UnavailableView(),
            _ => new PrologueView()
        };
        view.BindingContext = page;

        if (animate) await _pageHost.FadeToAsync(0.2, 90, Easing.CubicIn);
        if (turn != _pageTurn) return;
        _pageHost.Content = view;
        await _pageScroller.ScrollToAsync(0, 0, false);
        if (animate) await _pageHost.FadeToAsync(1, 260, Easing.CubicOut);
        else _pageHost.Opacity = 1;
    }

    // ============================================================
    // TABLE OF CONTENTS
    // ============================================================
    private readonly List<Action> _unsubscribe = new();
    private bool _tocPending;

    private void ScheduleTocRebuild()
    {
        if (_tocPending || !_bookOpen) return;
        _tocPending = true;
        Dispatcher.Dispatch(() =>
        {
            _tocPending = false;
            RebuildToc();
        });
    }

    /// <summary>Redraw the contents: seasons, their campaigns, then each campaign's cast page and episodes.</summary>
    private void RebuildToc()
    {
        foreach (var undo in _unsubscribe) undo();
        _unsubscribe.Clear();
        _toc.Children.Clear();

        var data = _vm.Data;
        Watch(data.Seasons);
        foreach (var season in data.Seasons)
        {
            AddRow(season, 0, season.Campaigns.Count > 0);
            Watch(season.Campaigns);
            if (!season.IsExpanded) continue;

            foreach (var campaign in season.Campaigns)
            {
                AddRow(campaign, 1, true);
                Watch(campaign.Episodes);
                if (!campaign.IsExpanded) continue;

                AddRow(campaign.RosterPage, 2, false);
                foreach (var episode in campaign.Episodes) AddRow(episode, 2, false);
            }
        }

        if (data.Seasons.Count == 0 && _vm.IsConnected)
            _toc.Children.Add(new Label { Text = "No seasons yet.", Style = AppStyle("EmptyNote") });
    }

    private void Watch(INotifyCollectionChanged list)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => ScheduleTocRebuild();
        list.CollectionChanged += handler;
        _unsubscribe.Add(() => list.CollectionChanged -= handler);
    }

    private void AddRow(TreeNode node, int depth, bool canExpand)
    {
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(TreeNode.IsExpanded) or nameof(TreeNode.IsSelected) or nameof(TreeNode.DisplayName) or nameof(Episode.Status))
                ScheduleTocRebuild();
        };
        node.PropertyChanged += handler;
        _unsubscribe.Add(() => node.PropertyChanged -= handler);

        var row = new Grid
        {
            ColumnDefinitions = { new(new GridLength(depth * 14)), new(new GridLength(22)), new(GridLength.Star) },
            Padding = new Thickness(2, 3),
            BackgroundColor = node.IsSelected ? Color.FromArgb("#55C9A24A") : Colors.Transparent
        };

        if (canExpand)
        {
            var arrow = new Label
            {
                Text = node.IsExpanded ? "▾" : "▸",
                FontSize = 14,
                TextColor = Tome.InkSoft,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            var toggle = new TapGestureRecognizer();
            toggle.Tapped += (_, _) => node.IsExpanded = !node.IsExpanded;
            arrow.GestureRecognizers.Add(toggle);
            row.Add(arrow, 1, 0);
        }

        View glyph = node switch
        {
            Episode e => new Ellipse { WidthRequest = 8, HeightRequest = 8, Fill = e.Status switch { EpisodeStatus.Aired => Tome.Aired, EpisodeStatus.Filmed => Tome.Filmed, _ => Tome.Planned }, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(1, 0, 8, 0) },
            _ => new Label
            {
                Text = node switch { Season => "❖", Campaign => "⚔", _ => "♛" },
                TextColor = node is Campaign ? Tome.Gold : Tome.Rubric,
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 7, 0)
            }
        };

        var name = new Label
        {
            Text = node.DisplayName,
            TextColor = Tome.Ink,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = node is Campaign ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation
        };
        switch (node)
        {
            case Season:
                name.FontFamily = "DmapHeading"; name.FontAttributes = FontAttributes.Bold; name.FontSize = 14;
                break;
            case Campaign:
                name.FontFamily = "DmapHeading"; name.FontAttributes = FontAttributes.Bold; name.FontSize = 12.5;
                break;
            case CastRosterPage:
                name.FontFamily = "DmapBodyItalic"; name.FontSize = 14;
                break;
            default:
                name.FontFamily = "DmapBody"; name.FontSize = 14;
                break;
        }

        var label = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        label.Add(glyph, 0, 0);
        label.Add(name, 1, 0);
        var open = new TapGestureRecognizer();
        open.Tapped += async (_, _) => await _vm.NavigateAsync(node);
        label.GestureRecognizers.Add(open);
        row.Add(label, 2, 0);

        _toc.Children.Add(row);
    }

    // ============================================================
    // BACK BUTTON: up a level, like the breadcrumb
    // ============================================================
    protected override bool OnBackButtonPressed()
    {
        if (!_bookOpen) return base.OnBackButtonPressed();
        switch (_vm.CurrentPage)
        {
            case PrologueViewModel:
                return base.OnBackButtonPressed();
            case TreeNode { ParentNode: TreeNode parent }:
                _ = _vm.NavigateAsync(parent);
                return true;
            default:
                _vm.ShowPrologueCommand.Execute(null);
                return true;
        }
    }
}

public static class TomeImages
{
    public static ImageSource Logo() => ImageSource.FromResource("DmapAndroid.Resources.dmaplogo.png", typeof(TomeImages).Assembly);
    public static ImageSource Scales() => ImageSource.FromResource("DmapAndroid.Resources.dragonscales.jpg", typeof(TomeImages).Assembly);
}
