//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Transport;

namespace RetroX25.Echo
{
    /// <summary>
    /// The echo service: a DTE behind a switch port that accepts every call to its address and sends back what it
    /// receives. <see cref="Program"/> runs it; the tests run it in the same process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DTE switched on while its link is down keeps trying to bring the link up. X.25 (10/96) 2.4.4.1 (text line
    /// 3326): "Either the DTE or the DCE may initiate data link set-up", and the DTE "shall initiate data link set-up by
    /// transmitting an SABM" (line 3329) - again on every T1 until the link is up; after N2 attempts the text leaves the
    /// rest to "appropriate higher layer recovery action" (line 3348-3350), unnamed. Over XOT the link is the TCP
    /// connection to the switch (RFC 1613 section 5, rfc1613.txt line 201), so the service dials it until the switch
    /// answers, on the same rule the client uses for its spare after a switch restart
    /// (<see cref="XotSpareRedial"/>: 2 s, doubling to 30 s - our own timing, not the standard's). Until 2026-11-18
    /// x25echo gave up at once and exited with code 1 when the switch was not there when it started.
    /// </para>
    /// <para>
    /// Once open, <see cref="X25Client"/> itself dials its spare connection again after the switch dies and comes back.
    /// <see cref="X25Client.Open"/> still throws when its first connection fails, on purpose: a caller that is not a
    /// service (x25pad, x25traffic, x25nmc) wants to be told at once; X.25 gives no rule that a calling program must wait.
    /// </para>
    /// </remarks>
    internal sealed class EchoService : IDisposable
    {
        // How many received packets may wait for the send window before the service starts dropping.
        private const int MaxPending = 100000;

        /// <summary>
        /// Longest line kept while looking for the exit command; anything longer cannot be the command.
        /// </summary>
        private const int MaxLine = 64;

        private readonly X25ClientConfig _config;
        private readonly bool _quiet;
        private readonly ILogger _log;

        // The queue of packets waiting for the send window. The greeting, the echoes and the goodbye all go through it
        // so they leave in the order they were made.
        private readonly ConcurrentQueue<EchoItem> _pending = new ConcurrentQueue<EchoItem>();

        // Per circuit: true when the last byte echoed was a CR that was turned into CR LF, so a following LF is not
        // echoed a second time.
        private readonly ConcurrentDictionary<X25Vc, bool> _afterCr = new ConcurrentDictionary<X25Vc, bool>();

        // Text typed so far on each circuit, kept to spot the exit command.
        private readonly ConcurrentDictionary<X25Vc, System.Text.StringBuilder> _lines = new ConcurrentDictionary<X25Vc, System.Text.StringBuilder>();

        // Set by Stop: ends the wait between dials.
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);

        private long _dropped;
        private long _dialFailures;
        private volatile X25Client? _client;
        private volatile bool _stopped;
        private Thread? _sender;

        /// <summary>
        /// One packet waiting to be echoed.
        /// </summary>
        /// <param name="Vc">
        /// The circuit it arrived on and goes back out of.
        /// </param>
        /// <param name="Data">
        /// A copy of the packet's user data.
        /// </param>
        /// <param name="QBit">
        /// The qualifier bit, echoed unchanged.
        /// </param>
        /// <param name="Close">
        /// True when the call is to be cleared once this packet has gone out (the user typed exit).
        /// </param>
        /// <param name="MBit">
        /// The more-data bit, echoed unchanged. Set only when the packet goes back byte for byte (quiet
        /// mode or a Q-bit packet); the terminal echo rewrites the text, so a message boundary means nothing there.
        /// </param>
        /// <param name="DBit">
        /// The delivery-confirmation bit, echoed unchanged under the same condition as <c>MBit</c>.
        /// </param>
        private sealed record EchoItem(X25Vc Vc, byte[] Data, bool QBit, bool Close = false, bool MBit = false, bool DBit = false);

        /// <summary>
        /// Creates the service. Nothing is dialled until <see cref="Open"/>.
        /// </summary>
        /// <param name="config">The switch to dial and the X.25 settings; its spare redial times are also the times
        /// between the first dials.</param>
        /// <param name="quiet">True for a plain byte-for-byte echo with no greeting and no exit command.</param>
        /// <param name="log">Where the service logs.</param>
        public EchoService(X25ClientConfig config, bool quiet, ILogger log)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _quiet = quiet;
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// How many dials to the switch have failed while the service waited for it to answer the first time.
        /// </summary>
        public long DialFailures => Interlocked.Read(ref _dialFailures);

