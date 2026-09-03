using Avalonia.Controls;
using Avalonia.Input;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class PADClientWindow : Window
{
    public PADClientWindow()
    {
        InitializeComponent();
    }

    public PADClientWindow(PADClientWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Wire up KeyDown and TextInput events for character mode
        // All keypresses are sent immediately to the PAD client
        this.KeyDown += (sender, e) =>
        {
            viewModel.HandleKeyDown(e);
        };

        this.TextInput += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Text))
            {
                viewModel.HandleTextInput(e.Text);
            }
        };

        // Focus the window so it can receive keyboard input immediately
        this.Opened += (sender, e) =>
        {
            this.Focus();
        };
    }
}
