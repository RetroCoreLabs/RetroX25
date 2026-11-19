//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.PacketLayer;

namespace RetroX25.Tests.Demo
{
    /// <summary>
    /// The large demo network of <c>src/demo/large-network</c>, started by its own script (<c>large.ps1</c>) from its
    /// own <c>network.ini</c> - the same thing a person starts by hand (README.md there). Seven countries joined by
    /// seven X.75 links, an echo service in each, and the thirteen hosts of <c>src/demo/hosts</c> spread over them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is expected is worked out from <c>network.ini</c> itself (countries, links, hosts), not written into this
    /// file, so the test follows the network when the file changes.
    /// </para>
    /// <para>
    /// The script is run with <c>-BinDir</c> set to this test's output folder: x25switch.exe, x25echo.exe and
    /// x25host.exe are built there by the project references, so the test runs the code of this build and not an older
    /// published copy. The state (generated/, logs/, .pids) stays in the demo folder, because generated/machines.ini
    /// finds the host library by a path relative to that folder; all three are in the folder's .gitignore.
    /// </para>
    /// <para>
    /// The network listens on fixed TCP ports (27000 + 100 * country + 1..3, links from + 10), so the test refuses to
    /// start while a hand-started copy runs (the .pids file is there). After <c>large.ps1 stop</c> every process id the
    /// script recorded is checked to be gone.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    [Category("Demo")]
    public class LargeNetworkTests
    {
        private static readonly byte[] PadCallUserData = { 0x01, 0x00, 0x00, 0x00 };

        private string _folder = string.Empty;
        private string _script = string.Empty;
        private Network _net = null!;
        private bool _started;
        private readonly List<int> _pids = new List<int>();
        private readonly List<DateTime> _pidStarts = new List<DateTime>();

        // One row per call placed, for the report written at the end (demo/large-network/logs/test-report.md):
        // from, to, number dialled, links crossed, ms, result.
        private readonly List<string[]> _rows = new List<string[]>();
        private readonly DateTime _runStarted = DateTime.Now;

        [OneTimeSetUp]
        public void ReadTheNetwork()
        {
            _folder = FindFolder();
            _script = Path.Combine(_folder, "large.ps1");
            _net = Network.Read(Path.Combine(_folder, "network.ini"));

            string[] programs = { "x25switch.exe", "x25echo.exe", "x25host.exe" };
            for (int i = 0; i < programs.Length; i++)
            {
                string exe = Path.Combine(AppContext.BaseDirectory, programs[i]);
                Assert.That(File.Exists(exe), Is.True, exe + " is missing - build the solution (src\\build.ps1) first");
            }

            Assert.That(File.Exists(Path.Combine(_folder, ".pids")), Is.False,
                "the large demo network is already running (demo/large-network/.pids); its ports are the ones this test needs. Run 'large.ps1 stop' first.");
        }

        [OneTimeTearDown]
        public void StopTheNetwork()
        {
            if (!_started)
            {
                return;
            }

            WriteReport();
            string output = RunScript("stop", out int exit);
            TestContext.Progress.WriteLine(output);

            // Every process the script started must be gone. One that is left is stopped here by the id the script
            // recorded, and only when it is still the same process (same start time; Windows reuses ids).
            List<string> left = new List<string>();
            for (int i = 0; i < _pids.Count; i++)
            {
                Process? p = null;
                try
                {
                    p = Process.GetProcessById(_pids[i]);
                }
                catch (ArgumentException)
                {
                    // Gone, as it should be.
                }

                if (p == null)
                {
                    continue;
                }

                using (p)
                {
                    bool same;
                    try
                    {
                        same = !p.HasExited && p.StartTime == _pidStarts[i];
                    }
                    catch (InvalidOperationException)
                    {
                        same = false;
                    }

                    if (same)
                    {
                        left.Add(_pids[i] + " (" + p.ProcessName + ")");
                        p.Kill(entireProcessTree: false);
                        p.WaitForExit(5000);
                    }
                }
            }

            Assert.That(exit, Is.EqualTo(0), "large.ps1 stop failed:\n" + output);
            Assert.That(left, Is.Empty, "large.ps1 stop left these processes running (stopped by the test): " + string.Join(", ", left));
        }

