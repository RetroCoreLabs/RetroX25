//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;
using RetroX25.Host;

namespace RetroX25.Tests
{
    /// <summary>
    /// x25host: the machine list, and machines answering real calls through a switch. Two scripted machines share
    /// one switch line and each gets only the calls to its own number.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class X25HostTests
    {
        private const string Alpha = "23410001";
        private const string Beta = "23410002";

        // Answers with its name, echoes a line back with "A:" in front, and on "bye" says goodbye and clears.
        private const string AlphaScript =
            "function onConnect(s) { s.send('ALPHA ' + machine.settings.hostname + ' ' + machine.settings.greeting + '\\n'); }\n" +
            "function onInput(s, t) {\n" +
            "  if (t.indexOf('bye') >= 0) { s.send('goodbye\\n'); s.clear(); return; }\n" +
            "  s.send('A:' + t);\n" +
            "}\n";

        // Counts its callers: the count is shared by every call to this machine.
        private const string BetaScript =
            "var callers = 0;\n" +
            "function onConnect(s) { callers++; s.send('BETA caller ' + callers + '\\n'); }\n";

        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private string _folder = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "x25host-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "alpha.js"), AlphaScript);
            File.WriteAllText(Path.Combine(_folder, "beta.js"), BetaScript);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                _disposables[i].Dispose();
            }

