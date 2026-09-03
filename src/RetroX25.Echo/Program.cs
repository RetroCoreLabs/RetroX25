using System;
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

            // Parse -x host:port, -a address, and -v.
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

            // Incoming call: auto-accept.
            client.SetOnIncoming((c, vc) =>
            {
                log.LogInformation("Echo: Incoming call from {Calling}, accepting", vc.CallingAddress);
                c.Accept(vc);
            });

            // Data received: echo it back byte-for-byte.
            client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
            {
                c.Send(vc, data, length, qBit);
            });

            // Clear: just log.
            client.SetOnClear((c, vc, cause, diag) =>
            {
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
        /// Prints usage to standard error and returns the error exit code.
        /// </summary>
        /// <returns>
        /// The exit code 1.
        /// </returns>
        private static int Usage()
        {
            Console.Error.WriteLine("Usage: x25echo [-x host:port] [-a address] [-v]");
            Console.Error.WriteLine("  -x host:port  XOT switch to connect to (default 127.0.0.1:2006)");
            Console.Error.WriteLine("  -a address    Local X.121 address to answer on (default 23420001)");
            Console.Error.WriteLine("  -v            Verbose (repeat for more)");
            return 1;
        }
    }
}
