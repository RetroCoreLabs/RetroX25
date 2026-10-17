//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIO.Protocols.X25.Client;

namespace RetroX25.Nmc
{
    /// <summary>
    /// x25nmc - X.25 network monitor console.
    /// </summary>
    /// <remarks>
    /// Connects to a switch access port over XOT, places an X.25 call to the switch's management address,
    /// logs in, then periodically polls switch info and statistics and prints live events. It exercises the
    /// switch's machine management API (qualifier-bit requests) and event stream.
    ///
    /// Usage: x25nmc [-x host:port] [-a mgmt-addr] [-u user] [-p pass] [-i interval] [-v]
    /// </remarks>
    internal static class Program
    {
        private const string DefaultHost = "127.0.0.1";
        private const ushort DefaultPort = 2001;

        private static readonly object Lock = new object();
        private static readonly ManualResetEventSlim Shutdown = new ManualResetEventSlim(false);
        private static readonly StringBuilder AuthBuffer = new StringBuilder();
        private static readonly StringBuilder ApiBuffer = new StringBuilder();

        private static X25Client _client = null!;
        private static X25Vc? _vc;
        private static string _user = "admin";
        private static string _pass = "admin";
        private static int _authPhase;
        private static bool _authenticated;
        private static bool _subscribed;
        private static volatile bool _running = true;

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments (see the class remarks).
        /// </param>
        /// <returns>
        /// Process exit code (0 on clean shutdown, 1 on usage or connect failure).
        /// </returns>
        private static int Main(string[] args)
        {
            string host = DefaultHost;
            ushort port = DefaultPort;
            string mgmtAddr = "23400000";
            int intervalSec = 2;
            int verbosity = 0;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-x":
                        if (i + 1 >= args.Length) { return Usage(); }
                        ToolSupport.ParseEndpoint(args[++i], DefaultHost, DefaultPort, out host, out port);
                        break;
                    case "-a": if (i + 1 >= args.Length) { return Usage(); } mgmtAddr = args[++i]; break;
                    case "-u": if (i + 1 >= args.Length) { return Usage(); } _user = args[++i]; break;
                    case "-p": if (i + 1 >= args.Length) { return Usage(); } _pass = args[++i]; break;
                    case "-i":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out intervalSec)) { return Usage(); }
                        break;
                    case "-v": verbosity++; break;
                    default: return Usage();
                }
            }

            using ILoggerFactory loggerFactory = ToolSupport.CreateLoggerFactory(verbosity);
            ILogger log = loggerFactory.CreateLogger("x25nmc");

            X25ClientConfig cfg = new X25ClientConfig
            {
                Host = host,
                Port = port,
                Framing = X25ClientFraming.Xot,
                IsDte = true,
                PacketSize = 128,
                WindowSize = 7,
                NumVcs = 20,
                LocalAddress = "99990002"
            };

            _client = new X25Client { ServiceName = "Nmc" };
            _client.SetOnConnected((c, vc) => Console.WriteLine("[nmc] circuit connected"));
            _client.SetOnData(OnData);
            _client.SetOnClear((c, vc, cause, diag) =>
            {
                Console.WriteLine($"[nmc] circuit cleared (cause={cause:X2})");
                _running = false;
                Shutdown.Set();
            });

            Console.WriteLine($"[nmc] connecting to {host}:{port}, calling {mgmtAddr}...");
            try
            {
                _client.Open(cfg, verbosity > 0 ? log : NullLogger.Instance);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[nmc] connect failed: {ex.Message}");
                return 1;
            }

            _vc = _client.Call(mgmtAddr);
            if (_vc == null)
            {
                Console.Error.WriteLine("[nmc] call to management address failed");
                return 1;
            }

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _running = false;
                Shutdown.Set();
            };

            Thread poller = new Thread(() => PollLoop(intervalSec)) { IsBackground = true, Name = "nmc-poll" };
            poller.Start();

            Shutdown.Wait();
            _client.Dispose();
            return 0;
        }

        private static void OnData(X25Client client, X25Vc vc, byte[] data, int length, bool qBit, bool mBit, bool dBit)
        {
            string text = Encoding.ASCII.GetString(data, 0, length);
            lock (Lock)
            {
                if (!_authenticated)
                {
                    AuthBuffer.Append(text);
                    RunAuth();
                }
                else
                {
                    ApiBuffer.Append(text);
                    ParseApi();
                }
            }
        }

        private static void RunAuth()
        {
            string buf = AuthBuffer.ToString();

            // The login prompts are matched as substrings; each field is answered with a bare CR.
            if (_authPhase == 0 && buf.Contains("Username:"))
            {
                SendLine(_user, qBit: false);
                _authPhase = 1;
                AuthBuffer.Clear();
            }
            else if (_authPhase == 1 && buf.Contains("Password:"))
            {
                SendLine(_pass, qBit: false);
                _authPhase = 2;
                AuthBuffer.Clear();
            }
            else if (_authPhase == 2)
            {
                if (buf.Contains("Welcome"))
                {
                    _authenticated = true;
                    AuthBuffer.Clear();
                    Console.WriteLine("[nmc] authenticated");
                }
                else if (buf.Contains("incorrect"))
                {
                    _authPhase = 0;
                    AuthBuffer.Clear();
                    Console.WriteLine("[nmc] login incorrect");
                }
            }
        }

        private static void ParseApi()
        {
            string all = ApiBuffer.ToString();
            int nl;
            while ((nl = all.IndexOf('\n')) >= 0)
            {
                string line = all.Substring(0, nl);
                all = all.Substring(nl + 1);
                HandleApiLine(line);
            }

            ApiBuffer.Clear();
            ApiBuffer.Append(all);
        }

        private static void HandleApiLine(string line)
        {
            if (line.StartsWith("INFO ", StringComparison.Ordinal))
            {
                Console.WriteLine("[info]  " + line.Substring(5).Replace('|', ' '));
            }
            else if (line.StartsWith("STATS ", StringComparison.Ordinal))
            {
                Console.WriteLine("[stats] " + line.Substring(6).Replace('|', ' '));
            }
            else if (line.StartsWith("EVENT ", StringComparison.Ordinal))
            {
                Console.WriteLine("[event] " + line.Substring(6));
            }
            else if (line.StartsWith("OK ", StringComparison.Ordinal))
            {
                if (line.Contains("subscribed"))
                {
                    _subscribed = true;
                }
            }
        }

        private static void PollLoop(int intervalSec)
        {
            int intervalMs = intervalSec > 0 ? intervalSec * 1000 : 2000;
            while (_running)
            {
                Thread.Sleep(intervalMs);
                if (!_running)
                {
                    break;
                }

                lock (Lock)
                {
                    if (!_authenticated || _vc == null)
                    {
                        continue;
                    }

                    if (!_subscribed)
                    {
                        SendLine("SUBSCRIBE", qBit: true);
                    }

                    SendLine("GET_INFO", qBit: true);
                    SendLine("GET_STATS", qBit: true);
                }
            }
        }

        private static void SendLine(string text, bool qBit)
        {
            X25Vc? vc = _vc;
            if (vc == null)
            {
                return;
            }

            // Auth lines are CR-terminated; API requests are LF-terminated.
            string framed = qBit ? text + "\n" : text + "\r";
            byte[] bytes = Encoding.ASCII.GetBytes(framed);
            _client.Send(vc, bytes, bytes.Length, qBit);
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: x25nmc [-x host:port] [-a mgmt-addr] [-u user] [-p pass] [-i interval] [-v]");
            return 1;
        }
    }
}
