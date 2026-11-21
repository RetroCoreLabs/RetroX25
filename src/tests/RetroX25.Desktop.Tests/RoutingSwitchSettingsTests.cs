//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Runtime;

using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The Routing screen's switch settings: "Max calls (whole switch)" (0 = no limit) and the Lines list with each
    /// line's channel count, read from and saved to the built-in switch's .ini file and the app's own file.
    /// </summary>
    [TestFixture]
    public sealed class RoutingSwitchSettingsTests
    {
        private sealed class Dialogs : IDialogService
        {
            public void ShowInfo(string title, string message) { }

            public void ShowWarning(string title, string message) { }

            public void ShowError(string title, string message) { }

            public Task<bool> ShowConfirmation(string title, string message) => Task.FromResult(true);

            public Task<string?> ShowOpenFileDialog(string title, string? filters = null) => Task.FromResult<string?>(null);

            public Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null) => Task.FromResult<string?>(null);
        }

        private string _iniPath = string.Empty;
        private ServiceProvider? _provider;

        private const string SwitchFile =
            "# the built-in switch\n" +
            "[switch]\n" +
            "dnic = 2340\n" +
            "max_calls = 3\n" +
            "\n" +
            "[port.0]\n" +
            "name = callers\n" +
            "type = access\n" +
            "transport = xot\n" +
            "mode = dce\n" +
            "port = 0\n" +
            "lci_count = 64\n" +
            "\n" +
            "[port.1]\n" +
            "# a trunk with no channel setting\n" +
            "type = trunk\n" +
            "transport = xot\n" +
            "mode = dte\n" +
            "host = 127.0.0.1\n" +
            "port = 1998\n";

        [SetUp]
        public void SetUp()
        {
            _iniPath = Path.Combine(Path.GetTempPath(), "retrox25-switch-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(_iniPath, SwitchFile);
        }

        [TearDown]
        public void TearDown()
        {
            _provider?.Dispose();
            if (File.Exists(_iniPath))
            {
                File.Delete(_iniPath);
            }
        }

        private RoutingViewModel Screen(string? switchFile)
        {
            RuntimeConfig config = new RuntimeConfig();
            config.Routing.XotInterfaces.Add(new XotInterfaceConfig { Name = "app-xot", Mode = XotMode.Client, Enabled = false });
            _provider?.Dispose();
            ServiceCollection services = new ServiceCollection();

            // Enabled = false: the file is named, so the screen edits it, but no switch is started by the test.
            App.ConfigureServices(services, config, new EmbeddedSwitchOptions { Enabled = false, ConfigFile = switchFile ?? string.Empty });
            services.AddSingleton<IDialogService>(new Dialogs());
            _provider = services.BuildServiceProvider();
            _provider.GetRequiredService<IRuntimeService>().Initialize(config);
            RoutingViewModel vm = _provider.GetRequiredService<RoutingViewModel>();

            // A test never writes the file the app was started on.
            vm.ConfigFilePath = null;
            return vm;
        }

        private static LineRow Line(RoutingViewModel vm, string name)
        {
            for (int i = 0; i < vm.Lines.Count; i++)
            {
                if (vm.Lines[i].Name == name)
                {
                    return vm.Lines[i];
                }
            }

            throw new AssertionException("no line " + name);
        }

        [Test]
        public void Load_ShowsMaxCalls_AndEveryLineWithItsChannelCount()
        {
            RoutingViewModel vm = Screen(_iniPath);

            Assert.That(vm.HasSwitchFile, Is.True);
            Assert.That(vm.MaxCallsText, Is.EqualTo("3"));
            Assert.That(Line(vm, "callers").ChannelsText, Is.EqualTo("64"));
            Assert.That(Line(vm, "callers").Kind, Is.EqualTo("access, XOT"));
            Assert.That(Line(vm, "port.1").ChannelsText, Is.EqualTo("4095 (default)"), "a trunk with no lci_count has the full range");
            Assert.That(Line(vm, "port.1").Kind, Is.EqualTo("trunk, XOT"));
            Assert.That(Line(vm, "app-xot").ChannelsText, Is.EqualTo("4095 (default)"));
            Assert.That(vm.HasUnsavedChanges, Is.False);
        }

        private const string Routes =
            "\n# the switch's routes\n" +
            "[route.0]\n" +
            "name = far\n" +
            "prefix = 2341\n" +
            "port = 1\n" +
            "\n" +
            "[route.1]\n" +
            "number = 23400001\n" +
            "port = callers\n";

        /// <summary>
        /// The switch's own routes are listed; turning one off and deleting another is saved into the switch file, and
        /// the rest of the file is kept (added 2026-11-21).
        /// </summary>
        [Test]
        public void SwitchRoutes_AreListed_AndAnOffAndADeleteAreSavedIntoTheSwitchFile()
        {
            File.WriteAllText(_iniPath, SwitchFile + Routes);
            RoutingViewModel vm = Screen(_iniPath);

            Assert.That(vm.SwitchRoutes.Count, Is.EqualTo(2));
            Assert.That(vm.SwitchRoutes[0].Name, Is.EqualTo("far"));
            Assert.That(vm.SwitchRoutes[1].Target, Does.Contain("callers"));

            vm.SwitchRoutes[0].Enabled = false;
            vm.SelectedSwitchRoute = vm.SwitchRoutes[1];
            vm.DeleteSwitchRouteCommand.Execute(null);
            Assert.That(vm.SwitchRoutes.Count, Is.EqualTo(1));
            vm.SaveCommand.Execute(null);

            string text = File.ReadAllText(_iniPath).Replace("\r\n", "\n");
            Assert.That(text, Does.StartWith("# the built-in switch\n[switch]\n"));
            Assert.That(text, Does.Contain("# the switch's routes\n[route.0]\nname = far\nprefix = 2341\nport = 1\nenabled = no"));
            Assert.That(text, Does.Not.Contain("[route.1]"));
            Assert.That(text, Does.Not.Contain("23400001"));

            RoutingViewModel again = Screen(_iniPath);
            Assert.That(again.SwitchRoutes.Count, Is.EqualTo(1));
            Assert.That(again.SwitchRoutes[0].Enabled, Is.False);
        }

        [Test]
        public void Load_WithoutMaxCalls_ShowsZero()
        {
            File.WriteAllText(_iniPath, SwitchFile.Replace("max_calls = 3\n", string.Empty));
            RoutingViewModel vm = Screen(_iniPath);
            Assert.That(vm.MaxCallsText, Is.EqualTo("0"), "absent = 0 = no limit");
        }

        [Test]
        public void NoSwitchFile_TheFieldIsOff_AndTheAppLinesAreStillListed()
        {
            RoutingViewModel vm = Screen(null);
            Assert.That(vm.HasSwitchFile, Is.False);
            Assert.That(vm.MaxCallsText, Is.EqualTo("0"));
            Assert.That(Line(vm, "app-xot").ChannelsText, Is.EqualTo("4095 (default)"));
        }

        [Test]
        public void BadInput_IsRefused_AndNothingIsWritten()
        {
            RoutingViewModel vm = Screen(_iniPath);

            vm.MaxCallsText = "12a";
            Assert.That(vm.MaxCallsError, Is.Not.Empty);
            vm.SaveCommand.Execute(null);
            Assert.That(vm.StatusText, Does.StartWith("Not saved"));
            Assert.That(File.ReadAllText(_iniPath), Is.EqualTo(SwitchFile));

            vm.MaxCallsText = "-1";
            Assert.That(vm.MaxCallsError, Is.Not.Empty, "digits only");

            vm.MaxCallsText = "5";
            Assert.That(vm.MaxCallsError, Is.Empty);
            Line(vm, "callers").ChannelCountText = "4096";
            Assert.That(Line(vm, "callers").Error, Is.Not.Empty, "the last channel may be at most 4095");
            Line(vm, "port.1").ChannelCountText = "0";
            Assert.That(Line(vm, "port.1").Error, Is.Not.Empty, "at least one channel");
            vm.SaveCommand.Execute(null);
            Assert.That(vm.StatusText, Does.StartWith("Not saved"));
            Assert.That(File.ReadAllText(_iniPath), Is.EqualTo(SwitchFile));
        }

        [Test]
        public void EmptyIsZero_AndZeroIsWrittenAsZeroWhereTheKeyWas()
        {
            RoutingViewModel vm = Screen(_iniPath);
            vm.MaxCallsText = string.Empty;
            Assert.That(vm.MaxCallsError, Is.Empty, "empty is read as 0");
            vm.SaveCommand.Execute(null);

            Assert.That(File.ReadAllText(_iniPath), Does.Contain("max_calls = 0"));
            Assert.That(Screen(_iniPath).MaxCallsText, Is.EqualTo("0"));
        }

        [Test]
        public void Edit_Save_ReadBack_KeepsTheRestOfTheFile()
        {
            RoutingViewModel vm = Screen(_iniPath);
            vm.MaxCallsText = "7";
            Line(vm, "callers").ChannelCountText = string.Empty;   // back to the default
            Line(vm, "port.1").ChannelCountText = "100";
            Assert.That(vm.HasUnsavedChanges, Is.True);
            Assert.That(Line(vm, "port.1").ChannelsText, Is.EqualTo("100"));

            vm.SaveCommand.Execute(null);
            Assert.That(vm.StatusText, Does.StartWith("Saved"), vm.StatusText);
            Assert.That(vm.HasUnsavedChanges, Is.False);

            string text = File.ReadAllText(_iniPath);
            Assert.That(text, Does.Contain("# the built-in switch"));
            Assert.That(text, Does.Contain("# a trunk with no channel setting"));
            Assert.That(text, Does.Contain("max_calls = 7"));
            Assert.That(text, Does.Not.Contain("lci_count = 64"));
            Assert.That(text, Does.Contain("lci_count = 100"));

            RoutingViewModel again = Screen(_iniPath);
            Assert.That(again.MaxCallsText, Is.EqualTo("7"));
            Assert.That(Line(again, "callers").ChannelsText, Is.EqualTo("4095 (default)"));
            Assert.That(Line(again, "port.1").ChannelsText, Is.EqualTo("100"));
        }

        [Test]
        public void AnAppXotLine_ChannelCount_GoesIntoTheRunningConfiguration()
        {
            RoutingViewModel vm = Screen(_iniPath);
            Line(vm, "app-xot").ChannelCountText = "256";
            vm.SaveCommand.Execute(null);

            RuntimeConfig config = _provider!.GetRequiredService<RuntimeConfig>();
            Assert.That(config.Routing.XotInterfaces[0].LciCount, Is.EqualTo((ushort)256));
            Assert.That(config.Routing.XotInterfaces[0].LciCountIsSet, Is.True);
        }
    }
}
