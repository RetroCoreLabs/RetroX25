# What we learned about the Cisco router (one page)

This page collects, in one place, everything measured about the Cisco router on the GNS3 bench: router R1
(Cisco 7200, IOS 12.4(24)T5, X.25 address 23600001, XOT on TCP port 1998, the DCE side) and router R2 (X.25 address
23600002, the DTE side) behind it on a serial cable. Each line says where it was measured. The detail, with the
bytes, is in the documents linked in the table. Facts that were not measured are marked "not verified".

## What we learned

| Fact | Evidence (where measured) | What it means for our code or for a user | Detail |
|---|---|---|---|
| A call to a router PAD (the `pad` service, the part that lets you type at the router) needs X.29 call user data `01 00 00 00`. Without it R1 clears with cause 13, diagnostic 64; R2 clears with cause 0, diagnostic 67 and its debug says `pad_svc_announce: non-standard PID`. The text "Invalid destination address" that Cisco prints is misleading. | R1: raw bytes, `CiscoR1MeasurementTests`, 2026-11-13. R2: `debug x25` on the bench (date not recorded in the test notes). | `x25pad` and the Desktop PAD window send the data on their own for every call. There is **no command-line option** for it any more (the old `x25traffic -u 01000000` was removed on 2026-11-14, commit 821085e). `x25traffic` is a data generator and cannot call a router PAD. | [xot-peers.md](xot-peers.md) point 1; [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) B1, B2 |
| Each PAD call holds one of the router's 5 vty lines (`line vty 0 4`). The 6th call at the same time is cleared with cause 0, diagnostic 67. The limit is the same on R1's own PAD and on R2. Channels are not the limit (two-way channels 1 to 1024). | `debug x25` (`Clear (5) 8 lci 6` with `Cause 0, Diag 67`), `show users` (vty 0 to 4 held), `show x25 context`, `show line`, `show x25 vc`; 5, 10, 20, 50 and 64 calls at once gave 5 connected each time, 2026-11-13. | Do not expect more than 5 PAD calls through one router. To raise it on the router: `line vty 0 15` (**not applied**). Calls to our echo service are not limited this way: 50 of 50, five rounds in a row. | [xot-peers.md](xot-peers.md) point 9 |
| XOT uses one TCP connection for each call (RFC 1613 section 5). 1, 2 and 5 calls gave 1, 2 and 5 connections. | `PeerEntry_SimultaneousCalls_...` and `ClientEntry_SimultaneousCalls_OpenOneConnectionEach`, 2026-11-13. | Our dial-out code opens a new connection for every call. | [xot-peers.md](xot-peers.md) points 2, 3, 7 |
| A second Call Request on a connection that already carries a call is never answered, and the first call is cleared with cause 19, diagnostic 23 (router log: `Packet type invalid for state P4`). | Raw bytes, `CiscoR1MeasurementTests`, 2026-11-13. | This is why the Client mode no longer shares a connection between calls. | [xot-peers.md](xot-peers.md) point 4 |
| A Call Request with no facility block is cleared with cause 3, diagnostic 66 (`XOT Call packet, Bad value for facility`). The router wants the facility block. | Probe from a second machine, 2026-11-13 (router log). | Our tools always send the sizes: packet size 128 and window 7 as `42 07 07 43 07 07`. | [xot-peers.md](xot-peers.md) point 6 |
| The router accepted a call that also carried x25emu's hop-count facility (`00 00 3f 00` after the standard facilities). | Measured 2026-11-12 and 2026-11-14. x25emu's switch, with the trunk to the router left at its default kind, sent this Call Request (22 bytes, logged by the switch): `10 40 0b 58 23 60 00 02 10 00 00 0a 42 07 07 43 07 07 00 00 3f 00`. The router did not refuse it for the facility: it passed the call on to R2 (R2 then cleared it for the missing call user data, cause 0 diag 67). On 2026-11-14 the same trunk with the PAD's automatic call user data got the `R1>` prompt and a `show clock` answer. | None known. Whether the standard allows a router to ignore it is not verified (see the open points). | - |
| XOT access control: with no `xot access-group` the router serves a caller with any source address. With `xot access-group 10` and a list that does not permit the caller, or with a list that does not exist, the TCP connection is reset about 0.06 s after the Call Request and **nothing is logged** by `debug x25 events`. Permitted callers are served as before. | Second machine on the LAN, 2026-11-13 and 2026-11-14. R1 was changed for the test and put back: the full `show running-config` before and after is identical (70 lines each). | A refused XOT caller sees "connection reset" right after connecting and the router log gives no reason. Cisco's guide (quoted in xot-peers.md) says a call that matches no list is rejected; it does not say what the caller sees. | [xot-peers.md](xot-peers.md) points 6 and 6b |
| **The router does not pass an XOT call on to another XOT destination.** A call that arrives over XOT and matches an `x25 route ... xot` entry is cleared at once with cause 13, diagnostic 64, and the router never opens the onward TCP connection. A call the router starts itself (`pad 23430200` on its prompt) goes out on the same route and works. Cisco's IOS Wide-Area Networking Command Reference, command `x25 route`: "Incoming calls received via XOT connections that match a routing entry specifying an XOT destination are cleared", to stop XOT connections being chained. No setting turns it off (`xot-source`, the `xot-keepalive` options, `continue` and `x25 routing tcp-use-if-defs` do other things). | 2026-11-15, R1, `debug x25 all` and `debug ip tcp transactions` read from `show logging`: the Call Request arrives (from 23400055 to 23430200, facilities and X.29 data correct), the Clear goes out 4 to 24 ms later, no SYN to the target. Changed and made no difference: the caller (x25emu's `x25pad`, our raw caller), the window (2 and 7), one or two TCP connections, the target address (this PC, and a temporary route to the laptop, removed afterwards), and the caller's machine (x25emu in WSL on this PC, and x25emu on the laptop at 192.168.1.178, 2026-11-15: same clear; through R2 from the laptop the results were the same as from WSL, including the slow way back at full speed). `X25emuPadToOurTadViaCiscoTests`. | A Cisco can sit between two XOT ends only if one leg is a serial X.25 line (XOT in, serial out to another router, XOT out from that one). x25emu → R1 → our app does not work and cannot be made to; x25emu → our app directly does. **x25emu → R2 → serial → R1 → our app works** (2026-11-15, `X25emuPad_CallingOurTadThroughR2AndR1_OverTheSerialLine_Connects`): R2 receives over XOT and sends to a serial line, R1 receives from a serial line and sends to XOT. A route that depends on where the call came from (`x25 route ^2343 input-interface Serial1/0 xot ...`) is accepted by IOS, but turning the call round on R2 (in on Serial1/0, back out of Serial1/0) is cleared by R2 with cause 0, diagnostic 64, so a single serial cable cannot be used as a loop. | [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) section 1, tests 11 to 13 |
| **A switched call's speed through the router is set by the windows and the round trip on every leg, and a serial line's default window is 2.** R1 acknowledges a packet to the sender only as it passes it on, so a slow leg behind it slows the leg in front. With the serial line's defaults at 2/2, R1 kept only 2 or 3 packets unacknowledged towards R2 although the call had asked for 7 and `show x25 vc` showed 7. `x25 win 7` / `x25 wout 7` need `clear x25 <interface>` before they apply ("Parameter change held until a RESTART event"). | 2026-11-15: our trunk capture (R1's P(R) stayed at 2 for 130 ms while our P(S) went to 6) and `debug x25 interface Serial1/0` on R1 and R2 during a full-speed x25traffic run. Full speed went from 55% to 72.5% delivered with the windows at 7; the rest came from the GNS3 idle sleep (every debug timestamp on a 32 ms beat; `idlesleep` 30 → 1 ms gave 127 kbit/s and 100%). | In GNS3, set `idlesleep` low and the serial windows to 7 before measuring speed; the 32 ms beat is the emulator's, not the router's. After a router restart the beat came back although `idlesleep` read 1 (2026-11-16); idle-PC 0x0 removed it (cause not known). The serial clock rate is not the limit in GNS3: 64 000, 128 000 and 2 015 232 (with the port restarted) gave the same 58 kbit/s. Both ways at once the two directions share about 120 packets a second. | [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) sections 4, 5 and 9.3 |
| Traffic from WSL (NAT mode) reaches the router with this PC's address, not WSL's own. | `show tcp brief` on R1. | A test of an unknown address must be made from a second machine. | [xot-peers.md](xot-peers.md) point 6 |
| Q, M and D bits cross the router unchanged, both ways. A chain of three 128-byte packets with M left the serial side as three packets that all carry M. | `debug x25 all` on R1, 2026-11-13. Measured when `x25traffic` still had bit options; today the proof is in `QmdBitsTests`. | Nothing is merged, split or cleared by the router. | [xot-peers.md](xot-peers.md) point 8 |
| The router's PAD answers a stream with an endless run of full packets that all carry M. Our X.25 layer used to cut the run at 64 KB with a reset (cause 0, diagnostic 39). | A 99 second stream (800 packets, 352 871 bytes back) ended with a normal clear, 2026-11-13. | Fixed in RetroIO (commit c9ce8b4): a long run is handed on in parts, with no reset for length. X.25 clauses 4.3.4 and 4.3.5 were read. | [xot-peers.md](xot-peers.md) point 10 |
| The router keeps its TCP connection to the PC open after a call ends. A connection that opens and closes with no packet is logged as `XOT incoming cx closed, no VC setup` and is harmless. | Bench, 2026-11-12; router log, 2026-11-13. | Do not wait for the router to close its connection. | [xot-peers.md](xot-peers.md) point 5; [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) section 12 |
| When the echo service sends a goodbye and clears at once, the router's PAD does not show the goodbye. The service therefore sends an X.29 invitation to clear and waits up to 2 seconds for the router to clear. | `debug x25 xot`, 2026-11-11. The reason the PAD drops the text was not found. | Built into `x25echo`. | [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) test A1 |

