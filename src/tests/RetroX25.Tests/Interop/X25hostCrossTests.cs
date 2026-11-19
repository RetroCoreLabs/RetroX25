//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroX25.Host;

namespace RetroX25.Tests
{
    /// <summary>
    /// The C x25host (x25emu's tools/x25host) and the C# x25host (RetroX25.Host, in this process) run the same host
    /// folder, every folder of demo/hosts, and one scripted caller session is played against each. What the caller is
    /// sent at every step is compared byte for byte: the text, the X.29 (Q-bit) data, and how the call ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each test starts its own network, as <see cref="AltosChatCrossTests"/> does: an x25emu switch in WSL with three
    /// XOT access ports on TCP ports chosen at run time; the C# host is the XOT client of port 0 answering
    /// <see cref="CSharpAddress"/>, x25host is the client of port 1 answering <see cref="CAddress"/>, and the caller is
    /// on port 2. Every program is started with its process number recorded and is stopped by that number.
    /// </para>
    /// <para>
    /// The C# x25host is the authority (x25emu tools/README-x25host.md says so). A difference keeps the test red; the
    /// only thing masked is the digits of a time or a date (the two programs run a few seconds apart), see
    /// <see cref="MaskTime"/>.
    /// </para>
    /// <para>
    /// The script is made from the folder's host.ini: a wrong login first, then a real one with a [user.N] that has a
    /// password, then every [command.N] (or every menu key, into every submenu and back), a wrong command, and the
    /// logout. A step ends when the host has been quiet for <see cref="QuietMs"/> after its first byte: both hosts
    /// answer at once on this machine, so a step's output never spreads into the next.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    [NonParallelizable]
    public class X25hostCrossTests
    {
        private const string CSharpAddress = "26246001";
        private const string CAddress = "26246002";
        private const string Calling = "26245101";
        private const int Wait = 20000;

        /// <summary>How long the host must be silent before a step counts as answered.</summary>
        private const int QuietMs = 600;

        /// <summary>How long a step may take to give its first byte.</summary>
        private const int FirstByteMs = 5000;

        // X.29 (12/97) 4.2.1: the PAD protocol identifier.
        private static readonly byte[] PadCud = { 0x01, 0x00, 0x00, 0x00 };

        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        [TearDown]
        public void TearDown()
        {
            // Last made, first gone: the caller, then the hosts, then the switch and the folder in WSL.
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposables[i].Dispose();
                }
                catch (Exception e)
                {
                    TestContext.Progress.WriteLine("stopping " + _disposables[i].GetType().Name + " failed: " + e.Message);
                }
            }

