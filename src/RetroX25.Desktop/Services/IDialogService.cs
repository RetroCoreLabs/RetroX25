using System.Threading.Tasks;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Service for displaying dialogs and message boxes.
/// Abstracts platform-specific dialog implementations.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an information message box.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Message content</param>
    void ShowInfo(string title, string message);

    /// <summary>
    /// Shows a warning message box.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Message content</param>
    void ShowWarning(string title, string message);

    /// <summary>
    /// Shows an error message box.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Message content</param>
    void ShowError(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Message content</param>
    /// <returns>True if user clicked Yes, false otherwise</returns>
    Task<bool> ShowConfirmation(string title, string message);

    /// <summary>
    /// Shows an open file dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="filters">File type filters (e.g., "JSON files:*.json;All files:*.*")</param>
    /// <returns>Selected file path or null if canceled</returns>
    Task<string?> ShowOpenFileDialog(string title, string? filters = null);

    /// <summary>
    /// Shows a save file dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="defaultFileName">Default file name</param>
    /// <param name="filters">File type filters (e.g., "JSON files:*.json;All files:*.*")</param>
    /// <returns>Selected file path or null if canceled</returns>
    Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null);
}
