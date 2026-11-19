//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace RetroX25.Host
{
    /// <summary>
    /// The operator console over telnet (<c>--console-port</c>, docs/x25host-format.md "The operator console"): a login
    /// from the <c>[operator.N]</c> sections of the machine list, then the same commands as on stdin. One thread per
    /// connection; <c>quit</c> leaves the connection, never the process.
    /// </summary>
    /// <remarks>
    /// Telnet (RFC 854): IAC (255) starts a command; WILL/WONT/DO/DONT (251-254) take one option octet, SB (250)
    /// runs to IAC SE (240). Those are read past and never answered: the client's defaults (local echo, line at a
    /// time) are what the console wants. For the password the server offers WILL ECHO (RFC 857), so the client stops
    /// echoing, and WONT ECHO after it. A line ends at CR or LF; CR LF and CR NUL count once.
    /// </remarks>
    internal sealed class TelnetConsole : IDisposable
    {
        private const byte Iac = 255;
        private const byte Will = 251;
        private const byte Wont = 252;
        private const byte Sb = 250;
        private const byte Se = 240;
        private const byte EchoOption = 1;

        /// <summary>Wrong logins before the connection is closed.</summary>
        private const int MaxTries = 3;

        /// <summary>The longest line taken.</summary>
        private const int MaxLine = 512;

        private readonly HostRunner _runner;
        private readonly List<MachineList.OperatorLogin> _logins;
        private readonly ILogger _log;
        private readonly TcpListener _listener;
        private Thread? _accept;
        private volatile bool _stopped;

        /// <summary>Makes the console; nothing listens before <see cref="Start"/>.</summary>
        /// <param name="runner">The machines.</param>
        /// <param name="logins">The operator logins; with none, nobody can log in (and the program refuses to start).</param>
        /// <param name="bind">The address to listen on.</param>
        /// <param name="port">The TCP port; 0 picks a free one (see <see cref="Port"/>).</param>
        /// <param name="log">The log.</param>
        public TelnetConsole(HostRunner runner, List<MachineList.OperatorLogin> logins, IPAddress bind, int port, ILogger log)
        {
            _runner = runner;
            _logins = logins;
            _log = log;
            _listener = new TcpListener(bind, port);
        }

        /// <summary>The port listened on, after <see cref="Start"/>.</summary>
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>Starts listening.</summary>
        public void Start()
        {
            _listener.Start();
            _accept = new Thread(AcceptLoop) { IsBackground = true, Name = "x25host-console-accept" };
            _accept.Start();
            _log.LogInformation("x25host: operator console on telnet {End}", _listener.LocalEndpoint);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
            _accept?.Join(2000);
        }

        private void AcceptLoop()
        {
            while (!_stopped)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException) when (_stopped)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Thread t = new Thread(() => Serve(client)) { IsBackground = true, Name = "x25host-console" };
                t.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            string from = client.Client.RemoteEndPoint?.ToString() ?? "?";
            using (client)
            {
                try
                {
                    NetworkStream s = client.GetStream();
                    LineReader reader = new LineReader(s);
                    Write(s, "x25host operator console\r\n");
                    string? user = null;
                    for (int tries = 0; tries < MaxTries && user == null; tries++)
                    {
                        Write(s, "login: ");
                        string? name = reader.ReadLine();
                        if (name == null)
                        {
                            return;
                        }

                        Write(s, "password: ");
                        s.Write(new byte[] { Iac, Will, EchoOption });
                        string? password = reader.ReadLine();
                        s.Write(new byte[] { Iac, Wont, EchoOption });
                        Write(s, "\r\n");
                        if (password == null)
                        {
                            return;
                        }

                        user = Check(name.Trim(), password);
                        if (user == null)
                        {
                            _log.LogWarning("x25host: console login refused for {Name} from {From}", name.Trim(), from);
                            Write(s, "login incorrect\r\n");
                        }
                    }

                    if (user == null)
                    {
                        return;
                    }

                    _log.LogInformation("x25host: operator {User} on the console from {From}", user, from);
                    OperatorConsole console = new OperatorConsole(_runner, overTelnet: true);
                    while (!console.QuitAsked && !_stopped)
                    {
                        Write(s, "x25host> ");
                        string? line = reader.ReadLine();
                        if (line == null)
                        {
                            break;
                        }

                        Write(s, console.Execute(line).Replace("\n", "\r\n", StringComparison.Ordinal));
                    }

                    _log.LogInformation("x25host: operator {User} left the console", user);
                }
                catch (IOException)
                {
                    // The operator's connection went away.
                }
                finally
                {
                    // Every way out (a refused login returns from inside the try).
                    CloseGently(client);
                }
            }
        }

        // A socket closed with bytes it has not read (the LF after the CR that ended the last line) sends a reset
        // (RST), and the client may lose the last text. So: no more sending (FIN), then read what is left until
        // the client closes too, for at most a second.
        private static void CloseGently(TcpClient client)
        {
            try
            {
                client.Client.Shutdown(SocketShutdown.Send);
                client.Client.ReceiveTimeout = 1000;
                byte[] rest = new byte[256];
                while (client.Client.Receive(rest) > 0)
                {
                }
            }
            catch (SocketException)
            {
                // Gone already, or the second ran out: the close below is all that is left.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private string? Check(string name, string password)
        {
            for (int i = 0; i < _logins.Count; i++)
            {
                if (string.Equals(_logins[i].Name, name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(_logins[i].Password, password, StringComparison.Ordinal))
                {
                    return _logins[i].Name;
                }
            }

            return null;
        }

        private static void Write(NetworkStream s, string text)
        {
            s.Write(Encoding.ASCII.GetBytes(text));
        }

        /// <summary>Lines from a telnet stream, with the telnet commands read past (see the class remarks).</summary>
        private sealed class LineReader
        {
            private readonly NetworkStream _s;
            private readonly StringBuilder _line = new StringBuilder();
            private bool _lastWasCr;

            public LineReader(NetworkStream s)
            {
                _s = s;
            }

            public string? ReadLine()
            {
                while (true)
                {
                    int b = _s.ReadByte();
                    if (b < 0)
                    {
                        return null;
                    }

                    if (b == Iac)
                    {
                        SkipCommand();
                        continue;
                    }

                    if ((b == '\n' || b == 0) && _lastWasCr)
                    {
                        // The second half of CR LF or CR NUL.
                        _lastWasCr = false;
                        continue;
                    }

                    _lastWasCr = b == '\r';
                    if (b == '\r' || b == '\n')
                    {
                        string line = _line.ToString();
                        _line.Clear();
                        return line;
                    }

                    if ((b == 8 || b == 127) && _line.Length > 0)
                    {
                        _line.Length--;
                    }
                    else if (b >= 32 && b < 127 && _line.Length < MaxLine)
                    {
                        _line.Append((char)b);
                    }
                }
            }

            private void SkipCommand()
            {
                int cmd = _s.ReadByte();
                if (cmd >= Will && cmd <= 254)
                {
                    _s.ReadByte();
                }
                else if (cmd == Sb)
                {
                    int prev = 0;
                    int b;
                    while ((b = _s.ReadByte()) >= 0 && !(prev == Iac && b == Se))
                    {
                        prev = b;
                    }
                }
            }
        }
    }
}