            _disposables.Clear();
            Directory.Delete(_folder, recursive: true);
        }

        // ------------------------------------------------------------------ the machine list

        [Test]
        public void List_IsRead_WithSettingsAndRelativeScripts()
        {
            List<MachineEntry> m = Parse(
                "# two machines on one line",
                "[machine.1]",
                "script = beta.js",
                "nua = " + Beta,
                "switch = 127.0.0.1:2000",
                "[machine.0]",
                "type = script",
                "script = alpha.js",
                "hostname = ALPHA",
                "nua = " + Alpha,
                "switch = 127.0.0.1:2000",
                "group = norway",
                "setting.greeting = \"hello there\"  # quoted keeps the space");

            Assert.That(m.Count, Is.EqualTo(2));
            Assert.That(m[0].Hostname, Is.EqualTo("ALPHA"), "machines come in the order of their numbers");
            Assert.That(m[0].Group, Is.EqualTo("norway"));
            Assert.That(m[0].Settings["greeting"], Is.EqualTo("hello there"));
            Assert.That(m[0].ScriptPath, Is.EqualTo(Path.Combine(_folder, "alpha.js")));
            Assert.That(m[1].Hostname, Is.EqualTo("machine.1"), "no hostname: the section name");
            Assert.That(m[1].Group, Is.EqualTo("default"));
            Assert.That(m[1].Line, Is.EqualTo("127.0.0.1:2000"));
        }

        [TestCase("colour = red", "unknown key colour")]
        [TestCase("script = missing.js", "is not there")]
        [TestCase("nua = 23A1", "1 to 15 digits")]
        [TestCase("nua = 1234567890123456", "1 to 15 digits")]
        [TestCase("switch = 2000", "host:port")]
        public void List_AWrongKey_StopsWithTheLine(string line, string expected)
        {
            FormatException? ex = Assert.Throws<FormatException>(() => Parse(
                "[machine.0]", "script = alpha.js", "nua = " + Alpha, "switch = 127.0.0.1:2000", line));
            Assert.That(ex!.Message, Does.Contain("list.ini:5").And.Contain(expected));
        }

        [Test]
        public void List_TwoMachinesWithOneNua_AreRefused()
        {
            FormatException? ex = Assert.Throws<FormatException>(() => Parse(
                "[machine.0]", "script = alpha.js", "nua = " + Alpha, "switch = 127.0.0.1:2000",
                "[machine.1]", "script = beta.js", "nua = " + Alpha, "switch = 127.0.0.1:2000"));
            Assert.That(ex!.Message, Does.Contain("is also [machine.0]"));
        }

        [Test]
        public void List_AMachineWithoutATypeOrAScript_IsRefused()
        {
            FormatException? ex = Assert.Throws<FormatException>(() => Parse(
                "[machine.0]", "nua = " + Alpha, "switch = 127.0.0.1:2000"));
            Assert.That(ex!.Message, Does.Contain("[machine.0] has no type"));
            ex = Assert.Throws<FormatException>(() => Parse(
                "[machine.0]", "type = script", "nua = " + Alpha, "switch = 127.0.0.1:2000"));
            Assert.That(ex!.Message, Does.Contain("[machine.0] has no script"));
        }

        [Test]
        public void List_AFolderTypeWithoutALibrary_IsRefused()
        {
            FormatException? ex = Assert.Throws<FormatException>(() => Parse(
                "[machine.0]", "type = vms", "nua = " + Alpha, "switch = 127.0.0.1:2000"));
            Assert.That(ex!.Message, Does.Contain("there is no [defaults] library"));
        }

        [Test]
        public void AScriptThatDoesNotLoad_StopsTheHost_NamingTheMachine()
        {
            File.WriteAllText(Path.Combine(_folder, "broken.js"), "function onConnect(s) { ");
            List<MachineEntry> m = Parse("[machine.0]", "script = broken.js", "hostname = BROKEN", "nua = " + Alpha, "switch = 127.0.0.1:2000");
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => new HostRunner(m, NullLogger.Instance));
            Assert.That(ex!.Message, Does.Contain("BROKEN").And.Contain("does not load"));
        }

        // ------------------------------------------------------------------ real calls through a switch

        [Test]
        public void TwoMachinesOnOneLine_EachAnswersItsOwnNumber_AndKeepsItsOwnState()
        {
            (XotSwitchPort line, XotSwitchPort callers) = BuildSwitch();
            HostRunner runner = StartHost(line.BoundPort);

            Caller a = Call(callers, Alpha);
            Assert.That(a.WaitFor("ALPHA ALPHA hi\r\n"), Is.True, "alpha: " + a.Text);

            Caller b1 = Call(callers, Beta);
            Caller b2 = Call(callers, Beta);
            Assert.That(b1.WaitFor("BETA caller 1\r\n"), Is.True, "beta 1: " + b1.Text);
            Assert.That(b2.WaitFor("BETA caller 2\r\n"), Is.True, "beta 2: " + b2.Text);
            Assert.That(a.Text, Does.Not.Contain("BETA"), "alpha got beta's text");

            a.Send("ping\r");
            Assert.That(a.WaitFor("A:ping\r"), Is.True, "alpha: " + a.Text);
            Assert.That(SpinWait.SpinUntil(() => runner.CallCount == 3, 5000), Is.True);
        }

        [Test]
        public void TheMachineClears_AfterItsLastText()
        {
            (XotSwitchPort line, XotSwitchPort callers) = BuildSwitch();
            HostRunner runner = StartHost(line.BoundPort);

            Caller a = Call(callers, Alpha);
            Assert.That(a.WaitFor("ALPHA"), Is.True);
            a.Send("bye\r");
            Assert.That(a.WaitFor("goodbye\r\n"), Is.True, "the goodbye must arrive before the clear: " + a.Text);
            Assert.That(a.Cleared.Wait(5000), Is.True, "the machine did not clear the call");
            Assert.That(a.Cause, Is.EqualTo(0), "cleared by the machine: DTE originated");
            Assert.That(SpinWait.SpinUntil(() => runner.CallCount == 0, 5000), Is.True);
        }

        /// <summary>
        /// The host is a DTE, so its clear carries cause 0 "DTE originated" (X.25 Table 5-7; cause 13 is the network's
        /// to give) and the diagnostic 67 "invalid called address" says why.
        /// </summary>
        [Test]
        public void ACallToANumberNoMachineHas_IsCleared_InvalidCalledAddress()
        {
            (XotSwitchPort line, XotSwitchPort callers) = BuildSwitch();
            StartHost(line.BoundPort);

            X25Client caller = OpenCaller(callers, out Caller watch);
            X25Vc? vc = caller.CallWait("23410099", 5000);
            Assert.That(vc, Is.Null, "a number no machine has was answered");
            Assert.That(watch.Cleared.Wait(5000), Is.True);
            Assert.That(watch.Cause, Is.EqualTo(0), "cause: DTE originated");
            Assert.That(watch.Diagnostic, Is.EqualTo(67), "diagnostic: invalid called address");
        }

        // ------------------------------------------------------------------ host folder machines

        private const string Folder = "23410003";

        /// <summary>A host folder in library/tiny: a login, one command, logout. Two sessions at most.</summary>
        private void WriteTinyHost(int sessions = 2)
        {
            string dir = Path.Combine(_folder, "library", "tiny");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "host.ini"), string.Join("\n",
                "[host]", "address = 1", "sessions = " + sessions, "banner = banner.txt",
                "[login]", "case = upper",
                "[user.0]", "name = GUEST", "password = SECRET",
                "[shell]", "case = upper", "logout_text = Bye {user}.",
                "[command.0]", "name = HELLO", "text = Hello from {node}, you are {user} at {calling}",
                "[command.1]", "name = LOGOUT", "do = logout"));
            File.WriteAllText(Path.Combine(dir, "banner.txt"), "{node} at {address}\n");
        }

        /// <summary>
        /// The single-machine form (<c>x25host &lt;folder&gt; -x host:port</c>): without <c>-a</c> the machine answers
        /// host.ini's address (here 1), and <c>-n</c> names it.
        /// </summary>
        [Test]
        public void Single_AFolderWithoutDashA_AnswersHostIniAddress_AndDashNNamesIt()
        {
            WriteTinyHost();
            (XotSwitchPort line, XotSwitchPort callers) = BuildSwitch("1");
            MachineEntry e = MachineList.Single(Path.Combine(_folder, "library", "tiny"), "127.0.0.1:" + line.BoundPort, null, "ONEOFF");
            HostRunner runner = new HostRunner(new List<MachineEntry> { e }, NullLogger.Instance);
            _disposables.Add(runner);
            runner.Start();
            Assert.That(SpinWait.SpinUntil(() => runner.AllLinesUp, 10000), Is.True);
            Assert.That(e.Nua, Is.EqualTo("1"));

            X25Client client = OpenCaller(callers, out Caller c);
            c.Vc = client.CallWait("1", 5000);
            Assert.That(c.Vc, Is.Not.Null);
            Assert.That(c.WaitFor("ONEOFF at 1"), Is.True, c.Text);
        }

        [Test]
        public void Single_BadValues_AreRefusedWithTheOption()
        {
            WriteTinyHost();
            string dir = Path.Combine(_folder, "library", "tiny");
            Assert.That(Assert.Throws<FormatException>(() => MachineList.Single(dir, "nohost", null, null))!.Message, Does.StartWith("-x"));
            Assert.That(Assert.Throws<FormatException>(() => MachineList.Single(dir, "127.0.0.1:2000", "12x", null))!.Message, Does.StartWith("-a"));
            Assert.That(Assert.Throws<FormatException>(() => MachineList.Single(_folder, "127.0.0.1:2000", null, null))!.Message, Does.Contain("no host.ini"));
        }

        [Test]
        public void List_AFolderMachine_IsFoundInTheLibrary()
        {
            WriteTinyHost();
            List<MachineEntry> m = Parse("[defaults]", "library = library",
                "[machine.0]", "type = tiny", "nua = " + Folder, "switch = 127.0.0.1:2000");
            Assert.That(m[0].HostFolder, Is.EqualTo(Path.Combine(_folder, "library", "tiny")));
            Assert.That(m[0].Hostname, Is.Empty, "the folder gives the name when the list does not");
        }

        [TestCase("type = nothere", "no host folder")]
        [TestCase("type = ../tiny", "must be the name of a folder")]
        [TestCase("script = alpha.js", "for type = script only")]
        public void List_AWrongFolderMachine_IsRefused(string line, string expected)
        {
            WriteTinyHost();
            FormatException? ex = Assert.Throws<FormatException>(() => Parse("[defaults]", "library = library",
                "[machine.0]", "type = tiny", "nua = " + Folder, "switch = 127.0.0.1:2000", line));
            Assert.That(ex!.Message, Does.Contain(expected));
        }

        [Test]
        public void AFolderThatDoesNotLoad_StopsTheHost_NamingTheMachineAndTheLine()
        {
            WriteTinyHost();
            File.AppendAllText(Path.Combine(_folder, "library", "tiny", "host.ini"), "\n[colours]\n");
            List<MachineEntry> m = Parse("[defaults]", "library = library",
                "[machine.0]", "type = tiny", "nua = " + Folder, "switch = 127.0.0.1:2000");
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => new HostRunner(m, NullLogger.Instance));
            Assert.That(ex!.Message, Does.Contain("[machine.0]").And.Contain("unknown section [colours]").And.Contain("host.ini:"));
        }

        /// <summary>
        /// A folder machine through a real switch: the banner arrives at once (before any key), the list's hostname and
        /// nua fill {node} and {address}, the password is hidden with an X.29 Set of parameter 2 in a Q-bit packet
        /// (02 02 00, then 02 02 01 after it), and logout says goodbye and clears with cause 0.
        /// </summary>
        [Test]
        public void AFolderMachine_AnswersACall_LogsIn_AndLogsOut()
        {
            WriteTinyHost();
            (XotSwitchPort line, XotSwitchPort callers) = BuildSwitch();
            List<MachineEntry> m = Parse("[defaults]", "library = library",
                "[machine.0]", "type = tiny", "hostname = TINY1", "nua = " + Folder, "switch = 127.0.0.1:" + line.BoundPort);
            HostRunner runner = new HostRunner(m, NullLogger.Instance);
            _disposables.Add(runner);
            runner.Start();
            Assert.That(SpinWait.SpinUntil(() => runner.AllLinesUp, 10000), Is.True);

            Caller c = Call(callers, Folder);
            Assert.That(c.WaitFor("TINY1 at " + Folder + "\r\nUsername: "), Is.True, "banner and prompt: " + c.Text);
            c.Send("guest\r");
            Assert.That(c.WaitFor("Password: "), Is.True, c.Text);
            Assert.That(SpinWait.SpinUntil(() => c.QText.Contains("02-02-00", StringComparison.Ordinal), 5000), Is.True, "echo off: " + c.QText);
            c.Send("SECRET\r");
            Assert.That(c.WaitFor("\r\n$ "), Is.True, c.Text);
            Assert.That(SpinWait.SpinUntil(() => c.QText.Contains("02-02-01", StringComparison.Ordinal), 5000), Is.True, "echo on: " + c.QText);
            c.Send("hello\r");
            Assert.That(c.WaitFor("Hello from TINY1, you are guest at 23400001\r\n"), Is.True, c.Text);
            c.Send("logout\r");
            Assert.That(c.WaitFor("Bye guest.\r\n"), Is.True, c.Text);
            Assert.That(c.Cleared.Wait(5000), Is.True);
            Assert.That(c.Cause, Is.EqualTo(0));
        }

        /// <summary>
        /// A PAD call (call user data 01 00 00 00) is ended with an X.29 invitation to clear after the last text (X.29
        /// 3.2.1), and the host waits for the PAD to clear: its own clear could overtake that text in the network. The
        /// test calls without the PAD identifier above, and those calls are cleared at once.
        /// </summary>
        [Test]
        public void APadCall_IsEndedWithAnInvitationToClear_AndThePadClears()
        {
            WriteTinyHost();
            (XotSwitchPort line, XotSwitchPort callers) = BuildSwitch();
            List<MachineEntry> m = Parse("[defaults]", "library = library",
                "[machine.0]", "type = tiny", "nua = " + Folder, "switch = 127.0.0.1:" + line.BoundPort);
            HostRunner runner = new HostRunner(m, NullLogger.Instance);
            _disposables.Add(runner);
            runner.Start();
            Assert.That(SpinWait.SpinUntil(() => runner.AllLinesUp, 10000), Is.True);

            X25Client client = OpenCaller(callers, out Caller c);
            c.Vc = client.CallWait(Folder, 5000, new byte[] { 1, 0, 0, 0 });
            Assert.That(c.Vc, Is.Not.Null);
            Assert.That(c.WaitFor("Username: "), Is.True, c.Text);
            c.Send("guest\rSECRET\r");
            Assert.That(c.WaitFor("$ "), Is.True, c.Text);
            c.Send("logout\r");
            Assert.That(c.WaitFor("Bye guest.\r\n"), Is.True, c.Text);
            Assert.That(SpinWait.SpinUntil(() => c.QText.EndsWith("01 ", StringComparison.Ordinal), 5000), Is.True, "no invitation to clear: " + c.QText);
            Assert.That(c.Cleared.Wait(1000), Is.False, "the host cleared instead of waiting for the PAD");
            client.Clear(c.Vc!, 0, 0);
            Assert.That(SpinWait.SpinUntil(() => runner.CallCount == 0, 5000), Is.True, "the host did not see the PAD's clear");

            // The counters of --stats-log: one call, the 20 typed bytes ("guest\rSECRET\r" and "logout\r"), nothing refused.
            List<string> stats = runner.StatsLines();
            Assert.That(stats, Has.Count.EqualTo(2));
            Assert.That(stats[0], Does.StartWith("tiny: calls in 1, refused busy 0, refused not a PAD call 0, bytes in 20,"), stats[0]);
            Assert.That(stats[0], Does.EndWith(", wrong logins 0"), stats[0]);
            Assert.That(stats[1], Does.StartWith("total: calls in 1,"), stats[1]);
        }

        [Test]
        public void AFullFolderMachine_RefusesTheNextCall_WithCauseDteOriginated()
        {
            WriteTinyHost(sessions: 1);
            (XotSwitchPort line, XotSwitchPort callers) = BuildSwitch();
            List<MachineEntry> m = Parse("[defaults]", "library = library",
                "[machine.0]", "type = tiny", "nua = " + Folder, "switch = 127.0.0.1:" + line.BoundPort);
            HostRunner runner = new HostRunner(m, NullLogger.Instance);
            _disposables.Add(runner);
            runner.Start();
            Assert.That(SpinWait.SpinUntil(() => runner.AllLinesUp, 10000), Is.True);

            Caller first = Call(callers, Folder);
            Assert.That(first.WaitFor("Username: "), Is.True);
            X25Client second = OpenCaller(callers, out Caller watch);
            Assert.That(second.CallWait(Folder, 5000), Is.Null, "the second call was taken with sessions = 1");
            Assert.That(watch.Cleared.Wait(5000), Is.True);
            Assert.That(watch.Cause, Is.EqualTo(0), "a DTE clears with cause 0 (X.25 Table 5-7)");
        }

        // ------------------------------------------------------------------ helpers

        private List<MachineEntry> Parse(params string[] lines)
        {
            return MachineList.Parse(lines, "list.ini", _folder);
        }

        private HostRunner StartHost(ushort linePort)
        {
            List<MachineEntry> m = Parse(
                "[machine.0]", "script = alpha.js", "hostname = ALPHA", "nua = " + Alpha, "switch = 127.0.0.1:" + linePort, "setting.greeting = hi",
                "[machine.1]", "script = beta.js", "hostname = BETA", "nua = " + Beta, "switch = 127.0.0.1:" + linePort);
            HostRunner runner = new HostRunner(m, NullLogger.Instance);
            _disposables.Add(runner);
            runner.Start();
            Assert.That(SpinWait.SpinUntil(() => runner.AllLinesUp, 10000), Is.True, "x25host never reached the switch");
            return runner;
        }

        private X25Client OpenCaller(XotSwitchPort callers, out Caller caller)
        {
            X25Client client = new X25Client { ServiceName = "caller" };
            _disposables.Add(client);
            Caller c = new Caller(client);
            client.SetOnData((cl, vc, data, length, qBit, mBit, dBit) =>
            {
                if (!qBit)
                {
                    c.Add(data, length);
                }
                else
                {
                    c.AddQ(data, length);
                }
            });
            client.SetOnClear((cl, vc, cause, diag) =>
            {
                c.Cause = cause;
                c.Diagnostic = diag;
                c.Cleared.Set();
            });
            client.Open(new X25ClientConfig { Host = "127.0.0.1", Port = callers.BoundPort, LocalAddress = "23400001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            caller = c;
            return client;
        }

        private Caller Call(XotSwitchPort callers, string address)
        {
            X25Client client = OpenCaller(callers, out Caller c);
            c.Vc = client.CallWait(address, 5000);
            Assert.That(c.Vc, Is.Not.Null, "the call to " + address + " was not answered");
            return c;
        }

        /// <summary>
        /// One switch: port 0 is the host line (every 2341... number goes there), port 1 is where callers connect.
        /// The host line keeps the host's own connection for arriving calls, as the demo's host lines do.
        /// </summary>
        private (XotSwitchPort Line, XotSwitchPort Callers) BuildSwitch(string prefix = "2341")
        {
            SwitchConfig cfg = new SwitchConfig { Dnic = "2340" };
            cfg.Routing.AddRoute(new X121Route { Type = X121RouteType.Prefix, Address = prefix, PrefixLen = prefix.Length, DestPort = 0 });
            SwitchNode node = new SwitchNode(cfg);
            XotSwitchPort line = new XotSwitchPort(new PortConfig { Id = 0, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1, LciCount = 64 }, NullLogger.Instance);
            XotSwitchPort callers = new XotSwitchPort(new PortConfig { Id = 1, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1, LciCount = 64 }, NullLogger.Instance);
            Assert.That(line.Start(), Is.True);
            Assert.That(callers.Start(), Is.True);
            _disposables.Add(line);
            _disposables.Add(callers);
            node.AddPort(line);
            node.AddPort(callers);
            new SwitchCore(node);
            return (line, callers);
        }

        /// <summary>One caller: what arrived, and how its call ended.</summary>
        private sealed class Caller
        {
            private readonly X25Client _client;
            private readonly StringBuilder _text = new StringBuilder();
            private readonly StringBuilder _q = new StringBuilder();

            public Caller(X25Client client)
            {
                _client = client;
            }

            public X25Vc? Vc { get; set; }

            public ManualResetEventSlim Cleared { get; } = new ManualResetEventSlim(false);

            public int Cause { get; set; } = -1;

            public int Diagnostic { get; set; } = -1;

            public string Text
            {
                get
                {
                    lock (_text)
                    {
                        return _text.ToString();
                    }
                }
            }

            /// <summary>The Q-bit packets (X.29 messages) that arrived, each as hex bytes, separated by spaces.</summary>
            public string QText
            {
                get
                {
                    lock (_q)
                    {
                        return _q.ToString();
                    }
                }
            }

            public void AddQ(byte[] data, int length)
            {
                lock (_q)
                {
                    _q.Append(BitConverter.ToString(data, 0, length)).Append(' ');
                }
            }

            public void Add(byte[] data, int length)
            {
                lock (_text)
                {
                    _text.Append(Encoding.ASCII.GetString(data, 0, length));
                }
            }

            public void Send(string text)
            {
                Assert.That(_client.SendText(Vc!, text), Is.True);
            }

            public bool WaitFor(string piece)
            {
                return SpinWait.SpinUntil(() => Text.Contains(piece, StringComparison.Ordinal), 5000);
            }
        }
    }
}
