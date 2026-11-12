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

namespace RetroX25.Echo
{
    /// <summary>
    /// x25echo - X.25 Echo Service.
    /// </summary>
    /// <remarks>
    /// Connects to a switch port via XOT, accepts incoming calls, and echoes back all received data
    /// byte-for-byte.
    ///
    /// Usage: x25echo [-x host:port] [-v]
    /// </remarks>
    internal static class Program
    {
        // Defaults: switch on localhost:2006, local X.121 address 23420001.
        private const string DefaultHost = "127.0.0.1";
        private const ushort DefaultPort = 2006;
        private const string LocalAddress = "23420001";

        // How many received packets may wait for the send window before the service starts dropping.
        private const int MaxPending = 100000;

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
        private sealed record EchoItem(X25Vc Vc, byte[] Data, bool QBit, bool Close = false);

        /// <summary>
        /// Longest line kept while looking for the exit command; anything longer cannot be the command.
        /// </summary>
        private const int MaxLine = 64;

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments: <c>-x host:port</c>, <c>-v</c> (repeatable).
        /// </param>
        /// <returns>
        /// Process exit code (0 on clean shutdown, 1 on usage error or connect failure).
        /// </returns>
        private static int Main(string[] args)
        {
            string host = DefaultHost;
            ushort port = DefaultPort;
            string localAddress = LocalAddress;
            int verbosity = 0;
            bool quiet = false;

            // Parse -x host:port, -a address, -q and -v.
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "-x")
                {
                    if (i + 1 >= args.Length)
                    {
                        return Usage();
                    }

                    ToolSupport.ParseEndpoint(args[++i], DefaultHost, DefaultPort, out host, out port);
                }
                else if (arg == "-a")
                {
                    if (i + 1 >= args.Length)
                    {
                        return Usage();
                    }

                    localAddress = args[++i];
                }
                else if (arg == "-q")
                {
                    quiet = true;
                }
                else if (arg == "-v")
                {
                    verbosity++;
                }
                else
                {
                    return Usage();
                }
            }

            using ILoggerFactory loggerFactory = ToolSupport.CreateLoggerFactory(verbosity);
            ILogger log = loggerFactory.CreateLogger("x25echo");

            X25ClientConfig cfg = new X25ClientConfig
            {
                Host = host,
                Port = port,
                Framing = X25ClientFraming.Xot,
                IsDte = true,
                PacketSize = 128,
                WindowSize = 7,
                NumVcs = 2048,
                LocalAddress = localAddress
            };

            X25Client client = new X25Client { ServiceName = "Echo" };

            // The queue of packets waiting for the send window. The greeting, the echoes and the
            // goodbye all go through it so they leave in the order they were made.
            ConcurrentQueue<EchoItem> pending = new ConcurrentQueue<EchoItem>();
            long dropped = 0;

            // Per circuit: true when the last byte echoed was a CR that was turned into CR LF, so a
            // following LF is not echoed a second time.
            ConcurrentDictionary<X25Vc, bool> afterCr = new ConcurrentDictionary<X25Vc, bool>();

            // Text typed so far on each circuit, kept to spot the exit command.
            ConcurrentDictionary<X25Vc, System.Text.StringBuilder> lines = new ConcurrentDictionary<X25Vc, System.Text.StringBuilder>();

            // Incoming call: auto-accept, then greet. The greeting is an extra packet the caller did not
            // send, so it is skipped with -q: x25traffic counts every packet it gets back, and a
            // performance run must see exactly what it sent.
            client.SetOnIncoming((c, vc) =>
            {
                log.LogInformation("Echo: Incoming call from {Calling}, accepting", vc.CallingAddress);
                c.Accept(vc);
                if (!quiet)
                {
                    pending.Enqueue(new EchoItem(vc, System.Text.Encoding.ASCII.GetBytes("Welcome to the echo server. Type exit to leave.\r\n"), false));
                }
            });

            // Data received: echo it back byte-for-byte.
            //
            // Send() answers false when the send window to the switch is full. Ignoring that answer
            // (as this tool did first) throws the packet away: at full speed a third of the echoes were
            // lost with no error anywhere, which made every "delivery" figure measured through the echo
            // service wrong. So the packet is copied into a queue and a sender thread keeps trying until
            // the window has room. The receive callback itself must not wait, because the same thread
            // delivers the acknowledgements that open the window.
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                if (pending.Count >= MaxPending)
                {
                    // The sender is far faster than the window lets us answer. Dropping is the last
                    // resort and is counted and reported, never silent.
                    long n = Interlocked.Increment(ref dropped);
                    if (n == 1 || n % 1000 == 0)
                    {
                        log.LogWarning("Echo: queue full, {Count} packet(s) dropped so far", n);
                    }

                    return;
                }

                byte[] copy;
                if (quiet || qBit)
                {
                    // A plain echo, byte for byte (and never touch an X.29 control message).
                    copy = new byte[length];
                    Buffer.BlockCopy(data, 0, copy, 0, length);
                }
                else
                {
                    afterCr.TryGetValue(vc, out bool previousWasCr);
                    copy = EchoForTerminal(data, length, ref previousWasCr);
                    afterCr[vc] = previousWasCr;
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
                    pending.Enqueue(new EchoItem(vc, part, qBit));
                }

