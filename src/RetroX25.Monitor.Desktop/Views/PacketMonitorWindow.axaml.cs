using Avalonia.Controls;
using RetroX25.Monitor.Desktop.ViewModels;

namespace RetroX25.Monitor.Desktop.Views
{
    /// <summary>
    /// The packet-analyzer window: a three-pane Wireshark-style view (packet list, layered detail tree, hex).
    /// </summary>
    public partial class PacketMonitorWindow : Window
    {
        /// <summary>
        /// Creates the packet-analyzer window.
        /// </summary>
        public PacketMonitorWindow()
        {
            InitializeComponent();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is PacketMonitorViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
