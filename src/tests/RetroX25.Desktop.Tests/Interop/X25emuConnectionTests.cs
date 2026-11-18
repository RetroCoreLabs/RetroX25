//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Avalonia.Headless.NUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.ViewModels;
using RetroX25.Tests;
using OurApp = RetroX25.Desktop.Tests.X25emuPeerTests.OurApp;
using OurPadCall = RetroX25.Desktop.Tests.X25emuPeerTests.OurPadCall;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// How many TCP connections XOT uses against x25emu for 1, 2 and 10 simultaneous calls, counted on both machines, and
    /// what our peer entry does when the far end is killed and comes back.
    /// </summary>
    /// <remarks>
    /// <para>RFC 1613 section 5: one TCP connection per virtual circuit. The counts are read two ways: here with the
    /// operating system's list of established connections (<c>IPGlobalProperties</c>, the same list
    /// <c>Get-NetTCPConnection</c> shows) and in WSL with <c>ss</c>.</para>
    /// <para>The peer-kill test starts a SCRATCH x25emu switch of its own (its own configuration file in /tmp, deleted at
    /// the end), so killing it touches nothing else. The switch is stopped by its recorded Linux process number.</para>
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    public sealed class X25emuConnectionTests
    {
        private static long StatActive(OurApp app)
        {
            string? cell = RetroX25.Desktop.Services.StatisticsReader.Build(app.Service.Runtime).Cell("X.25 active virtual circuits", "Total");
            return long.Parse(cell ?? "-1", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int SsCount(string filter)
        {
            string output = X25emuEnvironment.RunWsl("ss -Htn state established '" + filter + "' | wc -l", 15000, out int code);
            return int.Parse(output.Trim());
        }

        /// <summary>
        /// Calls we place: the answering side is OUR multi-call echo attached to x25emu Switch A (x25emu's own echo takes
        /// one call). N calls must be N TCP connections from us to x25emu, seen the same on both machines.
        /// </summary>
        [AvaloniaTest]
        public void OurCalls_OneTwoAndTenAtOnce_UseOneTcpConnectionEach_SeenOnBothMachines()
        {
            CountOurConnections(XotMode.Both);
        }

        /// <summary>
        /// The same with the old "Client" entry. It used to put every call on ONE connection (x25emu answered only the first
        /// call, the real Cisco router cleared the first call when the second arrived); it now opens a connection for each call.
        /// </summary>
        [AvaloniaTest]
        public void ClientEntry_OurCallsOneTwoAndTenAtOnce_UseOneTcpConnectionEach_SeenOnBothMachines()
        {
            CountOurConnections(XotMode.Client);
        }

        private static void CountOurConnections(XotMode mode)
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            // Calls go in on x25emu's ACCESS port 2001. The trunk port 2011 does not deliver a call for 2343 to the endpoint on
            // Switch A port 3 (measured: the Call Request arrives, no incoming-call line, no answer; see docs/crossenv-testing.md).
            using OurApp app = new OurApp(X25emuEnvironment.AccessA, null, "23510001", false, "234.*", mode);
            StringBuilder facts = new StringBuilder();
            int[] sizes = { 1, 2, 10 };
            for (int s = 0; s < sizes.Length; s++)
            {
                int n = sizes[s];
                // One idle connection per call that is coming (see X25emuEchoPool).
                using X25emuEchoPool echo = new X25emuEchoPool(X25emuEnvironment.WslHost!, X25emuEnvironment.CSharpAccessA, "23430001", n);
                List<OurPadCall> calls = new List<OurPadCall>();
                for (int i = 0; i < n; i++)
                {
                    OurPadCall call = new OurPadCall(app, "pad-" + n + "-" + i);
                    calls.Add(call);
                    // Calling address: a number of Switch A (2340), the network of the access port the call enters (X.25
                    // Appendix IV.2 i). It was our app's own 23510001 until 2026-11-18; x25emu now clears that with
                    // diagnostic 68.
                    Assert.That(call.Pad.Connect("23430001", "23400051"), Is.True, "call " + i + " of " + n);
                }

                XotLinkHealthTests.WaitUntil(() =>
                {
                    for (int i = 0; i < calls.Count; i++)
                    {
                        if (calls[i].Pad.ConnectionState != PadConnectionState.Connected)
                        {
                            return false;
                        }
                    }

                    return true;
                }, n + " calls to be connected", () => { int up = 0; for (int i = 0; i < calls.Count; i++) { if (calls[i].Pad.ConnectionState == PadConnectionState.Connected) { up++; } } return up + " of " + n + " connected; pool calls " + echo.Calls + "\n" + app.Log(); }, 30000);
                Thread.Sleep(500);
                int windows = X25emuPeerTests.OurTcpConnections(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessA);
                int linux = SsCount("( sport = :" + X25emuEnvironment.AccessA + " )");
                int echoSide = SsCount("( sport = :" + X25emuEnvironment.CSharpAccessA + " )");
                int report = app.Peer.GetPeerReport().DialedOpenNow;
                facts.AppendLine(n + " calls: Windows sees " + windows + " connections to x25emu:" + X25emuEnvironment.AccessA + ", WSL (ss) sees " + linux
                    + ", our peer report says " + report + " open; connections of our echo client to x25emu:" + X25emuEnvironment.CSharpAccessA + " = " + echoSide);
                Assert.That(windows, Is.EqualTo(n), facts.ToString());
                Assert.That(linux, Is.EqualTo(n), facts.ToString());
                Assert.That(report, Is.EqualTo(n), facts.ToString());
                for (int i = 0; i < calls.Count; i++)
                {
                    Assert.That(calls[i].Pad.SendText("N" + n + "-" + i + "\r"), Is.True);
                }

                XotLinkHealthTests.WaitUntil(() =>
                {
                    for (int i = 0; i < calls.Count; i++)
                    {
                        if (!calls[i].Received.Contains("N" + n + "-" + i))
                        {
                            return false;
                        }
                    }

                    return true;
                }, "every call to get its own text back", () => facts.ToString(), 30000);
                for (int i = 0; i < calls.Count; i++)
                {
                    calls[i].Pad.Disconnect();
                }

                XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialedOpenNow == 0, "all connections to close", () => app.Log());
                Assert.That(SsCount("( sport = :" + X25emuEnvironment.AccessA + " )"), Is.EqualTo(0), "nothing left open in WSL");
            }

            TestContext.Progress.WriteLine(facts.ToString());
            Assert.That(app.Errors(), Is.Empty);
        }

        /// <summary>
        /// Calls x25emu places: N x25pad programs call us at once; x25emu's XOT trunk dials a TCP connection to our listening
        /// port for each, and keeps one more idle one (measured: its log says "carrying LCI 0").
        /// </summary>
        [AvaloniaTest]
        public void X25emuCalls_OneTwoAndTenAtOnce_UseOneTcpConnectionEach_PlusTheIdleOne()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen);
            StringBuilder facts = new StringBuilder();
            int[] sizes = { 1, 2, 10 };
            for (int s = 0; s < sizes.Length; s++)
            {
                int n = sizes[s];
                List<X25emuEnvironment.LinuxProcess> pads = new List<X25emuEnvironment.LinuxProcess>();
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        pads.Add(new X25emuEnvironment.LinuxProcess(string.Empty,
                            X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "2340" + (1000 + i)));
                    }

                    XotLinkHealthTests.WaitUntil(() =>
                    {
                        for (int i = 0; i < pads.Count; i++)
                        {
                            if (!pads[i].Output.Contains("PAD ready"))
                            {
                                return false;
                            }
                        }

                        return true;
                    }, "the pads to be ready", () => pads[0].Output, 30000);
                    // Started 150 ms apart. All ten at the same instant is a different test (below): x25emu's XOT trunk gets
                    // confused when it dials many connections in the same millisecond.
                    for (int i = 0; i < pads.Count; i++)
                    {
                        pads[i].Write("CALL 23510001\r");
                        Thread.Sleep(150);
                    }

                    XotLinkHealthTests.WaitUntil(() =>
                    {
                        for (int i = 0; i < pads.Count; i++)
                        {
                            if (!pads[i].Output.Contains("\nCON"))
                            {
                                return false;
                            }
                        }

                        return true;
                    }, n + " calls to be answered", () => pads[0].Output + app.Log(), 40000);
                    Thread.Sleep(500);
                    int windows = X25emuPeerTests.OurTcpConnections(string.Empty, 0, X25emuEnvironment.OurXotPort);
                    int linux = SsCount("( dport = :" + X25emuEnvironment.OurXotPort + " )");
                    XotPeerReport report = app.Peer.GetPeerReport();
                    facts.AppendLine(n + " calls: Windows sees " + windows + " connections on our port " + X25emuEnvironment.OurXotPort + ", WSL (ss) sees " + linux
                        + ", our peer report says " + report.AcceptedOpenNow + " accepted open, " + report.CallsReceived + " calls received");
                    // x25emu dials one connection per call (RFC 1613 section 5) and keeps spare connections dialled to us for the next
                    // calls (its log: "carrying LCI 0"); how many spares stand varies with timing (0 to 2 measured). So: the three
                    // observers (Windows, WSL ss, our report) must agree to the connection, and there are at least n.
                    Assert.That(windows, Is.GreaterThanOrEqualTo(n), facts.ToString());
                    Assert.That(linux, Is.EqualTo(windows), facts.ToString());
                    Assert.That(report.AcceptedOpenNow, Is.EqualTo(windows), facts.ToString());
                    facts.AppendLine("   spare connections standing: " + (windows - n));
                    for (int i = 0; i < pads.Count; i++)
                    {
                        pads[i].Write("\u0010");
                        pads[i].Write("CLR\r");
                    }

                    // The calls are over: no circuit in our app. The spare connections x25emu keeps dialled stay (they carry no call).
                    XotLinkHealthTests.WaitUntil(() => StatActive(app) == 0, "no circuit left in our app", () => app.Log(), 30000);
                }
                finally
                {
                    for (int i = 0; i < pads.Count; i++)
                    {
                        pads[i].Dispose();
                    }
                }
            }

            TestContext.Progress.WriteLine(facts.ToString());
            Assert.That(app.Errors(), Is.Empty);
        }

        /// <summary>
        /// Ten calls from x25emu started in the same instant. What happens is MEASURED here, not wished for: x25emu's XOT
        /// trunk (Switch A port 4) dials one TCP connection per call in the same millisecond, and in its log two connections
        /// are given the same channel number ("dialled ... carrying LCI 64" twice) and only about half of the calls come
        /// up (5 of 10 in the run that found it; the others never get an answer). Our side is only asked to stay clean:
        /// no error line, nothing left open when the calls are cleared.
        /// </summary>
        [AvaloniaTest]
        public void X25emuCallsTenAtTheSameInstant_ReportsHowManyAreAnswered_AndOurSideStaysClean()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            using OurApp app = new OurApp(X25emuEnvironment.XotTrunkListen);
            List<X25emuEnvironment.LinuxProcess> pads = new List<X25emuEnvironment.LinuxProcess>();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    pads.Add(new X25emuEnvironment.LinuxProcess(string.Empty, X25emuEnvironment.ToolPath("x25pad"), "-x", "127.0.0.1:" + X25emuEnvironment.AccessA, "-a", "2340" + (2000 + i)));
                }

                XotLinkHealthTests.WaitUntil(() =>
                {
                    for (int i = 0; i < pads.Count; i++)
                    {
                        if (!pads[i].Output.Contains("PAD ready"))
                        {
                            return false;
                        }
                    }

                    return true;
                }, "the pads to be ready", () => pads[0].Output, 30000);
                for (int i = 0; i < pads.Count; i++)
                {
                    pads[i].Write("CALL 23510001\r");
                }

                Thread.Sleep(8000);
                int answered = 0;
                for (int i = 0; i < pads.Count; i++)
                {
                    if (pads[i].Output.Contains("\nCON"))
                    {
                        answered++;
                    }
                }

                XotPeerReport report = app.Peer.GetPeerReport();
                string facts = "10 calls at the same instant: " + answered + " answered, we accepted " + report.Accepted + " connections and received " + report.CallsReceived + " Call Requests";
                TestContext.Progress.WriteLine(facts);
                Assert.That(answered, Is.GreaterThanOrEqualTo(1), facts);
                for (int i = 0; i < pads.Count; i++)
                {
                    pads[i].Write("\u0010");
                    pads[i].Write("CLR\r");
                }

                Thread.Sleep(3000);
            }
            finally
            {
                for (int i = 0; i < pads.Count; i++)
                {
                    pads[i].Dispose();
                }
            }

            XotLinkHealthTests.WaitUntil(() => StatActive(app) == 0, "no circuit left in our app", () => app.Log(), 30000);
            TestContext.Progress.WriteLine("after the clears: " + app.Peer.GetPeerReport().AcceptedOpenNow + " spare connections stand (no circuit on them)");
            Assert.That(app.Errors(), Is.Empty);
        }
        private static string ScratchConf(int accessPort, int echoPort)
        {
            string window = "packet_size = 128\nwindow_size = 7\nmax_packet_size = 1024\nmax_window_size = 7\nlci_base = 1\nlci_count = 64\n";
            return "[switch]\ndnic = 2360\nmax_hops = 15\nmgmt_port = 9201\nmonitor_port = 4201\nname = Scratch\nlocation = Test\nmgmt_addr = 23600000\n\n"
                + "[user.0]\nname = admin\npassword = admin\nlevel = 1\n\n"
                + "[port.0]\ntype = access\ntransport = xot\nmode = dce\nport = " + accessPort + "\n" + window + "\n"
                + "[port.1]\ntype = access\ntransport = xot\nmode = dce\nport = " + echoPort + "\n" + window + "\n"
                + "[route.0]\ntype = prefix\naddress = 2360\nprefix_len = 4\nport = 1\n";
        }

        /// <summary>
        /// The far end is killed while a call is up, then a call is tried while it is down, then it comes back. Our peer
        /// entry reports each step: the drop, the failed dial with its reason, the log lines, the row in the XOT links
        /// panel, and the redial that works when the far end is back.
        /// </summary>
        [AvaloniaTest]
        public void FarEndKilledAndRestarted_TheDropIsReported_TheDialFails_AndTheNextCallRedials()
        {
            X25emuEnvironment.RequireReady(needInbound: true);
            const int AccessPort = 2020;
            const int EchoPort = 2021;
            string confPath = "/tmp/crossenv-scratch-" + Environment.ProcessId + ".conf";
            using (X25emuEnvironment.LinuxProcess writer = new X25emuEnvironment.LinuxProcess(string.Empty, "bash", "-c", "printf '%s' \"$1\" > \"$2\"", "x", ScratchConf(AccessPort, EchoPort), confPath))
            {
                Assert.That(writer.WaitForExit(15000), Is.True);
            }

            X25emuEnvironment.LinuxProcess? sw = null;
            X25Client? echo = null;
            try
            {
                using OurApp app = new OurApp(AccessPort, null, "23510001", false, "2360.*");
                MainWindowViewModel main = app.Provider.GetRequiredService<MainWindowViewModel>();
                StringBuilder facts = new StringBuilder();

                sw = StartSwitch(confPath, AccessPort, EchoPort);
                echo = StartEcho(EchoPort);

                // 1. A normal call: connects, echoes, clears.
                // Every call below gives a calling address of the scratch switch's own network 2360 (X.25 Appendix IV.2 i:
                // the access port checks it). With our app's 23510001 x25emu clears it, diagnostic 68 (seen 2026-11-18).
                OurPadCall first = new OurPadCall(app, "k1");
                Assert.That(first.Pad.Connect("23600001", "23600051"), Is.True);
                XotLinkHealthTests.WaitUntil(() => first.Pad.ConnectionState == PadConnectionState.Connected, "the first call", () => first.Received + app.Log(), 20000);
                first.Pad.SendText("BEFORE-KILL\r");
                XotLinkHealthTests.WaitUntil(() => first.Received.Contains("BEFORE-KILL"), "the echo", () => first.Received + app.Log());
                first.Pad.Disconnect();
                XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialedOpenNow == 0, "the connection to close");
                XotPeerReport r1 = app.Peer.GetPeerReport();
                facts.AppendLine("after a normal call: opened " + r1.DialedOpened + ", failures " + r1.DialFailures + ", drops " + r1.DialDrops);
                Assert.That(r1.DialedOpened, Is.EqualTo(1));
                Assert.That(r1.DialFailures, Is.EqualTo(0));
                Assert.That(r1.DialDrops, Is.EqualTo(0));

                // 2. A call is up, and the far end is killed (its Linux process, by number).
                OurPadCall second = new OurPadCall(app, "k2");
                Assert.That(second.Pad.Connect("23600001", "23600051"), Is.True);
                XotLinkHealthTests.WaitUntil(() => second.Pad.ConnectionState == PadConnectionState.Connected, "the second call", () => second.Received + app.Log(), 20000);
                sw.Dispose();
                sw = null;
                XotLinkHealthTests.WaitUntil(() => second.Pad.ConnectionState == PadConnectionState.Disconnected, "our PAD to learn that the call is gone", () => second.Received + app.Log(), 20000);
                XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialDrops == 1, "the drop to be counted", () => app.Log());
                XotPeerReport r2 = app.Peer.GetPeerReport();
                facts.AppendLine("after the kill: drops " + r2.DialDrops + ", open now " + r2.DialedOpenNow + "; PAD screen: " + second.Received.Replace("\r\n", " | "));
                string rowKilled = RowText(main);
                facts.AppendLine("panel row after the kill: " + rowKilled);

                // 3. A call while it is down: the dial fails, and the reason is kept and shown.
                OurPadCall third = new OurPadCall(app, "k3");
                bool placed = third.Pad.Connect("23600001", "23600051");
                XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialFailures >= 1, "the failed dial to be counted", () => app.Log());
                XotPeerReport r3 = app.Peer.GetPeerReport();
                string rowDown = RowText(main);
                facts.AppendLine("call while the far end is down: Connect returned " + placed + ", dial failures " + r3.DialFailures + ", last error \"" + r3.LastDialError + "\"");
                facts.AppendLine("panel row while down: " + rowDown);
                Assert.That(r3.LastDialError, Is.Not.Empty, facts.ToString());
                Assert.That(rowDown, Does.Contain("failed"), facts.ToString());
                Assert.That(app.Log(), Does.Contain("Failed to connect").Or.Contain("failed").Or.Contain("refused"), "a log line says the dial failed");

                // 4. The far end comes back: the next call dials again and works. No retry timer runs in between.
                echo.Dispose();
                echo = null;
                sw = StartSwitch(confPath, AccessPort, EchoPort);
                echo = StartEcho(EchoPort);
                OurPadCall fourth = new OurPadCall(app, "k4");
                Assert.That(fourth.Pad.Connect("23600001", "23600051"), Is.True);
                XotLinkHealthTests.WaitUntil(() => fourth.Pad.ConnectionState == PadConnectionState.Connected, "the call after the restart", () => fourth.Received + app.Log(), 20000);
                fourth.Pad.SendText("AFTER-RESTART\r");
                XotLinkHealthTests.WaitUntil(() => fourth.Received.Contains("AFTER-RESTART"), "the echo after the restart", () => fourth.Received + app.Log());
                fourth.Pad.Disconnect();
                XotLinkHealthTests.WaitUntil(() => app.Peer.GetPeerReport().DialedOpenNow == 0, "the connection to close");
                XotPeerReport r4 = app.Peer.GetPeerReport();
                facts.AppendLine("after the restart: opened " + r4.DialedOpened + ", failures " + r4.DialFailures + ", drops " + r4.DialDrops + "; panel row: " + RowText(main));
                TestContext.Progress.WriteLine(facts.ToString());
                TestContext.Progress.WriteLine(app.Log());
                Assert.That(r4.DialFailures, Is.EqualTo(r3.DialFailures), "no failure after the far end was back: " + facts);
                Assert.That(r4.DialedOpened, Is.EqualTo(r3.DialedOpened + 1), facts.ToString());
            }
            finally
            {
                echo?.Dispose();
                sw?.Dispose();
                X25emuEnvironment.RunWsl("rm -f " + confPath, 10000, out _);
            }
        }

        private static string RowText(MainWindowViewModel main)
        {
            XotPeerLineViewModel? line = XotLinkHealthTests.Peer(main, "XOT_X25emu");
            return line == null ? "(no row)" : line.Row.State + " // " + line.Row.Detail;
        }

        private static X25emuEnvironment.LinuxProcess StartSwitch(string confPath, int accessPort, int echoPort)
        {
            X25emuEnvironment.LinuxProcess process = new X25emuEnvironment.LinuxProcess(string.Empty, X25emuEnvironment.ToolPath("x25switch"), "-c", confPath);
            Assert.That(SpinWait.SpinUntil(() =>
            {
                int count = SsListening(accessPort) + SsListening(echoPort);
                return count >= 2;
            }, 15000), Is.True, "the scratch switch did not start listening:\n" + process.Output);
            return process;
        }

        private static int SsListening(int port)
        {
            string output = X25emuEnvironment.RunWsl("ss -Htln '( sport = :" + port + " )' | wc -l", 10000, out _);
            return int.Parse(output.Trim());
        }

        private static X25Client StartEcho(int port)
        {
            X25Client echo = new X25Client { ServiceName = "scratch-echo" };
            echo.SetOnIncoming((c, vc) => c.Accept(vc));
            echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) => c.Send(vc, data, length, qBit));
            echo.Open(new X25ClientConfig { Host = X25emuEnvironment.WslHost!, Port = (ushort)port, LocalAddress = "23600001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            Thread.Sleep(1000);
            return echo;
        }
    }
}
