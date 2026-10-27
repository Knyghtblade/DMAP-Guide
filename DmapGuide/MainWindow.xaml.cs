using DmapGuide.Services;
using DmapShared.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DmapGuide;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Random _rnd = new();
    private RenderTargetBitmap? _snapshot;
    private bool _coverOpening;
    private bool _coverOpened;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();

        // "--data <folder>" opens a different guide folder for this run only; otherwise the
        // guide beside the app (the shared folder) wins; otherwise the folder in Settings.
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--data");
        string? dataArg = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        string? folder = dataArg ?? (AppSettings.IsPublishedAsOneFile ? null : AppSettings.GuideFolderBesideApp);

        _vm = new MainViewModel(new WpfPlatform(), new AppSettings(), folder) { CommitPendingEdits = CommitFocusedTextBox };
        Title = $"DMAP Guide {_vm.AppVersion}";

        // The shared app only ever opens the guide beside it, and only when OneDrive syncs that
        // folder. If the guide is missing (the .exe was opened from a download or a stray copy)
        // or the folder isn't synced (the whole folder was downloaded), it says so instead of
        // quietly opening a guide nobody else can see.
        if (dataArg == null && AppSettings.IsPublishedAsOneFile)
        {
            _vm.RequiredFolder = AppSettings.ExpectedGuideFolderBesideApp;
            _vm.RequiredFolderCheck = AppSettings.NotSyncedProblem;
        }
        DataContext = _vm;

        _vm.Navigating += SnapshotPage;
        _vm.PropertyChanged += OnViewModelChanged;
        _vm.ShowCardRequested += ShowCard;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The book stays closed until the cover is clicked; the guide opens behind it.
        StartAmbience();
        await _vm.StartAsync();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;

        // Finish saving (it may need the network) before the window goes.
        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        await _vm.ShutdownAsync();
        Close();
    }

    // The text box being typed in only pushes its value on losing focus;
    // make sure that's captured before saving (e.g. Ctrl+S or closing).
    private void CommitFocusedTextBox()
    {
        if (Keyboard.FocusedElement is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    // ============================================================
    // TABLE OF CONTENTS
    // ============================================================
    // The TreeView picks a selection by itself when it gets focus (e.g. clicking
    // empty space), so pages only open from an actual click on an item or from
    // arrow keys, never from the tree's own selection changes.
    private bool _tocKeyNavigation;

    private void Toc_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        // Let the expander arrows and the scrollbar work normally.
        if (FindAncestor<ToggleButton>(source) != null || FindAncestor<ScrollBar>(source) != null)
            return;

        e.Handled = true;

        var item = FindAncestor<TreeViewItem>(source);
        if (item == null) return;

        if (e.ClickCount == 2)
            item.IsExpanded = !item.IsExpanded;
        else
            _ = _vm.NavigateAsync(item.DataContext);
    }

    private void Toc_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _tocKeyNavigation = true;
        Dispatcher.BeginInvoke(() => _tocKeyNavigation = false, DispatcherPriority.Input);
    }

    private void Toc_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_tocKeyNavigation) _ = _vm.NavigateAsync(e.NewValue);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    // ============================================================
    // FITTING THE BOOK TO THE WINDOW
    // ============================================================
    // The book is laid out at 1360 x 840 and scaled to the window's height, so everything
    // grows together on a big screen. Extra width (wide monitors) opens the book wider, up
    // to a limit that keeps lines of text readable, instead of leaving a huge empty desk.
    private const double DesignWidth = 1360, DesignHeight = 840, WidestBook = 1800;

    private void BookHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = BookHost.ActualWidth, h = BookHost.ActualHeight;
        if (w < 1 || h < 1) return;

        double scale = Math.Min(h / DesignHeight, w / DesignWidth);
        BookScale.ScaleX = BookScale.ScaleY = scale;
        Book.Width = Math.Min(w / scale, WidestBook);
    }

    // Text boxes on the page swallow the mouse wheel; scroll the page instead.
    private void PageScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        PageScroller.ScrollToVerticalOffset(PageScroller.VerticalOffset - e.Delta * 0.6);
        e.Handled = true;
    }

    // A link to a person or character opened the page: once it's laid out, scroll their
    // card to the top and let it glow gold for a moment so it's easy to spot.
    private void ShowCard(DmapShared.Models.CastMember member) =>
        Dispatcher.BeginInvoke(() =>
        {
            var card = FindCard(PageContent, member);
            if (card == null) return;

            var top = card.TransformToAncestor(PageScroller).Transform(new Point(0, 0)).Y;
            PageScroller.ScrollToVerticalOffset(Math.Max(0, PageScroller.VerticalOffset + top - 8));

            var gold = TryFindResource("GoldBrush") is SolidColorBrush b ? b.Color : Colors.Goldenrod;
            var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = gold, BlurRadius = 28, ShadowDepth = 0 };
            card.Effect = glow;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1800)) { BeginTime = TimeSpan.FromMilliseconds(700) };
            fade.Completed += (_, _) => { if (card.Effect == glow) card.Effect = null; };
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, fade);
        }, DispatcherPriority.Loaded);

    // The outermost element showing this person or character: their card in the page's list.
    private static FrameworkElement? FindCard(DependencyObject parent, object item)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && ReferenceEquals(fe.DataContext, item)) return fe;
            if (FindCard(child, item) is FrameworkElement found) return found;
        }
        return null;
    }

    // ============================================================
    // PAGE TURN
    // ============================================================
    private void SnapshotPage()
    {
        _snapshot = null;
        if (!_coverOpened || RightLeaf.ActualWidth < 1 || RightLeaf.ActualHeight < 1) return;

        var dpi = VisualTreeHelper.GetDpi(RightLeaf);
        double w = RightLeaf.ActualWidth, h = RightLeaf.ActualHeight;

        // Draw through a VisualBrush so the page's own margin/offset isn't baked in.
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(RightLeaf), null, new Rect(0, 0, w, h));

        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        _snapshot = bmp;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentPage)) return;

        PageScroller.ScrollToTop();
        if (_snapshot == null) return;

        TurnImage.Source = _snapshot;
        _snapshot = null;
        TurnLeaf.Visibility = Visibility.Visible;

        var dur = TimeSpan.FromMilliseconds(560);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fold = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        fold.Completed += (_, _) =>
        {
            TurnLeaf.Visibility = Visibility.Collapsed;
            TurnImage.Source = null;
        };

        TurnScale.BeginAnimation(ScaleTransform.ScaleXProperty, fold);
        TurnSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, -9, dur) { EasingFunction = ease });
        TurnShade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.8, dur));
        PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, dur + dur));
    }

    // ============================================================
    // COVER
    // ============================================================
    private void Cover_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => OpenCover();

    private void OpenCover()
    {
        if (_coverOpening) return;
        _coverOpening = true;

        var dur = TimeSpan.FromMilliseconds(1300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var swing = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        swing.Completed += (_, _) =>
        {
            Cover.Visibility = Visibility.Collapsed;
            _coverOpened = true;
        };

        LeftDoorScale.BeginAnimation(ScaleTransform.ScaleXProperty, swing);
        RightDoorScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, dur) { EasingFunction = ease });
        LeftDoorSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, -12, dur) { EasingFunction = ease });
        RightDoorSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, 12, dur) { EasingFunction = ease });

        // Light floods out of the seam, then fades.
        var glow = new DoubleAnimationUsingKeyFrames { Duration = dur };
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        SeamGlow.BeginAnimation(OpacityProperty, glow);
        SeamScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.5, 14, dur) { EasingFunction = ease });

        BurstSparkles();
    }

    private void BurstSparkles()
    {
        // Spine position in the sparkle canvas.
        Point spine = Pages.TranslatePoint(new Point(Pages.ColumnDefinitions[0].ActualWidth, Pages.ActualHeight / 2), SparkleCanvas);

        Color[] colors = { Color.FromRgb(0xFF, 0xE6, 0x9A), Color.FromRgb(0xFF, 0xF6, 0xD8), Color.FromRgb(0xC9, 0xA2, 0x4A), Color.FromRgb(0xC8, 0xB4, 0xFF) };

        for (int i = 0; i < 70; i++)
        {
            double size = 2 + _rnd.NextDouble() * 5;
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(colors[_rnd.Next(colors.Length)])
            };

            var move = new TranslateTransform();
            dot.RenderTransform = move;
            Canvas.SetLeft(dot, spine.X);
            Canvas.SetTop(dot, spine.Y + (_rnd.NextDouble() - 0.5) * Pages.ActualHeight * 0.8);
            SparkleCanvas.Children.Add(dot);

            double angle = _rnd.NextDouble() * Math.PI * 2;
            double dist = 120 + _rnd.NextDouble() * 520;
            var dur = TimeSpan.FromMilliseconds(900 + _rnd.Next(1100));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(angle) * dist, dur) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(angle) * dist * 0.6 - 60, dur) { EasingFunction = ease });

            var fade = new DoubleAnimation(1, 0, dur) { BeginTime = TimeSpan.FromMilliseconds(150) };
            fade.Completed += (_, _) => SparkleCanvas.Children.Remove(dot);
            dot.BeginAnimation(OpacityProperty, fade);
        }
    }

    // ============================================================
    // AMBIENCE: flickering candlelight
    // ============================================================
    private void StartAmbience()
    {
        GlowLeft.BeginAnimation(OpacityProperty, Flicker(0.55, 0.8));
        GlowRight.BeginAnimation(OpacityProperty, Flicker(0.35, 0.55));
    }

    private DoubleAnimationUsingKeyFrames Flicker(double min, double max)
    {
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        double t = 0;
        for (int i = 0; i < 14; i++)
        {
            t += 0.15 + _rnd.NextDouble() * 0.5;
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(min + _rnd.NextDouble() * (max - min), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)))
            {
                EasingFunction = new SineEase()
            });
        }
        return anim;
    }
}
