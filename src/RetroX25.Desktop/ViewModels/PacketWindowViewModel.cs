//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Models;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Packet Window (Ctrl+2) - displays protocol packet capture with filtering.
/// </summary>
/// <remarks>
/// <para>
/// Every line is a <see cref="PacketRowViewModel"/>: the runtime's history item plus the frame
/// class, packet type, channel number, sequence numbers, flags and check result read out of it.
/// The filters (direction, layer, LAPB frame class, X.25 packet group, channel number, errors only,
/// collapse) work on those values.
/// </para>
/// <para>
/// Everything that touches the list runs on the UI thread. The runtime reports a new packet on its own
/// thread; the view-model posts it over.
/// </para>
/// </remarks>
public partial class PacketWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;
    private readonly PacketViewState _viewState;

    // Every line made so far that is newer than the clear mark, whether or not a filter shows it.
    // Filter changes rebuild the visible list from here without asking the runtime again.
    private readonly List<PacketRowViewModel> _allRows = new();
    private readonly HashSet<long> _knownIds = new();

    // Observable collections for display
    [ObservableProperty]
    private ObservableCollection<PacketRowViewModel> _packets = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPacketSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedPacketDetail))]
    [NotifyPropertyChangedFor(nameof(SelectedPacketHex))]
    [NotifyPropertyChangedFor(nameof(SelectedPacketAscii))]
    private PacketRowViewModel? _selectedPacket;

    /// <summary>
    /// Get summary of selected packet.
    /// </summary>
    public string SelectedPacketSummary => SelectedPacket?.Item.GetSummary() ?? "";

    /// <summary>
    /// Gets the decoded detail text of the selected packet: the fields, the cause and diagnostic with
    /// their meaning, the facilities as hex and any parse error. Empty when nothing is selected.
    /// </summary>
    public string SelectedPacketDetail => SelectedPacket?.BuildDetailText() ?? "";

    /// <summary>
    /// Get hex dump of selected packet.
    /// </summary>
    public string SelectedPacketHex => SelectedPacket?.Item.GetPayloadHex() ?? "";

    /// <summary>
    /// Get ASCII representation of selected packet.
    /// </summary>
    public string SelectedPacketAscii => PrintableText(SelectedPacket?.Item.Payload);

    // The payload as text with every byte that is not a printable ASCII character shown as a dot, so
    // the line never fills with boxes for control bytes.
    private static string PrintableText(byte[]? payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return string.Empty;
        }

        char[] chars = new char[payload.Length];
        for (int i = 0; i < payload.Length; i++)
        {
            chars[i] = payload[i] >= 32 && payload[i] < 127 ? (char)payload[i] : (char)46;
        }

        return new string(chars);
    }

    /// <summary>
    /// Gets the detail tree of the selected packet (Wireshark-style: a heading per layer with its
    /// fields folded under it). Empty when nothing is selected.
    /// </summary>
    public ObservableCollection<PacketTreeNode> DetailTree { get; } = new();

    /// <summary>
    /// Gets or sets the tree line the user has clicked. The bytes it covers are marked in
    /// <see cref="HexBytes"/>.
    /// </summary>
    [ObservableProperty]
    private PacketTreeNode? _selectedNode;

    /// <summary>
    /// Gets the payload of the selected packet, one entry per byte, with the bytes of the selected
    /// tree line marked.
    /// </summary>
    public ObservableCollection<HexByteViewModel> HexBytes { get; } = new();

    // Rebuilds the tree and the byte list whenever another packet is selected.
    partial void OnSelectedPacketChanged(PacketRowViewModel? value)
    {
        DetailTree.Clear();
        HexBytes.Clear();
        SelectedNode = null;
        if (value == null)
        {
            return;
        }

        List<PacketTreeNode> roots = value.BuildDetailTree();
        for (int i = 0; i < roots.Count; i++)
        {
            DetailTree.Add(roots[i]);
        }

        byte[] payload = value.Item.Payload;
        for (int i = 0; i < payload.Length; i++)
        {
            HexBytes.Add(new HexByteViewModel(payload[i].ToString("X2")));
        }
    }

    // Marks the bytes of the clicked tree line, and only those.
    partial void OnSelectedNodeChanged(PacketTreeNode? value)
    {
        int start = value != null && value.HasBytes ? value.ByteStart : -1;
        int end = start >= 0 ? start + value!.ByteLength : -1;
        for (int i = 0; i < HexBytes.Count; i++)
        {
            HexBytes[i].IsHighlighted = i >= start && i < end;
        }
    }

    // Filter properties
    [ObservableProperty]
    private bool _showTx = true;

    [ObservableProperty]
    private bool _showRx = true;

    [ObservableProperty]
    private bool _showHdlc = true;

    [ObservableProperty]
    private bool _showLapb = true;

    [ObservableProperty]
    private bool _showX25 = true;

    // LAPB frame class filters (console: [I] [S] [U])
    [ObservableProperty]
    private bool _showIFrames = true;

    [ObservableProperty]
    private bool _showSFrames = true;

    [ObservableProperty]
    private bool _showUFrames = true;

    // X.25 packet group filters (console: [DATA] [CALL] [CLEAR] [RESET] [RESTART] [INT])
    [ObservableProperty]
    private bool _showDataPackets = true;

    [ObservableProperty]
    private bool _showCallPackets = true;

    [ObservableProperty]
    private bool _showClearPackets = true;

    [ObservableProperty]
    private bool _showResetPackets = true;

    [ObservableProperty]
    private bool _showRestartPackets = true;

    [ObservableProperty]
    private bool _showInterruptPackets = true;

    /// <summary>
    /// Gets or sets the logical channel number to show, as text. Empty shows every channel; with a
    /// number, only X.25 packets on that channel are shown (LAPB and HDLC lines have no channel).
    /// </summary>
    [ObservableProperty]
    private string _lcnFilterText = "";

    [ObservableProperty]
    private bool _errorsOnly;

    [ObservableProperty]
    private bool _isCollapsed = true; // Default to collapsed view

    [ObservableProperty]
    private string _statusText = "0 packets in this capture";

    // Counts of the packets captured in THIS window (not the engine's running totals, which are in the
    // Status window). The text starts with "In this capture" so nobody reads them as totals.
    [ObservableProperty]
    private string _statisticsText = "In this capture: TX: 0 | RX: 0 | HDLC: 0 | LAPB: 0 | X.25: 0 | Hidden: 0 | Errors: 0";

    /// <summary>
    /// Creates PacketWindowViewModel with dependency injection.
    /// </summary>
    public PacketWindowViewModel(
        ILogger<PacketWindowViewModel> logger,
        IRuntimeService runtimeService,
        PacketViewState viewState) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _viewState = viewState ?? throw new ArgumentNullException(nameof(viewState));

        _viewState.Cleared += OnViewCleared;

        // Subscribe to ProtocolRuntime events
        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.HistoryItemAdded += OnHistoryItemAdded;
            ReloadFromRuntime();
        }

        Logger.LogInformation("PacketWindowViewModel initialized");
    }

    /// <summary>
    /// Gets the logical channel number typed into the channel filter, or null when the box is empty
    /// or does not hold a number from 0 to 4095 (then no channel filter is applied).
    /// </summary>
    public int? LcnFilterValue
    {
        get
        {
            if (int.TryParse(LcnFilterText?.Trim(), out int value) && value >= 0 && value <= 4095)
            {
                return value;
            }

            return null;
        }
    }

    /// <summary>
    /// Reads the whole history from the runtime again and rebuilds every line.
    /// </summary>
    private void ReloadFromRuntime()
    {
        _allRows.Clear();
        _knownIds.Clear();

        System.Collections.Generic.IReadOnlyList<HistoryItem> history = _runtimeService.Runtime.GetHistory();
        long mark = _viewState.ClearedUpToId;
        for (int i = 0; i < history.Count; i++)
        {
            HistoryItem item = history[i];
            if (item.Id <= mark)
            {
                continue;
            }

            AddRow(item);
        }

        RebuildVisible();
        Logger.LogDebug("Loaded {Count} existing history items", _allRows.Count);
    }

    private PacketRowViewModel AddRow(HistoryItem item)
    {
        // The runtime keeps its note that a packet was not processed apart from the item; asking
        // now means a mark made a moment later is missed until the next Refresh.
        // The engine may have been stopped (Apply, or the app closing) while packets still waited on the UI thread:
        // there is then nobody to ask, and the packet is shown without the mark.
        PacketRelationshipInfo? relationship = _runtimeService.IsRunning ? _runtimeService.Runtime.GetPacketRelationship(item.Id) : null;
        bool flagged = relationship != null && relationship.HasProcessingError;

        PacketRowViewModel row = new PacketRowViewModel(item, flagged);
        _allRows.Add(row);
        _knownIds.Add(item.Id);
        return row;
    }

    /// <summary>
    /// History items that arrived from the engine and are waiting for the UI thread.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<HistoryItem> _incoming = new();

    /// <summary>
    /// 1 while a flush of <see cref="_incoming"/> is already queued on the UI thread.
    /// </summary>
    private int _flushQueued;

    /// <summary>
    /// Handle new history items from ProtocolRuntime (any thread). The items are queued and the UI thread
    /// takes everything that has arrived in ONE job. One job per packet, each changing the grid by one
    /// line, kept the window thread busy for 5 to 27 seconds during a burst of 2000 packets (measured
    /// with x25traffic on 2026-11-12), because the grid redraws on every change.
    /// </summary>
    private void OnHistoryItemAdded(object? sender, HistoryItem item)
    {
        _incoming.Enqueue(item);
        if (System.Threading.Interlocked.Exchange(ref _flushQueued, 1) == 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushIncoming);
        }
    }

    /// <summary>
    /// UI thread: takes every queued history item, trims to the engine's history, and updates the grid
    /// once. A small batch changes the grid line by line (cheap, keeps the selection); a large batch
    /// replaces the list in one step so the grid redraws once.
    /// </summary>
    private void FlushIncoming()
    {
        // Open the gate BEFORE draining: an item that arrives while this runs queues a new flush.
        System.Threading.Interlocked.Exchange(ref _flushQueued, 0);

        // The engine was stopped while these packets waited for the UI thread (Apply, or the app closing): there is no
        // engine to ask for its history size, and the packets belong to a run that is over.
        if (!_runtimeService.IsRunning)
        {
            while (_incoming.TryDequeue(out HistoryItem? _))
            {
            }

            return;
        }

        List<PacketRowViewModel> toShow = new List<PacketRowViewModel>();
        int taken = 0;
        while (_incoming.TryDequeue(out HistoryItem? item))
        {
            if (item.Id <= _viewState.ClearedUpToId || _knownIds.Contains(item.Id))
            {
                continue;
            }

            PacketRowViewModel row = AddRow(item);
            taken++;
            if (ShouldShowPacket(row))
            {
                toShow.Add(row);
            }
        }

        if (taken == 0)
        {
            return;
        }

        int overflow = Math.Max(0, _allRows.Count - _runtimeService.Runtime.HistoryCapacity);
        bool small = taken + overflow <= SmallBatch;
        TrimToEngineHistory(toShow, small);
        if (small)
        {
            for (int i = 0; i < toShow.Count; i++)
            {
                Packets.Add(toShow[i]);
            }
        }
        else
        {
            RebuildVisible();
            return;
        }

        UpdateStatus();
    }

    /// <summary>
    /// A batch of at most this many changed lines is applied one line at a time.
    /// </summary>
    private const int SmallBatch = 16;

    /// <summary>
    /// Drops the oldest lines when the window holds more than the engine's packet history (the
    /// "HistoryCapacity" setting). The window shows what the engine keeps. Without this the lines, the
    /// list of known ids and the grid grew for as long as the app ran, and every new packet recounted
    /// all of them. Returns the number of lines dropped. A dropped line is also taken off the grid
    /// here when <paramref name="touchGrid"/> is true; in a large batch the caller rebuilds the grid.
    /// </summary>
    private int TrimToEngineHistory(List<PacketRowViewModel> pendingShow, bool touchGrid)
    {
        int capacity = _runtimeService.Runtime.HistoryCapacity;
        int extra = _allRows.Count - capacity;
        if (capacity <= 0 || extra <= 0)
        {
            return 0;
        }

        for (int i = 0; i < extra; i++)
        {
            PacketRowViewModel old = _allRows[i];
            _knownIds.Remove(old.Id);
            pendingShow.Remove(old);

            // The oldest line is the first one in the grid as well, unless a filter hides it.
            if (!touchGrid)
            {
                // A large batch: the caller rebuilds the grid in one step.
            }
            else if (Packets.Count > 0 && ReferenceEquals(Packets[0], old))
            {
                Packets.RemoveAt(0);
            }
            else
            {
                Packets.Remove(old);
            }
        }

        _allRows.RemoveRange(0, extra);
        return extra;
    }

    private void OnViewCleared(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Drop what the mark now hides; keep the rest.
            long mark = _viewState.ClearedUpToId;
            for (int i = _allRows.Count - 1; i >= 0; i--)
            {
                if (_allRows[i].Id <= mark)
                {
                    _knownIds.Remove(_allRows[i].Id);
                    _allRows.RemoveAt(i);
                }
            }

            RebuildVisible();
        });
    }

    /// <summary>
    /// Check if packet should be shown based on current filters.
    /// </summary>
    private bool ShouldShowPacket(PacketRowViewModel row)
    {
        // Direction filter
        if (row.Direction == Direction.TX && !ShowTx) return false;
        if (row.Direction == Direction.RX && !ShowRx) return false;

        // Layer filter
        if (row.Layer == LayerType.HDLC && !ShowHdlc) return false;
        if (row.Layer == LayerType.LAPB && !ShowLapb) return false;
        if (row.Layer == LayerType.X25 && !ShowX25) return false;

        // LAPB frame class filter: a line with no class (HDLC, X.25) is not touched by it.
        if (row.L2Token == "I" && !ShowIFrames) return false;
        if (row.L2Token == "S" && !ShowSFrames) return false;
        if (row.L2Token == "U" && !ShowUFrames) return false;

        // X.25 packet group filter: types with no group (RR, RNR, REJ, registration, diagnostic) stay.
        if (row.L3Group == PacketDecoding.GroupData && !ShowDataPackets) return false;
        if (row.L3Group == PacketDecoding.GroupCall && !ShowCallPackets) return false;
        if (row.L3Group == PacketDecoding.GroupClear && !ShowClearPackets) return false;
        if (row.L3Group == PacketDecoding.GroupReset && !ShowResetPackets) return false;
        if (row.L3Group == PacketDecoding.GroupRestart && !ShowRestartPackets) return false;
        if (row.L3Group == PacketDecoding.GroupInterrupt && !ShowInterruptPackets) return false;

        // Channel filter: only X.25 packets have a channel, so everything else is hidden by it.
        int? lcn = LcnFilterValue;
        if (lcn.HasValue && (!row.LcnValue.HasValue || row.LcnValue.Value != lcn.Value)) return false;

        if (ErrorsOnly && !row.IsError) return false;

        // Collapse filter - only show main line items when collapsed
        if (IsCollapsed && !row.Item.IsMainLine) return false;

        return true;
    }

    /// <summary>
    /// Update status text and statistics.
    /// </summary>
    private void UpdateStatus()
    {
        StatusText = $"{Packets.Count} packets in this capture";

        // Calculate statistics
        var txCount = 0;
        var rxCount = 0;
        var hdlcCount = 0;
        var lapbCount = 0;
        var x25Count = 0;
        var errorCount = 0;

        for (int i = 0; i < Packets.Count; i++)
        {
            var packet = Packets[i];
            if (packet.Direction == Direction.TX) txCount++;
            if (packet.Direction == Direction.RX) rxCount++;

            if (packet.Layer == LayerType.HDLC) hdlcCount++;
            if (packet.Layer == LayerType.LAPB) lapbCount++;
            if (packet.Layer == LayerType.X25) x25Count++;
            if (packet.IsError) errorCount++;
        }

        int hidden = _allRows.Count - Packets.Count;
        // These are counts of the lines this window captured and shows, not the engine's totals.
        StatisticsText = $"In this capture: TX: {txCount} | RX: {rxCount} | HDLC: {hdlcCount} | LAPB: {lapbCount} | X.25: {x25Count} | Hidden: {hidden} | Errors: {errorCount}";
    }

    /// <summary>
    /// Refresh display when filters change.
    /// </summary>
    partial void OnShowTxChanged(bool value) => RebuildVisible();
    partial void OnShowRxChanged(bool value) => RebuildVisible();
    partial void OnShowHdlcChanged(bool value) => RebuildVisible();
    partial void OnShowLapbChanged(bool value) => RebuildVisible();
    partial void OnShowX25Changed(bool value) => RebuildVisible();
    partial void OnShowIFramesChanged(bool value) => RebuildVisible();
    partial void OnShowSFramesChanged(bool value) => RebuildVisible();
    partial void OnShowUFramesChanged(bool value) => RebuildVisible();
    partial void OnShowDataPacketsChanged(bool value) => RebuildVisible();
    partial void OnShowCallPacketsChanged(bool value) => RebuildVisible();
    partial void OnShowClearPacketsChanged(bool value) => RebuildVisible();
    partial void OnShowResetPacketsChanged(bool value) => RebuildVisible();
    partial void OnShowRestartPacketsChanged(bool value) => RebuildVisible();
    partial void OnShowInterruptPacketsChanged(bool value) => RebuildVisible();
    partial void OnLcnFilterTextChanged(string value) => RebuildVisible();
    partial void OnErrorsOnlyChanged(bool value) => RebuildVisible();
    partial void OnIsCollapsedChanged(bool value) => RebuildVisible();

    /// <summary>
    /// Rebuilds the visible list from the lines already made, with the current filters.
    /// </summary>
    private void RebuildVisible()
    {
        PacketRowViewModel? keep = SelectedPacket;
        List<PacketRowViewModel> visible = new List<PacketRowViewModel>(_allRows.Count);

        for (int i = 0; i < _allRows.Count; i++)
        {
            PacketRowViewModel row = _allRows[i];
            if (ShouldShowPacket(row))
            {
                visible.Add(row);
            }
        }

        // One new list for the grid, one redraw (adding the lines one at a time redraws the grid for each).
        Packets = new ObservableCollection<PacketRowViewModel>(visible);

        if (keep != null && Packets.Contains(keep))
        {
            SelectedPacket = keep;
        }

        UpdateStatus();
    }

    /// <summary>
    /// Hides the packets shown so far, in this window and in every other Packet window.
    /// </summary>
    /// <remarks>
    /// The engine has no call to empty its history, so this only hides: the lines go away, the
    /// engine's history and counters stay as they were. Packets that arrive after this are shown.
    /// </remarks>
    [RelayCommand]
    private void ClearPackets()
    {
        _viewState.ClearAll();
        Logger.LogInformation("Packet display cleared");
    }

    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
        Logger.LogDebug("Collapse mode: {Mode}", IsCollapsed ? "Collapsed" : "Expanded");
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_runtimeService.IsRunning)
        {
            ReloadFromRuntime();
        }

        Logger.LogInformation("Packet display refreshed");
    }

    /// <summary>
    /// Puts the channel number of the selected packet into the channel filter, or empties the filter
    /// when it is already set (the console's LCN button did the same).
    /// </summary>
    [RelayCommand]
    private void ToggleLcnFilter()
    {
        if (!string.IsNullOrEmpty(LcnFilterText))
        {
            LcnFilterText = "";
            return;
        }

        if (SelectedPacket != null && SelectedPacket.LcnValue.HasValue)
        {
            LcnFilterText = SelectedPacket.LcnValue.Value.ToString();
        }
    }

    /// <summary>
    /// Builds the text of an export of the packets that are shown now: every filter that is set
    /// (direction, layer, LAPB class, X.25 group, channel, errors only, collapse) applies.
    /// </summary>
    /// <param name="csv">
    /// True for comma separated values with one line per packet; false for the readable text form
    /// with the decoded fields of each packet.
    /// </param>
    /// <returns>The whole file text.</returns>
    public string BuildExport(bool csv)
    {
        StringBuilder sb = new StringBuilder(4096);

        if (csv)
        {
            sb.AppendLine("Id,Time,Layer,Direction,L2,L3,LCN,Seq,Flags,FCS,Error,Length,Description,Hex,Ascii");
            for (int i = 0; i < Packets.Count; i++)
            {
                PacketRowViewModel p = Packets[i];
                sb.Append(p.Id).Append(',')
                    .Append(p.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(',')
                    .Append(p.Layer).Append(',')
                    .Append(p.Direction).Append(',')
                    .Append(CsvField(p.L2Name)).Append(',')
                    .Append(CsvField(p.L3Type)).Append(',')
                    .Append(p.Lcn).Append(',')
                    .Append(CsvField(p.Sequence)).Append(',')
                    .Append(CsvField(p.Flags)).Append(',')
                    .Append(p.Fcs).Append(',')
                    .Append(CsvField(p.ErrorText)).Append(',')
                    .Append(p.Length).Append(',')
                    .Append(CsvField(p.Description)).Append(',')
                    .Append(CsvField(p.Item.GetPayloadHex())).Append(',')
                    .Append(CsvField(PrintableAscii(p.Item.Payload)))
                    .AppendLine();
            }

            return sb.ToString();
        }

        sb.AppendLine($"X.25 Emulator Packet Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Total Packets: {Packets.Count}");
        sb.AppendLine(StatisticsText);
        sb.AppendLine(new string('=', 80));
        sb.AppendLine();

        for (int i = 0; i < Packets.Count; i++)
        {
            PacketRowViewModel packet = Packets[i];
            sb.AppendLine($"[{packet.Id}] {packet.Timestamp:HH:mm:ss.fff} {packet.Layer} {packet.Direction}");
            sb.AppendLine($"    {packet.Description}");
            sb.AppendLine($"    Length: {packet.Length} bytes");
            if (packet.TypeText.Length > 0)
            {
                sb.AppendLine($"    Type: {packet.TypeText}  LCN: {packet.Lcn}  Seq: {packet.Sequence}  Flags: {packet.Flags}  FCS: {packet.Fcs}");
            }

            if (packet.IsError)
            {
                sb.AppendLine($"    ERROR: {packet.ErrorText}");
            }

            sb.AppendLine($"    Hex: {packet.Item.GetPayloadHex()}");
            sb.AppendLine($"    ASCII: {packet.Item.GetPayloadString()}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes an export of the packets shown now to a file. A name ending in <c>.csv</c> gives the
    /// comma separated form; any other name gives the readable text form.
    /// </summary>
    /// <param name="path">The file to write; it is replaced if it exists.</param>
    public void ExportToFile(string path)
    {
        bool csv = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, BuildExport(csv), new UTF8Encoding(false));
        Logger.LogInformation("Exported {Count} packets to {Path}", Packets.Count, path);
    }

    private static string CsvField(string value)
    {
        if (value.Length == 0)
        {
            return "";
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string PrintableAscii(byte[] data)
    {
        // Control bytes would break a CSV line in two, so they show as a dot.
        char[] chars = new char[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            chars[i] = data[i] >= 0x20 && data[i] < 0x7F ? (char)data[i] : '.';
        }

        return new string(chars);
    }

    [RelayCommand]
    private async Task ExportPackets()
    {
        try
        {
            var window = GetMainWindow();
            if (window == null)
            {
                Logger.LogWarning("Cannot export: Main window not found");
                return;
            }

            var storageProvider = window.StorageProvider;
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Packets",
                SuggestedFileName = $"packets_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("CSV Files (spreadsheet)") { Patterns = new[] { "*.csv" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                bool csv = file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                string text = BuildExport(csv);

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(text);

                Logger.LogInformation("Exported {Count} packets to {Path}", Packets.Count, file.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting packets");
        }
    }

    [RelayCommand]
    private async Task CopySelectedPacket()
    {
        if (SelectedPacket != null)
        {
            try
            {
                var window = GetMainWindow();
                if (window?.Clipboard != null)
                {
                    var text = new StringBuilder();
                    text.AppendLine(SelectedPacket.BuildDetailText());
                    text.AppendLine($"Hex: {SelectedPacket.Item.GetPayloadHex()}");
                    text.AppendLine($"ASCII: {SelectedPacket.Item.GetPayloadString()}");

                    await window.Clipboard.SetTextAsync(text.ToString());
                    Logger.LogInformation("Copied packet {Id} to clipboard", SelectedPacket.Id);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error copying packet to clipboard");
            }
        }
    }

    /// <summary>
    /// Get main window for file dialogs and clipboard access.
    /// </summary>
    private Avalonia.Controls.Window? GetMainWindow()
    {
        return (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    /// <summary>
    /// Clean up resources when ViewModel is disposed.
    /// </summary>
    public override void Dispose()
    {
        _viewState.Cleared -= OnViewCleared;

        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.HistoryItemAdded -= OnHistoryItemAdded;
        }

        Logger.LogInformation("PacketWindowViewModel disposed");
        base.Dispose();
    }
}

/// <summary>
/// One byte of the payload as shown in the hex dump, with a flag that says it belongs to the tree
/// line the user has clicked.
/// </summary>
public partial class HexByteViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>
    /// Creates the entry.
    /// </summary>
    /// <param name="text">The byte as two hex digits.</param>
    public HexByteViewModel(string text)
    {
        Text = text;
    }

    /// <summary>
    /// Gets the byte as two hex digits.
    /// </summary>
    public string Text { get; }
}
