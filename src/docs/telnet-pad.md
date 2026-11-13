# Using a PAD from a normal terminal program (telnet)

RetroX25.Desktop can open a TCP port where any telnet program (the `telnet` command, PuTTY, a terminal
emulator) gets a **PAD** of its own. A PAD is the part of an X.25 network that lets a person type at a
remote host: you type commands such as `CALL`, and after the call is connected your typing goes to the host
and the host's answer comes back. The Desktop app has the same PAD in its PAD Client window; this port makes
it reachable from outside the app.

It is **off by default**. When it is on, it listens on `127.0.0.1` (only this computer) port `2323`.

## Quick start

1. Open **Configuration** (Ctrl+,) and the **Telnet PAD** tab.
2. Tick **Enable the telnet PAD**. Leave the port at 2323, or type another one (1 to 65535).
3. Press **Apply**. The engine restarts, and the main window shows
   `Telnet PAD: listening on 127.0.0.1:2323, 0 sessions`.
4. In a terminal on the same computer:

   ```
   telnet 127.0.0.1 2323
   ```

5. You see a short banner and the prompt `PAD> `. Type a call to the app's own built-in TAD server (the
   address is the `X25.LocalMachineId` of the configuration; the sample files use `23430200`):

   ```
   PAD> CALL 23430200
   ```

   The PAD answers `COM` when the call is connected. Anything you type now goes to the host. The built-in
   TAD server answers each line with an `Echo:` line and an `Analysis:` line, then its `TAD> ` prompt.

To start with the port already on, start the app on a configuration file that has it switched on:

```
RetroX25.exe docs\config-samples\x25config-telnet-pad.json
```

## The settings

| Setting | What it does | Default |
|---|---|---|
| Enable | Listen for telnet connections or not | off |
| Port | The TCP port, 1 to 65535 | 2323 |
| Bind address | The IP address to listen on. `127.0.0.1` lets only programs on this computer connect. `0.0.0.0` lets other computers connect | 127.0.0.1 |
| Maximum sessions | How many telnet connections may be open at once. A further connection is told "PAD busy" and closed | 8 |

In a configuration file they are a `TelnetPad` section (the section may be left out; then the telnet PAD
stays off):

```json
"TelnetPad": {
  "Enabled": true,
  "Port": 2323,
  "BindAddress": "127.0.0.1",
  "MaxSessions": 8
}
```

**There is no login.** Anyone who can reach the port can place X.25 calls through your engine. Keep the bind
address at `127.0.0.1` unless you want other computers to use it.

Changes take effect when you press **Apply** in the Configuration window: the engine restarts, and every open
telnet session is closed.

## What the main window shows

One line says what the telnet PAD is doing:

| Line | Meaning |
|---|---|
| `Telnet PAD: off` | Switched off. Nothing listens |
| `Telnet PAD: listening on 127.0.0.1:2323, 2 sessions` | Listening, with two connections open |
| `Telnet PAD: failed: port 2323 is already in use` | It could not listen (red). Another program has the port. Pick another port and press Apply. The rest of the engine keeps working |

## How a session works

- Each telnet connection gets its **own PAD**, separate from the others and from the PAD Client windows.
- The server tells the telnet program to send each key at once and not to echo it itself. The PAD echoes
  what you type, as it does in the PAD Client window.
- Commands at the `PAD> ` prompt are the X.28 commands. `HELP` lists them: `CALL <address>`, `CLR`, `STAT`,
  `PAR`, `SET n:v`, `PROF`, `RESET`, `INT`, `DATA`.
- In a call, `Ctrl+P` takes you back to the command mode (the PAD escape of X.28). `CLR` clears the call and
  the PAD says `CLR CONF` and shows `PAD> ` again.
- If the host clears the call, you see the `CLR ...` signal with its cause and `PAD> ` comes back.
- Closing the telnet connection clears any call you had and frees the session.

## Checking it by hand

With the telnet PAD on and a second terminal:

```
telnet 127.0.0.1 2323
PAD> CALL 23430200
COM
hello
```

The tests in `RetroIO.Protocols.X25.Tests` (`TelnetPadServerTests`) and `RetroX25.Desktop.Tests`
(`TelnetPadSettingsTests`) do exactly this over real sockets.
