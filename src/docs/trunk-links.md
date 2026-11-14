# Trunk links: leased-line style trunks, with XOT as the extra

A trunk joins two X.25 switches. RetroX25's switch (`x25switch`, and the Desktop's built-in switch) can
join them two ways:

| Transport | What it is | Use it for |
|---|---|---|
| `hdlc-tcp` | A leased line that runs over one TCP connection: X.25 packets inside LAPB frames (the link layer with its own acknowledgements), the frames between 0x7E flag bytes. `hdlc-ppp` is the same thing under an older name from the C emulator (x25emu); it is not PPP. | Switch to switch. This is the normal trunk. |
| `hdlc-serial` | The same frames on a serial cable (RS-422 or a plain receive/transmit cable, full duplex). | Switch to switch over a real cable. |
| `xot` | X.25 over TCP, RFC 1613: one TCP connection per call, no link layer. | A Cisco router, or any peer that only speaks XOT. |

Both ends of a trunk must use the same family: an `hdlc-tcp` port cannot talk to an `xot` port. A port
whose transport the switch cannot run stops the program with a message that names the port and the transport. It is
never run as something else. (Before this was built, a port written as `hdlc-tcp` was silently run as XOT.)

## How an HDLC trunk works

1. The line opens: a TCP connection (hdlc-tcp) or the serial device (hdlc-serial).
2. LAPB sets the link up: one end sends a SABM, the other answers UA.
3. When the link is up, each end restarts X.25 on it (Restart Request, Restart Confirmation). Every call on the
   port is cleared by that, which is what the standard says a restart does.
4. Calls and data run inside LAPB I-frames. Each X.25 packet is one I-frame. Lost, repeated or damaged frames are
   repaired by LAPB (the FCS check catches damage; REJ and the T1 timer ask for what is missing).
5. If the link or the line is lost, every call on the port is cleared (cause 9, out of order), the port says so in the
   log, and it opens the line again by itself.

Modulo 8 only (window 1 to 7). The LAPB address pair follows the ITU rule: a command from the DTE and a response
from the DCE use address 0x01; a command from the DCE and a response from the DTE use 0x03.

### Who dials, who listens, who sends the SABM

| Transport | Mode | The port | Sends the SABM |
|---|---|---|---|
| hdlc-tcp | `dce` | **Listens** on `host:port` (empty or `0.0.0.0` host = every address; port 0 lets the system choose). It carries one link, so a new connection replaces the old one: a peer that restarted is not shut out by its own dead connection. | No: waits for the peer's |
| hdlc-tcp | `dte` | **Dials** `host:port`. If the dial fails or the line is lost it dials again every 2 seconds. | Yes |
| hdlc-serial | `dce` or `dte` | Opens `device`. If it cannot be opened, or the device goes away, it tries again every 2 seconds. | Both ends do; LAPB settles two that cross |

`mode` also decides which logical channel numbers a port prefers (Annex A of X.25: a DTE searches from the top of
its range, a DCE from the bottom), so one end of a trunk is `dce` and the other `dte`.

## Configuration keys

All are keys of a `[port.N]` section.

| Key | Meaning | Default |
|---|---|---|
| `type` | `trunk` (or `access`) | |
| `transport` | `xot`, `hdlc-tcp`, `hdlc-ppp` (= hdlc-tcp) or `hdlc-serial`. A written value that is none of these stops the load. A missing key means `xot`. | `xot` |
| `mode` | `dce` or `dte` | `dce` |
| `host`, `port` | hdlc-tcp: where to dial (dte) or what to listen on (dce) | |
| `device` | hdlc-serial: `COM3`, `/dev/ttyUSB0` | none (required) |
| `baud` | hdlc-serial, 300 to 921600 | 9600 |
| `databits`, `parity`, `stopbits` | hdlc-serial: 7 or 8, `none`/`odd`/`even`, 1 or 2 | 8, none, 1 |
| `flow` | hdlc-serial: `rtscts` or `none` | `rtscts` |
| `lci_base`, `lci_count` | The logical channel numbers the port carries calls on (1 to 4095). A port carries as many calls at once as it has channels. | 1, 16 |
| `window_size`, `packet_size` | X.25 defaults for the port | 2, 128 |
| `lapb_t1` | Time to wait for an acknowledgement before sending again, milliseconds (100 or more) | 3000 |
| `lapb_t2` | Delay before an acknowledgement is sent on its own, milliseconds; 0 = send it at once. Must be smaller than T1. | 0 |
| `lapb_t3` | Idle link check, milliseconds (100 or more) | 30000 |
| `lapb_n2` | How many times a frame is sent again before the link is declared lost (1 to 255) | 10 |
| `lapb_k` | LAPB window: frames out at once (1 to 7) | 7 |

