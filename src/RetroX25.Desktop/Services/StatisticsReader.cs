//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Globalization;
using RetroIO.Link;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Protocols.X25.Runtime.Transport;
using RetroIO.Protocols.X25.Transport;

// The engine has two classes called LapbStatistics: the runtime-wide one (in StatisticsSnapshot)
// and the one a single LAPB link keeps. These names keep them apart.
using LinkLapbStatistics = RetroIO.Link.LapbStatistics;

namespace RetroX25.Desktop.Services;

/// <summary>
/// One row of the statistics table: a counter, with one cell for each column.
/// </summary>
public sealed class StatisticsRow
{
    /// <summary>
    /// Creates a row.
    /// </summary>
    /// <param name="section">The group the row belongs to (HDLC, LAPB, X.25 or the per-link group).</param>
    /// <param name="label">The name of the counter, in plain words.</param>
    /// <param name="isProblem">True for a counter that counts something going wrong (shown in red).</param>
    /// <param name="columns">The number of columns of the table.</param>
    public StatisticsRow(string section, string label, bool isProblem, int columns)
    {
        Section = section;
        Label = label;
        IsProblem = isProblem;
        Cells = new string[columns];
        for (int i = 0; i < Cells.Length; i++)
        {
            Cells[i] = StatisticsReader.NotAvailable;
        }
    }

    /// <summary>Gets the group the row belongs to.</summary>
    public string Section { get; }

    /// <summary>Gets the name of the counter.</summary>
    public string Label { get; }

    /// <summary>Gets a value indicating whether the counter counts something going wrong.</summary>
    public bool IsProblem { get; }

    /// <summary>
    /// Gets the cells of the row, one for each column of the table (column 0 is the total).
    /// A cell holds the number as text, or <see cref="StatisticsReader.NotAvailable"/> when the
    /// column has no such counter.
    /// </summary>
    public string[] Cells { get; }
}

/// <summary>
/// The statistics table: rows are counters, columns are the total and then each link and each
/// transport interface of the engine.
/// </summary>
public sealed class StatisticsTable
{
    /// <summary>Gets the header of each column: "Total" first, then the name of each link or interface.</summary>
    public List<string> Headers { get; } = new List<string>();

    /// <summary>Gets a second header line for each column: what kind of link or interface it is.</summary>
    public List<string> Kinds { get; } = new List<string>();

    /// <summary>Gets the rows of the table.</summary>
    public List<StatisticsRow> Rows { get; } = new List<StatisticsRow>();

    /// <summary>Gets or sets the engine's uptime as text, or an empty text when there is no engine.</summary>
    public string UptimeText { get; set; } = string.Empty;

