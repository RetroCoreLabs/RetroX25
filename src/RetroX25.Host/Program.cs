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
    /// x25host - fake machines and services on an X.25 network, each a JavaScript program.
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
            int verbosity = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--machines" && i + 1 < args.Length)
                {
                    list = args[++i];
                }
                else if (arg == "--group" && i + 1 < args.Length)
                {
                    group = args[++i];
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

            if (list == null)
            {
                return Usage();
            }

            List<MachineEntry> machines;
            try
            {
                machines = MachineList.Load(list);
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
                runner = new HostRunner(machines, log);
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
                runner.Wait();
            }

            return 0;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: x25host --machines <list.ini> [--group <name>] [-v]");
            Console.Error.WriteLine("  --machines file  the machine list: one [machine.N] per machine (script, nua, switch, hostname)");
            Console.Error.WriteLine("  --group name     run only the machines with group = name");
            Console.Error.WriteLine("  -v               verbose (repeat for more)");
            return 1;
        }
    }
}
