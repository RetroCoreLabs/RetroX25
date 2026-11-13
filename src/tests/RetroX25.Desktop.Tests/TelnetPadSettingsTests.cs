//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The telnet PAD settings in the Configuration window and the telnet line in the main window:
    /// the fields are there, they validate, Apply starts, moves and stops the listener, and a real
    /// TCP client sees the result. Real windows, real engine, real sockets, headless.
    /// </summary>
    [TestFixture]
    public sealed class TelnetPadSettingsTests
    {
        /// <summary>
        /// A dialog service that answers by itself and remembers what it was shown.
        /// </summary>
        private sealed class RecordingDialogs : IDialogService
        {
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Infos = new List<string>();

            public void ShowInfo(string title, string message) { Infos.Add(message); }

            public void ShowWarning(string title, string message) { Warnings.Add(message); }

            public void ShowError(string title, string message) { Warnings.Add("ERROR: " + message); }

            public System.Threading.Tasks.Task<bool> ShowConfirmation(string title, string message) => System.Threading.Tasks.Task.FromResult(true);

            public System.Threading.Tasks.Task<string?> ShowOpenFileDialog(string title, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);

            public System.Threading.Tasks.Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        // The runtime service is not disposed when its service provider is (it is not IDisposable), so a
        // test that started a listener would leave the port open for the rest of the run. Every runtime
        // service built here is disposed in TearDown.
        private readonly List<IRuntimeService> _runtimeServices = new List<IRuntimeService>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _runtimeServices.Count; i++)
            {
                _runtimeServices[i].Dispose();
            }

            _runtimeServices.Clear();
        }

        private ServiceProvider BuildServices(RuntimeConfig config, RecordingDialogs dialogs)
        {
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            services.AddSingleton<IDialogService>(dialogs);
            ServiceProvider provider = services.BuildServiceProvider();
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            _runtimeServices.Add(runtimeService);
            runtimeService.Initialize(config);
            return provider;
        }

        private static void ShowAndSettle(Window window)
        {
            window.Width = 1000;
            window.Height = 800;
            window.Show();
            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
        }

        private static T Find<T>(Visual root, string name) where T : Control
        {
            List<Visual> all = new List<Visual>(root.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is T found && found.Name == name)
                {
                    return found;
                }
            }

            Assert.Fail("no " + typeof(T).Name + " named " + name + " in the window (is its tab selected?)");
            throw new InvalidOperationException();
        }

        private static void SelectTelnetTab(ConfigurationWindow window)
        {
            List<Visual> all = new List<Visual>(window.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is TabControl tabs)
                {
                    for (int t = 0; t < tabs.Items.Count; t++)
                    {
                        if (tabs.Items[t] is TabItem item && item.Header is string header && header == "Telnet PAD")
                        {
                            tabs.SelectedItem = item;
                            for (int j = 0; j < 5; j++)
                            {
                                Dispatcher.UIThread.RunJobs();
                            }

                            return;
                        }
                    }
                }
            }

            Assert.Fail("the Configuration window has no Telnet PAD tab");
        }

        private static void ClickApply(ConfigurationWindow window)
        {
            List<Visual> all = new List<Visual>(window.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Button button && button.Content is string text && text == "Apply")
                {
                    Assert.That(button.Command, Is.Not.Null);
                    button.Command!.Execute(button.CommandParameter);
                    Dispatcher.UIThread.RunJobs();
                    return;
                }
            }

            Assert.Fail("no Apply button");
        }

        private static void WaitUntil(Func<bool> condition, string what, int timeoutMs = 8000)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                Dispatcher.UIThread.RunJobs();
                if (condition())
                {
                    return;
                }

                Thread.Sleep(20);
            }

            Dispatcher.UIThread.RunJobs();
            Assert.That(condition(), Is.True, "gave up waiting for: " + what);
        }

        /// <summary>
        /// Connects to the telnet port and reads until the PAD prompt shows (or the time runs out).
        /// </summary>
        private static string ReadUntilPrompt(int port)
        {
            using TcpClient client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            client.ReceiveTimeout = 5000;
            NetworkStream stream = client.GetStream();
            StringBuilder sb = new StringBuilder();
            byte[] buffer = new byte[256];
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 5000 && !sb.ToString().Contains("PAD> "))
            {
                try
                {
                    int n = stream.Read(buffer, 0, buffer.Length);
                    if (n <= 0)
                    {
                        break;
                    }

                    sb.Append(Encoding.Latin1.GetString(buffer, 0, n));
                }
                catch (System.IO.IOException)
                {
                    break;
                }
            }

            return sb.ToString();
        }

        private static void AssertRefused(int port)
        {
            Assert.Throws<SocketException>(() =>
            {
                using TcpClient c = new TcpClient();
                c.Connect(IPAddress.Loopback, port);
            }, "something still listens on port " + port);
        }

        /// <summary>
        /// The tab has an Enable box, a Port box, a Bind address box and a Maximum sessions box, showing the
        /// defaults (off, 2323, 127.0.0.1, 8).
        /// </summary>
        [AvaloniaTest]
        public void SettingsTab_HasTheFields_WithTheDefaults()
        {
            RecordingDialogs dialogs = new RecordingDialogs();
            using ServiceProvider provider = BuildServices(App.CreateDefaultConfig(), dialogs);
            ConfigurationWindow window = new ConfigurationWindow(provider.GetRequiredService<ConfigurationWindowViewModel>());
            ShowAndSettle(window);
            SelectTelnetTab(window);

            CheckBox enabled = Find<CheckBox>(window, "TelnetPadEnabledCheck");
            NumericUpDown port = Find<NumericUpDown>(window, "TelnetPadPortBox");
            TextBox bind = Find<TextBox>(window, "TelnetPadBindBox");
            NumericUpDown max = Find<NumericUpDown>(window, "TelnetPadMaxSessionsBox");
            Assert.That(enabled.IsChecked, Is.False);
            Assert.That(port.Value, Is.EqualTo(2323m));
            Assert.That(bind.Text, Is.EqualTo("127.0.0.1"));
            Assert.That(max.Value, Is.EqualTo(8m));
            Assert.That(port.Minimum, Is.EqualTo(1m));
            Assert.That(port.Maximum, Is.EqualTo(65535m));
            window.Close();
        }

        /// <summary>
        /// Type values into the boxes, press Apply: the settings are saved into the configuration, the
        /// engine restarts, and a real telnet client gets the PAD prompt on the new port. Then switch it
        /// off, press Apply, and the port refuses connections.
        /// </summary>
        [AvaloniaTest]
        public void Apply_StartsTheListener_ThenSwitchingOffStopsIt()
        {
            RecordingDialogs dialogs = new RecordingDialogs();
            RuntimeConfig config = App.CreateDefaultConfig();
            using ServiceProvider provider = BuildServices(config, dialogs);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            Assert.That(runtimeService.Runtime.TelnetPadStatus.State, Is.EqualTo(TelnetPadState.Off));

            ConfigurationWindow window = new ConfigurationWindow(provider.GetRequiredService<ConfigurationWindowViewModel>());
            ShowAndSettle(window);
            SelectTelnetTab(window);

            int port = FreePort();
            Find<NumericUpDown>(window, "TelnetPadPortBox").Value = port;
            Find<NumericUpDown>(window, "TelnetPadMaxSessionsBox").Value = 3;
            Find<TextBox>(window, "TelnetPadBindBox").Text = "127.0.0.1";
            Find<CheckBox>(window, "TelnetPadEnabledCheck").IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            ClickApply(window);

            // Saved into the configuration the engine runs on.
            Assert.That(config.TelnetPad.Enabled, Is.True);
            Assert.That(config.TelnetPad.Port, Is.EqualTo(port));
            Assert.That(config.TelnetPad.MaxSessions, Is.EqualTo(3));
            Assert.That(dialogs.Warnings, Is.Empty, string.Join("; ", dialogs.Warnings));

            // The restarted engine really listens.
            Assert.That(runtimeService.Runtime.TelnetPadStatus.State, Is.EqualTo(TelnetPadState.Listening));
            Assert.That(ReadUntilPrompt(port), Does.Contain("PAD> "));
            Assert.That(Find<TextBlock>(window, "TelnetPadStatusLine").Text, Does.Contain("listening on 127.0.0.1:" + port));

            // Move it to another port: the old port stops answering, the new one answers.
            int port2 = FreePort();
            Find<NumericUpDown>(window, "TelnetPadPortBox").Value = port2;
            Dispatcher.UIThread.RunJobs();
            ClickApply(window);
            AssertRefused(port);
            Assert.That(ReadUntilPrompt(port2), Does.Contain("PAD> "));

            // Switch it off.
            Find<CheckBox>(window, "TelnetPadEnabledCheck").IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            ClickApply(window);
            AssertRefused(port2);
            Assert.That(runtimeService.Runtime.TelnetPadStatus.State, Is.EqualTo(TelnetPadState.Off));
            Assert.That(Find<TextBlock>(window, "TelnetPadStatusLine").Text, Is.EqualTo("Telnet PAD: off"));
            window.Close();
        }

        /// <summary>
        /// A port outside 1..65535 and an address that is not an IP address are refused with a sentence,
        /// and the engine is not restarted.
        /// </summary>
        [AvaloniaTest]
        public void Apply_RefusesABadPortAndABadAddress()
        {
            RecordingDialogs dialogs = new RecordingDialogs();
            RuntimeConfig config = App.CreateDefaultConfig();
            using ServiceProvider provider = BuildServices(config, dialogs);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            RetroIO.Protocols.X25.Runtime.ProtocolRuntime before = runtimeService.Runtime;

            ConfigurationWindowViewModel vm = provider.GetRequiredService<ConfigurationWindowViewModel>();
            vm.TelnetPadEnabled = true;
            vm.TelnetPadPort = 70000;
            vm.ApplySettingsCommand.Execute(null);
            Assert.That(dialogs.Warnings.Count, Is.EqualTo(1));
            Assert.That(dialogs.Warnings[0], Does.Contain("port"));
            Assert.That(runtimeService.Runtime, Is.SameAs(before), "the engine was restarted on a bad port");

            vm.TelnetPadPort = FreePort();
            vm.TelnetPadBindAddress = "not an address";
            vm.ApplySettingsCommand.Execute(null);
            Assert.That(dialogs.Warnings.Count, Is.EqualTo(2));
            Assert.That(dialogs.Warnings[1], Does.Contain("bind address"));
            Assert.That(runtimeService.Runtime, Is.SameAs(before));
        }

        /// <summary>
        /// The main window has a telnet line. It says "off", then "listening on address:port, N sessions"
        /// counting real open connections, and never leaves out the reason when the listener failed.
        /// </summary>
        [AvaloniaTest]
        public void MainWindow_ShowsOff_Listening_AndTheSessionCount()
        {
            RecordingDialogs dialogs = new RecordingDialogs();
            RuntimeConfig config = App.CreateDefaultConfig();
            int port = FreePort();
            using ServiceProvider provider = BuildServices(config, dialogs);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);

            Assert.That(Find<TextBlock>(window, "TelnetPadLine").Text, Is.EqualTo("Telnet PAD: off"));

            config.TelnetPad.Enabled = true;
            config.TelnetPad.Port = port;
            provider.GetRequiredService<IRuntimeService>().Restart(config);

            string expected = "Telnet PAD: listening on 127.0.0.1:" + port + ", 0 sessions";
            WaitUntil(() => Find<TextBlock>(window, "TelnetPadLine").Text == expected, "the main window to say " + expected);

            using TcpClient one = new TcpClient();
            one.Connect(IPAddress.Loopback, port);
            using TcpClient two = new TcpClient();
            two.Connect(IPAddress.Loopback, port);
            string expected2 = "Telnet PAD: listening on 127.0.0.1:" + port + ", 2 sessions";
            WaitUntil(() => Find<TextBlock>(window, "TelnetPadLine").Text == expected2, "the main window to say " + expected2);
            window.Close();
        }

        /// <summary>
        /// A port that is already in use: Apply says so in words, and the main window shows the failure
        /// (with the port) instead of a listener that is not there.
        /// </summary>
        [AvaloniaTest]
        public void PortInUse_IsShownInTheMainWindow_AndInTheApplyMessage()
        {
            RecordingDialogs dialogs = new RecordingDialogs();
            RuntimeConfig config = App.CreateDefaultConfig();
            using ServiceProvider provider = BuildServices(config, dialogs);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            ShowAndSettle(window);

            TcpListener occupier = new TcpListener(IPAddress.Loopback, 0);
            occupier.Start();
            try
            {
                int port = ((IPEndPoint)occupier.LocalEndpoint).Port;
                ConfigurationWindowViewModel vm = provider.GetRequiredService<ConfigurationWindowViewModel>();
                vm.TelnetPadEnabled = true;
                vm.TelnetPadPort = port;
                vm.ApplySettingsCommand.Execute(null);

                WaitUntil(() => (Find<TextBlock>(window, "TelnetPadLine").Text ?? "").StartsWith("Telnet PAD: failed: ", StringComparison.Ordinal),
                    "the main window to show the failed listen");
                string line = Find<TextBlock>(window, "TelnetPadLine").Text ?? "";
                Assert.That(line, Does.Contain("port " + port));
                Assert.That(line, Does.Contain("in use"));
                Assert.That(dialogs.Infos[dialogs.Infos.Count - 1], Does.Contain("The telnet PAD did not start"));
                Assert.That(vm.TelnetPadStatusText, Does.StartWith("Telnet PAD: failed: "));

                // The engine itself is up and carries on.
                Assert.That(provider.GetRequiredService<IRuntimeService>().IsRunning, Is.True);
            }
            finally
            {
                occupier.Stop();
            }

            window.Close();
        }

        /// <summary>
        /// A configuration file with a telnetPad section turns the listener on at start; a file without it
        /// (every older file) leaves it off.
        /// </summary>
        [AvaloniaTest]
        public void ConfigFile_WithAndWithoutTheSection()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "telnetpad-test-" + Guid.NewGuid().ToString("N") + ".json");
            int port = FreePort();
            try
            {
                System.IO.File.WriteAllText(path, "{ \"x25\": { \"localMachineId\": \"23430200\" }, \"telnetPad\": { \"enabled\": true, \"port\": " + port + ", \"maxSessions\": 4 } }");
                RuntimeConfig with = App.LoadConfigFile(path);
                Assert.That(with.TelnetPad.Enabled, Is.True);
                Assert.That(with.TelnetPad.Port, Is.EqualTo(port));
                Assert.That(with.TelnetPad.MaxSessions, Is.EqualTo(4));
                Assert.That(with.TelnetPad.BindAddress, Is.EqualTo("127.0.0.1"));

                using (ServiceProvider provider = BuildServices(with, new RecordingDialogs()))
                {
                    Assert.That(provider.GetRequiredService<IRuntimeService>().Runtime.TelnetPadStatus.State, Is.EqualTo(TelnetPadState.Listening));
                    Assert.That(ReadUntilPrompt(port), Does.Contain("PAD> "));

                    // Stopping the engine closes the telnet port.
                    provider.GetRequiredService<IRuntimeService>().Dispose();
                }

                AssertRefused(port);

                System.IO.File.WriteAllText(path, "{ \"x25\": { \"localMachineId\": \"23430200\" } }");
                RuntimeConfig without = App.LoadConfigFile(path);
                Assert.That(without.TelnetPad.Enabled, Is.False);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
