//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Net.Sockets;
using System.Threading;

namespace RetroX25.Xot
{
    /// <summary>
    /// Lightweight XOT client for testing (RFC 1613)
    /// </summary>
    public class XotTestClient : IDisposable
    {
        private readonly string _remoteHost;
        private readonly int _remotePort;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private Thread? _receiveThread;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        /// <summary>
        /// Raised on the receive thread for each complete XOT packet read from the server.
        /// </summary>
        /// <remarks>
        /// The event data holds the whole packet, the 4-byte XOT header followed by the X.25 packet, with
        /// direction <c>"RX"</c>. Packets with a non-zero version or a length over 4096 bytes are skipped and
        /// reported through <see cref="StatusChanged"/> instead.
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
        /// Raised when the connection ends, either through <see cref="Disconnect"/> or when the receive loop stops.
        /// </summary>
        /// <remarks>
        /// A call to <see cref="Disconnect"/> while connected can raise it twice: once from
        /// <see cref="Disconnect"/> itself and once from the receive loop as it exits.
        /// </remarks>
        public event EventHandler? Disconnected;

        /// <summary>
        /// Whether the underlying TCP connection reports itself as connected.
        /// </summary>
        public bool IsConnected => _tcpClient?.Connected ?? false;

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
        /// Connect to remote XOT server
        /// </summary>
        public bool Connect(int timeoutMs = 30000)
        {
            if (IsConnected)
                return true;

            try
            {
                StatusChanged?.Invoke(this, $"Connecting to {_remoteHost}:{_remotePort}...");

                _tcpClient = new TcpClient();
                var connectResult = _tcpClient.BeginConnect(_remoteHost, _remotePort, null, null);
                var success = connectResult.AsyncWaitHandle.WaitOne(timeoutMs);

                if (!success)
                {
                    StatusChanged?.Invoke(this, "Connection timeout");
                    _tcpClient?.Close();
                    return false;
                }

                _tcpClient.EndConnect(connectResult);
                _stream = _tcpClient.GetStream();

                // Start receive thread
                _cts = new CancellationTokenSource();
                _receiveThread = new Thread(() => ReceiveLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = "XOT Client Receive"
                };
                _receiveThread.Start();

                StatusChanged?.Invoke(this, $"Connected to {_remoteHost}:{_remotePort}");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        /// <summary>
        /// Send X.25 packet wrapped in XOT header
        /// </summary>
        public bool SendPacket(byte[] x25Packet)
        {
            if (!IsConnected || _stream == null)
            {
                StatusChanged?.Invoke(this, "Not connected");
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

                // Send to server
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
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Disconnect from server
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected)
                return;

            StatusChanged?.Invoke(this, "Disconnecting...");
            Cleanup();
            StatusChanged?.Invoke(this, "Disconnected");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Background receive loop
        /// </summary>
        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            byte[] headerBuffer = new byte[4];
            byte[] dataBuffer = new byte[4096];

            while (!cancellationToken.IsCancellationRequested && _stream != null && IsConnected)
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

            // Connection closed
            StatusChanged?.Invoke(this, "Connection closed by remote host");
            Disconnected?.Invoke(this, EventArgs.Empty);
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
        /// Cleanup resources
        /// </summary>
        private void Cleanup()
        {
            try
            {
                _cts?.Cancel();
                _stream?.Close();
                _tcpClient?.Close();
            }
            catch
            {
                // Ignore cleanup errors
            }

            _stream = null;
            _tcpClient = null;
        }

        /// <summary>
        /// Closes the connection and releases the cancellation source. Calls after the first do nothing.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Disconnect"/>, this does not raise <see cref="StatusChanged"/> or
        /// <see cref="Disconnected"/> itself.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Cleanup();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// Event args for XOT data events
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
        public string Direction { get; set; } = string.Empty; // "TX" or "RX"
    }
}
