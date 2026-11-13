//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using RetroIO.Protocols.X25.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RetroIO.Protocols.X25.Common;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Switch.Ports;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Tests for the "XOT links" section of the main window: what the real state of each XOT peer is
    /// (TCP connected or not, since when, how many connects failed and why, how many connections ended),
    /// and the one quiet "Built-in switch" line for the app's own plumbing to its built-in switch.
    /// </summary>
    /// <remarks>
    /// Every test uses real sockets on loopback and the real engine and switch; nothing is faked inside
    /// the app. A "peer" here is a small listener that accepts or refuses TCP connections, which is all a
    /// TCP-level link health check can see.
    /// </remarks>
    [TestFixture]
    public sealed class XotLinkHealthTests
    {
        // ----------------------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------------------

        internal static string FindSampleFile(string fileName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "docs", "config-samples", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Sample configuration not found above " + AppContext.BaseDirectory, fileName);
        }

        internal static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        internal static void WaitUntil(Func<bool> condition, string what, Func<string>? detail = null, int timeoutMs = 12000)
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
            string more = detail != null ? "\n--- detail ---\n" + detail() : string.Empty;
            Assert.That(condition(), Is.True, "gave up waiting for: " + what + more);
        }

        internal static string AllText(Visual root)
        {
            StringBuilder text = new StringBuilder();
            List<Visual> all = new List<Visual>(root.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is TextBlock block)
                {
                    if (!string.IsNullOrEmpty(block.Text))
                    {
                        text.Append(block.Text).Append('\n');
                    }

                    if (block.Inlines != null)
                    {
                        for (int j = 0; j < block.Inlines.Count; j++)
                        {
                            if (block.Inlines[j] is Run run && run.Text != null)
                            {
                                text.Append(run.Text);
                            }
                        }

                        text.Append('\n');
                    }
                }
            }

            return text.ToString();
        }

        internal static ServiceProvider BuildServices(RuntimeConfig config, EmbeddedSwitchOptions? switchOptions = null)
        {
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, switchOptions);
            ServiceProvider provider = services.BuildServiceProvider();

            // Same order as the app: the engine first, then the built-in switch.
            provider.GetRequiredService<IRuntimeService>().Initialize(config);
            provider.GetRequiredService<EmbeddedSwitchService>().StartIfEnabled();
            return provider;
        }

        private static string WriteSwitchIni(int callersPort, int routerDialsInPort, int routerPort, int desktopServerPort)
        {
            string path = Path.Combine(Path.GetTempPath(), "x25test-health-" + Guid.NewGuid().ToString("N") + ".ini");
            string ini =
                "[switch]\nname = TestSwitch\ndnic = 2350\nmgmt_addr = 23500000\n" +
                "[port.0]\ntype = access\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = " + callersPort + "\nlci_count = 64\n" +
                "[port.1]\ntype = trunk\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = " + routerDialsInPort + "\npeer_name = R1\n" +
                "[port.2]\ntype = trunk\ntransport = xot\nmode = dte\nhost = 127.0.0.1\nport = " + routerPort + "\npeer_name = R1\n" +
                "[port.3]\ntype = trunk\ntransport = xot\nmode = dte\nhost = 127.0.0.1\nport = " + desktopServerPort + "\npeer_name = TAD\n" +
                "[route.3]\ntype = prefix\naddress = 2343\nprefix_len = 4\nport = 3\n" +
                "[route.1]\ntype = prefix\naddress = 2360\nprefix_len = 4\nport = 2\n" +
                "[route.0]\ntype = dnic\ndnic = 2350\nport = 0\n" +
                "[user.0]\nname = admin\npassword = admin\nlevel = 1\n";
            File.WriteAllText(path, ini);
            return path;
        }

        /// <summary>The sample for the built-in switch with its two app entries moved to free ports.</summary>
        internal static RuntimeConfig EmbeddedConfig(int callersPort, int desktopServerPort, string iniPath, out EmbeddedSwitchOptions options)
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-embedded-switch.json"));
            options = new EmbeddedSwitchOptions { Enabled = true, ConfigFile = iniPath };

            // The sample is ONE Both-ways entry now. Most tests in this file are about the older shape, a Client entry to the
            // switch's caller port and a Server entry for the switch's dial-in, so the entry is split back into that pair here.
            // (The Both-ways shape is tested with the sample as it is: XotModeDefaultTests and the built-in switch tests.)
            RetroIO.Protocols.X25.XotInterfaceConfig both = config.Routing.XotInterfaces[0];
            both.Mode = RetroIO.Protocols.X25.XotMode.Client;
            both.RemotePort = callersPort;
            RetroIO.Protocols.X25.XotInterfaceConfig server = new RetroIO.Protocols.X25.XotInterfaceConfig
            {
                Name = "XOT_FromSwitch",
                Enabled = true,
                Mode = RetroIO.Protocols.X25.XotMode.Server,
                LocalPort = desktopServerPort,
                RemoteMachineId = both.RemoteMachineId,
                Description = "The built-in switch dials in here with calls for the TAD",
            };
            config.Routing.XotInterfaces.Add(server);
            return config;
        }

        /// <summary>The sample with one XOT client entry and no built-in switch, pointing at a loopback port.</summary>
        private static RuntimeConfig ClientOnlyConfig(string host, int port)
        {
            RuntimeConfig config = App.LoadConfigFile(FindSampleFile("x25config-xot-to-switch.json"));
            // These tests are about the Client mode (a link that opens a connection for each call); the sample file itself says Both.
            config.Routing.XotInterfaces[0].Mode = RetroIO.Protocols.X25.XotMode.Client;
            config.Routing.XotInterfaces[0].RemoteIPAddress = host;
            config.Routing.XotInterfaces[0].RemotePort = port;
            return config;
        }

        internal static XotPeerLineViewModel? Peer(MainWindowViewModel main, string name)
        {
            main.RefreshXotLinks();
            for (int i = 0; i < main.XotPeers.Count; i++)
            {
                if (main.XotPeers[i].Row.Name == name)
                {
                    return main.XotPeers[i];
                }
            }

            return null;
        }

        internal static string PeerNames(MainWindowViewModel main)
        {
            main.RefreshXotLinks();
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < main.XotPeers.Count; i++)
            {
                names.Append(main.XotPeers[i].Row.Name).Append(" | ").Append(main.XotPeers[i].Row.State).Append('\n');
            }

            return names.ToString();
        }

        /// <summary>
        /// A call that needs the Client entry: a PAD call to an address the sample config routes to it. The entry opens its
        /// own TCP connection for the call. Returns the PAD (kept so the caller can hang up).
        /// </summary>
        private static PadClient PlaceCallThroughClientEntry(ServiceProvider provider, string padName, out bool placed)
        {
            PadClient pad = provider.GetRequiredService<IRuntimeService>().Runtime.CreatePadClient(padName);
            placed = pad.Connect("23500001", "99990002");
            return pad;
        }

        internal static string AppLog(ServiceProvider provider)
        {
            IReadOnlyList<LogEntry> entries = provider.GetRequiredService<HistoryLoggingProvider>().GetAllLogs();
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                text.Append(entries[i].Level).Append(' ').Append(entries[i].Message).Append('\n');
            }

            return text.ToString();
        }

        private static bool LogHas(ServiceProvider provider, RetroIO.Protocols.X25.Common.LogLevel level, string part)
        {
            IReadOnlyList<LogEntry> entries = provider.GetRequiredService<HistoryLoggingProvider>().GetAllLogs();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Level == level && entries[i].Message.Contains(part, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A TCP peer on loopback: accepts connections and keeps them, until told to close them or stop.
        /// </summary>
        private sealed class TcpPeer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly List<TcpClient> _clients = new List<TcpClient>();
            private readonly Thread _thread;
            private volatile bool _stop;

            public TcpPeer(int port = 0)
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(Run) { IsBackground = true, Name = "tcp-peer" };
                _thread.Start();
            }

            public int Port { get; }

            public int Accepted
            {
                get { lock (_clients) { return _clients.Count; } }
            }

            private void Run()
            {
                while (!_stop)
                {
                    try
                    {
                        TcpClient client = _listener.AcceptTcpClient();
                        lock (_clients)
                        {
                            _clients.Add(client);
                        }
                    }
                    catch (SocketException)
                    {
                        // The listener was stopped while waiting: this thread is done.
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                }
            }

            /// <summary>Closes every connection the peer holds, as a peer that hangs up would.</summary>
            public void CloseConnections()
            {
                lock (_clients)
                {
                    for (int i = 0; i < _clients.Count; i++)
                    {
                        _clients[i].Close();
                    }

                    _clients.Clear();
                }
            }

            public void Dispose()
            {
                _stop = true;
                _listener.Stop();
                CloseConnections();
            }
        }

        // ----------------------------------------------------------------------------------------
        // (a) (b) (c): a lazy Desktop client link to an external peer
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Peer down: the row says not connected, the failed count grows with each try, and the last error
        /// text is shown. The Warning in the log says there is no automatic retry. Then the peer comes up
        /// and the next try connects (b), and when the peer hangs up the disconnect is counted (c).
        /// </summary>
        [AvaloniaTest]
        public void LazyClient_PeerDown_ThenUp_ThenHangsUp_ShowsTheRealState()
        {
            int port = FreePort();
            RuntimeConfig config = ClientOnlyConfig("127.0.0.1", port);
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            window.Width = 1000;
            window.Height = 700;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Nothing has needed the link yet: not connected, nothing wrong, and it says it opens at a call.
            XotPeerLineViewModel? line = Peer(main, "XOT_ToSwitch");
            Assert.That(line, Is.Not.Null, "no row for the XOT client entry\n" + PeerNames(main));
            Assert.That(line!.Row.State, Does.StartWith("not connected yet"));
            Assert.That(line.Row.LastConnected, Is.EqualTo("never"));
            Assert.That(line.Row.Behaviour, Is.EqualTo("opens a new TCP connection for each call, no automatic retry"));
            Assert.That(main.HasBuiltInLine, Is.False, "an XOT client entry with no built-in switch must not make the quiet line");

            // (a) a call needs the link and the peer is down: the connect fails, and fails again at the next call.
            PlaceCallThroughClientEntry(provider, "p1", out bool placed1);
            Assert.That(placed1, Is.False, "the call cannot be placed while the peer is down");
            line = Peer(main, "XOT_ToSwitch");
            Assert.That(line!.Row.State, Does.StartWith("NOT connected: "));
            Assert.That(line.Row.Health, Is.EqualTo(LinkHealth.Down));
            Assert.That(line.Row.FailedConnects, Is.EqualTo(1));
            Assert.That(line.Row.LastError, Is.Not.Empty);
            Assert.That(line.State, Does.Contain(line.Row.LastError));

            PlaceCallThroughClientEntry(provider, "p2", out bool placed2);
            Assert.That(placed2, Is.False);
            Assert.That(Peer(main, "XOT_ToSwitch")!.Row.FailedConnects, Is.EqualTo(2), "the failed count must grow");

            Assert.That(LogHas(provider, RetroIO.Protocols.X25.Common.LogLevel.Warning, "No automatic retry"), Is.True,
                "the failure must reach the log as a Warning that says there is no retry\n" + AppLog(provider));
            Assert.That(LogHas(provider, RetroIO.Protocols.X25.Common.LogLevel.Info, "opening a TCP connection"), Is.True,
                "opening the connection must reach the log as Information\n" + AppLog(provider));

            // The window shows the same words.
            Dispatcher.UIThread.RunJobs();
            string shown = AllText(window);
            Assert.That(shown, Does.Contain("Trunk links"));
            Assert.That(shown, Does.Contain("NOT connected: "));
            Assert.That(shown, Does.Contain("opens a new TCP connection for each call, no automatic retry"));

            // (b) the peer comes up: the next call connects.
            using (TcpPeer peer = new TcpPeer(port))
            {
                PlaceCallThroughClientEntry(provider, "p3", out bool placed3);
                Assert.That(placed3, Is.True, "the call to a listening peer must be placed (the peer never answers it, the connection stays)");
                WaitUntil(() => Peer(main, "XOT_ToSwitch")!.Row.State == "TCP connected", "the row to say TCP connected", () => PeerNames(main));
                line = Peer(main, "XOT_ToSwitch");
                Assert.That(line!.Row.Health, Is.EqualTo(LinkHealth.Up));
                Assert.That(line.Row.LastConnected, Is.Not.EqualTo("never"));
                Assert.That(line.Row.FailedConnects, Is.EqualTo(2), "the earlier failures stay counted");
                WaitUntil(() => peer.Accepted >= 1, "the peer to see the connection (the window also tests the far end with short connections of its own, so not exactly 1)", () => "accepted " + peer.Accepted + " | " + AppLog(provider));

                // (c) the peer hangs up: the disconnect is counted and the row says not connected now.
                peer.CloseConnections();
                WaitUntil(() => Peer(main, "XOT_ToSwitch")!.Row.Disconnects == 1, "the disconnect to be counted", () => PeerNames(main) + AppLog(provider));
                line = Peer(main, "XOT_ToSwitch");
                Assert.That(line!.Row.State, Does.StartWith("not connected now"));
                Assert.That(LogHas(provider, RetroIO.Protocols.X25.Common.LogLevel.Warning, "ended while a call was still up"), Is.True, AppLog(provider));
            }

            window.Close();
        }

        /// <summary>
        /// The log messages of the links reach the Log window with the right level: a failed connect is a
        /// Warning, opening a connection is Information.
        /// </summary>
        [AvaloniaTest]
        public void LinkMessages_ReachTheLogWindow_WithTheRightLevels()
        {
            int port = FreePort();
            using ServiceProvider provider = BuildServices(ClientOnlyConfig("127.0.0.1", port));
            PlaceCallThroughClientEntry(provider, "p1", out bool placed);
            Assert.That(placed, Is.False);

            LogWindowViewModel log = provider.GetRequiredService<LogWindowViewModel>();
            bool warning = false;
            bool info = false;
            for (int i = 0; i < log.LogEntries.Count; i++)
            {
                LogEntry entry = log.LogEntries[i];
                if (entry.Level == RetroIO.Protocols.X25.Common.LogLevel.Warning && entry.Message.Contains("could not connect"))
                {
                    warning = true;
                }

                if (entry.Level == RetroIO.Protocols.X25.Common.LogLevel.Info && entry.Message.Contains("opening a TCP connection"))
                {
                    info = true;
                }
            }

            Assert.That(warning, Is.True, "no Warning about the failed connect in the Log window\n" + AppLog(provider));
            Assert.That(info, Is.True, "no Information about opening the connection in the Log window\n" + AppLog(provider));
        }

        // ----------------------------------------------------------------------------------------
        // Switch DTE port to an external peer (loud row, redials)
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// A trunk port of the built-in switch that dials a real peer is a normal loud row. With the peer
        /// down it says not connected, counts the failed dials, shows the last error and says it redials every
        /// 2 s; when the peer comes up it connects by itself; when the peer hangs up the disconnect is counted.
        /// </summary>
        [AvaloniaTest]
        public void SwitchTrunkToARealPeer_IsALoudRow_ThatRedialsAndRecovers()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            int peerPort = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), peerPort, desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                MainWindow window = new MainWindow { DataContext = main };
                window.Width = 1000;
                window.Height = 700;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // Peer down: the dial fails and is tried again every two seconds, so the count grows by itself.
                WaitUntil(() => FindPort(main, 2) != null && FindPort(main, 2)!.Row.FailedConnects >= 2, "two failed dials",
                    () => PeerNames(main) + AppLog(provider), 15000);
                XotPeerLineViewModel dial = FindPort(main, 2)!;
                Assert.That(dial.Row.Name, Is.EqualTo("R1"));
                Assert.That(dial.Row.Direction, Is.EqualTo("we dial"));
                Assert.That(dial.Row.Peer, Is.EqualTo("127.0.0.1:" + peerPort));
                Assert.That(dial.Row.State, Does.StartWith("NOT connected: "));
                Assert.That(dial.Row.Health, Is.EqualTo(LinkHealth.Down));
                Assert.That(dial.Row.LastError, Is.Not.Empty);
                Assert.That(dial.Row.Behaviour, Is.EqualTo("redials every 2 s until it connects"));
                Assert.That(dial.Row.LastConnected, Is.EqualTo("never"));
                Assert.That(LogHas(provider, RetroIO.Protocols.X25.Common.LogLevel.Warning, "the next try is in 2 seconds"), Is.True,
                    "the Warning must say when the next try is\n" + AppLog(provider));

                Dispatcher.UIThread.RunJobs();
                string shown = AllText(window);
                Assert.That(shown, Does.Contain("redials every 2 s until it connects"));
                Assert.That(shown, Does.Contain("we dial 127.0.0.1:" + peerPort));

                // Peer up: the next redial connects.
                using (TcpPeer peer = new TcpPeer(peerPort))
                {
                    WaitUntil(() => FindPort(main, 2)!.Row.State == "TCP connected", "the dial to connect", () => PeerNames(main) + AppLog(provider), 15000);
                    Assert.That(FindPort(main, 2)!.Row.Health, Is.EqualTo(LinkHealth.Up));
                    Assert.That(FindPort(main, 2)!.Row.LastConnected, Is.Not.EqualTo("never"));
                    Assert.That(FindPort(main, 2)!.Row.FailedConnects, Is.GreaterThanOrEqualTo(2));

                    // The peer hangs up: the disconnect is counted.
                    peer.CloseConnections();
                    WaitUntil(() => FindPort(main, 2)!.Row.Disconnects >= 1, "the disconnect to be counted", () => PeerNames(main) + AppLog(provider));
                    Assert.That(LogHas(provider, RetroIO.Protocols.X25.Common.LogLevel.Warning, "connection down"), Is.True, AppLog(provider));
                }

                window.Close();
            }
            finally
            {
                File.Delete(ini);
            }
        }

        private static XotPeerLineViewModel? FindPort(MainWindowViewModel main, int portId)
        {
            main.RefreshXotLinks();
            for (int i = 0; i < main.XotPeers.Count; i++)
            {
                if (main.XotPeers[i].Row.Source == "switch port " + portId)
                {
                    return main.XotPeers[i];
                }
            }

            return null;
        }

        // ----------------------------------------------------------------------------------------
        // (d) The built-in switch pair as one quiet line
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// With the built-in switch on, the app's two loopback entries (to the switch, from the switch) and
        /// the two switch ports at their other ends are ONE quiet line, "Built-in switch: OK". The trunk
        /// ports to a real peer stay as normal rows. The Desktop entries are not rows.
        /// </summary>
        [AvaloniaTest]
        public void BuiltInSwitch_IsOneQuietLine_AndTrunksToRealPeersStayRows()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                MainWindow window = new MainWindow { DataContext = main };
                window.Width = 1000;
                window.Height = 700;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // The switch's dialling port needs a moment to reach the app's listener.
                WaitUntil(() => { main.RefreshXotLinks(); return main.HasBuiltInLine && main.BuiltInLine == "Built-in switch: OK"; },
                    "the quiet line to say OK", () => main.BuiltInLine + "\n" + PeerNames(main) + AppLog(provider));
                Assert.That(main.BuiltInBrush, Is.Not.EqualTo(Avalonia.Media.Brushes.Red), "OK must be quiet, not red");

                Assert.That(Peer(main, "XOT_ToSwitch"), Is.Null, "the app's link to the switch must be folded into the quiet line");
                Assert.That(Peer(main, "XOT_FromSwitch"), Is.Null, "the app's link from the switch must be folded into the quiet line");
                Assert.That(FindPort(main, 0), Is.Null, "switch port 0 is the other end of the app's link, folded");
                Assert.That(FindPort(main, 3), Is.Null, "switch port 3 is the other end of the app's link, folded");
                Assert.That(FindPort(main, 1), Is.Not.Null, "the trunk where the real peer dials in is a normal row\n" + PeerNames(main));
                Assert.That(FindPort(main, 2), Is.Not.Null, "the trunk that dials the real peer is a normal row\n" + PeerNames(main));
                Assert.That(FindPort(main, 1)!.Row.Direction, Is.EqualTo("we listen"));
                Assert.That(FindPort(main, 2)!.Row.Direction, Is.EqualTo("we dial"));

                Dispatcher.UIThread.RunJobs();
                string shown = AllText(window);
                Assert.That(shown, Does.Contain("Built-in switch: OK"));
                Assert.That(shown, Does.Not.Contain("XOT_FromSwitch (Desktop link)"));
                window.Close();
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// The quiet line turns red with the reason when the app's listener for the switch stops: the switch's
        /// dialling port cannot reach it any more.
        /// </summary>
        [AvaloniaTest]
        public void BuiltInSwitch_TurnsRedWithAReason_WhenTheAppsListenerStops()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                WaitUntil(() => { main.RefreshXotLinks(); return main.BuiltInLine == "Built-in switch: OK"; }, "OK first", () => main.BuiltInLine + AppLog(provider));

                provider.GetRequiredService<IRuntimeService>().Runtime.TransportInterfaces["XOT_FromSwitch"].Stop();

                WaitUntil(() => { main.RefreshXotLinks(); return main.BuiltInLine.StartsWith("Built-in switch: PROBLEM", StringComparison.Ordinal); },
                    "the quiet line to turn into a problem", () => main.BuiltInLine + "\n" + AppLog(provider));
                Assert.That(main.BuiltInLine, Does.Contain("link from the switch is not running"));
                Assert.That(main.BuiltInBrush, Is.EqualTo(Avalonia.Media.Brushes.Red));
                Assert.That(main.HasBuiltInLine, Is.True);
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// The quiet line turns red with the reason when a built-in switch port stops.
        /// </summary>
        [AvaloniaTest]
        public void BuiltInSwitch_TurnsRedWithAReason_WhenAnEmbeddedPortFails()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                WaitUntil(() => { main.RefreshXotLinks(); return main.BuiltInLine == "Built-in switch: OK"; }, "OK first", () => main.BuiltInLine + AppLog(provider));

                EmbeddedSwitchService embedded = provider.GetRequiredService<EmbeddedSwitchService>();
                ISwitchPort? access = null;
                for (int i = 0; i < embedded.RunningPorts.Count; i++)
                {
                    if (embedded.RunningPorts[i].Id == 0)
                    {
                        access = embedded.RunningPorts[i];
                    }
                }

                Assert.That(access, Is.Not.Null);
                access!.Stop();

                WaitUntil(() => { main.RefreshXotLinks(); return main.BuiltInLine.StartsWith("Built-in switch: PROBLEM", StringComparison.Ordinal); },
                    "the quiet line to turn into a problem", () => main.BuiltInLine + "\n" + AppLog(provider));
                Assert.That(main.BuiltInLine, Does.Contain("switch port 0 is not running"));
                Assert.That(main.BuiltInBrush, Is.EqualTo(Avalonia.Media.Brushes.Red));
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// When the built-in switch cannot start (a port is already in use), the quiet line is red and gives
        /// that reason; the app's entries still belong to it.
        /// </summary>
        [AvaloniaTest]
        public void BuiltInSwitch_TurnsRedWithTheReason_WhenItCannotStart()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);

            // Somebody else already listens on the callers port, so port 0 cannot start.
            using TcpPeer squatter = new TcpPeer(callers);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                main.RefreshXotLinks();

                Assert.That(provider.GetRequiredService<EmbeddedSwitchService>().IsRunning, Is.False);
                Assert.That(main.HasBuiltInLine, Is.True);
                Assert.That(main.BuiltInLine, Does.StartWith("Built-in switch: PROBLEM - "));
                Assert.That(main.BuiltInLine, Does.Contain("did not start"));
                Assert.That(main.BuiltInBrush, Is.EqualTo(Avalonia.Media.Brushes.Red));
            }
            finally
            {
                File.Delete(ini);
            }
        }

        // ----------------------------------------------------------------------------------------
        // (e) An external switch is never folded away
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// With no built-in switch, the XOT entries that point at an external switch (even on loopback) are
        /// normal loud rows and there is no quiet line.
        /// </summary>
        [AvaloniaTest]
        public void ExternalSwitch_BuiltInOff_ShowsNormalRows_AndNoQuietLine()
        {
            using TcpPeer externalSwitch = new TcpPeer();
            RuntimeConfig config = ClientOnlyConfig("127.0.0.1", externalSwitch.Port);
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            MainWindow window = new MainWindow { DataContext = main };
            window.Width = 1000;
            window.Height = 700;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            XotPeerLineViewModel? line = Peer(main, "XOT_ToSwitch");
            Assert.That(line, Is.Not.Null);
            Assert.That(line!.Row.Source, Is.EqualTo("Desktop link"));
            Assert.That(main.HasBuiltInLine, Is.False);
            Assert.That(main.BuiltInLine, Is.Empty);

            Dispatcher.UIThread.RunJobs();
            string shown = AllText(window);
            Assert.That(shown, Does.Contain("XOT_ToSwitch (Desktop link)"));
            Assert.That(shown, Does.Not.Contain("Built-in switch"));
            window.Close();
        }

        /// <summary>
        /// With the built-in switch on, a Desktop entry that points to a loopback port that is NOT one of the
        /// switch's ports, or to an address that is not loopback, is an external link: a normal loud row. The
        /// other entry, which does belong to the built-in switch, still makes the quiet line.
        /// </summary>
        [AvaloniaTest]
        public void ExternalSwitch_BuiltInOn_AnEntryNotForTheBuiltInSwitch_StaysALoudRow()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);

                // First the client entry points to a loopback port no built-in port listens on.
                config.Routing.XotInterfaces[0].RemotePort = FreePort();
                using (ServiceProvider provider = BuildServices(config, options))
                {
                    MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                    Assert.That(Peer(main, "XOT_ToSwitch"), Is.Not.Null, "loopback, but not a built-in port: must be a loud row\n" + PeerNames(main));
                    Assert.That(Peer(main, "XOT_FromSwitch"), Is.Null, "the server entry does belong to the built-in switch");
                    Assert.That(main.HasBuiltInLine, Is.True);
                }

                // Then it points to an address that is not loopback at all.
                config.Routing.XotInterfaces[0].RemoteIPAddress = "192.0.2.77";
                config.Routing.XotInterfaces[0].RemotePort = callers;
                using (ServiceProvider provider = BuildServices(config, options))
                {
                    MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                    XotPeerLineViewModel? line = Peer(main, "XOT_ToSwitch");
                    Assert.That(line, Is.Not.Null, "not loopback: must be a loud row\n" + PeerNames(main));
                    Assert.That(line!.Row.Peer, Is.EqualTo("192.0.2.77:" + callers));
                }
            }
            finally
            {
                File.Delete(ini);
            }
        }

        // ----------------------------------------------------------------------------------------
        // A "Both ways" (peer) entry: one entry that dials out per call and accepts from the same peer
        // ----------------------------------------------------------------------------------------

        private static TcpClient ConnectFrom(string sourceIp, int port)
        {
            TcpClient client = new TcpClient(new IPEndPoint(IPAddress.Parse(sourceIp), 0));
            client.Connect(IPAddress.Loopback, port);
            return client;
        }

        private static int CountRows(MainWindowViewModel main, string name)
        {
            main.RefreshXotLinks();
            int count = 0;
            for (int i = 0; i < main.XotPeers.Count; i++)
            {
                if (main.XotPeers[i].Row.Name == name)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// ONE row for a peer entry shows both directions: what was dialled out and what was accepted in,
        /// counted apart. A connection from an address nobody lists gets its own row "unlisted peer".
        /// </summary>
        [AvaloniaTest]
        public void PeerEntry_IsOneRow_WithBothDirections_AndAnUnlistedPeerRow()
        {
            using TcpPeer peer = new TcpPeer();
            RuntimeConfig config = ClientOnlyConfig("127.0.0.1", peer.Port);
            config.Routing.XotInterfaces[0].Mode = RetroIO.Protocols.X25.XotMode.Both;
            config.Routing.XotInterfaces[0].LocalPort = FreePort();
            int listenPort = config.Routing.XotInterfaces[0].LocalPort;
            using ServiceProvider provider = BuildServices(config);
            MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
            IRuntimeService runtimeService = provider.GetRequiredService<IRuntimeService>();

            XotPeerLineViewModel? line = Peer(main, "XOT_ToSwitch");
            Assert.That(line, Is.Not.Null, PeerNames(main));
            Assert.That(line!.Row.Direction, Is.EqualTo("dial out + accept in"));
            Assert.That(line.Row.State, Does.Contain("nothing connected yet"));
            Assert.That(CountRows(main, "XOT_ToSwitch"), Is.EqualTo(1), "one row per peer, not one per direction");

            // A call dials out: one TCP connection to the peer, shown as the dialling direction.
            RetroIO.Protocols.X25.Modules.PadClient pad = runtimeService.Runtime.CreatePadClient("pad1");
            pad.Connect("23500001", "23430001");
            // The main window also tests the far end with short connections of its own (as for a Client link), so the peer may see more than one.
                        WaitUntil(() => peer.Accepted >= 1, "the peer to see the dialled connection", () => PeerNames(main) + AppLog(provider));
            WaitUntil(() => Peer(main, "XOT_ToSwitch")!.Row.State.StartsWith("TCP connected: 1 dialled out, 0 accepted in", StringComparison.Ordinal),
                "the row to show the dialled connection", () => PeerNames(main));
            Assert.That(Peer(main, "XOT_ToSwitch")!.Row.Detail, Does.Contain("dialled out: 1 opened, 1 open now"));

            // The listed peer dials in: counted in the accepting direction of the SAME row.
            using TcpClient listed = ConnectFrom("127.0.0.1", listenPort);
            WaitUntil(() => Peer(main, "XOT_ToSwitch")!.Row.State.StartsWith("TCP connected: 1 dialled out, 1 accepted in", StringComparison.Ordinal),
                "the row to show the accepted connection too", () => PeerNames(main));
            Assert.That(Peer(main, "XOT_ToSwitch")!.Row.Detail, Does.Contain("accepted in: 1 connections, 1 open now"));
            Assert.That(CountRows(main, "XOT_ToSwitch"), Is.EqualTo(1));

            // An address nobody lists: accepted by default, and shown as its own row.
            using TcpClient stranger = ConnectFrom("127.0.0.2", listenPort);
            WaitUntil(() => Peer(main, "unlisted peer 127.0.0.2") != null, "a row for the unlisted peer", () => PeerNames(main));
            XotPeerLineViewModel unlisted = Peer(main, "unlisted peer 127.0.0.2")!;
            Assert.That(unlisted.Row.State, Does.Contain("TCP connected, 1 open"));
            Assert.That(unlisted.Row.Source, Does.Contain("XOT_ToSwitch"));
        }

        /// <summary>
        /// The built-in switch pair may now be ONE "Both ways" entry: it dials the switch's caller port and
        /// the switch's dialling port comes back to it. It becomes the one quiet line, and the entry is no row.
        /// </summary>
        [AvaloniaTest]
        public void BuiltInSwitch_PairAsOneBothWaysEntry_IsOneQuietLine()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                config.Routing.XotInterfaces[0].Name = "XOT_Switch";
                config.Routing.XotInterfaces[0].Mode = RetroIO.Protocols.X25.XotMode.Both;
                config.Routing.XotInterfaces[0].LocalPort = desktopServer;
                config.Routing.XotInterfaces.RemoveAt(1);
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();

                WaitUntil(() => { main.RefreshXotLinks(); return main.HasBuiltInLine && main.BuiltInLine == "Built-in switch: OK"; },
                    "the quiet line to say OK", () => main.BuiltInLine + "\n" + PeerNames(main) + AppLog(provider));
                Assert.That(Peer(main, "XOT_Switch"), Is.Null, "the one peer entry stands for the built-in pair and is no row");
                Assert.That(FindPort(main, 0), Is.Null, "switch port 0 is the entry's dialling end, folded");
                Assert.That(FindPort(main, 3), Is.Null, "switch port 3 is the entry's listening end, folded");
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// A "Both ways" entry that dials an external address stays loud even though the built-in switch's
        /// dialling port comes back to its listening port: the external scenario must not be hidden.
        /// </summary>
        [AvaloniaTest]
        public void BuiltInSwitch_BothWaysEntryToAnExternalAddress_StaysALoudRow()
        {
            int callers = FreePort();
            int desktopServer = FreePort();
            string ini = WriteSwitchIni(callers, FreePort(), FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                config.Routing.XotInterfaces[0].Name = "XOT_Switch";
                config.Routing.XotInterfaces[0].Mode = RetroIO.Protocols.X25.XotMode.Both;
                config.Routing.XotInterfaces[0].RemoteIPAddress = "192.0.2.77";
                config.Routing.XotInterfaces[0].LocalPort = desktopServer;
                config.Routing.XotInterfaces.RemoveAt(1);
                using ServiceProvider provider = BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();

                Assert.That(Peer(main, "XOT_Switch"), Is.Not.Null, "an entry that dials an external address is a loud row\n" + PeerNames(main));
                Assert.That(main.HasBuiltInLine, Is.False, "no part of the pair is a built-in one now");
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// The reader needs no window: loopback is recognised by address, not by name.
        /// </summary>
        [Test]
        public void LoopbackIsRecognisedByAddress()
        {
            Assert.That(XotLinkHealthReader.IsLoopbackHost("127.0.0.1"), Is.True);
            Assert.That(XotLinkHealthReader.IsLoopbackHost("127.5.5.5"), Is.True);
            Assert.That(XotLinkHealthReader.IsLoopbackHost("::1"), Is.True);
            Assert.That(XotLinkHealthReader.IsLoopbackHost("localhost"), Is.True);
            Assert.That(XotLinkHealthReader.IsLoopbackHost("192.168.1.155"), Is.False);
            Assert.That(XotLinkHealthReader.IsLoopbackHost("0.0.0.0"), Is.False);
            Assert.That(XotLinkHealthReader.IsLoopbackHost(""), Is.False);
            Assert.That(XotLinkHealthReader.IsLoopbackHost(null), Is.False);
            Assert.That(XotLinkHealthReader.ListensOnLoopback("0.0.0.0"), Is.True);
            Assert.That(XotLinkHealthReader.ListensOnLoopback("192.168.1.180"), Is.False);
        }
    }
}
