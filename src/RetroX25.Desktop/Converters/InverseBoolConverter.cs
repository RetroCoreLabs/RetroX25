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
/// Inverts a boolean value.
/// True -> False, False -> True
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    /// <summary>
    /// Inverts the bound bool.
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
    /// The opposite of <paramref name="value"/> when it is a bool, otherwise true.
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return true;
    }

    /// <summary>
    /// Inverts the bool coming back from the binding target.
    /// </summary>
    /// <param name="value">
    /// The value from the binding target.
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
    /// The opposite of <paramref name="value"/> when it is a bool, otherwise true.
    /// </returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return true;
    }
}
