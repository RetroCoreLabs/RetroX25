//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using NUnit.Framework;
using RetroIO.Protocols.X25.Models;
using RetroX25.Console.UI;

namespace RetroX25.Console.Tests
{
    /// <summary>
    /// Checks the layer 3 fields the packet window shows for X.25 packets.
    /// </summary>
    /// <remarks>
    /// The cases are octet strings taken from the conformance vectors that the C# stack and the C
    /// emulator (x25emu) both read, so the two implementations are held to the same bytes. The
    /// vector-file test at the end runs every packet in that file, when the file can be found.
    /// </remarks>
    [TestFixture]
    public class X25PacketDecoderTests
    {
        private static PacketInfo Decode(string hex)
        {
            PacketInfo packet = new PacketInfo();
            X25PacketDecoder.Decode(Convert.FromHexString(hex.Replace(" ", string.Empty)), packet);
            return packet;
        }

        /// <summary>
        /// Q and D are bits 8 and 7 of the first octet of a data packet (vector pkt-data-q-and-d), and
        /// the M bit is not set by them.
        /// </summary>
        [Test]
        public void DataPacket_QAndDBitsComeFromTheFirstOctet()
        {
            PacketInfo packet = Decode("D0 01 00 00");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.Data));
            Assert.That(packet.L3Q, Is.True, "Q is bit 8 of octet 1");
            Assert.That(packet.L3D, Is.True, "D is bit 7 of octet 1");
            Assert.That(packet.L3M, Is.False, "M is bit 5 of octet 3, which is clear here");
            Assert.That(packet.L3LCN, Is.EqualTo((ushort)1));
        }

        /// <summary>
        /// M is bit 5 of the third octet (vector pkt-data-ps3-pr5-m1); the first octet has no Q or D.
        /// </summary>
        [Test]
        public void DataPacket_MBitComesFromTheThirdOctet()
        {
            PacketInfo packet = Decode("10 01 B6 AA BB");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.Data));
            Assert.That(packet.L3M, Is.True);
            Assert.That(packet.L3Q, Is.False);
            Assert.That(packet.L3D, Is.False);
        }

        /// <summary>
        /// The logical channel group number is four bits, so group 15 with channel 255 is 4095
        /// (vector pkt-clear-request-not-obtainable). Three bits would give 2047.
        /// </summary>
        [Test]
        public void ClearRequest_GroupNumberIsFourBits_AndCauseAndDiagnosticAreRead()
        {
            PacketInfo packet = Decode("1F FF 13 0D 43");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.ClearRequest));
            Assert.That(packet.L3LCN, Is.EqualTo((ushort)4095));
            Assert.That(packet.L3Cause, Is.EqualTo((byte)0x0D));
            Assert.That(packet.L3Diag, Is.EqualTo((byte)0x43));
        }

        /// <summary>
        /// Bit 8 of a call request is the address (A) bit, not Q, so it must not show as Q.
        /// </summary>
        [Test]
        public void CallRequest_AddressBitIsNotReportedAsQ()
        {
            PacketInfo packet = Decode("90 01 0B 44 12 34 56 78 00");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.CallRequest));
            Assert.That(packet.L3Q, Is.False);
        }

        /// <summary>
        /// Bit 7 of a call request is the D (delivery confirmation) bit.
        /// </summary>
        [Test]
        public void CallRequest_DBitIsReported()
        {
            PacketInfo packet = Decode("50 01 0B 44 12 34 56 78 00");

            Assert.That(packet.L3D, Is.True);
            Assert.That(packet.L3Q, Is.False);
        }

        /// <summary>
        /// Bits 8 and 7 are zero in every packet type other than data, call request and call accepted,
        /// so a stray bit there is not reported as Q or D.
        /// </summary>
        [Test]
        public void ReceiveReady_QAndDAreNotReported()
        {
            PacketInfo packet = Decode("D0 01 A1");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.ReceiveReady));
            Assert.That(packet.L3Q, Is.False);
            Assert.That(packet.L3D, Is.False);
        }

        /// <summary>
        /// The facility octets of a call packet are shown as hex pairs
        /// (vector pkt-call-accepted-with-facilities).
        /// </summary>
        [Test]
        public void CallAccepted_FacilitiesAreShownAsHex()
        {
            PacketInfo packet = Decode("10 01 0F 00 06 42 07 07 43 07 07");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.CallAccepted));
            Assert.That(packet.L3Facilities, Is.EqualTo("42 07 07 43 07 07"));
        }

        /// <summary>
        /// A reset request carries its cause and diagnostic in the fourth and fifth octets
        /// (vector pkt-reset-request-local-procedure-error).
        /// </summary>
        [Test]
        public void ResetRequest_CauseAndDiagnosticAreRead()
        {
            PacketInfo packet = Decode("10 01 1B 05 01");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.ResetRequest));
            Assert.That(packet.L3Cause, Is.EqualTo((byte)5));
            Assert.That(packet.L3Diag, Is.EqualTo((byte)1));
        }

        /// <summary>
        /// A restart request is on channel 0 and carries its cause in the fourth octet
        /// (vector pkt-restart-request).
        /// </summary>
        [Test]
        public void RestartRequest_CauseIsRead()
        {
            PacketInfo packet = Decode("10 00 FB 07 00");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.RestartRequest));
            Assert.That(packet.L3LCN, Is.EqualTo((ushort)0));
            Assert.That(packet.L3Cause, Is.EqualTo((byte)7));
        }

        /// <summary>
        /// An address length that runs past the end of the packet is marked as an error and the reason
        /// is added to the description (vector mal-call-request-address-lengths-exceed-body).
        /// </summary>
        [Test]
        public void CallRequest_AddressLengthsPastTheEnd_AreMarkedAsAnError()
        {
            PacketInfo packet = Decode("10 01 0B FF 12");

            Assert.That(packet.IsError, Is.True);
            Assert.That(packet.Description, Does.Contain("exceed"));
        }

        /// <summary>
        /// Fewer than three octets is not a packet, and the record is left as it was.
        /// </summary>
        [Test]
        public void TwoOctets_LeaveThePacketUnchanged()
        {
            PacketInfo packet = Decode("10 01");

            Assert.That(packet.L3Type, Is.EqualTo(string.Empty));
            Assert.That(packet.L3LCN, Is.Null);
        }

        /// <summary>
        /// A modulo 128 data packet has a different header (X.25 5.3.1) and the C emulator refuses it,
        /// so its M bit is not reported rather than reported from the wrong place.
        /// </summary>
        [Test]
        public void Modulo128DataPacket_MBitIsNotReported()
        {
            PacketInfo packet = Decode("20 01 00 00 01");

            Assert.That(packet.L3Type, Is.EqualTo(X25PacketDecoder.Data));
            Assert.That(packet.L3M, Is.False);
        }

        /// <summary>
        /// A type octet that X.25 does not define is shown in hex.
        /// </summary>
        [Test]
        public void UnknownPacketType_IsShownInHex()
        {
            PacketInfo packet = Decode("10 01 55");

            Assert.That(packet.L3Type, Is.EqualTo("0x55"));
        }

        /// <summary>
        /// Every name is different, so the window can tell the packet types apart by their text.
        /// </summary>
        [Test]
        public void EveryPacketTypeHasItsOwnName()
        {
            HashSet<string> names = new HashSet<string>();
            Array values = Enum.GetValues(typeof(X25PacketType));
            for (int i = 0; i < values.Length; i++)
            {
                X25PacketType type = (X25PacketType)values.GetValue(i)!;
                if (type == X25PacketType.Unknown)
                {
                    continue;
                }

                string name = X25PacketDecoder.NameOf(type, 0);
                Assert.That(name, Does.Not.StartWith("0x"), type + " has no name");
                Assert.That(names.Add(name), Is.True, name + " is used for two packet types");
            }
        }

        /// <summary>
        /// A packet that nothing has tested has no FCS result: the value is unset, not true or false.
        /// </summary>
        [Test]
        public void PacketInfo_FcsResultIsUnsetUntilTheHdlcLayerReportsOne()
        {
            Assert.That(new PacketInfo().L2FCSValid, Is.Null);
        }

        /// <summary>
        /// Runs every packet in the shared conformance vector file through the decoder and compares
        /// the fields the window shows with the vector's expected values.
        /// </summary>
        /// <remarks>
        /// The same file is read by the C emulator's test_vectors, so a packet the two stacks read
        /// differently shows up there or here. The test is skipped, and says so, when the file cannot
        /// be found: set <c>X25_VECTORS</c> to its path, or keep the RetroIO repository beside this one.
        /// </remarks>
        [Test]
        public void SharedVectors_DecodeToTheExpectedFields()
        {
            string? path = VectorFile.Find();
            if (path == null)
            {
                Assert.Ignore("x25-vectors.json not found. Set X25_VECTORS, or put the RetroIO repository beside this one.");
                return;
            }

            VectorFile.Packet[] packets = VectorFile.LoadPackets(path);
            Assert.That(packets.Length, Is.GreaterThan(0), "the vector file has no packets");

            for (int i = 0; i < packets.Length; i++)
            {
                VectorFile.Packet vector = packets[i];
                PacketInfo packet = Decode(vector.Hex);
                string id = vector.Id;

                Assert.That(packet.L3Type, Is.EqualTo(VectorFile.ExpectedName(vector.Type)), id + ": type");
                Assert.That(packet.L3LCN, Is.EqualTo((ushort)vector.Lcn), id + ": logical channel");
                Assert.That(packet.IsError, Is.False, id + ": a valid vector must not be marked as an error");

                if (vector.Q.HasValue)
                {
                    Assert.That(packet.L3Q, Is.EqualTo(vector.Q.Value), id + ": Q");
                }

                if (vector.D.HasValue)
                {
                    Assert.That(packet.L3D, Is.EqualTo(vector.D.Value), id + ": D");
                }

                if (vector.M.HasValue)
                {
                    Assert.That(packet.L3M, Is.EqualTo(vector.M.Value), id + ": M");
                }

                if (vector.Cause.HasValue)
                {
                    Assert.That(packet.L3Cause, Is.EqualTo((byte)vector.Cause.Value), id + ": cause");
                }

                if (vector.Diag.HasValue && vector.Type != "Diagnostic")
                {
                    Assert.That(packet.L3Diag, Is.EqualTo((byte)vector.Diag.Value), id + ": diagnostic");
                }

                if (vector.FacilitiesHex != null)
                {
                    Assert.That(packet.L3Facilities, Is.EqualTo(vector.FacilitiesHex), id + ": facilities");
                }
            }
        }
    }
}
