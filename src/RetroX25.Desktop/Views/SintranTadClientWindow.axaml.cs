using Avalonia.Controls;
using Avalonia.Input;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class SintranTadClientWindow : Window
{
    public SintranTadClientWindow()
    {
        InitializeComponent();
    }

    public SintranTadClientWindow(SintranTadClientWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Handle Enter key in input TextBox
        var inputBox = this.FindControl<TextBox>("InputTextBox");
        if (inputBox != null)
        {
            inputBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    viewModel.SendInputCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }
    }
}
