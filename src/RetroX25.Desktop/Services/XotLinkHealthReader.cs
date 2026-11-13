//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Net;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;
using RetroIO.Protocols.X25.Transport;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Services;

/// <summary>
/// One trunk link (an XOT peer or an HDLC trunk) as the main window shows it: a snapshot taken at one moment. It holds plain values
/// and changes nothing in the engine or the switch.
/// </summary>
public sealed class XotPeerRow
{
    /// <summary>Gets or sets the name: the interface name in the configuration, or the peer name of a switch port.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets where the row comes from: "Desktop link" or "switch port N".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Gets or sets "we dial" or "we listen".</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>Gets or sets the address of the peer (we dial) or the address we listen on (we listen), as host:port.</summary>
    public string Peer { get; set; } = string.Empty;

    /// <summary>Gets or sets the state in words, for example "TCP connected" or "NOT connected: connection refused".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Gets or sets how the link behaves after a failure, in words.</summary>
    public string Behaviour { get; set; } = string.Empty;

    /// <summary>Gets or sets when a TCP connection was last made, or "never".</summary>
    public string LastConnected { get; set; } = "never";

    /// <summary>Gets or sets how many connects failed.</summary>
    public int FailedConnects { get; set; }

    /// <summary>Gets or sets the reason the last connect failed; empty if none has.</summary>
    public string LastError { get; set; } = string.Empty;

    /// <summary>Gets or sets how many connections went away after they were up.</summary>
    public int Disconnects { get; set; }

    /// <summary>Gets or sets the bytes and packets seen, in words.</summary>
    public string Traffic { get; set; } = string.Empty;

    /// <summary>Gets or sets the calls placed and answered, in words.</summary>
    public string Calls { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the link is up, down (a fault) or not decided yet (nothing wrong, nothing to show either).</summary>
    public LinkHealth Health { get; set; } = LinkHealth.Checking;

    /// <summary>Gets the first line: who, which way, to where, and the state.</summary>
    public string Headline => Name + " (" + Source + ")  " + Direction + " " + Peer + "  [" + State + "]";

    /// <summary>Gets the second line: what the link does after a failure.</summary>
    public string BehaviourLine => Behaviour;

    /// <summary>
    /// Gets or sets a ready-made third line. Set for a peer ("Both ways") row, which counts the two
    /// directions apart; when empty the line is built from the fields above.
    /// </summary>
    public string DetailOverride { get; set; } = string.Empty;

    /// <summary>Gets the third line: the history and the counters.</summary>
    public string Detail
    {
        get
        {
            if (DetailOverride.Length > 0)
            {
                return DetailOverride;
            }

            string failed = FailedConnects.ToString() + (LastError.Length > 0 ? " (last error: " + LastError + ")" : string.Empty);
            return "last connected: " + LastConnected + " | failed connects: " + failed + " | disconnects: " + Disconnects
                + " | " + Traffic + " | " + Calls;
        }
    }

    /// <summary>Gets all three lines as one text; used to tell whether a row changed.</summary>
    public string FullText => Headline + "\n" + BehaviourLine + "\n" + Detail;
}

/// <summary>
/// What <see cref="XotLinkHealthReader"/> found: one row for every XOT peer that matters, and the
/// one quiet line for the built-in switch's own plumbing.
/// </summary>
public sealed class XotLinkHealthSnapshot
{
    /// <summary>The loud rows: every XOT peer that is not part of the built-in switch pair.</summary>
    public List<XotPeerRow> Peers { get; } = new List<XotPeerRow>();

    /// <summary>True when the app's own XOT links and the built-in switch were found to belong together, so the quiet line applies.</summary>
    public bool HasBuiltIn { get; set; }

    /// <summary>True when the quiet line says all is well.</summary>
    public bool BuiltInOk { get; set; } = true;

