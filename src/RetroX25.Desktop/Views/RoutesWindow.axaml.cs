using Avalonia.Controls;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class RoutesWindow : Window
{
    public RoutesWindow()
    {
        InitializeComponent();
    }

    public RoutesWindow(RoutesWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
