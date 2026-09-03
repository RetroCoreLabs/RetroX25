using Avalonia.Controls;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class SintranTadServerWindow : Window
{
    public SintranTadServerWindow()
    {
        InitializeComponent();
    }

    public SintranTadServerWindow(SintranTadServerWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
