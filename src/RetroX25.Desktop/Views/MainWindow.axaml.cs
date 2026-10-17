//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The main application window.
/// </summary>
/// <remarks>
/// <see cref="App"/> creates it at startup and sets a <see cref="ViewModels.MainWindowViewModel"/>
/// as its data context.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// Creates the window and loads its layout.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
