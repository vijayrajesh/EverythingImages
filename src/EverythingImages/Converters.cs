using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace EverythingImages;

/// <summary>Visible when the value is non-null and, for strings, non-empty.
/// Parameter "invert" flips it.</summary>
public sealed class ShowIfSet : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var set = value is string s ? s.Length > 0 : value is bool b ? b : value != null;
        if (p as string == "invert") set = !set;
        return set ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class Invert : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not true;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not true;
}

/// <summary>Image file path -> bitmap decoded at the width in the parameter
/// (the Inspector preview).</summary>
public sealed class PathToImage : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not string path) return null;
        return Thumbnails.Load(path, int.TryParse(p as string, out var w) ? w : 700);
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>[fraction 0..1, full width] -> width of a progress fill.</summary>
public sealed class FractionWidth : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object? p, CultureInfo c) =>
        values is [double f, double w] ? Math.Max(0, Math.Min(1, f)) * w : 0.0;

    public object[] ConvertBack(object v, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
}