## Cause and diagnostic codes seen

| Cause / diagnostic | Meaning | Seen when | Where |
|---|---|---|---|
| 0 / 0 | Normal clear | Every ordinary end of a call, from either side; clear confirm follows | all call tests |
| 0 / 67 | The router's PAD refuses the call (the text "invalid destination address" is misleading) | A call without X.29 call user data to R2; the 6th simultaneous PAD call (no free vty line) on R1 or R2 | [xot-peers.md](xot-peers.md) points 1, 9 |
| 13 / 64 | Not obtainable | A call that arrived over XOT and whose route is an XOT route (Cisco never forwards XOT to XOT); a call without call user data to R1's own PAD (the 19-byte Call Request); also 2 of 5 calls in one run of point 3 (why this and not 0 / 67 there is **not explained**) | [xot-peers.md](xot-peers.md) points 1, 3 |
| 3 / 66 | Invalid facility (`Bad value for facility`) | A Call Request with a facility length of 0 | [xot-peers.md](xot-peers.md) point 6 |
| 19 / 23 | Local procedure error (`Packet type invalid for state P4`) | A second Call Request on a connection that already carries a call | [xot-peers.md](xot-peers.md) point 4 |
| 0 / 39 | Packet too long (our own reset) | A run of full M-bit packets reaching 64 KB; fixed | [xot-peers.md](xot-peers.md) point 10 |
| 5 / 1 | Reset, local procedure error, invalid P(S) | Peer sent more data packets than the window allows | [cisco-ios-x25-options.md](cisco-ios-x25-options.md) |
| 9 | Out of order | The far end was killed while a call was up; a lost link on a trunk | [xot-peers.md](xot-peers.md) (kill and restart); [trunk-links.md](trunk-links.md) |
| 13 | Not obtainable | Our switch finds no route, or caller and service share a port | [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) section 11 |
| 5 | Network congestion | Our switch, in a call storm of 2000 calls | [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) section 9.3 |
| 1 / 71 | Number busy | x25emu with no service on its port, after 5 seconds (not the Cisco router) | [crossenv-testing.md](crossenv-testing.md) |

