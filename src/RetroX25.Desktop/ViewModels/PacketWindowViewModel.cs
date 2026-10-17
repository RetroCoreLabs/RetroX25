//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Packet Window (F2) - displays protocol packet capture with filtering.
/// </summary>
public partial class PacketWindowViewModel : ViewModelWithLogger
{
    private readonly IRuntimeService _runtimeService;

    // Observable collections for display
    [ObservableProperty]
    private ObservableCollection<HistoryItem> _packets = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPacketSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedPacketHex))]
    [NotifyPropertyChangedFor(nameof(SelectedPacketAscii))]
    private HistoryItem? _selectedPacket;

    /// <summary>
    /// Get summary of selected packet.
    /// </summary>
    public string SelectedPacketSummary => SelectedPacket?.GetSummary() ?? "";

    /// <summary>
    /// Get hex dump of selected packet.
    /// </summary>
    public string SelectedPacketHex => SelectedPacket?.GetPayloadHex() ?? "";

    /// <summary>
    /// Get ASCII representation of selected packet.
    /// </summary>
    public string SelectedPacketAscii => SelectedPacket?.GetPayloadString() ?? "";

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

    [ObservableProperty]
    private bool _isCollapsed = true; // Default to collapsed view

    [ObservableProperty]
    private string _statusText = "0 packets";

    // Statistics
    [ObservableProperty]
    private string _statisticsText = "TX: 0 | RX: 0 | HDLC: 0 | LAPB: 0 | X.25: 0";

    /// <summary>
    /// Creates PacketWindowViewModel with dependency injection.
    /// </summary>
    public PacketWindowViewModel(
        ILogger<PacketWindowViewModel> logger,
        IRuntimeService runtimeService) : base(logger)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));

        // Subscribe to ProtocolRuntime events
        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.HistoryItemAdded += OnHistoryItemAdded;
            LoadExistingHistory();
        }

        Logger.LogInformation("PacketWindowViewModel initialized");
    }

    /// <summary>
    /// Load existing history from runtime.
    /// </summary>
    private void LoadExistingHistory()
    {
        var history = _runtimeService.Runtime.GetHistory();

        foreach (var item in history)
        {
            if (ShouldShowPacket(item))
            {
                Packets.Add(item);
            }
        }

        UpdateStatus();
        Logger.LogDebug("Loaded {Count} existing history items", Packets.Count);
    }

    /// <summary>
    /// Handle new history items from ProtocolRuntime.
    /// </summary>
    private void OnHistoryItemAdded(object? sender, HistoryItem item)
    {
        // Add to collection if it matches filters
        if (ShouldShowPacket(item))
        {
            // UI updates must happen on UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Packets.Add(item);
                UpdateStatus();
            });
        }
    }

    /// <summary>
    /// Check if packet should be shown based on current filters.
    /// </summary>
    private bool ShouldShowPacket(HistoryItem item)
    {
        // Direction filter
        if (item.Direction == Direction.TX && !ShowTx) return false;
        if (item.Direction == Direction.RX && !ShowRx) return false;

        // Layer filter
        if (item.Layer == LayerType.HDLC && !ShowHdlc) return false;
        if (item.Layer == LayerType.LAPB && !ShowLapb) return false;
        if (item.Layer == LayerType.X25 && !ShowX25) return false;

        // Collapse filter - only show main line items when collapsed
        if (IsCollapsed && !item.IsMainLine) return false;

        return true;
    }

    /// <summary>
    /// Update status text and statistics.
    /// </summary>
    private void UpdateStatus()
    {
        StatusText = $"{Packets.Count} packets";

        // Calculate statistics
        var txCount = 0;
        var rxCount = 0;
        var hdlcCount = 0;
        var lapbCount = 0;
        var x25Count = 0;

        for (int i = 0; i < Packets.Count; i++)
        {
            var packet = Packets[i];
            if (packet.Direction == Direction.TX) txCount++;
            if (packet.Direction == Direction.RX) rxCount++;

            if (packet.Layer == LayerType.HDLC) hdlcCount++;
            if (packet.Layer == LayerType.LAPB) lapbCount++;
            if (packet.Layer == LayerType.X25) x25Count++;
        }

        StatisticsText = $"TX: {txCount} | RX: {rxCount} | HDLC: {hdlcCount} | LAPB: {lapbCount} | X.25: {x25Count}";
    }

    /// <summary>
    /// Refresh display when filters change.
    /// </summary>
    partial void OnShowTxChanged(bool value) => RefreshDisplay();
    partial void OnShowRxChanged(bool value) => RefreshDisplay();
    partial void OnShowHdlcChanged(bool value) => RefreshDisplay();
    partial void OnShowLapbChanged(bool value) => RefreshDisplay();
    partial void OnShowX25Changed(bool value) => RefreshDisplay();
    partial void OnIsCollapsedChanged(bool value) => RefreshDisplay();

    /// <summary>
    /// Refresh the packet display based on current filters.
    /// </summary>
    private void RefreshDisplay()
    {
        Packets.Clear();

        var history = _runtimeService.Runtime.GetHistory();
        foreach (var item in history)
        {
            if (ShouldShowPacket(item))
            {
                Packets.Add(item);
            }
        }

        UpdateStatus();
    }

    [RelayCommand]
    private void ClearPackets()
    {
        Packets.Clear();
        UpdateStatus();
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
        RefreshDisplay();
        Logger.LogInformation("Packet display refreshed");
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
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"X.25 Emulator Packet Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Total Packets: {Packets.Count}");
                sb.AppendLine(StatisticsText);
                sb.AppendLine(new string('=', 80));
                sb.AppendLine();

                for (int i = 0; i < Packets.Count; i++)
                {
                    var packet = Packets[i];
                    sb.AppendLine($"[{packet.Id}] {packet.Timestamp:HH:mm:ss.fff} {packet.Layer} {packet.Direction}");
                    sb.AppendLine($"    {packet.Description}");
                    sb.AppendLine($"    Length: {packet.Length} bytes");
                    sb.AppendLine($"    Hex: {packet.GetPayloadHex()}");
                    sb.AppendLine($"    ASCII: {packet.GetPayloadString()}");
                    sb.AppendLine();
                }

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(sb.ToString());

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
                    text.AppendLine($"Packet #{SelectedPacket.Id} - {SelectedPacket.Timestamp:HH:mm:ss.fff}");
                    text.AppendLine($"Layer: {SelectedPacket.Layer} | Direction: {SelectedPacket.Direction}");
                    text.AppendLine($"Description: {SelectedPacket.Description}");
                    text.AppendLine($"Length: {SelectedPacket.Length} bytes");
                    text.AppendLine();
                    text.AppendLine($"Hex: {SelectedPacket.GetPayloadHex()}");
                    text.AppendLine($"ASCII: {SelectedPacket.GetPayloadString()}");

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
        if (_runtimeService.IsRunning)
        {
            _runtimeService.Runtime.HistoryItemAdded -= OnHistoryItemAdded;
        }

        Logger.LogInformation("PacketWindowViewModel disposed");
        base.Dispose();
    }
}