        /// <summary>
        /// <c>large.ps1 generate</c> writes one switch file per country and one host file. Each switch file routes every
        /// other DNIC exactly once, out on a link to a neighbour that is one step closer to that country (the shortest
        /// chain); the host file holds every host with its number, in the order of network.ini.
        /// </summary>
        [Test]
        [Order(1)]
        public void Generate_WritesASwitchPerCountry_RoutesToEveryOtherDnic_AndEveryHost()
        {
            string output = RunScript("generate", out int exit);
            Assert.That(exit, Is.EqualTo(0), "large.ps1 generate failed:\n" + output);
            string gen = Path.Combine(_folder, "generated");

            // The live test below runs one case per calling country, numbered 0..6 by its Range.
            Assert.That(_net.Countries.Count, Is.LessThanOrEqualTo(7), "network.ini has more than 7 countries: widen the Range of FromEachCountry_EveryHostAndEveryEchoAnswers");

            for (int c = 0; c < _net.Countries.Count; c++)
            {
                Country me = _net.Countries[c];
                string file = Path.Combine(gen, me.Name.ToLowerInvariant() + ".ini");
                Assert.That(File.Exists(file), Is.True, "no switch file for " + me.Name);
                Dictionary<string, Dictionary<string, string>> ini = Ini.Read(file);
                int[] dist = _net.Distances(c);

                Assert.That(ini["switch"]["dnic"], Is.EqualTo(me.Dnic), file);

                for (int o = 0; o < _net.Countries.Count; o++)
                {
                    if (o == c)
                    {
                        continue;
                    }

                    Country other = _net.Countries[o];
                    string? port = null;
                    int found = 0;
                    foreach (KeyValuePair<string, Dictionary<string, string>> s in ini)
                    {
                        if (s.Key.StartsWith("route.", StringComparison.Ordinal) && s.Value.TryGetValue("dnic", out string? d) && d == other.Dnic)
                        {
                            found++;
                            port = s.Value["port"];
                        }
                    }

                    Assert.That(found, Is.EqualTo(1), me.Name + ": routes to " + other.Name + " (" + other.Dnic + ")");
                    Dictionary<string, string> trunk = ini["port." + port];
                    Assert.That(trunk["type"], Is.EqualTo("trunk"), me.Name + ": the route to " + other.Name + " is not on a link");
                    int via = _net.IndexOf(trunk["peer_name"]);
                    Assert.That(_net.Linked(c, via), Is.True, me.Name + ": the route to " + other.Name + " leaves on a link to " + trunk["peer_name"] + ", which network.ini does not join to it");
                    Assert.That(_net.Distances(via)[o], Is.EqualTo(dist[o] - 1),
                        me.Name + ": the route to " + other.Name + " goes via " + trunk["peer_name"] + ", which is not on a shortest chain");
                }
            }

            Dictionary<string, Dictionary<string, string>> machines = Ini.Read(Path.Combine(gen, "machines.ini"));
            for (int h = 0; h < _net.Hosts.Count; h++)
            {
                Host host = _net.Hosts[h];
                Assert.That(machines.TryGetValue("machine." + h, out Dictionary<string, string>? m), Is.True, "machines.ini has no [machine." + h + "] for " + host.Hostname);
                Assert.That(m!["type"], Is.EqualTo(host.Type));
                Assert.That(m["hostname"], Is.EqualTo(host.Hostname));
                Assert.That(m["nua"], Is.EqualTo(host.Nua));
                Assert.That(m["switch"], Is.EqualTo("127.0.0.1:" + (27000 + 100 * host.Country + 3)));
            }
        }

