using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RetroX25.Desktop.Converters;

/// <summary>
/// Converts byte arrays to hex string representation.
/// Example: [0x01, 0x3F, 0x00] -> "01 3F 00"
/// </summary>
public class ByteArrayToHexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported for ByteArrayToHexConverter");
    }
}
