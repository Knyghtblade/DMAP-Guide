using DmapShared.Storage;
using DmapShared.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace DmapAndroid.Controls;

/// <summary>The one view model behind every page (as on the desktop).</summary>
public static class AppState
{
    public static MainViewModel Main { get; set; } = null!;
}

/// <summary>Follows Main.IsEditing while on screen (and stops when the page is gone, so old pages don't linger).</summary>
internal sealed class EditModeWatcher
{
    private readonly Action<bool> _apply;

    public EditModeWatcher(VisualElement element, Action<bool> apply)
    {
        _apply = apply;
        element.Loaded += (_, _) =>
        {
            AppState.Main.PropertyChanged += OnChanged;
            _apply(AppState.Main.IsEditing);
        };
        element.Unloaded += (_, _) => AppState.Main.PropertyChanged -= OnChanged;
        if (AppState.Main != null) _apply(AppState.Main.IsEditing);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsEditing)) _apply(AppState.Main.IsEditing);
    }
}

/// <summary>Text that reads like writing on the page, and turns into a field in Edit mode. Placeholder = hint when empty.</summary>
public class InkEntry : Entry
{
    // Fonts and colours come from the InkEntry style in App.xaml, so page styles (e.g. TitleEntry) can change them.
    public InkEntry()
    {
        new EditModeWatcher(this, editing =>
        {
            IsReadOnly = !editing;
            BackgroundColor = editing ? Tome.EditField : Colors.Transparent;
        });
    }
}

/// <summary>Multi-line <see cref="InkEntry"/>.</summary>
public class InkEditor : Editor
{
    public InkEditor()
    {
        AutoSize = EditorAutoSizeOption.TextChanges;
        MinimumHeightRequest = 34;
        new EditModeWatcher(this, editing =>
        {
            IsReadOnly = !editing;
            BackgroundColor = editing ? Tome.EditField : Colors.Transparent;
        });
    }
}

/// <summary>Shown only in Edit mode.</summary>
public class EditOnly : ContentView
{
    public EditOnly() => new EditModeWatcher(this, editing => IsVisible = editing);
}

/// <summary>Shown only when reading.</summary>
public class ReadingOnly : ContentView
{
    public ReadingOnly() => new EditModeWatcher(this, editing => IsVisible = !editing);
}

/// <summary>The gilded leather button.</summary>
public class TomeButton : Button
{
    public TomeButton()
    {
        FontFamily = "DmapHeading";
        FontSize = 11;
        FontAttributes = FontAttributes.Bold;
        TextColor = Tome.GoldLight;
        BackgroundColor = Tome.Leather;
        BorderColor = Tome.Gold;
        BorderWidth = 1;
        CornerRadius = 4;
        Padding = new Thickness(9, 4);
        MinimumHeightRequest = 30;
        HeightRequest = 34;
        HorizontalOptions = LayoutOptions.Start;
    }
}

/// <summary>"+ Add …" button beside section headers; shown in Edit mode.</summary>
public class AddButton : TomeButton
{
    public AddButton()
    {
        FontSize = 10;
        Margin = new Thickness(10, 0, 0, 0);
        VerticalOptions = LayoutOptions.Center;
        new EditModeWatcher(this, editing => IsVisible = editing);
    }
}

/// <summary>Small round ✕ (Edit mode).</summary>
public class RemoveButton : Button
{
    public RemoveButton()
    {
        Text = "✕";
        FontSize = 11;
        TextColor = Tome.Rubric;
        BackgroundColor = Color.FromArgb("#228B2A1E");
        BorderColor = Tome.Rubric;
        BorderWidth = 1;
        CornerRadius = 13;
        WidthRequest = 26;
        HeightRequest = 26;
        Padding = 0;
        VerticalOptions = LayoutOptions.Start;
        new EditModeWatcher(this, editing => IsVisible = editing);
    }
}

/// <summary>A row that opens a page when tapped (the desktop's quiet text links).</summary>
public class LinkRow : ContentView
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(LinkRow));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(LinkRow));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>When set, tapping opens this page (instead of running <see cref="Command"/>).</summary>
    public static readonly BindableProperty TargetProperty =
        BindableProperty.Create(nameof(Target), typeof(object), typeof(LinkRow));

    public object? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Raised when tapped (for rows that do something other than open a page).</summary>
    public event EventHandler? Tapped;

    public LinkRow()
    {
        Padding = new Thickness(5, 4);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            BackgroundColor = Color.FromArgb("#33C9A24A");
            Tapped?.Invoke(this, EventArgs.Empty);
            if (Target != null) await AppState.Main.NavigateAsync(Target);
            else if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
            BackgroundColor = Colors.Transparent;
        };
        GestureRecognizers.Add(tap);
    }
}

/// <summary>A date that can be "Not set": text when reading; a date picker with Clear in Edit mode.</summary>
public class DateField : ContentView
{
    public static readonly BindableProperty DateProperty = BindableProperty.Create(
        nameof(Date), typeof(DateTime?), typeof(DateField), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((DateField)b).Refresh());