        /// <summary>
        /// True once a connection to the switch was made and the service is serving calls.
        /// </summary>
        public bool IsOpen => _client != null;

        /// <summary>
        /// Dials the switch until it answers, then serves calls. Returns when the service is open, or when
        /// <see cref="Stop"/> was called first.
        /// </summary>
        /// <returns>True when the service is open; false when it was stopped before the switch answered.</returns>
        public bool Open()
        {
            XotSpareRedial redial = new XotSpareRedial(_config.SpareRedialFirstMs, _config.SpareRedialMostMs);
            while (!_stopped)
            {
                X25Client client = CreateClient();
                try
                {
                    client.Open(_config, _log);
                }
                catch (InvalidOperationException ex)
                {
                    // X25Client.Open's documented answer when the connection to the switch cannot be made. The switch is
                    // down or not started yet: the link is not up, so the DTE tries again (X.25 2.4.4.1).
                    client.Dispose();
                    long failures = Interlocked.Increment(ref _dialFailures);
                    int wait = redial.Next();
                    _log.LogWarning("Echo: waiting for the switch at {Host}:{Port} ({Reason}); dial {Count} failed, dialling again in {Wait} ms",
                        _config.Host, _config.Port, ex.Message, failures, wait);
                    _stop.Wait(wait);
                    continue;
                }

                lock (_stop)
                {
                    if (_stopped)
                    {
                        client.Dispose();
                        return false;
                    }

                    _client = client;
                }

                _sender = new Thread(SendLoop) { IsBackground = true, Name = "echo-sender" };
                _sender.Start();
                _log.LogInformation("Echo: Connected to {Host}:{Port}", _config.Host, _config.Port);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Blocks until <see cref="Stop"/> is called (the client's own run loop).
        /// </summary>
        public void Run()
        {
            X25Client? client = _client;
            if (client != null)
            {
                client.Run();
            }
            else
            {
                _stop.Wait();
            }
        }

        /// <summary>
        /// Stops the service: ends a wait for the switch, closes the client and ends <see cref="Run"/>.
        /// </summary>
        public void Stop()
        {
            X25Client? client;
            lock (_stop)
            {
                _stopped = true;
                client = _client;
            }

            _stop.Set();
            client?.Close();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Stop();
            _sender?.Join(2000);
            _client?.Dispose();
            _stop.Dispose();
        }

        /// <summary>
        /// A new client with the echo's handlers on it. A client whose first dial failed is thrown away and a new one is
        /// made for the next dial, so no state of a failed open is carried over.
        /// </summary>
        private X25Client CreateClient()
        {
            X25Client client = new X25Client { ServiceName = "Echo" };

            // Incoming call: auto-accept, then greet. The greeting is an extra packet the caller did not
            // send, so it is skipped with -q: x25traffic counts every packet it gets back, and a
            // performance run must see exactly what it sent.
            client.SetOnIncoming((c, vc) =>
            {
                _log.LogInformation("Echo: Incoming call from {Calling}, accepting", vc.CallingAddress);
                c.Accept(vc);
                if (!_quiet)
                {
                    _pending.Enqueue(new EchoItem(vc, System.Text.Encoding.ASCII.GetBytes("Welcome to the echo server. Type exit to leave.\r\n"), false));
                }
            });

            client.SetOnData(OnData);

            // Clear: just log.
            client.SetOnClear((c, vc, cause, diag) =>
            {
                _lines.TryRemove(vc, out _);
                _afterCr.TryRemove(vc, out _);
                _log.LogInformation("Echo: Call cleared (cause={Cause:X2} diag={Diag:X2})", cause, diag);
            });

            return client;
        }

        // Data received: echo it back byte-for-byte.
        //
        // Send() answers false when the send window to the switch is full. Ignoring that answer
        // (as this tool did first) throws the packet away: at full speed a third of the echoes were
        // lost with no error anywhere, which made every "delivery" figure measured through the echo
        // service wrong. So the packet is copied into a queue and a sender thread keeps trying until
        // the window has room. The receive callback itself must not wait, because the same thread
        // delivers the acknowledgements that open the window.
        private void OnData(X25Client c, X25Vc vc, byte[] data, int length, bool qBit, bool mBit, bool dBit)
        {
            if (_pending.Count >= MaxPending)
            {
                // The sender is far faster than the window lets us answer. Dropping is the last
                // resort and is counted and reported, never silent.
                long n = Interlocked.Increment(ref _dropped);
                if (n == 1 || n % 1000 == 0)
                {
                    _log.LogWarning("Echo: queue full, {Count} packet(s) dropped so far", n);
                }

                return;
            }

            byte[] copy;
            if (_quiet || qBit)
            {
                // A plain echo, byte for byte (and never touch an X.29 control message).
                copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
            }
            else
            {
                _afterCr.TryGetValue(vc, out bool previousWasCr);
                copy = Program.EchoForTerminal(data, length, ref previousWasCr);
                _afterCr[vc] = previousWasCr;
            }

            // Nothing to send back: the LF of a CR LF pair that arrived as its own packet. X25Layer.SendData
            // refuses an empty packet for ever, so queueing it would block the sender thread on it.
            // Longer than one packet (every CR grew by an LF): send it in packets of at most 128 octets,
            // the negotiated size, since a longer block is refused for ever too.
            for (int offset = 0; offset < copy.Length; offset += 128)
            {
                int piece = Math.Min(128, copy.Length - offset);
                byte[] part = new byte[piece];
                Buffer.BlockCopy(copy, offset, part, 0, piece);
                // M and D go back only on a byte-for-byte, single-packet echo (see EchoItem).
                bool exact = (_quiet || qBit) && copy.Length == piece;
                _pending.Enqueue(new EchoItem(vc, part, qBit, false, exact && mBit, exact && dBit));
            }

            if (_quiet)
            {
                return;
            }

            // Look for the exit command: a line of the word exit (any case) ended by CR or LF, or a
            // Ctrl-D (0x04). The line is echoed first like any other text, then the goodbye goes out
            // and the call is cleared.
            System.Text.StringBuilder line = _lines.GetOrAdd(vc, _ => new System.Text.StringBuilder());
            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                bool leave = false;
                if (b == 0x04)
                {
                    leave = true;
                }
                else if (b == 0x0D || b == 0x0A)
                {
                    leave = line.ToString().Trim().Equals("exit", StringComparison.OrdinalIgnoreCase);
                    line.Clear();
                }
                else if (line.Length < MaxLine)
                {
                    line.Append((char)b);
                }

                if (leave)
                {
                    _pending.Enqueue(new EchoItem(vc, System.Text.Encoding.ASCII.GetBytes("\r\nGoodbye.\r\n"), false));
                    // X.29 section 3.2.1: the invitation to clear asks the PAD to clear the call after it
                    // has delivered all earlier data to the terminal. It is a Q-bit packet holding the
                    // single octet 0x01 (X.29 4.4.3). The echo clears itself only if the peer does not.
                    _pending.Enqueue(new EchoItem(vc, new byte[] { 0x01 }, true, true));
                    _lines.TryRemove(vc, out _);
                    return;
                }
            }
        }

        private void SendLoop()
        {
            X25Client client = _client!;
            int idle = 0;
            while (!_stopped)
            {
                if (!_pending.TryPeek(out EchoItem? next))
                {
                    Thread.Sleep(1);
                    continue;
                }

                // A circuit that has cleared has nobody to echo to.
                if (!next.Vc.Connected || client.Send(next.Vc, next.Data, next.Data.Length, next.QBit, next.MBit, next.DBit))
                {
                    _pending.TryDequeue(out _);
                    idle = 0;
                    if (next.Close && next.Vc.Connected)
                    {
                        // X.25 section 4.5: data sent just before a clear may be discarded by the
                        // network, so the goodbye is given time to be acknowledged by the peer's
                        // packet layer first (its P(R) reaching our V(S)), not a fixed delay.
                        bool acknowledged = client.WaitForAcknowledgement(next.Vc, 2000);
                        // Give the PAD time to show the goodbye and clear the call itself.
                        Stopwatch wait = Stopwatch.StartNew();
                        while (next.Vc.Connected && wait.ElapsedMilliseconds < 2000)
                        {
                            Thread.Sleep(10);
                        }
                        _log.LogInformation("Echo: caller typed exit (invitation acknowledged: {Acknowledged}, peer cleared: {PeerCleared})", acknowledged, !next.Vc.Connected);
                        if (next.Vc.Connected)
                        {
                            client.Clear(next.Vc);
                        }
                    }
                }
                else if (++idle < 2000)
                {
                    // Window full. An acknowledgement usually arrives within microseconds, so give
                    // up the time slice and try again. A Sleep(1) here lasts about 15 ms on Windows
                    // and held the echo rate to 7 packets per 15 ms (the window size), about 470
                    // a second, which made every run look hung.
                    Thread.Yield();
                }
                else
                {
                    // Nothing has opened the window for a while (peer stalled): stop burning CPU.
                    Thread.Sleep(1);
                }
            }
        }
    }
}
