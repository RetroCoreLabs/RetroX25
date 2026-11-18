# RetroX25 plan - outstanding work only

**Next:** B2's check against x25emu's encoder, then B3 (STE port). Push (A6) only after Ronny has looked at the commits.

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

### A2. RetroIO test that fails only in a full run
`ASabmOnAnUpLink_ResetsTheLink_RestartsX25_AndIsCounted` failed once in the full X.25 suite (2026-11-17, 5 s, after
1240 others passed) and passed three times alone (about 130 ms). A second full run the same day passed (1253 passed,
0 failed). The first failure's message was not kept, so which of its waits ran out is not known. Next: when it fails
again, keep the full test output (the assertion names the step), then find what that step waits on.

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

### B1a. NUI: what is left (X.28 3.5.15.1.1, 5.2)
- NUI in a Call Accepted (6.21.3): the C# switch clears the call when no account has it (Annex E, b) row 13: cause 3,
  diag 84; `SwitchNuiTests.ANuiInTheCallAccepted_IsChecked`, 2026-11-17), and x25emu the same (its call-through-switch
  test). A valid one now names the called side's account in both switches.
- PAD users: `ID<nui>` / `IDOFF` done in the C# X.28 2026-11-17 (`X28NuiTests`): the NUI after `ID` is not echoed,
  keeps its case, is never logged; spaces and control characters are discarded and `-` `,` `+` give `ERR` (3.5.15.1.1);
  `ID` alone gives `ERR` (no prompting). X.28 defines no reply to a good one, so none is sent.
  The PAD puts the NUI in every later Call Request as facility 0xC6 (done 2026-11-17; RetroX25.Pad's transport carries
  it). The runtime PAD transport (RetroIO's internal one) cannot send facilities and refuses the call rather than drop
  the NUI. `N<nui>-<address>` (also after CALL) done 2026-11-17: no echo inside the N request,
  for that call only and instead of the ID one; any other facility request gives ERR and no call. Forgetting the NUI when the access path goes:
  already true by construction - x25pad makes one PadClient per telnet connection (or one for the console), and the
  NUI lives in it. `x25pad -n nui` done 2026-11-17 (keyboard and each telnet session; `X28.SetNui`, bad one refused before
  connecting; `PadTelnetTests.TelnetPadStartedWithANui_ItsCallCarriesTheNuiFacility` reads C6 05 "CARD7" off the wire).
- DTEs (SINTRAN machines and others): either they send NUI selection themselves, or the port's subscription is the
  identity (6.21: agreed for a period of time). Whether SINTRAN's X.25 software can send NUI is not known; check the
  SINTRAN X.25 manual in NDInsight.
- Prove: an X.28 PAD session with `ID` and with `N`.

### B1b. Billing: what is left (X.25 6.22, 7.3.8; D.12)
A PVC has no Call Request and no Clear, so it carries neither NUI nor charging information; its billing identity is its
configuration.
- Annex F (read 2026-11-17): charging information by subscription is one of the facilities that may be tied to a NUI,
  through NUI override (6.21.2). Neither switch has NUI override, so `charging_info` is per port only.

### B1c. Accounts and billing read-out, the same in both switches (decided 2026-11-16)
Both switches read the same `[account.N]`, bill by NUI, count D.12 segments, write the same record file, have
`show x25 account` and `x25 account add NAME range START END | nui NUI` / `x25 account del NAME` (C# since 2026-11-17,
`SwitchCliTests.Accounts_*`; the C# account table is replaced whole on a change so the core never reads it half
changed). Both PADs do `ID` / `IDOFF` / `N<nui>-` with no echo; x25emu's `x25pad -n` gives a default NUI.
- Read out: the C# `GET_ACCOUNTS` answer is x25emu's ACCOUNT line field for field since 2026-11-17
  (`NmcApiTests.Api_GetAccounts_ByteExact`). The Status window shows the built-in switch's accounts (2026-11-17,
  `StatusAccountsTests`). An external switch's accounts are not shown, by Ronny's choice (2026-11-17): read them with
  `show x25 account` or x25nmc.

