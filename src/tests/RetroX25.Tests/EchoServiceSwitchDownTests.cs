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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;
using RetroX25.Echo;

namespace RetroX25.Tests
{
    /// <summary>
    /// The echo service is a DTE behind a switch port. Started while the switch is down it keeps dialling until the
    /// switch answers, then serves calls; when the switch dies and comes back it serves calls again by itself.
    /// </summary>
    /// <remarks>
    /// X.25 (10/96) 2.4.4.1 (text line 3326): "Either the DTE or the DCE may initiate data link set-up"; line 3329: the
    /// DTE "shall initiate data link set-up by transmitting an SABM", and a set-up that fails N2 times is left to
    /// "appropriate higher layer recovery action" (line 3348-3350). Over XOT the link is the TCP connection (RFC 1613
    /// section 5, line 201). The time between dials (XotSpareRedial, 2 s doubling to 30 s by default; 100 ms to 400 ms
    /// here) is our own choice, not the standard's. Until 2026-11-18 x25echo exited with code 1 when the switch was not
    /// there at start.
    /// </remarks>
    [TestFixture]
    public class EchoServiceSwitchDownTests
    {
        private const string EchoAddress = "23410001";

        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposables[i].Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // A switch port the test already took down.
                }
            }

            _disposables.Clear();
        }

        [Test]
        public void StartedWithNoSwitch_ItWaits_ThenServesCalls_AndAgainAfterTheSwitchRestarts()
        {
            ushort subscriberTcp = FreePort();
            X25ClientConfig cfg = new X25ClientConfig
            {
                Host = "127.0.0.1",
                Port = subscriberTcp,
                LocalAddress = EchoAddress,
                Framing = X25ClientFraming.Xot,
                SpareRedialFirstMs = 100,
                SpareRedialMostMs = 400,
            };
            EchoService service = new EchoService(cfg, quiet: true, NullLogger.Instance);
            _disposables.Add(service);

            // Started with the switch down: Open does not give up, it keeps dialling.
            Task<bool> open = Task.Run(service.Open);
            Assert.That(SpinWait.SpinUntil(() => service.DialFailures >= 3, 10000), Is.True,
                "the service stopped dialling after " + service.DialFailures + " failure(s)");
            Assert.That(open.IsCompleted, Is.False, "Open returned while the switch was still down");

            // The switch is started later; the service reaches it and an echo call works.
            Network net = BuildSwitch(subscriberTcp, 0);
            Assert.That(open.Wait(10000), Is.True, "the service never reached the switch once it was up");
            Assert.That(open.Result, Is.True);
            Assert.That(SpinWait.SpinUntil(() => net.SubscriberPort.ConnectionCount >= 1, 5000), Is.True);
            CallAndExpectTheEcho(net, "23400001", "first call");

            // The switch dies and comes back on the same TCP ports; a new call is echoed.
            ushort callerTcp = net.CallerPort.BoundPort;
            net.Kill();
            Network again = BuildSwitch(subscriberTcp, callerTcp);
            Assert.That(SpinWait.SpinUntil(() => again.SubscriberPort.ConnectionCount >= 1, 10000), Is.True,
                "the service never dialled the switch again after its restart");
            CallAndExpectTheEcho(again, "23400002", "call after the switch restart");
        }

        [Test]
        public void Stop_WhileWaitingForTheSwitch_EndsOpenWithFalse()
        {
            X25ClientConfig cfg = new X25ClientConfig
            {
                Host = "127.0.0.1",
                Port = FreePort(),
                LocalAddress = EchoAddress,
                Framing = X25ClientFraming.Xot,
                SpareRedialFirstMs = 100,
                SpareRedialMostMs = 400,
            };
            EchoService service = new EchoService(cfg, quiet: true, NullLogger.Instance);
            _disposables.Add(service);
            Task<bool> open = Task.Run(service.Open);
            Assert.That(SpinWait.SpinUntil(() => service.DialFailures >= 1, 10000), Is.True);

            service.Stop();

            Assert.That(open.Wait(5000), Is.True, "Stop did not end the wait for the switch");
            Assert.That(open.Result, Is.False);
            Assert.That(service.IsOpen, Is.False);
        }

        // ------------------------------------------------------------------ helpers

        private void CallAndExpectTheEcho(Network net, string calling, string what)
        {
            X25Client caller = new X25Client { ServiceName = "caller" };
            _disposables.Add(caller);
            byte[]? received = null;
            using ManualResetEventSlim gotEcho = new ManualResetEventSlim(false);
            caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                byte[] copy = new byte[length];
                Array.Copy(data, 0, copy, 0, length);
                received = copy;
                gotEcho.Set();
            });
            caller.Open(new X25ClientConfig
            {
                Host = "127.0.0.1",
                Port = net.CallerPort.BoundPort,
                LocalAddress = calling,
                Framing = X25ClientFraming.Xot,
            }, NullLogger.Instance);

            X25Vc? vc = caller.CallWait(EchoAddress, 5000);
            Assert.That(vc, Is.Not.Null, what + ": the echo did not answer the call");
            string message = "echo " + calling;
            Assert.That(caller.SendText(vc!, message), Is.True, what);
            Assert.That(gotEcho.Wait(5000), Is.True, what + ": nothing came back");
            Assert.That(Encoding.ASCII.GetString(received!), Is.EqualTo(message), what);
        }

        private sealed class Network
        {
            public XotSwitchPort SubscriberPort = null!;
            public XotSwitchPort CallerPort = null!;

            /// <summary>The switch dies: both ports stop and close every connection.</summary>
            public void Kill()
            {
                SubscriberPort.Dispose();
                CallerPort.Dispose();
            }
        }

        /// <summary>
        /// One switch: port 0 is the echo's line (calls for 2341... go there), port 1 is where callers connect. A TCP
        /// port of 0 lets the system choose; a number puts the port where a switch that died had it.
        /// </summary>
        private Network BuildSwitch(ushort subscriberTcp, ushort callerTcp)
        {
            SwitchConfig cfg = new SwitchConfig { Dnic = "2340" };
            cfg.Routing.AddRoute(new X121Route { Type = X121RouteType.Prefix, Address = "2341", PrefixLen = 4, DestPort = 0 });
            SwitchNode node = new SwitchNode(cfg);
            Network net = new Network
            {
                SubscriberPort = new XotSwitchPort(new PortConfig { Id = 0, Mode = PortMode.Dce, Host = "127.0.0.1", Port = subscriberTcp, LciBase = 1, LciCount = 64 }, NullLogger.Instance),
                CallerPort = new XotSwitchPort(new PortConfig { Id = 1, Mode = PortMode.Dce, Host = "127.0.0.1", Port = callerTcp, LciBase = 1, LciCount = 64 }, NullLogger.Instance),
            };
            Assert.That(net.SubscriberPort.Start(), Is.True, "the subscriber port did not start on TCP port " + subscriberTcp);
            Assert.That(net.CallerPort.Start(), Is.True, "the caller port did not start on TCP port " + callerTcp);
            _disposables.Add(net.SubscriberPort);
            _disposables.Add(net.CallerPort);
            node.AddPort(net.SubscriberPort);
            node.AddPort(net.CallerPort);
            new SwitchCore(node);
            return net;
        }

        /// <summary>
        /// A TCP port nothing listens on now: bound to 0, read back, released.
        /// </summary>
        private static ushort FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            ushort port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
