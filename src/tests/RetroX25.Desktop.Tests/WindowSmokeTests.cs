//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using RetroIO.Protocols.X25.Runtime;

using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Opens every window of the desktop app once, headless, with the real view-model behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> Until 2026-09-04 nothing had ever shown a window of this app - it was
    /// moved between repositories, renamed, and published, all without one being opened. A build
    /// cannot catch the two things that break in exactly that situation: an <c>avares://</c>
    /// resource whose path no longer resolves after a rename, and a compiled binding to a property
    /// that moved or was renamed on the view-model. Both throw only when the window is loaded and
    /// laid out.
    /// </para>
    /// <para>
    /// These are SMOKE tests. They assert that the window builds, applies its template, lays out and
    /// renders without throwing - not that it looks right. Skia rendering is on in
    /// <see cref="TestAppBuilder"/> so an unresolvable <c>DynamicResource</c> brush fails here rather
    /// than on screen.
    /// </para>
    /// <para>
    /// They are not a substitute for opening the app once by hand; they are what stops the NEXT
    /// rename from breaking it silently.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class WindowSmokeTests
    {
        /// <summary>
        /// Builds the same service graph the app starts on.
        /// </summary>
        /// <returns>
        /// A provider the caller must dispose.
        /// </returns>
        /// <remarks>
        /// Calls <see cref="App.ConfigureServices"/> rather than repeating the registrations, so a
        /// service added to the app is automatically covered here. It also runs the same
        /// <c>IRuntimeService.Initialize</c> step the app performs before its first window, because a
        /// view-model that reads the runtime during construction would otherwise see it uninitialised
        /// in the test and only in the test.
        /// </remarks>
        private static ServiceProvider BuildServices()
        {
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services);
            ServiceProvider provider = services.BuildServiceProvider();

            IRuntimeService runtime = provider.GetRequiredService<IRuntimeService>();
            RuntimeConfig config = provider.GetRequiredService<RuntimeConfig>();
            runtime.Initialize(config);

            return provider;
        }

        /// <summary>
        /// Shows a window and lets the layout settle.
        /// </summary>
        /// <param name="window">The window to show.</param>
        /// <remarks>
        /// Several dispatcher passes, because the first one only reaches the top-level template;
        /// nested content templates and their bindings are applied on later passes, and a broken
        /// binding deeper in the tree would be missed by a single pass.
        /// </remarks>
        private static void ShowAndSettle(Window window)
        {
            window.Width = 1000;
            window.Height = 700;
            window.Show();

            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
        }

        [AvaloniaTest]
        public void MainWindow_opens_with_the_real_view_model()
        {
            using ServiceProvider provider = BuildServices();

            MainWindow window = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>()
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True, "the main window did not become visible");
            Assert.That(window.Content, Is.Not.Null, "the main window rendered no content");
        }

        [AvaloniaTest]
        public void ConfigurationWindow_opens()
        {
            using ServiceProvider provider = BuildServices();

            ConfigurationWindow window = new ConfigurationWindow
            {
                DataContext = provider.GetRequiredService<ConfigurationWindowViewModel>()
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }

        [AvaloniaTest]
        public void PacketWindow_opens()
        {
            using ServiceProvider provider = BuildServices();

            PacketWindow window = new PacketWindow
            {
                DataContext = provider.GetRequiredService<PacketWindowViewModel>()
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }

        [AvaloniaTest]
        public void LogWindow_opens()
        {
            using ServiceProvider provider = BuildServices();

            LogWindow window = new LogWindow
            {
                DataContext = provider.GetRequiredService<LogWindowViewModel>()
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }

        [AvaloniaTest]
        public void RoutesWindow_opens()
        {
            using ServiceProvider provider = BuildServices();

            RoutesWindow window = new RoutesWindow
            {
                DataContext = provider.GetRequiredService<RoutesWindowViewModel>()
            };

            ShowAndSettle(window);

            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Content, Is.Not.Null);
        }

        [AvaloniaTest]
        public void Every_registered_view_model_can_be_constructed()
        {
            using ServiceProvider provider = BuildServices();

            // A view-model that throws in its constructor takes the whole app down at startup, and
            // the window tests above only cover the five that have a window of their own. Explicit
            // list, no LINQ, no reflection sweep - a reflection sweep would silently stop covering a
            // type the moment someone changed how it is registered.
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<StatusWindowViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<TADServerWindowViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<XMSGServerWindowViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<SintranTadServerWindowViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<SimulateReceiveWindowViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<SerialInterfacesTabViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<XotInterfacesTabViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<KnownHostsTabViewModel>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<RoutingTabViewModel>(), Is.Not.Null);
            });
        }
    }
}
