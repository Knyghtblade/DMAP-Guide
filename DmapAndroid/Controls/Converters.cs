using DmapShared.Models;
using System.Collections;
using System.Globalization;

namespace DmapAndroid.Controls;

public class StatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        EpisodeStatus.Aired => Tome.Aired,
        EpisodeStatus.Filmed => Tome.Filmed,
        _ => Tome.Planned
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>DateTime? → text. Parameter is the format (default "ddd, MMM d, yyyy").</summary>
public class DateTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is DateTime d ? d.ToString(p as string ?? "ddd, MMM d, yyyy", CultureInfo.CurrentCulture) : "Not set";
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>True when there's something: not null, not blank, not zero, not an empty list.</summary>
public class NotEmptyConverter : IValueConverter
{
    public static bool HasValue(object? value) => value switch
    {
        null => false,
        string s => !string.IsNullOrWhiteSpace(s),
        int i => i != 0,
        ICollection list => list.Count > 0,
        _ => true
    };

    public object Convert(object? value, Type t, object? p, CultureInfo c) => HasValue(value);
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class IsEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => !NotEmptyConverter.HasValue(value);
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public class InvertConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => !(value is bool b && b);
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => !(value is bool b && b);
}
