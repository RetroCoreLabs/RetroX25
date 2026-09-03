using System;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroX25.Switch;
using RetroIO.Protocols.X25.Switch.Model;

namespace RetroX25.Tests
{
    /// <summary>
    /// Validates multi-switch trunk routing (the demo topology): a caller on switch A's access port places a
    /// call that routes across a trunk to switch B and out to an echo endpoint on B's access port, and the
    /// data round-trips. Exercises a DTE trunk connecting to a DCE trunk plus DNIC/default routing across two
    /// switches, over real XOT/TCP.
    /// </summary>
    [TestFixture]
    public class MultiSwitchTrunkTests
    {
        [Test]
        public void CallRoutesAcrossTwoTrunks_ThroughTransitSwitch()
        {
            // Mirrors the 3-switch demo: A (access+trunk) -> B (transit: trunk in/out) -> C (trunk+access echo).
            // ---- Switch C (DNIC 2352): trunk-in port 0, access (echo) port 1 ----
            SwitchConfig cfgC = new SwitchConfig { Dnic = "2352" };
            cfgC.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2352", DestPort = 1 });
            cfgC.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 0 });
            SwitchNode nodeC = new SwitchNode(cfgC);
            XotSwitchPort cTrunk = NewPort(0, PortType.Trunk, PortMode.Dce, 0);
            XotSwitchPort cAccess = NewPort(1, PortType.Access, PortMode.Dce, 0);
            nodeC.AddPort(cTrunk);
            nodeC.AddPort(cAccess);
            SwitchCore coreC = new SwitchCore(nodeC);
            cTrunk.Start();
            cAccess.Start();

            // ---- Switch B (DNIC 2351): trunk-in port 0, trunk-out to C port 1 ----
            SwitchConfig cfgB = new SwitchConfig { Dnic = "2351" };
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2352", DestPort = 1 });
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
            SwitchNode nodeB = new SwitchNode(cfgB);
            XotSwitchPort bTrunkIn = NewPort(0, PortType.Trunk, PortMode.Dce, 0);
            XotSwitchPort bTrunkOut = NewPort(1, PortType.Trunk, PortMode.Dte, cTrunk.ListenPort);
            nodeB.AddPort(bTrunkIn);
            nodeB.AddPort(bTrunkOut);
            SwitchCore coreB = new SwitchCore(nodeB);
            bTrunkIn.Start();
            bTrunkOut.Start();

            // ---- Switch A (DNIC 2350): access port 0, trunk-out to B port 1, plus a cross-env trunk
            // (port 2, listening) and a prefix-234 route before default -- exactly like the demo config. ----
            SwitchConfig cfgA = new SwitchConfig { Dnic = "2350" };
            cfgA.Routing.AddRoute(new X121Route { Type = X121RouteType.Prefix, Address = "234", PrefixLen = 3, DestPort = 2 });
            cfgA.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
            SwitchNode nodeA = new SwitchNode(cfgA);
            XotSwitchPort aAccess = NewPort(0, PortType.Access, PortMode.Dce, 0);
            XotSwitchPort aTrunk = NewPort(1, PortType.Trunk, PortMode.Dte, bTrunkIn.ListenPort);
            XotSwitchPort aCrossEnv = NewPort(2, PortType.Trunk, PortMode.Dce, 0);
            nodeA.AddPort(aAccess);
            nodeA.AddPort(aTrunk);
            nodeA.AddPort(aCrossEnv);
            SwitchCore coreA = new SwitchCore(nodeA);
            aAccess.Start();
            aTrunk.Start();
            aCrossEnv.Start();

