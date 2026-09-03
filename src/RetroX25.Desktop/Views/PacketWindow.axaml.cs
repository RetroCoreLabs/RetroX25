using Avalonia.Controls;
using Avalonia.Input;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class PacketWindow : Window
{
    public PacketWindow()
    {
        InitializeComponent();
    }

    public PacketWindow(PacketWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Add Esc key handler to close window
        this.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        };
    }
}
