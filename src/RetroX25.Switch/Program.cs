//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Monitor;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Management;
using RetroIO.Protocols.X25.Switch.Management.Telnet;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Switch
{
    /// <summary>
    /// x25switch - a multi-port X.25 packet switch.
    /// </summary>
    /// <remarks>
    /// Loads a switch configuration, brings up each port as an XOT endpoint, routes and stitches calls
    /// between ports, and exposes a telnet management console plus an X.25-based management surface.
    ///
    /// Usage: x25switch &lt;config-file&gt; [-v] [-v]
    ///
    /// One <c>-v</c> logs at debug level. Two log at trace level, which adds every packet each port
    /// receives or writes, in hex.
    /// </remarks>
    internal static class Program
    {
        private static readonly ManualResetEventSlim Shutdown = new ManualResetEventSlim(false);

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments: the configuration file path and an optional <c>-v</c>.
        /// </param>
        /// <returns>
        /// Process exit code (0 on clean shutdown, 1 on usage or startup error).
        /// </returns>
        private static int Main(string[] args)
        {
            string? configPath = null;
            int verbosity = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-v")
                {
                    verbosity++;
                }
                else if (configPath == null)
                {
                    configPath = args[i];
                }
            }

            if (configPath == null)
            {
                Console.Error.WriteLine("Usage: x25switch <config-file> [-v] [-v]    (two -v also log every packet)");
                return 1;
            }

            LogLevel level = verbosity >= 2 ? LogLevel.Trace : verbosity == 1 ? LogLevel.Debug : LogLevel.Information;
            // Forwards logs to the monitoring stream; wired to the server once it exists.
            MonitorLoggerProvider logForwarder = new MonitorLoggerProvider();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(level);
                b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
                b.AddProvider(logForwarder);
            });
            ILogger logger = loggerFactory.CreateLogger("x25switch");

            SwitchConfig config;
            try
            {
                config = SwitchConfigLoader.LoadFile(configPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load config {Path}", configPath);
                return 1;
            }

            SwitchNode node = new SwitchNode(config) { UptimeStartMs = Environment.TickCount64 };
            for (int i = 0; i < config.Ports.Count; i++)
            {
                XotSwitchPort port = new XotSwitchPort(config.Ports[i], logger);
                node.AddPort(port);

                // At -v -v (trace level) every packet each port receives or writes is logged in hex, with
                // its direction. Working out why two X.25 implementations will not talk to each other
                // comes down to reading the packets they actually exchanged; this is where they are.
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    port.PacketCaptured += (portId, inbound, packet) =>
                        logger.LogTrace("Port {Port} {Direction} {Packet}", portId, inbound ? "RX" : "TX", BitConverter.ToString(packet).Replace('-', ' '));
                }
            }

            SwitchCore core = new SwitchCore(node);
            SwitchCli cli = new SwitchCli(node);
            SwitchApiProcessor api = new SwitchApiProcessor(node);
            ManagementHost mgmt = new ManagementHost(node, cli, api);

            if (!string.IsNullOrEmpty(config.MgmtAddr))
            {
                core.ManagementAddress = config.MgmtAddr;
                core.ManagementCall += mgmt.OnManagementCall;
            }
            core.CallSetup += mgmt.OnCallSetup;
            core.CallClear += mgmt.OnCallClear;

            // The management host also sees circuit data (to serve management sessions); the engine ignores
            // data on circuits it has no stitch for.
            for (int i = 0; i < node.Ports.Count; i++)
            {
                ISwitchPort p = node.Ports[i];
                p.DataReceived += (port, lci, data, length, q, m, d) => mgmt.OnPortData(port, lci, data, length, q, m, d);
            }

            cli.ClearVcHandler = (portId, lci) => ClearVc(node, portId, lci);

            // Optional monitoring stream: capture every XOT port's traffic and publish to remote viewers.
            // Attached before the ports start so no early frames are missed.
            MonitorServer? monitor = null;
            MonitorHost? monitorHost = null;
            if (config.MonitorPort != 0)
            {
                monitor = new MonitorServer("0.0.0.0", config.MonitorPort);
                logForwarder.Server = monitor;
                monitorHost = new MonitorHost(monitor);
                for (int i = 0; i < node.Ports.Count; i++)
                {
                    if (node.Ports[i] is XotSwitchPort xotPort)
                    {
                        monitorHost.Attach(xotPort);
                    }
                }

                // Summarize which routes send traffic out each port, for the link-status view.
                for (int i = 0; i < config.Routing.Routes.Count; i++)
                {
                    X121Route route = config.Routing.Routes[i];
                    string desc = DescribeRoute(route);
                    if (desc.Length == 0)
                    {
                        continue;
                    }

                    if (monitorHost.PortRoutes.TryGetValue(route.DestPort, out string? existing) && existing.Length > 0)
                    {
                        monitorHost.PortRoutes[route.DestPort] = existing + ", " + desc;
                    }
                    else
                    {
                        monitorHost.PortRoutes[route.DestPort] = desc;
                    }
                }

                // Build the node's topology from its config-declared neighbours (management/monitor plane
                // only — the X.25 protocol is untouched). Viewers discover the network by walking these.
                TopologySnapshot topology = new TopologySnapshot
                {
                    NodeId = string.IsNullOrEmpty(config.Dnic) ? config.Name : config.Dnic,
                    Name = config.Name,
                    Dnic = config.Dnic,
                    MonitorPort = config.MonitorPort,
                    MgmtPort = config.MgmtPort
                };
                for (int i = 0; i < config.Ports.Count; i++)
                {
                    PortConfig pc = config.Ports[i];
                    if (pc.PeerName.Length == 0)
                    {
                        continue;
                    }

                    topology.Neighbours.Add(new NeighbourInfo
                    {
                        Name = pc.PeerName,
                        MonitorHost = pc.PeerMonitorHost,
                        MonitorPort = pc.PeerMonitorPort,
                        ViaPortId = pc.Id,
                        Up = false
                    });
                }

                monitorHost.SetTopology(topology, node.UptimeStartMs);
            }

            // A port that does not start is a startup error, not something to run on without. A switch
            // with a dead listener still answers its management console and looks alive, while every
            // call routed to that port goes nowhere. (A dialling trunk that has not linked yet still
            // counts as started: it keeps retrying in the background.)
            int failedPorts = 0;
            for (int i = 0; i < node.Ports.Count; i++)
            {
                if (!node.Ports[i].Start())
                {
                    failedPorts++;
                }
            }

            if (failedPorts > 0)
            {
                logger.LogError("x25switch: {Failed} of {Ports} ports did not start - see the errors above. Exiting.",
                    failedPorts, node.Ports.Count);
                monitorHost?.Dispose();
                for (int i = 0; i < node.Ports.Count; i++)
                {
                    node.Ports[i].Stop();
                }

                return 1;
            }

            if (monitor != null)
            {
                if (monitor.Start())
                {
                    logger.LogInformation("Monitoring stream listening on port {Port}", monitor.ListenPort);
                    // Publish a link/port status snapshot every two seconds for the link-status view.
                    monitorHost?.StartStatusTimer(2000, () => node.ActiveStitchCount);
                }
                else
                {
                    logger.LogWarning("Monitoring stream failed to bind port {Port}", config.MonitorPort);
                }
            }

            TelnetCliServer? telnet = null;
            if (config.MgmtPort != 0)
            {
                telnet = new TelnetCliServer(cli, "0.0.0.0", config.MgmtPort);
                if (telnet.Start())
                {
                    logger.LogInformation("Management console listening on port {Port}", telnet.Port);
                }
                else
                {
                    logger.LogWarning("Management console failed to bind port {Port}", config.MgmtPort);
                }
            }

            logger.LogInformation("x25switch '{Name}' (DNIC {Dnic}) started with {Ports} ports",
                string.IsNullOrEmpty(config.Name) ? "Switch" : config.Name, config.Dnic, node.Ports.Count);

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Shutdown.Set();
            };

            Shutdown.Wait();

            telnet?.Stop();
            monitorHost?.Dispose();
            monitor?.Stop();
            for (int i = 0; i < node.Ports.Count; i++)
            {
                node.Ports[i].Stop();
            }
            return 0;
        }

        /// <summary>
        /// Formats a route as a short human-readable descriptor for the link-status view.
        /// </summary>
        /// <param name="route">
        /// The route to describe.
        /// </param>
        /// <returns>
        /// A short descriptor such as <c>prefix 2352</c> or <c>default</c>, or empty for unshown types.
        /// </returns>
        private static string DescribeRoute(X121Route route)
        {
            switch (route.Type)
            {
                case X121RouteType.Prefix: return "prefix " + route.Address;
                case X121RouteType.Exact: return "exact " + route.Address;
                case X121RouteType.Dnic: return "dnic " + route.Dnic;
                case X121RouteType.Regex: return "regex " + route.Pattern;
                case X121RouteType.Hunt: return "hunt " + route.HuntGroupId;
                case X121RouteType.Default: return "default";
                default: return string.Empty;
            }
        }

        private static bool ClearVc(SwitchNode node, int portId, ushort lci)
        {
            ISwitchPort? port = node.GetPort(portId);
            if (port == null)
            {
                return false;
            }

            VcStitch? stitch = node.FindIngress(port, lci);
            if (stitch == null)
            {
                stitch = node.FindEgress(port, lci);
            }

            if (stitch == null)
            {
                return false;
            }

            stitch.InPort.ClearCall(stitch.InLci, SwitchCore.CauseDteOriginated, 0);
            stitch.OutPort.ClearCall(stitch.OutLci, SwitchCore.CauseDteOriginated, 0);
            node.DestroyStitch(stitch);
            return true;
        }
    }
}
