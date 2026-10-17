//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using CommunityToolkit.Mvvm.ComponentModel;
using RetroX25.Monitor.Desktop.Services;
using RetroX25.Monitor.Desktop.Theming;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// One selectable theme in the Preferences dialog: the underlying <see cref="AppTheme"/> plus a
    /// friendly label for the combo box.
    /// </summary>
    public sealed class ThemeChoice
    {
        /// <summary>
        /// The theme this choice selects.
        /// </summary>
        public AppTheme Theme { get; }

        /// <summary>
        /// The label shown in the picker.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Creates a theme choice.
        /// </summary>
        /// <param name="theme">
        /// The underlying theme.
        /// </param>
        /// <param name="label">
        /// The friendly label.
        /// </param>
        public ThemeChoice(AppTheme theme, string label)
        {
            Theme = theme;
            Label = label;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// The Preferences dialog view-model. Selecting a theme previews it live (the whole UI restyles);
    /// <see cref="Commit"/> persists the choice and <see cref="Revert"/> restores the theme that was active
    /// when the dialog opened.
    /// </summary>
    public sealed partial class PreferencesViewModel : ViewModelBase
    {
        // The theme active when the dialog opened, restored on Cancel.
        private readonly AppTheme _originalTheme;

        [ObservableProperty]
        private ThemeChoice _selectedTheme;

        /// <summary>
        /// The available theme choices.
        /// </summary>
        public ThemeChoice[] Themes { get; }

        /// <summary>
        /// Creates the Preferences view-model from the current preferences.
        /// </summary>
        /// <param name="prefs">
        /// The preferences whose values seed the dialog.
        /// </param>
        public PreferencesViewModel(AppPreferences prefs)
        {
            _originalTheme = prefs != null ? prefs.Theme : ThemeManager.CurrentTheme;

            Themes = new ThemeChoice[]
            {
                new ThemeChoice(AppTheme.X25, "X.25 — telecom (cyan)"),
                new ThemeChoice(AppTheme.RetroFs, "RetroFS (green / orange)")
            };

            // Select the choice matching the persisted theme (default to the first).
            _selectedTheme = Themes[0];
            for (int i = 0; i < Themes.Length; i++)
            {
                if (Themes[i].Theme == _originalTheme)
                {
                    _selectedTheme = Themes[i];
                    break;
                }
            }
        }

        // Selecting a theme in the picker previews it immediately.
        partial void OnSelectedThemeChanged(ThemeChoice value)
        {
            if (value != null)
            {
                ThemeManager.ApplyTheme(value.Theme);
            }
        }

        /// <summary>
        /// Persists the selected theme (called on OK).
        /// </summary>
        public void Commit()
        {
            // Read-modify-write so persisting the theme never clobbers the remembered switch list.
            AppPreferences prefs = PreferencesStore.Load();
            prefs.Theme = SelectedTheme != null ? SelectedTheme.Theme : _originalTheme;
            PreferencesStore.Save(prefs);
        }

        /// <summary>
        /// Restores the theme that was active when the dialog opened (called on Cancel).
        /// </summary>
        public void Revert()
        {
            if (ThemeManager.CurrentTheme != _originalTheme)
            {
                ThemeManager.ApplyTheme(_originalTheme);
            }
        }
    }
}
