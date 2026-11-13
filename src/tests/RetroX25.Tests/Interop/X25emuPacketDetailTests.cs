//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using NUnit.Framework;
using RetroIO.Protocols.X25.Models;
using RetroIO.Protocols.X25.Protocols;

namespace RetroX25.Tests
{
    /// <summary>
    /// Packet details through x25emu's switch: 16 and 128 byte packets, window sizes 1 to 7, the Q, M and D bits, reset and
    /// clear with several cause codes, all modulo 8. Every packet is built byte by byte (RawXotCaller), because no ready-made tool
    /// sets these bits: x25emu's x25traffic has no Q, M or D option, and our X25Client sets Q only.
    /// </summary>
    /// <remarks>
    /// <para>The route: the raw caller connects to Switch A's access port 2001 and calls 23430001; Switch A sends the call to
    /// our answering side on its port 3 (an <see cref="X25emuEchoPool"/> of our own X25Client connections, one per call). The far end
    /// therefore records exactly what x25emu delivered: data, Q, M and D, clear cause and diagnostic.</para>
    /// <para>What is NOT covered: the D bit end to end acknowledgement. The D bit is sent and what arrives is recorded, but
    /// whether x25emu honours it (confirms delivery) is only observed, not required. Extended (modulo 128) numbering is out
    /// of scope for this project.</para>
    /// </remarks>
    [TestFixture]
    [Category("Interop")]
    public sealed class X25emuPacketDetailTests
    {
        private const string FarAddress = "23430001";
        private const string CallerAddress = "23400077";

        private static byte[] Pattern(int size, int seed)
        {
            byte[] bytes = new byte[size];
            for (int i = 0; i < size; i++)
            {
                // Bytes 0x20 to 0x7E: printable, no 0x04 (the end-of-text byte x25emu's own echo stops on), no CR or LF.
                bytes[i] = (byte)(0x20 + ((i * 7 + seed * 13) % 0x5F));
            }

            return bytes;
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes);
        }

        private static string Facilities(byte[] callAccepted)
        {
            X25PacketInfo info = X25PacketParser.ParsePacket(callAccepted);
            return Hex(info.Facilities);
        }

        /// <summary>
        /// For each packet size (16 and 128 bytes) and each window size 1 to 7: a call is placed with those facilities, 20 packets
        /// of the full size are sent in batches of the window, and every one must arrive byte for byte. The Call Accepted
        /// facilities x25emu returns and the data packets it delivers are decoded from the raw bytes.
        /// </summary>
        [Test]
        public void PacketSizes16And128_WindowSizes1To7_Modulo8_EveryPacketArrivesByteForByte()
        {
            X25emuEnvironment.RequireReady();
            StringBuilder facts = new StringBuilder();
            byte[] sizeCodes = { 4, 7 };
            int[] sizes = { 16, 128 };
            for (int s = 0; s < sizes.Length; s++)
            {
                for (byte window = 1; window <= 7; window++)
                {
                    using X25emuEchoPool far = new X25emuEchoPool(X25emuEnvironment.WslHost!, X25emuEnvironment.CSharpAccessA, FarAddress, 1);
                    using RawXotCaller caller = new RawXotCaller(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessPort, 5);
                    Assert.That(caller.Call(FarAddress, CallerAddress, sizeCodes[s], window, 10000), Is.True, "no Call Accepted for size " + sizes[s] + " window " + window + "\n" + caller.Dump());
                    byte[]? accepted = caller.WaitFor(p => p.Length >= 3 && p[2] == 0x0F, 1000);
                    const int Packets = 20;
                    List<byte[]> sent = new List<byte[]>();
                    int next = 0;
                    while (next < Packets)
                    {
                        int batchEnd = Math.Min(Packets, next + window);
                        for (; next < batchEnd; next++)
                        {
                            byte[] payload = Pattern(sizes[s], next);
                            sent.Add(payload);
                            caller.SendData(payload);
                        }

                        int target = next;
                        Assert.That(SpinWait.SpinUntil(() => far.Received().Count >= target, 10000), Is.True,
                            "size " + sizes[s] + " window " + window + ": only " + far.Received().Count + " of " + target + " arrived\n" + caller.Dump());
                    }

                    List<(byte[] Data, bool Q, bool M, bool D)> got = far.Received();
                    Assert.That(got.Count, Is.EqualTo(Packets), "size " + sizes[s] + " window " + window);
                    for (int i = 0; i < Packets; i++)
                    {
                        Assert.That(Hex(got[i].Data), Is.EqualTo(Hex(sent[i])), "packet " + i + " at size " + sizes[s] + " window " + window);
                        Assert.That(got[i].Q || got[i].M || got[i].D, Is.False, "no Q, M or D bit was set");
                    }

                    facts.AppendLine("size " + sizes[s] + " window " + window + ": Call Accepted facilities " + Facilities(accepted!) + ", " + Packets + " packets arrived equal");
                    caller.Clear(0, 0);
                    Assert.That(SpinWait.SpinUntil(() => far.Clears().Count == 1, 5000), Is.True, "the far end saw no clear");
                    Assert.That(caller.WaitFor(p => p.Length >= 3 && p[2] == 0x17, 5000), Is.Not.Null, "no Clear Confirmation\n" + caller.Dump());
                }
            }

            TestContext.Progress.WriteLine(facts.ToString());
        }

