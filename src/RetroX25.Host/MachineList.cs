//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Globalization;

namespace RetroX25.Host
{
    /// <summary>
    /// One machine of a machine list: a script, the X.121 address (NUA) it answers, and the switch line it hangs on.
    /// </summary>
    internal sealed class MachineEntry
    {
        /// <summary>The section name, <c>machine.N</c>, for messages.</summary>
        public string Section { get; init; } = string.Empty;

        /// <summary>The machine's name, for the log and the script (<c>machine.settings.hostname</c>).</summary>
        public string Hostname { get; set; } = string.Empty;

        /// <summary>The X.121 address it answers; calls are given to the machine whose NUA equals the called address.</summary>
        public string Nua { get; set; } = string.Empty;

        /// <summary>The switch line: host and TCP port of the switch's XOT port.</summary>
        public string SwitchHost { get; set; } = string.Empty;

        /// <summary>The switch line's TCP port.</summary>
        public ushort SwitchPort { get; set; }

        /// <summary>The group (<c>--group</c> picks one); <c>default</c> when not given.</summary>
        public string Group { get; set; } = "default";

        /// <summary>The full path of the JavaScript file (<c>type = script</c>); empty for a host folder machine.</summary>
        public string ScriptPath { get; set; } = string.Empty;

        /// <summary>The full path of the host folder (<c>type = &lt;folder&gt;</c> in the library); empty for a script.</summary>
        public string HostFolder { get; set; } = string.Empty;

        /// <summary>The <c>type</c> as written, for messages.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>The <c>setting.&lt;name&gt;</c> values, given to the script as <c>machine.settings</c>.</summary>
        public Dictionary<string, string> Settings { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The switch line as one key: <c>host:port</c>.</summary>
        public string Line => SwitchHost + ":" + SwitchPort.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a machine list (docs/x25host-format.md, "Many machines at once"). The same ini style as the switch files:
    /// <c>[section]</c>, <c>key = value</c>, <c>#</c> starts a comment, a value in double quotes keeps its spaces.
    /// Anything wrong stops the program with the file, line and name: an unknown section or key, a missing key, a bad
    /// address, a script that is not there, two machines with one NUA. A typo is never a silently missing machine.
    /// </summary>
    /// <remarks>
    /// <code>
    /// [defaults]
    /// library = ../hosts          # where the host folders are, relative to the list file
    ///
    /// [machine.0]
    /// type = vms                  # the host folder library/vms (its host.ini), or "script"
    /// hostname = TOMVAX           # optional for a folder: else [host] hostname, else the folder name
    /// nua = 23429002
    /// switch = 127.0.0.1:25012
    ///
    /// [machine.1]
    /// type = script
    /// script = hosts/guess.js     # relative to the list file
    /// hostname = GUESS
    /// nua = 24221001
    /// switch = 127.0.0.1:25003
    /// group = norway              # optional
    /// setting.greeting = Hello    # optional, any number: machine.settings.greeting in the script
    /// </code>
    /// </remarks>
    internal static class MachineList
    {
        /// <summary>X.25 (10/96) 5.2.1: at most 15 digits in an address.</summary>
        private const int MaxAddressDigits = 15;

        /// <summary><c>script = builtin:&lt;name&gt;</c>: a script that comes inside the RetroIO host package.</summary>
        public const string BuiltInPrefix = "builtin:";

        /// <summary>Reads and checks a machine list file.</summary>
        /// <param name="path">The list file.</param>
        /// <returns>The machines, in the order of their numbers.</returns>
        /// <exception cref="FormatException">Anything wrong in the file; the message names the file, line and name.</exception>
        public static List<MachineEntry> Load(string path)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                throw new FormatException(path + ": no such file");
            }

            return Parse(File.ReadAllLines(full), full, Path.GetDirectoryName(full) ?? ".");
        }

