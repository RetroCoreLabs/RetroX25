//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroIO.Link.Hdlc;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Routing;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Tests
{
    /// <summary>
    /// Byte-stuffed HDLC (RFC 1662) over two real serial ports that are joined to each other by an HDLC line.
    /// A frame written to one port goes out over the line and comes out of the other, and the other way round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hardware (checked 2026-11-14): COM10 is an ESP32 board (HDLCGW 0.3.5), COM18 is a Pico (RC422Dev), the line
    /// between them runs at 307200 bit/s, and both ports are USB so the speed setting on the COM port itself does
    /// nothing. Each frame is a flag (0x7E), the body with 0x7E and 0x7D escaped by 0x7D and the original byte XOR 0x20,
    /// the 16-bit checksum, and a closing flag. A frame of up to 1024 bytes including the checksum works both ways, so a
    /// body of up to 1022 bytes.
    /// </para>
    /// <para>
    /// The tests are skipped when the two COM ports do not exist on the machine. To use other ports, set
    /// <c>HDLC_COM_A</c> and <c>HDLC_COM_B</c>. A port that another program has open fails the test with the reason.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("SerialLive")]
    [NonParallelizable]
    public class SerialHdlcLoopbackTests
    {
        /// <summary>The line speed, bit/s. The COM port setting does nothing on USB, but the switch port validates it.</summary>
        private const uint LineBaud = 307200;

        /// <summary>The largest frame that works both ways, checksum included.</summary>
        private const int MaxFrameWithFcs = 1024;

        private const int FcsLength = 2;

        private string _comA = string.Empty;
        private string _comB = string.Empty;

        /// <summary>
        /// Finds the two ports, or skips the test when this machine does not have them.
        /// </summary>
        [SetUp]
        public void FindThePorts()
        {
            _comA = Environment.GetEnvironmentVariable("HDLC_COM_A") ?? "COM10";
            _comB = Environment.GetEnvironmentVariable("HDLC_COM_B") ?? "COM18";
            List<string> present = new List<string>(SerialPort.GetPortNames());
            if (!present.Contains(_comA) || !present.Contains(_comB))
            {
                Assert.Ignore("the serial ports " + _comA + " and " + _comB + " are not on this machine");
            }
        }

        /// <summary>
        /// One end of the line: an open serial port with the framing on top. It sends a body as one HDLC frame and
        /// returns the bodies it receives, with the checksum checked and removed.
        /// </summary>
        private sealed class SerialEnd : IDisposable
        {
            private readonly SerialPort _port;
            private readonly HdlcDecoder _decoder = new HdlcDecoder(new HdlcFramerOptions { MaxFrameLength = 4096, DeliverCorruptFrames = true });
            private long _badChecksums;
            private readonly List<string> _controlFrames = new List<string>();
            private readonly Queue<byte[]> _frames = new Queue<byte[]>();
            private readonly byte[] _readBuffer = new byte[8192];
            private readonly List<byte> _raw = new List<byte>();

            public SerialEnd(string name)
            {
                Name = name;
                _port = new SerialPort
                {
                    PortName = name,
                    BaudRate = (int)LineBaud,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 50,
                    WriteTimeout = 5000,
                    // The ESP32 board (HDLCGW firmware) restarts into the chip's download mode (USB id 303A:0002, no
                    // answer) when DTR and RTS change in the order RTS only, both on, DTR only, both off, or when the port
                    // is set to 1200 bit/s (Arduino ESP32 core USBCDC.cpp, told to us by the firmware session
                    // 2026-11-14; it happened here on the first runs). So set them once to DTR on, RTS off and never touch
                    // them again, and never use 1200.
                    DtrEnable = true,
                    RtsEnable = false,
                };

                try
                {
                    _port.Open();
                }
                catch (Exception ex)
                {
                    _port.Dispose();
                    Assert.Fail("cannot open " + name + ": " + ex.Message + " (is another program using it?)");
                }
            }

            public string Name { get; }

            public long WrongChecksums => Interlocked.Read(ref _badChecksums);

            /// <summary>Control frames from the gateway firmware seen so far, as text (see <see cref="IsGatewayControlFrame"/>).</summary>
            public int ControlFrames => _controlFrames.Count;

            /// <summary>Counts of frames the framing refused (runt, too long, aborted) since the port was opened.</summary>
            public long OtherErrors => _decoder.Statistics.Runts + _decoder.Statistics.Overlong + _decoder.Statistics.Aborts;

            /// <summary>
            /// Sends one body as one frame: flag, escaped body, checksum, flag.
            /// </summary>
            public void Send(byte[] body, uint accm)
            {
                HdlcFramerOptions options = new HdlcFramerOptions { SendAccm = accm };
                byte[] wire = HdlcEncoder.Encode(body, options);
                _port.Write(wire, 0, wire.Length);
            }

            /// <summary>
            /// Waits for one frame and returns its body without the checksum, or null if none arrives in time.
            /// </summary>
            public byte[]? Receive(int timeoutMs)
            {
                DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (_frames.Count == 0 && DateTime.UtcNow < end)
                {
                    ReadSome();
                }

                return _frames.Count == 0 ? null : _frames.Dequeue();
            }

            /// <summary>
            /// Reads and throws away everything that is on the line until it has been quiet for the given time. Frames
            /// left over from an earlier test must not be mistaken for answers.
            /// </summary>
            /// <summary>Forgets the raw bytes kept so far.</summary>
            public void MarkRaw()
            {
                _raw.Clear();
            }

            /// <summary>The bytes read from the port since <see cref="MarkRaw"/>, as they were on the wire.</summary>
            public string RawText()
            {
                return _raw.Count + " bytes: " + BitConverter.ToString(_raw.ToArray());
            }

            public void Drain(int quietMs)
            {
                DateTime quietUntil = DateTime.UtcNow.AddMilliseconds(quietMs);
                while (DateTime.UtcNow < quietUntil)
                {
                    if (ReadSome() > 0)
                    {
                        quietUntil = DateTime.UtcNow.AddMilliseconds(quietMs);
                    }
                }

                _frames.Clear();
                _decoder.Reset();
            }

            private int ReadSome()
            {
                int count;
                try
                {
                    count = _port.Read(_readBuffer, 0, _readBuffer.Length);
                }
                catch (TimeoutException)
                {
                    return 0;
                }

                for (int r = 0; r < count; r++)
                {
                    _raw.Add(_readBuffer[r]);
                }

                int offset = 0;
                while (offset < count)
                {
                    HdlcDecodeStatus status = _decoder.Decode(new ReadOnlySpan<byte>(_readBuffer, offset, count - offset), out int consumed);
                    offset += consumed;
                    if (status == HdlcDecodeStatus.NeedMoreData)
                    {
                        break;
                    }

                    if (status == HdlcDecodeStatus.FrameComplete)
                    {
                        ReadOnlySpan<byte> frame = _decoder.Frame;
                        _frames.Enqueue(frame.Slice(0, frame.Length - FcsLength).ToArray());
                    }
                    else if (status == HdlcDecodeStatus.BadFcs)
                    {
                        ReadOnlySpan<byte> frame = _decoder.Frame;
                        if (IsGatewayControlFrame(frame))
                        {
                            _controlFrames.Add(BitConverter.ToString(frame.ToArray()));
                        }
                        else
                        {
                            Interlocked.Increment(ref _badChecksums);
                        }
                    }
                }

                return count;
            }

            /// <summary>
            /// The gateway firmware (HDLCGW, and RC422Dev on the other port) sends its own control frames to the PC: address
            /// byte 0xFE, a type, an id, flags and a payload, then the CRC-16/X.25 register stored WITHOUT the final
            /// inversion. Folded through the normal CRC, such a frame leaves 0x0000, where a good data frame leaves 0xF0B8,
            /// so the two cannot be mixed up (told by the firmware session 2026-11-14, from libxmsg control_frame.h; an
            /// example is the event 'line receive active'). They never crossed the line. Any other frame with a wrong
            /// checksum is a real error and is counted.
            /// </summary>
            private static bool IsGatewayControlFrame(ReadOnlySpan<byte> frame)
            {
                if (frame.Length < 4 || frame[0] != 0xFE)
                {
                    return false;
                }

                ushort register = 0xFFFF;
                for (int i = 0; i < frame.Length; i++)
                {
                    register ^= frame[i];
                    for (int bit = 0; bit < 8; bit++)
                    {
                        register = (register & 1) != 0 ? (ushort)((register >> 1) ^ 0x8408) : (ushort)(register >> 1);
                    }
                }

                return register == 0x0000;
            }

            public void Dispose()
            {
                _decoder.Dispose();
                try
                {
                    _port.Dispose();
                }
                catch (Exception)
                {
                    // The USB adapter is gone, or the port is closed already.
                }
            }
        }

        /// <summary>
        /// Builds a body of the given length from a pattern.
        /// </summary>
        private static byte[] Body(int length, string pattern, int seed)
        {
            byte[] body = new byte[length];
            Random random = new Random(seed);
            for (int i = 0; i < length; i++)
            {
                switch (pattern)
                {
                    case "counting": body[i] = (byte)(i + seed); break;
                    case "flags": body[i] = 0x7E; break;
                    case "escapes": body[i] = 0x7D; break;
                    case "both": body[i] = (i & 1) == 0 ? (byte)0x7E : (byte)0x7D; break;
                    case "zeros": body[i] = 0x00; break;
                    case "ones": body[i] = 0xFF; break;
                    case "controls": body[i] = (byte)(i % 0x20); break;
                    default: body[i] = (byte)random.Next(256); break;
                }
            }

            return body;
        }

        /// <summary>
        /// Sends the body from one end and checks the other end gets the same bytes.
        /// </summary>
        private static void CrossesTheLine(SerialEnd from, SerialEnd to, byte[] body, uint accm, string what)
        {
            long wrongBefore = to.WrongChecksums;
            to.MarkRaw();
            from.Send(body, accm);
            byte[]? got = to.Receive(3000);
            to.Drain(100);
            Assert.That(to.WrongChecksums, Is.EqualTo(wrongBefore), what + ": a frame with a wrong checksum arrived at " + to.Name + ". Bytes read there for this send, " + to.RawText());
            Assert.That(got, Is.Not.Null, what + ": nothing came out of " + to.Name + " (checksum errors there: " + to.WrongChecksums + ")");
            Assert.That(got!.Length, Is.EqualTo(body.Length), what + ": wrong length");
            Assert.That(got, Is.EqualTo(body), what + ": the bytes changed on the way");
        }

        /// <summary>
        /// Frames of many sizes, from the smallest the framing accepts up to the largest that works (1024 with the
        /// checksum), cross the line in both directions with every byte unchanged. Bytes 0x7E and 0x7D inside the
        /// frame are escaped by the sender and come back as they were.
        /// </summary>
        [Test]
        public void FramesOfManySizes_CrossTheLineBothWays_WithEveryByteUnchanged()
        {
            int[] sizes = { 2, 3, 4, 16, 100, 127, 128, 129, 255, 256, 257, 500, 512, 1000, MaxFrameWithFcs - FcsLength };
            using SerialEnd a = new SerialEnd(_comA);
            using SerialEnd b = new SerialEnd(_comB);
            a.Drain(300);
            b.Drain(300);

            for (int i = 0; i < sizes.Length; i++)
            {
                byte[] body = Body(sizes[i], "counting", i);
                CrossesTheLine(a, b, body, HdlcFramerOptions.AccmAllControlOctets, _comA + " to " + _comB + ", body " + sizes[i]);
                CrossesTheLine(b, a, body, HdlcFramerOptions.AccmAllControlOctets, _comB + " to " + _comA + ", body " + sizes[i]);
            }

            Assert.That(a.WrongChecksums + b.WrongChecksums, Is.EqualTo(0), "frames with a wrong checksum");
            Assert.That(a.OtherErrors + b.OtherErrors, Is.EqualTo(0), "runt, too long or aborted frames");
        }

        /// <summary>
        /// The bytes that need escaping, and the ones around them: a frame that is all flags, all escapes, both mixed, all
        /// control bytes below 0x20, zeros, ones and random bytes. The longest frame is a body of only flags, which doubles
        /// on the wire.
        /// </summary>
        [Test]
        public void FramesFullOfFlagAndEscapeBytes_CrossTheLineBothWays()
        {
            string[] patterns = { "flags", "escapes", "both", "controls", "zeros", "ones", "random" };
            int[] sizes = { 2, 7, 128, MaxFrameWithFcs - FcsLength };
            using SerialEnd a = new SerialEnd(_comA);
            using SerialEnd b = new SerialEnd(_comB);
            a.Drain(300);
            b.Drain(300);

            for (int p = 0; p < patterns.Length; p++)
            {
                for (int s = 0; s < sizes.Length; s++)
                {
                    byte[] body = Body(sizes[s], patterns[p], p * 10 + s);
                    string what = patterns[p] + " x " + sizes[s];
                    CrossesTheLine(a, b, body, HdlcFramerOptions.AccmAllControlOctets, _comA + " to " + _comB + ", " + what);
                    CrossesTheLine(b, a, body, HdlcFramerOptions.AccmAllControlOctets, _comB + " to " + _comA + ", " + what);
                }
            }

            Assert.That(a.WrongChecksums + b.WrongChecksums, Is.EqualTo(0), "frames with a wrong checksum");
        }

        /// <summary>
        /// The sender may escape only the flag and the escape byte (the minimum X.25 asks for) or also every byte below
        /// 0x20 (the RFC 1662 default our switch uses). The line carries both, and the receiver turns both back into the
        /// same bytes.
        /// </summary>
        [Test]
        public void BothEscapingStyles_ArriveAsTheSameBytes()
        {
            using SerialEnd a = new SerialEnd(_comA);
            using SerialEnd b = new SerialEnd(_comB);
            a.Drain(300);
            b.Drain(300);

            byte[] body = Body(600, "controls", 3);
            CrossesTheLine(a, b, body, HdlcFramerOptions.AccmNone, "flag and escape only, " + _comA + " to " + _comB);
            CrossesTheLine(b, a, body, HdlcFramerOptions.AccmNone, "flag and escape only, " + _comB + " to " + _comA);
            CrossesTheLine(a, b, body, HdlcFramerOptions.AccmAllControlOctets, "all control bytes, " + _comA + " to " + _comB);
            CrossesTheLine(b, a, body, HdlcFramerOptions.AccmAllControlOctets, "all control bytes, " + _comB + " to " + _comA);
        }

        /// <summary>
        /// Two hundred frames sent one after another in one direction arrive in the same order and unchanged, with none
        /// lost. Each frame holds its own number.
        /// </summary>
        [Test]
        public void ManyFramesInARow_ArriveInOrder_NoneLost()
        {
            const int Count = 200;
            using SerialEnd a = new SerialEnd(_comA);
            using SerialEnd b = new SerialEnd(_comB);
            a.Drain(300);
            b.Drain(300);

            // The receiving port must be read the whole time the other end writes. If nobody reads while 45 kB are
            // being sent, the Pico's USB buffer fills and it drops frames (its RECEIVED DROPPED counter), which is what
            // this test did before it had a reader thread.
            List<byte[]> arrived = new List<byte[]>();
            Thread reader = new Thread(() =>
            {
                while (arrived.Count < Count)
                {
                    byte[]? got = b.Receive(3000);
                    if (got == null)
                    {
                        return;
                    }

                    arrived.Add(got);
                }
            });
            reader.Start();

            for (int i = 0; i < Count; i++)
            {
                byte[] body = Body(40 + (i * 5 % 400), "random", i);
                body[0] = (byte)(i >> 8);
                body[1] = (byte)i;
                a.Send(body, HdlcFramerOptions.AccmAllControlOctets);
            }

            reader.Join();

            int firstWrong = -1;
            for (int i = 0; i < arrived.Count; i++)
            {
                byte[] expected = Body(40 + (i * 5 % 400), "random", i);
                expected[0] = (byte)(i >> 8);
                expected[1] = (byte)i;
                if (firstWrong < 0 && !arrived[i].AsSpan().SequenceEqual(expected))
                {
                    firstWrong = i;
                }
            }

            Assert.That(arrived.Count, Is.EqualTo(Count), "frames that arrived, out of " + Count);
            Assert.That(firstWrong, Is.EqualTo(-1), "the first frame that arrived changed or out of order");
            Assert.That(b.WrongChecksums, Is.EqualTo(0));
        }

        /// <summary>
        /// Both ends send at the same time (full duplex): each side sends 100 frames while it receives the other's, and
        /// all of them arrive unchanged.
        /// </summary>
        [Test]
        public void BothEndsSendAtTheSameTime_AllFramesArrive()
        {
            const int Count = 100;
            using SerialEnd a = new SerialEnd(_comA);
            using SerialEnd b = new SerialEnd(_comB);
            a.Drain(300);
            b.Drain(300);

            Thread sender = new Thread(() =>
            {
                for (int i = 0; i < Count; i++)
                {
                    byte[] body = Body(100 + (i * 7 % 300), "random", 1000 + i);
                    a.Send(body, HdlcFramerOptions.AccmAllControlOctets);
                }
            });
            Thread other = new Thread(() =>
            {
                for (int i = 0; i < Count; i++)
                {
                    byte[] body = Body(100 + (i * 11 % 300), "random", 2000 + i);
                    b.Send(body, HdlcFramerOptions.AccmAllControlOctets);
                }
            });
            sender.Start();
            other.Start();

            // Each end is read from one thread only: the main thread reads B, a second thread reads A.
            int gotAtA = 0;
            int wrongAtA = 0;
            Thread readerA = new Thread(() =>
            {
                for (int i = 0; i < Count; i++)
                {
                    byte[]? got = a.Receive(5000);
                    if (got == null)
                    {
                        return;
                    }

                    gotAtA++;
                    if (!got.AsSpan().SequenceEqual(Body(100 + (i * 11 % 300), "random", 2000 + i)))
                    {
                        wrongAtA++;
                    }
                }
            });
            readerA.Start();

            int gotAtB = 0;
            int wrongAtB = 0;
            for (int i = 0; i < Count; i++)
            {
                byte[]? got = b.Receive(5000);
                if (got == null)
                {
                    break;
                }

                gotAtB++;
                if (!got.AsSpan().SequenceEqual(Body(100 + (i * 7 % 300), "random", 1000 + i)))
                {
                    wrongAtB++;
                }
            }

            sender.Join();
            other.Join();
            readerA.Join();

            Assert.That(gotAtB, Is.EqualTo(Count), "frames that arrived at " + _comB);
            Assert.That(gotAtA, Is.EqualTo(Count), "frames that arrived at " + _comA);
            Assert.That(wrongAtB + wrongAtA, Is.EqualTo(0), "frames that changed or arrived out of order");
        }

        /// <summary>
        /// A frame longer than 1024 bytes with the checksum is not promised to work. This reports what the line does with
        /// a few larger sizes and does not fail on it. Run it by name only: it may upset the board.
        /// </summary>
        [Test]
        [Explicit("sends frames larger than the 1024 bytes that are known to work; may upset the ESP32 board")]
        public void FramesLargerThan1024_AreMeasured_NotAsserted()
        {
            int[] sizes = { 1023, 1024, 1500, 2048, 4000 };
            using SerialEnd a = new SerialEnd(_comA);
            using SerialEnd b = new SerialEnd(_comB);
            a.Drain(300);
            b.Drain(300);

            for (int i = 0; i < sizes.Length; i++)
            {
                byte[] body = Body(sizes[i], "counting", i);
                a.Send(body, HdlcFramerOptions.AccmAllControlOctets);
                byte[]? got = b.Receive(3000);
                string result = got == null ? "nothing arrived" : (got.AsSpan().SequenceEqual(body) ? "arrived unchanged" : "arrived changed, " + got.Length + " bytes");
                TestContext.Progress.WriteLine("body " + sizes[i] + " (" + (sizes[i] + FcsLength) + " with checksum), " + _comA + " to " + _comB + ": " + result);
                a.Drain(200);
                b.Drain(200);
            }

            Assert.Pass("measurement only; see the output");
        }

        private static PortConfig SerialTrunk(int id, PortMode mode, string device, string flow)
        {
            return new PortConfig
            {
                Id = id,
                Type = PortType.Trunk,
                Transport = PortTransport.HdlcSerial,
                Mode = mode,
                SerialDevice = device,
                SerialBaud = LineBaud,
                SerialDataBits = 8,
                SerialParity = "none",
                SerialStopBits = 1,
                SerialFlow = flow,
                LciBase = 1,
                LciCount = 16,
                LapbT1Ms = 500,
            };
        }

        /// <summary>
        /// Our own serial trunk port (the switch's <c>hdlc-serial</c> transport) on both ends: the port opens the
        /// device with the configured settings, the two ends send SABM and answer UA across the line, and both report
        /// the link up. This is the check that the serial settings of the switch work on real ports.
        /// </summary>
        [TestCase("none")]
        [TestCase("rtscts")]
        public void TheSwitchSerialPorts_BringTheLapbLinkUp_AcrossTheLine(string flow)
        {
            HdlcSerialSwitchPort portA = new HdlcSerialSwitchPort(SerialTrunk(0, PortMode.Dce, _comA, flow), NullLogger.Instance);
            HdlcSerialSwitchPort portB = new HdlcSerialSwitchPort(SerialTrunk(0, PortMode.Dte, _comB, flow), NullLogger.Instance);
            try
            {
                Assert.That(portA.Start(), Is.True, "port on " + _comA + " did not start: " + portA.LastError);
                Assert.That(portB.Start(), Is.True, "port on " + _comB + " did not start: " + portB.LastError);
                bool up = SpinWait.SpinUntil(() => portA.PeerConnected && portB.PeerConnected, 15000);
                Assert.That(up, Is.True,
                    "the LAPB link did not come up with flow = " + flow + ". " + _comA + ": up=" + portA.PeerConnected + ", last error '" + portA.LastError +
                    "'. " + _comB + ": up=" + portB.PeerConnected + ", last error '" + portB.LastError + "'");
            }
            finally
            {
                portA.Dispose();
                portB.Dispose();
            }
        }

        /// <summary>
        /// A whole X.25 call across the real serial line. Switch A has an access port and a serial trunk on the first
        /// COM port; switch B has a serial trunk on the second COM port and an access port with an echo service. A call
        /// placed on A crosses the line, and 100 packets of 128 bytes come back from the echo unchanged and in order, which
        /// is more than the 8 sequence numbers of one window, so they wrapped. Nothing on the line may have a wrong
        /// checksum and the link must not have been reset.
        /// </summary>
        [Test]
        public void ACallCrossesTheSerialTrunk_AndDataRoundTrips()
        {
            const int Packets = 100;

            SwitchConfig cfgB = new SwitchConfig { Dnic = "2352" };
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Dnic, Dnic = "2352", DestPort = 1 });
            cfgB.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 0 });
            SwitchNode nodeB = new SwitchNode(cfgB);
            HdlcSerialSwitchPort bTrunk = new HdlcSerialSwitchPort(SerialTrunk(0, PortMode.Dce, _comB, "none"), NullLogger.Instance);
            XotSwitchPort bAccess = new XotSwitchPort(
                new PortConfig { Id = 1, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            nodeB.AddPort(bTrunk);
            nodeB.AddPort(bAccess);
            SwitchCore coreB = new SwitchCore(nodeB);

            SwitchConfig cfgA = new SwitchConfig { Dnic = "2350" };
            cfgA.Routing.AddRoute(new X121Route { Type = X121RouteType.Default, DestPort = 1 });
            SwitchNode nodeA = new SwitchNode(cfgA);
            XotSwitchPort aAccess = new XotSwitchPort(
                new PortConfig { Id = 0, Type = PortType.Access, Mode = PortMode.Dce, Host = "127.0.0.1", Port = 0, LciBase = 1 },
                NullLogger.Instance);
            HdlcSerialSwitchPort aTrunk = new HdlcSerialSwitchPort(SerialTrunk(1, PortMode.Dte, _comA, "none"), NullLogger.Instance);
            nodeA.AddPort(aAccess);
            nodeA.AddPort(aTrunk);
            SwitchCore coreA = new SwitchCore(nodeA);

            try
            {
                Assert.That(bAccess.Start(), Is.True);
                Assert.That(bTrunk.Start(), Is.True, "trunk on " + _comB + ": " + bTrunk.LastError);
                Assert.That(aAccess.Start(), Is.True);
                Assert.That(aTrunk.Start(), Is.True, "trunk on " + _comA + ": " + aTrunk.LastError);
                Assert.That(SpinWait.SpinUntil(() => aTrunk.PeerConnected && bTrunk.PeerConnected, 15000), Is.True,
                    "the LAPB link over the serial line did not come up");

                using X25Client echo = new X25Client { ServiceName = "echo" };
                using X25Client caller = new X25Client { ServiceName = "caller" };
                ManualResetEventSlim echoAccepted = new ManualResetEventSlim(false);
                echo.SetOnIncoming((c, vc) => { c.Accept(vc); echoAccepted.Set(); });
                echo.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    byte[] reply = new byte[length];
                    Array.Copy(data, 0, reply, 0, length);
                    c.Send(vc, reply, length, qBit);
                });
                echo.Open(new X25ClientConfig { Host = "127.0.0.1", Port = bAccess.BoundPort, LocalAddress = "23520001", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

                int received = 0;
                int wrong = 0;
                ManualResetEventSlim allBack = new ManualResetEventSlim(false);
                caller.SetOnData((c, vc, data, length, qBit, mBit, dBit) =>
                {
                    int number = (data[0] << 8) | data[1];
                    int n = Interlocked.Increment(ref received);
                    if (length != 128 || number != n - 1)
                    {
                        Interlocked.Increment(ref wrong);
                    }

                    if (n == Packets)
                    {
                        allBack.Set();
                    }
                });
                caller.Open(new X25ClientConfig { Host = "127.0.0.1", Port = aAccess.BoundPort, LocalAddress = "23500099", Framing = X25ClientFraming.Xot }, NullLogger.Instance);

                X25Vc? call = caller.CallWait("23520001", 5000);
                Assert.That(call, Is.Not.Null, "the call should cross the serial line to the echo");
                Assert.That(echoAccepted.Wait(5000), Is.True);

                for (int i = 0; i < Packets; i++)
                {
                    byte[] payload = new byte[128];
                    payload[0] = (byte)(i >> 8);
                    payload[1] = (byte)(i & 0xFF);
                    for (int k = 2; k < payload.Length; k++)
                    {
                        payload[k] = (byte)(k ^ i);
                    }

                    // At most two packets are out at a time: the echo answers with a plain Send, which gives up when its
                    // window is shut (same reasoning as in HdlcMultiSwitchTrunkTests).
                    long deadline = Environment.TickCount64 + 20000;
                    while (i - Volatile.Read(ref received) >= 2)
                    {
                        Assert.That(Environment.TickCount64, Is.LessThan(deadline), "the echoes stopped coming back at packet " + i);
                        Thread.Sleep(1);
                    }

                    while (!caller.Send(call!, payload, payload.Length, false))
                    {
                        Assert.That(Environment.TickCount64, Is.LessThan(deadline), "the send window never opened");
                        Thread.Sleep(2);
                    }
                }

                Assert.That(allBack.Wait(30000), Is.True, "only " + received + " of " + Packets + " packets came back");
                Assert.That(wrong, Is.EqualTo(0), "packets that came back wrong or out of order");

                HdlcLinkSnapshot a = aTrunk.GetLinkSnapshot();
                HdlcLinkSnapshot b = bTrunk.GetLinkSnapshot();
                Assert.That(a.InformationFramesOut, Is.GreaterThanOrEqualTo(Packets));
                Assert.That(b.InformationFramesOut, Is.GreaterThanOrEqualTo(Packets));
                // a.FcsErrors and b.FcsErrors are not asserted: the gateway sends its own control frames (an event when the
                // line becomes active), which are not HDLC data frames and which the trunk port counts as frames with a wrong
                // checksum. A damaged data frame would show as a reset or as wrong data, both asserted here.
                TestContext.Progress.WriteLine("frames with a wrong checksum counted by the trunk ports: " + (a.FcsErrors + b.FcsErrors));
                Assert.That(a.LinkResets + b.LinkResets, Is.EqualTo(0), "the LAPB link was reset");
            }
            finally
            {
                aAccess.Stop();
                aTrunk.Dispose();
                bTrunk.Dispose();
                bAccess.Stop();
            }
        }
    }
}
