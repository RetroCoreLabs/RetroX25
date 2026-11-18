//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.PacketLayer;

namespace RetroX25.Tests
{
    /// <summary>
    /// The three-country X.75 network of <c>src/demo/x75-countries</c>, started by its own script from its own ini
    /// files - the same thing a person starts by hand (README.md there). Norway (2422) - England (2342) - Germany
    /// (2624); PAD users in Norway with NUIs; the Altos chat and an echo service per country.
    /// </summary>
    /// <remarks>
    /// The script is run with <c>-BinDir</c> set to this test's output folder (x25switch.exe, x25echo.exe and
    /// x25service.exe are built here by project references) and <c>-StateDir</c> set to a temporary folder, which is
    /// deleted afterwards. <c>x75.ps1 stop</c> stops the processes by the ids it recorded. The network uses fixed TCP
    /// ports, so this test cannot run while the network is up by hand: the script refuses and says so.
    /// The test talks XOT (RFC 1613) to Norway's PAD line itself, as x25pad does: a Call Request with the NUI and
    /// the PAD's call user data, then data packets.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public class X75CountriesScriptTests
    {
        private const string Altos = "026245890040004";
        private const int PadPort = 25001;

        private static readonly byte[] PadCallUserData = { 0x01, 0x00, 0x00, 0x00 };

        private string _script = string.Empty;
        private string _state = string.Empty;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        [OneTimeSetUp]
        public void StartTheNetwork()
        {
            _script = FindScript();
            _state = Path.Combine(Path.GetTempPath(), "x75countries-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_state);
            string output = RunScript("start", out int exit);
            Assert.That(exit, Is.EqualTo(0), "x75.ps1 start failed:\n" + output);
        }

        [OneTimeTearDown]
        public void StopTheNetwork()
        {
            for (int i = 0; i < _disposables.Count; i++)
            {
                _disposables[i].Dispose();
            }

            if (_state.Length > 0)
            {
                string output = RunScript("stop", out int exit);
                TestContext.Progress.WriteLine(output);
                Assert.That(exit, Is.EqualTo(0), "x75.ps1 stop failed (logs kept in " + _state + "):\n" + output);
                Directory.Delete(_state, recursive: true);
            }
        }

        /// <summary>
        /// Two test users in Norway call Altos Germany with the international prefix and meet in the chat: what one
        /// types, the other reads as "guest says: ...", both ways; ".x" leaves and the other sees "---guest---".
        /// </summary>
        [Test]
        public void TwoUsersInNorway_MeetInTheAltosChatInGermany()
        {
            Caller anna = Call(Altos, "242226500000101");
            Caller bjorn = Call(Altos, "242226500000102");
            Assert.That(anna.Cleared, Is.False, anna.Screen.ToString());
            Assert.That(bjorn.Cleared, Is.False, bjorn.Screen.ToString());

            // The 1987 welcome, then the pause (WaitPauseMs 3000 in altos-chat.json), then the conference.
            WaitForText(anna, "Conference-utility \"CHAT\"  ver. 3.1");
            WaitForText(anna, "+++guest+++");
            WaitForText(bjorn, "+++guest+++");

            Type(anna, "hello from Tromso\r");
            WaitForText(bjorn, "guest says: hello from Tromso");
            Type(bjorn, "hei Anna\r");
            WaitForText(anna, "guest says: hei Anna");

            Type(anna, ".x\r");
            WaitForText(bjorn, "---guest---");
        }

        /// <summary>
        /// The echo service in each country answers, so each hop is proved on its own: Norway by its national number,
        /// England and Germany with the international prefix.
        /// </summary>
        [TestCase("9001", TestName = "Echo in Norway, national number 9001")]
        [TestCase("023429001", TestName = "Echo in England, 023429001")]
        [TestCase("026249001", TestName = "Echo in Germany, 026249001")]
        public void TheEchoServiceInEachCountry_SendsTheLineBack(string called)
        {
            Caller c = Call(called, "242226500000103");
            Assert.That(c.Cleared, Is.False, "the call was cleared: " + c.Screen);
            Type(c, "ping " + called + "\r");
            WaitForText(c, "ping " + called);
            Clear(c);
        }

        /// <summary>
        /// The test servers of machines.ini, all run by one x25host, one on each country's host line: the clock in
        /// Norway (national number), the game in England and the notice board in Germany (international prefix).
        /// </summary>
        [Test]
        public void TheTestServersOfMachinesIni_AnswerInEachCountry()
        {
            Caller clock = Call("1001", "242226500000103");
            Assert.That(clock.Cleared, Is.False, "the clock was not reached: " + clock.Screen);
            WaitForText(clock, "OSLOCLOCK speaking clock");
            WaitForText(clock, "Goodbye.");

            Caller game = Call("023421001", "242226500000103");
            Assert.That(game.Cleared, Is.False, "the game was not reached: " + game.Screen);
            WaitForText(game, "Welcome to LONDONGAMES");
            WaitForText(game, "Your guess?");
            Type(game, "q\r");
            WaitForText(game, "Goodbye.");

            Caller board = Call("026241001", "242226500000103");
            Assert.That(board.Cleared, Is.False, "the board was not reached: " + board.Screen);
            WaitForText(board, "MUNICHBOARD notice board");
            Type(board, "hello from Norway\r");
            WaitForText(board, "Pinned.");
            Clear(board);
        }

        /// <summary>
        /// The two host folders of machines.ini (demo/hosts/vax and demo/hosts/bbs), called through the network from
        /// Norway's PAD line: the VAX in England with its DCL login, the bulletin board in Norway with its menus.
        /// </summary>
        [Test]
        public void TheHostFoldersOfMachinesIni_LogInAndAnswer()
        {
            Caller vax = Call("023421002", "242226500000102");
            Assert.That(vax.Cleared, Is.False, "the VAX was not reached: " + vax.Screen);
            WaitForText(vax, "TOMVAX - VAX 8650");
            WaitForText(vax, "Username: ");
            Type(vax, "guest\r");
            WaitForText(vax, "Password: ");
            Type(vax, "GUEST\r");
            WaitForText(vax, "$ ");
            Type(vax, "directory\r");
            WaitForText(vax, "NOTES.TXT");
            Type(vax, "logout\r");
            WaitForText(vax, "logged out at");

            Caller bbs = Call("1002", "242226500000102");
            Assert.That(bbs.Cleared, Is.False, "the BBS was not reached: " + bbs.Screen);
            WaitForText(bbs, "Name (GUEST for a guest visit): ");
            Type(bbs, "guest\r");
            WaitForText(bbs, "Password: ");
            Type(bbs, "x\r");
            WaitForText(bbs, "Choice: ");
            Type(bbs, "w\r");
            WaitForText(bbs, "guest from 2422700");
            Type(bbs, "g\r");
            WaitForText(bbs, "Thank you for calling NORDBBS.");
        }

        /// <summary>
        /// Norway's PAD line requires a NUI (nui_required = yes): a call without one is cleared with Local procedure
        /// error (0x13), diagnostic 84 (X.25 6.21.3, Annex E).
        /// </summary>
        [Test]
        public void ACallWithoutANui_IsCleared_LocalProcedureError_Diagnostic84()
        {
            Caller c = Call(Altos, null);
            Assert.That(c.Cleared, Is.True, "the call was not refused");
            Assert.That(c.ClearCause, Is.EqualTo(0x13));
            Assert.That(c.ClearDiagnostic, Is.EqualTo(84));
        }

        /// <summary>
        /// A NUI that names no account in norway.ini is refused the same way.
        /// </summary>
        [Test]
        public void ACallWithAnUnknownNui_IsCleared()
        {
            Caller c = Call(Altos, "242299999999999");
            Assert.That(c.Cleared, Is.True, "a NUI with no account was accepted");
        }

        /// <summary>
        /// Norway writes one call record per finished call, naming the NUI that paid. Test user 4 makes one echo call.
        /// </summary>
        [Test]
        public void NorwaysCallRecords_NameThePayingNui()
        {
            Caller c = Call("9001", "242226500000104");
            Assert.That(c.Cleared, Is.False, c.Screen.ToString());
            Clear(c);

            string file = Path.Combine(_state, "logs", "norway-calls.txt");
            bool found = SpinWait.SpinUntil(() => File.Exists(file) && ReadShared(file).Contains("242226500000104"), 10000);
            Assert.That(found, Is.True, "no call record with the NUI in " + file + ":\n" + (File.Exists(file) ? ReadShared(file) : "(no file)"));
        }

        // ---- the network and the script ----

        private static string FindScript()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "demo", "x75-countries", "x75.ps1");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("demo/x75-countries/x75.ps1 not found above " + AppContext.BaseDirectory);
        }

