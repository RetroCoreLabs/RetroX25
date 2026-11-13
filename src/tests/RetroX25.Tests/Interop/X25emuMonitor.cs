//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using RetroIO.Protocols.X25.Monitor;

namespace RetroX25.Tests
{
    /// <summary>
    /// Reads the per-port counters of an x25emu switch through its monitor port (the X25MON/1 stream), so a test can say
    /// what the C switch counted for a call.
    /// </summary>
    internal static class X25emuMonitor
    {
        /// <summary>The monitor ports of x25emu's Switch A, B and C (see demo/crossenv-wsl.sh and the demo configs).</summary>
        internal static readonly int[] SwitchMonitorPorts = { 4101, 4102, 4103 };

        /// <summary>
        /// Connects to a switch's monitor port and returns the first status snapshot it sends, as a table by port number.
        /// </summary>
        internal static Dictionary<int, PortStatus> Snapshot(int monitorPort)
        {
            using MonitorClient client = new MonitorClient();
            StatusSnapshot? status = null;
            ManualResetEventSlim got = new ManualResetEventSlim(false);
            client.StatusReceived += s => { status = s; got.Set(); };
            if (!client.Connect(X25emuEnvironment.WslHost!, monitorPort))
            {
                throw new InvalidOperationException("could not connect to the monitor port " + monitorPort);
            }

            if (!got.Wait(15000))
            {
                throw new InvalidOperationException("the monitor port " + monitorPort + " sent no status snapshot");
            }

            Dictionary<int, PortStatus> table = new Dictionary<int, PortStatus>();
            for (int i = 0; i < status!.Ports.Count; i++)
            {
                table[status.Ports[i].Id] = status.Ports[i];
            }

            return table;
        }

        /// <summary>One line per port that changed: what the counters rose by between two snapshots.</summary>
        internal static string Delta(Dictionary<int, PortStatus> before, Dictionary<int, PortStatus> after)
        {
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<int, PortStatus> pair in after)
            {
                PortStatus b = before.TryGetValue(pair.Key, out PortStatus? found) ? found : new PortStatus();
                PortStatus a = pair.Value;
                if (a.CallsIn != b.CallsIn || a.CallsOut != b.CallsOut || a.CallsCleared != b.CallsCleared || a.PacketsIn != b.PacketsIn || a.PacketsOut != b.PacketsOut)
                {
                    text.Append("port ").Append(pair.Key).Append(": calls_in +").Append(a.CallsIn - b.CallsIn)
                        .Append(" calls_out +").Append(a.CallsOut - b.CallsOut)
                        .Append(" cleared +").Append(a.CallsCleared - b.CallsCleared)
                        .Append(" packets_in +").Append(a.PacketsIn - b.PacketsIn)
                        .Append(" packets_out +").Append(a.PacketsOut - b.PacketsOut).Append('\n');
                }
            }

            return text.ToString();
        }

        internal static uint Rise(Dictionary<int, PortStatus> before, Dictionary<int, PortStatus> after, int port, Func<PortStatus, uint> field)
        {
            uint b = before.TryGetValue(port, out PortStatus? found) ? field(found) : 0;
            return field(after[port]) - b;
        }
    }
}
