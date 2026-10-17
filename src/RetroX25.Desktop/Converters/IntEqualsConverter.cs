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
/// Converts int comparison to bool (for conditional visibility)
/// Usage: IsVisible="{Binding Interfaces.Count, Converter={StaticResource IntEqualsConverter}, ConverterParameter=0}"
/// </summary>
public class IntEqualsConverter : IValueConverter
{
    /// <summary>
    /// Tells whether the bound value equals the converter parameter as integers.
    /// </summary>
    /// <param name="value">
    /// The bound value, an int or text that parses as one.
    /// </param>
    /// <param name="targetType">
    /// The type the binding target expects; not used.
    /// </param>
    /// <param name="parameter">
    /// The integer to compare with, an int or text that parses as one.
    /// </param>
    /// <param name="culture">
    /// The culture of the binding; not used.
    /// </param>
    /// <returns>
    /// True when both values are integers and equal. False when they differ, when either is
    /// null, or when either does not parse as an integer.
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        // Try to convert value to int
        int valueInt = 0;
        if (value is int i)
            valueInt = i;
        else if (!int.TryParse(value.ToString(), out valueInt))
            return false;

        // Try to convert parameter to int
        int paramInt = 0;
        if (parameter is int p)
            paramInt = p;
        else if (!int.TryParse(parameter.ToString(), out paramInt))
            return false;

        return valueInt == paramInt;
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
        throw new NotImplementedException("IntEqualsConverter does not support ConvertBack");
    }
}