        // Runs x75.ps1 and returns what it printed. Its output goes to a file, not a pipe: the programs the script
        // starts inherit the script's standard handles, so a pipe would stay open as long as the network runs and
        // reading it to the end would wait for ever (it did, the first time).
        private string RunScript(string command, out int exitCode)
        {
            string outFile = Path.Combine(_state, "script-" + command + ".txt");
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("& '" + _script + "' " + command + " -BinDir '" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
                + "' -StateDir '" + _state + "' *> '" + outFile + "'; exit $LASTEXITCODE");

            using Process process = Process.Start(info)!;
            int pid = process.Id;
            if (!process.WaitForExit(120000))
            {
                // Killed by the id recorded at start, never by name.
                Process.GetProcessById(pid).Kill(entireProcessTree: false);
                process.WaitForExit(5000);
                exitCode = -1;
                return "x75.ps1 " + command + " did not finish in 120 s\n" + (File.Exists(outFile) ? ReadShared(outFile) : string.Empty);
            }

            exitCode = process.ExitCode;
            return File.Exists(outFile) ? ReadShared(outFile) : string.Empty;
        }
        private static string ReadShared(string file)
        {
            using FileStream s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader r = new StreamReader(s);
            return r.ReadToEnd();
        }

        // ---- one PAD user, talking XOT to Norway's PAD line ----