            _disposables.Clear();
        }

        /// <summary>Every folder in demo/hosts, by name.</summary>
        public static IEnumerable<string> Folders()
        {
            string[] dirs = Directory.GetDirectories(HostsFolder());
            Array.Sort(dirs, StringComparer.Ordinal);
            string[] names = new string[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                names[i] = Path.GetFileName(dirs[i]);
            }

            return names;
        }

        private static string HostsFolder()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "demo", "hosts");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("demo/hosts was not found above " + AppContext.BaseDirectory);
        }

        /// <summary>
        /// One host folder, the same script against the C# and the C x25host: every step's text and Q-bit data, the
        /// invitation to clear after the logout, and whether the host cleared the call itself, are the same.
        /// </summary>
        [TestCaseSource(nameof(Folders))]
        public void TheSameSession_GivesTheSameBytes_FromBothHosts(string folder)
        {
            string windowsFolder = Path.Combine(HostsFolder(), folder);
            Ini ini = Ini.Load(Path.Combine(windowsFolder, "host.ini"));
            List<string> script = MakeScript(ini);
            Net net = StartNet(windowsFolder);

            Session cs = RunScript(net, CSharpAddress, script);
            Session c = RunScript(net, CAddress, script);

            StringBuilder differences = new StringBuilder();
            Assert.That(c.Steps.Count, Is.EqualTo(cs.Steps.Count));
            for (int i = 0; i < cs.Steps.Count; i++)
            {
                string step = "step " + i + " (" + (i == 0 ? "the call" : "types " + Show(script[i - 1])) + ")";
                Compare(step + " text", cs.Steps[i].Data, c.Steps[i].Data, differences);
                Compare(step + " Q-bit data", cs.Steps[i].Q, c.Steps[i].Q, differences);
            }

            if (cs.Invited != c.Invited)
            {
                differences.Append("after the logout, the invitation to clear (Q-bit 01) ").Append(cs.Invited ? "came" : "did not come")
                    .Append(" from the C# host and ").Append(c.Invited ? "came" : "did not come").Append(" from the C host\n");
            }

            if (cs.ClearedByHost != c.ClearedByHost)
            {
                differences.Append("after the logout, the C# host ").Append(cs.ClearedByHost ? "cleared" : "did not clear")
                    .Append(" the call itself and the C host ").Append(c.ClearedByHost ? "cleared" : "did not clear").Append(" it\n");
            }

            for (int i = 0; i < cs.Steps.Count; i++)
            {
                TestContext.Progress.WriteLine(folder + " step " + i + " " + (i == 0 ? "(call)" : Show(script[i - 1])) + "\n  C#: "
                    + Show(Encoding.Latin1.GetString(cs.Steps[i].Data)) + Q(cs.Steps[i].Q) + "\n  C:  " + Show(Encoding.Latin1.GetString(c.Steps[i].Data)) + Q(c.Steps[i].Q));
            }

            TestContext.Progress.WriteLine(folder + ": " + cs.Steps.Count + " steps; invitation C# " + cs.Invited + ", C " + c.Invited
                + "; cleared by the host C# " + cs.ClearedByHost + ", C " + c.ClearedByHost);
            Assert.That(differences.ToString(), Is.Empty, folder + ": the two x25hosts differ:\n" + differences);
        }

        private static string Q(byte[] q)
        {
            if (q.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder b = new StringBuilder("  [Q:");
            for (int i = 0; i < q.Length; i++)
            {
                b.Append(' ').Append(q[i].ToString("X2"));
            }

            return b.Append(']').ToString();
        }

        // ------------------------------------------------------------------ the script

        /// <summary>
        /// The lines the caller types, each without its CR, made from host.ini alone so that both runs type the same.
        /// </summary>
        private static List<string> MakeScript(Ini ini)
        {
            List<string> lines = new List<string>();
            Dictionary<string, string>? login = ini.Section("login");
            string? then = login != null ? login.GetValueOrDefault("then") : null;
            Dictionary<string, string>? host = ini.Section("host");
            string? start = host != null ? host.GetValueOrDefault("start") : null;

            if (login != null)
            {
                bool upper = login.GetValueOrDefault("case") == "upper";
                string nobody = upper ? "NOBODY" : "nobody";
                string wrong = upper ? "WRONG" : "wrong";
                Dictionary<string, string> user = PickUser(ini);
                string name = user.GetValueOrDefault("name") ?? string.Empty;
                string password = user.GetValueOrDefault("password") ?? string.Empty;
                string? command = login.GetValueOrDefault("command");
                if (command != null)
                {
                    // A login command (TOPS-20 LOGIN, TSO LOGON): a word that is not it, then the command with a user
                    // that does not exist and a wrong password, then the real one.
                    string word = command.Split(',')[0].Trim();
                    lines.Add(upper ? "HELLO" : "hello");
                    lines.Add(word + " " + nobody);
                    lines.Add(wrong);
                    lines.Add(word + " " + name);
                    lines.Add(password);
                }
                else
                {
                    lines.Add(nobody);
                    lines.Add(wrong);
                    lines.Add(name);
                    lines.Add(password);
                }
            }

            string? firstMenu = null;
            if (then != null && then.StartsWith("menu.", StringComparison.Ordinal))
            {
                firstMenu = then.Substring(5);
            }
            else if (start != null && start.StartsWith("menu.", StringComparison.Ordinal))
            {
                firstMenu = start.Substring(5);
            }

            if (firstMenu != null)
            {
                List<string> visited = new List<string>();
                string? logoutKey = WalkMenu(ini, firstMenu, null, visited, lines);
                Assert.That(logoutKey, Is.Not.Null, "menu." + firstMenu + " has no logout item");
                lines.Add(logoutKey!);
                return lines;
            }

            WalkShell(ini, lines);
            return lines;
        }

        /// <summary>The first [user.N] with a password (a login that must be typed right), else [user.0].</summary>
        private static Dictionary<string, string> PickUser(Ini ini)
        {
            Dictionary<string, string>? first = null;
            for (int i = 0; i < ini.Order.Count; i++)
            {
                if (!ini.Order[i].StartsWith("user.", StringComparison.Ordinal))
                {
                    continue;
                }

                Dictionary<string, string> u = ini.Sections[ini.Order[i]];
                first ??= u;
                if ((u.GetValueOrDefault("password") ?? string.Empty).Length > 0)
                {
                    return u;
                }
            }

            Assert.That(first, Is.Not.Null, "host.ini has [login] but no [user.N]");
            return first!;
        }

        /// <summary>
        /// Every item of a menu: the texts first, then a key it does not have, then each submenu (all of its items, then
        /// its way back). Returns the menu's logout key, if it has one.
        /// </summary>
        private static string? WalkMenu(Ini ini, string menu, string? parent, List<string> visited, List<string> lines)
        {
            visited.Add(menu);
            List<string[]> items = MenuItems(ini, menu);
            string? logoutKey = null;
            for (int i = 0; i < items.Count; i++)
            {
                string action = items[i][2];
                if (action == "logout")
                {
                    logoutKey = items[i][0];
                }
                else if (!action.StartsWith("menu.", StringComparison.Ordinal))
                {
                    lines.Add(items[i][0]);
                }
            }

            lines.Add("XYZZY");
            for (int i = 0; i < items.Count; i++)
            {
                string action = items[i][2];
                if (!action.StartsWith("menu.", StringComparison.Ordinal))
                {
                    continue;
                }

                string sub = action.Substring(5);
                if (visited.Contains(sub))
                {
                    continue;
                }

                lines.Add(items[i][0]);
                WalkMenu(ini, sub, menu, visited, lines);

                // Back to this menu by the submenu's own item for it.
                List<string[]> subItems = MenuItems(ini, sub);
                string? back = null;
                for (int k = 0; k < subItems.Count; k++)
                {
                    if (subItems[k][2] == "menu." + menu)
                    {
                        back = subItems[k][0];
                    }
                }

                Assert.That(back, Is.Not.Null, "menu." + sub + " has no way back to menu." + menu);
                lines.Add(back!);
            }

            return logoutKey;
        }

        /// <summary>The items of [menu.name] in order, each as { key, label, action }.</summary>
        private static List<string[]> MenuItems(Ini ini, string menu)
        {
            Dictionary<string, string> m = ini.Sections["menu." + menu];
            List<string[]> items = new List<string[]>();
            for (int n = 0; n < 100; n++)
            {
                if (!m.TryGetValue("item." + n, out string? item))
                {
                    continue;
                }

                string[] parts = item.Split('|');
                Assert.That(parts.Length, Is.EqualTo(3), "menu." + menu + " item." + n + " is not key | label | action");
                items.Add(new[] { parts[0].Trim(), parts[1].Trim(), parts[2].Trim() });
            }

            return items;
        }

        /// <summary>
        /// Every [command.N] with a word it needs (a file for cat, a folder for cd), a word the shell does not know, a
        /// file that is not there, and the logout command last.
        /// </summary>
        private static void WalkShell(Ini ini, List<string> lines)
        {
            Dictionary<string, string>? shell = ini.Section("shell");
            Assert.That(shell, Is.Not.Null, "host.ini has neither a menu nor a [shell]");
            string shellCase = shell!.GetValueOrDefault("case") ?? "upper";
            bool upper = shellCase == "upper";
            string? file = null;
            string? folder = null;
            string user = PickUserName(ini);
            for (int i = 0; i < ini.Order.Count; i++)
            {
                if (!ini.Order[i].StartsWith("file.", StringComparison.Ordinal))
                {
                    continue;
                }

                Dictionary<string, string> f = ini.Sections[ini.Order[i]];
                if (f.ContainsKey("content"))
                {
                    file ??= f["path"];
                }
                else
                {
                    folder ??= f["path"];
                }
            }

            string? logout = null;
            string? cat = null;
            for (int n = 0; n < 100; n++)
            {
                if (!ini.Sections.TryGetValue("command." + n, out Dictionary<string, string>? command))
                {
                    continue;
                }

                string name = command["name"];
                string doWhat = command.GetValueOrDefault("do") ?? string.Empty;
                switch (doWhat)
                {
                    case "logout":
                        logout ??= name;
                        break;
                    case "cat":
                        cat ??= name;
                        lines.Add(file != null ? name + " " + file : name);
                        break;
                    case "cd":
                        lines.Add(folder != null ? name + " " + folder : name);
                        break;
                    case "echo":
                        lines.Add(name + (upper ? " HELLO WORLD" : " hello world"));
                        break;
                    case "send":
                        lines.Add(name + " " + user + (upper ? " HELLO" : " hello"));
                        break;
                    case "wall":
                        lines.Add(name + (upper ? " HELLO ALL" : " hello all"));
                        break;
                    case "finger":
                        lines.Add(name + " " + user);
                        break;
                    default:
                        lines.Add(name);
                        break;
                }
            }

            lines.Add(upper ? "FROBNICATE" : "frobnicate");
            if (cat != null)
            {
                lines.Add(cat + (upper ? " NOFILE" : " nofile"));
            }

            Assert.That(logout, Is.Not.Null, "the shell has no do = logout command");
            lines.Add(logout!);
        }

        private static string PickUserName(Ini ini)
        {
            return ini.Section("login") != null ? PickUser(ini).GetValueOrDefault("name") ?? string.Empty : string.Empty;
        }

        // ------------------------------------------------------------------ one run

        private sealed class Step
        {
            public byte[] Data = Array.Empty<byte>();
            public byte[] Q = Array.Empty<byte>();
        }

        private sealed class Session
        {
            public List<Step> Steps = new List<Step>();
            public bool Invited;
            public bool ClearedByHost;
        }

        private Session RunScript(Net net, string address, List<string> script)
        {
            RawCaller caller = new RawCaller(net.Host, net.CallerPort, Calling);
            _disposables.Add(caller);
            Session session = new Session();

            // The host's line may still be coming up: a call refused before it is there is tried again.
            bool connected = false;
            long end = Environment.TickCount64 + Wait;
            while (!connected && Environment.TickCount64 < end)
            {
                connected = caller.Call(address);
                if (!connected)
                {
                    Thread.Sleep(500);
                }
            }

            Assert.That(connected, Is.True, "could not call " + address + ": " + caller.ClearCause);
            session.Steps.Add(caller.TakeStep());
            for (int i = 0; i < script.Count; i++)
            {
                caller.TypeLatin1(script[i] + "\r");
                session.Steps.Add(caller.TakeStep());
            }

            // The last line was the logout: a PAD call ends with the X.29 invitation to clear (Q bit, 01), after which a
            // PAD clears; the host clears by itself only 10 s later.
            session.Invited = caller.WaitForInvitation(FirstByteMs);
            session.ClearedByHost = caller.Cleared;
            caller.Hangup();
            return session;
        }

        private static void Compare(string what, byte[] cs, byte[] c, StringBuilder differences)
        {
            string left = MaskTime(Show(Encoding.Latin1.GetString(cs)));
            string right = MaskTime(Show(Encoding.Latin1.GetString(c)));
            if (left != right)
            {
                differences.Append(what).Append(":\n  C#: ").Append(Show(Encoding.Latin1.GetString(cs)))
                    .Append("\n  C:  ").Append(Show(Encoding.Latin1.GetString(c))).Append('\n');
            }
        }

        /// <summary>
        /// The digits of a time or a date become 9: a word made of digits, letters and the marks <c>: - / .</c> that holds
        /// a digit and a <c>:</c> (09:48:40) or a <c>-</c> between digits and letters or digits (19-NOV-2026, 2026-11-19).
        /// Every other digit is compared as it is.
        /// </summary>
        private static string MaskTime(string text)
        {
            StringBuilder b = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                int from = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == ':' || text[i] == '-' || text[i] == '/' || text[i] == '.'))
                {
                    i++;
                }

                if (i == from)
                {
                    b.Append(text[i]);
                    i++;
                    continue;
                }

                bool digit = false;
                bool colon = false;
                bool dateDash = false;
                for (int k = from; k < i; k++)
                {
                    if (char.IsAsciiDigit(text[k]))
                    {
                        digit = true;
                    }
                    else if (text[k] == ':')
                    {
                        colon = true;
                    }
                    else if (text[k] == '-' && k > from && k + 1 < i && char.IsAsciiLetterOrDigit(text[k - 1]) && char.IsAsciiLetterOrDigit(text[k + 1])
                        && (char.IsAsciiDigit(text[k - 1]) || char.IsAsciiDigit(text[k + 1])))
                    {
                        dateDash = true;
                    }
                }

                bool mask = digit && (colon || dateDash);
                for (int k = from; k < i; k++)
                {
                    b.Append(mask && char.IsAsciiDigit(text[k]) ? '9' : text[k]);
                }
            }

            return b.ToString();
        }

        /// <summary>Text with every control character and every byte over 126 written as &lt;XX&gt;.</summary>
        private static string Show(string text)
        {
            StringBuilder b = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch < 0x20 || ch > 0x7E)
                {
                    b.Append('<').Append(((int)ch).ToString("X2")).Append('>');
                }
                else
                {
                    b.Append(ch);
                }
            }

            return b.ToString();
        }

        // ------------------------------------------------------------------ host.ini, read for the script only

        /// <summary>
        /// Just enough of the host.ini format (docs/x25host-format.md) to make the script: sections, key = value, a
        /// value in double quotes taken without them, # lines skipped.
        /// </summary>
        private sealed class Ini
        {
            public readonly Dictionary<string, Dictionary<string, string>> Sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            public readonly List<string> Order = new List<string>();

            public Dictionary<string, string>? Section(string name)
            {
                return Sections.TryGetValue(name, out Dictionary<string, string>? s) ? s : null;
            }

            public static Ini Load(string path)
            {
                Ini ini = new Ini();
                string[] lines = File.ReadAllLines(path);
                Dictionary<string, string>? current = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }

                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        string name = line.Substring(1, line.Length - 2).Trim();
                        current = new Dictionary<string, string>(StringComparer.Ordinal);
                        ini.Sections[name] = current;
                        ini.Order.Add(name);
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (current == null || eq < 0)
                    {
                        continue;
                    }

                    string value = line.Substring(eq + 1).Trim();
                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    {
                        value = value.Substring(1, value.Length - 2);
                    }

                    current[line.Substring(0, eq).Trim()] = value;
                }

                return ini;
            }
        }

        // ------------------------------------------------------------------ the network

        /// <summary>The switch, both hosts and the folder in WSL that holds the switch's configuration and the host folder.</summary>
        private sealed class Net : IDisposable
        {
            public string Host = string.Empty;
            public int CallerPort;
            public string Folder = string.Empty;
            public X25emuEnvironment.LinuxProcess? Switch;
            public X25emuEnvironment.LinuxProcess? CHost;
            public HostRunner? CSharpHost;

            public void Dispose()
            {
                CSharpHost?.Dispose();
                CHost?.Dispose();
                Switch?.Dispose();
                if (Folder.Length > 0)
                {
                    X25emuEnvironment.RunWsl("rm -rf '" + Folder + "'", 10000, out _);
                }
            }
        }

        private Net StartNet(string windowsFolder)
        {
            if (string.IsNullOrWhiteSpace(X25emuEnvironment.Tools))
            {
                Assert.Ignore("Set X25EMU_TOOLS (the WSL folder of x25emu's built programs: x25switch, x25host) to run this test.");
            }

            string[] programs = { "x25switch", "x25host" };
            for (int i = 0; i < programs.Length; i++)
            {
                X25emuEnvironment.RunWsl("test -x '" + X25emuEnvironment.ToolPath(programs[i]) + "'", 15000, out int found);
                Assert.That(found, Is.EqualTo(0), "x25emu is not built: " + X25emuEnvironment.ToolPath(programs[i]) + " is missing. Run make in the x25emu checkout.");
            }

            Net net = new Net();
            _disposables.Add(net);
            net.Host = X25emuEnvironment.WslHost ?? string.Empty;
            if (string.IsNullOrWhiteSpace(net.Host))
            {
                string[] words = X25emuEnvironment.RunWsl("hostname -I", 15000, out _).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                Assert.That(words.Length, Is.GreaterThan(0), "WSL gave no address");
                net.Host = words[0];
            }

            // Three ports nothing uses now, chosen by WSL itself (bind to 0, read, let go).
            string ports = X25emuEnvironment.RunWsl(
                "python3 -c 'import socket\nss=[socket.socket() for i in range(3)]\nfor s in ss: s.bind((\"\",0))\nprint(\" \".join(str(s.getsockname()[1]) for s in ss))'",
                15000, out int portExit);
            string[] p = ports.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.That(portExit == 0 && p.Length == 3, Is.True, "could not choose ports in WSL: " + ports);
            int csPort = int.Parse(p[0]);
            int cPort = int.Parse(p[1]);
            net.CallerPort = int.Parse(p[2]);

            // The folder in WSL: the switch configuration and a copy of the host folder, which the C program reads.
            net.Folder = "/tmp/x25host-cross-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string source = X25emuEnvironment.RunWsl("wslpath -a '" + windowsFolder.Replace("'", "'\\''") + "'", 15000, out int converted).Trim();
            Assert.That(converted, Is.EqualTo(0), "wslpath could not convert " + windowsFolder + ": " + source);
            string hostCopy = net.Folder + "/" + Path.GetFileName(windowsFolder);
            string config =
                "[switch]\ndnic = 2624\nown_dnic = 2624\nmgmt_port = 0\nname = HostCross\n\n" +
                "[port.0]\ntype = access\nmode = dce\ntransport = xot\nport = " + csPort + "\nlci_base = 1\nlci_count = 32\n\n" +
                "[port.1]\ntype = access\nmode = dce\ntransport = xot\nport = " + cPort + "\nlci_base = 1\nlci_count = 32\n\n" +
                "[port.2]\ntype = access\nmode = dce\ntransport = xot\nport = " + net.CallerPort + "\nlci_base = 1\nlci_count = 32\n\n" +
                "[route.0]\ntype = prefix\naddress = " + CSharpAddress + "\nprefix_len = 8\nport = 0\n\n" +
                "[route.1]\ntype = prefix\naddress = " + CAddress + "\nprefix_len = 8\nport = 1\n";
            X25emuEnvironment.RunWsl("mkdir -p '" + net.Folder + "' && cp -r '" + source + "' '" + hostCopy + "' && cat > '" + net.Folder + "/switch.conf' <<'EOF'\n" + config + "EOF\n",
                15000, out int wrote);
            Assert.That(wrote, Is.EqualTo(0), "could not write the switch configuration and the host folder in " + net.Folder);

            net.Switch = new X25emuEnvironment.LinuxProcess(string.Empty, X25emuEnvironment.ToolPath("x25switch"), "-v", "-c", net.Folder + "/switch.conf");
            Assert.That(SpinWait.SpinUntil(() => CanConnect(net.Host, net.CallerPort) || net.Switch.HasExited, Wait), Is.True,
                "the switch never took connections at " + net.Host + ":" + net.CallerPort + ":\n" + net.Switch.Output);
            Assert.That(net.Switch.HasExited, Is.False, "the switch stopped:\n" + net.Switch.Output);

            // The C x25host, the single-machine form, on port 1.
            net.CHost = new X25emuEnvironment.LinuxProcess(string.Empty, X25emuEnvironment.ToolPath("x25host"), hostCopy, "-x", "127.0.0.1:" + cPort, "-a", CAddress);

            // The C# x25host, the same single-machine form (MachineList.Single is what Program.cs uses for it), on port 0.
            MachineEntry entry = MachineList.Single(windowsFolder, net.Host + ":" + csPort, CSharpAddress, null);
            net.CSharpHost = new HostRunner(new List<MachineEntry> { entry }, NullLogger.Instance);
            net.CSharpHost.Start();

            Assert.That(SpinWait.SpinUntil(() => net.Switch.Output.Contains("Port 0: connection up") && net.Switch.Output.Contains("Port 1: connection up"), Wait), Is.True,
                "a host never connected to the switch:\nswitch:\n" + net.Switch.Output + "\nx25host:\n" + net.CHost.Output);
            return net;
        }

        private static bool CanConnect(string host, int port)
        {
            try
            {
                using TcpClient client = new TcpClient();
                return client.ConnectAsync(host, port).Wait(2000) && client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ the caller

        /// <summary>
        /// A bare X.25 caller: it places a PAD call (protocol identifier 01 00 00 00) and keeps every byte the host sends,
        /// user data and Q-bit data apart. It types like a PAD that forwards on CR: the whole line and its CR in one
        /// packet, nothing edited and nothing echoed - so what it records is exactly what the host sent.
        /// </summary>
        private sealed class RawCaller : IDisposable
        {
            private readonly X25Client _client;
            private readonly List<byte> _data = new List<byte>();
            private readonly List<byte> _q = new List<byte>();
            private readonly object _gate = new object();
            private readonly ManualResetEventSlim _cleared = new ManualResetEventSlim(false);
            private X25Vc? _vc;
            private int _takenData;
            private int _takenQ;
            private long _lastArrival;

            public RawCaller(string host, int callerPort, string calling)
            {
                Name = "caller " + calling;
                _client = new X25Client { ServiceName = "host-raw-" + calling };
                _client.SetOnData((c, vc, data, length, q, m, d) =>
                {
                    lock (_gate)
                    {
                        List<byte> into = q ? _q : _data;
                        for (int i = 0; i < length; i++)
                        {
                            into.Add(data[i]);
                        }

                        _lastArrival = Environment.TickCount64;
                        System.Threading.Monitor.PulseAll(_gate);
                    }
                });
                _client.SetOnClear((c, vc, cause, diagnostic) =>
                {
                    ClearCause = "cause " + cause + " diagnostic " + diagnostic;
                    _cleared.Set();
                    lock (_gate)
                    {
                        System.Threading.Monitor.PulseAll(_gate);
                    }
                });
                _client.Open(new X25ClientConfig { Host = host, Port = (ushort)callerPort, LocalAddress = calling, Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            }

            public string Name { get; }

            public string ClearCause { get; private set; } = "no clear";

            public bool Cleared => _cleared.IsSet;

            public bool Call(string address)
            {
                _cleared.Reset();
                _vc = _client.CallWait(address, Wait, PadCud);
                return _vc != null && _vc.Connected;
            }

            public void TypeLatin1(string line)
            {
                byte[] bytes = Encoding.Latin1.GetBytes(line);
                Assert.That(bytes.Length, Is.LessThanOrEqualTo(128), "a script line must fit one packet");
                Assert.That(_client.Send(_vc!, bytes, bytes.Length, false, false), Is.True, Name + " could not send " + Show(line));
            }

            /// <summary>
            /// Waits for the host's answer to the last thing typed - its first byte, then <see cref="QuietMs"/> of
            /// silence (or a clear) - and returns everything that arrived since the step before.
            /// </summary>
            public Step TakeStep()
            {
                lock (_gate)
                {
                    long end = Environment.TickCount64 + FirstByteMs;
                    while (_data.Count == _takenData && _q.Count == _takenQ && !_cleared.IsSet)
                    {
                        long left = end - Environment.TickCount64;
                        if (left <= 0)
                        {
                            break;
                        }

                        System.Threading.Monitor.Wait(_gate, (int)left);
                    }

                    while (!_cleared.IsSet)
                    {
                        long quietFor = Environment.TickCount64 - _lastArrival;
                        if (quietFor >= QuietMs)
                        {
                            break;
                        }

                        System.Threading.Monitor.Wait(_gate, (int)(QuietMs - quietFor));
                    }

                    Step step = new Step
                    {
                        Data = _data.GetRange(_takenData, _data.Count - _takenData).ToArray(),
                        Q = _q.GetRange(_takenQ, _q.Count - _takenQ).ToArray(),
                    };
                    _takenData = _data.Count;
                    _takenQ = _q.Count;
                    return step;
                }
            }

            /// <summary>True when the last Q-bit data received is the invitation to clear, 01 (X.29 (12/97) 4.4.3).</summary>
            public bool WaitForInvitation(int timeoutMs)
            {
                long end = Environment.TickCount64 + timeoutMs;
                lock (_gate)
                {
                    while (_q.Count == 0 || _q[_q.Count - 1] != 0x01)
                    {
                        long left = end - Environment.TickCount64;
                        if (left <= 0 || _cleared.IsSet)
                        {
                            return false;
                        }

                        System.Threading.Monitor.Wait(_gate, (int)left);
                    }

                    return true;
                }
            }

            public void Hangup()
            {
                if (_vc != null && !_cleared.IsSet)
                {
                    _client.Clear(_vc);
                }
            }

            public void Dispose()
            {
                _client.Dispose();
                _cleared.Dispose();
            }
        }
    }
}
