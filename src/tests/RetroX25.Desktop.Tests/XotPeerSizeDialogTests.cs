//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The window size and packet size boxes of the XOT edit dialog: what a person types ends up in the entry, an empty
    /// box means "use the X.25 settings", a wrong value is refused in the dialog, and a saved entry opens with its values.
    /// </summary>
    [TestFixture]
    public sealed class XotPeerSizeDialogTests
    {
        private static XotInterfaceConfig Peer(byte? window, int? packet)
        {
            return new XotInterfaceConfig
            {
                Name = "P",
                Mode = XotMode.Both,
                RemoteIPAddress = "10.1.2.3",
                RemotePort = 1998,
                LocalPort = 1999,
                RemoteMachineId = "23400000",
                RequestedWindowSize = window,
                RequestedPacketSize = packet,
            };
        }

        [Test]
        public void SavedSizes_AreShownInTheDialog_AndComeBackOut()
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(NullLogger.Instance, Peer(3, 256), false);
            Assert.That(vm.RequestedWindowSize, Is.EqualTo(3));
            Assert.That(vm.RequestedPacketSize, Is.EqualTo(256));
            vm.OkCommand.Execute(null);
            Assert.That(vm.Result, Is.Not.Null, vm.ValidationError);
            Assert.That(vm.Result!.RequestedWindowSize, Is.EqualTo((byte)3));
            Assert.That(vm.Result.RequestedPacketSize, Is.EqualTo(256));
        }

        [Test]
        public void EmptyBoxes_MeanTheXTwentyFiveSettings_AndStayEmpty()
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(NullLogger.Instance, Peer(5, 512), false);
            vm.RequestedWindowSize = null;
            vm.RequestedPacketSize = null;
            vm.OkCommand.Execute(null);
            Assert.That(vm.Result, Is.Not.Null, vm.ValidationError);
            Assert.That(vm.Result!.RequestedWindowSize, Is.Null);
            Assert.That(vm.Result.RequestedPacketSize, Is.Null);
        }

        [TestCase(0)]
        [TestCase(8)]
        [TestCase(-1)]
        public void AWindowOutsideOneToSeven_IsRefusedInTheDialog(int window)
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(NullLogger.Instance, Peer(null, null), false);
            vm.RequestedWindowSize = window;
            vm.OkCommand.Execute(null);
            Assert.That(vm.Result, Is.Null);
            Assert.That(vm.ValidationError, Does.Contain("Window size"));
        }

        [TestCase(0)]
        [TestCase(8)]
        [TestCase(100)]
        [TestCase(8192)]
        public void APacketSizeThatIsNotAPowerOfTwoFrom16To4096_IsRefusedInTheDialog(int packet)
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(NullLogger.Instance, Peer(null, null), false);
            vm.RequestedPacketSize = packet;
            vm.OkCommand.Execute(null);
            Assert.That(vm.Result, Is.Null);
            Assert.That(vm.ValidationError, Does.Contain("Packet size"));
        }

        /// <summary>
        /// The real dialog window: the two boxes are there, a number put in each reaches the entry when OK is pressed, and the
        /// window shows the values of an entry it is opened on.
        /// </summary>
        [AvaloniaTest]
        public void TheDialogWindow_HasTheTwoBoxes_AndTheValuesReachTheEntry()
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(NullLogger.Instance, Peer(null, null), false);
            XotInterfaceEditDialog dialog = new XotInterfaceEditDialog { DataContext = vm, Width = 600, Height = 900 };
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            NumericUpDown? window = dialog.FindControl<NumericUpDown>("WindowSizeBox");
            NumericUpDown? packet = dialog.FindControl<NumericUpDown>("PacketSizeBox");
            Assert.That(window, Is.Not.Null, "no window size box in the dialog");
            Assert.That(packet, Is.Not.Null, "no packet size box in the dialog");
            Assert.That(window!.Value, Is.Null, "an entry without sizes shows empty boxes");
            Assert.That(packet!.Value, Is.Null);

            window.Value = 2;
            packet.Value = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.RequestedWindowSize, Is.EqualTo(2), "the window box does not reach the view-model");
            Assert.That(vm.RequestedPacketSize, Is.EqualTo(1024), "the packet box does not reach the view-model");

            vm.OkCommand.Execute(null);
            Button ok = FindButton(dialog, "OK");
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.Result, Is.Not.Null, vm.ValidationError);
            Assert.That(vm.Result!.RequestedWindowSize, Is.EqualTo((byte)2));
            Assert.That(vm.Result.RequestedPacketSize, Is.EqualTo(1024));

            // The other way: open a new dialog on that result and read the boxes.
            XotInterfaceEditDialogViewModel again = new XotInterfaceEditDialogViewModel(NullLogger.Instance, vm.Result, false);
            XotInterfaceEditDialog second = new XotInterfaceEditDialog { DataContext = again, Width = 600, Height = 900 };
            second.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(second.FindControl<NumericUpDown>("WindowSizeBox")!.Value, Is.EqualTo(2m));
            Assert.That(second.FindControl<NumericUpDown>("PacketSizeBox")!.Value, Is.EqualTo(1024m));
            second.Close();
            dialog.Close();
        }

        private static Button FindButton(Window window, string text)
        {
            List<Visual> all = new List<Visual>(window.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Button button && button.Content is string content && content == text)
                {
                    return button;
                }
            }

            Assert.Fail("no button '" + text + "'");
            throw new System.InvalidOperationException();
        }
    }
}
