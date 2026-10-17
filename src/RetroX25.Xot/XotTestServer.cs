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
    /// Lightweight XOT server for testing (RFC 1613)
    /// </summary>
    public class XotTestServer : IDisposable
    {
        private readonly int _listenPort;
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private Thread? _acceptThread;
        private Thread? _receiveThread;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        /// <summary>
        /// Raised on the receive thread for each complete XOT packet read from the connected client.
        /// </summary>
        /// <remarks>
        /// The event data holds the whole packet, the 4-byte XOT header followed by the X.25 packet, with
        /// direction <c>"RX"</c>. Packets with a non-zero version or a length over 4096 bytes are skipped and
        /// reported through <see cref="StatusChanged"/> instead.
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
        /// Raised when the server closes the current client connection after a send failure or when its
        /// receive loop ends.
        /// </summary>
        /// <remarks>
        /// It is raised only if <see cref="HasClient"/> is still true at that moment, so a connection whose
        /// TCP state already reads as disconnected is cleaned up without it. <see cref="Stop"/> closes the
        /// client directly and does not raise it itself.
        /// </remarks>
        public event EventHandler? ClientDisconnected;

        /// <summary>
        /// Whether the server is listening; true from a successful <see cref="Start"/> until <see cref="Stop"/>.
        /// </summary>
        public bool IsListening { get; private set; }

        /// <summary>
        /// Whether a client is connected, as reported by its TCP connection.
        /// </summary>
        /// <remarks>
        /// The server serves one client at a time and closes any other client that connects while this is true.
        /// </remarks>
        public bool HasClient => _client?.Connected ?? false;

        /// <summary>
        /// The listen address in <c>0.0.0.0:port</c> form; the server listens on all local addresses.
        /// </summary>
        public string ListenEndpoint => $"0.0.0.0:{_listenPort}";

        /// <summary>
        /// The remote endpoint of the connected client, or null when no client is connected.
        /// </summary>
        public string? ClientEndpoint { get; private set; }

        /// <summary>
        /// Creates a server for the given port. Nothing listens until <see cref="Start"/> is called.
        /// </summary>
        /// <param name="listenPort">
        /// The TCP port to listen on.
        /// </param>
        public XotTestServer(int listenPort)
        {
            _listenPort = listenPort;
        }

        /// <summary>
        /// Start listening for connections
        /// </summary>
        public bool Start()
        {
            if (IsListening)
                return true;

            try
            {
                _listener = new TcpListener(IPAddress.Any, _listenPort);
                _listener.Start();

                IsListening = true;
                StatusChanged?.Invoke(this, $"Server listening on port {_listenPort}");

                // Start accept thread
                _cts = new CancellationTokenSource();
                _acceptThread = new Thread(() => AcceptLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = "XOT Server Accept"
                };
                _acceptThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Failed to start server: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stop server and close all connections
        /// </summary>
        public void Stop()
        {
            if (!IsListening)
                return;

            StatusChanged?.Invoke(this, "Stopping server...");

            IsListening = false;
            _cts?.Cancel();

            try
            {
                _listener?.Stop();
                _client?.Close();
                _stream?.Close();
            }
            catch
            {
                // Ignore cleanup errors
            }

            StatusChanged?.Invoke(this, "Server stopped");
        }

        /// <summary>
        /// Send X.25 packet to connected client
        /// </summary>
        public bool SendPacket(byte[] x25Packet)
        {
            if (!HasClient || _stream == null)
            {
                StatusChanged?.Invoke(this, "No client connected");
                return false;
            }

            if (x25Packet == null || x25Packet.Length == 0)
            {
                StatusChanged?.Invoke(this, "Cannot send empty packet");
                return false;
            }

            try
            {
                // Create XOT packet (4-byte header + X.25 packet)
                byte[] xotPacket = XotPacketFormatter.CreateXotPacket(x25Packet);

                // Send to client
                _stream.Write(xotPacket, 0, xotPacket.Length);
                _stream.Flush();

                // Fire DataSent event
                DataSent?.Invoke(this, new XotDataEventArgs
                {
                    Data = xotPacket,
                    Timestamp = DateTime.Now,
                    Direction = "TX"
                });

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Send failed: {ex.Message}");
                CloseClientConnection();
                return false;
            }
        }

        /// <summary>
        /// Accept incoming client connections
        /// </summary>
        private void AcceptLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsListening && _listener != null)
            {
                try
                {
                    // Accept client (blocking call)
                    TcpClient client = _listener.AcceptTcpClient();

                    // If we already have a client, reject the new one
                    if (HasClient)
                    {
                        StatusChanged?.Invoke(this, "Rejected additional client (already have connection)");
                        client.Close();
                        continue;
                    }

                    // Accept this client
                    _client = client;
                    _stream = _client.GetStream();
                    ClientEndpoint = _client.Client.RemoteEndPoint?.ToString();

                    StatusChanged?.Invoke(this, $"Client connected: {ClientEndpoint}");
                    ClientConnected?.Invoke(this, ClientEndpoint ?? "unknown");

                    // Start receive thread for this client
                    _receiveThread = new Thread(() => ReceiveLoop(cancellationToken))
                    {
                        IsBackground = true,
                        Name = "XOT Server Receive"
                    };
                    _receiveThread.Start();
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        StatusChanged?.Invoke(this, $"Accept error: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Receive data from connected client
        /// </summary>
        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            byte[] headerBuffer = new byte[4];
            byte[] dataBuffer = new byte[4096];

            while (!cancellationToken.IsCancellationRequested && _stream != null && HasClient)
            {
                try
                {
                    // Read XOT header (4 bytes: version + length)
                    int bytesRead = ReadExact(_stream, headerBuffer, 0, 4);
                    if (bytesRead == 0)
                        break; // Connection closed

                    // Parse header
                    int version = (headerBuffer[0] << 8) | headerBuffer[1];
                    int length = (headerBuffer[2] << 8) | headerBuffer[3];

                    // Validate
                    if (version != 0)
                    {
                        StatusChanged?.Invoke(this, $"Invalid XOT version: {version}");
                        continue;
                    }

                    if (length > 4096)
                    {
                        StatusChanged?.Invoke(this, $"Packet too large: {length} bytes");
                        continue;
                    }

                    // Read X.25 packet
                    if (length > 0)
                    {
                        bytesRead = ReadExact(_stream, dataBuffer, 0, length);
                        if (bytesRead == 0)
                            break;

                        // Reconstruct full XOT packet for display
                        byte[] xotPacket = new byte[4 + length];
                        Array.Copy(headerBuffer, 0, xotPacket, 0, 4);
                        Array.Copy(dataBuffer, 0, xotPacket, 4, length);

                        // Fire DataReceived event
                        DataReceived?.Invoke(this, new XotDataEventArgs
                        {
                            Data = xotPacket,
                            Timestamp = DateTime.Now,
                            Direction = "RX"
                        });
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Stream disposed, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        StatusChanged?.Invoke(this, $"Receive error: {ex.Message}");
                    }
                    break;
                }
            }

            // Client disconnected
            CloseClientConnection();
        }

        /// <summary>
        /// Close current client connection
        /// </summary>
        private void CloseClientConnection()
        {
            if (!HasClient)
                return;

            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch
            {
                // Ignore cleanup errors
            }

            string endpoint = ClientEndpoint ?? "unknown";
            ClientEndpoint = null;
            _stream = null;
            _client = null;

            StatusChanged?.Invoke(this, $"Client disconnected: {endpoint}");
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Read exact number of bytes from stream
        /// </summary>
        private int ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                    return totalRead; // Connection closed

                totalRead += bytesRead;
            }
            return totalRead;
        }

        /// <summary>
        /// Stops the server through <see cref="Stop"/> and releases the cancellation source. Calls after the
        /// first do nothing.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