        /// <summary>
        /// Every host and every country's echo service, called from one country's PAD line: the national number inside
        /// the country, 0 + the international number abroad. Each call must connect; a host must send something first
        /// (its banner or login prompt); an echo must send a typed line back. All calls are made and every failure is
        /// listed at the end.
        /// </summary>
        [Test]
        [Order(2)]
        public void FromEachCountry_EveryHostAndEveryEchoAnswers([Range(0, 6)] int caller)
        {
            Assume.That(caller, Is.LessThan(_net.Countries.Count));
            EnsureStarted();

            Country from = _net.Countries[caller];
            int[] dist = _net.Distances(caller);
            int padPort = 27000 + 100 * caller + 1;
            List<string> failures = new List<string>();
            Stopwatch all = Stopwatch.StartNew();

            for (int h = 0; h < _net.Hosts.Count; h++)
            {
                Host host = _net.Hosts[h];
                string number = Dial(caller, host.Nua);
                Stopwatch sw = Stopwatch.StartNew();
                string result = CallHost(padPort, number);
                Report(failures, from.Name, host.Hostname, host.Nua, number, dist[host.Country], sw.ElapsedMilliseconds, result);
            }

            for (int c = 0; c < _net.Countries.Count; c++)
            {
                string full = _net.Countries[c].Dnic + "9001";
                string number = Dial(caller, full);
                Stopwatch sw = Stopwatch.StartNew();
                string result = CallEcho(padPort, number);
                Report(failures, from.Name, "echo " + _net.Countries[c].Name, full, number, dist[c], sw.ElapsedMilliseconds, result);
            }

            TestContext.Progress.WriteLine(from.Name + ": " + (_net.Hosts.Count + _net.Countries.Count) + " calls in " + all.ElapsedMilliseconds + " ms");
            Assert.That(failures, Is.Empty, from.Name + ": calls that failed:\n" + string.Join("\n", failures));
        }

        // full: the whole international address, DNIC first, which reaches the host from anywhere (also from the
        // host's own country, for a test that calls itself). number: what the PAD user really dialled.
        private void Report(List<string> failures, string from, string what, string full, string number, int links, long ms, string result)
        {
            string line = from + " -> " + what + " (" + full + ", dialled " + number + ", " + links + " link" + (links == 1 ? "" : "s") + ", " + ms + " ms): " + result;
            TestContext.Progress.WriteLine(line);
            _rows.Add(new[] { from, what, full, number, links.ToString(), ms.ToString(), result });
            if (!result.StartsWith("ok", StringComparison.Ordinal))
            {
                failures.Add(line);
            }
        }