### B2. X.75 packets
The utility field itself is done (RetroIO `X75Utilities`, 2026-11-18, 17 tests from Table 21, Figure 21 and 5.4.3 of
X.75 1988): transit networks, call identifier, throughput class, window, packet size, clearing network; the rest
carried byte for byte. The call identifier has no byte
order to get right: its content is "under the responsibility of the originating network" (5.3.2 Note), so it is a
3-octet name that the other networks only carry and compare; all zeros means "not significant".
- Packets done 2026-11-18 (RetroIO `X75PacketTests`, 11 tests from 4.2 and Figures 7, 8): the builder takes the
  utility field for Call Request, Call Connected (always carries it, 5.4.1) and the extended Clear Request; the parser
  reads it with `ParsePacket(data, x75: true)`, and refuses a three-octet Call Connected and a Clear Confirmation
  longer than three octets (Figure 8).
- Read against x25emu 2026-11-18 (its `x25.h`, `x25switch.c`): the same codes (marker 00, transit 41, call identifier
  81), the call identifier's three octets kept in arrival order, the DNIC as two BCD octets. One difference: x25emu cites "Table 29/X.75",
  "Figure 8" and a 323-octet packet limit with no separate field maximum, and allows 64 octets; the 1988 text on disk
  has the codes in Table 21 and says 63 (4.2.1.5), and its length octet has 6 bits. x25emu's text is another
  edition, not on disk. Find which edition each switch should follow before B6.

### B3. STE port mode
A port with no DTE/DCE roles. Channel ranges and the collision rule are agreed in the configuration: a call collision clears
both calls with "network congestion". Sequence numbers modulo 8 (modulo 128 is out of scope, see the scope decisions).
Read 2026-11-18 in X.75 1988 section 3.1 and Table 13, for the packet layer (`X25Layer`, which today knows only
DTE and DCE):
- Collision (3.1.4): both calls cleared, cause "Network congestion" = 0x05 (Table 13). Which end searches channels
  from the lowest number and which from the highest is "agreed bilaterally" - a port setting.
- T31 200 s for a Call Request, T33 180 s for a Clear Request (3.1.2, 3.1.5); Annex D says what happens when T33 runs
  out - not yet read.
- Table 13 has no "local procedure error" cause, which the engine sends as a DCE today; what an STE sends instead for a
  procedure error (Annex C/E) is not yet read. A cause not in Table 13 is passed on or changed to 0x05 (4.2.3.1).
- Clear collision (3.1.7): no confirmation, the channel is free. Restart between STEs: its section not found or read yet.
- Every Call Request and Call Connected sent on the port carries the utility field (`X25PacketBuilder`'s
  `networkUtilities`), and every one received is parsed with `x75: true`.
- Prove: two switches as STEs over HDLC-over-TCP; a call crosses; a collision test clears both; the call identifier and
  the transit list arrive intact.

### B4. Between networks
Route by DNIC to the STE port; add our DNIC to the transit list only when we are a TRANSIT network; clear a call whose
list already holds our DNIC (loop). X.75 cause and diagnostic codes on refusal.
- Settled 2026-11-18 from X.75 1988 5.3.1: the list names "each transit network controlling the virtual circuit up to
  this point" (call request) and "each transit network" (call connected, and a clear request that answers the call
  request). X.75 keeps originating, transit and destination networks apart (5.3.2, 5.3.3, 5.3.14), so the network
  whose subscriber made the call does not name itself, and the destination network does not either.
