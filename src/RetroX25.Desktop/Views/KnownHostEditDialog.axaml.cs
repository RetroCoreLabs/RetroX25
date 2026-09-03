using Avalonia.Controls;
using Avalonia.Interactivity;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class KnownHostEditDialog : Window
{
    public KnownHostEditDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        // Check if validation passed by checking if Result is set
        if (DataContext is KnownHostEditDialogViewModel viewModel && viewModel.Result != null)
        {
            Close(true); // Return true to indicate OK was clicked
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false); // Return false to indicate Cancel was clicked
    }
}
