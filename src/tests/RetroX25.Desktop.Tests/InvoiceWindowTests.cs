//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroX25.Desktop.ViewModels;
using RetroX25.Desktop.Views;

namespace RetroX25.Desktop.Tests
{
    /// <summary>
    /// The Bill window: a record file and a price list in, the account's bill on the paper sheet out, with a total worked
    /// out by hand. The sums themselves are tested in RetroIO (<c>InvoiceTests</c>); this checks the window reads the
    /// files, lists the paying accounts, and shows the bill, and that a bad file is said in words, not billed around.
    /// </summary>
    [TestFixture]
    public class InvoiceWindowTests
    {
        private static readonly string[] TariffFile =
        {
            "[tariff]",
            "currency = NOK",
            "home_dnic = 2342",
            "monthly_fee = 25000",
            "utc_offset = 60",
            "[tariff.home]",
            "call = 50",
            "per_unit = 30",
            "per_kilosegment = 200",
            "[tariff.other]",
            "call = 300",
            "per_unit = 150",
            "per_kilosegment = 1000",
        };

        private string _dir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "bill-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_dir, true);
        }

        // One call of lineA within the network, 2026-11-02 10:00 local, connected 150 s, 4 + 2 segments:
        // 0.50 + 3 minutes x 0.30 + ceil(6 x 2.00 / 1000) = 50 + 90 + 2 = 142 øre; with 250.00 a month, 251.42.
        private (string Records, string Tariff) Files()
        {
            Cdr call = new Cdr
            {
                StartTimeMs = 1_000_000,
                ConnectedTimeMs = 1_000_500,
                EndTimeMs = 1_150_500,
                StartWallSeconds = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                Calling = "23420001",
                Called = "23420100",
                SegmentsForward = 4,
                SegmentsReverse = 2,
                Account = "lineA",
            };
            string records = Path.Combine(_dir, "calls.cdr");
            string tariff = Path.Combine(_dir, "tariff.conf");
            File.WriteAllText(records, CdrFileWriter.Format(call) + "\n");
            File.WriteAllLines(tariff, TariffFile);
            return (records, tariff);
        }

        private static InvoiceWindowViewModel NewViewModel()
        {
            return new InvoiceWindowViewModel(NullLogger<InvoiceWindowViewModel>.Instance, null,
                () => new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        }

        [AvaloniaTest]
        public void TheBill_IsShownOnTheSheet_WithTheTotalWorkedOutByHand()
        {
            (string records, string tariff) = Files();
            InvoiceWindowViewModel vm = NewViewModel();
            Assert.That(vm.Month, Is.EqualTo("2026-11"), "this month by default");

            InvoiceWindow window = new InvoiceWindow(vm);
            window.Show();
            vm.RecordFile = records;
            vm.TariffFile = tariff;
            vm.LoadAccountsCommand.Execute(null);
            Assert.That(vm.Accounts, Is.EqualTo(new[] { "lineA" }));
            Assert.That(vm.SelectedAccount, Is.EqualTo("lineA"));

            vm.MakeBillCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            SelectableTextBlock sheet = window.FindControl<SelectableTextBlock>("BillSheet")!;
            Assert.That(vm.ErrorText, Is.Empty);
            Assert.That(sheet.Text, Does.Contain("TOTAL DUE"));
            Assert.That(sheet.Text, Does.Contain("251.42"));
            Assert.That(sheet.Text, Does.Contain("23420100"));
            window.Close();
        }

        [AvaloniaTest]
        public void ABadRecordLine_IsSaidInWords_AndNoBillIsShown()
        {
            (string records, string tariff) = Files();
            File.AppendAllText(records, "not,a,record\n");
            InvoiceWindowViewModel vm = NewViewModel();
            vm.RecordFile = records;
            vm.TariffFile = tariff;
            vm.SelectedAccount = "lineA";

            vm.MakeBillCommand.Execute(null);

            Assert.That(vm.BillText, Is.Empty);
            Assert.That(vm.ErrorText, Does.Contain("record line 2"));
        }

        /// <summary>
        /// The price list example in <c>docs/billing.md</c> loads, and has the prices its text says, so a reader who
        /// copies it gets a working file.
        /// </summary>
        [Test]
        public void ThePriceListExampleInTheGuide_Loads()
        {
            DirectoryInfo? d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "docs", "billing.md")))
            {
                d = d.Parent;
            }

            Assert.That(d, Is.Not.Null, "docs/billing.md was not found above the test folder");
            string guide = File.ReadAllText(Path.Combine(d!.FullName, "docs", "billing.md"));
            int heading = guide.IndexOf("### The price list", StringComparison.Ordinal);
            int start = guide.IndexOf("```ini\n", heading, StringComparison.Ordinal) + 7;
            int end = guide.IndexOf("```", start, StringComparison.Ordinal);
            Assert.That(heading, Is.GreaterThan(0));
            string[] lines = guide.Substring(start, end - start).Split('\n');

            RetroIO.Protocols.X25.Switch.Billing.Tariff t =
                RetroIO.Protocols.X25.Switch.Billing.Tariff.Load(RetroIO.Protocols.X25.Common.IniConfig.Parse(lines));
            Assert.That(t.MonthlyFee, Is.EqualTo(25000));
            Assert.That(t.HomeDnic, Is.EqualTo("2342"));
            Assert.That(t.Home.PerTimeUnit, Is.EqualTo(30));
            Assert.That(t.Other.PerKilosegment, Is.EqualTo(1000));
            Assert.That(t.OffPeakDiscountPercent, Is.EqualTo(50));
        }

        [TestCase("2026-11", true)]
        [TestCase("2026-13", false)]
        [TestCase("26-11", false)]
        public void TheMonth_IsReadAsYearDashMonth(string text, bool ok)
        {
            Assert.That(InvoiceWindowViewModel.TryParseMonth(text, out _, out _), Is.EqualTo(ok));
        }
    }
}
