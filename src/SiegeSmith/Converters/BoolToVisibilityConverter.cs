using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SiegeSmith.Converters;

/// <summary>Bool → <see cref="Visibility"/>. Null-safe (null reads as false) and invertible
/// via <see cref="Invert"/>, so one converter type serves both show-on-true and hide-on-true.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ Invert;
}
