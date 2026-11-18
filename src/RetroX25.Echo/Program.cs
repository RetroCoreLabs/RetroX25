//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Client;

namespace RetroX25.Echo
{
    /// <summary>
    /// x25echo - X.25 Echo Service.
    /// </summary>
    /// <remarks>
    /// Connects to a switch port via XOT, accepts incoming calls, and echoes back all received data
    /// byte-for-byte. When the switch is not there it keeps dialling until it is (<see cref="EchoService"/>).
    ///
    /// Usage: x25echo [-x host:port] [-v]
    /// </remarks>
    internal static class Program
    {
        // Defaults: switch on localhost:2006, local X.121 address 23420001.
        private const string DefaultHost = "127.0.0.1";
        private const ushort DefaultPort = 2006;
        private const string LocalAddress = "23420001";

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments: <c>-x host:port</c>, <c>-v</c> (repeatable).
        /// </param>
        /// <returns>
        /// Process exit code (0 on clean shutdown, 1 on a usage error). A switch that does not answer is not an error: the service waits for it.
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

            // The service dials the switch until it answers (see EchoService): a DTE started while its link is down
            // keeps trying to bring the link up, it does not exit.
            using EchoService service = new EchoService(cfg, quiet, log);

            // Ctrl+C stops the service cleanly, also while it is still waiting for the switch.
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                log.LogInformation("Echo: Shutting down");
                service.Stop();
            };

            log.LogInformation("Echo: Connecting to {Host}:{Port}...", host, port);
            if (!service.Open())
            {
                // Stopped (Ctrl+C) before the switch ever answered.
                return 0;
            }

            log.LogInformation("Echo: Service ready, waiting for calls...");

            // Block until Stop() is called from the Ctrl+C handler.
            service.Run();
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
