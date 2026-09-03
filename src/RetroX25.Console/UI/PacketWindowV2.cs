using System;
using System.Collections.Generic;
using System.Text;
using Terminal.Gui;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroIO.Link.Models;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Runtime.Models;
using RetroIO.Protocols.X25.Sintran;

namespace RetroX25.Console.UI
{
    /// <summary>
    /// New simplified packet window with unified packet tracking and collapse/expand functionality
    /// Clean design focused on essential features with proper unified packet integration
    /// </summary>
    public class PacketWindowV2 : Window
    {
        private readonly ProtocolRuntime _runtime;
        private readonly PacketDisplayManager _displayManager;
        
        // UI Controls
        private ListView _packetListView = null!;
        private TextView _detailsTextView = null!;
        private Button _collapseToggleButton = null!;
        private Button _exportButton = null!;
        private Button _closeButton = null!;
        private Label _statusLabel = null!;
        private Label _statsLabel = null!;
        
        // Data
        private List<PacketDisplayItem> _currentPackets = new List<PacketDisplayItem>();
        private bool _autoRefresh = true;

        /// <summary>
        /// Create new PacketWindowV2 with unified packet tracking
        /// </summary>
        /// <param name="runtime">Protocol runtime for accessing packets</param>
        public PacketWindowV2(ProtocolRuntime runtime) : base("Packet Window V2 - Unified Tracking")
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _displayManager = new PacketDisplayManager(_runtime);
            
            InitializeUI();
            SetupEventHandlers();
            RefreshPacketList();
            
            // Subscribe to runtime events for auto-refresh
            _runtime.HistoryItemAdded += OnHistoryItemAdded;
            _displayManager.CollapseStateChanged += OnCollapseStateChanged;
        }

        /// <summary>
        /// Initialize the UI components and layout
        /// </summary>
        private void InitializeUI()
        {
            Width = Dim.Fill();
            Height = Dim.Fill();
            
            // Top section: Packet list (left) and details (right)
            var topFrame = new FrameView("Packets")
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(50),
                Height = Dim.Percent(80)
            };
            
            var detailsFrame = new FrameView("Frame Details")
            {
                X = Pos.Right(topFrame),
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Percent(80)
            };
            
