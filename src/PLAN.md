# RetroX25 plan - outstanding work only

**Next:** B1 (facilities pass through the switch; B and D need it). Push (A6) only after Ronny has looked at the commits.

Rules for this file: finished items are deleted, not ticked. Each item says what to build, how it is proved,
and what it waits for. What was measured is in `docs/xot-peers.md`, `docs/trunk-links.md`,
`docs/cisco-learnings.md` and `docs/crossenv-testing.md`.

Rule for every item below: the standard is the authority. Test vectors come from the figures in the standard
(the X.25 and X.75 texts are kept outside this repository), never from the output of another program. Two programs
that agree can both be wrong.

---

## A. Things that are not right or not done

### A0. Serial: what is still not tested
Tested 2026-11-14 on two real USB serial ports joined by an HDLC line (see `docs/trunk-links.md`). Still not tested:
- x25emu's `hdlc-serial` trunk against ours. WSL cannot see the Windows COM ports (no `socat`, no USB pass-through set up).
- A real RS-422 UART, and baud, parity, stop bits and data bits other than the defaults: the ports above are USB and
  ignore them.
- Unplugging and plugging back (our port opens the device again every 2 seconds; only the in-memory cable proves it).
- Frames above 1024 bytes with the checksum on the gateways: the ESP32 firmware drops them (limit 1024 octets incl. FCS,
  `BYTE_FRAME_MAX`, and a 4096-byte ring of bit-stuffed line bits); raising it to 4105 needs the shared library, a bigger
  ring and PSRAM, and is Ronny's decision (asked 2026-11-14). The Pico's limit was not asked. Until then packet sizes up to
  512 are the most the gateway line can carry.
- Waits for: a real UART or a serial pair visible from WSL, and someone to pull the cable.

### A1a. Full speed both ways through the Cisco: not proven on real equipment
Measured 2026-11-16 on GNS3 (idle sleep 1 ms, idle-PC off, serial windows 7):
- One way, either direction: about 120 packets a second (121 and 127 kbit/s).
- Both ways at once (`_FullSpeed20000`): 20 000 of 20 000 back, 57 a second each way, the echoes keep pace with the
  send (6 min 8 s in all). The two directions share about 120 packets a second.
- The slow way back of 2026-11-15 (117 out, 24 back, last echo 8 minutes late) did not happen again after both routers
  had been restarted. Its cause is not known; it is not claimed fixed.
- Not known: whether a real Cisco shares the same budget between the directions. The emulator cannot say.
- Prove: the same tests on two real 25xx routers. The configs, the load script, the test commands and the packet-reading
  steps are in `docs/cisco-ios-x25-options.md` section 9.
- Waits for: Ronny to set up the hardware.

### A6. Housekeeping
- The work is committed by topic in both repositories and not pushed. RetroX25 pushes from WSL (Windows has no
  GitHub SSH key).
- Files that were already uncommitted when this work started and are not ours: `.gitattributes`, `.github/SECURITY.md`,
  `src/demo/` (README, demo.ps1, demo.sh, switch-a/b/c.ini), `DemoConfigTests.cs`. Ronny decides what happens to them.

---

## B. X.75: two networks joined by gateway exchanges (STE)

Goal: the picture "X.75 Network-selection": two national networks, each with its own DNIC, joined by an STE on each
side. Prove it between C# switches, between C# and x25emu, and measure what the Cisco router does with it.

What is known (checked 2026-11-14): x25emu already has an X.75 port (`trunk_kind = network`) and a three-network demo.
The C# switch has none. The Cisco IOS 12.4(24)T5 router has no X.75 command (only `x25 roa`); that no Cisco document says
"no X.75" is not verified.

