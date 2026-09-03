using System;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIO.Protocols.X25.Client;
using RetroIO.Link;
using RetroIO.Link.Framing;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;

namespace RetroX25.Trace
{
    /// <summary>
    /// x25trace - X.25 packet capture and decode tool.
    /// </summary>
    /// <remarks>
    /// Connects to (or listens on) an XOT port and passively
    /// decodes every X.25 packet it sees. It reuses the existing building blocks rather than re-implementing
    /// them: <see cref="TcpLinkTransport"/> for the socket, <see cref="XotFrameCodec"/> to de-frame XOT, and
    /// <see cref="X25PacketParser"/> to decode each packet.
    ///
    /// Usage: x25trace [-x host:port] [-l port] [-v]
    ///   -x host:port  Connect to XOT peer and decode packets.
    ///   -l port       Listen for an incoming XOT connection.
    ///   -v            Increase verbosity (repeat for hex dumps).
    /// </remarks>
    internal static class Program
    {
        private const string DefaultHost = "localhost";
        private const ushort DefaultPort = 1998;

        private static int _verbosity;
        private static readonly ManualResetEventSlim Shutdown = new ManualResetEventSlim(false);

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments: <c>-x host:port</c>, <c>-l port</c>, <c>-v</c>.
        /// </param>
        /// <returns>
        /// Process exit code (0 on clean shutdown, 1 on usage or transport error).
        /// </returns>
        private static int Main(string[] args)
        {
            string host = DefaultHost;
            ushort port = DefaultPort;
            bool listenMode = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "-x":
                        if (i + 1 >= args.Length) { return Usage(); }
                        ToolSupport.ParseEndpoint(args[++i], DefaultHost, DefaultPort, out host, out port);
                        break;
                    case "-l":
                        if (i + 1 >= args.Length || !ushort.TryParse(args[++i], out port)) { return Usage(); }
                        listenMode = true;
                        // -l gives only a port; bind on all interfaces (the transport parses this as the
                        // bind IP, and "localhost" is not a valid IP literal for it).
                        host = "0.0.0.0";
                        break;
                    case "-v":
                        _verbosity++;
                        break;
                    default:
                        return Usage();
                }
            }

            // The transport wants a logger; keep the wire quiet (trace prints its own decode to stdout).
            ILogger transportLogger = NullLogger.Instance;

            // De-framer: raw TCP bytes -> XOT frames (each a complete X.25 packet). Passive, so the codec's
            // send-side transport is a no-op sink.
            XotFrameCodec codec = new XotFrameCodec("trace", new NullFrameTransport());
            codec.FrameReceived += OnFrame;

            TcpLinkTransport transport = new TcpLinkTransport(transportLogger, "trace", listenMode, host, port);
            transport.DataReceived += (name, data) => codec.ProcessBytes(data);
            transport.Closed += (name, reason) =>
            {
                Console.Error.WriteLine("\nConnection closed.");
                Shutdown.Set();
            };

            if (listenMode)
            {
                Console.Error.WriteLine($"Listening on port {port}...");
            }
            else
            {
                Console.Error.WriteLine($"Connecting to {host}:{port}...");
            }

            if (!transport.Open())
            {
                Console.Error.WriteLine("Failed to open transport");
                return 1;
            }

