//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroX25.Host;

namespace RetroX25.Tests
{
    /// <summary>The x25host operator console (docs/x25host-format.md, "The operator console"), on a runner not started.</summary>
    [TestFixture]
    public sealed class OperatorConsoleTests
    {
        private string _folder = string.Empty;
        private HostRunner? _runner;

        [SetUp]
        public void MakeOneMachine()
        {
            _folder = Path.Combine(Path.GetTempPath(), "x25console-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "host.ini"), "[host]\naddress = 1234\nhostname = ONE\n");
            MachineEntry e = MachineList.Single(_folder, "127.0.0.1:1", null, null);
            _runner = new HostRunner(new List<MachineEntry> { e }, NullLogger.Instance);
        }

        [TearDown]
        public void Remove()
        {
            _runner?.Dispose();
            Directory.Delete(_folder, recursive: true);
        }

        [Test]
        public void ShowStats_GivesTheMachineAndTheTotal()
        {
            string answer = new OperatorConsole(_runner!).Execute("  SHOW   stats ");
            Assert.That(answer, Does.StartWith("ONE: calls in 0,"));
            Assert.That(answer, Does.Contain("\ntotal: calls in 0,"));
        }

        [Test]
        public void ShowHosts_NeedsTheWorker_AndSaysSoWhenItIsNotRunning()
        {
            // Not started: nobody takes the question, and the console says that instead of hanging.
            Assert.That(new OperatorConsole(_runner!).Execute("show hosts"), Is.EqualTo("the machines did not answer within 2 s\n"));
        }

        [Test]
        public void ShowHosts_OnARunningRunner_ListsTheMachine()
        {
            _runner!.Start();
            string answer = new OperatorConsole(_runner).Execute("show hosts");
            Assert.That(answer, Does.StartWith("ONE "));
            Assert.That(answer, Does.Contain("1234"));
            Assert.That(answer, Does.Contain("redialling"), "no switch on port 1");
            Assert.That(answer, Does.EndWith("calls 0\n"));
        }

        [Test]
        public void Quit_AsksFirst_AndOnlyYesStops()
        {
            OperatorConsole c = new OperatorConsole(_runner!);
            Assert.That(c.Execute("quit"), Does.Contain("(yes/no)"));
            Assert.That(c.Execute("no"), Is.EqualTo("not stopped\n"));
            Assert.That(c.QuitAsked, Is.False);
            c.Execute("quit");
            Assert.That(c.Execute("yes"), Is.EqualTo("stopping\n"));
            Assert.That(c.QuitAsked, Is.True);
        }

        [Test]
        public void ACommandNotBuilt_SaysSoByName_AndAnUnknownOneIsNamed()
        {
            OperatorConsole c = new OperatorConsole(_runner!);
            Assert.That(c.Execute("stop host ONE"), Is.EqualTo("'stop host' is not built yet (see src/PLAN.md, G1)\n"));
            Assert.That(c.Execute("frob"), Does.StartWith("unknown command: frob"));
            Assert.That(c.Execute(""), Is.Empty);
        }
    }
}
