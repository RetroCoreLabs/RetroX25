//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Pad
{
    /// <summary>
    /// The telnet side of x25pad: listens on a TCP port and gives every telnet connection its own PAD
    /// and its own XOT connection to the peer. The listener is the same <see cref="TelnetPadServer"/> the
    /// Desktop uses; only the way a PAD is made differs (here each PAD owns an <see cref="X25Client"/>).
    /// </summary>
    internal sealed class TelnetPadHost : IDisposable
    {
        private readonly string _host;
        private readonly ushort _port;
        private readonly int _verbosity;
        private readonly ILogger _log;
        private readonly Dictionary<PadClient, X25Client> _clients = new Dictionary<PadClient, X25Client>();
        private readonly TelnetPadServer _server;
        private readonly string? _nui;

        /// <summary>
        /// Creates a stopped host.
        /// </summary>
        /// <param name="host">XOT peer host each telnet session dials.</param>
        /// <param name="port">XOT peer port.</param>
        /// <param name="verbosity">Log detail passed to each X.25 client.</param>
        /// <param name="log">Logger for the X.25 clients.</param>
        /// <param name="serverLog">Logger for the telnet server.</param>
        /// <param name="nui">
        /// <c>-n</c>: the NUI every session's PAD starts with, as if <c>ID</c> and the NUI had been typed (X.28 5.2); null for none.
        /// Already checked by the caller. Each session has its own PAD, so one session's <c>IDOFF</c> does not touch another's.
        /// </param>
        public TelnetPadHost(string host, ushort port, int verbosity, ILogger log, ILogger serverLog, string? nui = null)
        {
            _nui = nui;
            _host = host;
            _port = port;
            _verbosity = verbosity;
            _log = log;
            _server = new TelnetPadServer(serverLog, CreatePad, ReleasePad);
        }

        /// <summary>
        /// The state of the telnet listener (listening address and port, open sessions, or the failure).
        /// </summary>
        /// <returns>The status snapshot.</returns>
        public TelnetPadStatus GetStatus()
        {
            return _server.GetStatus();
        }

        /// <summary>
        /// Starts listening.
        /// </summary>
        /// <param name="config">Port, bind address and session limit.</param>
        /// <returns>True when the listener is up; otherwise read <see cref="GetStatus"/> for the reason.</returns>
        public bool Start(TelnetPadConfig config)
        {
            return _server.Start(config);
        }

        /// <summary>
        /// Stops listening and ends every session.
        /// </summary>
        public void Stop()
        {
            _server.Stop();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _server.Dispose();
        }

        private PadClient CreatePad(string name)
        {
            Program.OpenPad(_host, _port, _verbosity, _log, out X25Client client, out PadClient pad);
            if (_nui != null)
            {
                pad.SetNui(_nui);
            }

            lock (_clients)
            {
                _clients[pad] = client;
            }

            return pad;
        }

        private void ReleasePad(PadClient pad)
        {
            X25Client? client;
            lock (_clients)
            {
                _clients.Remove(pad, out client);
            }

            pad.Dispose();
            client?.Dispose();
        }
    }
}
