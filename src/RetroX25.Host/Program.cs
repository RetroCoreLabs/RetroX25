//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Client;

namespace RetroX25.Host
{
    /// <summary>
    /// x25host - fake machines and services on an X.25 network, each a host folder (host.ini and text files) or a
    /// JavaScript program.
    /// </summary>
    /// <remarks>
    /// Usage: <c>x25host --machines machines.ini [--group name] [-v]</c>. Every <c>[machine.N]</c> of the list runs in
    /// this one process (docs/x25host-format.md, "Many machines at once"); the script API is in the README of the
    /// RetroIO package RetroIO.Protocols.X25.Host.
    /// </remarks>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string? list = null;
            string? group = null;
            string? folder = null;
            string? sw = null;
            string? address = null;
            string? hostname = null;
            bool statsLog = false;
            int consolePort = 0;
            System.Net.IPAddress consoleBind = System.Net.IPAddress.Loopback;
            int verbosity = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "-x" && i + 1 < args.Length)
                {
                    sw = args[++i];
                }
                else if (arg == "-a" && i + 1 < args.Length)
                {
                    address = args[++i];
                }
                else if (arg == "-n" && i + 1 < args.Length)
                {
                    hostname = args[++i];
                }
                else if (!arg.StartsWith('-') && folder == null && list == null)
                {
                    folder = arg;
                }
                else if (arg == "--machines" && i + 1 < args.Length)
                {
                    list = args[++i];
                }
                else if (arg == "--group" && i + 1 < args.Length)
                {
                    group = args[++i];
                }
                else if (arg == "--console-port" && i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort cp) && cp > 0)
                {
                    consolePort = cp;
                    i++;
                }
                else if (arg == "--console-bind" && i + 1 < args.Length && System.Net.IPAddress.TryParse(args[i + 1], out System.Net.IPAddress? ba))
                {
                    consoleBind = ba;
                    i++;
                }
                else if (arg == "--stats-log")
                {
                    statsLog = true;
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

            // Exactly one of the two forms: a machine list, or one host folder with its switch (-x).
            if ((list == null) == (folder == null) || (folder != null && (sw == null || group != null))
                || (list != null && (sw != null || address != null || hostname != null)))
            {
                return Usage();
            }

            List<MachineEntry> machines;
            List<MachineList.OperatorLogin> operators = new List<MachineList.OperatorLogin>();
            try
            {
                machines = folder != null
                    ? new List<MachineEntry> { MachineList.Single(folder, sw!, address, hostname) }
                    : MachineList.Load(list!);
                if (consolePort > 0)
                {
                    // The logins are in the machine list; without one, nobody could log in.
                    operators = list != null ? MachineList.LoadOperators(list) : operators;
                    if (operators.Count == 0)
                    {
                        Console.Error.WriteLine("x25host: --console-port needs an [operator.N] (name, password) in the machine list");
                        return 1;
                    }
                }
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine("x25host: " + ex.Message);
                return 1;
            }

            if (group != null)
            {
                List<MachineEntry> picked = new List<MachineEntry>();
                for (int i = 0; i < machines.Count; i++)
                {
                    if (machines[i].Group == group)
                    {
                        picked.Add(machines[i]);
                    }
                }

                machines = picked;
            }

            if (machines.Count == 0)
            {
                Console.Error.WriteLine("x25host: no machines to run" + (group != null ? " in group " + group : string.Empty));
                return 1;
            }

            using ILoggerFactory loggerFactory = ToolSupport.CreateLoggerFactory(verbosity);
            ILogger log = loggerFactory.CreateLogger("x25host");

            HostRunner runner;
            try
            {
                runner = new HostRunner(machines, log) { StatsLogMs = statsLog ? 60000 : 0 };
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("x25host: " + ex.Message);
                return 1;
            }

            using (runner)
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    log.LogInformation("x25host: stopping");
                    runner.Stop();
                };

                runner.Start();
                log.LogInformation("x25host started: {Count} machine(s)", machines.Count);

                TelnetConsole? telnet = null;
                if (consolePort > 0)
                {
                    telnet = new TelnetConsole(runner, operators, consoleBind, consolePort, log);
                    try
                    {
                        telnet.Start();
                    }
                    catch (System.Net.Sockets.SocketException ex)
                    {
                        Console.Error.WriteLine("x25host: the console cannot listen on " + consoleBind + ":" + consolePort + ": " + ex.Message);
                        runner.Stop();
                        return 1;
                    }
                }

                // The operator console on stdin (docs/x25host-format.md). With no terminal (stdin closed or
                // redirected from nothing) ReadLine returns null at once and the process just runs.
                OperatorConsole console = new OperatorConsole(runner);
                Thread reader = new Thread(() =>
                {
                    string? typed;
                    while ((typed = Console.In.ReadLine()) != null)
                    {
                        Console.Out.Write(console.Execute(typed));
                        if (console.QuitAsked)
                        {
                            runner.Stop();
                            return;
                        }
                    }
                }) { IsBackground = true, Name = "x25host-console" };
                reader.Start();
                runner.Wait();
                telnet?.Dispose();
            }

            return 0;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: x25host --machines <list.ini> [--group <name>] [-v]");
            Console.Error.WriteLine("       x25host <host-folder> -x <switch-host:port> [-a <address>] [-n <hostname>] [-v]");
            Console.Error.WriteLine("  -x host:port     one host folder as one machine, on this switch line (XOT)");
            Console.Error.WriteLine("  -a address       the X.121 address it answers (default: [host] address in host.ini)");
            Console.Error.WriteLine("  -n hostname      its name (default: [host] hostname, else the folder name)");
            Console.Error.WriteLine("  --machines file  the machine list: one [machine.N] per machine (type, nua, switch, hostname)");
            Console.Error.WriteLine("  --group name     run only the machines with group = name");
            Console.Error.WriteLine("  --console-port N the operator console over telnet on port N; the logins are [operator.N] in the list");
            Console.Error.WriteLine("  --console-bind a the address it listens on (default 127.0.0.1)");
            Console.Error.WriteLine("  --stats-log     each machine's counters (calls, bytes, packets, wrong logins) to the log once a minute");
            Console.Error.WriteLine("  -v               verbose (repeat for more)");
            return 1;
        }
    }
}