            // Packet list view
            _packetListView = new ListView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                AllowsMarking = false,
                AllowsMultipleSelection = false
            };
            
            // Details text view
            _detailsTextView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = false
            };
            
            // Details text view setup - remove problematic DrawFrame assignment
            
            topFrame.Add(_packetListView);
            detailsFrame.Add(_detailsTextView);
            
            // Bottom section: Controls and status
            var controlsY = Pos.Bottom(topFrame);
            
            // Status labels
            _statusLabel = new Label("Status: Ready")
            {
                X = 0,
                Y = controlsY,
                Width = Dim.Percent(50),
                Height = 1
            };
            
            _statsLabel = new Label("Statistics: No packets")
            {
                X = 0,
                Y = controlsY + 1,
                Width = Dim.Percent(50),
                Height = 1
            };
            
            // Control buttons
            _collapseToggleButton = new Button("Expand")
            {
                X = 0,
                Y = controlsY + 2,
                Width = 12,
                Height = 1
            };
            
            _exportButton = new Button("Export")
            {
                X = Pos.Right(_collapseToggleButton) + 2,
                Y = controlsY + 2,
                Width = 8,
                Height = 1
            };
            
            _closeButton = new Button("Close")
            {
                X = Pos.Right(_exportButton) + 2,
                Y = controlsY + 2,
                Width = 7,
                Height = 1
            };
            
            // Add all components to window
            Add(topFrame);
            Add(detailsFrame);
            Add(_statusLabel);
            Add(_statsLabel);
            Add(_collapseToggleButton);
            Add(_exportButton);
            Add(_closeButton);
        }

        /// <summary>
        /// Setup event handlers for UI controls
        /// </summary>
        private void SetupEventHandlers()
        {
            // Packet selection handler
            _packetListView.SelectedItemChanged += OnPacketSelected;
            
            // Button handlers
            _collapseToggleButton.Clicked += OnCollapseToggleClicked;
            _exportButton.Clicked += OnExportClicked;
            _closeButton.Clicked += OnCloseClicked;
            
            // Keyboard shortcuts handled in ProcessKey method
        }

        /// <summary>
        /// Handle packet selection in the list view
        /// </summary>
        private void OnPacketSelected(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < _currentPackets.Count)
            {
                var selectedPacket = _currentPackets[args.Item];
                ShowPacketDetails(selectedPacket);
            }
        }

        /// <summary>
        /// Show detailed information for the selected packet
        /// </summary>
        private void ShowPacketDetails(PacketDisplayItem packetItem)
        {
            var details = new StringBuilder();
            
            if (packetItem.Packet is UnifiedHistoryItem unifiedItem)
            {
                // Show unified packet details
                details.AppendLine("=== UNIFIED PACKET DETAILS ===");
                details.AppendLine($"Global Packet ID: {unifiedItem.Id}");
                details.AppendLine($"Direction: {unifiedItem.Direction}");
                details.AppendLine($"Created: {unifiedItem.Timestamp:HH:mm:ss.fff}");
                details.AppendLine($"Is Terminal: {unifiedItem.IsTerminal}");
                details.AppendLine($"Has Error: {unifiedItem.HasProcessingError}");
                details.AppendLine();
                
                // Show detailed layer information
                details.AppendLine(unifiedItem.GetDetailedDisplay());
            }
            else
            {
                // Show legacy packet details
                details.AppendLine("=== LEGACY PACKET DETAILS ===");
                details.AppendLine($"Packet ID: {packetItem.Packet.Id}");
                details.AppendLine($"Layer: {packetItem.Packet.Layer}");
                details.AppendLine($"Direction: {packetItem.Packet.Direction}");
                details.AppendLine($"Timestamp: {packetItem.Packet.Timestamp:HH:mm:ss.fff}");
                details.AppendLine($"Summary: {packetItem.Packet.GetSummary()}");
                details.AppendLine();
                
                if (packetItem.Packet.Payload?.Length > 0)
                {
                    details.AppendLine("=== RAW DATA ===");
                    details.AppendLine(Convert.ToHexString(packetItem.Packet.Payload));
                }
            }
            
            _detailsTextView.Text = details.ToString();
        }

        /// <summary>
        /// Handle collapse/expand toggle button click
        /// </summary>
        private void OnCollapseToggleClicked()
        {
            var newState = _displayManager.ToggleGlobalCollapse();
            RefreshPacketList();
            UpdateButtonText();
            
            _statusLabel.Text = $"Status: {(newState ? "Collapsed" : "Expanded")} view";
        }

        /// <summary>
        /// Handle export button click
        /// </summary>
        private void OnExportClicked()
        {
            try 
            {
                ExportPacketData();
                _statusLabel.Text = "Status: Export completed";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Status: Export failed - {ex.Message}";
            }
        }

        /// <summary>
        /// Handle close button click
        /// </summary>
        private void OnCloseClicked()
        {
            Application.RequestStop();
        }

        /// <summary>
        /// Export packet data to a file
        /// </summary>
        private void ExportPacketData()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"packets_export_{timestamp}.txt";
            
            var exportData = new StringBuilder();
            exportData.AppendLine("=== X.25 PACKET EXPORT ===");
            exportData.AppendLine($"Export Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            exportData.AppendLine($"Total Packets: {_currentPackets.Count}");
            exportData.AppendLine($"Display Mode: {(_displayManager.GlobalCollapseEnabled ? "Collapsed" : "Expanded")}");
            exportData.AppendLine();
            
            foreach (var packet in _currentPackets)
            {
                exportData.AppendLine("=" + new string('=', 50));
                
                if (packet.Packet is UnifiedHistoryItem unifiedItem)
                {
                    exportData.AppendLine($"Unified Packet ID: {unifiedItem.Id}");
                    exportData.AppendLine($"Direction: {unifiedItem.Direction}");
                    exportData.AppendLine($"Terminal: {unifiedItem.IsTerminal}");
                    exportData.AppendLine($"Error: {unifiedItem.HasProcessingError}");
                    exportData.AppendLine();
                    exportData.AppendLine(unifiedItem.GetDetailedDisplay());
                }
                else
                {
                    exportData.AppendLine($"Legacy Packet ID: {packet.Packet.Id}");
                    exportData.AppendLine($"Layer: {packet.Packet.Layer}");
                    exportData.AppendLine($"Direction: {packet.Packet.Direction}");
                    exportData.AppendLine($"Summary: {packet.Packet.GetSummary()}");
                }
                
                exportData.AppendLine();
            }
            
            System.IO.File.WriteAllText(filename, exportData.ToString());
        }

        /// <summary>
        /// Refresh the packet list from the runtime
        /// </summary>
        private void RefreshPacketList()
        {
            _currentPackets = _displayManager.GetDisplayItems();
            
            // Create display strings for list view
            var displayItems = new List<string>();
            foreach (var item in _currentPackets)
            {
                if (item.Packet is UnifiedHistoryItem unifiedItem)
                {
                    var indicator = unifiedItem.IsTerminal ? "►" : "•";
                    var errorFlag = unifiedItem.HasProcessingError ? " [ERROR]" : "";
                    var layers = string.Join("→", unifiedItem.PacketInfo.LayersVisited);
                    displayItems.Add($"{indicator} {unifiedItem.Id:D6} [{layers}] {unifiedItem.Direction} {unifiedItem.PacketInfo.GetSummary()}{errorFlag}");
                }
                else
                {
                    displayItems.Add($"• {item.Packet.Id:D6} [{item.Packet.Layer}] {item.Packet.Direction} {item.Packet.GetSummary()}");
                }
            }
            
            _packetListView.SetSource(displayItems);
            
            UpdateStatistics();
            UpdateButtonText();
        }

        /// <summary>
        /// Update statistics display
        /// </summary>
        private void UpdateStatistics()
        {
            var stats = _displayManager.GetDisplayStatistics();
            _statsLabel.Text = $"Statistics: Total={stats.TotalPackets}, Displayed={stats.DisplayedPackets}, Hidden={stats.HiddenPackets}, Errors={stats.ErrorPackets}";
        }

        /// <summary>
        /// Update collapse/expand button text
        /// </summary>
        private void UpdateButtonText()
        {
            _collapseToggleButton.Text = _displayManager.GlobalCollapseEnabled ? "Expand" : "Collapse";
        }

        /// <summary>
        /// Handle runtime history updates for auto-refresh
        /// </summary>
        private void OnHistoryItemAdded(object? sender, HistoryItem item)
        {
            if (_autoRefresh)
            {
                Application.MainLoop.Invoke(() => RefreshPacketList());
            }
        }

        /// <summary>
        /// Handle collapse state changes
        /// </summary>
        private void OnCollapseStateChanged(object? sender, bool collapsed)
        {
            Application.MainLoop.Invoke(() => 
            {
                UpdateButtonText();
                _statusLabel.Text = $"Status: {(collapsed ? "Collapsed" : "Expanded")} view";
            });
        }

        /// <summary>
        /// Handle key events
        /// </summary>
        public override bool ProcessKey(KeyEvent key)
        {
            switch (key.Key)
            {
                case Key.F5:
                    RefreshPacketList();
                    _statusLabel.Text = "Status: Refreshed";
                    return true;
                    
                case Key.Esc:
                    Application.RequestStop();
                    return true;
                    
                case Key.Tab:
                    // Toggle focus between packet list and details
                    if (_packetListView.HasFocus)
                        _detailsTextView.SetFocus();
                    else
                        _packetListView.SetFocus();
                    return true;
            }
            
            return base.ProcessKey(key);
        }

        /// <summary>
        /// Cleanup resources when window is disposed
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _runtime.HistoryItemAdded -= OnHistoryItemAdded;
                _displayManager.CollapseStateChanged -= OnCollapseStateChanged;
            }
            base.Dispose(disposing);
        }
    }
}