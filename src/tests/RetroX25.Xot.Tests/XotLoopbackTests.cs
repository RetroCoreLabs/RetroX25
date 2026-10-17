//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Net.Sockets;
using NUnit.Framework;
using RetroX25.Xot;

namespace RetroX25.Xot.Tests
{
    /// <summary>
    /// Runs the XOT test client and server against each other, and against a bare socket, over the
    /// loopback address.
    /// </summary>
    /// <remarks>
    /// RFC 1613 says a version other than 0, or a length that is not a legal X.25 packet length, closes
    /// the TCP connection. The tests with a bare socket send exactly those headers, and a header cut
    /// short, and check that the server closes the connection instead of reading on from the middle of
    /// a packet.
    /// </remarks>
    [TestFixture]
    public class XotLoopbackTests
    {
        private const int WaitMs = 5000;

        private XotTestServer _server = null!;
        private List<string> _serverStatus = null!;
        private List<byte[]> _serverReceived = null!;
        private int _clientConnected;
        private int _clientDisconnected;

        [SetUp]
        public void StartServer()
        {
            _serverStatus = new List<string>();
            _serverReceived = new List<byte[]>();
            _clientConnected = 0;
            _clientDisconnected = 0;

            _server = new XotTestServer(0);
            _server.StatusChanged += (s, text) => { lock (_serverStatus) { _serverStatus.Add(text); } };
            _server.DataReceived += (s, e) => { lock (_serverReceived) { _serverReceived.Add(e.Data); } };
            _server.ClientConnected += (s, endpoint) => Interlocked.Increment(ref _clientConnected);
            _server.ClientDisconnected += (s, e) => Interlocked.Increment(ref _clientDisconnected);

            Assert.That(_server.Start(), Is.True, "the server must start on a free port");
        }

        [TearDown]
        public void StopServer()
        {
            _server.Dispose();
        }

        private bool WaitFor(Func<bool> condition)
        {
            return SpinWait.SpinUntil(condition, WaitMs);
        }

        private TcpClient ConnectRaw()
        {
            TcpClient raw = new TcpClient { NoDelay = true };
            raw.Connect("127.0.0.1", _server.ListenPort);
            Assert.That(WaitFor(() => Volatile.Read(ref _clientConnected) == 1), Is.True, "the server must accept the socket");
            return raw;
        }

        private int ReceivedCount()
        {
            lock (_serverReceived)
            {
                return _serverReceived.Count;
            }
        }

