//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroX25.Host;

namespace RetroX25.Tests
{
    /// <summary>The x25host console over telnet (<c>--console-port</c>) and the <c>[operator.N]</c> logins.</summary>
    [TestFixture]
    public sealed class TelnetConsoleTests
    {
        private string _folder = string.Empty;
        private HostRunner? _runner;
        private TelnetConsole? _console;

        [SetUp]
        public void MakeOneMachine()
        {
            _folder = Path.Combine(Path.GetTempPath(), "x25telnet-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "host.ini"), "[host]\naddress = 1234\nhostname = ONE\n");
            _runner = new HostRunner(new List<MachineEntry> { MachineList.Single(_folder, "127.0.0.1:1", null, null) }, NullLogger.Instance);
            _runner.Start();
            List<MachineList.OperatorLogin> ops = new List<MachineList.OperatorLogin>
            {
                new MachineList.OperatorLogin { Name = "oper", Password = "Secret" },
            };
            _console = new TelnetConsole(_runner, ops, IPAddress.Loopback, 0, NullLogger.Instance);
            _console.Start();
        }

        [TearDown]
        public void Remove()
        {
            _console?.Dispose();
            _runner?.Dispose();
            Directory.Delete(_folder, recursive: true);
        }

        [Test]
        public void AnOperator_LogsIn_RunsACommand_AndQuitLeavesOnlyTheConnection()
        {
            using TcpClient c = new TcpClient();
            c.Connect(IPAddress.Loopback, _console!.Port);
            NetworkStream s = c.GetStream();
            s.ReadTimeout = 10000;
            StringBuilder seen = new StringBuilder();
            Until(s, seen, "login: ");

            // A wrong password (case matters), then the right one; the name in any case. Telnet commands from the
            // client (IAC DO ECHO) and CR NUL are read past.
            Send(s, "OPER\r\n");
            Until(s, seen, "password: ");
            Send(s, "secret\r\n");
            Until(s, seen, "login incorrect\r\nlogin: ");
            s.Write(new byte[] { 255, 253, 1 });
            Send(s, "oper\r\0");
            Until(s, seen, "password: ");
            Send(s, "Secret\r\n");
            Until(s, seen, "x25host> ");

            Send(s, "show stats\r\n");
            Until(s, seen, "ONE: calls in 0,");
            Until(s, seen, "\r\nx25host> ");
            Send(s, "quit\r\n");
            Until(s, seen, "bye\r\n");
            Assert.That(s.Read(new byte[16], 0, 16), Is.EqualTo(0), "quit closes the connection");
            Assert.That(new OperatorConsole(_runner!).Execute("show hosts"), Does.StartWith("ONE "), "the machines still run");
        }

        [Test]
        public void ThreeWrongLogins_CloseTheConnection()
        {
            using TcpClient c = new TcpClient();
            c.Connect(IPAddress.Loopback, _console!.Port);
            NetworkStream s = c.GetStream();
            s.ReadTimeout = 10000;
            StringBuilder seen = new StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                Until(s, seen, "login: ");
                Send(s, "x\r\n");
                Until(s, seen, "password: ");
                Send(s, "y\r\n");
                Until(s, seen, "login incorrect\r\n");
                seen.Clear();
            }

            Assert.That(s.Read(new byte[16], 0, 16), Is.EqualTo(0));
        }

        [Test]
        public void TheList_ReadsOperators_AndRefusesOneWithoutAPassword()
        {
            List<MachineList.OperatorLogin> ops = new List<MachineList.OperatorLogin>();
            MachineList.Parse(new[] { "[operator.0]", "name = oper", "password = pw" }, "m.ini", _folder, ops);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].Name, Is.EqualTo("oper"));
            FormatException ex = Assert.Throws<FormatException>(() =>
                MachineList.Parse(new[] { "[operator.0]", "name = oper" }, "m.ini", _folder, new List<MachineList.OperatorLogin>()))!;
            Assert.That(ex.Message, Is.EqualTo("m.ini:1: [operator.0] needs both name and password"));
            ex = Assert.Throws<FormatException>(() =>
                MachineList.Parse(new[] { "[operator.0]", "level = 3" }, "m.ini", _folder, new List<MachineList.OperatorLogin>()))!;
            Assert.That(ex.Message, Does.Contain("unknown key (only name and password)"));
        }

        private static void Send(NetworkStream s, string text)
        {
            s.Write(Encoding.ASCII.GetBytes(text));
        }

        // Reads until the text has arrived; telnet commands from the server (IAC WILL/WONT ECHO) are left out.
        private static void Until(NetworkStream s, StringBuilder seen, string wanted)
        {
            while (seen.ToString().IndexOf(wanted, StringComparison.Ordinal) < 0)
            {
                int b = s.ReadByte();
                Assert.That(b, Is.Not.EqualTo(-1), "closed; seen: " + seen);
                if (b == 255)
                {
                    s.ReadByte();
                    s.ReadByte();
                    continue;
                }

                seen.Append((char)b);
            }
        }
    }
}
