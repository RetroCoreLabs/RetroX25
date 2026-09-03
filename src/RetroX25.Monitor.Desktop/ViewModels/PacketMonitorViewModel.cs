using System;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Protocols.X25.Monitor;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// The packet-analyzer window view-model: connects to a monitoring server, decodes each raw captured
    /// frame it receives, streams the results into the packet list, and builds the layered-detail and hex
    /// panes for the selected packet. Under X25MON/1 the switch ships raw frames, so decoding happens here;
    /// the server filters by port and this view-model applies the address and packet-type dimensions.
    /// </summary>
    public sealed partial class PacketMonitorViewModel : ObservableObject, IDisposable
    {
        private const int MaxPackets = 5000;

        private readonly MonitorClient _client = new MonitorClient();
        private readonly Action<Action> _uiInvoke;

        // The active filter. The server honours only its port dimension (it has no decoded fields); this
        // view-model applies the source/destination/packet-type dimensions after decoding each frame.
        private MonitorFilter _activeFilter = new MonitorFilter();

        [ObservableProperty]
        private SwitchEndpoint? _selectedSwitch;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _status = "Disconnected";

        [ObservableProperty]
        private string _filterPorts = string.Empty;

        [ObservableProperty]
        private string _filterSrc = string.Empty;

        [ObservableProperty]
        private string _filterDst = string.Empty;

        [ObservableProperty]
        private string _filterPacketTypes = string.Empty;

        [ObservableProperty]
        private CaptureRowViewModel? _selectedPacket;

        [ObservableProperty]
        private string _hexDump = string.Empty;

        [ObservableProperty]
        private object? _selectedTreeItem;

        [ObservableProperty]
        private string _selectedFieldBytes = string.Empty;

        /// <summary>
        /// The known switch endpoints to choose from, shared with the shell.
        /// </summary>
        public ObservableCollection<SwitchEndpoint> Switches { get; }

        /// <summary>
        /// The captured packets, newest appended at the end.
        /// </summary>
        public ObservableCollection<CaptureRowViewModel> Packets { get; } = new ObservableCollection<CaptureRowViewModel>();

        /// <summary>
        /// The decoded layers of the selected packet.
        /// </summary>
        public ObservableCollection<LayerNodeViewModel> Layers { get; } = new ObservableCollection<LayerNodeViewModel>();

        /// <summary>
        /// Creates the view-model, optionally overriding how work is marshalled to the UI thread (tests pass a
        /// synchronous invoker).
        /// </summary>
        /// <param name="uiInvoke">
        /// Marshals an action onto the UI thread; defaults to the Avalonia dispatcher.
        /// </param>
        public PacketMonitorViewModel(ObservableCollection<SwitchEndpoint> switches, Action<Action>? uiInvoke = null)
        {
            Switches = switches;
            _selectedSwitch = switches.Count > 0 ? switches[0] : null;
            _uiInvoke = uiInvoke ?? (action => Dispatcher.UIThread.Post(action));
            _client.CaptureReceived += OnCaptureReceived;
            _client.ConnectionChanged += OnConnectionChanged;
        }

        /// <summary>
        /// Connects to the selected switch's monitoring server.
        /// </summary>
        [RelayCommand]
        private void Connect()
        {
            SwitchEndpoint? endpoint = SelectedSwitch;
            if (endpoint == null)
            {
                Status = "No switch selected";
                return;
            }

            if (_client.Connect(endpoint.Host, endpoint.Port))
            {
                Status = $"Connected to {endpoint.Display}";
            }
            else
            {
                Status = $"Failed to connect to {endpoint.Display}";
            }
        }

        /// <summary>
        /// Disconnects from the monitoring server.
        /// </summary>
        [RelayCommand]
        private void Disconnect()
        {
            _client.Disconnect();
        }

        /// <summary>
        /// Sends the current filter fields to the server to narrow the stream.
        /// </summary>
        [RelayCommand]
        private void ApplyFilter()
        {
            MonitorFilter filter = MonitorFilter.FromText(FilterPorts, FilterSrc, FilterDst, FilterPacketTypes);
            _activeFilter = filter;
            // The server applies the port dimension; the address/packet-type dimensions are applied here.
            _client.SendFilter(filter);
            Status = "Filter applied";
        }

        /// <summary>
        /// Clears the captured packet list and detail panes.
        /// </summary>
        [RelayCommand]
        private void ClearPackets()
        {
            Packets.Clear();
            Layers.Clear();
            HexDump = string.Empty;
            SelectedPacket = null;
        }

        /// <summary>
        /// Adds a decoded record to the packet list, trimming the oldest beyond the cap.
        /// </summary>
        /// <param name="record">
        /// The decoded capture record.
        /// </param>
        public void AddCapture(CaptureRecord record)
        {
            Packets.Add(new CaptureRowViewModel(record));
            while (Packets.Count > MaxPackets)
            {
                Packets.RemoveAt(0);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _client.Dispose();
        }

        partial void OnSelectedPacketChanged(CaptureRowViewModel? value)
        {
            Layers.Clear();
            SelectedTreeItem = null;
            SelectedFieldBytes = string.Empty;
            if (value == null)
            {
                HexDump = string.Empty;
                return;
            }

            for (int i = 0; i < value.Record.Layers.Count; i++)
            {
                Layers.Add(new LayerNodeViewModel(value.Record.Layers[i]));
            }

            HexDump = FormatHexDump(value.Record.Hex);
        }

        // When a field in the detail tree is selected, show its raw bytes sliced from the frame.
        partial void OnSelectedTreeItemChanged(object? value)
        {
            SelectedFieldBytes = DescribeFieldBytes(value);
        }

        private string DescribeFieldBytes(object? item)
        {
            if (item is not FieldRowViewModel field || field.Offset < 0 || SelectedPacket == null)
            {
                return string.Empty;
            }

            byte[] frame = SelectedPacket.Record.Frame;
            if (frame.Length == 0 || field.Offset >= frame.Length)
            {
                return string.Empty;
            }

            int end = Math.Min(frame.Length, field.Offset + field.Length);
            StringBuilder sb = new StringBuilder();
            sb.Append(field.Name).Append(" @").Append(field.Offset)
              .Append(" (").Append(end - field.Offset).Append(" bytes): ");
            for (int i = field.Offset; i < end; i++)
            {
                if (i > field.Offset)
                {
                    sb.Append(' ');
                }
                sb.Append(frame[i].ToString("X2"));
            }

            return sb.ToString();
        }

        private void OnCaptureReceived(CaptureRecord record)
        {
            // The wire carries the raw frame (decode-at-viewer): decode it into display fields here, then
            // apply the client-side filter dimensions the server cannot (source/destination/packet-type).
            CaptureRecord decoded = X25CaptureDecoder.Decode(record.Frame, record.Port, record.Direction,
                record.Seq, record.TimestampMs);
            if (!_activeFilter.Matches(decoded))
            {
                return;
            }

            _uiInvoke(() => AddCapture(decoded));
        }

        private void OnConnectionChanged(bool connected)
        {
            _uiInvoke(() =>
            {
                IsConnected = connected;
                if (!connected)
                {
                    Status = "Disconnected";
                }
            });
        }

        /// <summary>
        /// Reflows a space-separated hex string into an offset/hex/ASCII dump (16 bytes per line).
        /// </summary>
        /// <param name="spacedHex">
        /// The space-separated uppercase hex bytes.
        /// </param>
        /// <returns>
        /// A multi-line hex dump.
        /// </returns>
        private static string FormatHexDump(string spacedHex)
        {
            if (string.IsNullOrWhiteSpace(spacedHex))
            {
                return string.Empty;
            }

            string[] tokens = spacedHex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder(tokens.Length * 4);
            int offset = 0;
            while (offset < tokens.Length)
            {
                sb.Append(offset.ToString("X4"));
                sb.Append("  ");

                StringBuilder ascii = new StringBuilder(16);
                for (int col = 0; col < 16; col++)
                {
                    int index = offset + col;
                    if (index < tokens.Length)
                    {
                        sb.Append(tokens[index]);
                        sb.Append(' ');
                        ascii.Append(ToAscii(tokens[index]));
                    }
                    else
                    {
                        sb.Append("   ");
                    }
                }

                sb.Append(' ');
                sb.Append(ascii.ToString());
                sb.Append('\n');
                offset += 16;
            }

            return sb.ToString();
        }

        private static char ToAscii(string hexByte)
        {
            if (byte.TryParse(hexByte, System.Globalization.NumberStyles.HexNumber, null, out byte b)
                && b >= 0x20 && b < 0x7F)
            {
                return (char)b;
            }

            return '.';
        }
    }
}
