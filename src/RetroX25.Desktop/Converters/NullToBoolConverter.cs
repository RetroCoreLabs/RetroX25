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
/// Converts null to bool (null = false, not null = true).
/// Useful for enabling buttons when an item is selected.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    /// <summary>
    /// Tells whether the bound value is set.
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
    /// True when <paramref name="value"/> is not null, otherwise false.
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
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
        throw new NotImplementedException();
    }
}
