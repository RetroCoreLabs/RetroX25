using Avalonia.Controls;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class TADServerWindow : Window
{
    public TADServerWindow()
    {
        InitializeComponent();
    }

    public TADServerWindow(TADServerWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
