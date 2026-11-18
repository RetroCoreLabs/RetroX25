//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using RetroIO.Protocols.X25.Models;

namespace RetroX25.Tests
{
    /// <summary>
    /// One decoded X.25 packet seen on a tapped XOT connection.
    /// </summary>
    internal sealed class TappedPacket
    {
        /// <summary>True when the packet travelled from the connecting client to the far end.</summary>
        public bool FromClient;

        /// <summary>The XOT frame's X.25 packet, header (GFI, channel, type) included, XOT framing removed.</summary>
        public byte[] Packet = Array.Empty<byte>();

        /// <summary>When it was seen, in milliseconds since the tap started.</summary>
        public long AtMs;

        public X25PacketInfo Info => X25PacketParser.ParsePacket(Packet);

        public override string ToString()
        {
            string type;
            try
            {
                X25PacketInfo info = Info;
                type = info.PacketType + " channel=" + info.LCN;
                if (info.PacketType == X25PacketType.ClearRequest)
                {
                    type += " cause=" + info.ClearingCause + " diagnostic=" + info.DiagnosticCode;
                }
                else if (info.PacketType == X25PacketType.Data)
                {
                    type += " Q=" + (info.QBit ? 1 : 0) + " M=" + (info.MBit ? 1 : 0) + " D=" + (info.DBit ? 1 : 0) + " length=" + info.UserData.Length;
                }
                else if (info.PacketType == X25PacketType.CallRequest)
                {
                    type += " called=" + info.CalledAddress + " calling=" + info.CallingAddress;
                }
            }
            catch (ArgumentException)
            {
                type = "too short to decode";
            }

            return string.Format("{0,6} ms {1} {2}  [{3}]", AtMs, FromClient ? "client->far" : "far->client", type, BitConverter.ToString(Packet));
        }
    }

    /// <summary>
    /// A relay between a client and an XOT server that writes down every X.25 packet in order, so a test can read the real
    /// bytes. It carries one connection at a time, and counts the connections it carried.
    /// </summary>
    internal sealed class XotWireTap : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _targetHost;
        private readonly int _targetPort;
        private readonly List<TappedPacket> _packets = new List<TappedPacket>();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly List<TcpClient> _sockets = new List<TcpClient>();
        private volatile bool _stopped;
        private int _connections;

        internal XotWireTap(string targetHost, int targetPort)
        {
            _targetHost = targetHost;
            _targetPort = targetPort;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Thread accept = new Thread(AcceptLoop) { IsBackground = true, Name = "xot-tap-accept" };
            accept.Start();
        }

        /// <summary>The loopback port a client connects to.</summary>
        internal int Port { get; }

        /// <summary>How many client connections were carried.</summary>
        internal int Connections => Volatile.Read(ref _connections);

        internal List<TappedPacket> Packets()
        {
            lock (_packets)
            {
                return new List<TappedPacket>(_packets);
            }
        }

        /// <summary>All packets, one per line, in the order they crossed.</summary>
        internal string Dump()
        {
            StringBuilder text = new StringBuilder();
            List<TappedPacket> all = Packets();
            for (int i = 0; i < all.Count; i++)
            {
                text.AppendLine(all[i].ToString());
            }

            return text.ToString();
        }

        private void AcceptLoop()
        {
            while (!_stopped)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    TcpClient far = new TcpClient();
                    far.Connect(_targetHost, _targetPort);
                    lock (_sockets)
                    {
                        _sockets.Add(client);
                        _sockets.Add(far);
                    }

                    Interlocked.Increment(ref _connections);
                    Thread up = new Thread(() => Pump(client, far, true)) { IsBackground = true, Name = "xot-tap-up" };
                    Thread down = new Thread(() => Pump(far, client, false)) { IsBackground = true, Name = "xot-tap-down" };
                    up.Start();
                    down.Start();
                }
                catch (SocketException)
                {
                    client.Close();
                }
            }
        }

        private void Pump(TcpClient from, TcpClient to, bool fromClient)
        {
            byte[] chunk = new byte[4096];
            List<byte> pending = new List<byte>();
            try
            {
                NetworkStream source = from.GetStream();
                NetworkStream sink = to.GetStream();
                while (!_stopped)
                {
                    int count = source.Read(chunk, 0, chunk.Length);
                    if (count <= 0)
                    {
                        break;
                    }

                    sink.Write(chunk, 0, count);
                    for (int i = 0; i < count; i++)
                    {
                        pending.Add(chunk[i]);
                    }

                    // XOT frame (RFC 1613): version (2 bytes, 0), length (2 bytes), then the X.25 packet.
                    while (pending.Count >= 4)
                    {
                        int length = (pending[2] << 8) | pending[3];
                        if (pending.Count < 4 + length)
                        {
                            break;
                        }

                        byte[] packet = pending.GetRange(4, length).ToArray();
                        pending.RemoveRange(0, 4 + length);
                        lock (_packets)
                        {
                            _packets.Add(new TappedPacket { FromClient = fromClient, Packet = packet, AtMs = _clock.ElapsedMilliseconds });
                        }
                    }
                }
            }
            catch (IOException)
            {
                // The connection ended.
            }
            catch (ObjectDisposedException)
            {
                // Closed under us.
            }
            catch (InvalidOperationException)
            {
                // GetStream on a socket that is no longer connected: the connection ended (or the tap was disposed)
                // before this pump started. Since 2026-11-18 our X25Client opens a spare connection right after a call
                // takes its first one, so a test that ends soon after its call ends while that spare is still being
                // set up; this threw on the pump thread and took the whole test host down.
            }
            finally
            {
                try
                {
                    to.Client.Shutdown(SocketShutdown.Send);
                }
                catch (Exception)
                {
                    // Already closed.
                }
            }
        }

        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
            lock (_sockets)
            {
                for (int i = 0; i < _sockets.Count; i++)
                {
                    _sockets[i].Close();
                }
            }
        }
    }
}