    /// <summary>
    /// The quiet line: "Built-in switch: OK", or "Built-in switch: PROBLEM - " and the reasons. Empty when
    /// <see cref="HasBuiltIn"/> is false.
    /// </summary>
    public string BuiltInText { get; set; } = string.Empty;
}

/// <summary>
/// Reads the real state of every trunk link, read-only: the engine's XOT client and server transports,
/// and the trunk ports of the built-in switch (XOT, and HDLC over TCP or serial). The class keeps its old
/// name from when XOT was the only kind of link.
/// </summary>
/// <remarks>
/// <para>
/// An XOT client link of the engine opens its TCP connection only when a call needs it, and does not
/// try again by itself. An XOT port of the built-in switch that dials (a DTE port) tries again every
/// couple of seconds. The rows say which of the two a link is.
/// </para>
/// <para>
/// The Desktop config can hold two XOT entries (one to the switch, one from the switch) that only join
/// the app's own engine to the built-in switch over loopback. They are found by FACTS, not by name: the
/// built-in switch is on, and the entry's address is a loopback address and a port of a built-in switch port that
/// really is the other end. Those two entries and those two ports become the single quiet line. Any
/// other entry (an external switch, a Cisco router) and any other switch port (a trunk to a real peer)
/// is a normal row.
/// </para>
/// </remarks>
public static class XotLinkHealthReader
{
    /// <summary>
    /// Takes a snapshot of every XOT peer.
    /// </summary>
    /// <param name="config">The configuration the engine runs on (names, modes, addresses).</param>
    /// <param name="transports">The engine's transport interfaces by name; null or empty when no engine runs.</param>
    /// <param name="builtInSwitch">The built-in switch, or null when the app has no such service.</param>
    /// <param name="nowUtc">The time to measure "ago" from.</param>
    /// <returns>The snapshot.</returns>
    public static XotLinkHealthSnapshot Read(RuntimeConfig config, IReadOnlyDictionary<string, ITransportInterface>? transports,
        EmbeddedSwitchService? builtInSwitch, DateTime nowUtc)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        XotLinkHealthSnapshot snapshot = new XotLinkHealthSnapshot();
        List<XotInterfaceConfig> entries = config.Routing != null ? config.Routing.XotInterfaces : new List<XotInterfaceConfig>();
        bool switchAsked = builtInSwitch != null && builtInSwitch.Enabled;
        IReadOnlyList<PortConfig> ports = switchAsked ? builtInSwitch!.ConfiguredPorts : Array.Empty<PortConfig>();

        // Which entry belongs to which built-in switch port(s), decided by facts. An entry can be tied to
        // two ports: a "Client" entry dials a port of the switch that listens (a DCE port); a "Server"
        // entry listens for a port of the switch that dials (a DTE port); a "Both" (peer) entry does both
        // and so can stand for the whole pair on its own.
        int[] dcePortOfEntry = new int[entries.Count];
        int[] dtePortOfEntry = new int[entries.Count];
        bool[] folded = new bool[entries.Count];
        bool[] portFolded = new bool[ports.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            dcePortOfEntry[i] = -1;
            dtePortOfEntry[i] = -1;
            XotInterfaceConfig entry = entries[i];
            if (!entry.Enabled)
            {
                continue;
            }

            for (int p = 0; p < ports.Count; p++)
            {
                if (portFolded[p])
                {
                    continue;
                }

                // An XOT entry can only be the other end of an XOT port. An HDLC trunk port on the same number is
                // not its partner (it does not speak XOT), so it is never folded into the quiet line.
                if (ports[p].Transport != PortTransport.Xot)
                {
                    continue;
                }

                if ((entry.Mode == XotMode.Client || entry.Mode == XotMode.Both) && dcePortOfEntry[i] < 0 && IsLoopbackHost(entry.RemoteIPAddress)
                    && ports[p].Mode == PortMode.Dce && ports[p].Port == entry.RemotePort && ListensOnLoopback(ports[p].Host))
                {
                    dcePortOfEntry[i] = p;
                }
                else if ((entry.Mode == XotMode.Server || entry.Mode == XotMode.Both) && dtePortOfEntry[i] < 0
                    && ports[p].Mode == PortMode.Dte && IsLoopbackHost(ports[p].Host) && ports[p].Port == entry.LocalPort)
                {
                    dtePortOfEntry[i] = p;
                }
            }

            // A "Both ways" entry stands for the whole pair only when BOTH ends are found: it dials a
            // port of the built-in switch AND the switch has a dialling port that comes back to it. With
            // only one end found it is also talking to something else, so it stays a loud row.
            if (entry.Mode == XotMode.Both && (dcePortOfEntry[i] < 0 || dtePortOfEntry[i] < 0))
            {
                dcePortOfEntry[i] = -1;
                dtePortOfEntry[i] = -1;
            }

            if (dcePortOfEntry[i] >= 0 || dtePortOfEntry[i] >= 0)
            {
                folded[i] = true;
                if (dcePortOfEntry[i] >= 0)
                {
                    portFolded[dcePortOfEntry[i]] = true;
                }

                if (dtePortOfEntry[i] >= 0)
                {
                    portFolded[dtePortOfEntry[i]] = true;
                }
            }
        }

