//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using RetroIO.Link.Framing;

namespace RetroX25.Xot
{
    /// <summary>
    /// Why an <see cref="XotConnection"/> ended.
    /// </summary>
    public enum XotCloseReason
    {
        /// <summary>
        /// This side called <see cref="XotConnection.Close"/>.
        /// </summary>
        ClosedLocally,

        /// <summary>
        /// The other side closed the TCP connection.
        /// </summary>
        ClosedByRemote,

        /// <summary>
        /// The other side sent a packet that breaks the XOT framing, so the stream can no longer be read.
        /// </summary>
        FramingFailed,

        /// <summary>
        /// A read or write on the socket failed.
        /// </summary>
        Error
    }

    /// <summary>
    /// One XOT (X.25 over TCP, RFC 1613) connection: the socket, the framing and the receive thread.
    /// The XOT test client and the XOT test server both use it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this class exists</b></para>
    /// The client and the server each had their own copy of the same receive loop, and both copies
    /// had the same faults. A header cut short by the connection closing was read as complete. A
    /// packet with a bad version or an illegal length was skipped without reading its body, so the
    /// next header was read from the middle of it. And a local close raised the closed event once
    /// itself and a second time when the receive loop noticed. The framing now comes from
    /// <see cref="XotFrameCodec"/> in RetroIO, which follows RFC 1613: a version other than 0, or a
    /// length that is not a legal X.25 packet length, closes the connection.
    /// <para><b>Events</b></para>
    /// <see cref="PacketReceived"/> is raised on the receive thread. <see cref="Closed"/> is raised
    /// exactly once, whichever way the connection ends.
    /// </remarks>
    public sealed class XotConnection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly XotFrameCodec _codec;
        private readonly SocketTransport _transport;
        private readonly object _sendLock = new object();
        private readonly Thread _receiveThread;
        private int _closed;
        private string? _framingFailure;

        /// <summary>
        /// Raised on the receive thread for each complete XOT packet read, with the 4-byte XOT header
        /// followed by the X.25 packet.
        /// </summary>
        public event Action<byte[]>? PacketReceived;

        /// <summary>
        /// Raised exactly once, when the connection ends for any reason.
        /// </summary>
        /// <remarks>
        /// The first argument says why. The second is a short text for a log line: the reason for a
        /// framing failure, the socket error for an error, and empty otherwise.
        /// </remarks>
        public event Action<XotCloseReason, string>? Closed;

        /// <summary>
        /// Gets whether the connection is still open: not closed from either side and not failed.
        /// </summary>
        public bool IsOpen => Volatile.Read(ref _closed) == 0;

        /// <summary>
        /// Gets the remote end point as text, or <c>unknown</c> when the socket cannot report it.
        /// </summary>
        public string RemoteEndpoint { get; }

        /// <summary>
        /// Wraps a connected socket. The receive thread does not run until <see cref="Start"/>.
        /// </summary>
        /// <param name="client">
        /// The connected socket. The connection owns it from here and closes it.
        /// </param>
        /// <param name="linkId">
        /// A name for this connection, used for the codec and the thread name.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="client"/> or <paramref name="linkId"/> is null.
        /// </exception>
        public XotConnection(TcpClient client, string linkId)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (linkId == null)
            {
                throw new ArgumentNullException(nameof(linkId));
            }

            _stream = client.GetStream();
            RemoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

            _transport = new SocketTransport(_stream, _sendLock);
            _codec = new XotFrameCodec(linkId, _transport);
            _codec.FrameReceived += OnFrame;
            _codec.FramingFailed += OnFramingFailed;

            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "XOT receive " + linkId
            };
        }

        /// <summary>
        /// Starts the receive thread.
        /// </summary>
        public void Start()
        {
            _receiveThread.Start();
        }

        /// <summary>
        /// Wraps an X.25 packet in the XOT header and writes it to the socket.
        /// </summary>
        /// <param name="x25Packet">
        /// The X.25 packet, with no XOT header. It must be a legal X.25 packet length.
        /// </param>
        /// <param name="sent">
        /// The bytes written, the XOT header followed by the packet; null when nothing was sent.
        /// </param>
        /// <param name="error">
        /// Why nothing was sent; null when it was.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the packet was written.
        /// </returns>
        public bool TrySend(byte[] x25Packet, out byte[]? sent, out string? error)
        {
            sent = null;
            error = null;

            if (!IsOpen)
            {
                error = "The connection is closed";
                return false;
            }

            try
            {
                // The lock is the one the transport takes for each write; holding it here as well keeps
                // LastSent from being replaced by another thread's packet before it is read.
                lock (_sendLock)
                {
                    _codec.SendFrame(x25Packet);
                    sent = _transport.LastSent;
                }

                return true;
            }
            catch (ArgumentException ex)
            {
                // The codec refuses a packet that is not a legal X.25 packet length (3 to 4096).
                error = ex.Message;
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
            {
                error = ex.Message;
                Finish(XotCloseReason.Error, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Closes the connection and raises <see cref="Closed"/> with
        /// <see cref="XotCloseReason.ClosedLocally"/>, unless it has already ended.
        /// </summary>
        public void Close()
        {
            Finish(XotCloseReason.ClosedLocally, string.Empty);
        }

        /// <summary>
        /// Closes the connection, as <see cref="Close"/> does.
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        private void OnFrame(string linkId, ReadOnlySpan<byte> frame)
        {
            PacketReceived?.Invoke(XotPacketFormatter.CreateXotPacket(frame.ToArray()));
        }

        private void OnFramingFailed(string linkId, string reason)
        {
            // Raised from inside ProcessBytes; the receive loop closes the connection when it returns.
            _framingFailure = reason;
        }

        private void ReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (IsOpen)
                {
                    int read = _stream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        Finish(XotCloseReason.ClosedByRemote, string.Empty);
                        return;
                    }

                    _codec.ProcessBytes(buffer.AsSpan(0, read));
                    if (_framingFailure != null)
                    {
                        Finish(XotCloseReason.FramingFailed, _framingFailure);
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
            {
                // A local Close closes the stream under this read; that is not an error.
                Finish(XotCloseReason.Error, ex.Message);
            }
        }

        /// <summary>
        /// Ends the connection and raises <see cref="Closed"/> the first time only.
        /// </summary>
        /// <param name="reason">
        /// Why the connection ended; used only by the call that wins.
        /// </param>
        /// <param name="detail">
        /// Text for the log line; used only by the call that wins.
        /// </param>
        private void Finish(XotCloseReason reason, string detail)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            try
            {
                _stream.Close();
                _client.Close();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
            {
                // The socket is already gone, which is what was being asked for.
            }

            Closed?.Invoke(reason, detail);
        }

        /// <summary>
        /// Writes the codec's framed bytes to the socket and remembers the last packet written.
        /// </summary>
        private sealed class SocketTransport : IFrameTransport
        {
            private readonly NetworkStream _stream;
            private readonly object _sendLock;

            public SocketTransport(NetworkStream stream, object sendLock)
            {
                _stream = stream;
                _sendLock = sendLock;
            }

            /// <summary>
            /// Gets the bytes of the last packet written, the XOT header followed by the X.25 packet.
            /// </summary>
            public byte[]? LastSent { get; private set; }

            public void Send(ReadOnlySpan<byte> bytes)
            {
                lock (_sendLock)
                {
                    _stream.Write(bytes);
                    _stream.Flush();
                    LastSent = bytes.ToArray();
                }
            }
        }
    }
}
