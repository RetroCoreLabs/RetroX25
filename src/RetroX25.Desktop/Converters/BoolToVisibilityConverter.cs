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
/// Passes a bool through for an <c>IsVisible</c> binding: true shows the control, false hides it.
/// </summary>
/// <remarks>
/// Avalonia has no Visibility type with Visible and Collapsed values, as WPF has; visibility is the
/// bool <c>IsVisible</c> property. This converter exists so a binding written in the WPF style keeps
/// working, and it returns a bool.
/// </remarks>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Passes a bool through for an <c>IsVisible</c> binding.
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
    /// <paramref name="value"/> unchanged when it is a bool, otherwise false.
    /// </returns>
    /// <remarks>
    /// Avalonia has no Visibility type; visibility is the bool <c>IsVisible</c> property, so
    /// the result is a bool and not a Visibility value.
    /// </remarks>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        return false;
    }

    /// <summary>
    /// Passes a bool back from the binding target to the source.
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
    /// <paramref name="value"/> unchanged when it is a bool, otherwise false.
    /// </returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        return false;
    }
}