        // The quiet line.
        List<string> problems = new List<string>();
        bool anyFolded = false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (!folded[i])
            {
                continue;
            }

            anyFolded = true;
            if (dcePortOfEntry[i] >= 0)
            {
                CheckBuiltInPart(entries[i], ports[dcePortOfEntry[i]], true, transports, builtInSwitch!, problems);
            }

            if (dtePortOfEntry[i] >= 0)
            {
                CheckBuiltInPart(entries[i], ports[dtePortOfEntry[i]], false, transports, builtInSwitch!, problems);
            }
        }

        if (anyFolded)
        {
            snapshot.HasBuiltIn = true;
            snapshot.BuiltInOk = problems.Count == 0;
            snapshot.BuiltInText = problems.Count == 0
                ? "Built-in switch: OK"
                : "Built-in switch: PROBLEM - " + string.Join("; ", problems);
        }

        // Loud rows: the engine's other XOT entries, then the switch's other ports.
        for (int i = 0; i < entries.Count; i++)
        {
            if (folded[i] || !entries[i].Enabled)
            {
                continue;
            }

            ITransportInterface? transport = null;
            if (transports != null)
            {
                transports.TryGetValue(entries[i].Name, out transport);
            }

            snapshot.Peers.Add(DesktopRow(entries[i], transport, nowUtc));
            if (transport is XotPeerTransport peerTransport)
            {
                // Connections from addresses nobody lists get their own rows, so they cannot hide.
                AddUnlistedRows(snapshot.Peers, entries[i], peerTransport.GetPeerReport(), nowUtc);
            }
        }

        if (switchAsked)
        {
            for (int p = 0; p < ports.Count; p++)
            {
                if (portFolded[p])
                {
                    continue;
                }

                PortConfig portConfig = ports[p];
                ISwitchPort? runningPort = FindRunning(builtInSwitch!, portConfig.Id);
                if (portConfig.Transport == PortTransport.Xot)
                {
                    snapshot.Peers.Add(SwitchRow(portConfig, runningPort, builtInSwitch!, nowUtc));
                }
                else
                {
                    // An HDLC trunk (hdlc-tcp, its hdlc-ppp alias, or hdlc-serial): a leased line, not an XOT peer.
                    snapshot.Peers.Add(HdlcRow(portConfig, runningPort, builtInSwitch!, nowUtc));
                }
            }
        }