        /// <summary>
        /// The Q bit, an M-bit chain and the D bit are set in the packets we send, and what arrives at the far end is recorded:
        /// the bytes must be equal in every case. Whether the bits themselves arrive is measured and written to the test output.
        /// </summary>
        [Test]
        public void QBit_MBitChain_DBit_BytesAreEqualAtTheFarEnd_AndTheBitsAreMeasured()
        {
            X25emuEnvironment.RequireReady();
            using X25emuEchoPool far = new X25emuEchoPool(X25emuEnvironment.WslHost!, X25emuEnvironment.CSharpAccessA, FarAddress, 1);
            using RawXotCaller caller = new RawXotCaller(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessPort, 6);
            Assert.That(caller.Call(FarAddress, CallerAddress, 4, 7, 10000), Is.True, caller.Dump());
            byte[] q = Pattern(16, 1);
            byte[] m1 = Pattern(16, 2);
            byte[] m2 = Pattern(16, 3);
            byte[] m3 = Pattern(9, 4);
            byte[] d = Pattern(16, 5);
            caller.SendData(q, q: true);
            caller.SendData(m1, m: true);
            caller.SendData(m2, m: true);
            caller.SendData(m3);
            caller.SendData(d, d: true);
            Assert.That(SpinWait.SpinUntil(() => far.Received().Count >= 5, 10000), Is.True, "only " + far.Received().Count + " of 5 packets arrived\n" + caller.Dump());
            List<(byte[] Data, bool Q, bool M, bool D)> got = far.Received();
            StringBuilder facts = new StringBuilder();
            for (int i = 0; i < got.Count; i++)
            {
                facts.AppendLine("packet " + i + ": " + got[i].Data.Length + " bytes, Q=" + got[i].Q + " M=" + got[i].M + " D=" + got[i].D);
            }

            TestContext.Progress.WriteLine(facts.ToString());
            TestContext.Progress.WriteLine(caller.Dump());
            Assert.That(Hex(got[0].Data), Is.EqualTo(Hex(q)));
            Assert.That(got[0].Q, Is.True, "the Q bit must arrive:\n" + facts);
            Assert.That(Hex(got[1].Data), Is.EqualTo(Hex(m1)));
            Assert.That(Hex(got[2].Data), Is.EqualTo(Hex(m2)));
            Assert.That(Hex(got[3].Data), Is.EqualTo(Hex(m3)));
            Assert.That(got[1].M, Is.True, "the first packet of the chain keeps its M bit:\n" + facts);
            Assert.That(got[2].M, Is.True, "the second packet of the chain keeps its M bit:\n" + facts);
            Assert.That(got[3].M, Is.False, "the last packet of the chain has no M bit:\n" + facts);
            Assert.That(Hex(got[4].Data), Is.EqualTo(Hex(d)));
            caller.Clear(0, 0);
        }

