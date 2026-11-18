//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

namespace RetroX25.Host
{
    /// <summary>
    /// The operator console of docs/x25host-format.md: one command line in, the answer out. Built so far:
    /// <c>show hosts</c>, <c>show stats</c>, <c>help</c>, <c>quit</c>. The other commands of that table answer that they
    /// are not built yet, by name, so a missing command is never silent.
    /// </summary>
    internal sealed class OperatorConsole
    {
        private static readonly string[] NotBuilt =
        {
            "show host", "show users", "stop host", "start host", "restart host", "logout", "send", "reload", "log",
        };

        private readonly HostRunner _runner;

        private bool _confirming;

        /// <summary>Makes a console for the machines of one runner.</summary>
        /// <param name="runner">The runner.</param>
        public OperatorConsole(HostRunner runner)
        {
            _runner = runner;
        }

        /// <summary>Set by <c>quit</c>.</summary>
        public bool QuitAsked { get; private set; }

        /// <summary>Runs one command line.</summary>
        /// <param name="line">What the operator typed.</param>
        /// <returns>The answer, one or more lines, each ending in a newline (empty for an empty line).</returns>
        public string Execute(string line)
        {
            string cmd = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            if (_confirming)
            {
                // The question after quit: only "yes" stops; anything else goes back to the console.
                _confirming = false;
                if (cmd == "yes")
                {
                    QuitAsked = true;
                    return "stopping\n";
                }

                return "not stopped\n";
            }

            if (cmd.Length == 0)
            {
                return string.Empty;
            }

            switch (cmd)
            {
                case "show hosts":
                    return Join(_runner.HostLines());
                case "show stats":
                    return Join(_runner.StatsLines());
                case "show calls":
                    List<string> calls = _runner.CallLines();
                    return calls.Count == 0 ? "no calls\n" : Join(calls);
                case "help":
                    return "show hosts   one line per machine: hostname, type, NUA, group, state, calls now\n"
                        + "show stats   per machine: calls, refusals, bytes and data packets each way, wrong logins\n"
                        + "show calls   every call now: hostname, calling address, logical channel, since\n"
                        + "clear call <hostname> <lcn>   clear one call at once\n"
                        + "quit        stop x25host (asks first)\n";
                case "quit":
                    _confirming = true;
                    return "stop x25host and clear every call? (yes/no) ";
            }

            if (cmd.StartsWith("clear call", StringComparison.Ordinal))
            {
                string[] w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (w.Length != 4 || !ushort.TryParse(w[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ushort lcn))
                {
                    return "usage: clear call <hostname> <lcn>\n";
                }

                return _runner.ClearCall(w[2], lcn) ? "cleared\n" : "no call on lcn " + w[3] + " at " + w[2] + "\n";
            }

            for (int i = 0; i < NotBuilt.Length; i++)
            {
                if (cmd == NotBuilt[i] || cmd.StartsWith(NotBuilt[i] + " ", StringComparison.Ordinal))
                {
                    return "'" + NotBuilt[i] + "' is not built yet (see src/PLAN.md, G1)\n";
                }
            }

            return "unknown command: " + line.Trim() + " (help lists the commands)\n";
        }

        private static string Join(List<string> lines)
        {
            return lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
        }
    }
}
