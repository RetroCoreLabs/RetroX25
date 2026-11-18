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
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Runtime;

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
    ///   --telnet-port N   No keyboard: listen for telnet on port N and give each telnet connection its
    ///                     own PAD and its own XOT connection (the same server the Desktop telnet PAD uses).
    ///   --telnet-bind A   Address to listen on with --telnet-port (default 127.0.0.1).
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
            int telnetPort = 0;
            string telnetBind = TelnetPadConfig.DefaultBindAddress;
            string? nui = null;

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
                    case "--telnet-port":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out telnetPort) || telnetPort < 1 || telnetPort > 65535)
                        {
                            Console.Error.WriteLine("x25pad: --telnet-port needs a number from 1 to 65535.");
                            return 1;
                        }
                        break;
                    case "--telnet-bind":
                        if (i + 1 >= args.Length) { return Usage(); }
                        telnetBind = args[++i];
                        break;
                    case "-n":
                        // A NUI for every call, as if ID and the NUI had been typed (X.28 5.2); x25emu's x25pad -n.
                        // Checked here, before anything connects, so a bad one is said at once.
                        if (i + 1 >= args.Length) { return Usage(); }
                        nui = args[++i];
                        if (!X28.TryReadNui(nui, out string cleaned) || cleaned.Length == 0
                            || string.Equals(cleaned, "OFF", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine("x25pad: -n: not a NUI (no '-', ',' or '+', not empty, not OFF).");
                            return 1;
                        }
                        break;
                    case "-v":
                        verbosity++;
                        break;
                    default:
                        return Usage();
                }
            }

            using ILoggerFactory loggerFactory = ToolSupport.CreateLoggerFactory(verbosity);
            ILogger log = loggerFactory.CreateLogger("x25pad");

            if (telnetPort > 0)
            {
                return RunTelnet(host, port, telnetPort, telnetBind, verbosity, loggerFactory, log, nui);
            }

            Console.Error.WriteLine($"Connecting to {host}:{port}...");

            X25Client client;
            PadClient pad;
            try
            {
                OpenPad(host, port, verbosity, log, out client, out pad);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to open X.25 connection");
                return 1;
            }

            // Terminal output: received data (and command responses/prompts) + local keystroke echo.
            pad.DataReceived += (sender, e) => Console.Write(e.Data);
            pad.CharacterEcho += (sender, e) => Console.Write((char)e.Character);
            pad.ConnectionStateChanged += (sender, e) => Console.Error.WriteLine($"[{e.State}] {e.Reason}");

            // EXIT (or QUIT) at the * prompt: the PAD has cleared any call; end the key loop below.
            pad.ExitRequested += (sender, e) =>
            {
                _running = false;
                Console.Write("\r\nDisconnected.\r\n");
            };

            pad.Initialize();
            if (nui != null)
            {
                pad.SetNui(nui);
            }

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
                    // Enter sends CR only. X.28 4.15: a start-stop terminal sends 0/13 (CR) and the PAD
                    // inserts any LF itself, as X.3 parameter 13 says. Sending CR LF made the far side see
                    // a second, empty line (a second prompt on a Cisco router).
                    pad.OnKey('\r');
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
        /// Opens the X.25 client to the XOT peer and builds a PAD on top of it. The caller attaches the
        /// output handlers (console or telnet) and then calls <see cref="PadClient.Start"/>.
        /// </summary>
        /// <param name="host">XOT peer host.</param>
        /// <param name="port">XOT peer port.</param>
        /// <param name="verbosity">Log detail; above 0 the client logs to <paramref name="log"/>.</param>
        /// <param name="log">Logger.</param>
        /// <param name="client">The open X.25 client; the caller disposes it.</param>
        /// <param name="pad">The PAD wired to the client; the caller disposes it.</param>
        internal static void OpenPad(string host, ushort port, int verbosity, ILogger log, out X25Client client, out PadClient pad)
        {
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

            // Open the clean X.25 client (transport + XOT framing + packet layer); the PAD drives it
            // through the transport seam and receives events via the client callbacks.
            X25Client opened = new X25Client { ServiceName = "x25pad" };
            opened.Open(cfg, verbosity > 0 ? log : NullLogger.Instance);

            X25ClientPadTransport transport = new X25ClientPadTransport(opened);
            PadClient created = new PadClient(NullLogger.Instance, transport)
            {
                LocalAddress = LocalAddress
            };

            // Inbound: the clean layer's up-events feed the PAD's delivery methods.
            opened.SetOnConnected((c, vc) => created.NotifyCallAccepted(vc.Lcn));
            opened.SetOnData((c, vc, data, length, qBit, mBit, dBit) => created.DeliverData(vc.Lcn, data, qBit));
            opened.SetOnClear((c, vc, cause, diagnostic) => created.DeliverClear(vc.Lcn, cause, diagnostic));

            client = opened;
            pad = created;
        }

        /// <summary>
        /// Telnet mode: listens on a port and gives every telnet connection its own PAD and its own XOT
        /// connection to the peer. Runs until Ctrl+C.
        /// </summary>
        private static int RunTelnet(string host, ushort port, int telnetPort, string bind, int verbosity, ILoggerFactory loggerFactory, ILogger log, string? nui)
        {
            TelnetPadConfig telnet = new TelnetPadConfig { Enabled = true, Port = telnetPort, BindAddress = bind };
            string? problem = telnet.Validate();
            if (problem != null)
            {
                Console.Error.WriteLine("x25pad: " + problem);
                return 1;
            }

            using TelnetPadHost server = new TelnetPadHost(host, port, verbosity, log, loggerFactory.CreateLogger("x25pad.telnet"), nui);
            if (!server.Start(telnet))
            {
                Console.Error.WriteLine("x25pad: " + server.GetStatus().Text);
                return 1;
            }

            Console.Error.WriteLine($"{server.GetStatus().Text}; each connection calls out through XOT {host}:{port}. Ctrl+C to stop.");

            using ManualResetEventSlim stop = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                stop.Set();
            };
            stop.Wait();
            server.Stop();
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
            Console.Error.WriteLine("Usage: x25pad [-x host:port] [-n nui] [--telnet-port N [--telnet-bind addr]] [-v]");
            Console.Error.WriteLine("  -x host:port  XOT peer (default localhost:1998)");
            Console.Error.WriteLine("  --telnet-port N    listen for telnet on port N (one PAD and one XOT connection per telnet client)");
            Console.Error.WriteLine("  --telnet-bind addr address to listen on (default 127.0.0.1)");
            Console.Error.WriteLine("  -n nui        a NUI for every call, as if ID<nui> had been typed (each telnet session too)");
            Console.Error.WriteLine("  -v            Verbose (repeat for more)");
            return 1;
        }
    }
}