        /// <summary>
        /// Clear Requests with several cause codes and diagnostics: what the far end is told must equal what was sent. Then a
        /// Reset Request: x25emu answers it with a Reset Confirmation, and data sent after the reset (numbers start from 0
        /// again) still arrives.
        /// </summary>
        [Test]
        public void ClearCauses_AndReset_AreCarriedOnAndDecoded()
        {
            X25emuEnvironment.RequireReady();
            StringBuilder facts = new StringBuilder();
            (byte Cause, byte Diagnostic)[] clears = { (0x00, 0x00), (0x00, 0x39), (0x80, 0x00), (0x8D, 0x11) };
            for (int i = 0; i < clears.Length; i++)
            {
                using X25emuEchoPool far = new X25emuEchoPool(X25emuEnvironment.WslHost!, X25emuEnvironment.CSharpAccessA, FarAddress, 1);
                using RawXotCaller caller = new RawXotCaller(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessPort, 7);
                Assert.That(caller.Call(FarAddress, CallerAddress, 7, 2, 10000), Is.True, caller.Dump());
                caller.SendData(Pattern(20, i));
                Assert.That(SpinWait.SpinUntil(() => far.Received().Count >= 1, 5000), Is.True);
                caller.Clear(clears[i].Cause, clears[i].Diagnostic);
                Assert.That(SpinWait.SpinUntil(() => far.Clears().Count == 1, 5000), Is.True, "the far end saw no clear\n" + caller.Dump());
                (byte Cause, byte Diagnostic) seen = far.Clears()[0];
                facts.AppendLine("clear sent cause " + clears[i].Cause.ToString("X2") + " diag " + clears[i].Diagnostic.ToString("X2") + " -> far end saw cause " + seen.Cause.ToString("X2") + " diag " + seen.Diagnostic.ToString("X2"));
                Assert.That(caller.WaitFor(p => p.Length >= 3 && p[2] == 0x17, 5000), Is.Not.Null, "no Clear Confirmation\n" + caller.Dump());
                Assert.That(seen.Cause, Is.EqualTo(clears[i].Cause), facts.ToString());
                Assert.That(seen.Diagnostic, Is.EqualTo(clears[i].Diagnostic), facts.ToString());
            }

            using (X25emuEchoPool far = new X25emuEchoPool(X25emuEnvironment.WslHost!, X25emuEnvironment.CSharpAccessA, FarAddress, 1))
            using (RawXotCaller caller = new RawXotCaller(X25emuEnvironment.WslHost!, X25emuEnvironment.AccessPort, 9))
            {
                Assert.That(caller.Call(FarAddress, CallerAddress, 7, 2, 10000), Is.True, caller.Dump());
                caller.SendData(Pattern(30, 1));
                Assert.That(SpinWait.SpinUntil(() => far.Received().Count >= 1, 5000), Is.True);
                caller.Reset(0x00, 0x00);
                byte[]? confirm = caller.WaitFor(p => p.Length >= 3 && p[2] == 0x1F, 5000);
                facts.AppendLine("reset: " + (confirm == null ? "no Reset Confirmation" : "Reset Confirmation " + Hex(confirm)));
                Assert.That(confirm, Is.Not.Null, "x25emu must answer a Reset Request with a Reset Confirmation\n" + caller.Dump());
                byte[] after = Pattern(30, 2);
                caller.SendData(after);
                Assert.That(SpinWait.SpinUntil(() => far.Received().Count >= 2, 5000), Is.True, "data after the reset did not arrive\n" + caller.Dump());
                Assert.That(Hex(far.Received()[1].Data), Is.EqualTo(Hex(after)));
                caller.Clear(0, 0);
            }

            TestContext.Progress.WriteLine(facts.ToString());
        }

        /// <summary>
        /// x25emu's own tracer decodes what we send: x25trace listens for an XOT connection, we connect to it as the peer and send a
        /// Call Request, data packets with Q and M bits, and a Clear. Its output is read for the decoded fields.
        /// </summary>
        [Test]
        public void X25trace_DecodesTheCallRequest_TheDataAndTheClear_WeSend()
        {
            X25emuEnvironment.RequireReady();
            const int TracePort = 2098;
            using X25emuEnvironment.LinuxProcess trace = new X25emuEnvironment.LinuxProcess(string.Empty, X25emuEnvironment.ToolPath("x25trace"), "-l", TracePort.ToString(), "-v");
            Assert.That(SpinWait.SpinUntil(() =>
            {
                string output = X25emuEnvironment.RunWsl("ss -Htln '( sport = :" + TracePort + " )' | wc -l", 10000, out _);
                return output.Trim() == "1";
            }, 10000), Is.True, "x25trace did not start listening:\n" + trace.Output);
            using (RawXotCaller caller = new RawXotCaller(X25emuEnvironment.WslHost!, TracePort, 3))
            {
                caller.Call("23420001", CallerAddress, 4, 3, 1500);
                caller.SendData(Pattern(16, 1), q: true);
                caller.SendData(Pattern(16, 2), m: true);
                caller.SendData(Pattern(5, 3));
                caller.Clear(0x8D, 0x11);
                Thread.Sleep(1500);
            }

            Thread.Sleep(500);
            string text = trace.Output;
            TestContext.Progress.WriteLine(text);
            Assert.That(text, Does.Contain("23420001"), "x25trace shows the called address:\n" + text);
            Assert.That(text, Does.Contain(CallerAddress), "x25trace shows the calling address:\n" + text);
            Assert.That(text, Does.Contain("42:04:04 43:03:03"), "x25trace shows the packet size 16 and window 3 facilities:\n" + text);
            Assert.That(text, Does.Contain("Q=1 D=0 [16 bytes]"), "the Q bit data packet:\n" + text);
            Assert.That(text, Does.Contain("M=1 Q=0 D=0 [16 bytes]"), "the M bit data packet:\n" + text);
            Assert.That(text, Does.Contain("cause=8D diag=11"), "the clear cause and diagnostic:\n" + text);
        }
    }
}
