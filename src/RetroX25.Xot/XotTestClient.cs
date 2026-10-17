//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Net.Sockets;

namespace RetroX25.Xot
{
    /// <summary>
    /// Lightweight XOT client for testing (RFC 1613).
    /// </summary>
    /// <remarks>
    /// The socket, the framing and the receive thread belong to <see cref="XotConnection"/>; this class
    /// connects, sends, and turns the connection's events into the ones the test window listens to.
    /// </remarks>
    public class XotTestClient : IDisposable
    {
        private readonly string _remoteHost;
        private readonly int _remotePort;
        private readonly object _gate = new object();
        private XotConnection? _connection;
        private bool _disposed;

        /// <summary>
        /// Raised on the receive thread for each complete XOT packet read from the server.
        /// </summary>
        /// <remarks>
        /// The event data holds the whole packet, the 4-byte XOT header followed by the X.25 packet, with
        /// direction <c>"RX"</c>. A packet with a version other than 0 or an illegal length breaks the XOT
        /// framing, so RFC 1613 requires the connection to be closed: the reason is reported through
        /// <see cref="StatusChanged"/> and <see cref="Disconnected"/> follows.
        /// </remarks>
        public event EventHandler<XotDataEventArgs>? DataReceived;

        /// <summary>
        /// Raised after <see cref="SendPacket"/> has written a packet to the server.
        /// </summary>
        /// <remarks>
        /// The event data holds the whole packet as sent, the 4-byte XOT header followed by the X.25 packet,
        /// with direction <c>"TX"</c>.
        /// </remarks>
        public event EventHandler<XotDataEventArgs>? DataSent;

        /// <summary>
        /// Raised with a short text line whenever the client has something to report, such as connecting,
        /// connected, a timeout, a send or receive error, or a disconnect.
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Raised exactly once when a connection ends: after <see cref="Disconnect"/>, when the server
        /// closes it, when the XOT framing breaks, or when a read or write on the socket fails.
        /// </summary>
        /// <remarks>
        /// It is not raised by <see cref="Dispose"/>.
        /// </remarks>
        public event EventHandler? Disconnected;

        /// <summary>
        /// Whether a connection is open: <see cref="Connect"/> succeeded and nothing has ended it since.
        /// </summary>
        public bool IsConnected => _connection?.IsOpen ?? false;

        /// <summary>
        /// The server address in <c>host:port</c> form, as given to the constructor.
        /// </summary>
        public string RemoteEndpoint => $"{_remoteHost}:{_remotePort}";

        /// <summary>
        /// Creates a client for the given XOT server. No connection is made until <see cref="Connect"/> is called.
        /// </summary>
        /// <param name="remoteHost">
        /// The host name or IP address of the XOT server.
        /// </param>
        /// <param name="remotePort">
        /// The TCP port of the XOT server.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="remoteHost"/> is null.
        /// </exception>
        public XotTestClient(string remoteHost, int remotePort)
        {
            _remoteHost = remoteHost ?? throw new ArgumentNullException(nameof(remoteHost));
            _remotePort = remotePort;
        }

        /// <summary>
        /// Connects to the XOT server and starts reading from it.
        /// </summary>
        /// <param name="timeoutMs">
        /// How long to wait for the TCP connection, in milliseconds.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when connected, or when a connection is already open;
        /// <see langword="false"/> on a timeout or a connection failure, which is reported through
        /// <see cref="StatusChanged"/>.
        /// </returns>
        public bool Connect(int timeoutMs = 30000)
        {
            if (IsConnected)
            {
                return true;
            }

            TcpClient? tcp = null;
            try
            {
                StatusChanged?.Invoke(this, $"Connecting to {_remoteHost}:{_remotePort}...");

                tcp = new TcpClient();
                IAsyncResult connectResult = tcp.BeginConnect(_remoteHost, _remotePort, null, null);
                if (!connectResult.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    StatusChanged?.Invoke(this, "Connection timeout");
                    tcp.Close();
                    return false;
                }

                tcp.EndConnect(connectResult);

                XotConnection connection = new XotConnection(tcp, "client");
                connection.PacketReceived += packet => OnPacketReceived(packet);
                connection.Closed += (reason, detail) => OnClosed(connection, reason, detail);
                lock (_gate)
                {
                    _connection = connection;
                }

                connection.Start();
                StatusChanged?.Invoke(this, $"Connected to {_remoteHost}:{_remotePort}");
                return true;
            }
            catch (Exception ex)
            {
                // Any failure to connect (an unknown host, a refused connection, a bad port) is reported
                // to the window and the connect call returns false; none of them is an error in this class.
                StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
                tcp?.Close();
                return false;
            }
        }

