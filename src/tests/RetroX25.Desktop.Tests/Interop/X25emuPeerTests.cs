//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using Avalonia.Headless.NUnit;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Monitor;
using RetroIO.Link;
using RetroIO.Protocols.X25.Transport;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;
using RetroX25.Tests;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Our app's "Both ways (peer)" XOT entry against x25emu (the C emulator in WSL): we place calls into x25emu's
    /// switches, and x25emu's switches place calls to us, one after the other and at the same time.
    /// </summary>
    /// <remarks>
    /// <para>These tests live in the Desktop test project because x25emu dials US (inbound TCP), and the Windows
    /// firewall decides inbound connections program by program: the test host of RetroX25.Tests has a Block rule.</para>
    /// <para>x25emu's own echo answered ONE call only until it was fixed (2026-11-13; it now serves calls in a row, see X25emuEcho_TakesCallsInARow_AndAtTheSameTime). Before the fix, after the first call, Switch C offers later calls to the
    /// connection the first call closed. So every test that calls the x25emu echo (23420001) starts a FRESH echo of
    /// its own (X25emuEnvironment.StartEcho) and stops it by its recorded process number at the end.</para>
    /// <para>The addresses and ports are in docs/crossenv-testing.md. Our peer entry dials x25emu Switch A port 7
    /// (XOT, 2011); x25emu Switch A port 4 dials our entry's listening port for calls to 2351. Our built-in TAD
    /// answers every call to 2351 and echoes the data.</para>
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    public sealed class X25emuPeerTests
    {
        private const string PeerName = "XOT_X25emu";
        private const string OurAddress = "23510001";

        /// <summary>Our app as it would run: one XOT peer entry for x25emu, a route to x25emu's 234x addresses and the TAD for 2351.</summary>
        internal sealed class OurApp : IDisposable
        {
            public readonly ServiceProvider Provider;
            public readonly IRuntimeService Service;
            public readonly XotPeerTransport Peer;
            public readonly RuntimeConfig Config;

            public OurApp(int x25emuPort, XotWireTap? tap = null, string ourAddress = OurAddress, bool hdlcListener = false, string peerPattern = "234.*", XotMode mode = XotMode.Both)
            {
                Config = App.CreateDefaultConfig();
                Config.X25.LocalMachineId = ourAddress;
                Config.Routing.XotInterfaces.Clear();
                Config.Routing.XotInterfaces.Add(new XotInterfaceConfig
                {
                    Name = PeerName,
                    Enabled = true,
                    Mode = mode,
                    RemoteIPAddress = tap != null ? "127.0.0.1" : X25emuEnvironment.WslHost,
                    RemotePort = tap != null ? tap.Port : x25emuPort,
                    LocalPort = X25emuEnvironment.OurXotPort,
                    RemoteMachineId = "23400000",
                    AllowOnlyListedPeers = false,
                    ConnectTimeoutMs = 4000,
                });
                if (hdlcListener)
                {
                    // x25emu's hdlc-tcp trunk (Switch A port 6, a DTE) dials this port and starts the LAPB link; we answer it.
                    Config.Lapb.IsDCE = true;
                    Config.Routing.TcpLapbInterfaces.Add(new TcpLapbInterfaceConfig
                    {
                        Name = "HDLC_X25emu",
                        Enabled = true,
                        Mode = TcpMode.Server,
                        Port = X25emuEnvironment.OurHdlcPort,
                        RemoteMachineId = "23400000",
                        IsPrimary = false,
                        LapbAutoConnect = false,
                    });
                }

                Config.Routing.RoutingTable.Clear();
                Config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "ToX25emu", X121AddressPattern = peerPattern, Type = RetroIO.Protocols.X25.Runtime.RouteType.Interface, Destination = PeerName, Priority = 100 });
                Config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "LocalTAD", X121AddressPattern = ourAddress.Substring(0, 4) + ".*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Local, Destination = "LocalTAD", Priority = 200 });
                ServiceCollection services = new ServiceCollection();
                App.ConfigureServices(services, Config, null);
                Provider = services.BuildServiceProvider();
                Service = Provider.GetRequiredService<IRuntimeService>();
                Service.Initialize(Config);
                Peer = (XotPeerTransport)Service.Runtime.TransportInterfaces[PeerName];
            }

            public string Log()
            {
                RetroIO.Protocols.X25.Common.HistoryLoggingProvider history = Provider.GetRequiredService<RetroIO.Protocols.X25.Common.HistoryLoggingProvider>();
                IReadOnlyList<RetroIO.Protocols.X25.Common.LogEntry> entries = history.GetAllLogs();
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < entries.Count; i++)
                {
                    text.Append(entries[i].Level).Append(' ').Append(entries[i].Message).Append('\n');
                }

                return text.ToString();
            }

            public string Errors()
            {
                RetroIO.Protocols.X25.Common.HistoryLoggingProvider history = Provider.GetRequiredService<RetroIO.Protocols.X25.Common.HistoryLoggingProvider>();
                IReadOnlyList<RetroIO.Protocols.X25.Common.LogEntry> entries = history.GetAllLogs();
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Level == RetroIO.Protocols.X25.Common.LogLevel.Error)
                    {
                        text.Append(entries[i].Message).Append('\n');
                    }
                }

                return text.ToString();
            }

            public void Dispose()
            {
                Service.Dispose();
                Provider.Dispose();
            }
        }

        /// <summary>One of our PAD calls with the text it received.</summary>
        internal sealed class OurPadCall
        {
            public readonly PadClient Pad;
            private readonly StringBuilder _received = new StringBuilder();

            public OurPadCall(OurApp app, string name)
            {
                Pad = app.Service.Runtime.CreatePadClient(name);
                Pad.DataReceived += (s, e) =>
                {
                    lock (_received)
                    {
                        _received.Append(e.Data);
                    }
                };
            }

            public string Received
            {
                get
                {
                    lock (_received)
                    {
                        return _received.ToString();
                    }
                }
            }
        }

        private static int CountOf(string text, string what)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(what, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += what.Length;
            }

            return count;
        }

        /// <summary>TCP connections of this machine to the given remote address and port, by state, as Windows lists them.</summary>
        internal static int OurTcpConnections(string remoteAddress, int remotePort, int localPort = 0)
        {
            int count = 0;
            TcpConnectionInformation[] all = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
            for (int i = 0; i < all.Length; i++)
            {
                bool remoteMatches = remoteAddress.Length > 0 && all[i].RemoteEndPoint.Address.ToString() == remoteAddress && (remotePort == 0 || all[i].RemoteEndPoint.Port == remotePort);
                bool localMatches = localPort != 0 && all[i].LocalEndPoint.Port == localPort;
                if ((remoteMatches || localMatches) && all[i].State == TcpState.Established)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// We call x25emu's echo through our peer entry: one TCP connection is dialled for the call, the text comes
        /// back, and the clear closes it. Then the same again with a fresh echo (each test starts its own, so it never depends on what another test left).
        /// </summary>
        [AvaloniaTest]
        public void OurPeerEntry_CallsX25emuEcho_TwiceInARow_OneTcpConnectionPerCall()
        {
            X25emuEnvironment.RequireReady();
            using XotWireTap tap = new XotWireTap(X25emuEnvironment.WslHost!, X25emuEnvironment.XotTrunkListen);
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen, tap);
            Dictionary<int, PortStatus> before = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            for (int round = 1; round <= 2; round++)
            {
                using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
                OurPadCall call = new OurPadCall(app, "pad" + round);
                Assert.That(call.Pad.Connect(X25emuEnvironment.EchoAddress, OurAddress), Is.True);
                XotLinkHealthTests.WaitUntil(() => call.Pad.ConnectionState == PadConnectionState.Connected, "call " + round + " to be connected",
                    () => call.Received + "\nWIRE:\n" + tap.Dump() + "\n" + app.Log());
                XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialedOpenNow == 1, "one dialled connection open");
                Assert.That(OurTcpConnections(X25emuEnvironment.WslHost!, X25emuEnvironment.XotTrunkListen), Is.EqualTo(1), "Windows sees one TCP connection per call");
                Assert.That(call.Pad.SendText("ROUND-" + round + "-HELLO\r"), Is.True);
                XotLinkHealthTests.WaitUntil(() => CountOf(call.Received, "ROUND-" + round + "-HELLO") >= 1, "the text to come back", () => call.Received + "\n" + app.Log());
                call.Pad.Disconnect();
                XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialedOpenNow == 0, "the dialled connection to close");
            }

            Thread.Sleep(700);
            Dictionary<int, PortStatus> after = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            XotPeerReport report = app.Peer.GetPeerReport();
            string table = "x25emu Switch A changes:\n" + X25emuMonitor.Delta(before, after) + "dialled opened " + report.DialedOpened + " placed " + report.CallsPlaced + " failures " + report.DialFailures;
            TestContext.Progress.WriteLine(table);
            // Our packet history (what the Packet window lists) decodes what x25emu sent: the answers and the data.
            System.Text.StringBuilder history = new System.Text.StringBuilder();
            foreach (RetroIO.Protocols.X25.Models.HistoryItem item in app.Service.Runtime.GetHistory())
            {
                history.AppendLine(item.ToString());
            }

            Assert.That(history.ToString(), Does.Contain("CallAccepted").And.Contain("[RX]").And.Contain("ClearConfirmation"), "the packet history:\n" + history);
            Assert.That(report.DialedOpened, Is.EqualTo(2), table);
            Assert.That(report.CallsPlaced, Is.EqualTo(2), table);
            Assert.That(report.DialFailures, Is.EqualTo(0), table);
            Assert.That(X25emuMonitor.Rise(before, after, 7, p => p.CallsIn), Is.EqualTo(2u), table);
            Assert.That(app.Errors(), Is.Empty);
        }

        /// <summary>
        /// x25emu's PAD calls 23510001: Switch A sends the call out of its XOT trunk port 4, which dials OUR entry's
        /// listening port; our TAD answers and echoes. Counted on both sides, with the clear decoded.
        /// </summary>
        [AvaloniaTest]
        public void X25emuPad_CallsOurApp_ThroughItsXotTrunk_AndOurTadAnswers()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen);
            Dictionary<int, PortStatus> before = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "23400055");
            XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("PAD ready"), "x25emu's PAD to be ready", () => pad.Output);
            pad.Write("CALL " + OurAddress + "\r");
            XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("\nCON") || pad.Output.Contains("CLR"), "the call to be answered", () => pad.Output + "\n" + app.Log());
            Assert.That(pad.Output, Does.Contain("\nCON"), pad.Output + "\n" + app.Log());
            pad.Write("LINE-FROM-X25EMU\r");
            XotLinkHealthTests.WaitUntil(() => CountOf(pad.Output, "LINE-FROM-X25EMU") >= 1, "the line to come back", () => pad.Output + "\n" + app.Log());
            XotPeerReport during = app.Peer.GetPeerReport();
            // x25emu's dialling trunk also keeps one idle connection to us that carries no call (measured: its log says "carrying LCI 0"), so one call means one or two accepted connections open.
            Assert.That(during.AcceptedOpenNow, Is.InRange(1, 2));
            Assert.That(OurTcpConnections(string.Empty, 0, X25emuEnvironment.OurXotPort), Is.InRange(1, 2));
            pad.Write("\u0010");
            pad.Write("CLR\r");
            XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().AcceptedOpenNow <= 1, "the connection that carried the call to close (the idle one stays)", () => pad.Output + "\n" + app.Log());
            Thread.Sleep(700);
            Dictionary<int, PortStatus> after = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            XotPeerReport report = app.Peer.GetPeerReport();
            string table = "x25emu Switch A changes:\n" + X25emuMonitor.Delta(before, after) + "we accepted " + report.Accepted + " calls received " + report.CallsReceived;
            TestContext.Progress.WriteLine(table);
            TestContext.Progress.WriteLine(pad.Output);
            TestContext.Progress.WriteLine(app.Log());
            Assert.That(report.Accepted, Is.InRange(1, 2), "the call's connection, and maybe the idle one\n" + table);
            Assert.That(report.CallsReceived, Is.EqualTo(1), table);
            Assert.That(X25emuMonitor.Rise(before, after, 0, p => p.CallsIn), Is.EqualTo(1u), table);
            Assert.That(X25emuMonitor.Rise(before, after, 4, p => p.CallsOut), Is.EqualTo(1u), table);
            Assert.That(app.Errors(), Is.Empty);
        }

        /// <summary>
        /// Both sides place a call and both calls are up at the same moment: ours to x25emu's echo, x25emu's PAD to
        /// our TAD. Text goes both ways on both calls, then both are cleared.
        /// </summary>
        [AvaloniaTest]
        public void BothSidesCall_AtTheSameTime_BothCallsCarryTextAndClearCleanly()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen);
            Dictionary<int, PortStatus> before = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
            using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "23400055");
            XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("PAD ready"), "x25emu's PAD to be ready", () => pad.Output);

            // Start both calls without waiting for the first to finish.
            OurPadCall ours = new OurPadCall(app, "padBoth");
            Assert.That(ours.Pad.Connect(X25emuEnvironment.EchoAddress, OurAddress), Is.True);
            pad.Write("CALL " + OurAddress + "\r");
            XotLinkHealthTests.WaitUntil(() => ours.Pad.ConnectionState == PadConnectionState.Connected && pad.Output.Contains("\nCON"),
                "both calls to be up", () => ours.Received + "\n" + pad.Output + "\n" + app.Log());
            XotPeerReport up = app.Peer.GetPeerReport();
            Assert.That(up.DialedOpenNow, Is.EqualTo(1), "one connection we dialled");
            Assert.That(up.AcceptedOpenNow, Is.InRange(1, 2), "the connection x25emu dialled for the call, and maybe its idle one");

            for (int i = 0; i < 5; i++)
            {
                ours.Pad.SendText("OURS-" + i + "\r");
                pad.Write("THEIRS-" + i + "\r");
            }

            XotLinkHealthTests.WaitUntil(() => CountOf(ours.Received, "OURS-") >= 5 && CountOf(pad.Output, "THEIRS-") >= 5,
                "all text to come back on both calls", () => ours.Received + "\n---\n" + pad.Output);
            ours.Pad.Disconnect();
            pad.Write("\u0010");
            pad.Write("CLR\r");
            XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialedOpenNow == 0 && app.Peer.GetPeerReport().AcceptedOpenNow <= 1,
                "both connections to close", () => app.Log());
            Thread.Sleep(700);
            Dictionary<int, PortStatus> after = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            string table = "x25emu Switch A changes:\n" + X25emuMonitor.Delta(before, after);
            TestContext.Progress.WriteLine(table);
            Assert.That(X25emuMonitor.Rise(before, after, 7, p => p.CallsIn), Is.EqualTo(1u), table);
            Assert.That(X25emuMonitor.Rise(before, after, 4, p => p.CallsOut), Is.EqualTo(1u), table);
            Assert.That(X25emuMonitor.Rise(before, after, 0, p => p.CallsIn), Is.EqualTo(1u), table);
            Assert.That(app.Errors(), Is.Empty);
        }
        /// <summary>
        /// The check that x25emu can reach THIS machine: it runs in this project because the firewall decides
        /// inbound connections program by program (the other test project has a Block rule).
        /// </summary>
        [Test]
        public void TheX25emuSetup_CanReachUs_AndWeCanReachIt()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
        }

        /// <summary>
        /// x25emu dials OUR listening hdlc-tcp port: its PAD calls 23520001, Switch A sends the call out of its hdlc-tcp
        /// trunk (port 6, a DTE) which dials our TCP port 2211 and starts LAPB; our app answers the link, takes the call
        /// with its TAD and echoes. Clear cause and diagnostic are read from our packet history.
        /// </summary>
        [AvaloniaTest]
        public void X25emuPad_CallsOurApp_ThroughItsHdlcTcpTrunk_WhichDialsOurListeningPort()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen, null, "23520001", hdlcListener: true);
            Dictionary<int, PortStatus> before = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            XotLinkHealthTests.WaitUntil(() => OurTcpConnections(string.Empty, 0, X25emuEnvironment.OurHdlcPort) >= 1, "x25emu's trunk to dial our hdlc port", () => app.Log());
            using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "23400055");
            XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("PAD ready"), "x25emu's PAD to be ready", () => pad.Output);
            pad.Write("CALL 23520001\r");
            XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("\nCON") || pad.Output.Contains("CLR"), "the call to be answered", () => pad.Output + "\n" + app.Log());
            Assert.That(pad.Output, Does.Contain("\nCON"), pad.Output + "\n" + app.Log());
            pad.Write("LINE-OVER-HDLC\r");
            XotLinkHealthTests.WaitUntil(() => CountOf(pad.Output, "LINE-OVER-HDLC") >= 1, "the line to come back", () => pad.Output + "\n" + app.Log());
            pad.Write("\u0010");
            pad.Write("CLR\r");
            XotLinkHealthTests.WaitUntil(() => app.Log().Contains("Call cleared"), "our TAD to see the clear", () => pad.Output + "\n" + app.Log());
            Thread.Sleep(700);
            Dictionary<int, PortStatus> after = X25emuMonitor.Snapshot(X25emuMonitor.SwitchMonitorPorts[0]);
            string table = "x25emu Switch A changes:\n" + X25emuMonitor.Delta(before, after);
            TestContext.Progress.WriteLine(table);
            TestContext.Progress.WriteLine(app.Log());
            Assert.That(X25emuMonitor.Rise(before, after, 0, p => p.CallsIn), Is.EqualTo(1u), table);
            Assert.That(X25emuMonitor.Rise(before, after, 6, p => p.CallsOut), Is.EqualTo(1u), table);
            Assert.That(app.Errors(), Is.Empty);
        }

        /// <summary>
        /// A stream of data from x25emu into our TAD over the HDLC-over-TCP trunk (LAPB between the two): the packets are
        /// counted on both sides. (x25emu's stream tool sends as fast as the window allows.)
        /// </summary>
        [AvaloniaTest]
        public void X25emuStream_ThroughItsHdlcTcpTrunk_IsCountedExactlyByOurApp()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen, null, "23520001", hdlcListener: true);
            XotLinkHealthTests.WaitUntil(() => OurTcpConnections(string.Empty, 0, X25emuEnvironment.OurHdlcPort) >= 1, "x25emu's trunk to dial our hdlc port", () => app.Log());
            Thread.Sleep(1500);
            const int Packets = 300;
            using X25emuEnvironment.LinuxProcess traffic = new X25emuEnvironment.LinuxProcess(string.Empty,
                X25emuEnvironment.ToolPath("x25traffic"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-d", "23520001", "-n", Packets.ToString(), "-s", "128", "-r", "0");
            bool ended = traffic.WaitForExit(60000);
            string log = app.Log();
            StringBuilder interesting = new StringBuilder();
            foreach (string line in log.Split('\n'))
            {
                if (!line.StartsWith("Debug", StringComparison.Ordinal))
                {
                    interesting.AppendLine(line);
                }
            }

            TestContext.Progress.WriteLine("stream ended=" + ended + "\n" + traffic.Output + "\n" + interesting);
            Assert.That(ended, Is.True, traffic.Output);
            string? cell = RetroX25.Desktop.Services.StatisticsReader.Build(app.Service.Runtime).Cell("X.25 data packets received", "Total");
            Assert.That(cell, Is.EqualTo(Packets.ToString()), traffic.Output);
            Assert.That(app.Errors(), Is.Empty);
        }
    }
}