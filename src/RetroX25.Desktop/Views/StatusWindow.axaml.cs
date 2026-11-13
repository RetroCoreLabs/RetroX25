//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Views;

/// <summary>
/// The Status window (Ctrl+1): the one place that shows the counters, as one table with a row for each
/// counter and a column for the engine's total and for each link and each transport interface.
/// </summary>
/// <remarks>
/// The columns depend on the links and interfaces that exist, so the table is built here in code
/// instead of in the layout file. The cells are named <c>Cell_row_column</c> (row and column count
/// from 0; column 0 is the total) and the row names <c>Label_row</c>, so a test can find them.
/// </remarks>
public partial class StatusWindow : Window
{
    private static readonly FontFamily NumberFont = new FontFamily("Consolas,Courier New");

    // What the table on the screen was built from; when it is the same shape the cells are only updated.
    private string _shape = string.Empty;

    // The cell controls of the table on the screen, row by row.
    private readonly List<TextBlock[]> _cells = new List<TextBlock[]>();

    /// <summary>
    /// Creates the window and loads its layout, with no view-model attached.
    /// </summary>
    public StatusWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window bound to <paramref name="viewModel"/>, and closes it when the user
    /// presses Esc.
    /// </summary>
    /// <param name="viewModel">
    /// The view-model the window binds to.
    /// </param>
    public StatusWindow(StatusWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Show the table now, and again each time the view-model has a new one.
        ShowTable(viewModel.Table);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (sender, e) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // Add Esc key handler to close window
        this.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusWindowViewModel.Table) && sender is StatusWindowViewModel vm)
        {
            ShowTable(vm.Table);
        }
    }

    // Puts the table on the screen. A table with the same columns and rows as the one shown only gets
    // new cell texts; a different one (a link came or went) is built again.
    private void ShowTable(StatisticsTable table)
    {
        Border? host = this.FindControl<Border>("TableHost");
        if (host == null)
        {
            return;
        }

        string shape = ShapeOf(table);
        if (shape != _shape || host.Child == null)
        {
            host.Child = BuildGrid(table);
            _shape = shape;
            return;
        }

        for (int r = 0; r < table.Rows.Count; r++)
        {
            StatisticsRow row = table.Rows[r];
            for (int c = 0; c < row.Cells.Length; c++)
            {
                SetCell(_cells[r][c], row.Cells[c], row.IsProblem);
            }
        }
    }

    // A text that is the same for two tables with the same headers, kinds and row labels.
    private static string ShapeOf(StatisticsTable table)
    {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < table.Headers.Count; i++)
        {
            text.Append(table.Headers[i]).Append('\u0001').Append(table.Kinds[i]).Append('\u0002');
        }

        for (int i = 0; i < table.Rows.Count; i++)
        {
            text.Append(table.Rows[i].Section).Append('\u0001').Append(table.Rows[i].Label).Append('\u0002');
        }

        return text.ToString();
    }

    private Grid BuildGrid(StatisticsTable table)
    {
        _cells.Clear();
        int columns = table.Headers.Count;

        Grid grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int c = 0; c < columns; c++)
        {
            ColumnDefinition column = new ColumnDefinition(GridLength.Auto);
            column.MinWidth = 110;
            grid.ColumnDefinitions.Add(column);
        }

        // Header row: the name of each column and, under it, what kind it is.
        int gridRow = 0;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(Place(new TextBlock { Text = "Counter", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 12, 6) }, gridRow, 0, 1));
        for (int c = 0; c < columns; c++)
        {
            StackPanel head = new StackPanel { Margin = new Thickness(8, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Right };
            head.Children.Add(new TextBlock { Name = "Header_" + c, Text = table.Headers[c], FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
            head.Children.Add(new TextBlock { Text = table.Kinds[c], FontSize = 10, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Right });
            grid.Children.Add(Place(head, gridRow, c + 1, 1));
        }

        string section = string.Empty;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            StatisticsRow row = table.Rows[r];

            // A heading above the first row of each group.
            if (row.Section != section)
            {
                section = row.Section;
                gridRow++;
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.Children.Add(Place(
                    new TextBlock { Text = section, FontWeight = FontWeight.SemiBold, FontSize = 14, Margin = new Thickness(0, 12, 0, 4) },
                    gridRow, 0, columns + 1));
            }

            gridRow++;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            TextBlock label = new TextBlock
            {
                Name = "Label_" + r,
                Text = row.Label,
                FontSize = 12,
                Margin = new Thickness(0, 1, 12, 1)
            };
            if (row.IsProblem)
            {
                // A counter of something going wrong has a red name. (A null brush would make the
                // text invisible, so the colour is only set when it is wanted.)
                label.Foreground = Brushes.Red;
            }

            grid.Children.Add(Place(label, gridRow, 0, 1));

            TextBlock[] rowCells = new TextBlock[row.Cells.Length];
            for (int c = 0; c < row.Cells.Length; c++)
            {
                TextBlock cell = new TextBlock
                {
                    Name = "Cell_" + r + "_" + c,
                    FontFamily = NumberFont,
                    FontSize = 12,
                    Margin = new Thickness(8, 1, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                SetCell(cell, row.Cells[c], row.IsProblem);
                rowCells[c] = cell;
                grid.Children.Add(Place(cell, gridRow, c + 1, 1));
            }

            _cells.Add(rowCells);
        }

        return grid;
    }

    // Sets the text of a cell. A "-" (no such counter here) is dimmed, and a counter of something
    // going wrong is red as soon as it is not zero.
    private static void SetCell(TextBlock cell, string text, bool isProblem)
    {
        cell.Text = text;
        bool none = text == StatisticsReader.NotAvailable;
        cell.Opacity = none ? 0.4 : 1.0;
        if (isProblem && !none && text != "0")
        {
            cell.Foreground = Brushes.Red;
        }
        else
        {
            // Back to the theme's own text colour.
            cell.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    private static T Place<T>(T control, int row, int column, int columnSpan) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, columnSpan);
        return control;
    }
}
