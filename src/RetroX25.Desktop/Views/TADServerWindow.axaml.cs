//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

public partial class TADServerWindow : Window
{
    public TADServerWindow()
    {
        InitializeComponent();
    }

    public TADServerWindow(TADServerWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
