//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.ObjectModel;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;

using NUnit.Framework;

using RetroX25.Monitor.Desktop.Services;
using RetroX25.Monitor.Desktop.Theming;
using RetroX25.Monitor.Desktop.ViewModels;
using RetroX25.Monitor.Desktop.Views;

namespace RetroX25.Monitor.Desktop.Tests
{
    /// <summary>
    /// Opens the monitor's secondary windows once, headless, with their real view-models.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The main window is <see cref="NetworkMapWindow"/> and it already has real render tests in
    /// <see cref="NetworkMapWindowTests"/>. The other four had nothing: until 2026-09-04 no window of
    /// this app had ever been shown, so a renamed <c>avares://</c> resource or a compiled binding to
    /// a moved view-model property would have survived every build and every test, and appeared the
    /// first time someone pressed the menu item.
    /// </para>
    /// <para>
    /// Smoke tests only - they assert the window builds, lays out and renders without throwing, not
    /// that it looks right. Skia is on (see <see cref="TestAppBuilder"/>) so an unresolvable
    /// <c>DynamicResource</c> brush fails here instead of on screen.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class WindowSmokeTests
    {
        /// <summary>
        /// The endpoint list the three monitor view-models are built around.
        /// </summary>
        /// <returns>
        /// One entry, matching Switch-A of the demo network.
        /// </returns>
        /// <remarks>
        /// Not empty: an empty collection would skip every item template, which is where a broken
        /// binding usually is. Nothing connects - the view-models only dial when asked to.
        /// </remarks>
        private static ObservableCollection<SwitchEndpoint> Endpoints()
        {
            return new ObservableCollection<SwitchEndpoint>
            {
                new SwitchEndpoint("Switch-A", "127.0.0.1", 4001)
            };
        }

        /// <summary>
        /// Shows a window and lets the layout settle.
        /// </summary>
        /// <param name="window">The window to show.</param>
        private static void ShowAndSettle(Window window)
        {
            ThemeManager.ApplyTheme(AppTheme.X25);

            window.Width = 1000;
            window.Height = 700;
            window.Show();

            // Several passes: the first only reaches the top-level template, and a binding nested in
            // a content template would not have been applied yet.
            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
        }

        [AvaloniaTest]
        public void LinkStatusWindow_opens()
        {
            LinkStatusWindow window = new LinkStatusWindow
            {
                DataContext = new LinkStatusViewModel(Endpoints(), action => action())
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }

        [AvaloniaTest]
        public void LogWindow_opens()
        {
            LogWindow window = new LogWindow
            {
                DataContext = new LogViewModel(Endpoints(), action => action())
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }

        [AvaloniaTest]
        public void PacketMonitorWindow_opens()
        {
            PacketMonitorWindow window = new PacketMonitorWindow
            {
                DataContext = new PacketMonitorViewModel(Endpoints(), action => action())
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }

        [AvaloniaTest]
        public void PreferencesWindow_opens()
        {
            PreferencesWindow window = new PreferencesWindow
            {
                DataContext = new PreferencesViewModel(new AppPreferences())
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }
    }
}