        /// <summary>
        /// Wraps an X.25 packet in the XOT header and sends it to the server.
        /// </summary>
        /// <param name="x25Packet">
        /// The X.25 packet, with no XOT header. XOT carries legal X.25 packet lengths only, 3 to 4096
        /// bytes; anything else is refused here instead of being sent for the server to close on.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the packet was written; otherwise the reason is reported through
        /// <see cref="StatusChanged"/>.
        /// </returns>
        public bool SendPacket(byte[] x25Packet)
        {
            XotConnection? connection = _connection;
            if (connection == null || !connection.IsOpen)
            {
                StatusChanged?.Invoke(this, "Not connected");
                return false;
            }

            if (x25Packet == null || x25Packet.Length == 0)
            {
                StatusChanged?.Invoke(this, "Cannot send empty packet");
                return false;
            }

            if (!connection.TrySend(x25Packet, out byte[]? xotPacket, out string? error))
            {
                StatusChanged?.Invoke(this, $"Send failed: {error}");
                return false;
            }

            DataSent?.Invoke(this, new XotDataEventArgs
            {
                Data = xotPacket!,
                Timestamp = DateTime.Now,
                Direction = "TX"
            });

            return true;
        }

        /// <summary>
        /// Closes the connection to the server, if one is open. <see cref="Disconnected"/> follows.
        /// </summary>
        public void Disconnect()
        {
            XotConnection? connection = _connection;
            if (connection == null || !connection.IsOpen)
            {
                return;
            }

            StatusChanged?.Invoke(this, "Disconnecting...");
            connection.Close();
        }

        private void OnPacketReceived(byte[] xotPacket)
        {
            DataReceived?.Invoke(this, new XotDataEventArgs
            {
                Data = xotPacket,
                Timestamp = DateTime.Now,
                Direction = "RX"
            });
        }

        private void OnClosed(XotConnection connection, XotCloseReason reason, string detail)
        {
            lock (_gate)
            {
                if (_connection == connection)
                {
                    _connection = null;
                }
            }

            if (_disposed)
            {
                return;
            }

            switch (reason)
            {
                case XotCloseReason.ClosedLocally:
                    StatusChanged?.Invoke(this, "Disconnected");
                    break;
                case XotCloseReason.ClosedByRemote:
                    StatusChanged?.Invoke(this, "Connection closed by remote host");
                    break;
                case XotCloseReason.FramingFailed:
                    StatusChanged?.Invoke(this, $"Closed: {detail}");
                    break;
                default:
                    StatusChanged?.Invoke(this, $"Receive error: {detail}");
                    break;
            }

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Closes the connection and releases it. Calls after the first do nothing.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Disconnect"/>, this does not raise <see cref="StatusChanged"/> or
        /// <see cref="Disconnected"/>.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connection?.Close();
        }
    }

    /// <summary>
    /// Event data for the XOT test client and server data events.
    /// </summary>
    public class XotDataEventArgs : EventArgs
    {
        /// <summary>
        /// The whole XOT packet: the 4-byte XOT header followed by the X.25 packet.
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// The local time the packet was sent or received, taken from <see cref="DateTime.Now"/>.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// <c>"TX"</c> for a packet this side sent, <c>"RX"</c> for a packet this side received.
        /// </summary>
        public string Direction { get; set; } = string.Empty;
    }
}
