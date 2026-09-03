using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RetroX25.Desktop.Converters;

/// <summary>
/// Converts string comparison to bool (for conditional visibility)
/// Usage: IsVisible="{Binding Type, Converter={StaticResource StringEqualsConverter}, ConverterParameter=Serial}"
/// </summary>
public class StringEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        string valueStr = value.ToString() ?? string.Empty;
        string paramStr = parameter.ToString() ?? string.Empty;

        return string.Equals(valueStr, paramStr, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("StringEqualsConverter does not support ConvertBack");
    }
}
