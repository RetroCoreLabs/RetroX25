//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Client;

namespace RetroX25.Traffic
{
    /// <summary>
    /// x25traffic - Traffic generator for X.25 network testing.
    /// </summary>
    /// <remarks>
    /// Two modes:
    ///  - Stream mode (default): one call, continuous data packets, throughput report.
    ///  - Call-storm mode (-C): rapid open/send/clear cycles across many virtual circuits.
    ///
    /// Usage:
    ///   x25traffic -x host:port -d address [-n count] [-s size] [-r rate]
    ///   x25traffic -x host:port -d address -C [-n calls] [-s size] [-r rate] [-p pkts]
    ///
    /// There is no explicit poll loop: the runtime services the wire on
    /// its own threads, so this port paces sends with a stopwatch and reads counters updated by callbacks.
    /// </remarks>
    internal static class Program
    {
        private const string DefaultHost = "127.0.0.1";
        private const ushort DefaultPort = 2001;
        private const int MaxStormVcs = 2048;

        // Receive counters, updated from the data callback (runtime thread) -> use interlocked access.
        private static long _recvPkts;
        private static long _recvBytes;

        // Storm connect counter, updated from the connected callback.
        private static long _stormConnects;

        // Run flag toggled by Ctrl+C and by stream-mode clear.
        private static volatile bool _running = true;
        private static bool _callMode;

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments (see the class remarks for the option set).
        /// </param>
        /// <returns>
        /// Process exit code (0 on success, 1 on usage error or call failure).
        /// </returns>
        private static int Main(string[] args)
        {
            string host = DefaultHost;
            ushort port = DefaultPort;
            string called = "23420001";
            int count = 1000;
            int pktSize = 128;
            int rate = 100;
            int pktsPerCall = 3;
            bool callMode = false;
            int verbosity = 0;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "-x":
                        if (i + 1 >= args.Length) { return Usage(); }
                        ToolSupport.ParseEndpoint(args[++i], DefaultHost, DefaultPort, out host, out port);
                        break;
                    case "-d":
                        if (i + 1 >= args.Length) { return Usage(); }
                        called = args[++i];
                        break;
                    case "-n":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out count)) { return Usage(); }
                        break;
                    case "-s":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out pktSize)) { return Usage(); }
                        if (pktSize > 128) { pktSize = 128; }
                        break;
                    case "-r":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out rate)) { return Usage(); }
                        break;
                    case "-p":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out pktsPerCall)) { return Usage(); }
                        break;
                    case "-C":
                        callMode = true;
                        break;
                    case "-v":
                        verbosity++;
                        break;
                    default:
                        return Usage();
                }
            }

            using ILoggerFactory loggerFactory = ToolSupport.CreateLoggerFactory(verbosity);
            ILogger log = loggerFactory.CreateLogger("x25traffic");

            X25ClientConfig cfg = new X25ClientConfig
            {
                Host = host,
                Port = port,
                Framing = X25ClientFraming.Xot,
                IsDte = true,
                PacketSize = 128,
                WindowSize = 7,
                NumVcs = 2048,
                LocalAddress = "99990001"
            };

            X25Client client = new X25Client { ServiceName = "Traffic" };
            client.SetOnData((c, vc, data, length, q, m, d) =>
            {
                Interlocked.Increment(ref _recvPkts);
                Interlocked.Add(ref _recvBytes, length);
            });

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _running = false;
            };

            Console.WriteLine($"Connecting to {host}:{port}...");
            try
            {
                client.Open(cfg, log);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to connect: {ex.Message}");
                return 1;
            }

            int rc;
            if (callMode)
            {
                _callMode = true;
                rc = RunCallStorm(client, called, count, pktSize, pktsPerCall, rate);
            }
            else
            {
                rc = RunStream(client, called, count, pktSize, rate);
            }

            client.Dispose();
            return rc;
        }

        /// <summary>
        /// Stream mode: one call, then send packets at the requested rate and report throughput.
        /// </summary>
        /// <param name="client">
        /// The open client.
        /// </param>
        /// <param name="called">
        /// Destination X.121 address.
        /// </param>
        /// <param name="count">
        /// Number of packets to send (0 = infinite).
        /// </param>
        /// <param name="pktSize">
        /// Packet size in bytes.
        /// </param>
        /// <param name="rate">
        /// Target packets per second (0 = maximum).
        /// </param>
        /// <returns>
        /// 0 on success, 1 if the call failed.
        /// </returns>
        private static int RunStream(X25Client client, string called, int count, int pktSize, int rate)
        {
            client.SetOnClear((c, vc, cause, diag) =>
            {
                if (!_callMode)
                {
                    _running = false; // stream mode stops when the circuit clears
                }
            });

            Console.WriteLine($"Calling {called}...");
            X25Vc? vc = client.CallWait(called, 5000);
            if (vc == null)
            {
                Console.Error.WriteLine("Call failed");
                return 1;
            }

            Console.WriteLine("Connected.\n");

            byte[] payload = new byte[pktSize];
            for (int i = 0; i < pktSize; i++)
            {
                payload[i] = (byte)(i & 0xFF);
            }

            Stopwatch sw = Stopwatch.StartNew();
            long sent = 0;
            long sentBytes = 0;
            double intervalMs = rate > 0 ? 1000.0 / rate : 0.0;
            double nextSendMs = 0.0;
            double lastReportMs = 0.0;

            Console.WriteLine($"Streaming {count} x {pktSize}-byte packets at {(rate > 0 ? rate + "/s" : "max speed")}...");
            Console.WriteLine("Press Ctrl-C to stop.\n");

            _running = true;
            while (_running && (count == 0 || sent < count))
            {
                double now = sw.Elapsed.TotalMilliseconds;

                if (rate > 0 && now < nextSendMs)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (client.Send(vc, payload, pktSize, false))
                {
                    sent++;
                    sentBytes += pktSize;
                    nextSendMs = now + intervalMs;
                }

                if (now - lastReportMs >= 2000.0)
                {
                    double el = now / 1000.0;
                    long rp = Interlocked.Read(ref _recvPkts);
                    long rb = Interlocked.Read(ref _recvBytes);
                    Console.WriteLine($"  Sent: {sent} pkts ({KbitPerSec(sentBytes, el):F0} Kbit/s)  " +
                                      $"Recv: {rp} pkts ({KbitPerSec(rb, el):F0} Kbit/s)");
                    lastReportMs = now;
                }
            }

            double elapsed = sw.Elapsed.TotalSeconds;
            long recvPkts = Interlocked.Read(ref _recvPkts);
            long recvBytes = Interlocked.Read(ref _recvBytes);

            Console.WriteLine("\n=== Stream Results ===");
            Console.WriteLine($"Duration:  {elapsed:F2} seconds");
            Console.WriteLine($"Sent:      {sent} pkts, {sentBytes} bytes ({KbitPerSec(sentBytes, elapsed):F0} Kbit/s)");
            Console.WriteLine($"Received:  {recvPkts} pkts, {recvBytes} bytes ({KbitPerSec(recvBytes, elapsed):F0} Kbit/s)");
            if (sent > 0)
            {
                Console.WriteLine($"Delivery:  {100.0 * recvPkts / sent:F1}%");
            }

            client.Clear(vc);
            return 0;
        }

        /// <summary>
        /// Call-storm mode: open many circuits, send on each, then clear them all, reporting timings.
        /// </summary>
        /// <param name="client">
        /// The open client.
        /// </param>
        /// <param name="called">
        /// Destination X.121 address.
        /// </param>
        /// <param name="numCalls">
        /// Number of circuits to open (clamped to the storm maximum).
        /// </param>
        /// <param name="pktSize">
        /// Packet size in bytes.
        /// </param>
        /// <param name="pktsPerCall">
        /// Packets to send on each connected circuit.
        /// </param>
        /// <param name="rate">
        /// Target call-setup rate in calls per second (0 = maximum).
        /// </param>
        /// <returns>
        /// 0 on success.
        /// </returns>
        private static int RunCallStorm(X25Client client, string called, int numCalls, int pktSize, int pktsPerCall, int rate)
        {
            if (numCalls <= 0) { numCalls = 1000; }
            if (numCalls > MaxStormVcs) { numCalls = MaxStormVcs; }

            client.SetOnConnected((c, vc) => Interlocked.Increment(ref _stormConnects));

            byte[] payload = new byte[pktSize];
            for (int i = 0; i < pktSize; i++)
            {
                payload[i] = (byte)(i & 0xFF);
            }

            X25Vc?[] vcs = new X25Vc?[numCalls];
            int vcCount = 0;

            Console.WriteLine($"Opening {numCalls} virtual circuits to {called}...");

            Stopwatch total = Stopwatch.StartNew();
            int opened = 0;
            int failed = 0;
            double lastReportMs = 0.0;

            // Phase 1: open circuits (paced to the call-setup rate).
            for (int i = 0; i < numCalls && _running; i++)
            {
                X25Vc? vc = client.Call(called);
                if (vc == null)
                {
                    failed++;
                    continue;
                }

                vcs[vcCount++] = vc;
                opened++;

                if (rate > 0)
                {
                    Thread.Sleep((int)Math.Max(0, 1000 / rate));
                }

                double now = total.Elapsed.TotalMilliseconds;
                if (now - lastReportMs >= 1000.0)
                {
                    Console.WriteLine($"  Opening: {i + 1}/{numCalls} (connected: {Interlocked.Read(ref _stormConnects)}, failed: {failed})");
                    lastReportMs = now;
                }
            }

            // Wait for outstanding connects (up to ~5s).
            for (int w = 0; w < 5000 && Interlocked.Read(ref _stormConnects) < opened; w++)
            {
                Thread.Sleep(1);
            }

            double phase1Sec = total.Elapsed.TotalSeconds;
            long connected = Interlocked.Read(ref _stormConnects);
            Console.WriteLine($"\n  Opened: {opened} VCs ({connected} connected, {failed} failed) in {phase1Sec:F2} sec");

            // Phase 2: send on every connected circuit.
            Console.WriteLine($"  Sending {pktsPerCall} packets on each VC...");
            long totalPkts = 0;
            long totalBytes = 0;
            Stopwatch phase2 = Stopwatch.StartNew();

            for (int p = 0; p < pktsPerCall && _running; p++)
            {
                for (int i = 0; i < vcCount; i++)
                {
                    X25Vc? vc = vcs[i];
                    if (vc == null || !vc.Connected)
                    {
                        continue;
                    }

                    payload[0] = (byte)(i >> 8);
                    payload[1] = (byte)(i & 0xFF);
                    payload[2] = (byte)p;
                    if (client.Send(vc, payload, pktSize, false))
                    {
                        totalPkts++;
                        totalBytes += pktSize;
                    }
                }
            }

            double phase2Sec = phase2.Elapsed.TotalSeconds;
            Console.WriteLine($"  Sent: {totalPkts} packets, {totalBytes} bytes in {phase2Sec:F2} sec");

            // Phase 3: clear all circuits.
            Console.WriteLine($"  Clearing {vcCount} VCs...");
            Stopwatch phase3 = Stopwatch.StartNew();
            int cleared = 0;
            for (int i = 0; i < vcCount && _running; i++)
            {
                X25Vc? vc = vcs[i];
                if (vc != null)
                {
                    client.Clear(vc);
                    cleared++;
                }
            }

            Thread.Sleep(500); // let clears drain
            double phase3Sec = phase3.Elapsed.TotalSeconds;
            double totalSec = total.Elapsed.TotalSeconds;

            Console.WriteLine("\n=== Call Storm Results ===");
            Console.WriteLine($"VCs opened:    {opened} ({connected} connected, {failed} failed)");
            Console.WriteLine($"Setup time:    {phase1Sec:F2} sec ({(phase1Sec > 0 ? opened / phase1Sec : 0):F0} calls/sec)");
            Console.WriteLine($"Data sent:     {totalPkts} packets, {totalBytes} bytes ({KbitPerSec(totalBytes, phase2Sec):F0} Kbit/s)");
            Console.WriteLine($"Teardown:      {phase3Sec:F2} sec ({(phase3Sec > 0 ? cleared / phase3Sec : 0):F0} clears/sec)");
            Console.WriteLine($"Total time:    {totalSec:F2} sec");

            return 0;
        }

        /// <summary>
        /// Computes throughput in kilobits per second from a byte count and an elapsed time.
        /// </summary>
        /// <param name="bytes">
        /// Number of bytes transferred.
        /// </param>
        /// <param name="seconds">
        /// Elapsed time in seconds.
        /// </param>
        /// <returns>
        /// Throughput in Kbit/s, or 0 when <paramref name="seconds"/> is not positive.
        /// </returns>
        private static double KbitPerSec(long bytes, double seconds)
        {
            return seconds > 0 ? bytes * 8.0 / seconds / 1000.0 : 0.0;
        }

        /// <summary>
        /// Prints usage to standard error and returns the error exit code.
        /// </summary>
        /// <returns>
        /// The exit code 1.
        /// </returns>
        private static int Usage()
        {
            Console.Error.WriteLine(
                "Usage: x25traffic -x host:port -d address [options]\n" +
                "  Stream mode (default):\n" +
                "    -n count   Packets to send (0=infinite)\n" +
                "    -r rate    Packets/sec (0=max)\n" +
                "  Call storm mode (-C):\n" +
                "    -C         Enable call storm\n" +
                "    -n count   Number of calls (0=infinite)\n" +
                "    -r rate    Calls/sec (0=max)\n" +
                "    -p pkts    Packets per call (default 3)\n" +
                "  Common:\n" +
                "    -s size    Packet size (default 128)\n" +
                "    -v         Verbose");
            return 1;
        }
    }
}