            Console.Error.WriteLine("Connected. Capturing X.25 packets...\n");

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Shutdown.Set();
            };

            Shutdown.Wait();
            transport.Close();
            return 0;
        }

        /// <summary>
        /// Handles one de-framed X.25 packet: decodes and prints it.
        /// </summary>
        /// <param name="linkId">
        /// The originating link identifier (unused).
        /// </param>
        /// <param name="frame">
        /// The complete X.25 packet bytes.
        /// </param>
        private static void OnFrame(string linkId, ReadOnlySpan<byte> frame)
        {
            byte[] pkt = frame.ToArray();
            DecodePacket(pkt);
        }

        /// <summary>
        /// Decodes and prints a single X.25 packet in a compact single-line format.
        /// </summary>
        /// <param name="pkt">
        /// The packet bytes.
        /// </param>
        private static void DecodePacket(byte[] pkt)
        {
            DateTime now = DateTime.Now;
            string stamp = $"[{now.Second + now.Minute * 60}.{now.Millisecond:D3}]";

            if (pkt.Length < 3)
            {
                Console.WriteLine($"{stamp} ??? (too short: {pkt.Length} bytes)");
                return;
            }

            X25PacketInfo info = X25PacketParser.ParsePacket(pkt);

            if (info.PacketType == X25PacketType.Data)
            {
                Console.WriteLine($"{stamp} DATA  LCI={info.LCN,-4} P(S)={info.PS} P(R)={info.PR} " +
                                  $"M={(info.MBit ? 1 : 0)} Q={(info.QBit ? 1 : 0)} D={(info.DBit ? 1 : 0)} " +
                                  $"[{pkt.Length - 3} bytes]");
                if (_verbosity >= 2 && pkt.Length > 3)
                {
                    HexDump(pkt, 3, pkt.Length - 3);
                }

                return;
            }

            string name = info.GetPacketTypeName();
            StringBuilder line = new StringBuilder();
            line.Append($"{stamp} {name,-30} LCI={info.LCN,-4}");

            switch (info.PacketType)
            {
                case X25PacketType.CallRequest:
                case X25PacketType.CallAccepted:
                    Console.WriteLine(line.ToString());
                    Console.WriteLine($"    Called:  {info.CalledAddress}");
                    Console.WriteLine($"    Calling: {info.CallingAddress}");
                    break;

                case X25PacketType.ClearRequest:
                    Console.WriteLine($"{line} cause={info.ClearingCause:X2} diag={info.DiagnosticCode:X2}");
                    break;

                case X25PacketType.ResetRequest:
                    Console.WriteLine($"{line} cause={info.ResetCause:X2} diag={info.DiagnosticCode:X2}");
                    break;

                case X25PacketType.RR:
                case X25PacketType.RNR:
                case X25PacketType.REJ:
                    Console.WriteLine($"{stamp} {name,-6} LCI={info.LCN,-4} N(R)={info.PR}");
                    break;

                default:
                    Console.WriteLine(line.ToString());
                    break;
            }

            if (_verbosity >= 2)
            {
                HexDump(pkt, 0, pkt.Length);
            }
        }

        /// <summary>
        /// Prints a hex dump of a byte range, 16 bytes per line.
        /// </summary>
        /// <param name="data">
        /// The buffer to dump.
        /// </param>
        /// <param name="offset">
        /// Start offset within <paramref name="data"/>.
        /// </param>
        /// <param name="length">
        /// Number of bytes to dump.
        /// </param>
        private static void HexDump(byte[] data, int offset, int length)
        {
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                if (i % 16 == 0)
                {
                    line.Append($"    {i:x4}: ");
                }

                line.Append($"{data[offset + i]:x2} ");

                if (i % 16 == 15 || i == length - 1)
                {
                    Console.WriteLine(line.ToString());
                    line.Clear();
                }
            }
        }

        /// <summary>
        /// Prints usage to standard error and returns the error exit code.
        /// </summary>
        /// <returns>
        /// The exit code 1.
        /// </returns>
        private static int Usage()
        {
            Console.Error.WriteLine("Usage: x25trace [-x host:port] [-l port] [-v]");
            Console.Error.WriteLine("  -x host:port  Connect to XOT peer");
            Console.Error.WriteLine("  -l port       Listen for XOT connection");
            Console.Error.WriteLine("  -v            Verbose (repeat for hex dumps)");
            return 1;
        }
    }

    /// <summary>
    /// A no-op <see cref="IFrameTransport"/> used by the passive trace codec, which never transmits.
    /// </summary>
    internal sealed class NullFrameTransport : IFrameTransport
    {
        /// <inheritdoc/>
        public void Send(ReadOnlySpan<byte> bytes)
        {
            // Trace is receive-only; nothing is ever sent.
        }
    }
}
