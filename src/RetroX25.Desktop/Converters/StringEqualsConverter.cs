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
/// Converts string comparison to bool (for conditional visibility)
/// Usage: IsVisible="{Binding Type, Converter={StaticResource StringEqualsConverter}, ConverterParameter=Serial}"
/// </summary>
public class StringEqualsConverter : IValueConverter
{
    /// <summary>
    /// Tells whether the text of the bound value equals the converter parameter, ignoring
    /// case.
    /// </summary>
    /// <param name="value">
    /// The bound value; its <c>ToString</c> text is compared.
    /// </param>
    /// <param name="targetType">
    /// The type the binding target expects; not used.
    /// </param>
    /// <param name="parameter">
    /// The text to compare with; its <c>ToString</c> text is compared.
    /// </param>
    /// <param name="culture">
    /// The culture of the binding; not used. The comparison is ordinal.
    /// </param>
    /// <returns>
    /// True when the two texts match ignoring case. False when they differ or when either is
    /// null.
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        string valueStr = value.ToString() ?? string.Empty;
        string paramStr = parameter.ToString() ?? string.Empty;

        return string.Equals(valueStr, paramStr, StringComparison.OrdinalIgnoreCase);
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
        throw new NotImplementedException("StringEqualsConverter does not support ConvertBack");
    }
}
