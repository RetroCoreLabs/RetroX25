# How XOT peers work: dial out, accept in, source IP, NAT

XOT is X.25 carried over a TCP connection. This page says how RetroX25 treats the other end of such
a connection, and why one configuration entry is enough for a peer such as a Cisco router.

Sources (read for this page):

- RFC 1613, "cisco Systems X.25 over TCP (XOT)": <https://www.rfc-editor.org/rfc/rfc1613.html>
- Cisco, "X.25 over TCP Profiles" (IOS XE configuration guide):
  <https://www.cisco.com/c/en/us/td/docs/ios-xml/ios/wan_smxl/configuration/xe-16-10/wan_smxl_xe16_10_book/wan-x25otcp-pro.html>

## What the RFC says

- There are **no client and server roles**. Both ends are equal. There is no handshake, no peer
  identity and no security (the RFC does not discuss security at all).
- A **separate TCP connection is used for each X.25 virtual circuit** (section 5). All of them go to
  TCP port **1998** (section 5).
- The X.25 Call Request packet is sent by the XOT end that **opened** the TCP connection
  (section 6.1).
- The TCP connection **may be kept** after the circuit is cleared (section 5).

So an XOT end does two jobs: it **dials** when it has a call to make, and it **accepts** when the other
end dials. That is why the Configuration window has the mode **Both ways (peer)**: ONE entry for one
peer, that does both.

## What RetroX25 does for a "Both ways (peer)" entry

| Part | Behaviour |
|---|---|
| Dial out | When a call is routed to the entry, a **new TCP connection** is opened to the peer address and port (1998 unless you change it) and the call is placed on it. Two calls are two connections. The connection is opened when the call needs it, not before. |
| After the call | When the circuit is cleared, the connection we opened is closed. (The RFC allows keeping it; closing it keeps things simple and leaks nothing.) |
| No retry timer | If the dial fails, the call fails, the reason is shown in the XOT links panel, and the **next call tries again**. Nothing retries by itself. |
| Accept in | The entry listens on its local port. A connection whose **source IP address** is the peer address belongs to the entry, and its call is routed like any other incoming call. |
| One listening socket | Only one program can listen on a port. Several entries (peer entries and old-style Server entries) may use the same port: they share one listening socket, and each incoming connection goes to the entry whose peer address equals the source address. |
| Unlisted addresses | A connection from an address that no entry lists is **accepted** as an "unlisted peer" with the default settings, and shown in the XOT links panel as a row `unlisted peer <ip>`. If **Accept only listed peers** is on for any entry on that port, such a connection is **refused**, with the warning `refused: <ip> is not a listed peer` in the log. |

**Both ways (peer)** is what a NEW entry starts with in the edit dialog, and what the sample configs with a known peer use
(`x25config-xot-to-switch.json`, and `x25config-embedded-switch.json`, where the old Client and Server pair is now one entry).
The older modes stay selectable and behave as before: **Client** (only dials) and **Server** (only
listens, takes every address nobody else claims). A configuration file that has only those loads and
runs unchanged (`AnOldFileWithClientAndServerEntries_LoadsUnchanged_AndRuns`). The samples `x25config-tad-callback.json`,
`x25config-telnet-pad.json` and `x25config-xot-test.json` keep a Server entry because it has no one peer address to dial.

### Client mode: one TCP connection for each call

A **Client** entry (dial only) also opens a new TCP connection for every call, as RFC 1613 section 5 asks. It
runs the same dial-out code as the peer mode and differs only in that it never listens. Two calls are two
connections (pinned by `ClientMode_OpensOneTcpConnectionForEachCall` and, against x25emu, by 1, 2 and 10 calls
giving 1, 2 and 10 connections in `Get-NetTCPConnection` and `ss`).

Until 2026-11-13 the Client mode kept one connection per remote address for all calls. Measured on the Cisco router R1
(see "Measured against a real Cisco router" below), that made the router clear the first call as soon as a second
Call Request arrived on the same connection. The new behaviour was proved against x25emu; the router stopped before it
could be run there (`PLAN.md` item A1).
## Source IP, and why it is weak