        private sealed class Caller
        {
            public NetworkStream Stream = null!;
            public ushort Lci = 1;
            public byte SendSeq;
            public byte RecvSeq;
            public bool Cleared;
            public byte ClearCause;
            public byte ClearDiagnostic;
            public readonly StringBuilder Screen = new StringBuilder();
        }

        // Places a PAD call (call user data 01 00 00 00, X.29) on Norway's PAD line, with the NUI when one is given,
        // and returns once it is connected or cleared.
        private Caller Call(string called, string? nui)
        {
            TcpClient tcp = new TcpClient { NoDelay = true };
            _disposables.Add(tcp);
            tcp.Connect("127.0.0.1", PadPort);
            Caller c = new Caller { Stream = tcp.GetStream() };
            c.Stream.ReadTimeout = 15000;

            byte[]? facilities = nui == null ? null : X25CallParameters.NuiSelectionElement(Encoding.ASCII.GetBytes(nui));
            SendXot(c.Stream, X25PacketBuilder.CreateCallRequest(c.Lci, called, "10000", facilities, PadCallUserData));

            while (true)
            {
                X25PacketInfo info = Read(c.Stream);
                if (info.PacketType == X25PacketType.CallAccepted)
                {
                    return c;
                }

                if (info.PacketType == X25PacketType.ClearRequest)
                {
                    c.Cleared = true;
                    c.ClearCause = info.ClearingCause;
                    c.ClearDiagnostic = info.DiagnosticCode;
                    c.Screen.Append("cleared: cause 0x" + info.ClearingCause.ToString("X2") + " diagnostic " + info.DiagnosticCode);
                    SendXot(c.Stream, X25PacketBuilder.CreateClearConfirmation(c.Lci));
                    return c;
                }
            }
        }

        private static void Clear(Caller c)
        {
            SendXot(c.Stream, X25PacketBuilder.CreateClearRequest(c.Lci, 0x00, 0x00));
            long deadline = Environment.TickCount64 + 10000;
            while (Environment.TickCount64 < deadline)
            {
                X25PacketInfo info = Read(c.Stream);
                if (info.PacketType == X25PacketType.ClearConfirmation || info.PacketType == X25PacketType.ClearRequest)
                {
                    return;
                }
            }

            Assert.Fail("no clear confirmation");
        }

        private static void Type(Caller c, string line)
        {
            SendXot(c.Stream, X25PacketBuilder.CreateDataPacket(c.Lci, c.SendSeq, c.RecvSeq, Encoding.ASCII.GetBytes(line)));
            c.SendSeq = (byte)((c.SendSeq + 1) & 7);
        }

        // Reads until the screen holds the text. Every data packet is acknowledged with an RR (X.25 4.4.1.2, modulo 8)
        // so the far end's window stays open; Q-bit packets (X.29 messages to the PAD) are not screen text.
        private static void WaitForText(Caller c, string wanted)
        {
            long deadline = Environment.TickCount64 + 20000;
            while (c.Screen.ToString().IndexOf(wanted, StringComparison.Ordinal) < 0)
            {
                Assert.That(Environment.TickCount64, Is.LessThan(deadline), "never saw \"" + wanted + "\"; screen so far:\n" + c.Screen);
                X25PacketInfo info = Read(c.Stream);
                Assert.That(info.PacketType, Is.Not.EqualTo(X25PacketType.ClearRequest),
                    "cleared: cause 0x" + info.ClearingCause.ToString("X2") + " diagnostic " + info.DiagnosticCode + "; screen so far:\n" + c.Screen);
                if (info.PacketType != X25PacketType.Data)
                {
                    continue;
                }

                c.RecvSeq = (byte)((info.PS + 1) & 7);
                SendXot(c.Stream, X25PacketBuilder.CreateRR(c.Lci, c.RecvSeq));
                if (!info.QBit)
                {
                    c.Screen.Append(Encoding.ASCII.GetString(info.UserData));
                }
            }
        }

        // XOT (RFC 1613): a 2-octet version (0) and a 2-octet length before each packet.
        private static void SendXot(NetworkStream stream, byte[] packet)
        {
            byte[] frame = new byte[4 + packet.Length];
            frame[2] = (byte)(packet.Length >> 8);
            frame[3] = (byte)packet.Length;
            packet.CopyTo(frame, 4);
            stream.Write(frame, 0, frame.Length);
        }

        private static X25PacketInfo Read(NetworkStream stream)
        {
            byte[] header = ReadExactly(stream, 4);
            return X25PacketParser.ParsePacket(ReadExactly(stream, (header[2] << 8) | header[3]));
        }

        private static byte[] ReadExactly(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n == 0)
                {
                    throw new IOException("the switch closed the connection");
                }

                read += n;
            }

            return buffer;
        }
    }
}
