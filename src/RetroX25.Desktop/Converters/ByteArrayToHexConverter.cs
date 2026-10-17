//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

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
    /// <summary>
    /// Turns a byte array into upper-case hex pairs separated by spaces.
    /// </summary>
    /// <param name="value">
    /// The bound value.
    /// </param>
    /// <param name="targetType">
    /// The type the binding target expects; not used.
    /// </param>
    /// <param name="parameter">
    /// The converter parameter; not used.
    /// </param>
    /// <param name="culture">
    /// The culture of the binding; not used.
    /// </param>
    /// <returns>
    /// The hex string, or an empty string when <paramref name="value"/> is not a byte array
    /// or is empty.
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        return string.Empty;
    }

    /// <summary>
    /// Not supported; this converter only works one way.
    /// </summary>
    /// <param name="value">
    /// The value from the binding target; not used.
    /// </param>
    /// <param name="targetType">
    /// The type the source expects; not used.
    /// </param>
    /// <param name="parameter">
    /// The converter parameter; not used.
    /// </param>
    /// <param name="culture">
    /// The culture of the binding; not used.
    /// </param>
    /// <returns>
    /// Never returns.
    /// </returns>
    /// <exception cref="NotImplementedException">
    /// Always thrown.
    /// </exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported for ByteArrayToHexConverter");
    }
}
