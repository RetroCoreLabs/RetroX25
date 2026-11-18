//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Protocols.X25;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Modules;
using RetroIO.Protocols.X25.Runtime;

namespace RetroX25.Tests
{
    /// <summary>
    /// Step 7 of docs/altos-chat-port-plan.md: the Altos CHAT 3.1 in C# (RetroIO's ChatProtocolService) and in C
    /// (x25emu's tools/x25chat), each called from both PADs - x25emu's x25pad and RetroIO's PadClient - through one
    /// x25emu switch (tools/x25switch) in WSL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every test starts its own network: a switch in WSL with three XOT access ports on TCP ports chosen at run time
    /// (bound to port 0 in WSL, then let go, as x25emu's own tests/integration/test_altoschat_switch.c does), the C#
    /// chat in this process as the XOT client of port 0 answering 26245890040004, x25chat on port 1 answering
    /// 26245890040005, and every caller on port 2. Every program is started with its process number recorded and is
    /// stopped by that number.
    /// </para>
    /// <para>
    /// Skipped unless X25EMU_TOOLS names the WSL folder of x25emu's built programs (x25switch, x25chat, x25pad). The
    /// address Windows reaches WSL at is X25EMU_WSL_HOST, or else the first address <c>hostname -I</c> gives in WSL.
    /// The original source of the chat is at https://archive.org/details/altoschat; it is the authority for every text.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    [NonParallelizable]
    public class AltosChatCrossTests
    {
        private const string CSharpChat = "26245890040004";
        private const string CChat = "26245890040005";

        // X.29 (12/97) 4.2.1: the PAD protocol identifier.
        private static readonly byte[] PadCud = { 0x01, 0x00, 0x00, 0x00 };

        // The X.29 set PAD message both chats send first (plan, test 21): 2=1 3=2 13=4 15=1 16=127 17=24 18=18.
        private static readonly byte[] SetPad = { 0x02, 0x02, 0x01, 0x03, 0x02, 0x0D, 0x04, 0x0F, 0x01, 0x10, 0x7F, 0x11, 0x18, 0x12, 0x12 };

        // CHAT 3.1 chat.c:248-250, 277, 270; serv.c:253, with CR LF line ends (plan, decisions).
        private const string Banner =
            "Welcome to the original, evergreen\r\n" +
            "Conference-utility \"CHAT\"  ver. 3.1\r\n" +
            "Copyright (C) 1987 Hans Korneder, Muenchen\r\n" +
            "Waiting for connection.\r\n" +
            "Connection established.\r\n" +
            "Get help with:    .h\r\n" +
            "+++guest+++\r\n";

        private const string Join = "+++guest+++\r\n";
        private const int Wait = 20000;

        /// <summary>Which chat a test talks to.</summary>
        public enum Server
        {
            /// <summary>RetroIO's ChatProtocolService, in this process.</summary>
            CSharp,

            /// <summary>x25emu's tools/x25chat, in WSL.</summary>
            C,
        }

        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        [TearDown]
        public void TearDown()
        {
            // Last made, first gone: the callers, then the chats, then the switch.
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposables[i].Dispose();
                }
                catch (Exception e)
                {
                    TestContext.Progress.WriteLine("stopping " + _disposables[i].GetType().Name + " failed: " + e.Message);
                }
            }

