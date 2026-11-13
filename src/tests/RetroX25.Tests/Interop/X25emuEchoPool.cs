//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIO.Protocols.X25.Client;

namespace RetroX25.Tests
{
    /// <summary>
    /// An echo service for x25emu's endpoint port (Switch A port 3, XOT 2007) that can take many calls, also at the same time.
    /// </summary>
    /// <remarks>
    /// <para>Why a pool: x25emu's access port hands each incoming call to a SEPARATE idle TCP connection that the endpoint
    /// holds open (RFC 1613 section 5: one connection per circuit). One connection takes one call; for a second call the
    /// endpoint must have another idle connection ready, or Switch C/A clears the caller after 5 s with "number busy"
    /// (measured 2026-11-12: "nothing to deliver LCI 2 ... no channel to deliver the call on"). x25emu's own x25echo is one
    /// connection and so takes one call.</para>
    /// <para>So this keeps <c>idle</c> connections open, each its own <see cref="X25Client"/>, and puts a new one in place of every
    /// connection that took a call. A connection is stopped when its call ends. Everything it opens, it closes in
    /// <see cref="Dispose"/>.</para>
    /// </remarks>
    internal sealed class X25emuEchoPool : IDisposable
    {
        /// <summary>The most connections x25emu takes on one port (X25C_MAX_CONNS in its source).</summary>
        internal const int MaxConnections = 16;

        private readonly string _host;
        private readonly int _port;
        private readonly string _address;
        private readonly object _lock = new object();
        private readonly List<X25Client> _clients = new List<X25Client>();
        private readonly BlockingCollection<(X25Client Client, RetroIO.Protocols.X25.Client.X25Vc Vc, byte[] Data, bool Q)> _replies =
            new BlockingCollection<(X25Client, RetroIO.Protocols.X25.Client.X25Vc, byte[], bool)>();
        private readonly Thread _sender;
        private bool _stopped;
        private readonly List<(byte[] Data, bool Q, bool M, bool D)> _received = new List<(byte[], bool, bool, bool)>();
        private readonly List<(byte Cause, byte Diagnostic)> _clears = new List<(byte, byte)>();
        private int _calls;
        private long _bytesEchoed;
        private long _packetsEchoed;

        internal X25emuEchoPool(string host, int port, string address, int idle)
        {
            _host = host;
            _port = port;
            _address = address;

            // Replies go out from a thread of their own: waiting for a shut window inside the receive callback would stop the
            // very packets that open it again.
            _sender = new Thread(SendLoop) { IsBackground = true, Name = "echo-pool-sender" };
            _sender.Start();
            for (int i = 0; i < idle; i++)
            {
                AddClient();
            }

            Thread.Sleep(1000);
        }

        /// <summary>How many calls the pool has taken.</summary>
        internal int Calls => Volatile.Read(ref _calls);

        /// <summary>How many data bytes it sent back.</summary>
        internal long BytesEchoed => Interlocked.Read(ref _bytesEchoed);

        /// <summary>How many data packets it sent back.</summary>
        internal long PacketsEchoed => Interlocked.Read(ref _packetsEchoed);

        /// <summary>Every data packet the pool received, with its Q, M and D bits, in the order they came.</summary>
        internal List<(byte[] Data, bool Q, bool M, bool D)> Received()
        {
            lock (_received)
            {
                return new List<(byte[], bool, bool, bool)>(_received);
            }
        }

        /// <summary>Cause and diagnostic of every clear the pool was told about.</summary>
        internal List<(byte Cause, byte Diagnostic)> Clears()
        {
            lock (_clears)
            {
                return new List<(byte, byte)>(_clears);
            }
        }

        private void SendLoop()
        {
            try
            {
                foreach ((X25Client Client, X25Vc Vc, byte[] Data, bool Q) reply in _replies.GetConsumingEnumerable())
                {
                    long deadline = Environment.TickCount64 + 15000;
                    while (!reply.Client.Send(reply.Vc, reply.Data, reply.Data.Length, reply.Q) && Environment.TickCount64 < deadline && reply.Client.IsOpen)
                    {
                        Thread.Sleep(1);
                    }

                    Interlocked.Add(ref _bytesEchoed, reply.Data.Length);
                    Interlocked.Increment(ref _packetsEchoed);
                }
            }
            catch (InvalidOperationException)
            {
                // The collection was completed or a client was closed under us: the pool is being stopped.
            }
        }

        private void AddClient()
        {
            X25Client client = new X25Client { ServiceName = "echo-pool" };
            bool replaced = false;
            client.SetOnIncoming((c, vc) =>
            {
                c.Accept(vc);
                Interlocked.Increment(ref _calls);

                // This connection is used up now: put a fresh idle one in its place, on another thread (we are inside the
                // client's own callback).
                if (!replaced)
                {
                    replaced = true;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        lock (_lock)
                        {
                            // x25emu's endpoint library holds at most 16 connections per port (X25C_MAX_CONNS in its
                            // x25_client.h), so more than 16 would only be refused.
                            if (_stopped || _clients.Count >= MaxConnections)
                            {
                                return;
                            }
                        }

                        AddClient();
                    });
                }
            });
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                byte[] copy = new byte[length];
                Array.Copy(data, 0, copy, 0, length);
                lock (_received)
                {
                    _received.Add((copy, qBit, mBit, dBit));
                }

                if (!_replies.IsAddingCompleted)
                {
                    _replies.Add((client, vc, copy, qBit));
                }
            });
            client.SetOnClear((c, vc, cause, diagnostic) =>
            {
                lock (_clears)
                {
                    _clears.Add((cause, diagnostic));
                }

                // The call is over: this connection is finished with. Closed on another thread.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (_lock)
                    {
                        _clients.Remove(client);
                    }

                    client.Dispose();
                });
            });
            lock (_lock)
            {
                if (_stopped)
                {
                    client.Dispose();
                    return;
                }

                _clients.Add(client);
            }

            // NumVcs: x25emu gives the call the channel number of the connection's slot in its port (LCI 1 to 17 and more seen), and a
            // client only takes a call on a channel inside its own range; the default range is one channel. RFC 1613 section 6:
            // the number has no significance, so the range is made wide.
            client.Open(new X25ClientConfig { Host = _host, Port = (ushort)_port, LocalAddress = _address, Framing = X25ClientFraming.Xot, NumVcs = 64 }, NullLogger.Instance);
        }

        public void Dispose()
        {
            List<X25Client> all;
            lock (_lock)
            {
                _stopped = true;
                all = new List<X25Client>(_clients);
                _clients.Clear();
            }

            _replies.CompleteAdding();
            _sender.Join(5000);
            for (int i = 0; i < all.Count; i++)
            {
                all[i].Dispose();
            }
        }
    }
}
