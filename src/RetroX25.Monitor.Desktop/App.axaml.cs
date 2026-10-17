//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RetroX25.Monitor.Desktop.Services;
using RetroX25.Monitor.Desktop.Theming;
using RetroX25.Monitor.Desktop.ViewModels;
using RetroX25.Monitor.Desktop.Views;

namespace RetroX25.Monitor.Desktop
{
    /// <summary>
    /// The Avalonia application: loads XAML resources, applies the persisted theme, and shows the network
    /// map as the main window (the map is the app's entry point).
    /// </summary>
    public partial class App : Application
    {
        /// <inheritdoc/>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <inheritdoc/>
        public override void OnFrameworkInitializationCompleted()
        {
            // Apply the persisted theme before any window is shown so the first paint is already themed.
            AppPreferences prefs = PreferencesStore.Load();
            ThemeManager.ApplyTheme(prefs.Theme);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new NetworkMapWindow
                {
                    DataContext = new NetworkMapViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
