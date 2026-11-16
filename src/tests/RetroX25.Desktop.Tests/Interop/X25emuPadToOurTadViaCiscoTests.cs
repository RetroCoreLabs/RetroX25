//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Headless.NUnit;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Link;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.Services;
using RetroX25.Tests;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// Calls to our app's built-in TAD (23430200) through the real Cisco router R1, whose configuration holds
    /// <c>x25 route ^2343 xot &lt;this PC&gt;</c>. Our app listens as an XOT server on TCP 1998.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What R1 does, measured 2026-11-15 and written in Cisco's own rules: a call that R1 starts itself (<c>pad 23430200</c> on its
    /// prompt) is sent on to our TAD. A call that ARRIVES at R1 over XOT and matches a route to an XOT destination is cleared at once
    /// (cause 13 "not obtainable", diagnostic 64) and R1 never dials. The Cisco IOS Wide-Area Networking Command Reference, command
    /// <c>x25 route</c>: "Incoming calls received via XOT connections that match a routing entry specifying an XOT destination are
    /// cleared." It is there to stop one XOT connection being chained onto another, and no setting turns it off. The window (2 or 7),
    /// the caller (x25emu's pad or our raw caller) and the target address were each changed and made no difference.
    /// </para>
    /// <para>
    /// So a Cisco can only sit between two XOT ends if one leg is not XOT (a serial X.25 line). The tests below pin both halves of the
    /// rule. Nothing is changed on R1 or in x25emu.
    /// </para>
    /// <para>
    /// The way that works (measured 2026-11-15): x25emu calls R2 over XOT, R2 sends the call over the serial X.25 line to R1, and R1,
    /// which now receives it from a serial line and not from XOT, sends it on by XOT to our TAD. R2 holds <c>x25 routing</c> and
    /// <c>x25 route ^2343 interface Serial1/0</c>, and its FastEthernet0/0 is cabled to the LAN in GNS3. Turning the call round on R2
    /// instead (in on Serial1/0 and back out of Serial1/0) does not work: R2 clears it with cause 0 diagnostic 64.
    /// </para>
    /// <para>
    /// Needs: <c>CISCO_R1_HOST</c> (R1's LAN address), <c>CISCO_R2_HOST</c> (R2's LAN address, for the test through R2),
    /// <c>X25EMU_TOOLS</c> (the folder in WSL that holds the built x25pad), R1 routing 2343 to this PC, and TCP 1998 free on this PC.
    /// A test whose settings are missing is skipped.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("CiscoLive")]
    [Explicit("needs the real Cisco router R1 (CISCO_R1_HOST) and x25pad built in WSL (X25EMU_TOOLS)")]
    public sealed class X25emuPadToOurTadViaCiscoTests
    {
        private const string TadAddress = "23430200";

        /// <summary>
        /// x25emu's pad calls the TAD through R1. The call reaches R1 over XOT and R1's route is an XOT route, so R1 clears it with cause 13
        /// diagnostic 64 and our TAD never sees a call (Cisco's rule, see the class remarks).
        /// </summary>
        [AvaloniaTest]
        public void X25emuPad_CallingOurTadThroughR1_IsClearedByR1_XotToXotIsNotForwarded()
        {
            string? router = Environment.GetEnvironmentVariable("CISCO_R1_HOST");
            if (string.IsNullOrEmpty(router) || string.IsNullOrWhiteSpace(X25emuEnvironment.Tools))
            {
                Assert.Ignore("CISCO_R1_HOST and X25EMU_TOOLS must be set");
            }

            RuntimeConfig config = App.CreateDefaultConfig();
            config.X25.LocalMachineId = TadAddress;
            config.Routing.XotInterfaces.Clear();
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig
            {
                Name = "XOT_Server",
                Enabled = true,
                Mode = XotMode.Server,
                LocalPort = 1998,
                RemoteMachineId = "23600001",
                AllowOnlyListedPeers = false,
            });
            config.Routing.RoutingTable.Clear();
            config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "LocalTAD", X121AddressPattern = "2343.*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Local, Destination = "LocalTAD", Priority = 200 });

            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            using ServiceProvider provider = services.BuildServiceProvider();
            IRuntimeService service = provider.GetRequiredService<IRuntimeService>();
            try
            {
                service.Initialize(config);

                using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                    X25emuEnvironment.ToolPath("x25pad"), "-x", router + ":1998", "-a", "23400055");
                XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("PAD ready"), "x25emu's PAD to be ready", () => pad.Output + "\n" + Log(provider));
                pad.Write("CALL " + TadAddress + "\r");
                XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("\nCON") || pad.Output.Contains("CLR"), "the call to be answered", () => pad.Output + "\n" + Log(provider));
                TestContext.Progress.WriteLine("after CALL:\n" + pad.Output);
                Assert.That(pad.Output, Does.Contain("CLR NP C:13 D:64"), "R1 should clear the call: cause 13, diagnostic 64\n" + pad.Output + "\n" + Log(provider));
                Assert.That(HistoryText(service), Does.Not.Contain("CallRequest"), "R1 should never dial our TAD for a call that came in over XOT");
                Assert.That(Errors(provider), Is.Empty);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// x25emu's pad calls the TAD through R2 and R1: XOT into R2, the serial X.25 line to R1, XOT from R1 to our TAD. No leg goes
        /// from XOT straight to XOT on one router, so both routers pass the call on. The pad must see the call connected and our TAD's
        /// banner, and our TAD must have accepted it.
        /// </summary>
        [AvaloniaTest]
        public void X25emuPad_CallingOurTadThroughR2AndR1_OverTheSerialLine_Connects()
        {
            string? router = Environment.GetEnvironmentVariable("CISCO_R2_HOST");
            if (string.IsNullOrEmpty(router) || string.IsNullOrWhiteSpace(X25emuEnvironment.Tools))
            {
                Assert.Ignore("CISCO_R2_HOST and X25EMU_TOOLS must be set");
            }

            RuntimeConfig config = App.CreateDefaultConfig();
            config.X25.LocalMachineId = TadAddress;
            config.Routing.XotInterfaces.Clear();
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig
            {
                Name = "XOT_Server",
                Enabled = true,
                Mode = XotMode.Server,
                LocalPort = 1998,
                RemoteMachineId = "23600001",
                AllowOnlyListedPeers = false,
            });
            config.Routing.RoutingTable.Clear();
            config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "LocalTAD", X121AddressPattern = "2343.*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Local, Destination = "LocalTAD", Priority = 200 });

            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            using ServiceProvider provider = services.BuildServiceProvider();
            IRuntimeService service = provider.GetRequiredService<IRuntimeService>();
            try
            {
                service.Initialize(config);

                using X25emuEnvironment.LinuxProcess pad = new X25emuEnvironment.LinuxProcess(string.Empty,
                    X25emuEnvironment.ToolPath("x25pad"), "-x", router + ":1998", "-a", "23400055");
                XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("PAD ready"), "x25emu's PAD to be ready", () => pad.Output + "\n" + Log(provider));
                pad.Write("CALL " + TadAddress + "\r");
                XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("\nCON") || pad.Output.Contains("CLR"), "the call to be answered", () => pad.Output + "\n" + Log(provider));
                Assert.That(pad.Output, Does.Contain("\nCON"), "the call through R2 and R1 was not connected\n" + pad.Output + "\n" + Log(provider));
                XotLinkHealthTests.WaitUntil(() => pad.Output.Contains("TAD Loopback Echo Service"), "our TAD's banner at the pad", () => pad.Output + "\n" + HistoryText(service));
                TestContext.Progress.WriteLine("pad:\n" + pad.Output + "\nour TAD saw:\n" + HistoryText(service));
                Assert.That(HistoryText(service), Does.Contain("CallAccepted"), "our TAD did not accept the call\n" + HistoryText(service));
                Assert.That(Errors(provider), Is.Empty);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Load through the routed path: x25emu's <c>x25traffic</c> runs with <paramref name="arguments"/> by XOT to R2, which passes the
        /// calls over the serial cable to R1, which sends them by XOT to OUR switch (listening on TCP 1998). The switch routes 2343 to its
        /// access port, where an echo at 23430201 sends every data packet back unchanged, so the tool's delivery figure is a real count.
        /// (The TAD cannot be used for this: it answers with its own text, not with the packets.) The tool's whole report is printed and
        /// its "Delivery" line must say 100.0%.
        /// </summary>
        /// <param name="arguments">The x25traffic options after <c>-x</c> and <c>-d</c>.</param>
        /// <param name="timeoutMs">How long the run may take.</param>
        [Test]
        [TestCase("-n 200 -r 0", 120000, TestName = "{m}_FullSpeed")]
        [TestCase("-n 600 -r 20", 120000, TestName = "{m}_20PerSecond")]
        [TestCase("-n 900 -r 30", 120000, TestName = "{m}_30PerSecond")]
        [TestCase("-n 1200 -r 40", 120000, TestName = "{m}_40PerSecond")]
        [TestCase("-C -n 16 -r 2 -p 3", 120000, TestName = "{m}_16Calls")]
        [TestCase("-n 12000 -r 20", 900000, TestName = "{m}_TenMinutes")]
        [TestCase("-n 24000 -r 40", 900000, TestName = "{m}_TenMinutesAt40")]
        [TestCase("-n 20000 -r 0", 900000, TestName = "{m}_FullSpeed20000")]
        [TestCase("-n 6000 -r 0", 300000, false, TestName = "{m}_OneWayFullSpeed")]
        public void X25emuTraffic_ThroughR2AndR1_ToAnEchoOnOurSwitch_EveryPacketComesBack(string arguments, int timeoutMs, bool echoBack = true)
        {
            string? router = Environment.GetEnvironmentVariable("CISCO_R2_HOST");
            if (string.IsNullOrEmpty(router) || string.IsNullOrWhiteSpace(X25emuEnvironment.Tools))
            {
                Assert.Ignore("CISCO_R2_HOST and X25EMU_TOOLS must be set");
            }

            SwitchConfig cfg = new SwitchConfig { Dnic = "2343" };
            cfg.Routing.AddRoute(new X121Route { Type = X121RouteType.Prefix, Address = "2343", PrefixLen = 4, DestPort = 1 });
            cfg.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 0 });
            SwitchNode node = new SwitchNode(cfg);
            XotSwitchPort trunk = new XotSwitchPort(
                new PortConfig { Id = 0, Type = PortType.Trunk, Transport = PortTransport.Xot, Mode = PortMode.Dce, Host = "0.0.0.0", Port = 1998, LciBase = 1, LciCount = 256 },
                NullLogger.Instance);
            XotSwitchPort access = new XotSwitchPort(
                new PortConfig { Id = 1, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1, LciCount = 256 },
                NullLogger.Instance);
            node.AddPort(trunk);
            node.AddPort(access);
            SwitchCore core = new SwitchCore(node);

            // Every packet on the trunk to R1, with the time it was seen, decoded in order at the end when X25_TRUNK_TRACE is set.
            // "1" records the whole run; "from-to" (milliseconds after the switch started, e.g. 90000-92000) records only that
            // window, so a long run can be read in the middle.
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            List<(double Ms, bool In, byte[] Packet)> trunkTrace = new List<(double, bool, byte[])>();
            string traceSetting = Environment.GetEnvironmentVariable("X25_TRUNK_TRACE") ?? string.Empty;
            double traceFrom = 0;
            double traceTo = double.MaxValue;
            int dash = traceSetting.IndexOf('-');
            if (dash > 0)
            {
                traceFrom = double.Parse(traceSetting.AsSpan(0, dash), System.Globalization.CultureInfo.InvariantCulture);
                traceTo = double.Parse(traceSetting.AsSpan(dash + 1), System.Globalization.CultureInfo.InvariantCulture);
            }

            trunk.PacketCaptured += (id, inbound, packet) =>
            {
                double ms = clock.Elapsed.TotalMilliseconds;
                if (traceSetting.Length == 0 || ms < traceFrom || ms > traceTo)
                {
                    return;
                }

                lock (trunkTrace)
                {
                    trunkTrace.Add((ms, inbound, packet));
                }
            };
            using X25Client echo = new X25Client { ServiceName = "echo" };
            long echoed = 0;
            long windowWaits = 0;

            // A send is refused while the echo's send window is full (it has sent as many packets as the window allows and is waiting
            // for the caller's acknowledgement). Throwing those away would count as loss on the routers, so, like RetroX25.Echo, the
            // packets are queued and a sender thread keeps trying each one until the window opens.
            System.Collections.Concurrent.ConcurrentQueue<(X25Vc Vc, byte[] Data, bool Q)> pending = new System.Collections.Concurrent.ConcurrentQueue<(X25Vc, byte[], bool)>();
            using CancellationTokenSource stop = new CancellationTokenSource();
            Thread sender = new Thread(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    if (!pending.TryPeek(out (X25Vc Vc, byte[] Data, bool Q) next))
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    if (!next.Vc.Connected || echo.Send(next.Vc, next.Data, next.Data.Length, next.Q))
                    {
                        pending.TryDequeue(out _);
                        Interlocked.Increment(ref echoed);
                    }
                    else
                    {
                        Interlocked.Increment(ref windowWaits);
                        Thread.Sleep(1);
                    }
                }
            }) { IsBackground = true, Name = "test echo sender" };
            try
            {
                Assert.That(trunk.Start(), Is.True, "TCP 1998 is not free on this PC");
                Assert.That(access.Start(), Is.True);
                sender.Start();
                echo.SetOnIncoming((c, vc) => c.Accept(vc));
                echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    // One way only: the packets are taken and acknowledged but nothing is sent back, so the run measures how
                    // much the routers carry in one direction (the sender's rate; delivery is 0 by design).
                    if (!echoBack)
                    {
                        Interlocked.Increment(ref echoed);
                        return;
                    }

                    byte[] reply = new byte[length];
                    Array.Copy(data, 0, reply, 0, length);
                    pending.Enqueue((vc, reply, qBit));
                });
                // NumVcs: the client takes 8 calls at once by default, which a call storm of 16 would overrun.
                echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = access.BoundPort, LocalAddress = "23430201", Framing = X25ClientFraming.Xot, NumVcs = 64 }, NullLogger.Instance);

                // X25_TRAFFIC_CALLED: the number x25traffic calls. 23430201 by default; a router can rewrite another number to it on the
                // way (x25 route ... substitute-dest), which lets the same echo be reached by a different path through the routers.
                string called = Environment.GetEnvironmentVariable("X25_TRAFFIC_CALLED") ?? "23430201";
                List<string> command = new List<string> { X25emuEnvironment.ToolPath("x25traffic"), "-x", router + ":1998", "-d", called };
                command.AddRange(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                using X25emuEnvironment.LinuxProcess traffic = new X25emuEnvironment.LinuxProcess(string.Empty, command.ToArray());
                bool finished = traffic.WaitForExit(timeoutMs);
                TestContext.Progress.WriteLine("x25traffic " + arguments + " through R2 and R1 (finished: " + finished + "):\n" + traffic.Output
                    + "\nour echo sent back " + Interlocked.Read(ref echoed) + " packets; it waited for its send window " + Interlocked.Read(ref windowWaits) + " times");
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("X25_TRUNK_TRACE")))
                {
                    lock (trunkTrace)
                    {
                        StringBuilder t = new StringBuilder("trunk to R1, every packet (ms, direction, decoded, bytes):\n");
                        for (int i = 0; i < trunkTrace.Count; i++)
                        {
                            byte[] p = trunkTrace[i].Packet;
                            t.Append(trunkTrace[i].Ms.ToString("F1")).Append(trunkTrace[i].In ? "  R1->us  " : "  us->R1  ")
                                .Append(DecodePacket(p)).Append("  ").Append(BitConverter.ToString(p, 0, Math.Min(p.Length, 24))).Append('\n');
                        }

                        TestContext.Progress.WriteLine(t.ToString());
                    }
                }
                Assert.That(finished, Is.True, "x25traffic did not finish in time");
                if (!echoBack)
                {
                    Assert.That(Interlocked.Read(ref echoed), Is.EqualTo(long.Parse(arguments.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)), "not every packet reached our switch\n" + traffic.Output);
                    return;
                }

                if (arguments.Contains("-C", StringComparison.Ordinal))
                {
                    // A call storm reports calls, not delivery. x25emu's client opens one TCP connection per call (RFC 1613) and holds
                    // at most 16 (X25C_MAX_CONNS in src/client/x25_client.h), so a storm of more than 16 calls fails inside x25emu
                    // before anything is sent; 16 is the most this test can ask for.
                    Assert.That(traffic.Output, Does.Contain("VCs opened:    16 (16 connected, 0 failed)"), "not every call connected\n" + traffic.Output);
                    Assert.That(traffic.Output, Does.Contain("Cleared:       16 of 16"), "not every call cleared\n" + traffic.Output);
                    return;
                }

                Assert.That(traffic.Output, Does.Contain("Delivery:  100.0%").Or.Contain("Delivery: 100.0%"), "not every packet came back\n" + traffic.Output);
            }
            finally
            {
                stop.Cancel();
                trunk.Stop();
                access.Stop();
            }
        }

        /// <summary>
        /// Our TAD waits up to 90 seconds for a call to arrive over XOT on port 1998 (a person types <c>pad 23430200</c> on R1's own prompt
        /// while this runs), answers it, and the test then checks that the call was received, accepted and cleared.
        /// </summary>
        [AvaloniaTest]
        public void OurTad_WaitsForACallFromR1sOwnPrompt_AndAnswersIt()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CISCO_R1_HOST")))
            {
                Assert.Ignore("CISCO_R1_HOST must be set");
            }

            RuntimeConfig config = App.CreateDefaultConfig();
            config.X25.LocalMachineId = TadAddress;
            config.Routing.XotInterfaces.Clear();
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig
            {
                Name = "XOT_Server",
                Enabled = true,
                Mode = XotMode.Server,
                LocalPort = 1998,
                RemoteMachineId = "23600001",
                AllowOnlyListedPeers = false,
            });
            config.Routing.RoutingTable.Clear();
            config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "LocalTAD", X121AddressPattern = "2343.*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Local, Destination = "LocalTAD", Priority = 200 });

            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            using ServiceProvider provider = services.BuildServiceProvider();
            IRuntimeService service = provider.GetRequiredService<IRuntimeService>();
            try
            {
                service.Initialize(config);
                TestContext.Progress.WriteLine("TAD LISTENING on 1998");
                XotLinkHealthTests.WaitUntil(() => HistoryText(service).Contains("CallRequest"), "a call to arrive from R1", () => Log(provider), 90000);
                XotLinkHealthTests.WaitUntil(() => HistoryText(service).Contains("ClearConfirmation") || HistoryText(service).Contains("ClearRequest"), "the call to be cleared", () => HistoryText(service), 30000);
                string history = HistoryText(service);
                TestContext.Progress.WriteLine("packet history:\n" + history);
                Assert.That(history, Does.Contain("CallAccepted"), "the TAD did not accept the call\n" + history);
                Assert.That(Errors(provider), Is.Empty);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Our raw XOT caller (one TCP connection, the X.29 marker, packet size 128, the given window) calls R1 for 23430200. R1 clears it
        /// with cause 13 diagnostic 64 whatever the window, and our TAD sees nothing: the refusal is Cisco's XOT-to-XOT rule, not anything
        /// about x25emu's pad (its second idle connection, its window 7).
        /// </summary>
        /// <param name="window">The window size asked for.</param>
        [AvaloniaTest]
        [TestCase((byte)2)]
        [TestCase((byte)7)]
        public void RawCallerCallingOurTadThroughR1_IsClearedByR1_WhateverTheWindow(byte window)
        {
            string? router = Environment.GetEnvironmentVariable("CISCO_R1_HOST");
            if (string.IsNullOrEmpty(router))
            {
                Assert.Ignore("CISCO_R1_HOST must be set");
            }

            RuntimeConfig config = App.CreateDefaultConfig();
            config.X25.LocalMachineId = TadAddress;
            config.Routing.XotInterfaces.Clear();
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig
            {
                Name = "XOT_Server",
                Enabled = true,
                Mode = XotMode.Server,
                LocalPort = 1998,
                RemoteMachineId = "23600001",
                AllowOnlyListedPeers = false,
            });
            config.Routing.RoutingTable.Clear();
            config.Routing.RoutingTable.Add(new X25RouteEntry { Name = "LocalTAD", X121AddressPattern = "2343.*", Type = RetroIO.Protocols.X25.Runtime.RouteType.Local, Destination = "LocalTAD", Priority = 200 });

            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, null);
            using ServiceProvider provider = services.BuildServiceProvider();
            IRuntimeService service = provider.GetRequiredService<IRuntimeService>();
            try
            {
                service.Initialize(config);
                using RawXotCaller caller = new RawXotCaller(router!, 1998, 1);
                bool accepted = caller.Call(TadAddress, "23400077", 0x07, window, 8000, new byte[] { 0x01, 0x00, 0x00, 0x00 });
                System.Threading.Thread.Sleep(500);
                TestContext.Progress.WriteLine("window " + window + ", caller side:\n" + caller.Dump() + "\nour TAD saw:\n" + HistoryText(service));
                Assert.That(accepted, Is.False, "R1 forwarded an XOT call to an XOT route, which Cisco says it clears\n" + caller.Dump());
                byte[]? clear = caller.WaitForType(1, 0x13, 1000);
                Assert.That(clear, Is.Not.Null, "no Clear Request from R1\n" + caller.Dump());
                Assert.That(clear![3], Is.EqualTo((byte)0x0D), "cause 13, not obtainable");
                Assert.That(clear[4], Is.EqualTo((byte)0x40), "diagnostic 64");
                Assert.That(HistoryText(service), Does.Not.Contain("CallRequest"), "R1 should never dial our TAD");
            }
            finally
            {
                service.Dispose();
            }
        }

        private static string HistoryText(IRuntimeService service)
        {
            StringBuilder text = new StringBuilder();
            foreach (RetroIO.Protocols.X25.Models.HistoryItem item in service.Runtime.GetHistory())
            {
                text.AppendLine(item.ToString());
            }

            return text.ToString();
        }

        /// <summary>
        /// Names one modulo-8 X.25 packet (X.25 section 5, figures 5-1 to 5-8): the GFI's Q and D bits, the channel, and for data
        /// P(R), M and P(S); for RR, RNR and REJ the P(R); other types by their type octet. Length in octets at the end.
        /// </summary>
        private static string DecodePacket(byte[] p)
        {
            if (p.Length < 3)
            {
                return "short(" + p.Length + ")";
            }

            int lci = ((p[0] & 0x0F) << 8) | p[1];
            string gfi = ((p[0] & 0x80) != 0 ? "Q" : "-") + ((p[0] & 0x40) != 0 ? "D" : "-");
            byte type = p[2];
            string what;
            if ((type & 0x01) == 0)
            {
                what = "DATA PS=" + ((type >> 1) & 7) + " PR=" + (type >> 5) + ((type & 0x10) != 0 ? " M" : string.Empty) + " len=" + (p.Length - 3);
            }
            else if ((type & 0x1F) == 0x01)
            {
                what = "RR   PR=" + (type >> 5);
            }
            else if ((type & 0x1F) == 0x05)
            {
                what = "RNR  PR=" + (type >> 5);
            }
            else if ((type & 0x1F) == 0x09)
            {
                what = "REJ  PR=" + (type >> 5);
            }
            else
            {
                what = type switch
                {
                    0x0B => "CALL REQUEST",
                    0x0F => "CALL ACCEPTED",
                    0x13 => "CLEAR REQUEST",
                    0x17 => "CLEAR CONFIRM",
                    0x1B => "RESET REQUEST",
                    0x1F => "RESET CONFIRM",
                    0x23 => "INTERRUPT",
                    0x27 => "INTERRUPT CONFIRM",
                    0xFB => "RESTART",
                    0xFF => "RESTART CONFIRM",
                    0xF1 => "DIAGNOSTIC",
                    _ => "type 0x" + type.ToString("X2"),
                };
            }

            return "lci " + lci + " " + gfi + " " + what;
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

        private static string Log(ServiceProvider provider)
        {
            RetroIO.Protocols.X25.Common.HistoryLoggingProvider history = provider.GetRequiredService<RetroIO.Protocols.X25.Common.HistoryLoggingProvider>();
            IReadOnlyList<RetroIO.Protocols.X25.Common.LogEntry> entries = history.GetAllLogs();
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                text.Append(entries[i].Level).Append(' ').Append(entries[i].Message).Append('\n');
            }

            return text.ToString();
        }

        private static string Errors(ServiceProvider provider)
        {
            RetroIO.Protocols.X25.Common.HistoryLoggingProvider history = provider.GetRequiredService<RetroIO.Protocols.X25.Common.HistoryLoggingProvider>();
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
    }
}