A value that cannot work is refused with the port and the key named. It is not changed to something else.

Both ends of a serial cable must use the same baud, data bits, parity and stop bits.

## What you see

The Desktop main window has a **Trunk links** section with one block per trunk: XOT peers as before, and every HDLC
trunk with

- the state, in colour: `LAPB link up` (green), `line open, LAPB link not up yet`, `line open, LAPB link DOWN`,
  `NOT connected: <reason>` / `NOT open: <reason>` (red), `listening, nobody connected`, `connecting...`;
- what it does after a failure (redials or opens the device again every 2 s, or waits for the peer);
- when the link last came up, how many times it came up, how many **link resets** (the peer sent a SABM or an FRMR
  on a link that was up, or we had to set the link up again after N2 tries);
- how many dials or opens failed and the last error, and how many times the line dropped;
- frames in and out, retransmissions, REJ sent and received, frames refused by the FCS check;
- bytes on the wire in and out, and calls out and in.

**What the call and packet numbers mean** (the same on `show x25 port`, the monitor and the Trunk links panel; each
event is counted once): "calls in" is calls that arrived on the port, "calls out" is calls the switch placed out of
it, "cleared" is calls that ended on it. "Data packets in/out" count data packets only (call set-up, clear, RR, reset
and the like are not in them), so 300 data packets each way over a call give 300 in and 300 out on every port the
call touches. Bytes are the size of every X.25 packet on the wire, control packets included. The frame,
retransmission and REJ figures come from the link layer. The definition is in the comments on `PortCounters` in
RetroIO, and `SwitchCounterTests` proves it: one call across three switches (an XOT access port, an `hdlc-tcp` trunk, an
XOT trunk) gives one call in and one call out on every port it touches, N data packets each way give N in and N out on
every port, and every port clears one call.

**Cleared is the one number that is counted differently by our switch and by x25emu.** Both are correct for their own
definition, so do not compare them blindly:

- RetroX25 counts a clear **once per port the call touched**, when the call's channel on that port is free again. One
  call across three switches is one cleared on each port it crossed.
- x25emu counts a clear **once per call, on the port where the Clear Request arrived**. The same call shows one
  cleared on that port and none on the others (measured on its Switch A monitor: `port 0 ... cleared +1`, the trunk
  ports `cleared +0`).

Calls in, calls out and data packets are counted the same way by both.

The quiet `Built-in switch: OK` line is for the app's own two XOT links to its built-in switch and is not touched
by HDLC ports: an HDLC port is never taken for the other end of an XOT link, even when it listens on the same number.

`x25switch` writes the same events to its log (`LAPB link up; restarting X.25`, `line down (...); dialling again in 2
seconds`, `dial to ... failed ... Failure N in a row`). A warning appears for the first failure and every fifteenth.

## Samples

The three demo switches (`src/demo/switch-a.ini`, `switch-b.ini`, `switch-c.ini`) are chained with `hdlc-tcp`
trunks; see the [demo README](../demo/README.md). The trunk to the reference (x25emu) network on Switch A and the
Cisco and Desktop samples stay XOT because their peers speak XOT.

## What is measured, and what is not

