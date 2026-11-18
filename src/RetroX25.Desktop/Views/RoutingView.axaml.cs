//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Avalonia.Controls;
using Avalonia.Input;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The one routing screen (own network, the routes in lookup order, test an address). It is shown both in the
/// Routes window and on the Routing tab of the Configuration window; the data context is a
/// <see cref="RoutingViewModel"/>.
/// </summary>
public partial class RoutingView : UserControl
{
    /// <summary>
    /// Creates the view and loads its layout.
    /// </summary>
    public RoutingView()
    {
        InitializeComponent();
        DataGrid? grid = this.FindControl<DataGrid>("RouteGrid");
        if (grid != null)
        {
            // A double click on a route opens it in the editor.
            grid.DoubleTapped += OnRouteDoubleTapped;
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookOwner();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        HookOwner();
    }

    // The editor opens over the window this view is in, whichever of the two that is.
    private void HookOwner()
    {
        if (DataContext is RoutingViewModel vm && TopLevel.GetTopLevel(this) is Window window)
        {
            vm.SetOwnerWindow(window);
        }
    }

    private void OnRouteDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RoutingViewModel vm && vm.EditRouteCommand.CanExecute(null))
        {
            vm.EditRouteCommand.Execute(null);
        }
    }
}
