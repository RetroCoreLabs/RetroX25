using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using RetroIO.Protocols.X25.Monitor;
using RetroX25.Switch;

namespace RetroX25.Tests
{
    /// <summary>
    /// End-to-end monitoring tests: a <see cref="MonitorServer"/> is attached to a live XOT switch via a
    /// <see cref="MonitorHost"/>, a viewer connects, and traffic driven through the switch produces both
    /// decoded capture records and link/port status snapshots at the viewer.
    /// </summary>
    [TestFixture]
    public class MonitorSwitchInteropTests
    {
        [Test]
        public void SwitchTraffic_IsCapturedAndStreamedToViewer()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using MonitorServer monitor = new MonitorServer("127.0.0.1", 0);
            Assert.That(monitor.Start(), Is.True);

            MonitorHost host = new MonitorHost(monitor);
            host.Attach(sw.P0);
            host.Attach(sw.P1);

            List<CaptureRecord> captures = new List<CaptureRecord>();
            object gate = new object();
            MonitorFrameReader reader = new MonitorFrameReader();
            reader.MessageReceived += m =>
            {
                if (string.Equals(m.Type, MonitorProtocol.TypeCapture, StringComparison.Ordinal) && m.Capture != null)
                {
                    lock (gate)
                    {
                        captures.Add(m.Capture);
                    }
                }
            };

            using TcpClient viewer = SwitchXotHarness.ConnectViewer(monitor.ListenPort, reader);
            Assert.That(SpinWait.SpinUntil(() => monitor.ClientCount == 1, 3000), Is.True);

            SwitchXotHarness.RunCallerEchoScenario(sw, "PING");

            Assert.That(SpinWait.SpinUntil(() => HasType(captures, gate, "CallRequest")
                    && HasType(captures, gate, "Data"), 3000),
                Is.True, "the viewer should receive Call Request and Data captures");

            // Under X25MON/1 the wire carries the raw frame; decoding it at the viewer must yield layers.
            lock (gate)
            {
                for (int i = 0; i < captures.Count; i++)
                {
                    CaptureRecord decoded = X25CaptureDecoder.Decode(captures[i].Frame, captures[i].Port,
                        captures[i].Direction, captures[i].Seq, captures[i].TimestampMs);
                    Assert.That(decoded.Layers.Count, Is.GreaterThan(0), "every capture decodes to layers");
                }
            }
        }

        [Test]
        public void SwitchStatus_IsPublishedToViewer()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using MonitorServer monitor = new MonitorServer("127.0.0.1", 0);
            Assert.That(monitor.Start(), Is.True);

            MonitorHost host = new MonitorHost(monitor);
            host.Attach(sw.P0);
            host.Attach(sw.P1);

            StatusSnapshot? lastStatus = null;
            object gate = new object();
            ManualResetEventSlim gotStatus = new ManualResetEventSlim(false);
            MonitorFrameReader reader = new MonitorFrameReader();
            reader.MessageReceived += m =>
            {
                if (string.Equals(m.Type, MonitorProtocol.TypeStatus, StringComparison.Ordinal) && m.Status != null)
                {
                    lock (gate)
                    {
                        lastStatus = m.Status;
                    }
                    gotStatus.Set();
                }
            };

            using TcpClient viewer = SwitchXotHarness.ConnectViewer(monitor.ListenPort, reader);
            Assert.That(SpinWait.SpinUntil(() => monitor.ClientCount == 1, 3000), Is.True);

            // Generate traffic so the counters move, then publish a status snapshot.
            SwitchXotHarness.RunCallerEchoScenario(sw, "PING");
            host.PublishStatus();