- **hdlc-tcp**, in the RetroIO test project: link up, calls and data both ways with the M, Q and D bits and 128-byte
  packets, sequence numbers wrapping over 200 packets, a frame dropped, repeated or damaged in flight (a relay in
  between that changes chosen frames), the TCP connection cut and redialled, both ends restarting, the SABM
  arriving on a live link, DCE and DTE roles, counters equal to what was sent, a two-switch chain.
- **Three demo switches over two HDLC trunks**, run for real (`demo.ps1 start`): 5000 packets of 128 bytes through
  A, B and C to an echo and back, none lost; 1000 calls held open at once through both trunks.
- **Against x25emu** (the C emulator), our `hdlc-tcp` trunk dialling its Switch A: 3000 packets of 128 bytes both
  ways, no loss, no reordering; its Switch A stopped and started again (our port redialled and relinked); our
  switch stopped and started again (its port took the new connection and relinked). The two Interop tests in
  `RetroX25.Tests` repeat this when `X25EMU_WSL_HOST` names the machine that runs x25emu.
- **hdlc-serial on real serial ports (checked 2026-11-14).** Two USB serial ports joined by an HDLC line at 307200
  bit/s: COM10 is an ESP32 board (HDLCGW firmware) and COM18 is a Pico (RC422Dev). The tests are in
  `SerialHdlcLoopbackTests` in `RetroX25.Tests`. They show:
  - Frames with a body of 2 to 1022 bytes (1024 with the 2-byte checksum) cross in both directions with every byte
    unchanged, including frames made only of 0x7E and 0x7D bytes, and with either escaping style (only flag and escape,
    or every byte below 0x20 as well).
  - 200 frames written back to back arrive in order and unchanged, **if the receiving port is read the whole time**.
    A test that wrote all 200 before it read any lost 119 of them: the Pico's buffer filled and it dropped frames.
  - Both ends can send at the same time.
  - Our `hdlc-serial` port opens the device and the LAPB link comes up, with `flow = none` and with `flow = rtscts`.
  - A whole X.25 call across the line between two switches: 100 packets of 128 bytes come back from an echo, in order
    and unchanged, and the link is not reset.
  Frames longer than 1024 bytes with the checksum are not promised to work and are not tested as a rule (an Explicit
  test measures them; it was not run after the board restart below).
- **Two things to know about these particular boards.**
  - The ESP32 board restarts into the chip's download mode (it then shows up as `303A:0002` on another COM number
    and answers nothing) when DTR and RTS change in the order RTS only, both on, DTR only, both off, or when the port is
    set to 1200 bit/s. A close of the port can complete that order. This is in the board's firmware (Arduino ESP32 core
    `USBCDC.cpp`, told to us by the firmware's owner) and is kept on purpose so flashing works without buttons. Our
    switch port sets DTR on at open; which lines Windows moves at open and close was not measured. It happened twice
    during the first runs of the tests, not in the last two full runs. If it happens, the board has to be restarted
    (unplug it, or `esptool ... --before no-reset --after hard-reset` on the new port).
  - The gateway sends its own control frames to the PC: address byte 0xFE, a type, an id, flags and a payload, then a
    CRC-16 stored without the final inversion. One example is the event text `line receive active`, sent the first time
    line bytes arrive after the line was idle. They never crossed the line. A normal HDLC receiver sees them as frames
    with a wrong checksum and drops them, which is what our port does. The tests set them aside (address 0xFE and CRC
    residue 0x0000) and fail on any other wrong checksum.
- **Not tested on serial:** other baud rates, parity, stop bits and data bits (the ports are USB, so these settings do
  nothing there), a real RS-422 UART, unplugging and plugging back, and x25emu's serial trunk against ours.
- **The other direction, x25emu dialling OUR listening port** (our port as DCE against its DTE): done. x25emu's PAD calls
  our app through its `hdlc-tcp` trunk, and 300 packets through that trunk are counted exactly by our app
  (`X25emuPad_CallsOurApp_ThroughItsHdlcTcpTrunk_...` and `X25emuStream_ThroughItsHdlcTcpTrunk_...` in
  `RetroX25.Desktop.Tests`).
