using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NovaSetlist.Ui;

/// <summary>The colour-coding palette for setlist rows. "" = no colour.</summary>
public static class RowColors
{
    public static readonly string[] Tokens = { "", "blue", "teal", "green", "amber", "red", "purple", "pink" };

    private static readonly Dictionary<string, Color> Map = new()
    {
        ["blue"] = Color.FromRgb(0x4A, 0x9E, 0xDE),
        ["teal"] = Color.FromRgb(0x3B, 0xBF, 0xB2),
        ["green"] = Color.FromRgb(0x34, 0xC7, 0x7B),
        ["amber"] = Color.FromRgb(0xE5, 0xB4, 0x42),
        ["red"] = Color.FromRgb(0xE0, 0x52, 0x46),
        ["purple"] = Color.FromRgb(0x9B, 0x6B, 0xD6),
        ["pink"] = Color.FromRgb(0xD6, 0x67, 0xA5),
    };

    public static Color? ColorOf(string? token) =>
        token is not null && Map.TryGetValue(token, out var c) ? c : null;
}

/// <summary>
/// Colour token → brush. ConverterParameter selects the flavour:
/// "bar" (solid, transparent for none), "tint" (faint row wash), "swatch" (solid).
/// </summary>
public sealed class ColorTokenBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> Cache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var token = value as string ?? "";
        var mode = parameter as string ?? "bar";
        var key = mode + ":" + token;
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        Brush brush = Brushes.Transparent;
        if (RowColors.ColorOf(token) is { } c)
        {
            brush = mode == "tint"
                ? new SolidColorBrush(Color.FromArgb(0x14, c.R, c.G, c.B))
                : new SolidColorBrush(c);
            brush.Freeze();
        }
        Cache[key] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>values[0] == values[1] → Visible, else Collapsed (swatch check mark).</summary>
public sealed class EqualsToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0], values[1]) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