XOT has **no authentication**. The only thing that tells one peer from another is the **source IP
address** of the TCP connection.

- **Cisco does the same.** An incoming XOT call is matched by the source IP address only, through the
  optional `xot access-group <standard ACL 1-99> [profile <name>]` lists: the first list that permits the
  source address decides the X.25 settings (the profile) for the call. Once any such list exists, a call
  that matches none of them is rejected. Cisco's page does not say what happens when there is no list at
  all, so this page does not claim anything about that.
- Incoming calls are then routed by **X.121 address and call user data** through `x25 route`.
- **NAT:** behind a NAT router the source address is the translated one, and it is **shared by every
  device behind that router**. One address can stand for many machines, so "listed peer" means "something
  behind that address", not "this machine". If two entries list the same address on the same port, the
  first one gets every connection from it, and a warning in the log says so.
- Anyone who can reach the port can connect from an address you did not list. **Accept only listed peers**
  keeps out the strangers that can be told apart by address; it is not a password.

## The XOT links panel

The main window shows **one row per peer**, with both directions counted apart:

- **dialled out**: connections opened, open now, failed (with the last error), dropped (the connection
  ended while a call was still up), bytes in and out, calls placed;
- **accepted in**: connections accepted, open now, dropped, bytes in and out, calls received, and
  connections refused because the address was not listed;
- one extra row `unlisted peer <ip>` for each unlisted address that connected.

The quiet line **Built-in switch: OK** still works. The pair of entries that joins the app to its own
built-in switch (one entry to the switch, one from the switch) may now be **one** peer entry, found the
same way: by facts (loopback address, and the port numbers of real switch ports), never by name. A peer
entry that dials an external address stays a normal, loud row.

## Configuration file

```json
{
  "Name": "XOT_R1",
  "Enabled": true,
  "Mode": "Both",
  "RemoteIPAddress": "192.168.1.155",
  "RemotePort": 1998,
  "LocalPort": 1998,
  "AllowOnlyListedPeers": false,
  "RemoteMachineId": "23600001"
}
```

`Mode` is `Client`, `Server` or `Both`. `AllowOnlyListedPeers` is read only for `Both` entries.
`RequestedWindowSize` (1 to 7) and `RequestedPacketSize` (a power of two, 16 to 4096) are optional. When set,
the entry's calls ask for those sizes in the Call Request facilities, and calls arriving on it are accepted with
the sizes they ask for. Left out, the X.25 section of the configuration applies. A wrong value stops the runtime
from starting, and the message names the entry. The XOT edit dialog has a box for each (empty = the X.25 settings) and
refuses a wrong value when you press OK. There is no profile system.

Read from real bytes against x25emu (`TwoPeers_WithDifferentSizes_PutTheirOwnSizesInTheCallRequest`): a peer set to window 2 and
packet size 256 puts `42 08 08 43 02 02` in its Call Request, a peer set to window 5 and packet size 64 puts
`42 06 06 43 05 05`, and the Packet window shows the same bytes. The facility 0x42 carries the packet size as a power of two
(8 = 256, 6 = 64), 0x43 the window size.

## Measured against x25emu (the C emulator)

