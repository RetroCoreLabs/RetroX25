//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Client;
using RetroIO.Protocols.X25.Host;
using RetroIO.Protocols.X25.Transport;

namespace RetroX25.Host
{
    /// <summary>
    /// Runs every machine of a machine list in one process: each machine a <see cref="ScriptedHost"/> (a JavaScript
    /// program) or a <see cref="DescribedHost"/> (a host folder), each switch line
    /// one XOT connection, and a call given to the machine whose NUA equals the called address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Threads. The X.25 client delivers calls, data and clears on its own threads. A <see cref="ScriptedHost"/> is not
    /// thread safe, so all of that is posted to one worker thread that owns every machine and also gives them the time
    /// (every 50 ms, for the scripts' timers). What a script sends goes to one sender thread, which waits for the
    /// send window as x25echo does: the client's receive thread delivers the acknowledgements that open the window, so
    /// it must never wait itself.
    /// </para>
    /// <para>
    /// Several machines may share one switch line. The switch hands an arriving call only to a connection the host
    /// opened (an XOT listening port with KeepConnection), so one process keeps one connection per line and sorts the
    /// calls by called address. A call to a number no machine on the line has is cleared with cause 0 (DTE
    /// originated) and diagnostic 67 (invalid called address). Cause 13 "not obtainable" is a network cause a DTE
    /// cannot give (X.25 Table 5-7: a DTE's clear carries cause 0, or 128-255 for its own codes); the switch turns
    /// any other value into 0. The diagnostic says why.
    /// </para>
    /// <para>
    /// A switch that is not there yet is dialled again (2 s, doubling to 30 s, <see cref="XotSpareRedial"/>), as
    /// x25echo does: a DTE started while its link is down keeps trying to bring it up (X.25 2.4.4.1).
    /// </para>
    /// </remarks>
    internal sealed class HostRunner : IDisposable
    {
        /// <summary>X.25 Annex E diagnostic 67 "invalid called address".</summary>
        private const byte DiagnosticInvalidCalled = 67;

        /// <summary>Clear cause 0 "DTE originated" for a call the machine ends.</summary>
        private const byte CauseDteOriginated = 0;

        /// <summary>How often the scripts are given the time.</summary>
        private const int TickMs = 50;

        private readonly ILogger _log;
        private readonly List<Machine> _machines = new List<Machine>();
        private readonly List<HostLine> _lines = new List<HostLine>();
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        private readonly ConcurrentQueue<OutItem> _out = new ConcurrentQueue<OutItem>();
        private readonly ConcurrentDictionary<int, CallRef> _calls = new ConcurrentDictionary<int, CallRef>();
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private int _nextSession;
        private volatile bool _stopped;
        private Thread? _worker;
        private Thread? _sender;

        /// <summary>
        /// Loads every machine's script. A script that does not load stops everything here, naming the machine: nothing
        /// starts half set up.
        /// </summary>
        /// <param name="entries">The machines (from <see cref="MachineList"/>).</param>
        /// <param name="log">Where the host logs; every line about a machine starts with its hostname.</param>
        /// <exception cref="InvalidOperationException">A script did not load.</exception>
        public HostRunner(List<MachineEntry> entries, ILogger log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            Dictionary<string, HostLine> lines = new Dictionary<string, HostLine>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                MachineEntry e = entries[i];
                Machine m = new Machine(this, e);
                if (e.HostFolder.Length > 0)
                {
                    try
                    {
                        // The list's hostname and nua make the folder (a TYPE) one particular machine.
                        HostDescription d = HostDescription.Load(e.HostFolder, e.Hostname.Length > 0 ? e.Hostname : null, e.Nua);
                        e.Hostname = d.Hostname;
                        m.Host = new DescribedHost(d, m);
                    }
                    catch (FormatException ex)
                    {
                        throw new InvalidOperationException("[" + e.Section + "] (type = " + e.Type + "): the host folder does not load: " + ex.Message, ex);
                    }
                }
                else
                {
                    ScriptedHostSettings settings = new ScriptedHostSettings();
                    foreach (KeyValuePair<string, string> pair in e.Settings)
                    {
                        settings.Values[pair.Key] = pair.Value;
                    }

                    settings.Values["hostname"] = e.Hostname;
                    settings.Values["nua"] = e.Nua;
                    try
                    {
                        m.Host = new ScriptedHost(File.ReadAllText(e.ScriptPath), settings, m);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(e.Hostname + " ([" + e.Section + "], " + e.ScriptPath + "): the script does not load: " + ex.Message, ex);
                    }
                }

                if (!lines.TryGetValue(e.Line, out HostLine? line))
                {
                    line = new HostLine(this, e.SwitchHost, e.SwitchPort, e.Nua);
                    lines.Add(e.Line, line);
                    _lines.Add(line);
                }

                line.Machines.Add(e.Nua, m);
                _machines.Add(m);
            }
        }

