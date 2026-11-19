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
        public void ShowHost_GivesTheMachineInFull()
        {
            _runner!.Start();
            string[] lines = new OperatorConsole(_runner).Execute("show host one").Split('\n');
            Assert.That(lines[0], Does.StartWith("ONE "));
            Assert.That(lines[1], Does.StartWith("switch 127.0.0.1:1, host folder "));
            Assert.That(lines[2], Does.Match(@"^started \d{4}-\d\d-\d\d \d\d:\d\d:\d\d, up 0h 00m \d\ds$"));
            Assert.That(lines[3], Does.StartWith("calls in 0, "));
            Assert.That(new OperatorConsole(_runner).Execute("show host TWO"), Is.EqualTo("no machine called TWO\n"));
        }

        [Test]
        public void Log_SetsTheLevelOfOneMachine_OrAll_AndRefusesANumberOrAWrongName()
        {
            OperatorConsole c = new OperatorConsole(_runner!);
            Assert.That(c.Execute("log one warning"), Is.EqualTo("log level warning for 1 machine(s)\n"));
            Assert.That(c.Execute("log * info"), Is.EqualTo("log level information for 1 machine(s)\n"));
            Assert.That(c.Execute("log two error"), Is.EqualTo("no machine called two\n"));
            Assert.That(c.Execute("log one 3"), Does.StartWith("usage: log"));
            Assert.That(c.Execute("log one loud"), Does.StartWith("usage: log"));
        }

        [Test]
        public void AMachineLog_LetsThroughOnlyItsLevelAndAbove()
        {
            List<string> seen = new List<string>();
            MachineLog log = new MachineLog(new ListLogger(seen)) { Minimum = Microsoft.Extensions.Logging.LogLevel.Warning };
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(log, "quiet");
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(log, "loud");
            Assert.That(seen, Is.EqualTo(new List<string> { "loud" }));
        }

        private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly List<string> _seen;

            public ListLogger(List<string> seen) => _seen = seen;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) => _seen.Add(formatter(state, exception));
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
            Assert.That(c.Execute("reload"), Does.StartWith("reload needs a machine list"));
            Assert.That(c.Execute("help"), Does.Contain("log <hostname|*> <level>"));
            Assert.That(c.Execute("frob"), Does.StartWith("unknown command: frob"));
            Assert.That(c.Execute(""), Is.Empty);
        }
    }
}