- X.75 itself has no loop rule (the word does not appear). x25emu cites X.110 5.6 for it ("Transit networks will check
  routing information of each call to prevent circular routings"); X.110 is not on disk, so that quote is not checked.
  A loop is still caught with the 5.3.1 list: on a second pass a transit network finds itself.
- x25emu differs: `tools/x25switch.c` (the `sw_transit_add` call, about line 3257) adds its own DNIC to EVERY call
  leaving over a `trunk_kind = network` port, including one its own subscriber made. So x25emu's originating network
  sends a list with itself in it, where 5.3.1 says the list holds transit networks only. Not fixed - x25emu belongs to
  the session working on it; decide before B6.
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

### C1. Layer: what is left
Fixed 2026-11-16 (tests failed before): call and clear packets on a PVC get Reset "local procedure error" # 35 and the PVC
stays usable; every other procedure error on a PVC resets instead of clearing (Table C.4); window of 2 holds the third
packet; diag 36 on an unassigned channel. Proved in the switch 2026-11-17 (`SwitchPvcTests`): interrupt on a PVC both
ways (X.25 4.3.7), an interrupt during an outage reset "Out of order", resets from both ends. A reset collision on a
PVC (4.4.3.3) completes the reset with no confirmation, as DTE and as DCE (`R13_AResetCollisionOnAPvc_...`). Nothing
left in C1.

### PVC roles (Ronny 2026-11-16: "configure what a pvc client or server means - always follow the x25 spec")
X.25 has no client or server for a PVC: both DTEs are in data transfer from the start (4.2), and over LAPB the PVC is
simply there once the link is up. Only XOT has roles, set by RFC 1613 6.5: the XOT that opened the TCP connection is the
initiator and sends the PVC setup packet with status 0x00; the other is the responder, checks it against its own
configuration and replies with its own setup packet - status 0x00 is success ("An XOT PVC setup is successful if the
responder returns a status of 0x00"), 0x13..0x19 a refusal - or closes the TCP connection. After success each XOT
resets the PVC on its local interface. Configure it as the RFC names it (initiator / responder), on the
XOT port that carries the PVC, and refuse any other word.

### C2. Switch: what is left
Done 2026-11-16 in RetroIO (unit tests on loopback ports): `pvc_base`, `pvc_count`, `[pvc.N]` with `account =`, refused
for an unknown port, a channel outside the port's PVC range, PVC and call ranges overlapping, a channel used twice, and
an XOT port (needs C3); the join at start, billed to the account; data both ways with no call; port down resets the far
end "Out of order" (0x01) and never clears; data from the far end meanwhile is reset "Out of order"; port back resets it
"Remote DTE operational" (0x09); a reset crosses. Also fixed: the switch's congestion reset sent 0x05 (local procedure
error) instead of 0x07 (Table 5-8).
- Proved on real HDLC ports (LAPB over TCP, `HdlcPvcTests`): five PVCs and a call on one line, each with its own data;
  a lost line resets the PVCs, never clears them, and they carry data after the restart.
- Still to check: `show x25 pvc` and the counters with a real PVC; the whole path switch-to-switch through RetroX25.
- C# PVC billing records done 2026-11-17, the same rule as x25emu: a line every `[switch] pvc_record_minutes`
  (default 15, 0 off), and a line when a reset or an outage ends the period; the lines add up to the total; a message
  still open carries into the next period, a reset drops it. Hand-worked figures in `SwitchPvcTests`.
  The counting and the period line take the stitch's lock (fixed the same day); nothing under it calls a port, so it
  cannot wait on a port lock. No test forces the two threads to meet - none can make that moment happen on purpose.

### C3. XOT setup exchange: what is left
Done 2026-11-17: the setup packet itself (`XotPvcSetup` in RetroIO: build, parse, the responder's flow-control check
giving 0x00 / 0x18 / 0x19), 11 tests on bytes written from the RFC figure. The RFC does not say what goes in the
packet's GFI/LCN octets; the class keeps whatever is given. x25emu (`src/x25/xot_pvc.c`) writes GFI bits 0 and the
PVC's channel number there - that is x25emu's choice, not the RFC's; a router capture (C4) is still needed to know what
a Cisco sends and accepts.
- Configuration done 2026-11-17: port `iface_name` and `xot_pvc_role = initiator | responder` (any other word refused),
  `[pvc.N] remote_iface` and `remote_lci`. x25emu has an interface-name field but never reads it from its file - its
  own gap, to fix there with the same key names.
- Responder decision done 2026-11-17 (`XotPvcResponder`, 9 tests): 0x00, 0x13, 0x16, 0x17 (already joined, freed
  when the connection closes), 0x18, 0x19, 0x1A (unreadable, or initiator status not 0x00).
- `[pvc.N] remote_host` (the far XOT, RFC 1613 6.5) is read; port 1998 when none is given.
- Responder side in `XotSwitchPort` done 2026-11-17 (`XotPvcPortTests`, over a real socket): answers a setup, binds
  the PVC to that connection with the far end's channel on the wire, resets it "Remote DTE operational" (0x09), carries
  data; a refusal is sent and the connection closed; a lost connection resets the PVC "Out of order" (0x01), never clears.
- Initiator side done 2026-11-17 (`StartPvcSetup`): dials, sends the setup, joins on 0x00, tries again after
  `PvcSetupRetryMs` (5 minutes, the RFC's suggestion) on a refusal, a close without reply, or a lost connection. A PVC
  packet with no joined connection is not sent anywhere. Proved C# to C#: data both ways; refusal retried.
- Loader and switch done 2026-11-17: PVCs on XOT ports are accepted; an initiator end must have `remote_host`,
  `remote_iface`, `remote_lci` (refused otherwise); the switch starts the setup for an initiator end at start.
  A PVC with XOT at both ends is refused: one set of remote_* settings names one far XOT.
- Proved 2026-11-17: a whole switch (XOT responder port + loopback port) carries a PVC from a far C# XOT initiator.
- 0x14 "Destination interface is not up" done 2026-11-17: the C# switch answers it while the port of the PVC's other
  end is out of order (the same mark X.25 4.6 resets by). x25emu answers 0x14 when its own XOT port is inactive.
- Proved 2026-11-17 (`CrossEnvXotPvcTests`, CrossEnvLive): the C# initiator is answered 0x00 by the x25emu switch
  and the PVC takes data. Windows' 127.0.0.1 does not reach a WSL listener on this machine; the test dials WSL's own
  address. Not yet checked: the data coming out of x25emu's other PVC end.
- Proved 2026-11-17: x25emu as the initiator into the C# switch - `x25traffic -P 1 -I Serial1` (new PVC mode, see
  x25emu) is answered 0x00 and its 5 packets come out of the switch's other port. x25emu's switch is responder only (`xot_pvc_respond`); its initiator
  is the client library (`client_pvc_setup_send`, used by x25traffic, x25echo, x25pad). So: C# initiator to the
  x25emu switch, and x25traffic as initiator to the C# switch. The rest of the old line: the exchange in `XotSwitchPort` - a `[pvc.N]` end on an XOT port names the far XOT (host), the far
  interface name and LCN; the initiator dials and sends the setup, the responder checks interface (0x13), PVC (0x16),
  already connected (0x17) and flow control, replies or closes; then both reset the PVC locally. Retry after a refusal
  waits (RFC suggests 5 minutes). The loader then stops refusing PVCs on XOT ports.
- Prove: C# against x25emu in both directions; C# to C#.

### C4. Router
Read the Cisco page for `x25 pvc (XOT)` first (it could not be fetched; the syntax is not verified). Then a PVC to R1,
`clear xot`, router restart, and a frame-by-frame decode of the router's setup packet.
- Waits for: the Cisco syntax, and Ronny's agreement to change R1.

---

## D. Billing and call records

Standards read: X.25 (facilities 6.18 to 6.22, 7.3.6 to 7.3.8), X.75 (5.3.x), D.11, D.12. Not read: D.10, D.13, and no
real call record from an old network was found.

### D2. Record format v2: the columns still missing
Both switches write 23 columns (2026-11-16): the old 14, then connect time, connected duration (D.11 charges from Call
Connected), segments each way, account, NUI, reverse charging, who is charged, charging answer given. Still to add, on
the end: why the line was written (call end, PVC period, PVC reset - a DTE reset has cause 0, the same as a
period end, so today the line cannot tell them apart), facilities, clearing network, transit list. Document the format in `docs/` (part of the B1c billing guide).

### D2b. C# switch: telnet console login
Since 2026-11-17 RetroIO's `TelnetCliServer` asks for a `[user.N]` login (three tries) and checks the level, unless
`mgmt_open = 1` (now read and saved by the C# switch) - `Server_AsksForALogin_AndALevel0UserMayOnlyLook`. Still to do:
- RetroX25.Switch `Program.cs` already passes the users and `mgmt_open`, but builds only once RetroIO is packed to the
  local feed (the constructor changed).
### D3. C# switch: reverse charging refused
A call asking for reverse charging to a DTE that has not agreed to accept it is cleared with cause 0x19 (6.19). Needs a
per-port "accepts reverse charging" setting, in both switches.

### D4. Tariffs and bills (decided by Ronny 2026-11-17)
- One price file (`tariff.conf`), the same prices for every account: monthly fee, fee per call, price per time unit
  connected (D.11), price per 1000 segments (D.12); a second set for calls to another network (X.75); an off-peak
  discount by time of day. Whole minor units, no floating point.
- One C# piece in RetroIO reads the record file and the price file and works out the bill. The C# switch console
  prints it (`show x25 invoice <account>`), and the RetroX25 app shows it in a window laid out like a printed bill.
  x25emu only writes the record file, which the C# code reads.
- A call is "another network" when its called address (the calling one, if the called side pays) starts with a
  DNIC that is not ours (`home_dnic`): the record has no column for it, and the X.75 call identifier is kept by a call
  passing through, so it cannot tell. Those calls get their own part of the bill, with the call identifier on each
  line. No settlement between networks.
- Prove: unit tests against bills worked out by hand; one bill from a record file x25emu wrote.
- Built 2026-11-17: RetroIO `Switch/src/Billing` (price list, record reader, bill, printed layout), `show x25
  invoice`, `tariff_file`, the app's Bill window (Ctrl+7), column 24 (called side's account) in both switches;
  tests `InvoiceTests` (hand-worked bills, and one from lines x25emu wrote) and `InvoiceWindowTests`.
- Still to read: D.10 (general tariff principles) - not on disk. The price structure is from memory, not a source.

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

## F. After SINTRAN X.25 works: bring x25emu and the C# switch into step, then test all three

Ready since 2026-11-17: the SINTRAN K COSMOS X.25 link runs against the C# switch, SINTRAN as DTE and the
switch port as DCE (NDInsight `Installation/Communication/X.25/COSMOS-X25-OPTION-C00/README.md`, "The right
pairing"). Still to do first, on the SINTRAN side: PAD-to-TAD through the switch.

### F1. Port to x25emu what changed in the C# switch on 2026-11-16 (or show it is already right there)

- `hdlc-tcp` port: `tcp = listen | dial`, which end opens the TCP connection, separate from `mode = dte | dce`
  (not written = as before: DCE listens, DTE dials). C#: `PortTcpRole`, `PortConfig.TcpListens`,
  `SwitchConfigLoader`, `HdlcTcpSwitchPort`; tests `SwitchTcpRoleTests`.
- LAPB, ITU dialect: a DM answering our SABM clears "our SABM is waiting"; with crossing SABMs, T1 enters
  information transfer instead of sending the SABM again (X.25 2.4.4.1, 2.4.5.11.1). C#: `LapbLayer.cs`;
  tests `LapbItuLinkSetupTests`.
- LAPB station: "data transfer" only once the engine says the link is up. C#: `LapbStation.SyncState`;
  test `CrossingSabms_DataTransferOnlyOnceTheUaToOursArrives`.
- UA F bit copies the SABM's P bit (another session's change, same day). C#: `LapbLayer.cs`.
Proved by: the same cases as unit tests in x25emu, each vector taken from the X.25 text.

### F2. Thorough tests across all three

C# switch, x25emu and GNS3/Cisco, every pair in both directions, on XOT and on hdlc-tcp where the peer has it:
link set-up (one SABM, crossing SABMs, SABM refused with DM), keepalive polls over minutes, restart, calls,
data with sequence wrap, clear. Then SINTRAN X.25 against each of the three. Proved by: decoded captures of
every run kept with the results in `docs/crossenv-testing.md`.
