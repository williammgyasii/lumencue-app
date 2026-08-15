using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ChurchProjection.UI.Converters;

/// <summary>Two-way bridge between a hex colour string (e.g. <c>#FFFFFFFF</c>) and an Avalonia <see cref="Color"/>.</summary>
public sealed class HexColorConverter : IValueConverter
{
    public static readonly HexColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && Color.TryParse(s, out var color) ? color : Colors.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color color
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
            : "#00000000";
}
