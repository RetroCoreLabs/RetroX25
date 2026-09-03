using Avalonia.Controls;
using Avalonia.Interactivity;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class XotInterfaceEditDialog : Window
{
    public XotInterfaceEditDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        // Check if validation passed by checking if Result is set
        if (DataContext is XotInterfaceEditDialogViewModel viewModel && viewModel.Result != null)
        {
            Close(true); // Return true to indicate OK was clicked
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false); // Return false to indicate Cancel was clicked
    }
}
