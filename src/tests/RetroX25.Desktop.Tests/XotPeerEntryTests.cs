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
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Creating a "Both ways (peer)" XOT entry in the Configuration window: the dialog offers the mode,
    /// the entry is stored, Apply restarts the engine, and the new entry really listens and really
    /// accepts a connection.
    /// </summary>
    [TestFixture]
    public sealed class XotPeerEntryTests
    {
        private readonly List<IRuntimeService> _runtimeServices = new List<IRuntimeService>();

        /// <summary>A dialog service that answers by itself and remembers what it was shown.</summary>
        private sealed class RecordingDialogs : IDialogService
        {
            public readonly List<string> Warnings = new List<string>();

            public void ShowInfo(string title, string message) { }

            public void ShowWarning(string title, string message) { Warnings.Add(message); }

            public void ShowError(string title, string message) { Warnings.Add("ERROR: " + message); }

            public System.Threading.Tasks.Task<bool> ShowConfirmation(string title, string message) => System.Threading.Tasks.Task.FromResult(true);

            public System.Threading.Tasks.Task<string?> ShowOpenFileDialog(string title, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);

            public System.Threading.Tasks.Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null) => System.Threading.Tasks.Task.FromResult<string?>(null);
        }

        private readonly RecordingDialogs _dialogs = new RecordingDialogs();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _runtimeServices.Count; i++)
            {
                _runtimeServices[i].Dispose();
            }

            _runtimeServices.Clear();
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static void WaitUntil(Func<bool> condition, string what, int timeoutMs = 10000)
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

            Assert.That(condition(), Is.True, "gave up waiting for: " + what);
        }

        private ServiceProvider BuildServices(RuntimeConfig config)
        {
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            services.AddSingleton<IDialogService>(_dialogs);
            ServiceProvider provider = services.BuildServiceProvider();
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            _runtimeServices.Add(runtimeService);
            runtimeService.Initialize(config);
            return provider;
        }

        private static Button FindButton(Visual root, string text)
        {
            List<Visual> all = new List<Visual>(root.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Button button && button.Content is string content && content == text)
                {
                    return button;
                }
            }

            Assert.Fail("no button '" + text + "'");
            throw new InvalidOperationException();
        }

        [Test]
        public void TheDialogOffersTheThreeModes_AndKeepsThePeerSettings()
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, null, true);
            Assert.That(vm.AvailableModes, Is.EqualTo(new[] { "Client", "Server", "Both ways (peer)" }));

            XotInterfaceConfig existing = new XotInterfaceConfig
            {
                Name = "P", Mode = XotMode.Both, RemoteIPAddress = "10.1.2.3", RemotePort = 1998, LocalPort = 1999, RemoteMachineId = "x",
                AllowOnlyListedPeers = true, LciBase = 5, LciCount = 9, ConnectTimeoutMs = 1234,
            };
            XotInterfaceEditDialogViewModel edit = new XotInterfaceEditDialogViewModel(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, existing, false);
            Assert.That(edit.Mode, Is.EqualTo("Both ways (peer)"));
            Assert.That(edit.AllowOnlyListedPeers, Is.True);
            edit.OkCommand.Execute(null);
            Assert.That(edit.Result, Is.Not.Null, edit.ValidationError);
            Assert.That(edit.Result!.Mode, Is.EqualTo(XotMode.Both));
            Assert.That(edit.Result.AllowOnlyListedPeers, Is.True);
            Assert.That(edit.Result.LciBase, Is.EqualTo(5), "settings without a field in the dialog are carried over, not reset");
            Assert.That(edit.Result.LciCount, Is.EqualTo(9));
            Assert.That(edit.Result.ConnectTimeoutMs, Is.EqualTo(1234));

            // A peer entry needs the peer's address: it is where we dial and who we accept.
            edit.RemoteIPAddress = "";
            edit.OkCommand.Execute(null);
            Assert.That(edit.ValidationError, Does.Contain("peer's IP address"));
        }

        /// <summary>
        /// Add in the XOT tab, choose "Both ways (peer)", press OK, then Apply: the engine restarts with a
        /// peer transport, and a real TCP connection to the entry's local port is accepted by it.
        /// </summary>
        [AvaloniaTest]
        public void CreatePeerEntryInTheWindow_Apply_ThenReallyConnectToItsListener()
        {
            RuntimeConfig config = App.CreateDefaultConfig();
            using ServiceProvider provider = BuildServices(config);
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();
            ConfigurationWindowViewModel vm = provider.GetRequiredService<ConfigurationWindowViewModel>();
            ConfigurationWindow window = new ConfigurationWindow(vm) { Width = 1000, Height = 800 };
            window.Show();
            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            int listenPort = FreePort();
            vm.XotInterfacesTab.AddInterfaceCommand.Execute(null);

            // The dialog is owned by the Configuration window.
            XotInterfaceEditDialog? dialog = null;
            WaitUntil(() =>
            {
                foreach (Window owned in window.OwnedWindows)
                {
                    if (owned is XotInterfaceEditDialog found)
                    {
                        dialog = found;
                    }
                }

                return dialog != null;
            }, "the XOT edit dialog to open");

            XotInterfaceEditDialogViewModel dialogVm = (XotInterfaceEditDialogViewModel)dialog!.DataContext!;
            dialogVm.Name = "XOT_Peer1";
            dialogVm.Enabled = true;
            dialogVm.Mode = XotInterfaceEditDialogViewModel.BothText;
            dialogVm.RemoteIPAddress = "127.0.0.1";
            dialogVm.RemotePort = FreePort();
            dialogVm.LocalPort = listenPort;
            dialogVm.RemoteMachineId = "23500000";
            dialogVm.AllowOnlyListedPeers = false;

            // OK: the command validates and makes the result, the click closes the dialog with it.
            dialogVm.OkCommand.Execute(null);
            Assert.That(dialogVm.Result, Is.Not.Null, dialogVm.ValidationError);
            FindButton(dialog, "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => config.Routing.XotInterfaces.Count == 1, "the new entry in the configuration");

            XotInterfaceConfig stored = config.Routing.XotInterfaces[0];
            Assert.That(stored.Mode, Is.EqualTo(XotMode.Both));
            Assert.That(stored.LocalPort, Is.EqualTo(listenPort));
            Assert.That(stored.Name, Is.EqualTo("XOT_Peer1"));

            // Apply restarts the engine on the new entry.
            vm.ApplySettingsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(_dialogs.Warnings, Is.Empty, string.Join("; ", _dialogs.Warnings));
            Assert.That(runtimeService.Runtime.TransportInterfaces.ContainsKey("XOT_Peer1"), Is.True);
            XotPeerTransport peer = (XotPeerTransport)runtimeService.Runtime.TransportInterfaces["XOT_Peer1"];
            Assert.That(peer.Status, Is.EqualTo(TransportStatus.Active));

            // And it really accepts: a connection to the local port, from the listed address, is taken.
            using TcpClient client = new TcpClient();
            client.Connect(IPAddress.Loopback, listenPort);
            WaitUntil(() => peer.GetPeerReport().Accepted == 1, "the peer entry to accept the connection");
            Assert.That(peer.GetPeerReport().AcceptedOpenNow, Is.EqualTo(1));
            window.Close();
        }
    }
}
