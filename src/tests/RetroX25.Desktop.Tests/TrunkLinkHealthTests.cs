//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Threading;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Ports;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Tests for the HDLC trunks in the main window's "Trunk links" section. Same method as
    /// <see cref="XotLinkHealthTests"/>: the real engine, the real built-in switch and real sockets on loopback.
    /// The other end of each trunk is a plain HDLC port started by the test, which is all a trunk peer is.
    /// </summary>
    /// <remarks>
    /// The serial trunk row is NOT covered here: the built-in switch opens a real serial device, and this machine has
    /// no virtual serial pair to give it. The serial port's own behaviour is tested in the RetroIO test project over
    /// an in-memory line.
    /// </remarks>
    [TestFixture]
    public sealed class TrunkLinkHealthTests
    {
        /// <summary>
        /// The switch file: port 0 is the access port the app dials, port 1 an HDLC trunk that listens (the peer
        /// dials in), port 2 an HDLC trunk that dials the peer, port 3 the XOT port that dials the app's listener.
        /// </summary>
        private static string WriteSwitchIni(int callersPort, int listenPort, int dialPort, int desktopServerPort)
        {
            string path = Path.Combine(Path.GetTempPath(), "x25test-trunk-" + Guid.NewGuid().ToString("N") + ".ini");
            string ini =
                "[switch]\nname = TestSwitch\ndnic = 2350\nmgmt_addr = 23500000\n" +
                "[port.0]\ntype = access\ntransport = xot\nmode = dce\nhost = 127.0.0.1\nport = " + callersPort + "\nlci_count = 64\n" +
                "[port.1]\ntype = trunk\ntransport = hdlc-tcp\nmode = dce\nhost = 127.0.0.1\nport = " + listenPort + "\npeer_name = LEASED-IN\nlapb_t1 = 500\n" +
                "[port.2]\ntype = trunk\ntransport = hdlc-tcp\nmode = dte\nhost = 127.0.0.1\nport = " + dialPort + "\npeer_name = LEASED-OUT\nlapb_t1 = 500\n" +
                "[port.3]\ntype = trunk\ntransport = xot\nmode = dte\nhost = 127.0.0.1\nport = " + desktopServerPort + "\npeer_name = TAD\n" +
                "[route.3]\ntype = prefix\naddress = 2343\nprefix_len = 4\nport = 3\n" +
                "[route.1]\ntype = prefix\naddress = 2360\nprefix_len = 4\nport = 2\n" +
                "[route.0]\ntype = dnic\ndnic = 2350\nport = 0\n" +
                "[user.0]\nname = admin\npassword = admin\nlevel = 1\n";
            File.WriteAllText(path, ini);
            return path;
        }

        private static PortConfig PeerConfig(int id, PortMode mode, string host, int port)
        {
            return new PortConfig
            {
                Id = id,
                Type = PortType.Trunk,
                Transport = PortTransport.HdlcTcp,
                Mode = mode,
                Host = host,
                Port = (ushort)port,
                LciBase = 1,
                LciCount = 16,
                DefaultWindowSize = 7,
                LapbT1Ms = 500,
            };
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

        /// <summary>
        /// A trunk port that dials, with no peer: the row says not connected, counts the failed dials, shows the
        /// last error and the redial time. When the peer comes up the LAPB link comes up and the row turns green
        /// with the link counters; when the peer goes away the row turns red again.
        /// </summary>
        [AvaloniaTest]
        public void HdlcTrunkThatDials_ShowsFailedDials_ThenLinkUp_ThenDownWhenThePeerStops()
        {
            int callers = XotLinkHealthTests.FreePort();
            int desktopServer = XotLinkHealthTests.FreePort();
            int peerPort = XotLinkHealthTests.FreePort();
            string ini = WriteSwitchIni(callers, XotLinkHealthTests.FreePort(), peerPort, desktopServer);
            try
            {
                RuntimeConfig config = XotLinkHealthTests.EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = XotLinkHealthTests.BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                MainWindow window = new MainWindow { DataContext = main };
                window.Width = 1000;
                window.Height = 700;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // Peer down: the dial fails and is tried again every two seconds.
                XotLinkHealthTests.WaitUntil(() => FindPort(main, 2) != null && FindPort(main, 2)!.Row.FailedConnects >= 2, "two failed dials",
                    () => XotLinkHealthTests.PeerNames(main) + XotLinkHealthTests.AppLog(provider), 15000);
                XotPeerLineViewModel dial = FindPort(main, 2)!;
                Assert.That(dial.Row.Name, Is.EqualTo("LEASED-OUT"));
                Assert.That(dial.Row.Direction, Is.EqualTo("we dial"));
                Assert.That(dial.Row.Peer, Is.EqualTo("127.0.0.1:" + peerPort));
                Assert.That(dial.Row.State, Does.StartWith("NOT connected: "));
                Assert.That(dial.Row.Health, Is.EqualTo(LinkHealth.Down));
                Assert.That(dial.Row.LastError, Is.Not.Empty);
                Assert.That(dial.Row.Behaviour, Does.Contain("HDLC over TCP"));
                Assert.That(dial.Row.Behaviour, Does.Contain("redials every 2 s until it connects"));
                Assert.That(dial.Row.LastConnected, Is.EqualTo("never"));
                Assert.That(dial.Row.Detail, Does.Contain("failed dials "));
                Assert.That(dial.Row.Detail, Does.Contain("last error: " + dial.Row.LastError));

                Dispatcher.UIThread.RunJobs();
                string shown = XotLinkHealthTests.AllText(window);
                Assert.That(shown, Does.Contain("Trunk links"));
                Assert.That(shown, Does.Contain("LEASED-OUT (switch port 2)"));
                Assert.That(shown, Does.Contain("HDLC over TCP, DTE; redials every 2 s until it connects"));

                // Peer up: it listens as the DCE end of the trunk. The next redial connects and the LAPB link comes up.
                using (HdlcTcpSwitchPort peer = new HdlcTcpSwitchPort(PeerConfig(9, PortMode.Dce, "127.0.0.1", peerPort)))
                {
                    Assert.That(peer.Start(), Is.True, "the test peer could not listen");
                    XotLinkHealthTests.WaitUntil(() => FindPort(main, 2)!.Row.State == "LAPB link up", "the LAPB link to come up",
                        () => XotLinkHealthTests.PeerNames(main) + XotLinkHealthTests.AppLog(provider), 15000);
                    XotPeerLineViewModel up = FindPort(main, 2)!;
                    Assert.That(up.Row.Health, Is.EqualTo(LinkHealth.Up));
                    Assert.That(up.Row.LastConnected, Is.Not.EqualTo("never"));
                    Assert.That(up.Row.FailedConnects, Is.GreaterThanOrEqualTo(2), "the earlier failed dials stay counted");
                    Assert.That(up.Row.Detail, Does.Contain("link ups 1"));
                    Assert.That(up.Row.Detail, Does.Contain("link resets 0"));
                    Assert.That(up.Row.Detail, Does.Contain("retransmits 0"));
                    Assert.That(up.Row.Detail, Does.Contain("calls out 0, in 0"));
                    Assert.That(up.Row.Detail, Does.Not.Contain("frames in 0 /"), "the SABM and UA must be counted");

                    // The peer stops: the TCP connection ends, the row turns red and the drop is counted.
                    peer.Stop();
                    XotLinkHealthTests.WaitUntil(() => FindPort(main, 2)!.Row.Health == LinkHealth.Down && FindPort(main, 2)!.Row.Disconnects >= 1,
                        "the row to turn red after the peer stopped", () => XotLinkHealthTests.PeerNames(main) + XotLinkHealthTests.AppLog(provider));
                    Assert.That(FindPort(main, 2)!.Row.State, Does.Not.EqualTo("LAPB link up"));
                }

                window.Close();
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// A trunk port that listens: nobody connected, then the peer dials in and the link comes up. The row for a
        /// port that listens says so and does not show a dial.
        /// </summary>
        [AvaloniaTest]
        public void HdlcTrunkThatListens_WaitsForThePeer_ThenShowsLinkUp()
        {
            int callers = XotLinkHealthTests.FreePort();
            int desktopServer = XotLinkHealthTests.FreePort();
            int listenPort = XotLinkHealthTests.FreePort();
            string ini = WriteSwitchIni(callers, listenPort, XotLinkHealthTests.FreePort(), desktopServer);
            try
            {
                RuntimeConfig config = XotLinkHealthTests.EmbeddedConfig(callers, desktopServer, ini, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = XotLinkHealthTests.BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                MainWindow window = new MainWindow { DataContext = main };
                window.Width = 1000;
                window.Height = 700;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                XotPeerLineViewModel? listening = FindPort(main, 1);
                Assert.That(listening, Is.Not.Null, XotLinkHealthTests.PeerNames(main));
                Assert.That(listening!.Row.Name, Is.EqualTo("LEASED-IN"));
                Assert.That(listening.Row.Direction, Is.EqualTo("we listen"));
                Assert.That(listening.Row.State, Is.EqualTo("listening, nobody connected"));
                Assert.That(listening.Row.Behaviour, Does.Contain("HDLC over TCP, DCE; waits for the peer to dial in"));

                using (HdlcTcpSwitchPort peer = new HdlcTcpSwitchPort(PeerConfig(9, PortMode.Dte, "127.0.0.1", listenPort)))
                {
                    Assert.That(peer.Start(), Is.True);
                    XotLinkHealthTests.WaitUntil(() => FindPort(main, 1)!.Row.State == "LAPB link up", "the LAPB link to come up",
                        () => XotLinkHealthTests.PeerNames(main) + XotLinkHealthTests.AppLog(provider), 15000);
                    Assert.That(FindPort(main, 1)!.Row.Health, Is.EqualTo(LinkHealth.Up));
                    Assert.That(FindPort(main, 1)!.Row.Detail, Does.Contain("link ups 1"));
                }

                window.Close();
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>
        /// An HDLC trunk port is never mistaken for the other end of the app's own XOT link, even when it listens on
        /// the very port that link dials. Both are shown as rows; there is no quiet "Built-in switch" line.
        /// </summary>
        [AvaloniaTest]
        public void HdlcPortOnTheNumberOfAnXotLink_IsNotFoldedIntoTheQuietLine()
        {
            int callers = XotLinkHealthTests.FreePort();
            int desktopServer = XotLinkHealthTests.FreePort();
            string path = Path.Combine(Path.GetTempPath(), "x25test-trunk-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(path,
                "[switch]\nname = TestSwitch\ndnic = 2350\nmgmt_addr = 23500000\n" +
                "[port.0]\ntype = trunk\ntransport = hdlc-tcp\nmode = dce\nhost = 127.0.0.1\nport = " + callers + "\npeer_name = LEASED\nlapb_t1 = 500\n" +
                "[route.0]\ntype = dnic\ndnic = 2350\nport = 0\n" +
                "[user.0]\nname = admin\npassword = admin\nlevel = 1\n");
            try
            {
                RuntimeConfig config = XotLinkHealthTests.EmbeddedConfig(callers, desktopServer, path, out EmbeddedSwitchOptions options);
                using ServiceProvider provider = XotLinkHealthTests.BuildServices(config, options);
                MainWindowViewModel main = provider.GetRequiredService<MainWindowViewModel>();
                main.RefreshXotLinks();

                Assert.That(main.HasBuiltInLine, Is.False, "an HDLC port is not the other end of an XOT link\n" + main.BuiltInLine);
                Assert.That(FindPort(main, 0), Is.Not.Null, "the HDLC port must be a row\n" + XotLinkHealthTests.PeerNames(main));
                Assert.That(XotLinkHealthTests.Peer(main, "XOT_ToSwitch"), Is.Not.Null, "the XOT link must stay a row\n" + XotLinkHealthTests.PeerNames(main));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