A refused XOT caller (access list) has no code at all: the TCP connection is reset and no X.25 packet is sent.

## How to look at the router

- **Reach the consoles.** GNS3 listens only on the laptop. Make a tunnel with `ssh -N -L 5101:127.0.0.1:5101
  -L 5103:127.0.0.1:5103 <user>@<laptop address>` and open a plain TCP connection to `localhost:5101` (R1) or
  `localhost:5103` (R2). When the terminal tool is down, a short script that opens that TCP connection and sends the
  lines works as well; the GNS3 console quirks are in [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) section 7 (if a PAD
  call is open, Enter resumes it; Ctrl-^ then `x` leaves it).
- **First command:** `terminal length 0` (otherwise long output stops at `--More--`).
- **Debug:** `clear logging`, then `debug x25 events` (or `debug x25 all`, or `debug x25 xot`), make the call, then
  `undebug all`. The router has `no logging console`, so read the output with `show logging` or
  `show logging | include XOT|Cause|Diag`.
- **State:** `show tcp brief` (source addresses and TCP connections of XOT), `show users` and `show line` (vty lines in
  use), `show x25 vc` (circuits and which vty each one is bound to), `show x25 context` (channel ranges).
- **Always finish with** `undebug all` and `clear logging`, and do not leave a changed configuration behind: save
  `show running-config` before and after and compare.
- Debug output stays in the router's log buffer. Nothing in a measurement here changed the router, except the access
  list test (2026-11-14), which was put back.

## Not measured, or not verified

- Whether X.25 requires a DTE or router to ignore a facility after the `00 00` marker that it does not know: not verified (the 1988 X.75 text does not cover it; one source says `00 00` is the marker for standard facilities, so `3f` would be a private code after that marker). The router accepted it in practice.
- Why a run of simultaneous calls in `PeerEntry_SimultaneousCalls_...` saw cause 13 diagnostic 64 where the same limit in point 9 gives cause 0
  diagnostic 67.
- A raised vty limit (`line vty 0 15`) was not applied, so it was not measured.
- A physical Cisco router (2500 or similar) has not been tested; only IOS 7200 images in GNS3 were.
- Speed and load limits of the router: Dynamips runs the router in software on the laptop, and this was not measured.
- Modulo 128 sequence numbers: not implemented, so not measured against the router ([modulo-128.md](modulo-128.md)).
- `xot access-group ... profile` (X.25 settings chosen by the list) was not tried.

## Related pages

[xot-peers.md](xot-peers.md) (the measurements, in order), [cisco-gns3-xot-test.md](cisco-gns3-xot-test.md) (the bench and
the manual tests), [cisco-ios-x25-options.md](cisco-ios-x25-options.md) (router set-ups), [crossenv-testing.md](crossenv-testing.md)
(the tests that run against the router), [trunk-links.md](trunk-links.md) (trunk behaviour), [tools.md](tools.md).
