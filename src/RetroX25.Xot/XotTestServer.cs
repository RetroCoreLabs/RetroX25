//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RetroX25.Xot
{
    /// <summary>
    /// Lightweight XOT server for testing (RFC 1613).
    /// </summary>
    /// <remarks>
    /// The server serves one client at a time. The socket, the framing and the receive thread of that
    /// client belong to <see cref="XotConnection"/>; this class listens, accepts, sends, and turns the
    /// connection's events into the ones the test window listens to.
    /// </remarks>
    public class XotTestServer : IDisposable
    {
        private readonly int _listenPort;
        private readonly object _gate = new object();
        private TcpListener? _listener;
        private XotConnection? _connection;
        private Thread? _acceptThread;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private volatile bool _stopping;

        /// <summary>
        /// Raised on the receive thread for each complete XOT packet read from the connected client.
        /// </summary>
        /// <remarks>
        /// The event data holds the whole packet, the 4-byte XOT header followed by the X.25 packet, with
        /// direction <c>"RX"</c>. A packet with a version other than 0 or an illegal length breaks the XOT
        /// framing, so RFC 1613 requires the connection to be closed: the reason is reported through
        /// <see cref="StatusChanged"/> and <see cref="ClientDisconnected"/> follows.
        /// </remarks>
        public event EventHandler<XotDataEventArgs>? DataReceived;

        /// <summary>
        /// Raised after <see cref="SendPacket"/> has written a packet to the connected client.
        /// </summary>
        /// <remarks>
        /// The event data holds the whole packet as sent, the 4-byte XOT header followed by the X.25 packet,
        /// with direction <c>"TX"</c>.
        /// </remarks>
        public event EventHandler<XotDataEventArgs>? DataSent;

        /// <summary>
        /// Raised with a short text line whenever the server has something to report, such as starting,
        /// stopping, a client arriving or leaving, a rejected extra client, or a send, accept or receive error.
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Raised on the accept thread when a client is accepted.
        /// </summary>
        /// <remarks>
        /// The string is the client's remote endpoint, or <c>"unknown"</c> when it is not available.
        /// </remarks>
        public event EventHandler<string>? ClientConnected;

        /// <summary>
        /// Raised exactly once for each accepted client when that client's connection ends: the client
        /// closed it, the XOT framing broke, a read or write failed, or a send failed.
        /// </summary>
        /// <remarks>
        /// It is not raised while <see cref="Stop"/> or <see cref="Dispose"/> closes the client.
        /// </remarks>
        public event EventHandler? ClientDisconnected;

        /// <summary>
        /// Whether the server is listening; true from a successful <see cref="Start"/> until <see cref="Stop"/>.
        /// </summary>
        public bool IsListening { get; private set; }

        /// <summary>
        /// Whether a client is connected: its connection is open and nothing has ended it.
        /// </summary>
        /// <remarks>
        /// The server serves one client at a time and closes any other client that connects while this is true.
        /// </remarks>
        public bool HasClient => _connection?.IsOpen ?? false;

        /// <summary>
        /// The listen address in <c>0.0.0.0:port</c> form; the server listens on all local addresses.
        /// </summary>
        public string ListenEndpoint => $"0.0.0.0:{_listenPort}";

        /// <summary>
        /// The TCP port the server is listening on. When the constructor was given port 0, this is the
        /// port the operating system chose.
        /// </summary>
        /// <value>
        /// The port in use, or the port given to the constructor while the server is not listening.
        /// </value>
        public int ListenPort
        {
            get
            {
                TcpListener? listener = _listener;
                if (IsListening && listener != null)
                {
                    return ((IPEndPoint)listener.LocalEndpoint).Port;
                }

                return _listenPort;
            }
        }

        /// <summary>
        /// The remote endpoint of the connected client, or null when no client is connected.
        /// </summary>
        public string? ClientEndpoint { get; private set; }

        /// <summary>
        /// Creates a server for the given port. Nothing listens until <see cref="Start"/> is called.
        /// </summary>
        /// <param name="listenPort">
        /// The TCP port to listen on; 0 lets the operating system choose, and <see cref="ListenPort"/>
        /// then tells which.
        /// </param>
        public XotTestServer(int listenPort)
        {
            _listenPort = listenPort;
        }

        /// <summary>
        /// Starts listening and accepting clients.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when listening, or when it already was; <see langword="false"/> when
        /// the port could not be opened, which is reported through <see cref="StatusChanged"/>.
        /// </returns>
        public bool Start()
        {
            if (IsListening)
            {
                return true;
            }

            try
            {
                _stopping = false;
                _listener = new TcpListener(IPAddress.Any, _listenPort);
                _listener.Start();

                IsListening = true;
                StatusChanged?.Invoke(this, $"Server listening on port {ListenPort}");

                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                _acceptThread = new Thread(() => AcceptLoop(token))
                {
                    IsBackground = true,
                    Name = "XOT Server Accept"
                };
                _acceptThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                // A port that is taken, reserved or not allowed is reported and Start returns false.
                StatusChanged?.Invoke(this, $"Failed to start server: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stops listening and closes the connected client, if any. <see cref="ClientDisconnected"/>
        /// is not raised for that client.
        /// </summary>
        public void Stop()
        {
            if (!IsListening)
            {
                return;
            }

            StatusChanged?.Invoke(this, "Stopping server...");

            _stopping = true;
            IsListening = false;
            _cts?.Cancel();

            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
                // The listener socket was already closed, which is what Stop is asking for.
            }

            _connection?.Close();

            StatusChanged?.Invoke(this, "Server stopped");
        }

        /// <summary>
        /// Wraps an X.25 packet in the XOT header and sends it to the connected client.
        /// </summary>
        /// <param name="x25Packet">
        /// The X.25 packet, with no XOT header. XOT carries legal X.25 packet lengths only, 3 to 4096
        /// bytes; anything else is refused here instead of being sent for the client to close on.
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
                StatusChanged?.Invoke(this, "No client connected");
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

        private void AcceptLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsListening && _listener != null)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();

                    if (HasClient)
                    {
                        StatusChanged?.Invoke(this, "Rejected additional client (already have connection)");
                        client.Close();
                        continue;
                    }

                    XotConnection connection = new XotConnection(client, "server");
                    connection.PacketReceived += packet => OnPacketReceived(packet);
                    connection.Closed += (reason, detail) => OnClosed(connection, reason, detail);

                    lock (_gate)
                    {
                        _connection = connection;
                        ClientEndpoint = connection.RemoteEndpoint;
                    }

                    StatusChanged?.Invoke(this, $"Client connected: {connection.RemoteEndpoint}");
                    ClientConnected?.Invoke(this, connection.RemoteEndpoint);
                    connection.Start();
                }
                catch (ObjectDisposedException)
                {
                    // The listener was stopped.
                    break;
                }
                catch (SocketException ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        StatusChanged?.Invoke(this, $"Accept error: {ex.Message}");
                    }
                }
            }
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
            string endpoint = connection.RemoteEndpoint;
            lock (_gate)
            {
                if (_connection == connection)
                {
                    _connection = null;
                    ClientEndpoint = null;
                }
            }

            if (_stopping || _disposed)
            {
                return;
            }

            if (reason == XotCloseReason.FramingFailed)
            {
                StatusChanged?.Invoke(this, $"Closed client {endpoint}: {detail}");
            }
            else if (reason == XotCloseReason.Error)
            {
                StatusChanged?.Invoke(this, $"Receive error: {detail}");
            }

            StatusChanged?.Invoke(this, $"Client disconnected: {endpoint}");
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Stops the server through <see cref="Stop"/> and releases the cancellation source. Calls after the
        /// first do nothing.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