        /// <summary>Calls open now, on every machine.</summary>
        public int CallCount => _calls.Count;

        /// <summary>True when every line has its connection to the switch.</summary>
        public bool AllLinesUp
        {
            get
            {
                for (int i = 0; i < _lines.Count; i++)
                {
                    if (_lines[i].Client == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Starts the machines (their <c>onStart</c>), the worker, the sender and one dialler per line.</summary>
        public void Start()
        {
            DateTime now = DateTime.Now;
            for (int i = 0; i < _machines.Count; i++)
            {
                _machines[i].Host!.Start(now);
                _log.LogInformation("{Host}: started, nua {Nua}, line {Line}", _machines[i].Entry.Hostname, _machines[i].Entry.Nua, _machines[i].Entry.Line);
            }

            _worker = new Thread(WorkLoop) { IsBackground = true, Name = "x25host-machines" };
            _worker.Start();
            _sender = new Thread(SendLoop) { IsBackground = true, Name = "x25host-sender" };
            _sender.Start();
            for (int i = 0; i < _lines.Count; i++)
            {
                _lines[i].Start();
            }
        }

        /// <summary>Blocks until <see cref="Stop"/>.</summary>
        public void Wait()
        {
            _stop.Wait();
        }

        /// <summary>Stops every line, the worker and the sender.</summary>
        public void Stop()
        {
            _stopped = true;
            _stop.Set();
            for (int i = 0; i < _lines.Count; i++)
            {
                _lines[i].Stop();
            }

            _work.CompleteAdding();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Stop();
            _worker?.Join(2000);
            _sender?.Join(2000);
            for (int i = 0; i < _lines.Count; i++)
            {
                _lines[i].Dispose();
            }

            _stop.Dispose();
            _work.Dispose();
        }

        private void Post(Action action)
        {
            try
            {
                _work.Add(action);
            }
            catch (InvalidOperationException)
            {
                // Stopping: the worker takes no more work.
            }
        }

        private void WorkLoop()
        {
            Stopwatch tick = Stopwatch.StartNew();
            while (!_stopped)
            {
                try
                {
                    if (_work.TryTake(out Action? action, TickMs))
                    {
                        action();
                    }

                    if (tick.ElapsedMilliseconds >= TickMs)
                    {
                        tick.Restart();
                        DateTime now = DateTime.Now;
                        for (int i = 0; i < _machines.Count; i++)
                        {
                            _machines[i].Host!.Tick(now);
                        }
                    }
                }
                catch (InvalidOperationException) when (_work.IsAddingCompleted)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // ScriptedHost already clears a call whose script fails; anything reaching here is ours. Logged, and
                    // the other machines go on.
                    _log.LogError(ex, "x25host: a machine event failed");
                }
            }
        }

        // ------------------------------------------------------------------ calls from the switch (client threads)

        private void OnIncoming(HostLine line, X25Client client, X25Vc vc)
        {
            if (!line.Machines.TryGetValue(vc.CalledAddress, out Machine? m))
            {
                _log.LogWarning("x25host: call from {Calling} to {Called} on line {Line}: no machine has that number, cleared",
                    vc.CallingAddress, vc.CalledAddress, line.Key);
                client.Clear(vc, CauseDteOriginated, DiagnosticInvalidCalled);
                return;
            }

            // The machine is asked on its own thread whether it takes the call, and is given the call in the same step,
            // so two calls arriving together cannot both take the last free session. This thread waits for the answer:
            // the call is accepted or cleared from inside this callback.
            int session = Interlocked.Increment(ref _nextSession);
            string? calling = vc.CallingAddress.Length > 0 ? vc.CallingAddress : null;
            byte[] cud = vc.CallUserData;
            CallRef call = new CallRef(m, line, vc);
            HostAdmission answer = HostAdmission.Busy;

            // Not disposed: after a time-out the worker may still set it. It holds no kernel handle unless waited on long.
            ManualResetEventSlim done = new ManualResetEventSlim(false);
            {
                bool timedOut = false;
                Post(() =>
                {
                    answer = m.Host!.Admit(cud);
                    if (answer == HostAdmission.Accept)
                    {
                        _calls[session] = call;
                        m.Host.Connect(session, string.Empty, calling);
                        if (Volatile.Read(ref timedOut))
                        {
                            // The switch thread gave up waiting and cleared the call: the machine forgets it again.
                            call.Gone = true;
                            _calls.TryRemove(session, out _);
                            m.Host.Disconnect(session);
                        }
                    }

                    done.Set();
                });

                if (!done.Wait(2000))
                {
                    Volatile.Write(ref timedOut, true);
                    call.Gone = true;
                    _log.LogWarning("{Host}: call from {Calling} not answered by the machine in 2 s, cleared", m.Entry.Hostname, vc.CallingAddress);
                    client.Clear(vc, CauseDteOriginated, 0);
                    return;
                }
            }

            if (answer != HostAdmission.Accept)
            {
                call.Gone = true;
                // A DTE clears with cause 0 (X.25 Table 5-7); "Number busy" is a network cause it cannot give. The log
                // says why.
                _log.LogInformation("{Host}: call from {Calling} refused: {Why}", m.Entry.Hostname, vc.CallingAddress,
                    answer == HostAdmission.Busy ? "every session is in use" : "not a PAD call (call user data does not start 01 00 00 00)");
                client.Clear(vc, CauseDteOriginated, 0);
                return;
            }

            vc.UserData = session;
            client.Accept(vc);
            _log.LogInformation("{Host}: call {Session} from {Calling}", m.Entry.Hostname, session, vc.CallingAddress);
        }

        private void OnData(X25Client client, X25Vc vc, byte[] data, int length, bool qBit)
        {
            if (qBit || vc.UserData is not int session || !_calls.TryGetValue(session, out CallRef? call))
            {
                // A Q-bit packet is an X.29 message from the PAD (a parameter answer), not typed text: not for the
                // machine.
                return;
            }

            byte[] copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            Post(() => call.Machine.Host!.Input(session, copy));
        }

        private void OnClear(X25Vc vc, byte cause, byte diagnostic)
        {
            if (vc.UserData is not int session || !_calls.TryRemove(session, out CallRef? call))
            {
                return;
            }

            call.Gone = true;

            _log.LogInformation("{Host}: call {Session} cleared (cause {Cause} diag {Diag})", call.Machine.Entry.Hostname, session, cause, diagnostic);
            Post(() => call.Machine.Host!.Disconnect(session));
        }

        // ------------------------------------------------------------------ what the scripts do (worker thread)

        private void Send(int session, ReadOnlySpan<byte> data)
        {
            if (!_calls.TryGetValue(session, out CallRef? call) || data.Length == 0)
            {
                return;
            }

            _out.Enqueue(new OutItem(session, call, data.ToArray(), false, false));
        }

        /// <summary>
        /// An X.29 Set of X.3 parameter 2 (echo) to the caller's PAD: message code 0x02, then the pair (2, 1 or 0),
        /// in a Q-bit data packet (X.29 (11/88) 3.5.4 and 4.4.1).
        /// </summary>
        private void SetEcho(int session, bool echo)
        {
            if (!_calls.TryGetValue(session, out CallRef? call))
            {
                return;
            }

            _out.Enqueue(new OutItem(session, call, new byte[] { 0x02, 0x02, echo ? (byte)1 : (byte)0 }, false, true));
        }

        private void ClearCall(int session)
        {
            if (!_calls.TryRemove(session, out CallRef? call))
            {
                return;
            }

            // After what was sent before it (the goodbye), so it goes through the same queue.
            _out.Enqueue(new OutItem(session, call, Array.Empty<byte>(), true, false));
        }

        private void SendLoop()
        {
            int idle = 0;
            int offset = 0;
            while (!_stopped)
            {
                if (!_out.TryPeek(out OutItem? next))
                {
                    Thread.Sleep(1);
                    continue;
                }

                X25Client? client = next.Call.Line.Client;
                X25Vc vc = next.Call.Vc;
                if (client == null || next.Call.Gone)
                {
                    // The line, or the call (cleared from the far end, or never accepted), is gone: nobody to send to.
                    _out.TryDequeue(out _);
                    offset = 0;
                    continue;
                }

                if (!vc.Connected)
                {
                    // The machine answers a call as soon as it is given it (a banner), which is before the client has
                    // sent the Call Accepted and marked the circuit connected (X25Client.OnLayerIncomingCall). The
                    // bytes wait for that, in order.
                    Thread.Sleep(1);
                    continue;
                }

                if (next.Close)
                {
                    // X.25 4.5: data sent just before a clear may be lost, so the last data is let through first.
                    client.WaitForAcknowledgement(vc, 2000);
                    client.Clear(vc, CauseDteOriginated, 0);
                    _out.TryDequeue(out _);
                    Machine closed = next.Call.Machine;
                    _log.LogInformation("{Host}: call {Session} cleared by the machine", closed.Entry.Hostname, next.Session);
                    continue;
                }

                // One packet at a time, at most the call's packet size (a longer block is refused for ever).
                int size = Math.Max(1, client.GetPacketSizeOut(vc));
                int piece = Math.Min(size, next.Data.Length - offset);
                byte[] part = new byte[piece];
                Buffer.BlockCopy(next.Data, offset, part, 0, piece);
                if (client.Send(vc, part, piece, qBit: next.Q))
                {
                    idle = 0;
                    offset += piece;
                    int session = next.Session;
                    Machine m = next.Call.Machine;
                    Post(() => m.Host!.OutputDrained(session, piece));
                    if (offset >= next.Data.Length)
                    {
                        _out.TryDequeue(out _);
                        offset = 0;
                    }
                }
                else if (++idle < 2000)
                {
                    // Window full; an acknowledgement usually comes within microseconds (see x25echo).
                    Thread.Yield();
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        /// <summary>A call: which machine has it, on which line, on which circuit.</summary>
        private sealed class CallRef
        {
            public CallRef(Machine machine, HostLine line, X25Vc vc)
            {
                Machine = machine;
                Line = line;
                Vc = vc;
            }

            public Machine Machine { get; }

            public HostLine Line { get; }

            public X25Vc Vc { get; }

            /// <summary>Cleared from the far end, or never accepted: what is still queued for it is dropped.</summary>
            public volatile bool Gone;
        }

        /// <summary>Bytes for a call (Q: an X.29 message to the PAD), or (Close) the call's clear after what went before.</summary>
        private sealed record OutItem(int Session, CallRef Call, byte[] Data, bool Close, bool Q);

        /// <summary>One machine: its entry, its host (a script or a host folder), and the output back to the runner.</summary>
        private sealed class Machine : IHostOutput, IHostPadControl
        {
            private readonly HostRunner _runner;

            public Machine(HostRunner runner, MachineEntry entry)
            {
                _runner = runner;
                Entry = entry;
            }

            public MachineEntry Entry { get; }

            public IHostMachine? Host { get; set; }

            public void SetEcho(int session, bool echo)
            {
                _runner.SetEcho(session, echo);
            }

            public void Send(int session, ReadOnlySpan<byte> data)
            {
                _runner.Send(session, data);
            }

            public void Clear(int session)
            {
                _runner.ClearCall(session);
            }

            public void Log(string text)
            {
                _runner._log.LogInformation("{Host}: {Text}", Entry.Hostname, text);
            }
        }

        /// <summary>One switch line: a connection to one XOT port of the switch, and the machines on it.</summary>
        private sealed class HostLine : IDisposable
        {
            private readonly HostRunner _runner;
            private readonly X25ClientConfig _config;
            private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
            private volatile bool _stopped;
            private Thread? _thread;

            public HostLine(HostRunner runner, string host, ushort port, string localAddress)
            {
                _runner = runner;
                Key = host + ":" + port;
                _config = new X25ClientConfig
                {
                    Host = host,
                    Port = port,
                    Framing = X25ClientFraming.Xot,
                    IsDte = true,
                    PacketSize = 128,
                    WindowSize = 7,
                    NumVcs = 2048,
                    LocalAddress = localAddress,
                };
            }

            public string Key { get; }

            public Dictionary<string, Machine> Machines { get; } = new Dictionary<string, Machine>(StringComparer.Ordinal);

            public volatile X25Client? Client;

            public void Start()
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "x25host-line-" + Key };
                _thread.Start();
            }

            public void Stop()
            {
                _stopped = true;
                _stop.Set();
                Client?.Close();
            }

            public void Dispose()
            {
                Stop();
                _thread?.Join(2000);
                Client?.Dispose();
                _stop.Dispose();
            }

            private void Run()
            {
                XotSpareRedial redial = new XotSpareRedial(_config.SpareRedialFirstMs, _config.SpareRedialMostMs);
                long failures = 0;
                while (!_stopped)
                {
                    X25Client client = new X25Client { ServiceName = "x25host" };
                    client.SetOnIncoming((c, vc) => _runner.OnIncoming(this, c, vc));
                    client.SetOnData((c, vc, data, length, q, m, d) => _runner.OnData(c, vc, data, length, q));
                    client.SetOnClear((c, vc, cause, diag) => _runner.OnClear(vc, cause, diag));
                    try
                    {
                        client.Open(_config, _runner._log);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // X25Client.Open's answer when the switch cannot be reached: dial again (see the class remarks).
                        client.Dispose();
                        failures++;
                        int wait = redial.Next();
                        _runner._log.LogWarning("x25host: waiting for the switch at {Line} ({Reason}); dial {Count} failed, dialling again in {Wait} ms",
                            Key, ex.Message, failures, wait);
                        _stop.Wait(wait);
                        continue;
                    }

                    Client = client;
                    _runner._log.LogInformation("x25host: line {Line} connected", Key);
                    // The client dials its spare connection again itself after a switch restart; Run returns on Close.
                    client.Run();
                    return;
                }
            }
        }
    }
}
