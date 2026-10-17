//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using NUnit.Framework;
using RetroIO.Protocols.X25.Models;
using RetroX25.Xot;

namespace RetroX25.Xot.Tests
{
    /// <summary>
    /// Checks that the ready-made test packets are well-formed X.25 and are named correctly.
    /// </summary>
    /// <remarks>
    /// The packets used to be byte arrays with an extra octet after the first two, so the packet type
    /// landed in the fourth octet and a call request went out as a receive ready. Each test here reads a
    /// packet back with the same parser the conformance vectors use.
    /// </remarks>
    [TestFixture]
    public class PredefinedPacketsTests
    {
        /// <summary>
        /// Each predefined packet is parsed as the type it is meant to be, on logical channel 1.
        /// </summary>
        [TestCase("CallRequest", X25PacketType.CallRequest)]
        [TestCase("CallAccept", X25PacketType.CallAccepted)]
        [TestCase("ClearRequest", X25PacketType.ClearRequest)]
        [TestCase("ClearConfirmation", X25PacketType.ClearConfirmation)]
        [TestCase("DataPacket", X25PacketType.Data)]
        [TestCase("ReceiverReady", X25PacketType.RR)]
        [TestCase("ReceiverNotReady", X25PacketType.RNR)]
        [TestCase("ResetRequest", X25PacketType.ResetRequest)]
        [TestCase("ResetConfirmation", X25PacketType.ResetConfirmation)]
        public void EveryPredefinedPacket_ParsesAsItsOwnType(string property, X25PacketType expected)
        {
            byte[] packet = (byte[])typeof(PredefinedPackets).GetProperty(property)!.GetValue(null)!;

            X25PacketInfo info = X25PacketParser.ParsePacket(packet);

            Assert.That(info.PacketType, Is.EqualTo(expected), property);
            Assert.That(info.LCN, Is.EqualTo(PredefinedPackets.LogicalChannel), property);
            Assert.That(info.ParseError, Is.Null, property);
        }

        /// <summary>
        /// The call request carries the called and calling addresses its constants name, and no facilities.
        /// </summary>
        [Test]
        public void CallRequest_CarriesTheNamedAddresses()
        {
            X25PacketInfo info = X25PacketParser.ParsePacket(PredefinedPackets.CallRequest);

            Assert.That(info.CalledAddress, Is.EqualTo(PredefinedPackets.CalledAddress));
            Assert.That(info.CallingAddress, Is.EqualTo(PredefinedPackets.CallingAddress));
            Assert.That(info.Facilities, Is.Empty);
        }

        /// <summary>
        /// The data packet carries its text after a three-octet header.
        /// </summary>
        [Test]
        public void DataPacket_CarriesItsTextAfterThreeOctets()
        {
            byte[] packet = PredefinedPackets.DataPacket;
            X25PacketInfo info = X25PacketParser.ParsePacket(packet);

            Assert.That(System.Text.Encoding.ASCII.GetString(info.UserData), Is.EqualTo(PredefinedPackets.DataText));
            Assert.That(packet.Length, Is.EqualTo(3 + PredefinedPackets.DataText.Length));
        }

        /// <summary>
        /// Every predefined packet is a legal XOT payload: at least three octets and no more than 4096.
        /// </summary>
        [Test]
        public void EveryPredefinedPacket_IsALegalXotLength()
        {
            byte[][] all =
            {
                PredefinedPackets.CallRequest, PredefinedPackets.CallAccept, PredefinedPackets.ClearRequest,
                PredefinedPackets.ClearConfirmation, PredefinedPackets.DataPacket, PredefinedPackets.ReceiverReady,
                PredefinedPackets.ReceiverNotReady, PredefinedPackets.ResetRequest, PredefinedPackets.ResetConfirmation
            };

            for (int i = 0; i < all.Length; i++)
            {
                Assert.That(all[i].Length, Is.InRange(3, 4096), "packet " + i);
            }
        }

        /// <summary>
        /// The namer reads the type from the third octet, so a correctly formed call request, which is
        /// what a person types into the hex dialog, is named a call request.
        /// </summary>
        [Test]
        public void GetPacketName_NamesACorrectlyFormedCallRequest()
        {
            byte[] packet = Convert.FromHexString("10010B4412345678" + "00");

            Assert.That(PredefinedPackets.GetPacketName(packet), Does.StartWith("Call Request"));
        }

        /// <summary>
        /// A data packet's name carries its sequence numbers (vector pkt-data-ps3-pr5-m1).
        /// </summary>
        [Test]
        public void GetPacketName_ShowsTheSequenceNumbersOfADataPacket()
        {
            byte[] packet = Convert.FromHexString("1001B6AABB");

            Assert.That(PredefinedPackets.GetPacketName(packet), Does.Contain("P(S)=3").And.Contain("P(R)=5"));
        }

        /// <summary>
        /// A receive ready's name carries its P(R) (vector pkt-rr-pr5).
        /// </summary>
        [Test]
        public void GetPacketName_ShowsThePrOfAReceiveReady()
        {
            Assert.That(PredefinedPackets.GetPacketName(Convert.FromHexString("1001A1")), Does.Contain("P(R)=5"));
        }

        /// <summary>
        /// A type octet that X.25 does not define is shown in hex, and a packet with fewer than three
        /// octets is called invalid.
        /// </summary>
        [Test]
        public void GetPacketName_HandlesUnknownAndTooShort()
        {
            Assert.That(PredefinedPackets.GetPacketName(Convert.FromHexString("100155")), Is.EqualTo("Unknown (0x55)"));
            Assert.That(PredefinedPackets.GetPacketName(Convert.FromHexString("1001")), Is.EqualTo("Invalid Packet"));
        }
    }
}
