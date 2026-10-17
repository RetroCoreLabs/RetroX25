//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Generic;
using System.Text;
using RetroIO.Protocols.X25.Switch;
using RetroIO.Protocols.X25.Switch.Management;
using RetroIO.Protocols.X25.Switch.Model;
using RetroIO.Protocols.X25.Switch.Ports;

namespace RetroX25.Switch
{
    /// <summary>
    /// Hosts the X.25-based management surface: it accepts calls to the reserved management address, runs a
    /// login session on each, buffers circuit data into lines, and pushes asynchronous events to monitoring
    /// sessions.
    /// </summary>
    public sealed class ManagementHost
    {
        private sealed class Session
        {
            public ISwitchPort Port = null!;
            public ushort Lci;
            public MgmtSession Mgmt = null!;
            public readonly StringBuilder Buffer = new StringBuilder();
        }

        private readonly SwitchNode _node;
        private readonly SwitchCli _cli;
        private readonly SwitchApiProcessor _api;
        private readonly object _lock = new object();
        private readonly Dictionary<long, Session> _sessions = new Dictionary<long, Session>();

        /// <summary>
        /// Creates the management host.
        /// </summary>
        /// <param name="node">
        /// The switch state.
        /// </param>
        /// <param name="cli">
        /// The text command processor.
        /// </param>
        /// <param name="api">
        /// The machine API processor.
        /// </param>
        public ManagementHost(SwitchNode node, SwitchCli cli, SwitchApiProcessor api)
        {
            _node = node;
            _cli = cli;
            _api = api;
        }

        private static long Key(int port, ushort lci)
        {
            return ((long)port << 16) | lci;
        }

        /// <summary>
        /// Handles an incoming call to the management address: accepts it and starts a login session.
        /// </summary>
        /// <param name="port">
        /// The port the call arrived on.
        /// </param>
        /// <param name="lci">
        /// The logical channel of the call.
        /// </param>
        /// <param name="calledAddress">
        /// The dialled address (the management address).
        /// </param>
        /// <param name="callingAddress">
        /// The originating address.
        /// </param>
        /// <param name="callUserData">
        /// Call user data (unused).
        /// </param>
        public void OnManagementCall(ISwitchPort port, ushort lci, string calledAddress, string callingAddress, byte[] callUserData)
        {
            port.AcceptCall(lci);

            MgmtSession mgmt = new MgmtSession(_cli, _api, _node.Config.Users, _node.Config.Name, _node.Config.Dnic);
            Session session = new Session { Port = port, Lci = lci, Mgmt = mgmt };
            lock (_lock)
            {
                _sessions[Key(port.Id, lci)] = session;
            }

            // Parameter set (qualifier bit) then the login banner (plain text).
            port.SendData(lci, MgmtSession.ParameterSet, qBit: true, mBit: false, dBit: false);
            SendText(port, lci, mgmt.Banner());
        }

        /// <summary>
        /// Feeds circuit data belonging to a management session, dispatching completed lines.
        /// </summary>
        /// <param name="port">
        /// The port the data arrived on.
        /// </param>
        /// <param name="lci">
        /// The logical channel.
        /// </param>
        /// <param name="data">
        /// The data bytes.
        /// </param>
        /// <param name="length">
        /// Number of valid bytes.
        /// </param>
        /// <param name="qBit">
        /// Whether the data carried the qualifier bit.
        /// </param>
        /// <param name="mBit">
        /// More-data bit (unused).
        /// </param>
        /// <param name="dBit">
        /// Delivery-confirmation bit (unused).
        /// </param>
        /// <returns>
        /// True if the data belonged to a management session and was consumed.
        /// </returns>
        public bool OnPortData(ISwitchPort port, ushort lci, byte[] data, int length, bool qBit, bool mBit, bool dBit)
        {
            Session? session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(Key(port.Id, lci), out session))
                {
                    return false;
                }
            }

            for (int i = 0; i < length; i++)
            {
                char c = (char)data[i];
                if (c == '\r' || c == '\n')
                {
                    if (session.Buffer.Length > 0)
                    {
                        string line = session.Buffer.ToString();
                        session.Buffer.Clear();
                        DispatchLine(session, line, qBit);
                    }
                }
                else
                {
                    session.Buffer.Append(c);
                }
            }

            return true;
        }

        private void DispatchLine(Session session, string line, bool qBit)
        {
            MgmtResult result = session.Mgmt.FeedLine(line, qBit);
            if (result.Output.Length > 0)
            {
                SendData(session.Port, session.Lci, result.Output, result.QBit);
            }

            if (result.Clear)
            {
                session.Port.ClearCall(session.Lci, SwitchCore.CauseDteOriginated, 0);
                lock (_lock)
                {
                    _sessions.Remove(Key(session.Port.Id, session.Lci));
                }
            }
        }

        /// <summary>
        /// Pushes a call-setup event to monitoring and subscribed sessions.
        /// </summary>
        /// <param name="callingAddress">
        /// Originating address.
        /// </param>
        /// <param name="calledAddress">
        /// Destination address.
        /// </param>
        /// <param name="inPort">
        /// Ingress port.
        /// </param>
        /// <param name="outPort">
        /// Egress port.
        /// </param>
        public void OnCallSetup(string callingAddress, string calledAddress, int inPort, int outPort)
        {
            PushEvent(ManagementEvents.CallSetup(callingAddress, calledAddress, inPort, outPort));
        }

        /// <summary>
        /// Pushes a call-clear event to monitoring and subscribed sessions.
        /// </summary>
        /// <param name="port">
        /// Port the clear was observed on.
        /// </param>
        /// <param name="lci">
        /// Logical channel that cleared.
        /// </param>
        /// <param name="cause">
        /// Clearing cause.
        /// </param>
        /// <param name="diagnostic">
        /// Diagnostic code.
        /// </param>
        public void OnCallClear(int port, ushort lci, byte cause, byte diagnostic)
        {
            PushEvent(ManagementEvents.CallClear(port, lci, cause, diagnostic));
        }

        private void PushEvent(string eventLine)
        {
            List<Session> targets = new List<Session>();
            lock (_lock)
            {
                Session[] all = new Session[_sessions.Count];
                _sessions.Values.CopyTo(all, 0);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].Mgmt.Subscribed || all[i].Mgmt.Monitoring)
                    {
                        targets.Add(all[i]);
                    }
                }
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Session s = targets[i];
                // Subscribed sessions receive events with the qualifier bit; monitor sessions as plain text.
                SendData(s.Port, s.Lci, eventLine, s.Mgmt.Subscribed);
            }
        }

        private static void SendText(ISwitchPort port, ushort lci, string text)
        {
            SendData(port, lci, text, false);
        }

        private static void SendData(ISwitchPort port, ushort lci, string text, bool qBit)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            port.SendData(lci, bytes, qBit, mBit: false, dBit: false);
        }
    }
}
