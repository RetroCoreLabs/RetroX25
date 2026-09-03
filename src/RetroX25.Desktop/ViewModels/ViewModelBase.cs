using CommunityToolkit.Mvvm.ComponentModel;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// Base class for all ViewModels in the application.
/// Provides INotifyPropertyChanged implementation via CommunityToolkit.Mvvm.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Called when the ViewModel is being disposed.
    /// Override to clean up resources, unsubscribe from events, etc.
    /// </summary>
    public virtual void Dispose()
    {
        // Base implementation does nothing
        // Derived classes should override to clean up
    }
}
