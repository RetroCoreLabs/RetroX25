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
| No retry timer | If the dial fails, the call fails, the reason is shown in the Trunk links panel, and the **next call tries again**. Nothing retries by itself. |
| Accept in | The entry listens on its local port. A connection whose **source IP address** is the peer address belongs to the entry, and its call is routed like any other incoming call. |
| One listening socket | Only one program can listen on a port. Several entries (peer entries and old-style Server entries) may use the same port: they share one listening socket, and each incoming connection goes to the entry whose peer address equals the source address. |
| Unlisted addresses | A connection from an address that no entry lists is **accepted** as an "unlisted peer" with the default settings, and shown in the Trunk links panel as a row `unlisted peer <ip>`. If **Accept only listed peers** is on for any entry on that port, such a connection is **refused**, with the warning `refused: <ip> is not a listed peer` in the log. |

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
Call Request arrived on the same connection. The new behaviour was proved against x25emu and then on 2026-11-13 against the
router (points 3 and 7 below: 1, 2 and 5 calls gave 1, 2 and 5 connections).
## Source IP, and why it is weak

XOT has **no authentication**. The only thing that tells one peer from another is the **source IP
address** of the TCP connection.

- **Cisco does the same.** An incoming XOT call is matched by the source IP address only, through the
  optional `xot access-group <standard ACL 1-99> [profile <name>]` lists: the first list that permits the
  source address decides the X.25 settings (the profile) for the call. Once any such list exists, a call
  that matches none of them is rejected. Cisco's guide "X.25 over TCP Profiles" (wan-x25otcp-pro) says: "For incoming
  XOT calls, the source IP address is tested", "If no access lists are applied to XOT, the default X.25 settings are
  used", the `xot access-group` command disables the older XOT behaviour, and only standard IP access lists are
  accepted. By that text a router with no `xot access-group` (R1 has none and no access list) accepts a call from any
  source address. Measured for R1 on 2026-11-13: a caller from a second machine was served ("Measured against a real
  Cisco router", point 6). The text does not say what happens to a caller that a list does not permit, or to a list that
  does not exist; measured on 2026-11-14: the TCP connection is reset and nothing is logged (point 6b).
- Incoming calls are then routed by **X.121 address and call user data** through `x25 route`.
- **NAT:** behind a NAT router the source address is the translated one, and it is **shared by every
  device behind that router**. One address can stand for many machines, so "listed peer" means "something
  behind that address", not "this machine". If two entries list the same address on the same port, the
  first one gets every connection from it, and a warning in the log says so.
- Anyone who can reach the port can connect from an address you did not list. **Accept only listed peers**
  keeps out the strangers that can be told apart by address; it is not a password.

## The Trunk links panel

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
  failed dial and shown in the Trunk links row as `dialled out: ... failed 1 (last error: ...), dropped 1`; the log has a
  line for the failed dial; no timer retries, and the first call after the switch is back dials again and works.
- **No Restart over XOT:** x25emu drops packets on channel 0 over XOT and answers on its own channel number. Our code
  sends no Restart and no other channel-0 packet over XOT (RFC 1613 section 6.4) and translates channel numbers.
- **Several calls on one connection:** its access port answers only the first (so a Client entry now uses one
  connection for each call).

## Measured against a real Cisco router

Everything learned about the router, in one table with the cause and diagnostic codes and the commands to look at it: [cisco-learnings.md](cisco-learnings.md).

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
| 5 | 5 | 3 (the router cleared the other two with cause 13, diagnostic 64 on their own connections; the router's PAD has 5 vty lines, see point 9; why this run saw cause 13 diagnostic 64 where point 9 sees cause 0 diagnostic 67 is not explained) |

**4. Two calls on ONE connection are refused.** Measured with raw bytes: the first call (channel 1) is accepted and
the router sends its prompt. Then we send a second Call Request on the same connection, channel 2. The router never
answers channel 2. It sends a **Clear Request for channel 1** with cause 19 (local procedure error), diagnostic 23, and
its log says `Packet type invalid for state P4`. The router logs the second Call Request on `lci 2` and then the clear
of `lci 1` in the same millisecond. A Client entry that puts two calls on one connection therefore loses the first
call. This is why the Client mode now opens one connection for each call.

**5. A connection that opens and closes with no packet** is logged by the router as `XOT incoming cx closed, no VC setup`
and is harmless.

**6. A connection from an address the router has no access list for: measured, it is accepted (2026-11-13).** The probe ran on a second machine on the same network, ubuntu18lts, address 192.168.1.228 (not this PC's 192.168.1.180; WSL cannot be used because Windows translates its address, checked with `show tcp brief`). R1 has no `xot access-group` and no access list. One TCP connection to R1 port 1998 and one XOT Call Request were sent from there, and the bytes were read in order:

- `show tcp brief` on R1 while the connection was open: `192.168.1.155.1998  192.168.1.228.51836  ESTAB`.
- Sent (XOT header `00 00 00 16`, then the call): `10 01 0b 58 23 60 00 01 10 00 00 06 42 07 07 43 07 07 01 00 00 00` (called 23600001, calling 10000, packet size 128 and window 7, call user data `01 00 00 00`).
- Received after 0.05 s: Call Accepted `10 01 0f 00 06 42 07 07 43 07 07`, then a Q-bit data packet `90 01 00 04`, then a data packet `10 01 02 0d 0a 52 31 3e`, which is the text `R1>` after a CR LF (the router PAD prompt).
- Router log: `XOT I P/Inactive Call (22)` from 192.168.1.228, `XOT O P3 Call Confirm`, the two data packets, then after our Clear Request `Cause 0, Diag 0` and `Clear Confirm`.

So a router with no access list serves a caller from any source address, as Cisco's text says. A first probe with a facility length of 0 (no facilities) was cleared by the router with `XOT Call packet, Bad value for facility`, cause 3, diagnostic 66: the router wants the facility block, and the cause was the packet, not the address.

**6b. The same router with an `xot access-group` (measured 2026-11-14, R1 changed for the test and put back).** R1 got `access-list 10 permit 192.168.1.180` and `xot access-group 10`. The same call (the bytes above) was sent each time, and the router log was read after each:

| Case | Caller | Result |
|---|---|---|
| A. List 10 permits only 192.168.1.180 | 192.168.1.228, not in the list | **Refused at the TCP level:** the connection opened, the router reset it 0.06 s after the Call Request was sent (`connection reset`). Nothing was answered and **nothing was logged** by `debug x25 events`: no XOT line at all. |
| B. Same list | this PC (WSL leaves as 192.168.1.180) | Served: Call Accepted, the `R1>` prompt, Clear Confirm after our clear. |
| C. `access-list 10 permit 192.168.1.228` added | 192.168.1.228 | Served, the same as B. |
| D. `xot access-group 11`, list 11 does not exist | 192.168.1.180, which was served in B | **Refused the same way** (reset at 0.09 s, nothing logged). This confirms Cisco's statement that a list that does not exist denies everything. |

So an address the list does not permit gets a reset TCP connection and no X.25 packet and no log line, which is what a client sees as "connection reset" right after connecting. Afterwards `no xot access-group 11`, `no access-list 10` put R1 back; its full `show running-config` was saved before and after and the two are identical (70 lines each). `no xot access-group 10` answered `Can't delete unknown access group 10` because the group had already been changed to 11; harmless.

**7. All the router tests were run again on 2026-11-13 and pass:** the three `Live_*` tests (each alone; they need the
switch running, or the router calling in, as their comments say, and they must be run one at a time because each
needs port 1998 or 1999 for itself), `ClientEntry_SimultaneousCalls_OpenOneConnectionEach` (1 call: 1 connection; 2
calls: 2 connections, both connected), `PeerEntry_SimultaneousCalls_...` (table in point 3 again) and both
`CiscoR1MeasurementTests`.

**8. Q, M and D bits cross the router unchanged, both ways.** Read from `debug x25 all` on R1 for a call from our
client library (measured with the `x25traffic` options `--qbit`, `--dbit` and `--mbit-chain`, which have since been removed: the bits are now tested through the library, see `QmdBitsTests`) through R1 to R2's PAD. A Q-bit data packet from us left the router's serial side with the Q bit set
(`O ... Data (19) Q`), and the Q-bit packets R2 sent came out of the XOT side with Q (`XOT O ... Data (4) Q`). A packet
sent with the D bit left the serial side with `D`; the confirmation came back as a Receive Ready and the tool reported
all packets confirmed. A chain of three 128-byte packets with the M bit left the serial side as three packets that
all carry `M` (131 bytes each: 128 data and 3 header). Nothing was merged, split or cleared.

**9. Only 5 PAD calls at once, on R1 and on R2 alike.** Both routers have `line vty 0 4`, and each PAD call holds one of those lines for its whole life. The 6th call at the same time is cleared by the router's PAD with cause 0, diagnostic 67 (the diagnostic reads "invalid destination address", which is misleading). Proof, from the routers' own output: `debug x25` shows `X.25 O R1 Clear (5) 8 lci 6` with `Cause 0, Diag 67` for lci 6 to 8 right after Call Confirm on lci 1 to 5; `show users` lists vty 0 to 4 held by 99990001; `show x25 context` gives two-way channels 1 to 1024, so channels are not the limit; R1's own PAD (23600001) has the same limit; and after the calls are released a new call connects. (The same test sizes gave 5, 10, 20, 50 and 64 calls at once: 5 connected each time.) To raise it on the router (not applied): `line vty 0 15`. Calls to our echo service through the same switch: 50 of 50, five rounds in a row, nothing left open on either side. Measured on 2026-11-13 and read again the same day (R2 `show line` lists the five lines, VTY 2 to 6, and `show x25 vc` binds each of the five circuits to its own vty).

**10. A long stream into the router's PAD: fixed (2026-11-13).** Sending 64-byte packets to R2's PAD, the router echoes every byte and answers with a never ending run of full 128-byte packets that all carry the M bit. Our X.25 layer joined such a run into one message for its consumers and reset the circuit (cause 0, diagnostic 39, "packet too long") when the run reached 64 KB, so the stream ended after about 19 seconds with 154 to 156 packets sent. **What the standard says** (ITU-T X.25 10/96, read from the text): 4.3.4 "If a DTE or DCE wishes to indicate a sequence of more than one packet, it uses a more data mark (M bit)"; 4.3.5 defines the complete packet sequence (category A packets, full with M=1 and D=0, and the category B packet that ends it) and says "When transmitted by a source DTE, a complete packet sequence is always delivered to the destination DTE as a single complete packet sequence", and "user data fields of data packets are delivered to the receiving DTE exactly as they have been received by the network" when the maximum sizes are equal; Table 4-1 shows the packets arrive with the M bit kept. 4.3.2 gives the one reset for size: a data field longer than the agreed maximum is a local procedure error. No clause was found that makes the receiving DTE join the packets into one block, none that bounds the length of a sequence, and none that names a reset for a long one. RFC 1613 says only that an XOT implementation doing local flow control may fragment or combine the packets of a sequence and must keep to the agreed packet sizes. So the layer now hands a long sequence on in parts of about 64 KB, every part but the last with a `moreData` flag, and never resets for length; `DataReceived` still gives every packet with its own M bit, which is what the client library, the PAD, the TAD and the switch use. **Proof:** a unit test sends 100 000 full packets (12.8 MB) with no reset and every byte in order; and on 2026-11-13 the same stream into R2's PAD (`x25traffic -d 23600002 -u 01000000 -n 800 -r 100 -s 64`; the option `-u` existed then and has been removed, the PAD now sends the X.29 call user data on its own, so the same stream today needs a PAD client) ran for 99 seconds, sent all 800 packets, received 352 871 bytes (the old cut came at about 66 000) and ended with a normal clear, cause 0 diagnostic 0, no reset anywhere.

**11. The router calling x25emu, and x25emu calling the router, both through our switch** (`demo/switch-cisco-x25emu.ini`).
x25emu's PAD (`x25pad -a 23400001`; measured when it still had a call-user-data option, now it sends the X.29 data itself) called 23600001 and got the `R1>` prompt and the clock; the call went
x25emu Switch A, our switch (trunk on 2210), R1. The router's `pad 23430001` reached an x25emu echo service
attached to Switch A (port 2007) through R1, our switch (trunk 1998 in), Switch A (our switch dials its access port
2001), and the text came back. Each switch counted one call in and one out. The clear is cause 0 diagnostic 0 on both
sides. Without a service attached to 2007 x25emu waits 5 seconds and clears the caller with cause 1 (number busy),
diagnostic 71. A storm of 5 calls from x25emu's `x25traffic` to R1's PAD: 5 of 5 connected, 5 of 5 cleared.

