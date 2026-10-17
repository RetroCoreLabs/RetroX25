//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

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