    public DateTime? Date
    {
        get => (DateTime?)GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    public string Format { get; set; } = "ddd, MMM d, yyyy";

    private readonly Label _text = new() { FontFamily = "DmapBody", FontSize = 15, TextColor = Tome.Ink, Margin = new Thickness(3, 1) };
    private readonly DatePicker _picker = new() { FontFamily = "DmapBody", FontSize = 15, TextColor = Tome.Ink, BackgroundColor = Tome.EditField };
    private readonly Button _clear = new() { Text = "Clear", FontFamily = "DmapBodyItalic", FontSize = 13, TextColor = Tome.InkSoft, BackgroundColor = Colors.Transparent, Padding = new Thickness(6, 0) };
    private readonly Button _set = new() { Text = "Set date", FontFamily = "DmapBodyItalic", FontSize = 14, TextColor = Tome.Rubric, BackgroundColor = Tome.EditField, Padding = new Thickness(8, 2), HorizontalOptions = LayoutOptions.Start };
    private readonly HorizontalStackLayout _editor;
    private bool _editing;
    private bool _updating;

    public DateField()
    {
        _editor = new HorizontalStackLayout { Spacing = 2, Children = { _picker, _clear, _set } };
        Content = new Grid { Children = { _text, _editor } };

        _picker.DateSelected += (_, e) =>
        {
            if (_updating) return;
            DateTime? picked = e.NewDate;
            Date = picked?.Date;
        };
        _clear.Clicked += (_, _) => Date = null;
        _set.Clicked += (_, _) => Date = DateTime.Today;

        new EditModeWatcher(this, editing =>
        {
            _editing = editing;
            Refresh();
        });
    }

    private void Refresh()
    {
        _text.Text = Date is DateTime d ? d.ToString(Format, CultureInfo.CurrentCulture) : "Not set";
        _text.IsVisible = !_editing;
        _editor.IsVisible = _editing;
        _picker.IsVisible = _clear.IsVisible = Date.HasValue;
        _set.IsVisible = !Date.HasValue;
        _updating = true;
        _picker.Date = Date ?? DateTime.Today;
        _updating = false;
    }
}

/// <summary>A portrait in a gilded ring, with ✦ while there's no picture.</summary>
public class Portrait : Grid
{
    public static readonly BindableProperty ImagePathProperty = BindableProperty.Create(
        nameof(ImagePath), typeof(string), typeof(Portrait), null,
        propertyChanged: (b, _, _) => ((Portrait)b).Refresh());

    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(Portrait), 72.0,
        propertyChanged: (b, _, _) => ((Portrait)b).Resize());

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private readonly Image _image = new() { Aspect = Aspect.AspectFill };
    private readonly Label _star = new() { Text = "✦", TextColor = Tome.InkFaded, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    private readonly Ellipse _ring = new() { Stroke = Tome.Gold, StrokeThickness = 2.5 };

    public Portrait()
    {
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;
        Children.Add(new Ellipse { Fill = Color.FromArgb("#33876A45") });
        Children.Add(_star);
        Children.Add(_image);
        Children.Add(_ring);
        Resize();
        Loaded += (_, _) => GuideImages.Arrived += Refresh;
        Unloaded += (_, _) => GuideImages.Arrived -= Refresh;
    }

    private void Resize()
    {
        WidthRequest = HeightRequest = Size;
        _image.Clip = new EllipseGeometry(new Point(Size / 2, Size / 2), Size / 2, Size / 2);
        _star.FontSize = Size * 0.32;
    }

    private void Refresh()
    {
        string? file = GuideImages.Resolve(ImagePath);
        _image.Source = file == null ? null : ImageSource.FromFile(file);
    }
}

/// <summary>A campaign's cover art (hidden when there isn't one).</summary>
public class CoverArt : Border
{
    public static readonly BindableProperty ImagePathProperty = BindableProperty.Create(
        nameof(ImagePath), typeof(string), typeof(CoverArt), null,
        propertyChanged: (b, _, _) => ((CoverArt)b).Refresh());

    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    private readonly Image _image = new() { Aspect = Aspect.AspectFill };

    public CoverArt()
    {
        Stroke = Tome.Gold;
        StrokeThickness = 2;
        StrokeShape = new RoundRectangle { CornerRadius = 6 };
        HeightRequest = 170;
        Margin = new Thickness(0, 10, 0, 0);
        Content = _image;
        IsVisible = false;
        Loaded += (_, _) => GuideImages.Arrived += Refresh;
        Unloaded += (_, _) => GuideImages.Arrived -= Refresh;
    }

    private void Refresh()
    {
        string? file = GuideImages.Resolve(ImagePath);
        _image.Source = file == null ? null : ImageSource.FromFile(file);
        IsVisible = !string.IsNullOrWhiteSpace(ImagePath);
    }
}

/// <summary>The tome's colours (the same as the desktop's).</summary>
public static class Tome
{
    public static readonly Color Desk = Color.FromArgb("#1E140C");
    public static readonly Color Leather = Color.FromArgb("#55101E");
    public static readonly Color Ink = Color.FromArgb("#2E1F14");
    public static readonly Color InkSoft = Color.FromArgb("#5A4431");
    public static readonly Color InkFaded = Color.FromArgb("#8C7A63");
    public static readonly Color Rubric = Color.FromArgb("#9E141E");
    public static readonly Color Gold = Color.FromArgb("#C9A24A");
    public static readonly Color GoldLight = Color.FromArgb("#E8CF8A");
    public static readonly Color Arcane = Color.FromArgb("#5B3E8C");
    public static readonly Color Aired = Color.FromArgb("#4F7F3A");
    public static readonly Color Filmed = Color.FromArgb("#C08A1E");
    public static readonly Color Planned = Color.FromArgb("#8C7A63");
    public static readonly Color EditField = Color.FromArgb("#70FFFBEF");
    public static readonly Color Paper = Color.FromArgb("#EEDDB6");
}
