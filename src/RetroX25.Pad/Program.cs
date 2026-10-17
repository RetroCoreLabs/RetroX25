//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Modules;

namespace RetroX25.Pad
{
    /// <summary>
    /// x25pad - Interactive X.25 PAD client.
    /// </summary>
    /// <remarks>
    /// Connects to a switch via XOT and presents an X.28 PAD
    /// prompt; use commands such as <c>CALL addr</c>, <c>CLR</c>, <c>SET param:val</c>, <c>PAR</c>, <c>STAT</c>.
    /// The X.3/X.28/X.29 logic is reused from <see cref="PadClient"/>; this program is only the terminal
    /// front-end (feed keystrokes in, print echo + received data out), mirroring how the GUI drives it.
    ///
    /// Usage: x25pad [-x host:port] [-v]
    ///   -x host:port  XOT peer (default localhost:1998).
    ///   -v            Verbose (repeat for more).
    ///
    /// The <c>-s</c> (SLIP/LAPB) and <c>-l</c> (listen) modes are not yet wired; the program reports that
    /// and exits when they are requested.
    /// </remarks>
    internal static class Program
    {
        private const string DefaultHost = "localhost";
        private const ushort DefaultPort = 1998;
        private const string LocalAddress = "10000";

        private static volatile bool _running = true;

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments: <c>-x host:port</c>, <c>-v</c>.
        /// </param>
        /// <returns>
        /// Process exit code (0 on clean shutdown, 1 on usage error, connect failure, or unsupported mode).
        /// </returns>
        private static int Main(string[] args)
        {
            string host = DefaultHost;
            ushort port = DefaultPort;
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
                    case "-s":
                        Console.Error.WriteLine("x25pad: SLIP/LAPB mode (-s) is not yet supported in this port; use -x (XOT).");
                        return 1;
                    case "-l":
                        Console.Error.WriteLine("x25pad: listen mode (-l) is not yet supported in this port; use -x (XOT).");
                        return 1;
                    case "-v":
                        verbosity++;
                        break;
                    default:
                        return Usage();
                }
            }

            using ILoggerFactory loggerFactory = ToolSupport.CreateLoggerFactory(verbosity);
            ILogger log = loggerFactory.CreateLogger("x25pad");

            X25ClientConfig cfg = new X25ClientConfig
            {
                Host = host,
                Port = port,
                Framing = X25ClientFraming.Xot,
                IsDte = true,
                PacketSize = 128,
                WindowSize = 7,
                NumVcs = 8,
                LocalAddress = LocalAddress
            };

            Console.Error.WriteLine($"Connecting to {host}:{port}...");

            // Open the clean X.25 client (transport + XOT framing + packet layer); the PAD drives it
            // through the transport seam and receives events via the client callbacks.
            X25Client client = new X25Client { ServiceName = "x25pad" };
            try
            {
                client.Open(cfg, verbosity > 0 ? log : NullLogger.Instance);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to open X.25 connection");
                return 1;
            }

            X25ClientPadTransport transport = new X25ClientPadTransport(client);
            PadClient pad = new PadClient(NullLogger.Instance, transport)
            {
                LocalAddress = LocalAddress
            };

            // Inbound: the clean layer's up-events feed the PAD's delivery methods.
            client.SetOnConnected((c, vc) => pad.NotifyCallAccepted(vc.Lcn));
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) => pad.DeliverData(vc.Lcn, data, qBit));
            client.SetOnClear((c, vc, cause, diagnostic) => pad.DeliverClear(vc.Lcn));

            // Terminal output: received data (and command responses/prompts) + local keystroke echo.
            pad.DataReceived += (sender, e) => Console.Write(e.Data);
            pad.CharacterEcho += (sender, e) => Console.Write((char)e.Character);
            pad.ConnectionStateChanged += (sender, e) => Console.Error.WriteLine($"[{e.State}] {e.Reason}");

            pad.Initialize();

            Console.Error.WriteLine("Transport connected");
            Console.Write("X.25 PAD ready (XOT mode)\r\n*");

            // Ctrl+C tears down cleanly.
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _running = false;
                Console.Write("\r\nDisconnected.\r\n");
                pad.Dispose();
                client.Dispose();
                Environment.Exit(0);
            };

            // Feed each keystroke to the PAD; X.28 handles command vs data mode, CALL routing, and echo.
            while (_running)
            {
                ConsoleKeyInfo key;
                try
                {
                    key = Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException)
                {
                    // stdin is redirected / not a console: nothing to read interactively.
                    break;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    // Enter sends CR+LF (X.28 line handling).
                    pad.OnKey('\r');
                    pad.OnKey('\n');
                }
                else
                {
                    pad.OnKey(key.KeyChar);
                }
            }

            pad.Dispose();
            client.Dispose();
            return 0;
        }

        /// <summary>
        /// Prints usage to standard error and returns the error exit code.
        /// </summary>
        /// <returns>
        /// The exit code 1.
        /// </returns>
        private static int Usage()
        {
            Console.Error.WriteLine("Usage: x25pad [-x host:port] [-v]");
            Console.Error.WriteLine("  -x host:port  XOT peer (default localhost:1998)");
            Console.Error.WriteLine("  -v            Verbose (repeat for more)");
            return 1;
        }
    }
}
