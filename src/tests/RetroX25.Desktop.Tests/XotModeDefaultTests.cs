//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Runtime;
using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// "Both ways (peer)" is the mode a NEW XOT entry starts with, and the sample configs use it. Client and Server stay
    /// selectable, and a config file that says Client or Server loads and runs unchanged.
    /// </summary>
    [TestFixture]
    public sealed class XotModeDefaultTests
    {
        [Test]
        public void ANewEntryInTheDialog_StartsAsBothWays()
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(NullLogger.Instance, null, true);
            Assert.That(vm.Mode, Is.EqualTo(XotInterfaceEditDialogViewModel.BothText));
            Assert.That(vm.AvailableModes, Does.Contain(XotInterfaceEditDialogViewModel.ClientText));
            Assert.That(vm.AvailableModes, Does.Contain(XotInterfaceEditDialogViewModel.ServerText));

            vm.Name = "P";
            vm.RemoteIPAddress = "10.1.2.3";
            vm.RemoteMachineId = "23400000";
            vm.OkCommand.Execute(null);
            Assert.That(vm.Result, Is.Not.Null, vm.ValidationError);
            Assert.That(vm.Result!.Mode, Is.EqualTo(XotMode.Both));
        }

        [Test]
        public void ClientAndServerCanStillBePickedInTheDialog()
        {
            XotInterfaceEditDialogViewModel vm = new XotInterfaceEditDialogViewModel(NullLogger.Instance, null, true);
            vm.Name = "P";
            vm.RemoteIPAddress = "10.1.2.3";
            vm.RemoteMachineId = "23400000";
            vm.Mode = XotInterfaceEditDialogViewModel.ClientText;
            vm.OkCommand.Execute(null);
            Assert.That(vm.Result!.Mode, Is.EqualTo(XotMode.Client));

            vm.Mode = XotInterfaceEditDialogViewModel.ServerText;
            vm.OkCommand.Execute(null);
            Assert.That(vm.Result!.Mode, Is.EqualTo(XotMode.Server));
        }

        [Test]
        public void TheSampleConfigsThatHaveAPeerUseBothWays()
        {
            RuntimeConfig toSwitch = App.LoadConfigFile(XotLinkHealthTests.FindSampleFile("x25config-xot-to-switch.json"));
            Assert.That(toSwitch.Routing.XotInterfaces[0].Mode, Is.EqualTo(XotMode.Both));
            RuntimeConfig embedded = App.LoadConfigFile(XotLinkHealthTests.FindSampleFile("x25config-embedded-switch.json"));
            Assert.That(embedded.Routing.XotInterfaces.Count, Is.EqualTo(1), "the pair is one entry now");
            Assert.That(embedded.Routing.XotInterfaces[0].Mode, Is.EqualTo(XotMode.Both));
        }

        /// <summary>
        /// A file written before the change, with one Client entry and one Server entry, loads with those modes and the engine
        /// starts on it.
        /// </summary>
        [Test]
        public void AnOldFileWithClientAndServerEntries_LoadsUnchanged_AndRuns()
        {
            string path = Path.Combine(Path.GetTempPath(), "x25-old-modes-" + Guid.NewGuid().ToString("N") + ".json");
            int serverPort = XotLinkHealthTests.FreePort();
            string json = "{ \"X25\": { \"LocalMachineId\": \"23430200\" }, \"Routing\": { \"Enabled\": true, \"XotInterfaces\": ["
                + "{ \"Name\": \"OldClient\", \"Enabled\": true, \"Mode\": \"Client\", \"RemoteIPAddress\": \"127.0.0.1\", \"RemotePort\": 2200, \"RemoteMachineId\": \"23500000\" },"
                + "{ \"Name\": \"OldServer\", \"Enabled\": true, \"Mode\": \"Server\", \"LocalPort\": " + serverPort + ", \"RemoteMachineId\": \"23500000\" }"
                + "] } }";
            File.WriteAllText(path, json);
            try
            {
                RuntimeConfig config = App.LoadConfigFile(path);
                Assert.That(config.Routing.XotInterfaces.Count, Is.EqualTo(2));
                Assert.That(config.Routing.XotInterfaces[0].Mode, Is.EqualTo(XotMode.Client));
                Assert.That(config.Routing.XotInterfaces[1].Mode, Is.EqualTo(XotMode.Server));

                ServiceCollection services = new ServiceCollection();
                App.ConfigureServices(services, config, null);
                using ServiceProvider provider = services.BuildServiceProvider();
                IRuntimeService service = provider.GetRequiredService<IRuntimeService>();
                try
                {
                    service.Initialize(config);
                    Assert.That(service.Runtime.TransportInterfaces.ContainsKey("OldClient"), Is.True);
                    Assert.That(service.Runtime.TransportInterfaces.ContainsKey("OldServer"), Is.True);
                }
                finally
                {
                    service.Dispose();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
