using DmapGuide.Models;
using DmapGuide.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DmapGuide.Converters;

/// <summary>bool → Visibility. Pass "Invert" as the parameter to flip it.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c)
    {
        bool b = value is bool v && v;
        if (p as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) => !(value is bool b && b);
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => !(value is bool b && b);
}

/// <summary>Hides an element when the value is null, an empty string or zero items.</summary>
public class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c)
    {
        bool empty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int i => i == 0,
            _ => false
        };
        if (p as string == "Invert") empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>DateTime? → text. Parameter is the format (default "ddd, MMM d, yyyy").</summary>
public class DateTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) =>
        value is DateTime d ? d.ToString(p as string ?? "ddd, MMM d, yyyy", CultureInfo.CurrentCulture) : "Not set";
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Stored image path → loaded bitmap (null if missing).</summary>
public class ImageConverter : IValueConverter
{
    public object? Convert(object value, Type t, object? p, CultureInfo c)
    {
        string? full = DataStore.Current?.ResolveImage(value as string);
        if (full == null) return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(full);
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't lock the file
            if (int.TryParse(p as string, out int w)) bmp.DecodePixelWidth = w;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Planned = Frozen("#8C7A63");
    private static readonly Brush Filmed = Frozen("#C08A1E");
    private static readonly Brush Aired = Frozen("#4F7F3A");

    public object Convert(object value, Type t, object? p, CultureInfo c) => value switch
    {
        EpisodeStatus.Aired => Aired,
        EpisodeStatus.Filmed => Filmed,
        _ => Planned
    };
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
