using System;
using Microsoft.Extensions.Logging;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// Base class for ViewModels that require logging functionality.
/// Extends ViewModelBase and adds ILogger support.
/// </summary>
public abstract class ViewModelWithLogger : ViewModelBase
{
    protected readonly ILogger Logger;

    /// <summary>
    /// Creates a new instance of ViewModelWithLogger with logging support.
    /// </summary>
    /// <param name="logger">Logger instance for this ViewModel</param>
    protected ViewModelWithLogger(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
