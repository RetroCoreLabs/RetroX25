//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop;

/// <summary>
/// Data template that finds the view for a view-model by name.
/// </summary>
/// <remarks>
/// The view type name is the view-model's full type name with every "ViewModel" replaced by
/// "View".
/// </remarks>
public class ViewLocator : IDataTemplate
{

    /// <summary>
    /// Creates the view for the view-model in <paramref name="param"/>.
    /// </summary>
    /// <param name="param">
    /// The view-model to build a view for.
    /// </param>
    /// <returns>
    /// A new instance of the matching view type, a <see cref="TextBlock"/> that says
    /// "Not Found:" followed by the type name when no such type exists, or null when
    /// <paramref name="param"/> is null.
    /// </returns>
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    /// <summary>
    /// Tells whether this template handles <paramref name="data"/>.
    /// </summary>
    /// <param name="data">
    /// The object to test.
    /// </param>
    /// <returns>
    /// True when <paramref name="data"/> is a <see cref="ViewModelBase"/>.
    /// </returns>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
