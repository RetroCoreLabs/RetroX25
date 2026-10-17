//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace RetroX25.Monitor.Desktop.Theming
{
    /// <summary>
    /// The selectable colour themes. Each corresponds to one palette resource dictionary under
    /// <c>Styles/Palettes</c>; both palettes define the same semantic brush keys.
    /// </summary>
    public enum AppTheme
    {
        /// <summary>
        /// The default X.25 "telecom" palette (dark slate, cyan/blue accent).
        /// </summary>
        X25,

        /// <summary>
        /// The RetroFS palette (dark navy, green accent, orange secondary).
        /// </summary>
        RetroFs
    }

    /// <summary>
    /// Owns the application's active colour palette. It installs exactly one palette
    /// <see cref="Avalonia.Controls.ResourceDictionary"/> into the application's merged resources and
    /// swaps it wholesale when the theme changes. Because the UI binds palette colours with
    /// <c>{DynamicResource ...}</c>, swapping the dictionary restyles everything live.
    /// </summary>
    public static class ThemeManager
    {
        // Absolute avares URIs of the two palette dictionaries. Keep these in step with the files on disk.
        private const string X25Uri = "avares://x25monitor/Styles/Palettes/X25Palette.axaml";
        private const string RetroFsUri = "avares://x25monitor/Styles/Palettes/RetroFsPalette.axaml";

        // The palette dictionary currently installed in Application.Resources, so we can remove it on swap.
        private static ResourceDictionary? _current;

        /// <summary>
        /// The theme currently applied.
        /// </summary>
        public static AppTheme CurrentTheme { get; private set; } = AppTheme.X25;

        /// <summary>
        /// Raised on the UI thread after the palette has been swapped, so view-models that cache brushes
        /// (e.g. the network map's node/link colours) can refresh them.
        /// </summary>
        public static event Action? ThemeChanged;

        /// <summary>
        /// Installs the palette for the given theme, replacing any previously-installed palette, and raises
        /// <see cref="ThemeChanged"/>.
        /// </summary>
        /// <param name="theme">
        /// The theme to apply.
        /// </param>
        public static void ApplyTheme(AppTheme theme)
        {
            Application? app = Application.Current;
            if (app == null)
            {
                return;
            }

            string uri = theme == AppTheme.RetroFs ? RetroFsUri : X25Uri;

            // Load the palette dictionary from its compiled XAML resource.
            ResourceDictionary palette = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri));

            // Swap: drop the old palette (if any), then add the new one so its keys win.
            if (_current != null)
            {
                app.Resources.MergedDictionaries.Remove(_current);
            }
            app.Resources.MergedDictionaries.Add(palette);
            _current = palette;
            CurrentTheme = theme;

            ThemeChanged?.Invoke();
        }

        /// <summary>
        /// Resolves a semantic brush key against the active palette.
        /// </summary>
        /// <param name="key">
        /// The semantic brush key (e.g. <c>NodeReachableBrush</c>).
        /// </param>
        /// <returns>
        /// The brush, or <see cref="Brushes.Gray"/> when the key is not found (defensive; both palettes
        /// should define every key).
        /// </returns>
        public static IBrush Brush(string key)
        {
            Application? app = Application.Current;
            if (app != null
                && app.TryGetResource(key, app.ActualThemeVariant, out object? value)
                && value is IBrush brush)
            {
                return brush;
            }

            return Brushes.Gray;
        }
    }
}
