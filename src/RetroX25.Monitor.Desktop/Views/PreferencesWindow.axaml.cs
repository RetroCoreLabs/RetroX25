//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using Avalonia.Interactivity;
using RetroX25.Monitor.Desktop.ViewModels;

namespace RetroX25.Monitor.Desktop.Views
{
    /// <summary>
    /// The Preferences dialog. Theme changes preview live via the view-model; OK persists the choice,
    /// Cancel restores the theme that was active when the dialog opened.
    /// </summary>
    public partial class PreferencesWindow : Window
    {
        /// <summary>
        /// Creates the Preferences window.
        /// </summary>
        public PreferencesWindow()
        {
            InitializeComponent();
        }

        private void OnOk(object? sender, RoutedEventArgs e)
        {
            if (DataContext is PreferencesViewModel vm)
            {
                vm.Commit();
            }

            Close();
        }

        private void OnCancel(object? sender, RoutedEventArgs e)
        {
            if (DataContext is PreferencesViewModel vm)
            {
                vm.Revert();
            }

            Close();
        }
    }
}