    /// <summary>
    /// Finds the column whose header is <paramref name="header"/>.
    /// </summary>
    /// <param name="header">The header text, for example "Total" or a link name.</param>
    /// <returns>The index of the column, or -1 when there is none.</returns>
    public int ColumnIndex(string header)
    {
        for (int i = 0; i < Headers.Count; i++)
        {
            if (string.Equals(Headers[i], header, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the row whose label is <paramref name="label"/>.
    /// </summary>
    /// <param name="label">The counter name.</param>
    /// <returns>The row, or null when there is none.</returns>
    public StatisticsRow? FindRow(string label)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (string.Equals(Rows[i].Label, label, StringComparison.Ordinal))
            {
                return Rows[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Reads one cell.
    /// </summary>
    /// <param name="label">The counter name (the row).</param>
    /// <param name="header">The column header ("Total" or a link or interface name).</param>
    /// <returns>The cell text, or null when the row or the column does not exist.</returns>
    public string? Cell(string label, string header)
    {
        StatisticsRow? row = FindRow(label);
        int column = ColumnIndex(header);
        if (row == null || column < 0)
        {
            return null;
        }

        return row.Cells[column];
    }
}

/// <summary>
/// Builds the statistics table from the running engine. It is the only place that turns the engine's
/// counters into figures for a screen, so every screen that shows a counter shows the same number.
/// </summary>
/// <remarks>
/// <para>
/// Column "Total" comes from the engine's own runtime-wide counters (<c>GetStatistics()</c>). Each
/// TCP-LAPB link gets a column with its LAPB frame counts, retransmissions, timeouts and wire
/// counters; each transport interface (a serial line or an XOT interface) gets a column with its
/// bytes, calls and errors.
/// </para>
/// <para>
/// A link and a transport interface are different objects, so a byte is never counted twice in the
/// totals of the last group of rows. If a link and an interface ever share a name they are the same
/// wire, and then only the link is added into that total.
/// </para>
/// <para>
/// The engine does not count LAPB retransmissions or timeouts in its runtime-wide counters (only each
/// link counts its own), so the Total cell of those two rows is the sum of the link columns.
/// </para>
/// </remarks>
public static class StatisticsReader
{
    /// <summary>The text of a cell whose column has no such counter.</summary>
    public const string NotAvailable = "-";

    // Section names, also used as headings in the window.
    private const string Hdlc = "HDLC (frames on the wire)";
    private const string Lapb = "LAPB (data link)";
    private const string X25 = "X.25 (packets and calls)";
    private const string PerLink = "Per link and interface";
    private const string Accounts = "Accounts (built-in switch)";

    /// <summary>
    /// Reads the engine and builds the table.
    /// </summary>
    /// <param name="runtime">The running engine.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runtime"/> is null.</exception>
    public static StatisticsTable Build(ProtocolRuntime runtime)
    {
        if (runtime == null)
        {
            throw new ArgumentNullException(nameof(runtime));
        }

        List<ITransportInterface> interfaceList = new List<ITransportInterface>(runtime.TransportInterfaces.Count);
        foreach (KeyValuePair<string, ITransportInterface> pair in runtime.TransportInterfaces)
        {
            interfaceList.Add(pair.Value);
        }

        return Build(runtime.GetStatistics(), runtime.GetAllLinks(), interfaceList);
    }

    /// <summary>
    /// Builds the table from figures that were already read. <see cref="Build(ProtocolRuntime)"/> is
    /// this with the engine's own figures; a test can pass figures it made itself.
    /// </summary>
    /// <param name="total">The engine's runtime-wide counters.</param>
    /// <param name="links">The links of the engine's link manager (TCP-LAPB links).</param>
    /// <param name="interfaceList">The transport interfaces (serial lines and XOT interfaces).</param>
    /// <returns>The table.</returns>
    public static StatisticsTable Build(StatisticsSnapshot total, ILink[] links, IReadOnlyList<ITransportInterface> interfaceList)
    {
        StatisticsTable table = new StatisticsTable();
        table.UptimeText = FormatTimeSpan(total.Uptime);

        // Columns: Total, then the links, then the interfaces.
        table.Headers.Add("Total");
        table.Kinds.Add("whole engine");
        for (int i = 0; i < links.Length; i++)
        {
            table.Headers.Add(links[i].Name);
            table.Kinds.Add(DescribeLinkKind(links[i]));
        }

        for (int i = 0; i < interfaceList.Count; i++)
        {
            table.Headers.Add(interfaceList[i].Name);
            table.Kinds.Add(DescribeInterfaceKind(interfaceList[i]));
        }

        int firstLink = 1;
        int firstInterface = 1 + links.Length;

        // ---- HDLC: runtime-wide only ----
        AddRow(table, Hdlc, "HDLC frames received", false)[0] = total.HDLC.FramesReceived.ToString(CultureInfo.InvariantCulture);
        AddRow(table, Hdlc, "HDLC frames sent", false)[0] = total.HDLC.FramesTransmitted.ToString(CultureInfo.InvariantCulture);
        AddRow(table, Hdlc, "HDLC bytes received", false)[0] = total.HDLC.BytesReceived.ToString(CultureInfo.InvariantCulture);
        AddRow(table, Hdlc, "HDLC bytes sent", false)[0] = total.HDLC.BytesTransmitted.ToString(CultureInfo.InvariantCulture);
        AddRow(table, Hdlc, "HDLC bad frames received", true)[0] = total.HDLC.BadFramesReceived.ToString(CultureInfo.InvariantCulture);
        AddRow(table, Hdlc, "HDLC bad frames sent", true)[0] = total.HDLC.BadFramesTransmitted.ToString(CultureInfo.InvariantCulture);
        AddRow(table, Hdlc, "HDLC frames with bad FCS", true)[0] = total.HDLC.FramesInvalidFcs.ToString(CultureInfo.InvariantCulture);

        // ---- LAPB: Total from the runtime, link columns from each link's own LAPB ----
        string[] iRx = AddRow(table, Lapb, "LAPB I frames received", false);
        string[] iTx = AddRow(table, Lapb, "LAPB I frames sent", false);
        string[] sRx = AddRow(table, Lapb, "LAPB S frames received", false);
        string[] sTx = AddRow(table, Lapb, "LAPB S frames sent", false);
        string[] uRx = AddRow(table, Lapb, "LAPB U frames received", false);
        string[] uTx = AddRow(table, Lapb, "LAPB U frames sent", false);
        string[] retx = AddRow(table, Lapb, "LAPB retransmissions", true);
        string[] timeouts = AddRow(table, Lapb, "LAPB timeouts (T1 and T2)", true);
        string[] badRx = AddRow(table, Lapb, "LAPB bad frames received", true);
        string[] badTx = AddRow(table, Lapb, "LAPB bad frames sent", true);
        string[] state = AddRow(table, Lapb, "LAPB state", false);

        iRx[0] = total.LAPB.IFramesReceived.ToString(CultureInfo.InvariantCulture);
        iTx[0] = total.LAPB.IFramesTransmitted.ToString(CultureInfo.InvariantCulture);
        sRx[0] = total.LAPB.SFramesReceived.ToString(CultureInfo.InvariantCulture);
        sTx[0] = total.LAPB.SFramesTransmitted.ToString(CultureInfo.InvariantCulture);
        uRx[0] = total.LAPB.UFramesReceived.ToString(CultureInfo.InvariantCulture);
        uTx[0] = total.LAPB.UFramesTransmitted.ToString(CultureInfo.InvariantCulture);
        badRx[0] = total.LAPB.BadFramesReceived.ToString(CultureInfo.InvariantCulture);
        badTx[0] = total.LAPB.BadFramesTransmitted.ToString(CultureInfo.InvariantCulture);
        // There is no runtime-wide LAPB state: each link has its own, so the Total cell stays "-".

        // ---- X.25: runtime-wide only ----
        AddRow(table, X25, "X.25 packets received", false)[0] = total.X25.PacketsReceived.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 packets sent", false)[0] = total.X25.PacketsTransmitted.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 call requests received", false)[0] = total.X25.CallRequestsReceived.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 call requests sent", false)[0] = total.X25.CallRequestsTransmitted.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 data packets received", false)[0] = total.X25.DataPacketsReceived.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 data packets sent", false)[0] = total.X25.DataPacketsTransmitted.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 active virtual circuits", false)[0] = total.X25.ActiveVirtualCircuits.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 virtual circuits in total", false)[0] = total.X25.TotalVirtualCircuits.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 bad packets received", true)[0] = total.X25.BadPacketsReceived.ToString(CultureInfo.InvariantCulture);
        AddRow(table, X25, "X.25 bad packets sent", true)[0] = total.X25.BadPacketsTransmitted.ToString(CultureInfo.InvariantCulture);

        // ---- Per link and per interface ----
        string[] bytesRx = AddRow(table, PerLink, "Bytes received", false);
        string[] bytesTx = AddRow(table, PerLink, "Bytes sent", false);
        string[] framesRx = AddRow(table, PerLink, "Link frames received", false);
        string[] framesTx = AddRow(table, PerLink, "Link frames sent", false);
        string[] linkErrors = AddRow(table, PerLink, "Link errors", true);
        string[] openConnections = AddRow(table, PerLink, "Open connections", false);
        string[] outTried = AddRow(table, PerLink, "Outbound calls tried", false);
        string[] outWorked = AddRow(table, PerLink, "Outbound calls that worked", false);
        string[] inReceived = AddRow(table, PerLink, "Inbound calls received", false);
        string[] inAccepted = AddRow(table, PerLink, "Inbound calls accepted", false);
        string[] inRefused = AddRow(table, PerLink, "Inbound calls refused", true);
        string[] connErrors = AddRow(table, PerLink, "Connection errors", true);
        string[] timeoutErrors = AddRow(table, PerLink, "Timeout errors", true);
        string[] protocolErrors = AddRow(table, PerLink, "Protocol errors", true);

        long sumRetx = 0;
        long sumTimeouts = 0;
        long sumBytesRx = 0;
        long sumBytesTx = 0;
        long sumFramesRx = 0;
        long sumFramesTx = 0;
        long sumLinkErrors = 0;
        long sumOpen = 0;
        long sumOutTried = 0;
        long sumOutWorked = 0;
        long sumInReceived = 0;
        long sumInAccepted = 0;
        long sumInRefused = 0;
        long sumConn = 0;
        long sumTimeoutErrors = 0;
        long sumProtocol = 0;

        for (int i = 0; i < links.Length; i++)
        {
            int c = firstLink + i;
            ILink link = links[i];

            LinkStatistics stats = link.GetStatistics();
            bytesRx[c] = Number(stats.BytesReceived);
            bytesTx[c] = Number(stats.BytesTransmitted);
            framesRx[c] = Number(stats.FramesReceived);
            framesTx[c] = Number(stats.FramesTransmitted);
            linkErrors[c] = Number(stats.Errors);
            sumBytesRx += stats.BytesReceived;
            sumBytesTx += stats.BytesTransmitted;
            sumFramesRx += stats.FramesReceived;
            sumFramesTx += stats.FramesTransmitted;
            sumLinkErrors += stats.Errors;

            if (link is ILapbCapable lapb)
            {
                LinkLapbStatistics lapbStats = lapb.GetLapbStatistics();
                iRx[c] = Number(lapbStats.IFramesReceived);
                iTx[c] = Number(lapbStats.IFramesSent);
                sRx[c] = Number(lapbStats.SFramesReceived);
                sTx[c] = Number(lapbStats.SFramesSent);
                uRx[c] = Number(lapbStats.UFramesReceived);
                uTx[c] = Number(lapbStats.UFramesSent);
                retx[c] = Number(lapbStats.Retransmissions);
                long linkTimeouts = lapbStats.T1Timeouts + lapbStats.T2Timeouts;
                timeouts[c] = Number(linkTimeouts);
                state[c] = lapb.LapbState.ToString();
                sumRetx += lapbStats.Retransmissions;
                sumTimeouts += linkTimeouts;
            }
        }

        for (int i = 0; i < interfaceList.Count; i++)
        {
            int c = firstInterface + i;
            ITransportInterface transport = interfaceList[i];

            // A serial line runs its own LAPB link. The engine shows only whether it is up; it does not
            // give out that link's frame counts, so those cells stay "-".
            state[c] = DescribeSerialLapbState(transport);

            TransportStatistics stats = transport.GetStatistics();
            bytesRx[c] = Number(stats.BytesReceived);
            bytesTx[c] = Number(stats.BytesTransmitted);
            openConnections[c] = Number(stats.ActiveConnections);
            outTried[c] = Number(stats.OutboundCallsAttempted);
            outWorked[c] = Number(stats.OutboundCallsSucceeded);
            inReceived[c] = Number(stats.InboundCallsReceived);
            inAccepted[c] = Number(stats.InboundCallsAccepted);
            inRefused[c] = Number(stats.InboundCallsRejected);
            connErrors[c] = Number(stats.ConnectionErrors);
            timeoutErrors[c] = Number(stats.TimeoutErrors);
            protocolErrors[c] = Number(stats.ProtocolErrors);

            sumOpen += stats.ActiveConnections;
            sumOutTried += stats.OutboundCallsAttempted;
            sumOutWorked += stats.OutboundCallsSucceeded;
            sumInReceived += stats.InboundCallsReceived;
            sumInAccepted += stats.InboundCallsAccepted;
            sumInRefused += stats.InboundCallsRejected;
            sumConn += stats.ConnectionErrors;
            sumTimeoutErrors += stats.TimeoutErrors;
            sumProtocol += stats.ProtocolErrors;

            // Same name as a link means the same wire: its bytes are already in the link's figures.
            if (!HasLinkNamed(links, transport.Name))
            {
                sumBytesRx += stats.BytesReceived;
                sumBytesTx += stats.BytesTransmitted;
            }
        }

        // The engine keeps no runtime-wide retransmission or timeout count, so the total is the sum of
        // the links' own counts (zero when there is no link with LAPB).
        retx[0] = Number(sumRetx);
        timeouts[0] = Number(sumTimeouts);

        bytesRx[0] = Number(sumBytesRx);
        bytesTx[0] = Number(sumBytesTx);
        if (links.Length > 0)
        {
            framesRx[0] = Number(sumFramesRx);
            framesTx[0] = Number(sumFramesTx);
            linkErrors[0] = Number(sumLinkErrors);
        }

        if (interfaceList.Count > 0)
        {
            openConnections[0] = Number(sumOpen);
            outTried[0] = Number(sumOutTried);
            outWorked[0] = Number(sumOutWorked);
            inReceived[0] = Number(sumInReceived);
            inAccepted[0] = Number(sumInAccepted);
            inRefused[0] = Number(sumInRefused);
            connErrors[0] = Number(sumConn);
            timeoutErrors[0] = Number(sumTimeoutErrors);
            protocolErrors[0] = Number(sumProtocol);
        }

        return table;
    }

    /// <summary>
    /// Builds the billing table of the built-in switch: one column for each account, one row for each figure. The
    /// figures are the switch's own account totals, the same ones <c>show x25 account</c> and <c>GET_ACCOUNTS</c> give,
    /// so the window, the console and the record file agree (see docs/billing.md).
    /// </summary>
    /// <remarks>
    /// These are the bill. "Reset Statistics" resets the engine's counters, not these: a bill is never zeroed from a
    /// screen.
    /// </remarks>
    /// <param name="accounts">The switch's accounts, in their order; empty for none.</param>
    /// <returns>The table; it has no columns when there are no accounts.</returns>
    public static StatisticsTable BuildAccounts(IReadOnlyList<RetroIO.Protocols.X25.Switch.Model.SwitchAccount> accounts)
    {
        StatisticsTable table = new StatisticsTable();
        for (int i = 0; i < accounts.Count; i++)
        {
            RetroIO.Protocols.X25.Switch.Model.SwitchAccount a = accounts[i];
            table.Headers.Add(a.Name);
            table.Kinds.Add(a.Nui.Length > 0 ? "NUI " + a.Nui : a.AddrStart + " to " + a.AddrEnd);
        }

        string[] up = AddRow(table, Accounts, "Calls up now", false);
        string[] made = AddRow(table, Accounts, "Calls made", false);
        string[] received = AddRow(table, Accounts, "Calls received", false);
        string[] pSent = AddRow(table, Accounts, "Packets sent", false);
        string[] pRecv = AddRow(table, Accounts, "Packets received", false);
        string[] bSent = AddRow(table, Accounts, "Bytes sent", false);
        string[] bRecv = AddRow(table, Accounts, "Bytes received", false);
        string[] sSent = AddRow(table, Accounts, "Segments sent", false);
        string[] sRecv = AddRow(table, Accounts, "Segments received", false);
        string[] time = AddRow(table, Accounts, "Call time", false);
        for (int i = 0; i < accounts.Count; i++)
        {
            RetroIO.Protocols.X25.Switch.Model.SwitchAccount a = accounts[i];
            up[i] = Number(a.CallsActive);
            made[i] = Number(a.CallsMade);
            received[i] = Number(a.CallsReceived);
            pSent[i] = Number((long)a.PacketsSent);
            pRecv[i] = Number((long)a.PacketsReceived);
            bSent[i] = Number((long)a.BytesSent);
            bRecv[i] = Number((long)a.BytesReceived);
            sSent[i] = Number((long)a.SegmentsSent);
            sRecv[i] = Number((long)a.SegmentsReceived);
            time[i] = FormatTimeSpan(TimeSpan.FromMilliseconds(a.TotalDurationMs));
        }

        return table;
    }

    /// <summary>
    /// Formats a span of time for a screen, such as "3h 12m", "4m 9s" or "17s".
    /// </summary>
    /// <param name="span">The time.</param>
    /// <returns>The text.</returns>
    public static string FormatTimeSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return (int)span.TotalHours + "h " + span.Minutes + "m";
        }

        if (span.TotalMinutes >= 1)
        {
            return (int)span.TotalMinutes + "m " + span.Seconds + "s";
        }

        return (int)span.TotalSeconds + "s";
    }

