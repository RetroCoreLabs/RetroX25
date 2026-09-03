using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using RetroIO.Link;
using RetroIO.Link.Framing;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Switch
{
    /// <summary>
    /// A switch port that carries X.25 over a single TCP connection (XOT framing): access ports listen for
    /// a terminal, trunk ports connect to a peer. It decodes inbound X.25 packets into port events and
    /// builds outbound packets for the engine's calls, data and clears.
    /// </summary>
    /// <remarks>
    /// Virtual circuits are multiplexed by logical channel over the one connection. Each circuit keeps a
    /// small send/receive sequence so forwarded data carries valid sequence numbers, and received data is
    /// acknowledged with a receive-ready packet. Flow-control windowing is kept intentionally simple.
    /// </remarks>
    public sealed class XotSwitchPort : ISwitchPort
    {
        private sealed class Vc
        {
            public byte SendSeq;
            public byte RecvSeq;
        }

        private readonly ILogger _logger;
        private readonly object _lock = new object();
        private readonly Dictionary<ushort, Vc> _vcs = new Dictionary<ushort, Vc>();

        private TcpLinkTransport? _transport;
        private XotFrameCodec? _codec;
        private ushort _nextOutLci;
        private bool _active;
        private bool _peerConnected;
        private int _connectAttempts;
        private long _lastConnectedMs;
        private long _lastDisconnectedMs;
        private long _bytesSent;
        private long _bytesReceived;

        /// <inheritdoc/>
        public int Id { get; }

        /// <inheritdoc/>
        public PortConfig Config { get; }

        /// <inheritdoc/>
        public bool Active => _active;

        /// <summary>
        /// The TCP port the transport is actually bound to. For a listening (DCE) port opened on port 0
        /// this is the OS-assigned port, available after <see cref="Start"/>; otherwise the configured port.
        /// </summary>
        public int ListenPort => _transport != null ? _transport.Port : Config.Port;

        /// <summary>
        /// True when a peer is actually connected over this port's transport. For a DCE (listening) port
        /// this means a peer has dialed in; for a DTE (dialling) port it means the outbound connection is up.
        /// Distinct from <see cref="Active"/>, which for a listener only means the socket is bound.
        /// </summary>
        public bool PeerConnected => _peerConnected;

        /// <summary>
        /// Number of times this port has attempted to open its transport (dial-out attempts for a DTE port,
        /// listener binds for a DCE port).
        /// </summary>
        public int ConnectAttempts => _connectAttempts;

        /// <summary>
        /// Milliseconds (monotonic) when a peer last connected, or 0 if never.
        /// </summary>
        public long LastConnectedMs => _lastConnectedMs;

        /// <summary>
        /// Milliseconds (monotonic) when a peer last disconnected, or 0 if never.
        /// </summary>
        public long LastDisconnectedMs => _lastDisconnectedMs;

        /// <summary>
        /// Total X.25 packet bytes sent on this port (excludes XOT/HDLC framing).
        /// </summary>
        public long BytesSent => System.Threading.Interlocked.Read(ref _bytesSent);

        /// <summary>
        /// Total X.25 packet bytes received on this port (excludes XOT/HDLC framing).
        /// </summary>
        public long BytesReceived => System.Threading.Interlocked.Read(ref _bytesReceived);

        /// <inheritdoc/>
        public PortCounters Counters { get; } = new PortCounters();

        /// <inheritdoc/>
        public event PortIncomingCall? IncomingCall;

        /// <inheritdoc/>
        public event PortCallConnected? CallConnected;

        /// <inheritdoc/>
        public event PortDataReceived? DataReceived;

        /// <inheritdoc/>
        public event PortCallCleared? CallCleared;

        /// <inheritdoc/>
        public event PortStateChanged? StateChanged;

        /// <summary>
        /// Raised for every raw X.25 packet crossing this port, in both directions, before/after framing.
        /// A monitoring host subscribes to this to capture traffic for the remote packet stream.
        /// </summary>
        public event PortFrameCaptured? FrameCaptured;

        /// <summary>
        /// Creates an XOT switch port.
        /// </summary>
        /// <param name="config">
        /// The port configuration.
        /// </param>
        /// <param name="logger">
        /// Logger for transport diagnostics.
        /// </param>
        public XotSwitchPort(PortConfig config, ILogger logger)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;
            Id = config.Id;
            _nextOutLci = config.LciBase == 0 ? (ushort)1 : config.LciBase;
        }

        /// <inheritdoc/>
        public bool Start()
        {
            // Access ports run as DCE and listen; trunk/DTE ports connect out.
            bool isServer = Config.Mode == PortMode.Dce;
            string host = isServer ? (string.IsNullOrEmpty(Config.Host) ? "0.0.0.0" : Config.Host) : Config.Host;

            // A dialling (DTE) trunk keeps retrying/reconnecting so the link comes up when the peer appears
            // and recovers after a drop; a listening (DCE) port re-accepts automatically.
            int reconnectDelayMs = isServer ? 0 : 3000;
            _transport = new TcpLinkTransport(_logger, "port" + Id, isServer, host, Config.Port, reconnectDelayMs);
            _codec = new XotFrameCodec("port" + Id, new FrameSink(_transport));
            _codec.FrameReceived += OnFrame;
            _transport.DataReceived += (name, bytes) =>
            {
                // Byte counting is done at the X.25-packet layer (in OnFrame), not here on the raw socket
                // chunk, so the C# and C sides report identical byte totals even across different framing
                // (XOT vs HDLC). See docs/MONITOR_PROTOCOL_V1.md section 7.
                _codec.ProcessBytes(bytes);
            };
            _transport.Opened += name =>
            {
                _peerConnected = true;
                _active = true;
                _lastConnectedMs = Environment.TickCount64;
                _logger.LogInformation("Port {Id} ({Mode} {Type}): peer connected", Id, Config.Mode, Config.Type);
                StateChanged?.Invoke(this, true);
            };
            _transport.Closed += (name, reason) =>
            {
                _peerConnected = false;
                _active = false;
                _lastDisconnectedMs = Environment.TickCount64;
                _logger.LogInformation("Port {Id} ({Mode} {Type}): peer disconnected ({Reason})", Id, Config.Mode, Config.Type, reason);
                StateChanged?.Invoke(this, false);
            };

            System.Threading.Interlocked.Increment(ref _connectAttempts);
            bool ok = _transport.Open();
            if (isServer)
            {
                // A listening port is considered up (bound) once the listener starts; a peer may not be
                // connected yet -- PeerConnected reflects that separately.
                _active = ok;
                _logger.LogInformation("Port {Id} ({Mode} {Type}): listening on {Host}:{Port}",
                    Id, Config.Mode, Config.Type, host, Config.Port);
            }
            else
            {
                _logger.LogInformation("Port {Id} ({Mode} {Type}): dial {Host}:{Port} {Result}",
                    Id, Config.Mode, Config.Type, host, Config.Port, ok ? "connected" : "FAILED");
            }
            return ok;
        }

        /// <inheritdoc/>
        public void Stop()
        {
            _transport?.Close();
            _active = false;
        }

        /// <inheritdoc/>
        public ushort PlaceCall(string calledAddress, string callingAddress, byte[]? callUserData)
        {
            ushort lci = AllocateOutLci();
            lock (_lock)
            {
                _vcs[lci] = new Vc();
            }

            _logger.LogInformation("Port {Id}: placing call to {Called} (calling {Calling}) LCI={Lci}",
                Id, calledAddress, callingAddress, lci);
            Send(X25PacketBuilder.CreateCallRequest(lci, calledAddress, callingAddress, null, callUserData));
            Counters.IncrementCallsOut();
            return lci;
        }

        /// <inheritdoc/>
        public void AcceptCall(ushort lci)
        {
            Send(X25PacketBuilder.CreateCallAccepted(lci));
        }

        /// <inheritdoc/>
        public bool SendData(ushort lci, ReadOnlySpan<byte> data, bool qBit, bool mBit, bool dBit)
        {
            if (data.Length == 0)
            {
                return false;
            }

            Vc? vc;
            byte ps;
            byte pr;
            lock (_lock)
            {
                if (!_vcs.TryGetValue(lci, out vc))
                {
                    return false;
                }

                ps = vc.SendSeq;
                vc.SendSeq = (byte)((vc.SendSeq + 1) & 0x07);
                pr = vc.RecvSeq;
            }

            Send(X25PacketBuilder.CreateDataPacket(lci, ps, pr, data.ToArray(), mBit, qBit, dBit));
            Counters.IncrementPacketsOut();
            return true;
        }

        /// <inheritdoc/>
        public void ClearCall(ushort lci, byte cause, byte diagnostic)
        {
            Send(X25PacketBuilder.CreateClearRequest(lci, cause, diagnostic));
            Remove(lci);
        }

        private void OnFrame(string linkId, ReadOnlySpan<byte> frame)
        {
            byte[] pkt = frame.ToArray();

            // Count received bytes at the X.25-packet layer (excludes XOT/HDLC framing) so both
            // implementations report identical totals -- see docs/MONITOR_PROTOCOL_V1.md section 7.
            System.Threading.Interlocked.Add(ref _bytesReceived, pkt.Length);

            // Capture the inbound packet for the monitoring stream before processing it.
            FrameCaptured?.Invoke(Id, true, pkt);

            X25PacketInfo info = X25PacketParser.ParsePacket(pkt);
            ushort lci = info.LCN;

            switch (info.PacketType)
            {
                case X25PacketType.CallRequest:
                    Counters.IncrementCallsIn();
                    lock (_lock)
                    {
                        _vcs[lci] = new Vc();
                    }
                    _logger.LogInformation("Port {Id}: incoming call {Calling} -> {Called} LCI={Lci}",
                        Id, info.CallingAddress, info.CalledAddress, lci);
                    IncomingCall?.Invoke(this, lci, info.CalledAddress, info.CallingAddress, Array.Empty<byte>());
                    break;

                case X25PacketType.CallAccepted:
                    _logger.LogInformation("Port {Id}: call accepted LCI={Lci}", Id, lci);
                    CallConnected?.Invoke(this, lci);
                    break;

                case X25PacketType.Data:
                {
                    Counters.IncrementPacketsIn();
                    AckData(lci, info.PS);
                    // The data payload follows the packet header (3 octets modulo 8, 4 modulo 128).
                    int header = info.Modulo128 ? 4 : 3;
                    int payloadLen = pkt.Length > header ? pkt.Length - header : 0;
                    byte[] payload = new byte[payloadLen];
                    if (payloadLen > 0)
                    {
                        Array.Copy(pkt, header, payload, 0, payloadLen);
                    }
                    DataReceived?.Invoke(this, lci, payload, payloadLen, info.QBit, info.MBit, info.DBit);
                    break;
                }

                case X25PacketType.ClearRequest:
                    _logger.LogInformation("Port {Id}: clear LCI={Lci} cause={Cause} diag={Diag}",
                        Id, lci, info.ClearingCause, info.DiagnosticCode);
                    Send(X25PacketBuilder.CreateClearConfirmation(lci));
                    Counters.IncrementCallsCleared();
                    Remove(lci);
                    CallCleared?.Invoke(this, lci, info.ClearingCause, info.DiagnosticCode);
                    break;

                case X25PacketType.ClearConfirmation:
                    Remove(lci);
                    break;

                case X25PacketType.RestartRequest:
                    Send(X25PacketBuilder.CreateRestartConfirmation());
                    break;

                default:
                    // Flow-control and other supervisory packets need no action in this simple model.
                    break;
            }
        }

        private void AckData(ushort lci, byte ps)
        {
            byte pr;
            lock (_lock)
            {
                if (!_vcs.TryGetValue(lci, out Vc? vc))
                {
                    return;
                }

                vc.RecvSeq = (byte)((ps + 1) & 0x07);
                pr = vc.RecvSeq;
            }

            Send(X25PacketBuilder.CreateRR(lci, pr));
        }

        private void Remove(ushort lci)
        {
            lock (_lock)
            {
                _vcs.Remove(lci);
            }
        }

        private ushort AllocateOutLci()
        {
            lock (_lock)
            {
                // Advance to the next free channel within the port's range.
                ushort limit = (ushort)(Config.LciBase + Config.LciCount);
                for (int guard = 0; guard < Config.LciCount + 1; guard++)
                {
                    ushort candidate = _nextOutLci;
                    _nextOutLci++;
                    if (_nextOutLci >= limit || _nextOutLci == 0)
                    {
                        _nextOutLci = Config.LciBase == 0 ? (ushort)1 : Config.LciBase;
                    }
                    if (!_vcs.ContainsKey(candidate))
                    {
                        return candidate;
                    }
                }

                return _nextOutLci;
            }
        }

        private void Send(byte[] packet)
        {
            // Capture the outbound packet for the monitoring stream.
            FrameCaptured?.Invoke(Id, false, packet);
            // Count sent bytes at the X.25-packet layer (excludes the XOT header) so both implementations
            // report identical totals -- see docs/MONITOR_PROTOCOL_V1.md section 7.
            System.Threading.Interlocked.Add(ref _bytesSent, packet.Length);
            _codec?.SendFrame(packet);
        }

        /// <summary>
        /// Adapts the link transport to the framing codec's send interface.
        /// </summary>
        private sealed class FrameSink : IFrameTransport
        {
            private readonly TcpLinkTransport _transport;

            public FrameSink(TcpLinkTransport transport)
            {
                _transport = transport;
            }

            public void Send(ReadOnlySpan<byte> bytes)
            {
                _transport.Write(bytes);
            }
        }
    }
}