            using X25Client echo = new X25Client { ServiceName = "echo" };
            using X25Client caller = new X25Client { ServiceName = "caller" };
            try
            {
                ManualResetEventSlim echoAccepted = new ManualResetEventSlim(false);
                echo.SetOnIncoming((c, vc) => { c.Accept(vc); echoAccepted.Set(); });
                echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    byte[] reply = new byte[length];
                    Array.Copy(data, 0, reply, 0, length);
                    c.Send(vc, reply, length, qBit);
                });
                echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)cAccess.ListenPort, LocalAddress = "23520001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

                byte[]? received = null;
                ManualResetEventSlim gotEcho = new ManualResetEventSlim(false);
                caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    received = new byte[length];
                    Array.Copy(data, 0, received, 0, length);
                    gotEcho.Set();
                });
                caller.Open(new X25ClientConfig { Host = "127.0.0.1", Port = (ushort)aAccess.ListenPort, LocalAddress = "23500099", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

                X25Vc? vc = caller.CallWait("23520001", 5000);
                Assert.That(vc, Is.Not.Null, "the call should route across two trunks (A -> B -> C) to the echo");
                Assert.That(echoAccepted.Wait(5000), Is.True, "the echo on switch C should receive the onward call");

                Assert.That(caller.SendText(vc!, "TWOHOPS"), Is.True);
                Assert.That(gotEcho.Wait(5000), Is.True, "the echo should return across both trunks");
                Assert.That(Encoding.UTF8.GetString(received!), Is.EqualTo("TWOHOPS"));
            }
            finally
            {
                aAccess.Stop(); aTrunk.Stop(); aCrossEnv.Stop(); bTrunkIn.Stop(); bTrunkOut.Stop(); cTrunk.Stop(); cAccess.Stop();
            }
        }

        private static XotSwitchPort NewPort(int id, PortType type, PortMode mode, int connectPort)
        {
            return new XotSwitchPort(
                new PortConfig { Id = id, Type = type, Mode = mode, Host = "127.0.0.1", Port = (ushort)connectPort, LciBase = 1 },
                NullLogger.Instance);
        }

        [Test]
        public void CallRoutesAcrossTrunk_ToEchoOnNextSwitch()
        {
            // ---- Switch B (DNIC 2351): trunk-in on port 0, access (echo) on port 1 ----
            SwitchConfig cfgB = new SwitchConfig { Dnic = "2351" };
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2352", DestPort = 1 });
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 0 });

            SwitchNode nodeB = new SwitchNode(cfgB);
            XotSwitchPort bTrunk = new XotSwitchPort(
                new PortConfig { Id = 0, Type = PortType.Trunk, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            XotSwitchPort bAccess = new XotSwitchPort(
                new PortConfig { Id = 1, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            nodeB.AddPort(bTrunk);
            nodeB.AddPort(bAccess);
            SwitchCore coreB = new SwitchCore(nodeB);
            bTrunk.Start();
            bAccess.Start();

            // ---- Switch A (DNIC 2350): access (caller) on port 0, trunk-out to B on port 1 ----
            SwitchConfig cfgA = new SwitchConfig { Dnic = "2350" };
            cfgA.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });

            SwitchNode nodeA = new SwitchNode(cfgA);
            XotSwitchPort aAccess = new XotSwitchPort(
                new PortConfig { Id = 0, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            XotSwitchPort aTrunk = new XotSwitchPort(
                new PortConfig { Id = 1, Type = PortType.Trunk, Mode = PortMode.Dte, Host = "127.0.0.1", Port = (ushort)bTrunk.ListenPort, LciBase = 1 },
                NullLogger.Instance);
            nodeA.AddPort(aAccess);
            nodeA.AddPort(aTrunk);
            SwitchCore coreA = new SwitchCore(nodeA);
            aAccess.Start();
            aTrunk.Start();

            using X25Client echo = new X25Client { ServiceName = "echo" };
            using X25Client caller = new X25Client { ServiceName = "caller" };
            try
            {
                ManualResetEventSlim echoAccepted = new ManualResetEventSlim(false);
                echo.SetOnIncoming((c, vc) =>
                {
                    c.Accept(vc);
                    echoAccepted.Set();
                });
                echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    byte[] reply = new byte[length];
                    Array.Copy(data, 0, reply, 0, length);
                    c.Send(vc, reply, length, qBit);
                });
                echo.Open(new X25ClientConfig
                {
                    Host = "127.0.0.1",
                    Port = (ushort)bAccess.ListenPort,
                    LocalAddress = "23520001",
                    Framing = X25ClientFraming.Xot
                }, NullLogger.Instance);

                byte[]? received = null;
                ManualResetEventSlim gotEcho = new ManualResetEventSlim(false);
                caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    received = new byte[length];
                    Array.Copy(data, 0, received, 0, length);
                    gotEcho.Set();
                });
                caller.Open(new X25ClientConfig
                {
                    Host = "127.0.0.1",
                    Port = (ushort)aAccess.ListenPort,
                    LocalAddress = "23500001",
                    Framing = X25ClientFraming.Xot
                }, NullLogger.Instance);

                X25Vc? vc = caller.CallWait("23520001", 4000);
                Assert.That(vc, Is.Not.Null, "the call should route across the trunk and be accepted");
                Assert.That(echoAccepted.Wait(4000), Is.True, "the echo on switch B should receive the onward call");

                Assert.That(caller.SendText(vc!, "ACROSS"), Is.True);
                Assert.That(gotEcho.Wait(4000), Is.True, "the echo should return across the trunk");
                Assert.That(Encoding.UTF8.GetString(received!), Is.EqualTo("ACROSS"));
            }
            finally
            {
                aAccess.Stop();
                aTrunk.Stop();
                bTrunk.Stop();
                bAccess.Stop();
            }
        }
    }
}