From the cross-environment tests (how to run them: [crossenv-testing.md](crossenv-testing.md)). Counts were read in
three places: Windows (the operating system's list of established connections), WSL (`ss`), and our own peer report.

| Calls placed at once | TCP connections open | Notes |
|---|---|---|
| We call x25emu: 1 | 1 | Windows 1, `ss` 1, our report 1 |
| We call x25emu: 2 | 2 | all three views agree |
| We call x25emu: 10 | 10 | all three views agree |
| x25emu calls us: 1 | 1 | no spare connection |
| x25emu calls us: 2 | 4 | 2 for the calls and 2 **spare** connections that x25emu dialled and keeps for the next call |
| x25emu calls us: 10 | 12 | 10 for the calls and 2 spares |

So each call has its own TCP connection, as RFC 1613 section 5 says. x25emu's XOT trunk dials spare connections too;
they carry no circuit and appear as accepted connections in the "accepted in" counts. After a burst of ten calls at
the same instant 5 spares stood open. They are not an error.

- **Kill and restart of the far end** (x25emu switch stopped while a call is up, a call tried while it is down, the
  switch started again): the PAD shows `CLR DER C:9 D:0` (cause 9, out of order); the peer report counts one drop; the
  next call fails with `No connection could be made because the target machine actively refused it.`, counted as one
  failed dial and shown in the XOT links row as `dialled out: ... failed 1 (last error: ...), dropped 1`; the log has a
  line for the failed dial; no timer retries, and the first call after the switch is back dials again and works.
- **No Restart over XOT:** x25emu drops packets on channel 0 over XOT and answers on its own channel number. Our code
  sends no Restart and no other channel-0 packet over XOT (RFC 1613 section 6.4) and translates channel numbers.
- **Several calls on one connection:** its access port answers only the first (so a Client entry now uses one
  connection for each call).

## Measured against a real Cisco router

Router R1 (Cisco 7200 under GNS3, XOT on TCP port 1998, X.121 address 23600001; a second router R2 is behind it).
Run with the explicit tests `CiscoR1MeasurementTests` (raw bytes, in `RetroX25.Tests`), `CiscoR1PeerTests` (our app,
in `RetroX25.Desktop.Tests`) and the three `Live_*` tests in `PadWindowCallTests`. The router's own log (`debug x25
events`) was read for each case. Nothing on the router was changed.

**1. A call to the router's PAD needs call user data 01000000.** The bytes, in order:

| We send | The router answers |
|---|---|
| Call Request without call user data (19 bytes: `10 01 0B 88 23 60 00 01 23 40 00 77 06 42 07 07 43 02 02`) | Clear Request `10 01 13 0D 40`: cause 13 (not obtainable), diagnostic 64 |
| The same with call user data `01 00 00 00` (23 bytes) | Call Accepted `10 01 0F 00 06 42 07 07 43 02 02`; then a Q-bit data packet `90 01 00 04` (an X.29 message of two bytes); then data `10 01 02 0D 0A 52 31 3E`, which is the text CR LF `R1>` |
After our Clear Request (cause 0, diagnostic 0) the router answers with Clear Confirmation `10 01 17`.

**2. One TCP connection per call, both ways.** Our app placing a call: one TCP connection to R1 port 1998, closed after
the call. R1 placing a call to our built-in TAD (`Live_R1CallsTheTad_...`): one TCP connection from the router, with
the router sending the Call Request, our Call Confirm (11 bytes) and a Clear after the user typed `QUIT`.

**3. Several calls at once through a "Both ways (peer)" entry** (`PeerEntry_SimultaneousCalls_...`):

| Calls at once | TCP connections dialled | Calls connected |
|---|---|---|
| 1 | 1 | 1 |
| 2 | 2 | 2 |
| 5 | 5 | 3 (the router cleared the other two with cause 13, diagnostic 64 on their own connections; the router does not say why. I have not verified the reason; a limit on the number of PAD sessions is a guess) |

**4. Two calls on ONE connection are refused.** Measured with raw bytes: the first call (channel 1) is accepted and
the router sends its prompt. Then we send a second Call Request on the same connection, channel 2. The router never
answers channel 2. It sends a **Clear Request for channel 1** with cause 19 (local procedure error), diagnostic 23, and
its log says `Packet type invalid for state P4`. The router logs the second Call Request on `lci 2` and then the clear
of `lci 1` in the same millisecond. A Client entry that puts two calls on one connection therefore loses the first
call. This is why the Client mode now opens one connection for each call.

**5. A connection that opens and closes with no packet** is logged by the router as `XOT incoming cx closed, no VC setup`
and is harmless.

**6. A connection from an address the router has no access list for: NOT measured.** To see it, the connection has to
arrive from an address other than this PC's. This PC has one address on that network; the only other machine that
could forward (the laptop that holds the router console) has no route to the router (`No route to host`). Adding an
address to this PC or changing the router was not done. The first time a different machine is on that network, run
`CiscoR1MeasurementTests.CallToTheRouterPad_...` from it.

