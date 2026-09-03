using CommunityToolkit.Mvvm.ComponentModel;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// The common base for the application's view-models. It supplies
    /// <see cref="CommunityToolkit.Mvvm.ComponentModel.ObservableObject"/> change notification so views can
    /// bind, and gives the app a single place to add cross-cutting view-model behaviour later.
    /// </summary>
    public abstract class ViewModelBase : ObservableObject
    {
    }
}