            Assert.That(gotStatus.Wait(3000), Is.True, "the viewer should receive a status snapshot");
            lock (gate)
            {
                Assert.That(lastStatus, Is.Not.Null);
                Assert.That(lastStatus!.Ports.Count, Is.EqualTo(2), "both switch ports are reported");

                uint totalCalls = 0;
                for (int i = 0; i < lastStatus.Ports.Count; i++)
                {
                    totalCalls += lastStatus.Ports[i].CallsIn + lastStatus.Ports[i].CallsOut;
                }
                Assert.That(totalCalls, Is.GreaterThan(0u), "the call through the switch should be counted");
            }
        }

        [Test]
        public void SwitchTopology_IsPublishedToViewer_WithNeighbours()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using MonitorServer monitor = new MonitorServer("127.0.0.1", 0);
            Assert.That(monitor.Start(), Is.True);

            MonitorHost host = new MonitorHost(monitor);
            host.Attach(sw.P0);
            host.Attach(sw.P1);

            // Declare this node with one neighbour reachable over port P1's id.
            TopologySnapshot topology = new TopologySnapshot
            {
                NodeId = "2352",
                Name = "SwitchC",
                Dnic = "2352",
                MonitorPort = monitor.ListenPort
            };
            topology.Neighbours.Add(new NeighbourInfo
            {
                Name = "SwitchB",
                MonitorHost = "127.0.0.1",
                MonitorPort = 4002,
                ViaPortId = sw.P1.Id,
                Up = false
            });
            host.SetTopology(topology, Environment.TickCount64);

            TopologySnapshot? received = null;
            ManualResetEventSlim gotTopology = new ManualResetEventSlim(false);
            MonitorFrameReader reader = new MonitorFrameReader();
            reader.MessageReceived += m =>
            {
                if (string.Equals(m.Type, MonitorProtocol.TypeTopology, StringComparison.Ordinal) && m.Topology != null)
                {
                    received = m.Topology;
                    gotTopology.Set();
                }
            };

            using TcpClient viewer = SwitchXotHarness.ConnectViewer(monitor.ListenPort, reader);
            Assert.That(SpinWait.SpinUntil(() => monitor.ClientCount == 1, 3000), Is.True);

            host.PublishTopology();

            Assert.That(gotTopology.Wait(3000), Is.True, "the viewer should receive a topology snapshot");
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Name, Is.EqualTo("SwitchC"));
            Assert.That(received.Neighbours.Count, Is.EqualTo(1));
            Assert.That(received.Neighbours[0].Name, Is.EqualTo("SwitchB"));
            Assert.That(received.Neighbours[0].MonitorPort, Is.EqualTo(4002));
        }

        [Test]
        public void CachedTopology_IsSentToNewViewerOnConnect()
        {
            using SwitchXotHarness.Running sw = SwitchXotHarness.StartTwoPortSwitch();
            using MonitorServer monitor = new MonitorServer("127.0.0.1", 0);
            Assert.That(monitor.Start(), Is.True);

            MonitorHost host = new MonitorHost(monitor);
            host.Attach(sw.P0);

            TopologySnapshot topology = new TopologySnapshot { Name = "SeedNode", Dnic = "2350" };
            host.SetTopology(topology, Environment.TickCount64);

            // Publish before any viewer connects: the server caches it.
            host.PublishTopology();

            TopologySnapshot? received = null;
            ManualResetEventSlim gotTopology = new ManualResetEventSlim(false);
            MonitorFrameReader reader = new MonitorFrameReader();
            reader.MessageReceived += m =>
            {
                if (string.Equals(m.Type, MonitorProtocol.TypeTopology, StringComparison.Ordinal) && m.Topology != null)
                {
                    received = m.Topology;
                    gotTopology.Set();
                }
            };

            // A viewer connecting afterwards should immediately receive the cached topology.
            using TcpClient viewer = SwitchXotHarness.ConnectViewer(monitor.ListenPort, reader);

            Assert.That(gotTopology.Wait(3000), Is.True, "a new viewer should receive the cached topology on connect");
            Assert.That(received!.Name, Is.EqualTo("SeedNode"));
        }

        private static bool HasType(List<CaptureRecord> captures, object gate, string packetType)
        {
            lock (gate)
            {
                for (int i = 0; i < captures.Count; i++)
                {
                    // The wire carries the raw frame; decode it to learn the packet type (decode-at-viewer).
                    CaptureRecord decoded = X25CaptureDecoder.Decode(captures[i].Frame, captures[i].Port,
                        captures[i].Direction, captures[i].Seq, captures[i].TimestampMs);
                    if (string.Equals(decoded.PacketType, packetType, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
