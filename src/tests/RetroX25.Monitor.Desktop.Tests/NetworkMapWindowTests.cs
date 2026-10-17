//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using RetroX25.Monitor.Desktop.Theming;
using RetroX25.Monitor.Desktop.ViewModels;
using RetroX25.Monitor.Desktop.Views;

namespace RetroX25.Monitor.Desktop.Tests
{
    /// <summary>
    /// Headless render tests for the network-map window. They validate two things that can only be seen by
    /// actually laying out and rendering the Avalonia UI: (1) the NodifyM editor (via its clipping host) does
    /// not cover the toolbar, and (2) the editor's background grid is suppressed (an empty canvas, not a dense
    /// grid covering everything). <see cref="NodifyM.Avalonia.Controls.NodifyEditor"/> ignores its layout slot
    /// and overflows, so the window wraps it in a clipping <c>Border</c> named <c>EditorHost</c> that occupies
    /// the grid row; these tests assert against that host.
    /// </summary>
    [TestFixture]
    public sealed class NetworkMapWindowTests
    {
        // A fresh, laid-out window with an empty (not-yet-discovered) map.
        private static NetworkMapWindow ShowMap()
        {
            // Ensure the palette is installed so DynamicResource brushes resolve during the render.
            ThemeManager.ApplyTheme(AppTheme.X25);

            // Synchronous UI-invoke so any view-model callbacks run inline on the test thread.
            NetworkMapViewModel vm = new NetworkMapViewModel(action => action());
            NetworkMapWindow window = new NetworkMapWindow
            {
                DataContext = vm,
                Width = 980,
                Height = 760
            };

            window.Show();
            // Let the layout fully settle (several passes) before anyone reads bounds.
            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
            return window;
        }

        [AvaloniaTest]
        public void Editor_host_does_not_cover_the_toolbar()
        {
            NetworkMapWindow window = ShowMap();

            Border? host = FindDescendant<Border>(window, b => b.Name == "EditorHost");
            Border? toolbar = FindDescendant<Border>(window, b => b.Classes.Contains("Toolbar"));

            Assert.That(host, Is.Not.Null, "editor host Border not found");
            Assert.That(toolbar, Is.Not.Null, "toolbar Border not found");
            if (host is null || toolbar is null)
            {
                return;
            }

            // The host must clip so the overflowing editor cannot paint above it (over the toolbar).
            Assert.That(host.ClipToBounds, Is.True, "editor host must clip its content");

            Point hostTopLeft = host.TranslatePoint(new Point(0, 0), window) ?? new Point();
            Point toolbarTopLeft = toolbar.TranslatePoint(new Point(0, 0), window) ?? new Point();
            double toolbarBottom = toolbarTopLeft.Y + toolbar.Bounds.Height;

            Assert.That(toolbar.Bounds.Height, Is.GreaterThan(40),
                "toolbar collapsed — its two rows should be well over 40px tall");
            Assert.That(hostTopLeft.Y, Is.GreaterThanOrEqualTo(toolbarBottom - 1.0),
                "editor host top (" + hostTopLeft.Y + ") sits above the toolbar bottom (" + toolbarBottom + ")");
        }

        [AvaloniaTest]
        public void Editor_background_has_no_grid()
        {
            NetworkMapWindow window = ShowMap();

            Border? host = FindDescendant<Border>(window, b => b.Name == "EditorHost");
            Assert.That(host, Is.Not.Null, "editor host Border not found");
            if (host is null)
            {
                return;
            }

            Point hostTopLeft = host.TranslatePoint(new Point(0, 0), window) ?? new Point();

            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null, "no rendered frame captured");
            if (frame is null)
            {
                return;
            }

            using ILockedFramebuffer fb = frame.Lock();
            int frameHeight = fb.Size.Height;
            int stride = fb.RowBytes;

            // Sample a horizontal line across the middle of the visible editor band. With no nodes discovered,
            // it should be a flat colour; a background grid would introduce many periodic darker pixels.
            int y = (int)(hostTopLeft.Y + host.Bounds.Height / 2);
            int xStart = (int)(hostTopLeft.X + 20);
            int xEnd = (int)(hostTopLeft.X + host.Bounds.Width - 20);
            Assert.That(y, Is.GreaterThan(0).And.LessThan(frameHeight), "sample row is off-frame");
            Assert.That(xEnd - xStart, Is.GreaterThan(100), "editor sample width too small");

            List<int> sums = new List<int>();
            for (int x = xStart; x < xEnd; x++)
            {
                int argb = Marshal.ReadInt32(fb.Address, y * stride + x * 4);
                // Sum of the low three colour channels; order-independent for outlier detection.
                int sum = (argb & 0xFF) + ((argb >> 8) & 0xFF) + ((argb >> 16) & 0xFF);
                sums.Add(sum);
            }

            int median = Median(sums);
            int outliers = 0;
            for (int i = 0; i < sums.Count; i++)
            {
                if (Math.Abs(sums[i] - median) > 20)
                {
                    outliers++;
                }
            }

            double fraction = (double)outliers / sums.Count;
            // A real grid draws a line roughly every ~15px (~6% of the row). No grid → near-zero outliers.
            Assert.That(fraction, Is.LessThan(0.02),
                "editor row has " + (fraction * 100).ToString("0.0") + "% off-colour pixels — a background grid is showing");
        }

        // Depth-first search for the first visual of type T matching the predicate.
        private static T? FindDescendant<T>(Visual root, Func<T, bool> predicate) where T : class
        {
            Stack<Visual> stack = new Stack<Visual>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Visual current = stack.Pop();
                if (current is T typed && predicate(typed))
                {
                    return typed;
                }

                List<Visual> children = new List<Visual>(current.GetVisualChildren());
                for (int i = 0; i < children.Count; i++)
                {
                    stack.Push(children[i]);
                }
            }

            return null;
        }

        private static int Median(List<int> values)
        {
            int[] copy = values.ToArray();
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }
    }
}