        private bool ServerSaid(string fragment)
        {
            lock (_serverStatus)
            {
                for (int i = 0; i < _serverStatus.Count; i++)
                {
                    if (_serverStatus[i].Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A packet the client sends reaches the server whole, with the XOT header in front, and the
        /// client reports the same bytes as sent.
        /// </summary>
        [Test]
        public void PacketSentByTheClient_ArrivesAtTheServerUnchanged()
        {
            using XotTestClient client = new XotTestClient("127.0.0.1", _server.ListenPort);
            byte[]? sent = null;
            client.DataSent += (s, e) => sent = e.Data;

            Assert.That(client.Connect(5000), Is.True);
            Assert.That(client.SendPacket(PredefinedPackets.CallRequest), Is.True);

            Assert.That(WaitFor(() => ReceivedCount() == 1), Is.True, "the server must receive the packet");
            byte[] expected = XotPacketFormatter.CreateXotPacket(PredefinedPackets.CallRequest);
            Assert.That(_serverReceived[0], Is.EqualTo(expected));
            Assert.That(sent, Is.EqualTo(expected));
        }

        /// <summary>
        /// A packet the server sends reaches the client.
        /// </summary>
        [Test]
        public void PacketSentByTheServer_ArrivesAtTheClient()
        {
            using XotTestClient client = new XotTestClient("127.0.0.1", _server.ListenPort);
            byte[]? received = null;
            client.DataReceived += (s, e) => received = e.Data;

            Assert.That(client.Connect(5000), Is.True);
            Assert.That(WaitFor(() => _server.HasClient), Is.True);
            Assert.That(_server.SendPacket(PredefinedPackets.ClearRequest), Is.True);

            Assert.That(WaitFor(() => received != null), Is.True, "the client must receive the packet");
            Assert.That(received, Is.EqualTo(XotPacketFormatter.CreateXotPacket(PredefinedPackets.ClearRequest)));
        }

        /// <summary>
        /// Closing the client raises its <c>Disconnected</c> event once, not once for the call and again
        /// when the receive loop notices, and the server sees the client leave once.
        /// </summary>
        [Test]
        public void ClientDisconnect_RaisesDisconnectedOnce_AndTheServerSeesItOnce()
        {
            using XotTestClient client = new XotTestClient("127.0.0.1", _server.ListenPort);
            int disconnected = 0;
            client.Disconnected += (s, e) => Interlocked.Increment(ref disconnected);

            Assert.That(client.Connect(5000), Is.True);
            Assert.That(WaitFor(() => _server.HasClient), Is.True);

            client.Disconnect();

            Assert.That(WaitFor(() => Volatile.Read(ref _clientDisconnected) == 1), Is.True, "the server must see the client leave");
            Assert.That(client.IsConnected, Is.False);
            client.Disconnect();
            Assert.That(Volatile.Read(ref disconnected), Is.EqualTo(1), "Disconnected must be raised once");
            Assert.That(Volatile.Read(ref _clientDisconnected), Is.EqualTo(1), "ClientDisconnected must be raised once");
        }

        /// <summary>
        /// When the server closes the client's connection, the client reports it once and is no longer connected.
        /// </summary>
        [Test]
        public void ServerClosingTheConnection_IsReportedByTheClientOnce()
        {
            using XotTestClient client = new XotTestClient("127.0.0.1", _server.ListenPort);
            int disconnected = 0;
            client.Disconnected += (s, e) => Interlocked.Increment(ref disconnected);

            Assert.That(client.Connect(5000), Is.True);
            Assert.That(WaitFor(() => _server.HasClient), Is.True);

            _server.Stop();

            Assert.That(WaitFor(() => Volatile.Read(ref disconnected) == 1), Is.True, "the client must notice the server closing");
            Assert.That(client.IsConnected, Is.False);
        }

        /// <summary>
        /// A header with a version other than 0 closes the connection and delivers nothing (RFC 1613).
        /// </summary>
        [Test]
        public void BadVersion_ClosesTheConnection_AndDeliversNothing()
        {
            using TcpClient raw = ConnectRaw();
            byte[] bytes = Convert.FromHexString("00010003" + "10010B");
            raw.GetStream().Write(bytes, 0, bytes.Length);

            Assert.That(WaitFor(() => Volatile.Read(ref _clientDisconnected) == 1), Is.True, "the server must close the connection");
            Assert.That(ReceivedCount(), Is.EqualTo(0), "a packet behind a bad version must not be delivered");
            Assert.That(ServerSaid("version"), Is.True, "the reason must be reported");
        }

        /// <summary>
        /// A length below the three octets of the shortest X.25 packet closes the connection (RFC 1613:
        /// the length must be a legal X.25 packet length).
        /// </summary>
        [Test]
        public void IllegalLength_ClosesTheConnection()
        {
            using TcpClient raw = ConnectRaw();
            byte[] bytes = Convert.FromHexString("00000002" + "1001");
            raw.GetStream().Write(bytes, 0, bytes.Length);

            Assert.That(WaitFor(() => Volatile.Read(ref _clientDisconnected) == 1), Is.True);
            Assert.That(ReceivedCount(), Is.EqualTo(0));
            Assert.That(ServerSaid("length"), Is.True);
        }

        /// <summary>
        /// The next header is not read from inside a packet whose header was refused: the bytes of the
        /// refused packet never reach the receive event as a second, false packet.
        /// </summary>
        [Test]
        public void BytesBehindARefusedHeader_AreNeverReadAsAnotherPacket()
        {
            using TcpClient raw = ConnectRaw();

            // A bad version, then, as its body, bytes that would be a valid XOT packet if read as a header.
            byte[] bytes = Convert.FromHexString("00010007" + "00000003" + "10010B");
            raw.GetStream().Write(bytes, 0, bytes.Length);

            Assert.That(WaitFor(() => Volatile.Read(ref _clientDisconnected) == 1), Is.True);
            Assert.That(ReceivedCount(), Is.EqualTo(0));
        }

        /// <summary>
        /// A header cut short by the connection closing is not a packet and ends the connection.
        /// </summary>
        [Test]
        public void HeaderCutShortByTheConnectionClosing_DeliversNothing_AndEndsTheConnection()
        {
            TcpClient raw = ConnectRaw();
            byte[] twoOctets = Convert.FromHexString("0000");
            raw.GetStream().Write(twoOctets, 0, twoOctets.Length);
            raw.Close();

            Assert.That(WaitFor(() => Volatile.Read(ref _clientDisconnected) == 1), Is.True, "the server must notice the close");
            Assert.That(ReceivedCount(), Is.EqualTo(0));
            Assert.That(_server.HasClient, Is.False);
        }

        /// <summary>
        /// A packet that arrives in two TCP writes is delivered once, whole.
        /// </summary>
        [Test]
        public void PacketSplitAcrossTwoWrites_IsDeliveredOnce()
        {
            using TcpClient raw = ConnectRaw();
            byte[] packet = XotPacketFormatter.CreateXotPacket(PredefinedPackets.CallRequest);
            NetworkStream stream = raw.GetStream();

            stream.Write(packet, 0, 5);
            stream.Flush();
            Assert.That(ReceivedCount(), Is.EqualTo(0), "half a packet is not a packet");
            stream.Write(packet, 5, packet.Length - 5);
            stream.Flush();

            Assert.That(WaitFor(() => ReceivedCount() == 1), Is.True);
            Assert.That(_serverReceived[0], Is.EqualTo(packet));
        }

        /// <summary>
        /// A packet shorter than the three octets every X.25 packet has is refused when sending, and the
        /// connection stays open.
        /// </summary>
        [Test]
        public void SendingATooShortPacket_IsRefused_AndTheConnectionStaysOpen()
        {
            using XotTestClient client = new XotTestClient("127.0.0.1", _server.ListenPort);
            string? lastStatus = null;
            client.StatusChanged += (s, text) => lastStatus = text;

            Assert.That(client.Connect(5000), Is.True);

            Assert.That(client.SendPacket(new byte[] { 0x10, 0x01 }), Is.False);
            Assert.That(lastStatus, Does.StartWith("Send failed"));
            Assert.That(client.IsConnected, Is.True, "a refused packet must not end the connection");
            Assert.That(ReceivedCount(), Is.EqualTo(0));
        }
    }
}