                if (quiet)
                {
                    return;
                }

                // Look for the exit command: a line of the word exit (any case) ended by CR or LF, or a
                // Ctrl-D (0x04). The line is echoed first like any other text, then the goodbye goes out
                // and the call is cleared.
                System.Text.StringBuilder line = lines.GetOrAdd(vc, _ => new System.Text.StringBuilder());
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
                        pending.Enqueue(new EchoItem(vc, System.Text.Encoding.ASCII.GetBytes("\r\nGoodbye.\r\n"), false));
                        // X.29 section 3.2.1: the invitation to clear asks the PAD to clear the call after it
                        // has delivered all earlier data to the terminal. It is a Q-bit packet holding the
                        // single octet 0x01 (X.29 4.4.3). The echo clears itself only if the peer does not.
                        pending.Enqueue(new EchoItem(vc, new byte[] { 0x01 }, true, true));
                        lines.TryRemove(vc, out _);
                        return;
                    }
                }
            });

            Thread sender = new Thread(() =>
            {
                int idle = 0;
                while (true)
                {
                    if (!pending.TryPeek(out EchoItem? next))
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    // A circuit that has cleared has nobody to echo to.
                    if (!next.Vc.Connected || client.Send(next.Vc, next.Data, next.Data.Length, next.QBit))
                    {
                        pending.TryDequeue(out _);
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
                            log.LogInformation("Echo: caller typed exit (invitation acknowledged: {Acknowledged}, peer cleared: {PeerCleared})", acknowledged, !next.Vc.Connected);
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
            })
            { IsBackground = true, Name = "echo-sender" };
            sender.Start();

            // Clear: just log.
            client.SetOnClear((c, vc, cause, diag) =>
            {
                lines.TryRemove(vc, out _);
                afterCr.TryRemove(vc, out _);
                log.LogInformation("Echo: Call cleared (cause={Cause:X2} diag={Diag:X2})", cause, diag);
            });

            log.LogInformation("Echo: Connecting to {Host}:{Port}...", host, port);

            try
            {
                client.Open(cfg, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Echo: Failed to connect");
                return 1;
            }

            log.LogInformation("Echo: Connected");
            log.LogInformation("Echo: Service ready, waiting for calls...");

            // Ctrl+C stops the service cleanly.
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                log.LogInformation("Echo: Shutting down");
                client.Close();
            };

            // Block until Close() is called from the Ctrl+C handler.
            client.Run();

            client.Dispose();
            return 0;
        }

        /// <summary>
        /// Builds the echo of a typed packet for a person at a terminal: every CR goes back as CR LF,
        /// so the next echoed line starts on its own row instead of drawing over this one.
        /// </summary>
        /// <remarks>
        /// A PAD in line mode, such as a Cisco router's, ends a typed line with a bare CR. Echoed
        /// byte for byte, that CR returns the cursor to the start of the same row and the next echo
        /// overwrites the line. An LF that directly follows a CR (in the same packet or the next one) is
        /// part of the same line end and is not echoed again.
        /// </remarks>
        /// <param name="data">
        /// The packet's user data.
        /// </param>
        /// <param name="length">
        /// Number of valid bytes in <paramref name="data"/>.
        /// </param>
        /// <param name="previousWasCr">
        /// True on entry when the last byte of the circuit's previous packet was a CR that was turned
        /// into CR LF; set on exit for the next packet.
        /// </param>
        internal static byte[] EchoForTerminal(byte[] data, int length, ref bool previousWasCr)
        {
            byte[] result = new byte[length * 2];
            int n = 0;

            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                if (b == 0x0A && previousWasCr)
                {
                    previousWasCr = false;
                    continue;
                }

                result[n++] = b;
                if (b == 0x0D)
                {
                    result[n++] = 0x0A;
                    previousWasCr = true;
                }
                else
                {
                    previousWasCr = false;
                }
            }

            byte[] echo = new byte[n];
            Buffer.BlockCopy(result, 0, echo, 0, n);
            return echo;
        }

        /// <summary>
        /// Prints usage to standard error and returns the error exit code.
        /// </summary>
        /// <returns>
        /// The exit code 1.
        /// </returns>
        private static int Usage()
        {
            Console.Error.WriteLine("Usage: x25echo [-x host:port] [-a address] [-q] [-v]");
            Console.Error.WriteLine("  -x host:port  XOT switch to connect to (default 127.0.0.1:2006)");
            Console.Error.WriteLine("  -a address    Local X.121 address to answer on (default 23420001)");
            Console.Error.WriteLine("  -q            Quiet: no welcome text, no exit command and no CR to CR LF change, only a plain byte-for-byte echo");
            Console.Error.WriteLine("                (use for x25traffic runs, which count every packet they get back)");
            Console.Error.WriteLine("  -v            Verbose (repeat for more)");
            return 1;
        }
    }
}
