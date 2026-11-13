//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;
using RetroX25.Tests;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The Desktop windows and settings against x25emu (the C emulator in WSL), with nothing but the real engine behind them:
    /// the facilities two peers with different sizes put in their Call Requests, the PAD window calling x25emu's echo, the XOT
    /// panel and the Packet window showing x25emu's frames, an x25emu PAD calling our built-in TAD through x25emu's
    /// 2343 route, and a telnet PAD reaching x25emu through our built-in switch.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    public sealed class X25emuWindowsTests
    {
        private const string PeerName = "XOT_X25emu";

        private static void Pump(int ms)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
        }

        private static void Type(PADClientWindow window, string line)
        {
            window.KeyTextInput(line);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        }

        /// <summary>The Call Request / Call Accepted of one tapped connection, as the facility bytes in hex ("none" when empty).</summary>
        private static string FacilitiesOf(XotWireTap tap, int connectionIndexFromEnd, X25PacketType type, out byte[] raw)
        {
            raw = Array.Empty<byte>();
            List<TappedPacket> all = tap.Packets();
            for (int i = 0; i < all.Count; i++)
            {
                X25PacketInfo info = all[i].Info;
                if (info.PacketType == type)
                {
                    raw = info.Facilities;
                    return raw.Length == 0 ? "none" : BitConverter.ToString(raw);
                }
            }

            return "no " + type + " seen";
        }

        // ----------------------------------------------------------------------------------------
        // A2: the sizes typed for a peer reach the wire
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Two peer entries with different sizes (window 2 / packet 256, window 5 / packet 64), each behind its own wire tap into
        /// x25emu. One call each. The real Call Request bytes of each carry that peer's own pair: flow control facilities 0x42 (packet
        /// size, codes 8 = 256 and 6 = 64) and 0x43 (window size). The Packet window shows the same bytes as hex.
        /// </summary>
        [AvaloniaTest]
        public void TwoPeers_WithDifferentSizes_PutTheirOwnSizesInTheCallRequest()
        {
            X25emuEnvironment.RequireReady();
            using XotWireTap tapA = new XotWireTap(X25emuEnvironment.WslHost!, X25emuEnvironment.XotTrunkListen);
            using XotWireTap tapB = new XotWireTap(X25emuEnvironment.WslHost!, X25emuEnvironment.XotTrunkListen);
            RuntimeConfig config = App.CreateDefaultConfig();
            config.X25.LocalMachineId = "23510001";
            config.Routing.XotInterfaces.Clear();
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig { Name = "PeerA", Enabled = true, Mode = XotMode.Client, RemoteIPAddress = "127.0.0.1", RemotePort = tapA.Port, RemoteMachineId = "23400000", RequestedWindowSize = 2, RequestedPacketSize = 256, ConnectTimeoutMs = 4000 });
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig { Name = "PeerB", Enabled = true, Mode = XotMode.Client, RemoteIPAddress = "127.0.0.1", RemotePort = tapB.Port, RemoteMachineId = "23400000", RequestedWindowSize = 5, RequestedPacketSize = 64, ConnectTimeoutMs = 4000 });
            config.Routing.RoutingTable.Clear();
            config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "ToA", X121AddressPattern = "2342.*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Interface, Destination = "PeerA", Priority = 100 });
            config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "ToB", X121AddressPattern = "2341.*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Interface, Destination = "PeerB", Priority = 100 });
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            using ServiceProvider provider = services.BuildServiceProvider();
            IRuntimeService service = provider.GetRequiredService<IRuntimeService>();
            service.Initialize(config);
            try
            {
                // Peer A calls x25emu's echo (a fresh one, so the call is answered); peer B's call goes to Switch B and is
                // answered or cleared - only its Call Request is read.
                using (X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho())
                {
                    PadClient padA = service.Runtime.CreatePadClient("padA");
                    Assert.That(padA.Connect(X25emuEnvironment.EchoAddress, "23510001"), Is.True);
                    XotLinkHealthTests.WaitUntil(() => padA.ConnectionState == PadConnectionState.Connected, "call A to be connected", () => tapA.Dump());
                    padA.Disconnect();
                }

                PadClient padB = service.Runtime.CreatePadClient("padB");
                padB.Connect("23410001", "23510001");
                XotLinkHealthTests.WaitUntil(() => tapB.Packets().Count >= 2, "peer B's call to cross the wire", () => tapB.Dump());
                padB.Disconnect();

                string requestA = FacilitiesOf(tapA, 0, X25PacketType.CallRequest, out byte[] rawA);
                string requestB = FacilitiesOf(tapB, 0, X25PacketType.CallRequest, out byte[] rawB);
                TestContext.Progress.WriteLine("peer A Call Request facilities: " + requestA + "\npeer B Call Request facilities: " + requestB);
                string acceptedA = FacilitiesOf(tapA, 0, X25PacketType.CallAccepted, out _);
                TestContext.Progress.WriteLine("peer A Call Accepted facilities from x25emu: " + acceptedA);

                // Raw bytes: 42 <called-to-calling code> <calling-to-called code>, then 43 <window> <window>.
                Assert.That(requestA, Does.Contain("42-08-08").And.Contain("43-02-02"), "peer A asked for packet 256 / window 2\n" + tapA.Dump());
                Assert.That(requestB, Does.Contain("42-06-06").And.Contain("43-05-05"), "peer B asked for packet 64 / window 5\n" + tapB.Dump());

                // The same bytes are in the Packet window.
                PacketWindowViewModel packets = provider.GetRequiredService<PacketWindowViewModel>();
                Dispatcher.UIThread.RunJobs();
                bool a = false;
                bool b = false;
                StringBuilder seenRows = new StringBuilder();
                for (int i = 0; i < packets.Packets.Count; i++)
                {
                    PacketRowViewModel row = packets.Packets[i];
                    seenRows.Append(row.Direction).Append(" | ").Append(row.TypeText).Append(" | ").Append(row.L3Type).Append('\n');
                    if (row.TypeText == "CALL-REQ")
                    {
                        string detail = row.BuildDetailText();
                        a |= detail.Contains("42 08 08") && detail.Contains("43 02 02");
                        b |= detail.Contains("42 06 06") && detail.Contains("43 05 05");
                    }
                }

                Assert.That(a, Is.True, "the Packet window shows peer A's facilities\n" + seenRows + (packets.Packets.Count > 0 ? packets.Packets[0].BuildDetailText() : ""));
                Assert.That(b, Is.True, "the Packet window shows peer B's facilities");
            }
            finally
            {
                service.Dispose();
            }
        }

        // ----------------------------------------------------------------------------------------
        // B6: the windows against x25emu
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// The PAD window calls x25emu's echo and the text comes back; the XOT panel lists the x25emu peer with its
        /// dialled connection; the Packet window decodes the frames x25emu sent.
        /// </summary>
        [AvaloniaTest]
        public void PadWindow_CallsX25emuEcho_TheXotPanelShowsThePeer_AndThePacketWindowDecodesItsFrames()
        {
            X25emuEnvironment.RequireReady();
            using X25emuPeerTests.OurApp app = new X25emuPeerTests.OurApp(X25emuEnvironment.XotTrunkListen);
            using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
            MainWindowViewModel main = app.Provider.GetRequiredService<MainWindowViewModel>();
            MainWindow mainWindow = new MainWindow { DataContext = main, Width = 1000, Height = 700 };
            mainWindow.Show();
            Dispatcher.UIThread.RunJobs();

            PADClientWindowViewModel viewModel = new PADClientWindowViewModel(NullLogger<PADClientWindowViewModel>.Instance, app.Service, 1);
            PADClientWindow window = new PADClientWindow(viewModel) { Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Type(window, "CALL " + X25emuEnvironment.EchoAddress);
            XotLinkHealthTests.WaitUntil(() => viewModel.ConsoleText.Contains("COM", StringComparison.OrdinalIgnoreCase) || viewModel.ConsoleText.Contains("connected", StringComparison.OrdinalIgnoreCase),
                "the call to be connected", () => viewModel.ConsoleText + "\n" + app.Log());
            Pump(300);
            Type(window, "hello from the pad window");
            XotLinkHealthTests.WaitUntil(() => CountOf(viewModel.ConsoleText, "hello from the pad window") >= 1, "the text in the console", () => viewModel.ConsoleText + "\n" + app.Log());

            // The XOT panel: one row, named after the x25emu peer, with the connection we dialled.
            XotPeerLineViewModel? line = null;
            XotLinkHealthTests.WaitUntil(() =>
            {
                line = XotLinkHealthTests.Peer(main, PeerName);
                return line != null && line.Row.State.Length > 0;
            }, "a row for the x25emu peer", () => XotLinkHealthTests.PeerNames(main));
            TestContext.Progress.WriteLine("XOT panel row: " + line!.Row.Name + " | " + line.Row.State + " | " + line.State);
            Assert.That(line.Row.Name, Is.EqualTo(PeerName));
            Assert.That(app.Peer.GetPeerReport().DialedOpened, Is.GreaterThanOrEqualTo(1));
            Assert.That(XotLinkHealthTests.AllText(mainWindow), Does.Contain(PeerName), "the main window lists the peer");

            // The Packet window: x25emu's Call Accepted and the data it sent back, decoded.
            PacketWindowViewModel packets = app.Provider.GetRequiredService<PacketWindowViewModel>();
            Dispatcher.UIThread.RunJobs();
            bool accepted = false;
            bool data = false;
            StringBuilder rows = new StringBuilder();
            for (int i = 0; i < packets.Packets.Count; i++)
            {
                PacketRowViewModel row = packets.Packets[i];
                rows.Append(row.Direction).Append(' ').Append(row.TypeText).Append(' ').Append(row.Lcn).Append('\n');
                if (row.Direction == RetroIO.Protocols.X25.Models.Direction.RX && row.TypeText == "CALL-ACC")
                {
                    accepted = true;
                }

                if (row.Direction == RetroIO.Protocols.X25.Models.Direction.RX && row.TypeText == "DATA"
                    && row.Item.GetPayloadHex().Length > 0)
                {
                    data = true;
                }
            }

            Assert.That(accepted, Is.True, "the Packet window lists x25emu's Call Accepted\n" + rows);
            Assert.That(data, Is.True, "the Packet window lists x25emu's data packet\n" + rows);

            window.Close();
            mainWindow.Close();
            Assert.That(app.Errors(), Is.Empty);
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

        /// <summary>
        /// The built-in switch with one more trunk: it dials x25emu's access port 2007 (calls to 2343 come back down it) and
        /// sends calls for 2340..2342 up it. Callers connect to the callers port, the app's TAD sits behind the TAD port.
        /// </summary>
        private static string WriteSwitchIni(int callersPort, int tadPort, int x25emuPort, string x25emuHost)
        {
            string path = Path.Combine(Path.GetTempPath(), "x25test-x25emu-" + Guid.NewGuid().ToString("N") + ".ini");
            string ini =
                "[switch]\nname = TestSwitch\ndnic = 2350\nmgmt_addr = 23500000\n" +
                "[port.0]\ntype = access\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = " + callersPort + "\nlci_count = 64\n" +
                "[port.1]\ntype = trunk\ntransport = xot\nmode = dte\nhost = " + x25emuHost + "\nport = " + x25emuPort + "\npeer_name = X25emu\n" +
                "[port.2]\ntype = trunk\ntransport = xot\nmode = dte\nhost = 127.0.0.1\nport = " + tadPort + "\npeer_name = TAD\n" +
                "[route.0]\ntype = prefix\naddress = 2343\nprefix_len = 4\nport = 2\n" +
                "[route.1]\ntype = prefix\naddress = 2342\nprefix_len = 4\nport = 1\n" +
                "[route.2]\ntype = prefix\naddress = 2341\nprefix_len = 4\nport = 1\n" +
                "[route.3]\ntype = prefix\naddress = 2340\nprefix_len = 4\nport = 1\n" +
                "[route.4]\ntype = dnic\ndnic = 2350\nport = 0\n" +
                "[user.0]\nname = admin\npassword = admin\nlevel = 1\n";
            File.WriteAllText(path, ini);
            return path;
        }

        private static ServiceProvider BuiltInSwitchApp(string ini, int callersPort, int tadPort, bool telnet, out int telnetPort)
        {
            RuntimeConfig config = App.LoadConfigFile(XotLinkHealthTests.FindSampleFile("x25config-embedded-switch.json"));
            config.Routing.XotInterfaces[0].RemotePort = callersPort;
            config.Routing.XotInterfaces[0].LocalPort = tadPort;
            config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "ToSwitch_234x", X121AddressPattern = "234[012].*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Interface, Destination = "XOT_ToSwitch", Priority = 100 });
            telnetPort = XotLinkHealthTests.FreePort();
            if (telnet)
            {
                config.TelnetPad.Enabled = true;
                config.TelnetPad.Port = telnetPort;
                config.TelnetPad.BindAddress = "127.0.0.1";
            }

            return XotLinkHealthTests.BuildServices(config, new EmbeddedSwitchOptions { Enabled = true, ConfigFile = ini });
        }

        /// <summary>
        /// x25emu's PAD calls 23430200. Switch A sends it down its port 3, where our built-in switch holds a connection; the
        /// switch hands it to the app's built-in TAD, which answers and echoes.
        /// </summary>
        [AvaloniaTest]
        public void X25emuPad_CallsOurBuiltInTad_ThroughItsTwoThreeFourThreeRoute()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            int callers = XotLinkHealthTests.FreePort();
            int tad = XotLinkHealthTests.FreePort();
            string ini = WriteSwitchIni(callers, tad, X25emuEnvironment.CSharpAccessA, X25emuEnvironment.WslHost!);
            try
            {
                using ServiceProvider provider = BuiltInSwitchApp(ini, callers, tad, false, out _);
                Thread.Sleep(1500);
                using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                    X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "23400056");
                XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("PAD ready"), "x25emu's PAD to be ready", () => pad.Output);
                pad.Write("CALL 23430200\r");
                XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("\nCON") || pad.Output.Contains("CLR"), "the call to be answered", () => pad.Output + "\n" + XotLinkHealthTests.AppLog(provider));
                Assert.That(pad.Output, Does.Contain("\nCON"), pad.Output + "\n" + XotLinkHealthTests.AppLog(provider));
                pad.Write("LINE-FOR-THE-TAD\r");
                XotLinkHealthTests.WaitUntil(() => CountOf(pad.Output, "LINE-FOR-THE-TAD") >= 1, "the TAD's answer", () => pad.Output + "\n" + XotLinkHealthTests.AppLog(provider));
                pad.Write("\u0010");
                pad.Write("CLR\r");
                Pump(500);
                provider.GetRequiredService<IRuntimeService>().Dispose();
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// A telnet client connects to the app's telnet PAD and calls x25emu's echo; the call goes into the built-in switch,
        /// up its trunk into x25emu, and the text comes back.
        /// </summary>
        [AvaloniaTest]
        public void TelnetPad_ReachesX25emu_ThroughOurBuiltInSwitch()
        {
            X25emuEnvironment.RequireReady();
            int callers = XotLinkHealthTests.FreePort();
            int tad = XotLinkHealthTests.FreePort();
            using XotWireTap tap = new XotWireTap(X25emuEnvironment.WslHost!, X25emuEnvironment.XotTrunkListen);
            string ini = WriteSwitchIni(callers, tad, tap.Port, "127.0.0.1");
            try
            {
                using X25emuEnvironment.LinuxProcess echo = X25emuEnvironment.StartEcho();
                using ServiceProvider provider = BuiltInSwitchApp(ini, callers, tad, true, out int telnetPort);
                Thread.Sleep(1500);
                using TcpClient client = new TcpClient();
                client.Connect(IPAddress.Loopback, telnetPort);
                NetworkStream stream = client.GetStream();
                StringBuilder seen = new StringBuilder();
                Func<string, bool> waitFor = what =>
                {
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    byte[] buffer = new byte[512];
                    stream.ReadTimeout = 200;
                    while (watch.ElapsedMilliseconds < 10000)
                    {
                        if (seen.ToString().Contains(what))
                        {
                            return true;
                        }

                        try
                        {
                            int n = stream.Read(buffer, 0, buffer.Length);
                            if (n <= 0)
                            {
                                return false;
                            }

                            seen.Append(Encoding.Latin1.GetString(buffer, 0, n));
                        }
                        catch (IOException)
                        {
                        }
                    }

                    return seen.ToString().Contains(what);
                };
                Assert.That(waitFor("PAD> "), Is.True, seen.ToString());
                byte[] call = Encoding.ASCII.GetBytes("CALL " + X25emuEnvironment.EchoAddress + "\r");
                stream.Write(call, 0, call.Length);
                Assert.That(waitFor("COM"), Is.True, seen + "\nWIRE to x25emu:\n" + tap.Dump() + "\n" + XotLinkHealthTests.AppLog(provider));
                Thread.Sleep(300);
                byte[] text = Encoding.ASCII.GetBytes("TELNET-TO-X25EMU\r");
                stream.Write(text, 0, text.Length);
                Assert.That(waitFor("TELNET-TO-X25EMU"), Is.True, seen + "\n" + XotLinkHealthTests.AppLog(provider));
                provider.GetRequiredService<IRuntimeService>().Dispose();
            }
            finally
            {
                File.Delete(ini);
            }
        }
    }
}
