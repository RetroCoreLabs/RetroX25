using Avalonia.Controls;
using RetroX25.Monitor.Desktop.ViewModels;

namespace RetroX25.Monitor.Desktop.Views
{
    /// <summary>
    /// The log-viewer window: a filterable, searchable live view of a switch's log stream.
    /// </summary>
    public partial class LogWindow : Window
    {
        /// <summary>
        /// Creates the log-viewer window.
        /// </summary>
        public LogWindow()
        {
            InitializeComponent();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is LogViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
