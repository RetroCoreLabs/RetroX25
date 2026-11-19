//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Text;
using NUnit.Framework;
using RetroIO.Protocols.X25.Host;

namespace RetroX25.Tests
{
    /// <summary>
    /// The host folders of the library (<c>src/demo/hosts</c>), each loaded as it is on disk and driven without a
    /// network: log in, run the commands, log out (PLAN G5: every host in the library gets such a test). The
    /// folders that are also in the x75 demo are called through the network in <see cref="X75CountriesScriptTests"/>.
    /// </summary>
    [TestFixture]
    public sealed class LibraryHostTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 19, 9, 48, 40);

        [Test]
        public void Tops20_LogsInWithTheLoginCommand_RunsItsCommands_AndKillsTheJob()
        {
            (DescribedHost h, Output o) = Load("tops20");
            h.Connect(1, string.Empty, "24227001");
            Assert.That(o.Take(1), Is.EqualTo("AURORA, Research and Development, TOPS-20 Monitor 7(7)\r\n@"));

            Type(h, "DAYTIME\r");
            Assert.That(o.Take(1), Is.EqualTo("?Unrecognized command\r\n@"), "not logged in yet");
            Type(h, "LOGIN GUEST WRONG 341\r");
            Assert.That(o.Take(1), Is.EqualTo("?Incorrect user name or password\r\n@"));
            Type(h, "login guest GUEST 341\r");
            Assert.That(o.Take(1), Is.EqualTo(" Job 57 on TTY127 19-NOV-2026 09:48:40\r\n@"));

            Type(h, "DIR\r");
            Assert.That(o.Take(1), Does.Contain("LOGIN.CMD"));
            Type(h, "TYPE LOGIN.CMD\r");
            Assert.That(o.Take(1), Does.Contain("TERMINAL VT100"));
            Type(h, "FROB\r");
            Assert.That(o.Take(1), Is.EqualTo("?Unrecognized command\r\n@"));
            Type(h, "LOGOUT\r");

            // {user} is the name as typed (docs/x25host-format.md, Logins), so "guest" here.
            Assert.That(o.Take(1), Does.StartWith("Killed Job 57, User guest, Account 341, TTY 127,\r\n at 19-NOV-2026 09:48:40"));
            Assert.That(o.Cleared, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void Rsts_LogsInWithAProjectProgrammerNumber_RunsItsCommands_AndSaysBye()
        {
            (DescribedHost h, Output o) = Load("rsts");
            h.Connect(1, string.Empty, "24227001");
            Assert.That(o.Take(1), Is.EqualTo("RSTS V10.1-L 19-NOV-2026 09:48:40\r\nUser: "));
            Type(h, "200,200\rnope\r");
            Assert.That(o.Take(1), Is.EqualTo("Password: \r\nInvalid entry - try again\r\nUser: "));
            Type(h, "200,200\rGUEST\r");
            Assert.That(o.Take(1), Is.EqualTo("Password: \r\nLast interactive login on 19-NOV-2026, 09:48:40 at KB0:\r\n"
                + "Last non-interactive login on 19-NOV-2026, 09:48:40\r\n$ "));
            Type(h, "dir\r");
            Assert.That(o.Take(1), Does.Contain("NOTICE.TXT"));
            Type(h, "TYPE NOTICE.TXT\r");
            Assert.That(o.Take(1), Does.Contain("Type BYE to log off."));
            Type(h, "mc ncp\r");
            Assert.That(o.Take(1), Is.EqualTo("^\r\n?Invalid command\r\n$ "));
            Type(h, "BYE\r");
            Assert.That(o.Take(1), Does.Contain("Job 7 User 200,200 logged off KB41 at 19-NOV-2026 09:48:40"));
            Assert.That(o.Cleared, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void SunOs_LogsIn_RunsItsCommands_AndLogsOut()
        {
            (DescribedHost h, Output o) = Load("sunos");
            h.Connect(1, string.Empty, "24227001");
            Assert.That(o.Take(1), Is.EqualTo("\r\n\r\nSunOS UNIX (csdc_1)\r\n\r\nlogin: "));
            Type(h, "guest\rguest\r");
            Assert.That(o.Take(1), Is.EqualTo("Password:\r\nSunOS Release 4.1.1 (GENERIC) #1: Thu Oct 11 12:00:00 PDT 1990\r\ncsdc_1% "));
            Type(h, "cd /home/guest\r"); // the format has no home folder: the shell starts in /, as on 4.3BSD here
            o.Take(1);
            Type(h, "cat README\r");
            Assert.That(o.Take(1), Does.Contain("demonstration SunOS host"));
            Type(h, "frob\r");
            Assert.That(o.Take(1), Is.EqualTo("frob: Command not found.\r\ncsdc_1% "));
            Type(h, "exit\r");
            Assert.That(o.Take(1), Does.StartWith("logout"));
            Assert.That(o.Cleared, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void SystemV_LogsIn_RunsItsCommands_AndExits()
        {
            (DescribedHost h, Output o) = Load("sysv");
            h.Connect(1, string.Empty, "24227001");
            Assert.That(o.Take(1), Is.EqualTo("\r\nUNIX(r) System V Release 4.0 (joe)\r\n\r\nlogin: "));
            Type(h, "guest\rwrong\r");
            Assert.That(o.Take(1), Is.EqualTo("Password:\r\nLogin incorrect\r\nlogin: "));
            Type(h, "guest\rguest\r");
            Assert.That(o.Take(1), Is.EqualTo("Password:\r\n$ "));
            Type(h, "cat /etc/issue\r");
            Assert.That(o.Take(1), Does.Contain("System V Release 4.0"));
            Type(h, "foo\r");
            Assert.That(o.Take(1), Is.EqualTo("foo: not found\r\n$ "));
            Type(h, "exit\r");
            Assert.That(o.Cleared, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void Tso_LogsOnWithTheLogonCommand_RunsItsCommands_AndLogsOff()
        {
            (DescribedHost h, Output o) = Load("tso");
            h.Connect(1, string.Empty, "24227001");
            Assert.That(o.Take(1), Is.EqualTo("IKJ54012A ENTER LOGON -\r\n"));
            Type(h, "logon guest\r");
            Assert.That(o.Take(1), Is.EqualTo("ENTER CURRENT PASSWORD FOR guest-\r\n"));
            Type(h, "GUEST\r");
            Assert.That(o.Take(1), Is.EqualTo("\r\nIKJ56455I guest LOGON IN PROGRESS AT 09:48:40 ON 19-NOV-2026\r\nREADY\r\n"));
            Type(h, "listc\r");
            Assert.That(o.Take(1), Does.Contain("GUEST.CLIST"));
            Type(h, "frob\r");
            Assert.That(o.Take(1), Is.EqualTo("IKJ56500I COMMAND FROB NOT FOUND\r\nREADY\r\n"));
            Type(h, "LOGOFF\r");
            Assert.That(o.Take(1), Does.StartWith("IKJ56470I guest LOGGED OFF TSO AT 09:48:40 ON 19-NOV-2026"));
            Assert.That(o.Cleared, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void Vm370_LogsOn_GivesTheLogmsg_RunsCms_AndLogsOff()
        {
            (DescribedHost h, Output o) = Load("vm370");
            h.Connect(1, string.Empty, "24227001");
            Assert.That(o.Take(1), Is.EqualTo("VM/370 ONLINE\r\n.\r\n"));
            Type(h, "query time\r");
            Assert.That(o.Take(1), Is.EqualTo("?CP: query\r\n.\r\n"));
            Type(h, "logon guest\rnope\r");
            Assert.That(o.Take(1), Is.EqualTo("ENTER PASSWORD:\r\n\r\nDMKLOG050E PASSWORD INCORRECT\r\n.\r\n"));
            Type(h, "logon guest\rGUEST\r");
            Assert.That(o.Take(1), Does.Contain("LOGON AT 09:48:40 EST 19-NOV-2026\r\nCMS...VERSION 6.1"));
            Type(h, "type PROFILE EXEC\r");
            Assert.That(o.Take(1), Does.Contain("DEMONSTRATION VM/370 SYSTEM"));
            Type(h, "frob\r");
            Assert.That(o.Take(1), Is.EqualTo("UNKNOWN CP/CMS COMMAND\r\nR;\r\n"));
            Type(h, "logoff\r");
            Assert.That(o.Take(1), Does.StartWith("CONNECT=00:01:12 VIRTCPU=000:00.04 TOTCPU=000:00.11\r\nLOGOFF AT 09:48:40 EST 19-NOV-2026"));
            Assert.That(o.Cleared, Is.EqualTo(new List<int> { 1 }));
        }

        private static (DescribedHost Host, Output Out) Load(string folder)
        {
            HostDescription d = HostDescription.Load(FindHostFolder(folder), null, "24221999");
            var o = new Output();
            var h = new DescribedHost(d, o);
            h.Start(T0);
            return (h, o);
        }

        private static string FindHostFolder(string name)
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "demo", "hosts", name);
                if (File.Exists(Path.Combine(candidate, "host.ini")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("the host folder demo/hosts/" + name + " was not found above " + AppContext.BaseDirectory);
        }

        private static void Type(DescribedHost h, string text) => h.Input(1, Encoding.ASCII.GetBytes(text));

        private sealed class Output : IHostOutput, IHostPadControl
        {
            private readonly Dictionary<int, StringBuilder> _text = new Dictionary<int, StringBuilder>();

            public List<int> Cleared { get; } = new List<int>();

            public void Send(int session, ReadOnlySpan<byte> data)
            {
                if (!_text.TryGetValue(session, out StringBuilder? sb))
                {
                    sb = new StringBuilder();
                    _text.Add(session, sb);
                }

                for (int i = 0; i < data.Length; i++)
                {
                    sb.Append((char)data[i]);
                }
            }

            public void Clear(int session) => Cleared.Add(session);

            public void Log(string text)
            {
            }

            public void SetEcho(int session, bool echo)
            {
            }

            /// <summary>What was sent to the session since the last call, then forgotten.</summary>
            public string Take(int session)
            {
                if (!_text.TryGetValue(session, out StringBuilder? sb))
                {
                    return string.Empty;
                }

                string s = sb.ToString();
                sb.Clear();
                return s;
            }
        }
    }
}