    // Adds a row with "-" in every cell and returns its cells so the caller can fill them in.
    private static string[] AddRow(StatisticsTable table, string section, string label, bool isProblem)
    {
        StatisticsRow row = new StatisticsRow(section, label, isProblem, table.Headers.Count);
        table.Rows.Add(row);
        return row.Cells;
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool HasLinkNamed(ILink[] links, string name)
    {
        for (int i = 0; i < links.Length; i++)
        {
            if (string.Equals(links[i].Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeLinkKind(ILink link)
    {
        return link.Type switch
        {
            LinkType.TcpLapb => "TCP-LAPB link",
            LinkType.Xot => "XOT link",
            LinkType.Serial => "serial link",
            _ => "link"
        };
    }

    private static string DescribeInterfaceKind(ITransportInterface transport)
    {
        return transport.Type switch
        {
            TransportType.XOT_Client => "XOT client",
            TransportType.XOT_Server => "XOT server",
            TransportType.Serial => "serial line",
            _ => "interface"
        };
    }

    // The LAPB state of a serial line, in a word. "-" for an interface that has no LAPB link of its own.
    private static string DescribeSerialLapbState(ITransportInterface transport)
    {
        if (transport is SerialTransport serial && serial.LinkRole != null)
        {
            return serial.IsLinkUp ? "DataTransfer" : "Down";
        }

        return NotAvailable;
    }
}