        /// <summary>Reads and checks the lines of a machine list.</summary>
        /// <param name="lines">The file's lines.</param>
        /// <param name="fileName">The file's name, for messages.</param>
        /// <param name="baseDirectory">The folder script paths are relative to.</param>
        /// <returns>The machines, in the order of their numbers.</returns>
        /// <exception cref="FormatException">Anything wrong; the message names the file, line and name.</exception>
        public static List<MachineEntry> Parse(string[] lines, string fileName, string baseDirectory)
        {
            return Parse(lines, fileName, baseDirectory, new List<OperatorLogin>());
        }

        /// <summary>Reads the operator logins of a machine list (<c>[operator.N]</c>: name, password).</summary>
        /// <param name="path">The machine list.</param>
        /// <returns>The logins; empty when the file has none.</returns>
        /// <exception cref="FormatException">Anything wrong in the file.</exception>
        public static List<OperatorLogin> LoadOperators(string path)
        {
            string full = Path.GetFullPath(path);
            List<OperatorLogin> ops = new List<OperatorLogin>();
            Parse(File.ReadAllLines(full), full, Path.GetDirectoryName(full) ?? ".", ops);
            return ops;
        }

        /// <summary>As <see cref="Parse(string[], string, string)"/>, also giving the operator logins.</summary>
        /// <param name="lines">The file's lines.</param>
        /// <param name="fileName">The file's name, for messages.</param>
        /// <param name="baseDirectory">The folder script paths are relative to.</param>
        /// <param name="ops">Filled with the <c>[operator.N]</c> logins.</param>
        /// <returns>The machines.</returns>
        /// <exception cref="FormatException">Anything wrong; the message names the file, line and name.</exception>
        public static List<MachineEntry> Parse(string[] lines, string fileName, string baseDirectory, List<OperatorLogin> ops)
        {
            OperatorLogin? currentOperator = null;
            SortedDictionary<int, MachineEntry> machines = new SortedDictionary<int, MachineEntry>();
            Dictionary<MachineEntry, int> sectionLine = new Dictionary<MachineEntry, int>();
            MachineEntry? current = null;
            bool inDefaults = false;
            bool defaultsSeen = false;
            string library = string.Empty;

            for (int n = 0; n < lines.Length; n++)
            {
                string where = fileName + ":" + (n + 1).ToString(CultureInfo.InvariantCulture);
                string line = StripComment(lines[n]).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line[0] == '[')
                {
                    if (line[line.Length - 1] != ']')
                    {
                        throw new FormatException(where + ": a section line must end with ]");
                    }

                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (name == "defaults")
                    {
                        if (defaultsSeen)
                        {
                            throw new FormatException(where + ": [defaults] is given twice");
                        }

                        defaultsSeen = true;
                        inDefaults = true;
                        current = null;
                        currentOperator = null;
                        continue;
                    }

                    inDefaults = false;
                    currentOperator = null;
                    if (name.StartsWith("operator.", StringComparison.Ordinal)
                        && int.TryParse(name.AsSpan(9), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    {
                        // A login for the telnet console (--console-port): name and password.
                        current = null;
                        currentOperator = new OperatorLogin { Section = name, Where = where };
                        ops.Add(currentOperator);
                        continue;
                    }

                    if (!name.StartsWith("machine.", StringComparison.Ordinal)
                        || !int.TryParse(name.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    {
                        throw new FormatException(where + ": unknown section [" + name + "] (only [defaults], [machine.N] and [operator.N])");
                    }

                    if (machines.ContainsKey(number))
                    {
                        throw new FormatException(where + ": [" + name + "] is given twice");
                    }

                    current = new MachineEntry { Section = name };
                    machines.Add(number, current);
                    sectionLine.Add(current, n + 1);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    throw new FormatException(where + ": expected key = value");
                }

                string key = line.Substring(0, eq).Trim();
                string value = Unquote(line.Substring(eq + 1).Trim());
                if (inDefaults)
                {
                    if (key != "library")
                    {
                        throw new FormatException(where + ": unknown key " + key + " in [defaults] (only library)");
                    }

                    library = Path.GetFullPath(Path.Combine(baseDirectory, value));
                    if (!Directory.Exists(library))
                    {
                        throw new FormatException(where + ": library " + value + " is not there (" + library + ")");
                    }

                    continue;
                }

                if (currentOperator != null)
                {
                    if (key == "name" && value.Length > 0)
                    {
                        currentOperator.Name = value;
                    }
                    else if (key == "password" && value.Length > 0)
                    {
                        currentOperator.Password = value;
                    }
                    else
                    {
                        throw new FormatException(where + ": [" + currentOperator.Section + "] " + key
                            + (key == "name" || key == "password" ? " may not be empty" : ": unknown key (only name and password)"));
                    }

                    continue;
                }

                if (current == null)
                {
                    throw new FormatException(where + ": a key before the first [machine.N]");
                }

                SetKey(current, key, value, where, baseDirectory);
            }

            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i].Name.Length == 0 || ops[i].Password.Length == 0)
                {
                    throw new FormatException(ops[i].Where + ": [" + ops[i].Section + "] needs both name and password");
                }
            }

            List<MachineEntry> result = new List<MachineEntry>(machines.Count);
            Dictionary<string, MachineEntry> byNua = new Dictionary<string, MachineEntry>(StringComparer.Ordinal);
            foreach (KeyValuePair<int, MachineEntry> pair in machines)
            {
                MachineEntry m = pair.Value;
                string where = fileName + ":" + sectionLine[m].ToString(CultureInfo.InvariantCulture) + ": [" + m.Section + "]";
                if (m.Type.Length == 0)
                {
                    m.Type = m.ScriptPath.Length > 0 ? "script" : throw new FormatException(where + " has no type");
                }

                if (m.Type == "script")
                {
                    if (m.ScriptPath.Length == 0)
                    {
                        throw new FormatException(where + " has no script");
                    }
                }
                else
                {
                    // A host folder of the library. Its own host.ini is read (and checked) when the machine is made.
                    if (m.ScriptPath.Length > 0 || m.Settings.Count > 0)
                    {
                        throw new FormatException(where + ": script and setting.<name> are for type = script only");
                    }

                    if (library.Length == 0)
                    {
                        throw new FormatException(where + ": type = " + m.Type + " is a host folder, but there is no [defaults] library");
                    }

                    if (m.Type.Contains('/', StringComparison.Ordinal) || m.Type.Contains('\\', StringComparison.Ordinal)
                        || m.Type.Contains("..", StringComparison.Ordinal))
                    {
                        throw new FormatException(where + ": type = " + m.Type + " must be the name of a folder in the library");
                    }

                    string folder = Path.Combine(library, m.Type);
                    if (!File.Exists(Path.Combine(folder, "host.ini")))
                    {
                        throw new FormatException(where + ": type = " + m.Type + ": no host folder " + folder + " with a host.ini");
                    }

                    m.HostFolder = folder;
                }

                if (m.Nua.Length == 0)
                {
                    throw new FormatException(where + " has no nua");
                }

                if (m.SwitchHost.Length == 0)
                {
                    throw new FormatException(where + " has no switch");
                }

                if (m.Hostname.Length == 0 && m.Type == "script")
                {
                    // A host folder gives its own name ([host] hostname, else its folder name) when it is read.
                    m.Hostname = m.Section;
                }

                if (byNua.TryGetValue(m.Nua, out MachineEntry? other))
                {
                    throw new FormatException(where + ": nua " + m.Nua + " is also [" + other.Section + "]");
                }

                byNua.Add(m.Nua, m);
                result.Add(m);
            }

            return result;
        }