        /// <summary>
        /// Writes demo/large-network/logs/test-report.md (the folder is not in git): a summary, the links crossed
        /// between every pair of countries, and one row per call with what came back. Written before the network is
        /// stopped, so a failing run still leaves its report.
        /// </summary>
        private void WriteReport()
        {
            int failed = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i][5].StartsWith("ok", StringComparison.Ordinal))
                {
                    failed++;
                }
            }

            StringBuilder b = new StringBuilder();
            b.Append("# Large network test report\n\n");
            b.Append("Run started ").Append(_runStarted.ToString("yyyy-MM-dd HH:mm:ss")).Append(", written by ")
                .Append("`tests/RetroX25.Tests/Demo/LargeNetworkTests.cs`.\n\n");
            b.Append("| | |\n|---|---|\n");
            b.Append("| Countries | ").Append(_net.Countries.Count).Append(" |\n");
            b.Append("| Hosts | ").Append(_net.Hosts.Count).Append(" |\n");
            b.Append("| Calls placed | ").Append(_rows.Count).Append(" |\n");
            b.Append("| Calls that worked | ").Append(_rows.Count - failed).Append(" |\n");
            b.Append("| Calls that failed | ").Append(failed).Append(" |\n\n");

            // How many X.75 links a call crosses from the row's country to the column's (the shortest chain).
            b.Append("## Links crossed between countries\n\n| From \\ to |");
            for (int c = 0; c < _net.Countries.Count; c++)
            {
                b.Append(' ').Append(_net.Countries[c].Name).Append(" (").Append(_net.Countries[c].Dnic).Append(") |");
            }

            b.Append("\n|---|");
            for (int c = 0; c < _net.Countries.Count; c++)
            {
                b.Append("---|");
            }

            b.Append('\n');
            for (int r = 0; r < _net.Countries.Count; r++)
            {
                int[] dist = _net.Distances(r);
                b.Append("| ").Append(_net.Countries[r].Name).Append(" |");
                for (int c = 0; c < _net.Countries.Count; c++)
                {
                    b.Append(' ').Append(dist[c]).Append(" |");
                }

                b.Append('\n');
            }

            b.Append("\n## Every call\n\nThe result is what the test saw: for a host its first text, for an echo the line that came back.\n\n");
            b.Append("| From | To | Full address (with DNIC) | Number dialled | Links | ms | Result |\n|---|---|---|---|---|---|---|\n");
            for (int i = 0; i < _rows.Count; i++)
            {
                string[] row = _rows[i];
                b.Append("| ");
                for (int k = 0; k < row.Length; k++)
                {
                    // A pipe or a line end in the host's text would break the table.
                    b.Append(row[k].Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")).Append(" | ");
                }

                b.Append('\n');
            }

            // Every country's switch, as its own console tells it after all the calls: the traffic counters and the
            // state of each port (the PAD line, echo line, host line and the X.75 links).
            b.Append("\n## Switch statistics after the calls\n\nRead from each switch's management console (telnet 9400 + the country's number, admin / admin).\n");
            for (int c = 0; c < _net.Countries.Count; c++)
            {
                Country country = _net.Countries[c];
                b.Append("\n### ").Append(country.Name).Append(" (").Append(country.Dnic).Append(")\n\n```text\n");
                b.Append(SwitchConsole(9400 + c, new[] { "show x25 stats", "show x25 port" }));
                b.Append("\n```\n");
            }

            string logs = Path.Combine(_folder, "logs");
            Directory.CreateDirectory(logs);
            string file = Path.Combine(logs, "test-report.md");
            File.WriteAllText(file, b.ToString());
            TestContext.Progress.WriteLine("report: " + file);
        }

        /// <summary>
        /// Logs in to a switch's management console over telnet and returns what each command printed. Telnet option
        /// bytes (IAC sequences) are dropped. The answer to a step is everything until the console has been quiet for
        /// 700 ms. A switch that cannot be reached gives a line saying so, which the report shows as it is.
        /// </summary>
        private static string SwitchConsole(int port, string[] commands)
        {
            try
            {
                using TcpClient client = new TcpClient();
                if (!client.ConnectAsync("127.0.0.1", port).Wait(5000))
                {
                    return "(the console on port " + port + " did not answer)";
                }

                NetworkStream stream = client.GetStream();
                ConsoleReader reader = new ConsoleReader(stream);
                reader.ReadQuiet(700);
                WriteLine(stream, "admin");
                reader.ReadQuiet(700);
                WriteLine(stream, "admin");
                reader.ReadQuiet(700);
                StringBuilder all = new StringBuilder();
                for (int i = 0; i < commands.Length; i++)
                {
                    WriteLine(stream, commands[i]);
                    all.Append(reader.ReadQuiet(700).TrimEnd()).Append("\n\n");
                }

                return all.ToString().TrimEnd();
            }
            catch (Exception e)
            {
                return "(console on port " + port + " failed: " + e.Message + ")";
            }
        }

        private static void WriteLine(NetworkStream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text + "\r\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Reads a telnet console, dropping IAC sequences (IAC cmd opt, and IAC SB ... IAC SE).
        /// </summary>
        /// <remarks>
        /// A read still waiting when the quiet time runs out is kept and used by the next call. Starting a new read
        /// instead left the old one waiting on the connection: it took the first byte of the next answer and nothing
        /// ever looked at it (the report first showed "how x25 stats" for "show x25 stats" because of that).
        /// </remarks>
        private sealed class ConsoleReader
        {
            private readonly NetworkStream _stream;
            private readonly byte[] _buffer = new byte[4096];
            private Task<int>? _pending;
            private int _state;

            public ConsoleReader(NetworkStream stream)
            {
                _stream = stream;
            }

            // Everything until nothing has arrived for quietMs.
            public string ReadQuiet(int quietMs)
            {
                StringBuilder text = new StringBuilder();
                while (true)
                {
                    _pending ??= _stream.ReadAsync(_buffer, 0, _buffer.Length);
                    if (!_pending.Wait(quietMs))
                    {
                        return text.ToString().Replace("\r", string.Empty);
                    }

                    int n = _pending.Result;
                    _pending = null;
                    if (n <= 0)
                    {
                        return text.ToString().Replace("\r", string.Empty);
                    }

                    Take(n, text);
                }
            }

            private void Take(int n, StringBuilder text)
            {
                int state = _state;
                for (int i = 0; i < n; i++)
                {
                    byte x = _buffer[i];
                    if (state == 0)
                    {
                        if (x == 0xFF)
                        {
                            state = 1;
                        }
                        else if (x != 0)
                        {
                            text.Append((char)x);
                        }
                    }
                    else if (state == 1)
                    {
                        // IAC IAC is a data 0xFF; SB starts a sub-negotiation; WILL/WONT/DO/DONT take one option byte.
                        state = x == 0xFF ? 0 : x == 0xFA ? 3 : (x >= 0xFB && x <= 0xFE) ? 2 : 0;
                    }
                    else if (state == 2)
                    {
                        state = 0;
                    }
                    else if (state == 3)
                    {
                        state = x == 0xFF ? 4 : 3;
                    }
                    else
                    {
                        state = x == 0xF0 ? 0 : 3;
                    }
                }

                // A telnet command may be split over two reads.
                _state = state;
            }
        }

        // The number a PAD user dials: the national number inside the caller's country, 0 + the whole number abroad.
        private string Dial(int caller, string international)
        {
            string dnic = _net.Countries[caller].Dnic;
            return international.StartsWith(dnic, StringComparison.Ordinal) ? international.Substring(dnic.Length) : "0" + international;
        }

        private void EnsureStarted()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            string output = RunScript("start", out int exit);

            // The ids the script recorded, with their start times, so teardown can tell they are still the same processes.
            string pidFile = Path.Combine(_folder, ".pids");
            if (File.Exists(pidFile))
            {
                string[] lines = File.ReadAllLines(pidFile);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!int.TryParse(lines[i].Trim(), out int id))
                    {
                        continue;
                    }

                    try
                    {
                        using Process p = Process.GetProcessById(id);
                        _pidStarts.Add(p.StartTime);
                        _pids.Add(id);
                    }
                    catch (ArgumentException)
                    {
                        // Already exited: nothing to stop later. The start check below says why.
                    }
                }
            }

            Assert.That(exit, Is.EqualTo(0), "large.ps1 start failed:\n" + output);
            Assert.That(output, Does.Contain("Running."), "large.ps1 start did not start the network:\n" + output);
        }

        // ---- one PAD call, raw XOT (RFC 1613) to a country's PAD line, as x25pad does ----

        private static string CallHost(int padPort, string number)
        {
            using TcpClient tcp = new TcpClient { NoDelay = true };
            tcp.Connect("127.0.0.1", padPort);
            NetworkStream s = tcp.GetStream();
            s.ReadTimeout = 15000;
            string? refused = Connect(s, number);
            if (refused != null)
            {
                return refused;
            }

            // The first screen text: the banner or the login prompt. Q-bit packets are X.29 messages to the PAD.
            byte recv = 0;
            string text = string.Empty;
            long deadline = Environment.TickCount64 + 15000;
            while (text.Length == 0)
            {
                if (Environment.TickCount64 > deadline)
                {
                    return "connected, but no text in 15 s";
                }

                X25PacketInfo info = Read(s);
                if (info.PacketType == X25PacketType.ClearRequest)
                {
                    return "connected, then cleared before any text: cause 0x" + info.ClearingCause.ToString("X2") + " diagnostic " + info.DiagnosticCode;
                }

                if (info.PacketType != X25PacketType.Data)
                {
                    continue;
                }

                recv = (byte)((info.PS + 1) & 7);
                SendXot(s, X25PacketBuilder.CreateRR(1, recv));
                if (!info.QBit && info.UserData.Length > 0)
                {
                    text = Encoding.ASCII.GetString(info.UserData);
                }
            }

            string cleared = Clear(s);
            string first = text.Replace("\r", "\\r").Replace("\n", "\\n");
            return (cleared.Length == 0 ? "ok" : cleared) + ", first text \"" + (first.Length > 60 ? first.Substring(0, 60) + "..." : first) + "\"";
        }

        private static string CallEcho(int padPort, string number)
        {
            using TcpClient tcp = new TcpClient { NoDelay = true };
            tcp.Connect("127.0.0.1", padPort);
            NetworkStream s = tcp.GetStream();
            s.ReadTimeout = 15000;
            string? refused = Connect(s, number);
            if (refused != null)
            {
                return refused;
            }

            string line = "ping " + number;
            SendXot(s, X25PacketBuilder.CreateDataPacket(1, 0, 0, Encoding.ASCII.GetBytes(line + "\r")));
            StringBuilder back = new StringBuilder();
            long deadline = Environment.TickCount64 + 15000;
            while (back.ToString().IndexOf(line, StringComparison.Ordinal) < 0)
            {
                if (Environment.TickCount64 > deadline)
                {
                    return "connected, but the line did not come back in 15 s; got \"" + back + "\"";
                }

                X25PacketInfo info = Read(s);
                if (info.PacketType == X25PacketType.ClearRequest)
                {
                    return "connected, then cleared before the echo: cause 0x" + info.ClearingCause.ToString("X2") + " diagnostic " + info.DiagnosticCode;
                }

                if (info.PacketType == X25PacketType.Data)
                {
                    SendXot(s, X25PacketBuilder.CreateRR(1, (byte)((info.PS + 1) & 7)));
                    if (!info.QBit)
                    {
                        back.Append(Encoding.ASCII.GetString(info.UserData));
                    }
                }
            }

            string cleared = Clear(s);
            return cleared.Length == 0 ? "ok" : cleared;
        }

        // Sends the Call Request (LCI 1, PAD call user data) and waits for Call Connected or a clear. Returns null when
        // connected, else what happened.
        private static string? Connect(NetworkStream s, string number)
        {
            SendXot(s, X25PacketBuilder.CreateCallRequest(1, number, string.Empty, null, PadCallUserData));
            while (true)
            {
                X25PacketInfo info = Read(s);
                if (info.PacketType == X25PacketType.CallAccepted)
                {
                    return null;
                }

                if (info.PacketType == X25PacketType.ClearRequest)
                {
                    SendXot(s, X25PacketBuilder.CreateClearConfirmation(1));
                    return "not connected: cleared, cause 0x" + info.ClearingCause.ToString("X2") + " diagnostic " + info.DiagnosticCode;
                }
            }
        }

        // Clears the call and waits for the confirmation (or the far end's own clear, which crossed ours). Returns an
        // empty string when that worked.
        private static string Clear(NetworkStream s)
        {
            SendXot(s, X25PacketBuilder.CreateClearRequest(1, 0x00, 0x00));
            long deadline = Environment.TickCount64 + 10000;
            while (Environment.TickCount64 < deadline)
            {
                X25PacketInfo info = Read(s);
                if (info.PacketType == X25PacketType.ClearConfirmation || info.PacketType == X25PacketType.ClearRequest)
                {
                    return string.Empty;
                }
            }

            return "no clear confirmation in 10 s";
        }

        // XOT (RFC 1613): a 2-octet version (0) and a 2-octet length before each packet.
        private static void SendXot(NetworkStream stream, byte[] packet)
        {
            byte[] frame = new byte[4 + packet.Length];
            frame[2] = (byte)(packet.Length >> 8);
            frame[3] = (byte)packet.Length;
            packet.CopyTo(frame, 4);
            stream.Write(frame, 0, frame.Length);
        }

        private static X25PacketInfo Read(NetworkStream stream)
        {
            byte[] header = ReadExactly(stream, 4);
            return X25PacketParser.ParsePacket(ReadExactly(stream, (header[2] << 8) | header[3]));
        }

        private static byte[] ReadExactly(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n == 0)
                {
                    throw new IOException("the switch closed the connection");
                }

                read += n;
            }

            return buffer;
        }

        // ---- the script ----

        private static string FindFolder()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "demo", "large-network");
                if (File.Exists(Path.Combine(candidate, "large.ps1")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("demo/large-network/large.ps1 not found above " + AppContext.BaseDirectory);
        }

        // Runs large.ps1 and returns what it printed. The output goes to a file, not a pipe: the programs the script
        // starts inherit its standard handles, so a pipe would stay open while the network runs (as in
        // X75CountriesScriptTests). The file is in the temporary folder and deleted after reading.
        private string RunScript(string command, out int exitCode)
        {
            string outFile = Path.Combine(Path.GetTempPath(), "large-network-" + command + "-" + Guid.NewGuid().ToString("N") + ".txt");
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("& '" + _script + "' " + command + " -BinDir '" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
                + "' *> '" + outFile + "'; exit $LASTEXITCODE");

            using Process process = Process.Start(info)!;
            int pid = process.Id;
            string output;
            if (!process.WaitForExit(120000))
            {
                // Killed by the id recorded at start, never by name.
                Process.GetProcessById(pid).Kill(entireProcessTree: false);
                process.WaitForExit(5000);
                exitCode = -1;
                output = "large.ps1 " + command + " did not finish in 120 s\n";
            }
            else
            {
                exitCode = process.ExitCode;
                output = string.Empty;
            }

            if (File.Exists(outFile))
            {
                output += File.ReadAllText(outFile);
                File.Delete(outFile);
            }

            return output;
        }

        // ---- network.ini, read the way large.ps1 reads it ----

        private sealed class Country
        {
            public string Name = string.Empty;
            public string Dnic = string.Empty;
            public int Hosts;
        }

        private sealed class Host
        {
            public int Country;
            public string Type = string.Empty;
            public string Hostname = string.Empty;
            public string Nua = string.Empty;
        }

        private sealed class Network
        {
            public readonly List<Country> Countries = new List<Country>();
            public readonly List<Host> Hosts = new List<Host>();
            public readonly List<(int A, int B)> Links = new List<(int A, int B)>();

            public static Network Read(string file)
            {
                Dictionary<string, Dictionary<string, string>> ini = Ini.Read(file);
                Network n = new Network();
                foreach (KeyValuePair<string, Dictionary<string, string>> s in ini)
                {
                    if (s.Key.StartsWith("country.", StringComparison.Ordinal))
                    {
                        n.Countries.Add(new Country { Name = s.Value["name"], Dnic = s.Value["dnic"] });
                    }
                }

                foreach (KeyValuePair<string, Dictionary<string, string>> s in ini)
                {
                    if (s.Key.StartsWith("link.", StringComparison.Ordinal))
                    {
                        n.Links.Add((n.IndexOf(s.Value["a"]), n.IndexOf(s.Value["b"])));
                    }
                    else if (s.Key.StartsWith("host.", StringComparison.Ordinal))
                    {
                        int c = n.IndexOf(s.Value["country"]);
                        n.Countries[c].Hosts++;
                        n.Hosts.Add(new Host
                        {
                            Country = c,
                            Type = s.Value["type"],
                            Hostname = s.Value["hostname"],
                            Nua = n.Countries[c].Dnic + "1" + n.Countries[c].Hosts.ToString("000"),
                        });
                    }
                }

                return n;
            }

            public int IndexOf(string name)
            {
                for (int i = 0; i < Countries.Count; i++)
                {
                    if (Countries[i].Name == name)
                    {
                        return i;
                    }
                }

                throw new InvalidDataException("no country called " + name + " in network.ini");
            }

            public bool Linked(int a, int b)
            {
                for (int i = 0; i < Links.Count; i++)
                {
                    if ((Links[i].A == a && Links[i].B == b) || (Links[i].A == b && Links[i].B == a))
                    {
                        return true;
                    }
                }

                return false;
            }

            // The number of links crossed from one country to each other one (breadth-first); -1 when unreachable.
            public int[] Distances(int from)
            {
                int[] dist = new int[Countries.Count];
                Array.Fill(dist, -1);
                dist[from] = 0;
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(from);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    for (int i = 0; i < Links.Count; i++)
                    {
                        int other = Links[i].A == c ? Links[i].B : Links[i].B == c ? Links[i].A : -1;
                        if (other >= 0 && dist[other] < 0)
                        {
                            dist[other] = dist[c] + 1;
                            queue.Enqueue(other);
                        }
                    }
                }

                return dist;
            }
        }

        // A plain ini reader: [section], key = value, '#' starts a comment. Sections keep their file order.
        private static class Ini
        {
            public static Dictionary<string, Dictionary<string, string>> Read(string file)
            {
                Dictionary<string, Dictionary<string, string>> sections = new Dictionary<string, Dictionary<string, string>>();
                Dictionary<string, string>? current = null;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int hash = line.IndexOf('#');
                    if (hash >= 0)
                    {
                        line = line.Substring(0, hash);
                    }

                    line = line.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        current = new Dictionary<string, string>();
                        sections[line.Substring(1, line.Length - 2)] = current;
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (current == null || eq < 0)
                    {
                        throw new InvalidDataException(file + ":" + (i + 1) + ": not 'key = value' inside a [section]");
                    }

                    current[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }

                return sections;
            }
        }
    }
}
