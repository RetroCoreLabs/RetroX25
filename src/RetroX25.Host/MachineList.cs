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

        /// <summary>The full path of the JavaScript file.</summary>
        public string ScriptPath { get; set; } = string.Empty;

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
    /// [machine.0]
    /// type = script               # the only type so far; host.ini machines come later (PLAN G1)
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
            SortedDictionary<int, MachineEntry> machines = new SortedDictionary<int, MachineEntry>();
            Dictionary<MachineEntry, int> sectionLine = new Dictionary<MachineEntry, int>();
            MachineEntry? current = null;

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
                    if (!name.StartsWith("machine.", StringComparison.Ordinal)
                        || !int.TryParse(name.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    {
                        throw new FormatException(where + ": unknown section [" + name + "] (only [machine.N])");
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

                if (current == null)
                {
                    throw new FormatException(where + ": a key before the first [machine.N]");
                }

                string key = line.Substring(0, eq).Trim();
                string value = Unquote(line.Substring(eq + 1).Trim());
                SetKey(current, key, value, where, baseDirectory);
            }

            List<MachineEntry> result = new List<MachineEntry>(machines.Count);
            Dictionary<string, MachineEntry> byNua = new Dictionary<string, MachineEntry>(StringComparer.Ordinal);
            foreach (KeyValuePair<int, MachineEntry> pair in machines)
            {
                MachineEntry m = pair.Value;
                string where = fileName + ":" + sectionLine[m].ToString(CultureInfo.InvariantCulture) + ": [" + m.Section + "]";
                if (m.ScriptPath.Length == 0)
                {
                    throw new FormatException(where + " has no script");
                }

                if (m.Nua.Length == 0)
                {
                    throw new FormatException(where + " has no nua");
                }

                if (m.SwitchHost.Length == 0)
                {
                    throw new FormatException(where + " has no switch");
                }

                if (m.Hostname.Length == 0)
                {
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
                    // Only scripts so far. A host.ini machine (type = <folder>) comes with PLAN G1; until then it is
                    // refused by name rather than started as something else.
                    if (value != "script")
                    {
                        throw new FormatException(where + ": type = " + value + " is not supported yet (only script)");
                    }

                    break;
                case "script":
                    if (value.Length == 0)
                    {
                        throw new FormatException(where + ": script is empty");
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
