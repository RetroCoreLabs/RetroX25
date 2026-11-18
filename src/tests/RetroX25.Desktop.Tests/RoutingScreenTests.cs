//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Runtime;

using RetroX25.Desktop.Services;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The routing screen, headless, on the real engine: the own-network card, the route list in lookup order,
    /// the add/edit dialog with its live checks and preview, the test box, and Save to the file the engine reads.
    /// </summary>
    [TestFixture]
    public sealed class RoutingScreenTests
    {
        private sealed class Dialogs : IDialogService
        {
            public bool ConfirmAnswer { get; set; } = true;

            public int ConfirmCount { get; private set; }

            public string LastError { get; private set; } = string.Empty;

            public void ShowInfo(string title, string message) { }

            public void ShowWarning(string title, string message) { }

            public void ShowError(string title, string message) { LastError = message; }

            public Task<bool> ShowConfirmation(string title, string message)
            {
                ConfirmCount++;
                return Task.FromResult(ConfirmAnswer);
            }

            public Task<string?> ShowOpenFileDialog(string title, string? filters = null) => Task.FromResult<string?>(null);

            public Task<string?> ShowSaveFileDialog(string title, string? defaultFileName = null, string? filters = null) => Task.FromResult<string?>(null);
        }

        // A configuration with no lines (nothing listens), the own network Norway and five routes of five kinds,
        // written most general first so the screen has to sort them.
        private static RuntimeConfig Sample()
        {
            RuntimeConfig config = new RuntimeConfig();
            config.Routing.OwnDnics.Add("2422");
            config.Routing.Routes.Add(new X121RouteConfig { Name = "Gateway", Kind = "default", Port = "x75-gw" });
            config.Routing.Routes.Add(new X121RouteConfig { Name = "Sweden", Kind = "dcc", Match = "240", Port = "x75-gw" });
            config.Routing.Routes.Add(new X121RouteConfig { Name = "Datapak", Kind = "dnic", Match = "2402", Port = "x75-gw", BackupPorts = { "nordic" } });
            config.Routing.Routes.Add(new X121RouteConfig { Name = "Customer", Kind = "prefix", Match = "24020001", Port = "nordic" });
            config.Routing.Routes.Add(new X121RouteConfig { Name = "TAD", Kind = "exact", Match = "2422110006", Port = "local" });
            return config;
        }

        private static ServiceProvider Build(RuntimeConfig config, Dialogs dialogs)
        {
            ServiceCollection services = new ServiceCollection();
            App.ConfigureServices(services, config, EmbeddedSwitchOptions.Off);
            services.AddSingleton<IDialogService>(dialogs);
            ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IRuntimeService>().Initialize(config);
            return provider;
        }

        private static RoutingViewModel Screen(ServiceProvider provider)
        {
            RoutingViewModel vm = provider.GetRequiredService<RoutingViewModel>();

            // A test never writes the file the app was started on.
            vm.ConfigFilePath = null;
            return vm;
        }

        private static RouteKindChoice Kind(string word)
        {
            for (int i = 0; i < RouteEditDialogViewModel.Kinds.Length; i++)
            {
                if (RouteEditDialogViewModel.Kinds[i].Word == word)
                {
                    return RouteEditDialogViewModel.Kinds[i];
                }
            }

            throw new ArgumentException(word);
        }

        /// <summary>
        /// The list shows the routes in the order the lookup tries them, not the order they were written:
        /// number, prefix, network, country, default.
        /// </summary>
        [AvaloniaTest]
        public void TheList_IsInLookupOrder_WithKindAndPlainWords()
        {
            using ServiceProvider provider = Build(Sample(), new Dialogs());
            RoutingViewModel vm = Screen(provider);

            Assert.That(vm.Routes.Count, Is.EqualTo(5));
            string[] names = { "TAD", "Customer", "Datapak", "Sweden", "Gateway" };
            string[] kinds = { "Number", "Prefix", "Network", "Country", "Default" };
            for (int i = 0; i < names.Length; i++)
            {
                Assert.That(vm.Routes[i].Name, Is.EqualTo(names[i]), "row " + (i + 1));
                Assert.That(vm.Routes[i].Kind, Is.EqualTo(kinds[i]), "row " + (i + 1));
                Assert.That(vm.Routes[i].Order, Is.EqualTo(i + 1));
            }

            Assert.That(vm.Routes[2].Match, Does.Contain("2402").And.Contain("Sweden"), "the network is named with its country");
            Assert.That(vm.Routes[2].Target, Is.EqualTo("x75-gw, then backup 1 nordic"), "the backup order is shown");
            Assert.That(vm.Routes[0].KindBrush, Is.Not.Null);
            Assert.That(RouteRow.KindColour(X121RouteType.Dnic), Is.Not.EqualTo(RouteRow.KindColour(X121RouteType.Dcc)));
        }

        /// <summary>
        /// The own-network card names the country from X.121 Annex J, explains number completion, and refuses
        /// a DNIC that is not four or five digits.
        /// </summary>
        [AvaloniaTest]
        public void TheOwnNetworkCard_NamesTheCountry_AndRefusesABadDnic()
        {
            using ServiceProvider provider = Build(Sample(), new Dialogs());
            RoutingViewModel vm = Screen(provider);

            Assert.That(vm.OwnNetworkText, Does.Contain("2422").And.Contain("Norway"), vm.OwnNetworkText);

            vm.InternationalPrefix = "0";
            Assert.That(vm.OwnNetworkError, Is.Empty);
            Assert.That(vm.OwnNetworkText, Does.Contain("gets 2422 put in front"), vm.OwnNetworkText);

            // A local number is completed with the own DNIC and then taken by the exact route.
            vm.TestNumber = "110006";
            vm.TestAddressCommand.Execute(null);
            Assert.That(vm.TestResult, Does.Contain("route 'TAD' wins").And.Contain("2422110006"), vm.TestResult);

            vm.OwnDnicsText = "24";
            Assert.That(vm.OwnNetworkError, Does.Contain("24"), "a 2-digit DNIC was taken");
            vm.SaveCommand.Execute(null);
            Assert.That(vm.StatusText, Does.StartWith("Not saved"), "Save went on with a bad own network");
        }

        /// <summary>
        /// The editor checks as it is typed: a short DNIC, a missing port, a name another route has, and a
        /// backup that is the route's own port are each refused with a sentence, and OK gives nothing until
        /// all is well. The country of the digits is shown.
        /// </summary>
        [AvaloniaTest]
        public void TheEditor_ChecksEveryFieldAsItIsTyped()
        {
            RouteEditDialogViewModel d = new RouteEditDialogViewModel(NullLogger.Instance, null, true,
                null, new List<string> { "x75-gw", "nordic", "local" }, new List<string> { "Datapak" });
            d.SelectedKind = Kind("dnic");
            d.Name = "Sweden";
            d.Match = "24";
            Assert.That(d.IsValid, Is.False);
            Assert.That(d.ValidationError, Does.StartWith("Network (DNIC)"), d.ValidationError);

            d.Match = "2402";
            Assert.That(d.MatchCountry, Is.EqualTo("Sweden"));
            Assert.That(d.ValidationError, Does.Contain("port"), "no port was accepted");

            d.Port = "x75-gw";
            Assert.That(d.IsValid, Is.True, d.ValidationError);

            d.Name = "datapak";
            Assert.That(d.ValidationError, Does.Contain("already called"), "a second route of the same name was accepted");
            d.Name = "Sweden";

            d.BackupToAdd = "x75-gw";
            d.AddBackupCommand.Execute(null);
            Assert.That(d.ValidationError, Does.Contain("own port"), d.ValidationError);
            d.SelectedBackup = "x75-gw";
            d.RemoveBackupCommand.Execute(null);

            d.BackupToAdd = "nordic";
            d.AddBackupCommand.Execute(null);
            d.BackupToAdd = "local";
            d.AddBackupCommand.Execute(null);
            d.SelectedBackup = "local";
            d.MoveBackupUpCommand.Execute(null);
            Assert.That(d.BackupPorts[0], Is.EqualTo("local"), "moving a backup up did not change the order");
            Assert.That(d.BackupOrderText, Is.EqualTo("Tried in this order: x75-gw, then backup 1 local, then backup 2 nordic."));

            d.OkCommand.Execute(null);
            Assert.That(d.Result, Is.Not.Null);
            Assert.That(d.Result!.Kind, Is.EqualTo("dnic"));
            Assert.That(d.Result.BackupPorts, Is.EqualTo(new List<string> { "local", "nordic" }));

            d.Match = "x";
            d.OkCommand.Execute(null);
            Assert.That(d.Result, Is.Null, "OK gave a route with a bad match");
        }

        /// <summary>
        /// Add puts a route into the working copy and the list at its place in the lookup order; the preview
        /// in the editor shows the real lookup with the new route in place; the configuration is not changed
        /// until Save.
        /// </summary>
        [AvaloniaTest]
        public void Add_ShowsAPreview_AndTakesItsPlaceInTheOrder()
        {
            RuntimeConfig config = Sample();
            using ServiceProvider provider = Build(config, new Dialogs());
            RoutingViewModel vm = Screen(provider);
            string preview = string.Empty;
            vm.ShowEditor = d =>
            {
                d.SelectedKind = Kind("dnic");
                d.Name = "Denmark";
                d.Match = "2382";
                d.Port = "nordic";
                d.PreviewNumber = "238212345678";
                preview = d.PreviewText;
                d.OkCommand.Execute(null);
                return Task.FromResult(true);
            };

            vm.AddRouteCommand.Execute(null);

            Assert.That(preview, Does.Contain("route 'Denmark' wins"), preview);
            Assert.That(vm.Routes.Count, Is.EqualTo(6));
            Assert.That(vm.Routes[2].Name, Is.EqualTo("Datapak"), "the two networks keep the order they were written in");
            Assert.That(vm.Routes[3].Name, Is.EqualTo("Denmark"));
            Assert.That(vm.SelectedRoute?.Name, Is.EqualTo("Denmark"));
            Assert.That(vm.HasUnsavedChanges, Is.True);
            Assert.That(config.Routing.Routes.Count, Is.EqualTo(5), "the configuration changed before Save");
        }

        /// <summary>
        /// Edit changes the selected route, Delete asks first and removes it, and turning a route off makes the
        /// lookup pass it by.
        /// </summary>
        [AvaloniaTest]
        public void EditDeleteAndTheOnSwitch_ChangeTheWorkingCopy()
        {
            Dialogs dialogs = new Dialogs();
            using ServiceProvider provider = Build(Sample(), dialogs);
            RoutingViewModel vm = Screen(provider);

            vm.SelectedRoute = vm.Routes[1];
            vm.ShowEditor = d =>
            {
                Assert.That(d.Match, Is.EqualTo("24020001"), "the editor did not show the route");
                d.Match = "240200017";
                d.OkCommand.Execute(null);
                return Task.FromResult(true);
            };
            vm.EditRouteCommand.Execute(null);
            Assert.That(vm.Routes[1].Match, Does.Contain("numbers starting 2402 00017"), vm.Routes[1].Match);

            vm.TestNumber = "240200019999";
            vm.TestAddressCommand.Execute(null);
            Assert.That(vm.TestResult, Does.Contain("route 'Datapak' wins"), vm.TestResult);

            // Off: the network route is passed by and the country route takes the number.
            vm.Routes[2].Enabled = false;
            vm.TestAddressCommand.Execute(null);
            Assert.That(vm.TestResult, Does.Contain("route 'Sweden' wins"), vm.TestResult);

            dialogs.ConfirmAnswer = false;
            vm.SelectedRoute = vm.Routes[3];
            vm.DeleteRouteCommand.Execute(null);
            Assert.That(vm.Routes.Count, Is.EqualTo(5), "a No still deleted the route");

            dialogs.ConfirmAnswer = true;
            vm.SelectedRoute = vm.Routes[3];
            vm.DeleteRouteCommand.Execute(null);
            Assert.That(dialogs.ConfirmCount, Is.EqualTo(2));
            Assert.That(vm.Routes.Count, Is.EqualTo(4));
            vm.TestAddressCommand.Execute(null);
            Assert.That(vm.TestResult, Does.Contain("route 'Gateway' wins"), vm.TestResult);
        }

        /// <summary>
        /// The test box gives the lookup's own explanation, the number in international form, and the order the
        /// ports are tried. A line that is not a port of this node is said to be missing (diagnostic 67).
        /// </summary>
        [AvaloniaTest]
        public void TheTestBox_ShowsTheExplanationAndTheBackupOrder()
        {
            using ServiceProvider provider = Build(Sample(), new Dialogs());
            RoutingViewModel vm = Screen(provider);

            vm.TestNumber = "240212345678";
            vm.TestAddressCommand.Execute(null);
            Assert.That(vm.TestResult, Does.Contain("route 'Datapak' wins").And.Contain("network, 4 digits"), vm.TestResult);
            Assert.That(vm.TestResult, Does.Contain("no port 'x75-gw'").And.Contain("diagnostic 67"), vm.TestResult);
            Assert.That(vm.TestResult, Does.Contain("Order tried: x75-gw, then backup 1 nordic."), vm.TestResult);

            vm.TestNumber = "2422110006";
            vm.TestAddressCommand.Execute(null);
            Assert.That(vm.TestResult, Does.Contain("is for this node"), vm.TestResult);
        }

        /// <summary>
        /// Save writes the new form into the configuration, empties the old list, reloads the engine's table,
        /// and rewrites only the routing keys of the file: another section in it is kept, and the file loads
        /// again with the same routes.
        /// </summary>
        [AvaloniaTest]
        public void Save_WritesTheFileTheEngineReads_AndKeepsTheRestOfIt()
        {
            RuntimeConfig config = Sample();
            config.Routing.RoutingTable.Add(new X25RouteEntry
            {
                Name = "OldStyle",
                X121AddressPattern = "2350.*",
                Type = RouteType.Interface,
                Destination = "x75-gw",
            });

            string path = Path.Combine(Path.GetTempPath(), "retrox25-routing-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path,
                "{\n  \"HistoryCapacity\": 77,\n  \"EmbeddedSwitch\": { \"Enabled\": false, \"ConfigFile\": \"keep-me.ini\" },\n"
                + "  \"routing\": { \"Enabled\": true, \"RoutingTable\": [ { \"Name\": \"OldStyle\", \"X121AddressPattern\": \"2350.*\" } ] }\n}\n");
            try
            {
                using ServiceProvider provider = Build(config, new Dialogs());
                RoutingViewModel vm = Screen(provider);
                vm.ConfigFilePath = path;
                Assert.That(vm.Routes.Count, Is.EqualTo(6), "the old-style entry was not read");

                vm.SaveCommand.Execute(null);
                Assert.That(vm.StatusText, Does.StartWith("Saved to"), vm.StatusText);
                Assert.That(vm.HasUnsavedChanges, Is.False);
                Assert.That(config.Routing.Routes.Count, Is.EqualTo(6));
                Assert.That(config.Routing.RoutingTable.Count, Is.EqualTo(0), "the old list was kept and would load twice");
                IRuntimeService runtime = provider.GetRequiredService<IRuntimeService>();
                Assert.That(runtime.Runtime.Router.Table.Routes.Count, Is.EqualTo(6), "the engine was not reloaded");

                string text = File.ReadAllText(path);
                Assert.That(text, Does.Contain("keep-me.ini"), "another section of the file was lost");
                Assert.That(text, Does.Contain("\"HistoryCapacity\": 77"));
                Assert.That(text, Does.Not.Contain("\"Routing\""), "a second routing section was added next to the one in lower case");

                RuntimeConfig again = App.LoadConfigFile(path);
                Assert.That(again.Routing.Routes.Count, Is.EqualTo(6));
                Assert.That(again.Routing.RoutingTable.Count, Is.EqualTo(0));
                Assert.That(again.Routing.OwnDnics, Is.EqualTo(new List<string> { "2422" }));
                Assert.That(RoutingConfigConverter.ToNetworkConfig(again.Routing).Routes.Count, Is.EqualTo(6));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// The Routing window and the Routing tab show the same screen; the window builds and lays out with it.
        /// </summary>
        [AvaloniaTest]
        public void TheRoutingWindow_ShowsTheScreen()
        {
            using ServiceProvider provider = Build(Sample(), new Dialogs());
            RoutesWindow window = new RoutesWindow(Screen(provider)) { Width = 1200, Height = 760 };
            window.Show();
            for (int i = 0; i < 5; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            RoutingView view = (RoutingView)window.Content!;
            Avalonia.Controls.DataGrid? grid = view.FindControl<Avalonia.Controls.DataGrid>("RouteGrid");
            Assert.That(grid, Is.Not.Null, "the route list is not in the window");
            Assert.That(grid!.IsVisible, Is.True);
            window.Close();
        }
    }
}
