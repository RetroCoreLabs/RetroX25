using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Protocols.X25.Monitor;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// One port's live status row in the link-status view.
    /// </summary>
    public sealed class PortStatusRowViewModel
    {
        /// <summary>
        /// The port identifier.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// The port mode text.
        /// </summary>
        public string Mode { get; }

        /// <summary>
        /// The routes that send traffic out this port.
        /// </summary>
        public string Routes { get; }

        /// <summary>
        /// Whether the port is up (bound).
        /// </summary>
        public bool Active { get; }

        /// <summary>
        /// Whether a peer is actually connected (trunk linked).
        /// </summary>
        public bool PeerConnected { get; }

        /// <summary>
        /// Transport open/dial attempts.
        /// </summary>
        public int ConnectAttempts { get; }

        /// <summary>
        /// Wire bytes sent.
        /// </summary>
        public long BytesSent { get; }

        /// <summary>
        /// Wire bytes received.
        /// </summary>
        public long BytesReceived { get; }

        /// <summary>
        /// Calls received.
        /// </summary>
        public uint CallsIn { get; }

        /// <summary>
        /// Calls placed.
        /// </summary>
        public uint CallsOut { get; }

        /// <summary>
        /// Calls cleared.
        /// </summary>
        public uint CallsCleared { get; }

        /// <summary>
        /// Packets received.
        /// </summary>
        public uint PacketsIn { get; }

        /// <summary>
        /// Packets sent.
        /// </summary>
        public uint PacketsOut { get; }

        /// <summary>
        /// Creates a port status row from a status entry.
        /// </summary>
        /// <param name="status">
        /// The port status entry.
        /// </param>
        public PortStatusRowViewModel(PortStatus status)
        {
            Id = status.Id;
            Mode = status.Mode;
            Routes = status.Routes;
            Active = status.Active;
            PeerConnected = status.PeerConnected;
            ConnectAttempts = status.ConnectAttempts;
            BytesSent = status.BytesSent;
            BytesReceived = status.BytesReceived;
            CallsIn = status.CallsIn;
            CallsOut = status.CallsOut;
            CallsCleared = status.CallsCleared;
            PacketsIn = status.PacketsIn;
            PacketsOut = status.PacketsOut;
        }
    }

    /// <summary>
    /// The link-status window view-model: connects to a selected switch and shows live per-port state and
    /// traffic counters from the server's status snapshots.
    /// </summary>
    public sealed partial class LinkStatusViewModel : ObservableObject, IDisposable
    {
        private readonly MonitorClient _client = new MonitorClient();
        private readonly Action<Action> _uiInvoke;

        [ObservableProperty]
        private SwitchEndpoint? _selectedSwitch;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _status = "Disconnected";

        [ObservableProperty]
        private int _activeCircuits;

        /// <summary>
        /// The known switch endpoints to choose from, shared with the shell.
        /// </summary>
        public ObservableCollection<SwitchEndpoint> Switches { get; }

        /// <summary>
        /// The live per-port status rows.
        /// </summary>
        public ObservableCollection<PortStatusRowViewModel> Ports { get; } = new ObservableCollection<PortStatusRowViewModel>();

        /// <summary>
        /// Creates the link-status view-model.
        /// </summary>
        /// <param name="switches">
        /// The shared switch endpoints.
        /// </param>
        /// <param name="uiInvoke">
        /// Marshals work onto the UI thread; defaults to the Avalonia dispatcher.
        /// </param>
        public LinkStatusViewModel(ObservableCollection<SwitchEndpoint> switches, Action<Action>? uiInvoke = null)
        {
            Switches = switches;
            _selectedSwitch = switches.Count > 0 ? switches[0] : null;
            _uiInvoke = uiInvoke ?? (action => Dispatcher.UIThread.Post(action));
            _client.StatusReceived += OnStatusReceived;
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
        /// Applies a status snapshot to the view (rebuilds the port rows).
        /// </summary>
        /// <param name="snapshot">
        /// The status snapshot.
        /// </param>
        public void ApplyStatus(StatusSnapshot snapshot)
        {
            ActiveCircuits = snapshot.ActiveCircuits;
            Ports.Clear();
            for (int i = 0; i < snapshot.Ports.Count; i++)
            {
                Ports.Add(new PortStatusRowViewModel(snapshot.Ports[i]));
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _client.Dispose();
        }

        private void OnStatusReceived(StatusSnapshot snapshot)
        {
            _uiInvoke(() => ApplyStatus(snapshot));
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
    }
}
