//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Threading;
using RetroIO.Protocols.X25.Monitor;

using RetroIO.Protocols.X25.Switch;
namespace RetroX25.Switch
{
    /// <summary>
    /// Raised for every raw X.25 packet crossing a port, in both directions.
    /// </summary>
    /// <param name="portId">
    /// The identifier of the port the packet crossed.
    /// </param>
    /// <param name="inbound">
    /// True when the packet was received by the port, false when transmitted.
    /// </param>
    /// <param name="frame">
    /// The raw X.25 packet bytes.
    /// </param>
    public delegate void PortFrameCaptured(int portId, bool inbound, byte[] frame);

    /// <summary>
    /// Bridges switch ports to a <see cref="MonitorServer"/>: it captures every packet (decoding with
    /// <see cref="X25CaptureDecoder"/> and publishing the record) and periodically publishes a link/port
    /// status snapshot built from the attached ports' counters.
    /// </summary>
    public sealed class MonitorHost : IDisposable
    {
        private readonly MonitorServer _server;
        private readonly List<XotSwitchPort> _ports = new List<XotSwitchPort>();
        private long _seq;

        /// <summary>
        /// Per-port route summary (port id → the routes that send traffic out it), populated by the host so
        /// each status snapshot can report which route is assigned to a link.
        /// </summary>
        public Dictionary<int, string> PortRoutes { get; } = new Dictionary<int, string>();
        private Timer? _statusTimer;
        private Func<int>? _activeCircuits;

        // The node's self-identity plus declared neighbours, published on the monitor plane so viewers can
        // build a network map. Neighbour Up state is refreshed from live port state on each publish.
        private TopologySnapshot? _topology;
        private long _uptimeStartMs;

        /// <summary>
        /// Creates a monitoring host that publishes to the given server.
        /// </summary>
        /// <param name="server">
        /// The monitoring server that fans captures out to viewer clients.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="server"/> is null.
        /// </exception>
        public MonitorHost(MonitorServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>
        /// Attaches to a port so its traffic is captured and its counters are reported.
        /// </summary>
        /// <param name="port">
        /// The port to observe.
        /// </param>
        public void Attach(XotSwitchPort port)
        {
            _ports.Add(port);
            port.FrameCaptured += OnFrame;
        }

        /// <summary>
        /// Starts periodically publishing status snapshots to viewers.
        /// </summary>
        /// <param name="intervalMs">
        /// Publish interval in milliseconds.
        /// </param>
        /// <param name="activeCircuits">
        /// Supplies the switch's active circuit count, or null to report zero.
        /// </param>
        public void StartStatusTimer(int intervalMs, Func<int>? activeCircuits)
        {
            _activeCircuits = activeCircuits;
            _statusTimer = new Timer(_ => { PublishStatus(); PublishTopology(); }, null, intervalMs, intervalMs);
        }

        /// <summary>
        /// Sets this node's topology (self identity plus declared neighbours) for periodic publishing. The
        /// neighbours' <see cref="NeighbourInfo.Up"/> state is refreshed from live port state on each publish.
        /// </summary>
        /// <param name="topology">
        /// The topology snapshot describing this node and its declared neighbours.
        /// </param>
        /// <param name="uptimeStartMs">
        /// The <see cref="Environment.TickCount64"/> value captured when the node started, used to compute
        /// uptime on each publish.
        /// </param>
        public void SetTopology(TopologySnapshot topology, long uptimeStartMs)
        {
            _topology = topology;
            _uptimeStartMs = uptimeStartMs;
        }

        /// <summary>
        /// Refreshes the topology's live fields (uptime and per-neighbour link state) and publishes it.
        /// </summary>
        public void PublishTopology()
        {
            TopologySnapshot? topology = _topology;
            if (topology == null)
            {
                return;
            }

            topology.UptimeMs = Environment.TickCount64 - _uptimeStartMs;
            for (int i = 0; i < topology.Neighbours.Count; i++)
            {
                NeighbourInfo neighbour = topology.Neighbours[i];
                XotSwitchPort? port = FindPort(neighbour.ViaPortId);
                neighbour.Up = port != null && port.PeerConnected;
            }

            _server.PublishTopology(topology);
        }

        /// <summary>
        /// Builds and publishes a status snapshot from the attached ports' current counters.
        /// </summary>
        public void PublishStatus()
        {
            StatusSnapshot snapshot = new StatusSnapshot
            {
                TimestampMs = Environment.TickCount64,
                ActiveCircuits = _activeCircuits != null ? _activeCircuits() : 0
            };

            for (int i = 0; i < _ports.Count; i++)
            {
                XotSwitchPort port = _ports[i];
                PortRoutes.TryGetValue(port.Id, out string? routeSummary);
                snapshot.Ports.Add(new PortStatus
                {
                    Id = port.Id,
                    Mode = port.Config.Mode.ToString(),
                    Routes = routeSummary ?? string.Empty,
                    Active = port.Active,
                    PeerConnected = port.PeerConnected,
                    CallsIn = port.Counters.CallsIn,
                    CallsOut = port.Counters.CallsOut,
                    CallsCleared = port.Counters.CallsCleared,
                    PacketsIn = port.Counters.PacketsIn,
                    PacketsOut = port.Counters.PacketsOut,
                    ConnectAttempts = port.ConnectAttempts,
                    LastConnectedMs = port.LastConnectedMs,
                    LastDisconnectedMs = port.LastDisconnectedMs,
                    BytesSent = port.BytesSent,
                    BytesReceived = port.BytesReceived
                });
            }

            _server.PublishStatus(snapshot);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _statusTimer?.Dispose();
            _statusTimer = null;
        }

        private XotSwitchPort? FindPort(int portId)
        {
            for (int i = 0; i < _ports.Count; i++)
            {
                if (_ports[i].Id == portId)
                {
                    return _ports[i];
                }
            }

            return null;
        }

        private void OnFrame(int portId, bool inbound, byte[] frame)
        {
            // Under X25MON/1 the switch does NOT decode: it ships the raw X.25 frame plus a small header and
            // the viewer decodes it (see docs/MONITOR_PROTOCOL_V1.md section 5). This keeps capture messages
            // tiny and means the switch needs no X.25 decoder.
            long seq = Interlocked.Increment(ref _seq);
            CaptureDirection direction = inbound ? CaptureDirection.Rx : CaptureDirection.Tx;
            CaptureRecord record = new CaptureRecord
            {
                Seq = seq,
                Port = portId,
                Direction = direction,
                TimestampMs = Environment.TickCount64,
                Frame = frame,
                TotalLength = frame.Length
            };
            _server.Publish(record);
        }
    }
}
