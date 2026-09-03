using Avalonia.Controls;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class SimulateReceiveWindow : Window
{
    public SimulateReceiveWindow()
    {
        InitializeComponent();
    }

    public SimulateReceiveWindow(SimulateReceiveWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