            _disposables.Clear();
        }

        // ------------------------------------------------------------------ 1. x25pad to the C# chat

        /// <summary>
        /// x25emu's x25pad calls the C# chat through x25switch: the opening screen is the 1987 one, byte for byte; the
        /// X.29 set has arrived, because the PAD now edits the line (DEL removes "o") and echoes Enter as CR LF
        /// (X.3 parameter 13 = 4); and two x25pads talk.
        /// </summary>
        [Test]
        public void X25emuPads_CallTheCSharpChat_SeeThe1987Screen_AndTalk()
        {
            Net net = StartNet(30);
            TwoTalk(net.Address(Server.CSharp), XPad(net, "26240101"), XPad(net, "26240102"));
        }

        // ------------------------------------------------------------------ 2. the C# PAD to x25chat

        /// <summary>
        /// RetroIO's PAD (PadClient, driven key by key as the Desktop and telnet PADs drive it) calls x25emu's x25chat
        /// through x25switch: the same screen, the same set obeyed, and two C# PADs talk.
        /// </summary>
        [Test]
        public void CSharpPads_CallTheCChat_SeeThe1987Screen_AndTalk()
        {
            Net net = StartNet(30);
            TwoTalk(net.Address(Server.C), CsPad(net, "26240201"), CsPad(net, "26240202"));
        }

        // ------------------------------------------------------------------ 3. mixed

        /// <summary>
        /// One x25pad and one C# PAD in the same chat, with each chat as the server.
        /// </summary>
        [TestCase(Server.CSharp)]
        [TestCase(Server.C)]
        public void OneX25emuPadAndOneCSharpPad_TalkInTheSameChat(Server server)
        {
            Net net = StartNet(30);
            TwoTalk(net.Address(server), XPad(net, "26240301"), CsPad(net, "26240302"));
        }

        // ------------------------------------------------------------------ 4. forty users

        /// <summary>
        /// Forty users on one chat, twenty x25pads and twenty C# PADs, limit 40. Half move to channel 1 (ten of each
        /// kind on each channel). Every user then says three lines; every user gets every line of every other user on
        /// its own channel exactly once and in the order sent, and nothing from the other channel.
        /// </summary>
        /// <remarks>
        /// The senders take turns: a PAD shows its own echo on the same screen as the chat's text, and a line arriving
        /// while the user types would be cut by the echo there. Within a turn the three lines go at once.
        /// </remarks>
        [TestCase(Server.CSharp)]
        [TestCase(Server.C)]
        public void FortyUsers_TwentyOfEachPad_EveryLineOnceInOrderOnTheRightChannel(Server server)
        {
            const int Users = 40;
            const int Lines = 3;
            Net net = StartNet(Users);
            string address = net.Address(server);
            ITerminal[] users = new ITerminal[Users];
            int[] channel = new int[Users];
            for (int i = 0; i < Users; i++)
            {
                string calling = "2624" + (4000 + i).ToString("D4");
                users[i] = (i % 2 == 0) ? XPad(net, calling) : CsPad(net, calling);
                channel[i] = (i / 2) % 2;
                users[i].Call(address);
                Assert.That(users[i].WaitForCount(Join, 1, Wait), Is.True, users[i].Name + " did not join:\n" + users[i].Screen);
            }

            for (int i = 0; i < Users; i++)
            {
                Assert.That(users[i].WaitForCount(Join, Users - i, Wait), Is.True,
                    users[i].Name + " did not see the " + (Users - i) + " joins from its own on:\n" + users[i].Screen);
            }

            // To channel 1. A change is told to the old and the new channel (serv.c), which here is everybody.
            int movers = 0;
            for (int i = 0; i < Users; i++)
            {
                if (channel[i] == 1)
                {
                    users[i].Type(".c 1\r");
                    movers++;
                }
            }

            for (int i = 0; i < Users; i++)
            {
                Assert.That(users[i].WaitForCount(">>>guest>>> chan 1\r\n", movers, Wait), Is.True,
                    users[i].Name + " did not see all " + movers + " channel changes:\n" + users[i].Screen);
            }

            for (int s = 0; s < Users; s++)
            {
                StringBuilder lines = new StringBuilder();
                for (int k = 0; k < Lines; k++)
                {
                    lines.Append("u").Append(s.ToString("D2")).Append(" line ").Append(k).Append('\r');
                }

                users[s].Type(lines.ToString());
                for (int r = 0; r < Users; r++)
                {
                    if (r != s && channel[r] == channel[s])
                    {
                        Assert.That(users[r].WaitForCount(SaysLine(s, Lines - 1), 1, Wait), Is.True,
                            users[r].Name + " did not get the last line of " + users[s].Name + ":\n" + users[r].Screen);
                    }
                }
            }

            int checkedLines = 0;
            for (int r = 0; r < Users; r++)
            {
                string screen = users[r].Screen;
                for (int s = 0; s < Users; s++)
                {
                    int previous = -1;
                    for (int k = 0; k < Lines; k++)
                    {
                        string line = SaysLine(s, k);
                        int expected = (r != s && channel[r] == channel[s]) ? 1 : 0;
                        Assert.That(Count(screen, line), Is.EqualTo(expected),
                            users[r].Name + " (channel " + channel[r] + ") and line " + k + " of " + users[s].Name + " (channel " + channel[s] + ")");
                        if (expected == 1)
                        {
                            int at = screen.IndexOf(line, StringComparison.Ordinal);
                            Assert.That(at, Is.GreaterThan(previous), users[r].Name + " got the lines of " + users[s].Name + " out of order");
                            previous = at;
                            checkedLines++;
                        }
                    }
                }
            }

            // Each user hears the 19 others on its channel, three lines each.
            Assert.That(checkedLines, Is.EqualTo(Users * (Users / 2 - 1) * Lines));
            TestContext.Progress.WriteLine(server + " chat: " + checkedLines + " lines checked, each delivered once, in order, on the right channel");
        }

        // ------------------------------------------------------------------ 5. byte for byte

        /// <summary>
        /// The same scripted session, two callers A and B, against the C# chat and against x25chat. What each caller
        /// receives is recorded step by step and compared byte for byte; the X.29 set (with the Q bit) and the clear
        /// after <c>.q</c> are compared too. <c>.w</c> carries the time each call came in, so for that step only the
        /// digits are masked.
        /// </summary>
        [Test]
        public void TheSameSession_GivesTheSameBytes_FromBothChats()
        {
            Net net = StartNet(30);
            Session cs = RunScript(net, Server.CSharp);
            Session c = RunScript(net, Server.C);

            StringBuilder differences = new StringBuilder();
            Compare("Q-bit data to A", cs.A.QData(), c.A.QData(), false, differences);
            Compare("Q-bit data to B", cs.B.QData(), c.B.QData(), false, differences);
            for (int i = 0; i < cs.Steps.Count; i++)
            {
                string step = "step " + i + " (" + cs.Steps[i].Who + " types " + Show(cs.Steps[i].Line) + ")";
                bool mask = cs.Steps[i].Line.StartsWith(".w", StringComparison.Ordinal);
                Compare(step + " to A", cs.Steps[i].ToA, c.Steps[i].ToA, mask, differences);
                Compare(step + " to B", cs.Steps[i].ToB, c.Steps[i].ToB, mask, differences);
            }

            if (cs.ACleared != c.ACleared)
            {
                differences.Append("after .q, A's call was ").Append(cs.ACleared ? "cleared" : "not cleared").Append(" by the C# chat and ")
                    .Append(c.ACleared ? "cleared" : "not cleared").Append(" by x25chat\n");
            }

            for (int i = 0; i < cs.Steps.Count; i++)
            {
                TestContext.Progress.WriteLine("step " + i + " " + cs.Steps[i].Who + " " + Show(cs.Steps[i].Line) + "\n  A: " + Show(Encoding.Latin1.GetString(cs.Steps[i].ToA))
                    + "\n  B: " + Show(Encoding.Latin1.GetString(cs.Steps[i].ToB)));
            }

            TestContext.Progress.WriteLine("steps compared: " + cs.Steps.Count + "; bytes to A: C# " + cs.A.Bytes().Length + ", C " + c.A.Bytes().Length
                + "; bytes to B: C# " + cs.B.Bytes().Length + ", C " + c.B.Bytes().Length);
            Assert.That(differences.ToString(), Is.Empty, "the two chats differ:\n" + differences);
        }

        // ------------------------------------------------------------------ the scripted session

        private sealed class Step
        {
            public string Who = string.Empty;
            public string Line = string.Empty;
            public byte[] ToA = Array.Empty<byte>();
            public byte[] ToB = Array.Empty<byte>();
        }

        private sealed class Session
        {
            public RawCaller A = null!;
            public RawCaller B = null!;
            public List<Step> Steps = new List<Step>();
            public bool ACleared;
        }

        /// <summary>
        /// The script. Each entry is "A " or "B " and then the line typed, without its CR. It covers every command of
        /// the plan's table and the input rules (empty, spaces, control characters, the top bit, 118 and 200
        /// characters).
        /// </summary>
        private static readonly string[] Script =
        {
            "A .h",
            "A .hello",
            "A hello b",
            "B hi a",
            "A ",
            "A    ",
            "A a\u0001b\u0007c",
            "A Èi top bit",
            "A " + new string('x', 200),
            "A " + new string('y', 110) + "        ",
            "A " + new string('z', 100) + "       ",
            "B .n Bob",
            "A .s",
            "A .so",
            "A .nfoo",
            "A .c 5",
            "B .c5",
            "A .p secret",
            "B .c 0",
            "B .p zero",
            "A .p secret",
            "B .c 5",
            "B .c 5,wrong",
            "B .c 5,secretXYZ",
            "B .c 5,secret",
            "A .p longerthan8",
            "B .c 6",
            "A .c 0",
            "B .c 5",
            "A .p",
            "B .c-7",
            "B .c",
            "A !1 psst",
            "A !9 nobody",
            "A !0 to myself",
            "B .cool",
            "A .c 0",
            "B !hi",
            "A first\nsecond",
            "A crlf\r\nnext",
            "A .m",
            "A --!",
            "A .b",
            "A .foo",
            "A .H",
            "A .X",
            "B .n 0123456789012345678901234567890123456789",
            "A .n ",
            "A .s",
            "A .w",
            "A .q",
        };

        private Session RunScript(Net net, Server server)
        {
            string address = net.Address(server);
            Session session = new Session
            {
                A = Raw(net, "26245001"),
                B = Raw(net, "26245002"),
            };
            Assert.That(session.A.Call(address), Is.True, "A could not call the " + server + " chat: " + session.A.ClearCause);
            Assert.That(session.A.WaitForCount(Join, 1, Wait), Is.True, "A did not join: " + session.A.Text);
            Assert.That(session.B.Call(address), Is.True, "B could not call the " + server + " chat: " + session.B.ClearCause);
            Assert.That(session.B.WaitForCount(Join, 1, Wait), Is.True, "B did not join: " + session.B.Text);
            Assert.That(session.A.WaitForCount(Join, 2, Wait), Is.True, "A did not see B join: " + session.A.Text);
            int syncA = 0;
            int syncB = 0;
            Sync(session.A, ref syncA);
            Sync(session.B, ref syncB);
            bool aGone = false;
            for (int i = 0; i < Script.Length; i++)
            {
                RawCaller who = Script[i][0] == 'A' ? session.A : session.B;
                string line = Script[i].Substring(2);
                int fromA = session.A.Bytes().Length;
                int fromB = session.B.Bytes().Length;
                who.TypeLatin1(line + "\r");

                // The script's own .m gets the same reply the syncs wait for: count it, or the sync stops one reply early.
                if (line.StartsWith(".m", StringComparison.Ordinal))
                {
                    if (who == session.A)
                    {
                        syncA++;
                    }
                    else
                    {
                        syncB++;
                    }
                }

                // The one who typed first: its .m goes on the same call behind its line, so when the reply is back the
                // line has been handled. Only then the other: the two calls are separate connections through the switch,
                // and the other's .m could otherwise overtake the line.
                if (line == ".q" && who == session.A)
                {
                    aGone = true;
                    session.ACleared = session.A.WaitForClear(Wait);
                    Sync(session.B, ref syncB);
                }
                else if (who == session.A)
                {
                    Sync(session.A, ref syncA);
                    Sync(session.B, ref syncB);
                }
                else
                {
                    Sync(session.B, ref syncB);
                    Sync(session.A, ref syncA);
                }
                byte[] a = session.A.Bytes();
                byte[] b = session.B.Bytes();
                session.Steps.Add(new Step
                {
                    Who = Script[i].Substring(0, 1),
                    Line = line,
                    ToA = a.AsSpan(fromA).ToArray(),
                    ToB = b.AsSpan(fromB).ToArray(),
                });
            }

            Assert.That(aGone, Is.True);
            session.B.Hangup();
            return session;
        }

        private const string SmtpReply = "This is disabled by default- use SMTP!\r\n";

        /// <summary>
        /// Makes sure everything the chat sent this caller so far has arrived: the caller types <c>.m</c> and waits for
        /// its reply. The chat handles lines in the order they arrive and each caller's output in order, so once the
        /// reply is here, so is everything that came before it.
        /// </summary>
        private static void Sync(RawCaller caller, ref int count)
        {
            caller.TypeLatin1(".m\r");
            count++;
            Assert.That(caller.WaitForCount(SmtpReply, count, Wait), Is.True, caller.Name + " did not get .m reply " + count + ":\n" + caller.Text);
        }

        private static void Compare(string what, byte[] cs, byte[] c, bool maskDigits, StringBuilder differences)
        {
            string left = Show(Encoding.Latin1.GetString(cs));
            string right = Show(Encoding.Latin1.GetString(c));
            if (maskDigits)
            {
                left = MaskDigits(left);
                right = MaskDigits(right);
            }

            if (left != right)
            {
                differences.Append(what).Append(":\n  C#: ").Append(left).Append("\n  C:  ").Append(right).Append('\n');
            }
        }

        private static string MaskDigits(string text)
        {
            StringBuilder b = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                b.Append(char.IsAsciiDigit(text[i]) ? '9' : text[i]);
            }

            return b.ToString();
        }

        /// <summary>Text with every control character and every byte over 126 written as &lt;XX&gt;.</summary>
        private static string Show(string text)
        {
            StringBuilder b = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch < 0x20 || ch > 0x7E)
                {
                    b.Append('<').Append(((int)ch).ToString("X2")).Append('>');
                }
                else
                {
                    b.Append(ch);
                }
            }

            return b.ToString();
        }

        // ------------------------------------------------------------------ two users talk

        /// <summary>
        /// The checks of tests 1 to 3: the first caller's screen holds the 1987 opening exactly; the second joins and
        /// both see it; a line typed with a mistake and DEL arrives corrected; the echo of Enter is CR LF; text crosses
        /// both ways as "guest says: ..." and never comes back to the sender.
        /// </summary>
        private static void TwoTalk(string address, ITerminal one, ITerminal two)
        {
            one.Call(address);
            Assert.That(one.WaitForCount(Join, 1, Wait), Is.True, one.Name + " did not join:\n" + one.Screen);
            Assert.That(one.Screen, Does.Contain(Banner), one.Name + " did not get the 1987 opening exactly:\n" + Show(one.Screen));
            Assert.That(Count(one.Screen, "Welcome to the original"), Is.EqualTo(1));

            two.Call(address);
            Assert.That(two.WaitForCount(Join, 1, Wait), Is.True, two.Name + " did not join:\n" + two.Screen);
            Assert.That(two.Screen, Does.Contain(Banner), two.Name + " did not get the 1987 opening exactly:\n" + Show(two.Screen));
            Assert.That(one.WaitForCount(Join, 2, Wait), Is.True, one.Name + " did not see the second join:\n" + one.Screen);

            // X.3 15 = 1 and 16 = 127: DEL takes back the "o" in the PAD; the chat gets "hello from one".
            one.Type("helo\u007Flo from one\r");
            Assert.That(two.WaitForCount("guest says: hello from one\r\n", 1, Wait), Is.True, two.Name + " did not get the line:\n" + Show(two.Screen));

            // X.3 13 = 4: the echo of the CR is CR LF. The chat never sends the sender its own line, so the text on the
            // sender's screen is the PAD's echo.
            Assert.That(one.WaitForCount("lo from one\r\n", 1, Wait), Is.True, one.Name + " did not echo the line with CR LF:\n" + Show(one.Screen));
            Assert.That(one.Screen, Does.Not.Contain("says: hello from one"), "the sender got its own line back");

            two.Type("and hello from two\r");
            Assert.That(one.WaitForCount("guest says: and hello from two\r\n", 1, Wait), Is.True, one.Name + " did not get the line:\n" + Show(one.Screen));
            Assert.That(two.WaitForCount("and hello from two\r\n", 1, Wait), Is.True, two.Name + " did not echo the line with CR LF:\n" + Show(two.Screen));
            Assert.That(two.Screen, Does.Not.Contain("says: and hello from two"), "the sender got its own line back");

            // The second leaves; the first is told once.
            two.Type(".x\r");
            Assert.That(one.WaitForCount("---guest---\r\n", 1, Wait), Is.True, one.Name + " did not see the other leave:\n" + Show(one.Screen));
            TestContext.Progress.WriteLine(one.Name + " screen:\n" + Show(one.Screen));
            TestContext.Progress.WriteLine(two.Name + " screen:\n" + Show(two.Screen));
        }

        private static string SaysLine(int sender, int line)
        {
            return "guest says: u" + sender.ToString("D2") + " line " + line + "\r\n";
        }

        private static int Count(string text, string part)
        {
            int n = 0;
            int at = text.IndexOf(part, StringComparison.Ordinal);
            while (at >= 0)
            {
                n++;
                at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal);
            }

            return n;
        }

        // ------------------------------------------------------------------ the network

        /// <summary>
        /// The switch, both chats and the folder in WSL that holds the switch's configuration.
        /// </summary>
        private sealed class Net : IDisposable
        {
            public string Host = string.Empty;
            public int CSharpChatPort;
            public int CChatPort;
            public int CallerPort;
            public string Folder = string.Empty;
            public X25emuEnvironment.LinuxProcess? Switch;
            public X25emuEnvironment.LinuxProcess? CChatProcess;
            public ProtocolRuntime? Runtime;

            public string Address(Server server)
            {
                return server == Server.CSharp ? CSharpChat : CChat;
            }

            public void Dispose()
            {
                Runtime?.Dispose();
                CChatProcess?.Dispose();
                Switch?.Dispose();
                if (Folder.Length > 0)
                {
                    X25emuEnvironment.RunWsl("rm -rf '" + Folder + "'", 10000, out _);
                }
            }
        }

        private Net StartNet(int maxUsers)
        {
            if (string.IsNullOrWhiteSpace(X25emuEnvironment.Tools))
            {
                Assert.Ignore("Set X25EMU_TOOLS (the WSL folder of x25emu's built programs: x25switch, x25chat, x25pad) to run this test.");
            }

            string[] programs = { "x25switch", "x25chat", "x25pad" };
            for (int i = 0; i < programs.Length; i++)
            {
                X25emuEnvironment.RunWsl("test -x '" + X25emuEnvironment.ToolPath(programs[i]) + "'", 15000, out int found);
                Assert.That(found, Is.EqualTo(0), "x25emu is not built: " + X25emuEnvironment.ToolPath(programs[i]) + " is missing. Run make in the x25emu checkout.");
            }

            Net net = new Net();
            _disposables.Add(net);
            net.Host = X25emuEnvironment.WslHost ?? string.Empty;
            if (string.IsNullOrWhiteSpace(net.Host))
            {
                string[] words = X25emuEnvironment.RunWsl("hostname -I", 15000, out _).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                Assert.That(words.Length, Is.GreaterThan(0), "WSL gave no address");
                net.Host = words[0];
            }

            // Three ports nothing uses now, chosen by WSL itself (bind to 0, read, let go).
            string ports = X25emuEnvironment.RunWsl(
                "python3 -c 'import socket\nss=[socket.socket() for i in range(3)]\nfor s in ss: s.bind((\"\",0))\nprint(\" \".join(str(s.getsockname()[1]) for s in ss))'",
                15000, out int portExit);
            string[] p = ports.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.That(portExit == 0 && p.Length == 3, Is.True, "could not choose ports in WSL: " + ports);
            net.CSharpChatPort = int.Parse(p[0]);
            net.CChatPort = int.Parse(p[1]);
            net.CallerPort = int.Parse(p[2]);

            // The switch: network 2624; ports 0 and 1 are the chats' lines, port 2 is where the callers connect. An XOT
            // port takes one TCP connection per channel, and a C# caller keeps a spare one open, so the channel counts
            // are well above the most calls.
            net.Folder = "/tmp/altoschat-cross-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string config =
                "[switch]\ndnic = 2624\nown_dnic = 2624\nmgmt_port = 0\nname = ChatCross\n\n" +
                "[port.0]\ntype = access\nmode = dce\ntransport = xot\nport = " + net.CSharpChatPort + "\nlci_base = 1\nlci_count = 128\n\n" +
                "[port.1]\ntype = access\nmode = dce\ntransport = xot\nport = " + net.CChatPort + "\nlci_base = 1\nlci_count = 128\n\n" +
                "[port.2]\ntype = access\nmode = dce\ntransport = xot\nport = " + net.CallerPort + "\nlci_base = 1\nlci_count = 256\n\n" +
                "[route.0]\ntype = prefix\naddress = " + CSharpChat + "\nprefix_len = 14\nport = 0\n\n" +
                "[route.1]\ntype = prefix\naddress = " + CChat + "\nprefix_len = 14\nport = 1\n";
            X25emuEnvironment.RunWsl("mkdir -p '" + net.Folder + "' && cat > '" + net.Folder + "/switch.conf' <<'EOF'\n" + config + "EOF\n", 15000, out int wrote);
            Assert.That(wrote, Is.EqualTo(0), "could not write the switch configuration in " + net.Folder);

            net.Switch = new X25emuEnvironment.LinuxProcess(string.Empty, X25emuEnvironment.ToolPath("x25switch"), "-v", "-c", net.Folder + "/switch.conf");
            Assert.That(SpinWait.SpinUntil(() => CanConnect(net.Host, net.CallerPort) || net.Switch.HasExited, Wait), Is.True,
                "the switch never took connections at " + net.Host + ":" + net.CallerPort + ":\n" + net.Switch.Output);
            Assert.That(net.Switch.HasExited, Is.False, "the switch stopped:\n" + net.Switch.Output);

            // The C chat, no pause after "Waiting for connection." (the C# one is given the same).
            net.CChatProcess = new X25emuEnvironment.LinuxProcess(string.Empty, X25emuEnvironment.ToolPath("x25chat"),
                "-x", "127.0.0.1:" + net.CChatPort, "-a", CChat, "-n", maxUsers.ToString(), "-w", "0");

            // The C# chat, as the XOT client of port 0 (the recipe of RetroIO's ChatProtocolServiceTests.StartChatRuntime,
            // with the switch's host in WSL).
            RuntimeConfig rc = new RuntimeConfig();
            rc.X25.LocalMachineId = "26245890040001";
            rc.Chat.Address = CSharpChat;
            rc.Chat.MaxUsers = maxUsers;
            rc.Chat.WaitPauseMs = 0;
            rc.Routing.XotInterfaces.Add(new XotInterfaceConfig
            {
                Name = "Altos",
                Mode = XotMode.Client,
                RemoteIPAddress = net.Host,
                RemotePort = net.CSharpChatPort,
                KeepConnectionForIncomingCalls = true,
            });
            rc.Routing.RoutingTable.Add(new X25RouteEntry
            {
                Name = "Chat",
                X121AddressPattern = CSharpChat,
                Type = RetroIO.Protocols.X25.Runtime.RouteType.Local,
                Destination = "CHAT",
                Priority = 200,
            });
            net.Runtime = new ProtocolRuntime(NullLogger<ProtocolRuntime>.Instance, rc);
            Assert.That(net.Runtime.StartTransportInterfaces(), Is.True, "the C# chat's XOT entry did not start");

            // Both chats hold a connection on their switch port before any call is placed.
            Assert.That(SpinWait.SpinUntil(() => net.Switch.Output.Contains("Port 0: connection up") && net.Switch.Output.Contains("Port 1: connection up"), Wait), Is.True,
                "a chat never connected to the switch:\nswitch:\n" + net.Switch.Output + "\nx25chat:\n" + net.CChatProcess.Output);
            return net;
        }

        private static bool CanConnect(string host, int port)
        {
            try
            {
                using TcpClient client = new TcpClient();
                return client.ConnectAsync(host, port).Wait(2000) && client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ callers

        /// <summary>A user at a PAD: the screen (echo and the chat's text, in the order shown) and the keyboard.</summary>
        private interface ITerminal
        {
            string Name { get; }

            string Screen { get; }

            void Call(string address);

            void Type(string keys);

            bool WaitForCount(string part, int count, int timeoutMs);
        }

        private ITerminal XPad(Net net, string calling)
        {
            X25emuPadTerminal pad = new X25emuPadTerminal(net.CallerPort, calling);
            _disposables.Add(pad);
            return pad;
        }

        private ITerminal CsPad(Net net, string calling)
        {
            CSharpPadTerminal pad = new CSharpPadTerminal(net.Host, net.CallerPort, calling);
            _disposables.Add(pad);
            return pad;
        }

        private RawCaller Raw(Net net, string calling)
        {
            RawCaller caller = new RawCaller(net.Host, net.CallerPort, calling);
            _disposables.Add(caller);
            return caller;
        }

        /// <summary>Text waited on under a lock, woken whenever more arrives.</summary>
        private abstract class Screened
        {
            protected readonly object Gate = new object();
            private readonly StringBuilder _screen = new StringBuilder();

            public string Screen
            {
                get
                {
                    lock (Gate)
                    {
                        return _screen.ToString();
                    }
                }
            }

            protected void Show(string text)
            {
                lock (Gate)
                {
                    _screen.Append(text);
                    System.Threading.Monitor.PulseAll(Gate);
                }
            }

            public bool WaitForCount(string part, int count, int timeoutMs)
            {
                long end = Environment.TickCount64 + timeoutMs;
                lock (Gate)
                {
                    while (Count(_screen.ToString(), part) < count)
                    {
                        long left = end - Environment.TickCount64;
                        if (left <= 0)
                        {
                            return false;
                        }

                        System.Threading.Monitor.Wait(Gate, (int)left);
                    }

                    return true;
                }
            }
        }

        /// <summary>
        /// x25emu's x25pad in WSL, its standard output read as raw bytes (not lines, so every CR and LF is kept) and
        /// its keyboard fed through standard input. Started as <c>wsl.exe -e bash -c 'echo $$; exec "$@"'</c>, so the
        /// first line out is the program's own Linux process number, the number it is stopped by.
        /// </summary>
        private sealed class X25emuPadTerminal : Screened, ITerminal, IDisposable
        {
            private readonly Process _wsl;
            private readonly Thread _reader;
            private readonly StringBuilder _errors = new StringBuilder();
            private readonly ManualResetEventSlim _gotPid = new ManualResetEventSlim(false);
            private int _linuxPid;
            private bool _disposed;

            public X25emuPadTerminal(int callerPort, string calling)
            {
                Name = "x25pad " + calling;
                ProcessStartInfo start = new ProcessStartInfo("wsl.exe")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                string? distro = Environment.GetEnvironmentVariable("X25EMU_WSL_DISTRO");
                if (!string.IsNullOrWhiteSpace(distro))
                {
                    start.ArgumentList.Add("-d");
                    start.ArgumentList.Add(distro);
                }

                start.ArgumentList.Add("-e");
                start.ArgumentList.Add("bash");
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add("echo $$; exec \"$@\"");
                start.ArgumentList.Add("bash");
                start.ArgumentList.Add(X25emuEnvironment.ToolPath("x25pad"));
                start.ArgumentList.Add("-x");
                start.ArgumentList.Add("127.0.0.1:" + callerPort);
                start.ArgumentList.Add("-a");
                start.ArgumentList.Add(calling);
                _wsl = new Process { StartInfo = start };
                _wsl.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (_errors)
                        {
                            _errors.AppendLine(e.Data);
                        }
                    }
                };
                _wsl.Start();
                _wsl.BeginErrorReadLine();
                _reader = new Thread(Read) { IsBackground = true, Name = Name };
                _reader.Start();
                Assert.That(_gotPid.Wait(15000), Is.True, "WSL did not report the process number of " + Name);
                Assert.That(WaitForCount("PAD ready", 1, Wait), Is.True, Name + " did not start:\n" + Screen + "\n" + Errors);
            }

            public string Name { get; }

            private string Errors
            {
                get
                {
                    lock (_errors)
                    {
                        return _errors.ToString();
                    }
                }
            }

            private void Read()
            {
                Stream stream = _wsl.StandardOutput.BaseStream;
                byte[] buffer = new byte[4096];
                StringBuilder pid = new StringBuilder();
                while (true)
                {
                    int n;
                    try
                    {
                        n = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    if (n <= 0)
                    {
                        return;
                    }

                    int from = 0;
                    if (!_gotPid.IsSet)
                    {
                        while (from < n && buffer[from] != (byte)'\n')
                        {
                            pid.Append((char)buffer[from]);
                            from++;
                        }

                        if (from < n)
                        {
                            from++;
                            _linuxPid = int.Parse(pid.ToString().Trim());
                            _gotPid.Set();
                        }
                    }

                    if (from < n)
                    {
                        Show(Encoding.Latin1.GetString(buffer, from, n - from));
                    }
                }
            }

            public void Call(string address)
            {
                Type("CALL " + address + "\r");
            }

            public void Type(string keys)
            {
                byte[] bytes = Encoding.Latin1.GetBytes(keys);
                Stream input = _wsl.StandardInput.BaseStream;
                input.Write(bytes, 0, bytes.Length);
                input.Flush();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                // Stopped by its own Linux process number, then the wsl.exe this object started, by its own handle.
                if (_linuxPid != 0 && !_wsl.HasExited)
                {
                    X25emuEnvironment.RunWsl("kill -TERM " + _linuxPid + " 2>/dev/null", 10000, out _);
                    _wsl.WaitForExit(5000);
                }

                try
                {
                    if (!_wsl.HasExited)
                    {
                        _wsl.Kill(true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }

                _wsl.Dispose();
                _gotPid.Dispose();
            }
        }

        /// <summary>
        /// RetroIO's PAD as the Desktop and telnet PADs use it: a PadClient over an X25Client, fed one key at a time;
        /// the screen is the PAD's echo and the chat's text in the order the PAD shows them.
        /// </summary>
        private sealed class CSharpPadTerminal : Screened, ITerminal, IDisposable
        {
            private readonly X25Client _client;
            private readonly PadClient _pad;
            private readonly ManualResetEventSlim _connected = new ManualResetEventSlim(false);

            public CSharpPadTerminal(string host, int callerPort, string calling)
            {
                Name = "C# PAD " + calling;
                _client = new X25Client { ServiceName = "chat-pad-" + calling };
                X25ClientPadTransport transport = new X25ClientPadTransport(_client);
                _pad = new PadClient(NullLogger.Instance, transport) { LocalAddress = calling };
                _client.SetOnConnected((c, vc) => _pad.NotifyCallAccepted(vc.Lcn));
                _client.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    byte[] copy = new byte[length];
                    Array.Copy(data, copy, length);
                    _pad.DeliverData(vc.Lcn, copy, qBit);
                });
                _client.SetOnClear((c, vc, cause, diagnostic) => _pad.DeliverClear(vc.Lcn, cause, diagnostic));
                _pad.ConnectionStateChanged += (s, e) =>
                {
                    if (e.State == PadConnectionState.Connected)
                    {
                        _connected.Set();
                    }
                };
                _pad.DataReceived += (s, e) => Show(e.Data);
                _pad.CharacterEcho += (s, e) => Show(((char)e.Character).ToString());
                _client.Open(new X25ClientConfig { Host = host, Port = (ushort)callerPort, LocalAddress = calling, Framing = X25ClientFraming.Xot }, NullLogger.Instance);
                _pad.Initialize();
            }

            public string Name { get; }

            public void Call(string address)
            {
                Type("CALL " + address + "\r");
                Assert.That(_connected.Wait(Wait), Is.True, Name + " did not connect to " + address + ":\n" + Screen);
            }

            public void Type(string keys)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    _pad.OnKey(keys[i]);
                }
            }

            public void Dispose()
            {
                _pad.Dispose();
                _client.Dispose();
                _connected.Dispose();
            }
        }

        /// <summary>
        /// A bare X.25 caller: it places a PAD call (protocol identifier 01 00 00 00) and keeps every byte the chat sends,
        /// user data and Q-bit data apart. It types like a PAD that forwards on CR: the whole line and its CR in one
        /// packet, nothing edited and nothing echoed - so what it records is exactly what the chat sent.
        /// </summary>
        private sealed class RawCaller : IDisposable
        {
            private readonly X25Client _client;
            private readonly List<byte> _data = new List<byte>();
            private readonly List<byte> _q = new List<byte>();
            private readonly object _gate = new object();
            private readonly ManualResetEventSlim _cleared = new ManualResetEventSlim(false);
            private X25Vc? _vc;

            public RawCaller(string host, int callerPort, string calling)
            {
                Name = "caller " + calling;
                _client = new X25Client { ServiceName = "chat-raw-" + calling };
                _client.SetOnData((c, vc, data, length, q, m, d) =>
                {
                    lock (_gate)
                    {
                        List<byte> into = q ? _q : _data;
                        for (int i = 0; i < length; i++)
                        {
                            into.Add(data[i]);
                        }

                        System.Threading.Monitor.PulseAll(_gate);
                    }
                });
                _client.SetOnClear((c, vc, cause, diagnostic) =>
                {
                    ClearCause = "cause " + cause + " diagnostic " + diagnostic;
                    _cleared.Set();
                });
                _client.Open(new X25ClientConfig { Host = host, Port = (ushort)callerPort, LocalAddress = calling, Framing = X25ClientFraming.Xot }, NullLogger.Instance);
            }

            public string Name { get; }

            public string ClearCause { get; private set; } = "no clear";

            public string Text => Encoding.Latin1.GetString(Bytes());

            public byte[] Bytes()
            {
                lock (_gate)
                {
                    return _data.ToArray();
                }
            }

            public byte[] QData()
            {
                lock (_gate)
                {
                    return _q.ToArray();
                }
            }

            public bool Call(string address)
            {
                _vc = _client.CallWait(address, Wait, PadCud);
                return _vc != null && _vc.Connected;
            }

            public void TypeLatin1(string line)
            {
                // A packet holds at most 128 octets (the default packet size); a longer line goes as a full packet with
                // the M bit, then the rest (X.25 4.3.5), as a PAD that fills its buffer sends it.
                byte[] bytes = Encoding.Latin1.GetBytes(line);
                int at = 0;
                while (at < bytes.Length)
                {
                    int length = Math.Min(128, bytes.Length - at);
                    bool more = at + length < bytes.Length;
                    Assert.That(_client.Send(_vc!, bytes.AsSpan(at, length).ToArray(), length, false, more), Is.True, Name + " could not send " + Show(line));
                    at += length;
                }
            }

            public void Hangup()
            {
                if (_vc != null && !_cleared.IsSet)
                {
                    _client.Clear(_vc);
                }
            }

            public bool WaitForClear(int timeoutMs)
            {
                return _cleared.Wait(timeoutMs);
            }

            public bool WaitForCount(string part, int count, int timeoutMs)
            {
                long end = Environment.TickCount64 + timeoutMs;
                lock (_gate)
                {
                    while (Count(Encoding.Latin1.GetString(_data.ToArray()), part) < count)
                    {
                        long left = end - Environment.TickCount64;
                        if (left <= 0)
                        {
                            return false;
                        }

                        System.Threading.Monitor.Wait(_gate, (int)left);
                    }

                    return true;
                }
            }

            public void Dispose()
            {
                _client.Dispose();
                _cleared.Dispose();
            }
        }
    }
}
