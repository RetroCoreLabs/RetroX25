using Avalonia.Controls;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class XMSGServerWindow : Window
{
    public XMSGServerWindow()
    {
        InitializeComponent();
    }

    public XMSGServerWindow(XMSGServerWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