        /// <summary>
        /// The single-machine form, <c>x25host &lt;host-folder&gt; -x host:port [-a address] [-n hostname]</c>: one host
        /// folder as one machine. The switch and address get the same checks as in a machine list.
        /// </summary>
        /// <param name="folder">The host folder.</param>
        /// <param name="sw">The switch line, <c>host:port</c>.</param>
        /// <param name="address">The address (<c>-a</c>), or null for host.ini's.</param>
        /// <param name="hostname">The hostname (<c>-n</c>), or null for host.ini's.</param>
        /// <returns>The machine.</returns>
        /// <exception cref="FormatException">A value is wrong, or the folder has no host.ini.</exception>
        public static MachineEntry Single(string folder, string sw, string? address, string? hostname)
        {
            string full = Path.GetFullPath(folder);
            if (!File.Exists(Path.Combine(full, "host.ini")))
            {
                throw new FormatException(folder + ": no host.ini in this folder");
            }

            MachineEntry m = new MachineEntry { Section = "command line", HostFolder = full, Type = Path.GetFileName(full) };
            SetKey(m, "switch", sw, "-x", full);
            if (address != null)
            {
                SetKey(m, "nua", address, "-a", full);
            }

            m.Hostname = hostname ?? string.Empty;
            return m;
        }