### B1. Facilities pass through the switch
Done 2026-11-14: the window and packet size are negotiated end to end (the onward Call Request asks for what the caller
asked for, limited to the egress port's `max_packet_size`; the Call Accepted states what the far end agreed to). Every other
facility is still dropped when the switch relays a call. Billing, X.75 and the hop count all need them to cross.
- Build: carry the facility block from the incoming Call Request to the outgoing one, and back in Call Connected and
  Clear. Unknown facilities pass as bytes.
- Prove: unit test with a known facility block in, the same bytes out; a hop-count facility crosses a three-switch chain.

### B2. X.75 packets
Parser and builder for the X.75 utility field (up to 63 octets in Call Request, Call Connected, Clear): transit network
identification (DNIC list), call identifier (24 bit), throughput class, window, packet size.
- Prove: byte vectors written from the standard's figures; round trip; every field.
- Then check the same vectors against x25emu's encoder (that catches a regression, it does not prove fidelity).

### B3. STE port mode
A port with no DTE/DCE roles. Channel ranges and the collision rule are agreed in the configuration: a call collision clears
both calls with "network congestion". Sequence numbers modulo 8 (modulo 128 is out of scope, see the scope decisions).
- Prove: two switches as STEs over HDLC-over-TCP; a call crosses; a collision test clears both; the call identifier and
  the transit list arrive intact.

### B4. Between networks
Route by DNIC to the STE port; add our DNIC to the transit list; clear a call whose list already holds our DNIC (loop).
X.75 cause and diagnostic codes on refusal.
- Prove: a loop of three networks clears with the right cause; the transit list on a two-hop call is in order.

### B5. Demos
1. Four-switch mesh in one network (the picture "X.25 Network-selection"): never built or tested. Prove a call takes the
   second way when a trunk is cut.
2. Two networks, one STE each, a subscriber on each side (the picture "X.75 Network-selection").
3. The same with one network run by x25emu.

### B6. C# against x25emu
Call in each direction across the STE pair, with data, clear causes and the call identifier checked in both logs.
- Waits for: B2 to B4. Any difference is looked up in the standard, not settled by choosing a side.

### B7. Cisco measurement
Send one Call Request with an X.75 utility field through R1 as plain X.25 transit (needs `x25 routing` and a DNIC route
on each side), and read the result and the router's debug output.
- Prove: record what R1 does: carries it, strips it or clears (cause and diagnostic). Written into `cisco-learnings.md`.
- Waits for: GNS3 up, and a router configuration change that Ronny agrees to.

### B8. Documentation
Two-country page next to `x25-network.md` with the X.75 picture (copy it into `docs/images`), and the tools list updated.

---

## C. Permanent virtual circuits (PVC)

Found by reading the code on 2026-11-14 (only x25emu's tests were run):
- x25emu: works through its switch, including the RFC 1613 6.5 setup exchange, a reset on link loss and no clear on a PVC.
- C#: the packet layer can hold a PVC (`AddPvc`), but the runtime never calls it, the switch reads `[pvc.N]` and never
  joins two channels, and there is no setup exchange. A PVC cannot be used through our switch today.
- Probable defects (read, not run): a Call Request on a PVC gets a Clear instead of a Reset with diagnostic 35 (no code 35
  in the diagnostic list); a Clear Request on a PVC confirms and leaves the PVC unusable.

### C1. Layer
Unit tests first, each failing before the fix: call packets on a PVC give Reset cause 5 diag 35; Clear Request on a PVC
leaves it usable; window of 2 holds the third packet; interrupt on a PVC; reset from each side and a reset collision;
packet on an unassigned channel gives diag 36.

### C2. Switch
Read `pvc_base`, `pvc_count` and `[pvc.N]` (errors for overlap and an unknown port). Build the join, call `AddPvc`, and make
`show x25 pvc` and the counters show it.
- Prove: data both ways with no call; link drop gives the far side Reset "out of order" and no Clear; link return gives
  "remote DTE operational"; five PVCs at once; PVC and calls on one port; Restart resets the PVC without breaking the join.

### C3. XOT setup exchange
The RFC 1613 6.5 setup packet (initiator and responder, refusal codes 0x13, 0x14, 0x16, 0x17, 0x18).
- Prove: vectors from the RFC; C# against x25emu in both directions.

### C4. Router
Read the Cisco page for `x25 pvc (XOT)` first (it could not be fetched; the syntax is not verified). Then a PVC to R1,
`clear xot`, router restart, and a frame-by-frame decode of the router's setup packet.
- Waits for: the Cisco syntax, and Ronny's agreement to change R1.

---

## D. Billing and call records

Found 2026-11-14: x25emu counts 64-octet segments per call and per account, bills by address range or by NUI (billing
login in the call), and returns charging information on clear; its call record line lacks segments, account and NUI, and
it ignores reverse charging. The C# switch has a call-record event and address-range accounts only, and nothing writes
the file. Standards read: X.25 (facilities 6.18 to 6.22, 7.3.6 to 7.3.8), X.75 (5.3.x), D.11, D.12. Not read: D.10, D.13,
and no real call record from an old network was found.

### D1. Decide the call clock
D.11 counts from Call Connected; x25emu counts from when the call is joined. Pick one, write it down, make both match.

### D2. Record format v2
Keep the 14 existing columns, add on the end: connect time, connected duration, segments each way, account, NUI, reverse
charge, who is charged, facilities, charging requested, clearing network, transit list. Document it in `docs/`.

### D3. C# switch
In this order, each with unit tests: write the record file (the setting exists, nothing writes it); count segments
(complete packet sequence rule, X.25 4.3.5, round up per message); parse reverse charging, NUI and charging request;
choose the account by NUI before the address range and strip the NUI before relaying; reverse charging accepted or
refused with cause 0x19; charging information returned on clear.

### D4. Tariffs and bills
Prices in whole minor units (no floating point) in a tariff section; a small tool reads a record file and prints a bill per
account. The record stays facts only; prices are applied by the tool.

### D5. Across networks
The calling network bills its subscribers (X.75 section 3). Every switch writes its own record with the call identifier so
the records for one call can be matched. A per-network total for settlement.

### D6. Same call, both switches
One scripted call through the C# switch and through x25emu gives the same record columns (time columns left out).

---

## E. Documentation still open

The wrong and misleading statements found by the audit of 2026-11-14 are fixed and committed in RetroIO, RetroX25 and
x25emu. What is left:
- Ten guides in `src/docs` are not on the docfx site (it publishes only the program READMEs and the demo README).
  Adding them to `docfx.json`, `toc.yml` and `gen_toc.py` looks small; it needs a docfx build to prove.
- Screenshot slots are placeholders (Ronny's to capture).
- The XMSG protocol notes exist twice in RetroIO (`src/DOC/protocols` and a copy inside the Node package).
- RetroIO `src/tests/demo/` is a dead copy of an old demo (its README now says so).
- x25emu: `HANDOFF-pvc.md` (untracked, not ours) is out of date; the "Multi-connection access ports" line in its README
  was not confirmed.