        return snapshot;
    }

    // ---------------------------------------------------------------------------------------------
    // The quiet line
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks one half of the built-in pair (the app's entry and the switch port at its other end) and
    /// adds a plain reason to the list for every fault found.
    /// </summary>
    private static void CheckBuiltInPart(XotInterfaceConfig entry, PortConfig portConfig, bool dialSide, IReadOnlyDictionary<string, ITransportInterface>? transports,
        EmbeddedSwitchService builtInSwitch, List<string> problems)
    {
        string what = dialSide ? "link to the switch" : "link from the switch";
        ITransportInterface? transport = null;
        if (transports != null)
        {
            transports.TryGetValue(entry.Name, out transport);
        }

        if (transport == null || !(transport.Status == TransportStatus.Active || transport.Status == TransportStatus.Connected))
        {
            problems.Add("the app's XOT " + what + " is not running" + (transport != null ? " (" + transport.Status + ")" : string.Empty));
        }
        else if (dialSide)
        {
            TransportStatistics stats = transport.GetStatistics();
            if (stats.LastFailureUtc != null && (stats.LastConnectedUtc == null || stats.LastFailureUtc > stats.LastConnectedUtc))
            {
                problems.Add("the app could not connect to the switch: " + stats.LastError);
            }
        }

        ISwitchPort? running = FindRunning(builtInSwitch, portConfig.Id);
        if (running == null)
        {
            problems.Add("the switch is not running" + (builtInSwitch.Failure.Length > 0 ? ": " + builtInSwitch.Failure : string.Empty));
        }
        else if (!running.Active)
        {
            problems.Add("switch port " + portConfig.Id + " is not running");
        }
        else if (running is XotSwitchPort xot && portConfig.Mode == PortMode.Dte && !xot.PeerConnected
            && (xot.ConnectFailures > 0 || xot.Disconnects > 0))
        {
            problems.Add("switch port " + portConfig.Id + " cannot reach the app's listener: "
                + (xot.LastError.Length > 0 ? xot.LastError : "connection closed"));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Rows
    // ---------------------------------------------------------------------------------------------

    private static XotPeerRow DesktopRow(XotInterfaceConfig entry, ITransportInterface? transport, DateTime nowUtc)
    {
        if (entry.Mode == XotMode.Both)
        {
            return PeerRow(entry, transport as XotPeerTransport, nowUtc);
        }

        if (entry.Mode == XotMode.Client && transport is XotPeerTransport dialOnly)
        {
            return DialOnlyRow(entry, dialOnly, nowUtc);
        }

        bool client = entry.Mode == XotMode.Client;
        XotPeerRow row = new XotPeerRow
        {
            Name = entry.Name,
            Source = "Desktop link",
            Direction = client ? "we dial" : "we listen",
            Peer = client ? entry.RemoteIPAddress + ":" + entry.RemotePort : "on port " + entry.LocalPort,
            Behaviour = client
                ? "opens when a call needs it, no automatic retry"
                : "waits for the peer to dial in"
        };

        if (transport == null)
        {
            row.State = "NOT STARTED";
            row.Health = LinkHealth.Down;
            return row;
        }

        TransportStatistics stats = transport.GetStatistics();
        row.LastConnected = When(stats.LastConnectedUtc, nowUtc);
        row.FailedConnects = stats.ConnectionErrors;
        row.LastError = stats.LastError ?? string.Empty;
        row.Disconnects = stats.Disconnects;
        row.Traffic = "bytes in " + stats.BytesReceived + " / out " + stats.BytesTransmitted + ", packets: not counted here";
        row.Calls = client
            ? "calls placed " + stats.OutboundCallsAttempted + ", answered " + stats.OutboundCallsSucceeded
            : "calls received " + stats.InboundCallsReceived + ", accepted " + stats.InboundCallsAccepted;

        bool running = transport.Status == TransportStatus.Active || transport.Status == TransportStatus.Connected;
        if (!running)
        {
            row.State = "NOT RUNNING (" + transport.Status + ")";
            row.Health = LinkHealth.Down;
        }
        else if (!client)
        {
            row.State = stats.ActiveConnections > 0
                ? "listening, " + stats.ActiveConnections + " connected"
                : "listening, nobody connected";
            row.Health = stats.ActiveConnections > 0 ? LinkHealth.Up : LinkHealth.Checking;
        }
        else if (stats.ActiveConnections > 0)
        {
            row.State = "TCP connected";
            row.Health = LinkHealth.Up;
        }
        else if (stats.LastFailureUtc != null && (stats.LastConnectedUtc == null || stats.LastFailureUtc > stats.LastConnectedUtc))
        {
            row.State = "NOT connected: " + stats.LastError;
            row.Health = LinkHealth.Down;
        }
        else
        {
            row.State = stats.LastConnectedUtc != null
                ? "not connected now (the connection ended; opens again at the next call)"
                : "not connected yet (no call has needed it)";
            row.Health = LinkHealth.Checking;
        }

        return row;
    }

    /// <summary>
    /// The row of a "Client" entry: it only dials, and opens a new TCP connection for each call (RFC 1613 section 5).
    /// </summary>
    private static XotPeerRow DialOnlyRow(XotInterfaceConfig entry, XotPeerTransport transport, DateTime nowUtc)
    {
        XotPeerReport r = transport.GetPeerReport();
        XotPeerRow row = new XotPeerRow
        {
            Name = entry.Name,
            Source = "Desktop link",
            Direction = "we dial",
            Peer = entry.RemoteIPAddress + ":" + entry.RemotePort,
            Behaviour = "opens a new TCP connection for each call, no automatic retry",
            LastConnected = When(r.LastDialConnectedUtc, nowUtc),
            FailedConnects = r.DialFailures,
            LastError = r.LastDialError,
            Disconnects = r.DialDrops,
            Traffic = "bytes in " + r.DialBytesIn + " / out " + r.DialBytesOut + ", packets: not counted here",
            Calls = "calls placed " + r.CallsPlaced + ", connections opened " + r.DialedOpened + ", open now " + r.DialedOpenNow
        };

        if (transport.Status != TransportStatus.Active && transport.Status != TransportStatus.Connected)
        {
            row.State = "NOT RUNNING (" + transport.Status + ")";
            row.Health = LinkHealth.Down;
        }
        else if (r.DialedOpenNow > 0)
        {
            row.State = "TCP connected";
            row.Health = LinkHealth.Up;
        }
        else if (r.LastDialFailureUtc != null && (r.LastDialConnectedUtc == null || r.LastDialFailureUtc > r.LastDialConnectedUtc))
        {
            row.State = "NOT connected: " + r.LastDialError;
            row.Health = LinkHealth.Down;
        }
        else
        {
            row.State = r.LastDialConnectedUtc != null
                ? "not connected now (the connection ended; a new one is opened at the next call)"
                : "not connected yet (no call has needed it)";
            row.Health = LinkHealth.Checking;
        }

        return row;
    }

    /// <summary>
    /// The ONE row of a "Both ways" (peer) entry: both directions counted apart. We dial when a call
    /// needs it (one TCP connection per call, RFC 1613 section 5) and we accept connections from the peer.
    /// </summary>
    private static XotPeerRow PeerRow(XotInterfaceConfig entry, XotPeerTransport? transport, DateTime nowUtc)
    {
        string peerHost = string.IsNullOrWhiteSpace(entry.RemoteIPAddress) ? "(no address)" : entry.RemoteIPAddress!;
        XotPeerRow row = new XotPeerRow
        {
            Name = entry.Name,
            Source = "Desktop link",
            Direction = "dial out + accept in",
            Peer = peerHost + ":" + entry.RemotePort + " / listening on port " + entry.LocalPort,
            Behaviour = "dials a new TCP connection for each call, no automatic retry; accepts connections from " + peerHost
                + (entry.AllowOnlyListedPeers ? " only (other addresses are refused)" : " (other addresses are accepted as unlisted peers)")
        };

        if (transport == null)
        {
            row.State = "NOT STARTED";
            row.Health = LinkHealth.Down;
            return row;
        }

        XotPeerReport r = transport.GetPeerReport();
        row.LastConnected = When(r.LastDialConnectedUtc, nowUtc) + " out / " + When(r.LastAcceptedUtc, nowUtc) + " in";
        row.FailedConnects = r.DialFailures;
        row.LastError = r.LastDialError;
        row.Disconnects = r.DialDrops + r.AcceptDrops;
        string failed = r.DialFailures.ToString() + (r.LastDialError.Length > 0 ? " (last error: " + r.LastDialError + ")" : string.Empty);
        row.DetailOverride =
            "dialled out: " + r.DialedOpened + " opened, " + r.DialedOpenNow + " open now, failed " + failed + ", dropped " + r.DialDrops
            + ", bytes in " + r.DialBytesIn + " / out " + r.DialBytesOut + ", calls placed " + r.CallsPlaced
            + " | accepted in: " + r.Accepted + " connections, " + r.AcceptedOpenNow + " open now, dropped " + r.AcceptDrops
            + ", bytes in " + r.AcceptBytesIn + " / out " + r.AcceptBytesOut + ", calls received " + r.CallsReceived
            + (r.RefusedUnlisted > 0 ? ", refused (unlisted address) " + r.RefusedUnlisted : string.Empty)
            + " | last connected: " + row.LastConnected;

        bool running = transport.Status == TransportStatus.Active || transport.Status == TransportStatus.Connected;
        int open = r.DialedOpenNow + r.AcceptedOpenNow;
        if (!running)
        {
            row.State = "NOT RUNNING (" + transport.Status + ")";
            row.Health = LinkHealth.Down;
        }
        else if (open > 0)
        {
            row.State = "TCP connected: " + r.DialedOpenNow + " dialled out, " + r.AcceptedOpenNow + " accepted in";
            row.Health = LinkHealth.Up;
        }
        else if (r.LastDialFailureUtc != null && (r.LastDialConnectedUtc == null || r.LastDialFailureUtc > r.LastDialConnectedUtc))
        {
            row.State = "NOT connected: " + r.LastDialError;
            row.Health = LinkHealth.Down;
        }
        else
        {
            row.State = r.DialedOpened + r.Accepted > 0
                ? "no connection open now (one is opened for each call)"
                : "listening on port " + entry.LocalPort + "; nothing connected yet";
            row.Health = LinkHealth.Checking;
        }

        return row;
    }

    /// <summary>
    /// Adds a row "unlisted peer &lt;ip&gt;" for every source address that connected to a peer entry's port
    /// without being its listed peer.
    /// </summary>
    private static void AddUnlistedRows(List<XotPeerRow> rows, XotInterfaceConfig entry, XotPeerReport report, DateTime nowUtc)
    {
        for (int i = 0; i < report.Unlisted.Count; i++)
        {
            XotUnlistedPeerReport u = report.Unlisted[i];
            rows.Add(new XotPeerRow
            {
                Name = "unlisted peer " + u.Address,
                Source = "port " + entry.LocalPort + " of " + entry.Name,
                Direction = "dialled in",
                Peer = u.Address,
                State = u.OpenNow > 0 ? "TCP connected, " + u.OpenNow + " open" : "no connection open now",
                Behaviour = "this address is not listed as a peer; its connections are handled with the default settings",
                LastConnected = When(u.LastAcceptedUtc, nowUtc),
                Disconnects = u.Drops,
                Traffic = "bytes in " + u.BytesIn + " / out " + u.BytesOut,
                Calls = u.Accepted + " connections since start",
                Health = LinkHealth.Checking,
            });
        }
    }

    private static XotPeerRow SwitchRow(PortConfig config, ISwitchPort? running, EmbeddedSwitchService builtInSwitch, DateTime nowUtc)
    {
        bool dials = config.Mode == PortMode.Dte;
        string name = config.PeerName.Length > 0 ? config.PeerName : "port " + config.Id;
        XotPeerRow row = new XotPeerRow
        {
            Name = name,
            Source = "switch port " + config.Id,
            Direction = dials ? "we dial" : "we listen",
            Peer = dials ? config.Host + ":" + config.Port : "on " + (config.Host.Length > 0 ? config.Host : "0.0.0.0") + ":" + config.Port
        };

        XotSwitchPort? port = running as XotSwitchPort;
        double redialSeconds = port != null ? port.RedialDelay / 1000.0 : 2.0;
        row.Behaviour = dials
            ? "redials every " + redialSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s until it connects"
            : "waits for the peer to dial in";

        if (running == null)
        {
            row.State = "NOT RUNNING: the built-in switch is not running" + (builtInSwitch.Failure.Length > 0 ? " (" + builtInSwitch.Failure + ")" : string.Empty);
            row.Health = LinkHealth.Down;
            return row;
        }

        row.Calls = "calls out " + running.Counters.CallsOut + ", in " + running.Counters.CallsIn + ", cleared " + running.Counters.CallsCleared;
        // Data packets only (the definition is on PortCounters); the byte counts are every packet on the wire.
        row.Traffic = "data packets in " + running.Counters.PacketsIn + " / out " + running.Counters.PacketsOut
            + ", bytes in " + running.Counters.BytesIn + " / out " + running.Counters.BytesOut;

        if (port != null)
        {
            row.LastConnected = WhenTicks(port.LastConnectedMs);
            row.FailedConnects = port.ConnectFailures;
            row.LastError = port.LastError;
            row.Disconnects = port.Disconnects;
        }

        if (!running.Active)
        {
            row.State = "NOT RUNNING (port is stopped)";
            row.Health = LinkHealth.Down;
        }
        else if (dials)
        {
            if (running.PeerConnected)
            {
                row.State = "TCP connected";
                row.Health = LinkHealth.Up;
            }
            else if (port != null && (port.ConnectFailures > 0 || port.Disconnects > 0))
            {
                row.State = "NOT connected" + (port.LastError.Length > 0 ? ": " + port.LastError : string.Empty);
                row.Health = LinkHealth.Down;
            }
            else
            {
                row.State = "connecting...";
                row.Health = LinkHealth.Checking;
            }
        }
        else
        {
            int connections = port != null ? port.ConnectionCount : (running.PeerConnected ? 1 : 0);
            row.State = connections > 0 ? "listening, " + connections + " connected" : "listening, nobody connected";
            row.Health = connections > 0 ? LinkHealth.Up : LinkHealth.Checking;
        }

        return row;
    }

    /// <summary>
    /// The row of an HDLC trunk port (LAPB over 0x7E framing on TCP or on a serial line) of the built-in switch.
    /// </summary>
    /// <remarks>
    /// "Up" here means the LAPB link is up (SABM answered by UA), not only that the line is open: a TCP connection
    /// or an open serial device with a dead or silent peer is not a working trunk. The numbers come from the port
    /// itself (<see cref="HdlcSwitchPortBase.GetLinkSnapshot"/> and the connection counts), not from a guess.
    /// </remarks>
    private static XotPeerRow HdlcRow(PortConfig config, ISwitchPort? running, EmbeddedSwitchService builtInSwitch, DateTime nowUtc)
    {
        bool serial = config.Transport == PortTransport.HdlcSerial;
        bool dials = !serial && config.Mode == PortMode.Dte;
        string name = config.PeerName.Length > 0 ? config.PeerName : "port " + config.Id;
        string medium = serial ? "HDLC over serial" : "HDLC over TCP";
        XotPeerRow row = new XotPeerRow
        {
            Name = name,
            Source = "switch port " + config.Id,
            Direction = serial ? "serial line" : dials ? "we dial" : "we listen",
            Peer = serial
                ? config.SerialDevice + " at " + config.SerialBaud + " baud"
                : dials ? config.Host + ":" + config.Port : "on " + (config.Host.Length > 0 ? config.Host : "0.0.0.0") + ":" + config.Port
        };

        HdlcSwitchPortBase? port = running as HdlcSwitchPortBase;
        double redialSeconds = port != null ? port.RedialDelay / 1000.0 : 2.0;
        string redial = redialSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        row.Behaviour = serial
            ? medium + ", " + (config.Mode == PortMode.Dce ? "DCE" : "DTE") + "; opens the device again every " + redial + " s if it goes away"
            : dials
                ? medium + ", DTE; redials every " + redial + " s until it connects"
                : medium + ", DCE; waits for the peer to dial in";

        if (running == null)
        {
            row.State = "NOT RUNNING: the built-in switch is not running" + (builtInSwitch.Failure.Length > 0 ? " (" + builtInSwitch.Failure + ")" : string.Empty);
            row.Health = LinkHealth.Down;
            return row;
        }

        if (port == null)
        {
            // A running port of another kind under an HDLC configuration cannot happen with the factory; say so
            // instead of showing numbers that are not there.
            row.State = "NOT RUNNING: the port is not an HDLC port";
            row.Health = LinkHealth.Down;
            return row;
        }

        HdlcLinkSnapshot link = port.GetLinkSnapshot();
        row.LastConnected = WhenTicks(link.LastLinkUpMs);
        row.FailedConnects = port.ConnectFailures;
        row.LastError = port.LastError;
        row.Disconnects = port.Disconnects;
        row.Traffic = "bytes in " + link.WireBytesIn + " / out " + link.WireBytesOut;
        row.Calls = "calls out " + running.Counters.CallsOut + ", in " + running.Counters.CallsIn;
        row.DetailOverride =
            "last link up: " + row.LastConnected + ", link ups " + link.LinkUps + ", link resets " + link.LinkResets
            + " | " + (serial ? "failed opens " : "failed dials ") + port.ConnectFailures
            + (port.LastError.Length > 0 ? " (last error: " + port.LastError + ")" : string.Empty)
            + ", line drops " + port.Disconnects
            + " | frames in " + link.FramesIn + " / out " + link.FramesOut + ", retransmits " + link.Retransmissions
            + ", REJ sent " + link.RejectsSent + " / received " + link.RejectsReceived
            + ", bad frames (FCS) " + link.FcsErrors
            + " | bytes in " + link.WireBytesIn + " / out " + link.WireBytesOut
            + " | calls out " + running.Counters.CallsOut + ", in " + running.Counters.CallsIn;

        if (!running.Active)
        {
            row.State = "NOT RUNNING (port is stopped)";
            row.Health = LinkHealth.Down;
        }
        else if (link.LinkUp)
        {
            row.State = "LAPB link up";
            row.Health = LinkHealth.Up;
        }
        else if (link.LineOpen)
        {
            row.State = link.LinkUps > 0
                ? "line open, LAPB link DOWN (" + link.LapbState + ")"
                : "line open, LAPB link not up yet (" + link.LapbState + ")";
            row.Health = link.LinkUps > 0 ? LinkHealth.Down : LinkHealth.Checking;
        }
        else if (port.ConnectFailures > 0 || port.Disconnects > 0)
        {
            row.State = (serial ? "NOT open" : "NOT connected") + (port.LastError.Length > 0 ? ": " + port.LastError : string.Empty);
            row.Health = LinkHealth.Down;
        }
        else if (dials || serial)
        {
            row.State = serial ? "opening the device..." : "connecting...";
            row.Health = LinkHealth.Checking;
        }
        else
        {
            row.State = "listening, nobody connected";
            row.Health = LinkHealth.Checking;
        }

        return row;
    }

    // ---------------------------------------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------------------------------------

    private static ISwitchPort? FindRunning(EmbeddedSwitchService builtInSwitch, int id)
    {
        IReadOnlyList<ISwitchPort> running = builtInSwitch.RunningPorts;
        for (int i = 0; i < running.Count; i++)
        {
            if (running[i].Id == id)
            {
                return running[i];
            }
        }

        return null;
    }

    /// <summary>True when the host text is a loopback address (127.x.x.x, ::1) or the name localhost.</summary>
    internal static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (string.Equals(host.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim(), out IPAddress? address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// True when a port bound to this host text can be reached over loopback: it is a loopback address,
    /// or it is empty / 0.0.0.0 / :: which means every address.
    /// </summary>
    internal static bool ListensOnLoopback(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        string h = host.Trim();
        return h == "0.0.0.0" || h == "::" || IsLoopbackHost(h);
    }

    private static string When(DateTime? utc, DateTime nowUtc)
    {
        if (utc == null)
        {
            return "never";
        }

        return utc.Value.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " (" + Ago(nowUtc - utc.Value) + ")";
    }

    private static string WhenTicks(long tickMs)
    {
        if (tickMs == 0)
        {
            return "never";
        }

        long agoMs = Math.Max(0, Environment.TickCount64 - tickMs);
        DateTime utc = DateTime.UtcNow - TimeSpan.FromMilliseconds(agoMs);
        return utc.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " (" + Ago(TimeSpan.FromMilliseconds(agoMs)) + ")";
    }

    private static string Ago(TimeSpan span)
    {
        if (span.TotalSeconds < 1)
        {
            return "just now";
        }

        if (span.TotalMinutes < 1)
        {
            return (int)span.TotalSeconds + " s ago";
        }

        if (span.TotalHours < 1)
        {
            return (int)span.TotalMinutes + " min ago";
        }

        return (int)span.TotalHours + " h ago";
    }
}