        /// <summary>One <c>[operator.N]</c>: a login for the telnet console.</summary>
        public sealed class OperatorLogin
        {
            /// <summary>The section name, for messages.</summary>
            public string Section { get; init; } = string.Empty;

            /// <summary>The file and line of the section, for messages.</summary>
            public string Where { get; init; } = string.Empty;

            /// <summary>The login name (compared in any case).</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>The password (compared exactly).</summary>
            public string Password { get; set; } = string.Empty;
        }

        private static void SetKey(MachineEntry m, string key, string value, string where, string baseDirectory)
        {
            if (key.StartsWith("setting.", StringComparison.Ordinal) && key.Length > 8)
            {
                m.Settings[key.Substring(8)] = value;
                return;
            }

            switch (key)
            {
                case "type":
                    // "script", or the name of a host folder in [defaults] library (checked once the file is read).
                    if (value.Length == 0)
                    {
                        throw new FormatException(where + ": type is empty");
                    }

                    m.Type = value;
                    break;
                case "script":
                    if (value.Length == 0)
                    {
                        throw new FormatException(where + ": script is empty");
                    }

                    if (value.StartsWith(BuiltInPrefix, StringComparison.Ordinal))
                    {
                        // A script built into RetroIO.Protocols.X25.Host (its scripts folder), e.g. builtin:altos-chat.
                        string name = value.Substring(BuiltInPrefix.Length);
                        if (RetroIO.Protocols.X25.Host.HostScripts.Get(name) == null)
                        {
                            throw new FormatException(where + ": script " + value + ": no built-in script of that name (there are: "
                                + string.Join(", ", RetroIO.Protocols.X25.Host.HostScripts.Names()) + ")");
                        }

                        m.ScriptPath = value;
                        break;
                    }

                    string path = Path.GetFullPath(Path.Combine(baseDirectory, value));
                    if (!File.Exists(path))
                    {
                        throw new FormatException(where + ": script " + value + " is not there (" + path + ")");
                    }

                    m.ScriptPath = path;
                    break;
                case "hostname":
                    m.Hostname = value;
                    break;
                case "nua":
                    if (value.Length == 0 || value.Length > MaxAddressDigits || !AllDigits(value))
                    {
                        throw new FormatException(where + ": nua = " + value + " must be 1 to 15 digits (X.25 5.2.1)");
                    }

                    m.Nua = value;
                    break;
                case "switch":
                    int colon = value.LastIndexOf(':');
                    if (colon <= 0 || !ushort.TryParse(value.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port == 0)
                    {
                        throw new FormatException(where + ": switch = " + value + " must be host:port");
                    }

                    m.SwitchHost = value.Substring(0, colon);
                    m.SwitchPort = port;
                    break;
                case "group":
                    m.Group = value.Length == 0 ? "default" : value;
                    break;
                default:
                    throw new FormatException(where + ": unknown key " + key);
            }
        }

        private static bool AllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Drops a <c>#</c> comment, but not a <c>#</c> inside double quotes.</summary>
        private static string StripComment(string line)
        {
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    quoted = !quoted;
                }
                else if (line[i] == '#' && !quoted)
                {
                    return line.Substring(0, i);
                }
            }

            return line;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}
