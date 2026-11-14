# Testing against the C emulator (x25emu) and the Cisco router

RetroX25 has a second X.25 implementation to test against: **x25emu**, a separate emulator written in C that runs in
WSL. Two programs written by different hands that agree on the bytes are a much better check than a program that
agrees with itself. This page says how to start the test network, what every address is, what the tests prove, and what
was measured about the other side. A short section at the end covers the real Cisco router.

## What you need

| Need | How to check |
|---|---|
| WSL with x25emu **built** (`make` in its folder; the test script never builds or changes that folder) | `wsl -e bash -c 'ls <x25emu folder>/tools/x25switch'` |
| The Windows firewall lets WSL dial in on TCP **2210** and **2211** | the tests say "x25emu cannot reach us" if it does not |
| The test project run from a program the firewall does not block | the Desktop test project is allowed; the `RetroX25.Tests` test host has a Block rule, so the tests where x25emu dials **in** live in `RetroX25.Desktop.Tests` |

## Run it

```powershell
# from the repository root; the argument is the x25emu folder INSIDE WSL
.\src\demo\crossenv-test.ps1 -X25emu <x25emu folder in WSL>
```

The script (`src/demo/crossenv-test.ps1`):

1. finds the address Windows uses to reach WSL (`wsl hostname -I`) and the address WSL uses to reach Windows (the WSL
   default route);
2. checks that the x25emu programs exist;
3. starts the x25emu test network in WSL (`src/demo/crossenv-wsl.sh`), holds it by the process number of its `wsl.exe`,
   and waits until its ports answer;
4. sets the variables below and runs every test of category `Interop` in `RetroX25.Tests` and
   `RetroX25.Desktop.Tests`;
5. stops everything it started, by process number (never by name), and checks that nothing is left.

Useful switches: `-CheckOnly` (start, run only the check that says in plain words what is missing, stop), `-NoStart`
(the network is already running), `-WslHost`, `-WindowsHost`, `-Filter`, `-OurXotPort`, `-OurHdlcPort`.

To run by hand, set the variables yourself:

| Variable | Meaning |
|---|---|
| `X25EMU_WSL_HOST` | The address Windows uses to reach the x25emu listeners. **Without it every Interop test is skipped.** |
| `X25EMU_TOOLS` | The folder in WSL that holds the built x25emu programs |
| `X25EMU_WINDOWS_HOST` | The address WSL uses to reach this machine (needed where x25emu dials us) |
| `X25EMU_WSL_DISTRO` | The WSL distribution (default distribution if unset) |
| `X25EMU_OUR_XOT_PORT`, `X25EMU_OUR_HDLC_PORT` | The Windows ports x25emu dials for calls to 2351 and 2352 (2210 and 2211) |
| `X25EMU_ACCESS_PORT` | Switch A's access port for callers (2001) |

### Networking facts that cost time before

- From Windows, WSL is reached by its **WSL address**, not `localhost`, and that address **changes after a reboot**.
- WSL reaches Windows by the WSL default route address. The firewall must let it in on 2210 and 2211.
- Start the Windows side first when WSL has to dial in: WSL dials out, and a refused dial is not retried by a timer.
- `X25EMU_WSL_HOST=<wsl address>` is all a hand-run needs on the Windows side.

## The network and its addresses

x25emu has three switches chained together (A, B, C). We add ports to Switch A and send calls through them. The
addresses are X.121 numbers; the first four digits are the network (the "DNIC").

| Network | Address range | What is there |
|---|---|---|
| x25emu Switch A | 2340 | Port 0: access port 2001, where callers connect (`x25pad`, `x25traffic`). Port 3: access port 2007, where **our** endpoint connects in; calls to 2343 go to it. Port 4: XOT trunk, x25emu dials **us** on 2210; calls to **2351** go out here. Port 5: `hdlc-tcp` trunk, we dial in on 2010; calls to **2350** go out here. Port 6: `hdlc-tcp` trunk, x25emu dials **our** listening port 2211; calls to **2352** go out here. Port 7: XOT trunk, our XOT peer entry dials in on 2011; calls to **2353** go out here. |
| x25emu Switch B, C | 2341, 2342 | Chained behind A. The x25emu echo is on Switch C (port 2006), address **23420001**. |
| RetroX25 (Windows) | 2350 to 2353 | The ports above end in our switch, our XOT peer entry, or the Desktop app (its built-in TAD answers one address, the one named as the app's own machine id). |
| Management | | Switch A monitor port 4101. |

Calls into 2343 must come in on the **access** port 2001. Measured: sent into the trunk port 2011 the Call Request
reaches Switch A but is not delivered to the endpoint on port 3.

## What the tests prove

Everything here is in the test projects, category `Interop`. A test that needs the network checks it first and says in
plain words what is missing.

| Question | Test (in `tests/RetroX25.Tests/Interop` unless noted) | Result |
|---|---|---|
| Does the environment work in both directions? | `X25emuEnvironmentTests` | WSL reaches us, we reach WSL |
| Does our client call the x25emu echo, first call on a fresh connection included? | `OurClient_CallsX25emuEcho_AndTheFirstCallOnAFreshConnectionWorks` | yes |
| Does the x25emu echo take calls in a row and at once? | `X25emuEcho_TakesCallsInARow_AndAtTheSameTime` | in a row yes; at the same instant see below |
| Does x25emu's traffic tool reach our echo with every packet? | `X25emuTraffic_CallsOurEcho_AndEveryPacketArrives` | yes |
| Does our monitor client decode x25emu's switch topology? | `OurMonitorClient_DecodesX25emuSwitchTopology` | yes |
| Does our PAD reach the x25emu echo through its three switches? | `OurPad_ReachesX25emuEcho_ThroughItsThreeSwitches` | yes |
| Does x25emu's PAD reach our echo through our switch, counted on both sides? | `X25emuPad_ThroughOurSwitch_ReachesOurEcho_AndTheCallIsCountedOnceOnEachSide` | yes |
| Packet sizes 16 and 128, windows 1 to 7, modulo 8: every packet byte for byte? | `PacketSizes16And128_WindowSizes1To7_Modulo8_EveryPacketArrivesByteForByte` | yes |
| Q bit, M-bit chains, D bit | `QBit_MBitChain_DBit_BytesAreEqualAtTheFarEnd_AndTheBitsAreMeasured` | bytes equal at the far end |
| M-bit chain and D bit, our client library to x25emu's echo | `OurChainedDBitBurst_ToX25emuEcho_ComesBackWholeAndInOrder` (`X25emuBitsTests`) | 400 packets back, each once and in order, M on 3 in 4, D on all, all acknowledged |
| M-bit chain and D bit, our client library through x25emu to our echo | `OurChainedDBitBurst_ThroughX25emu_ToOurEcho_ArrivesWholeAndTheBitsAreKept` (`X25emuBitsTests`) | the wire tap decodes both directions (M on 3 packets in 4, D on all, numbers in order) and the echo's counts say the same |
| Clear causes and reset | `ClearCauses_AndReset_AreCarriedOnAndDecoded` | carried on and decoded |
| Does x25emu's trace tool decode what we send? | `X25trace_DecodesTheCallRequest_TheDataAndTheClear_WeSend` | yes |
| `hdlc-tcp` trunk, our end dialling its port | `X25emuHdlcTrunkInteropTests` (in `tests/RetroX25.Tests`) | calls both ways, 3000 packets each way, link loss at either end and recovery |
| Our XOT peer entry calls x25emu, twice in a row, one TCP connection each | `OurPeerEntry_CallsX25emuEcho_TwiceInARow_OneTcpConnectionPerCall` (Desktop.Tests) | yes |
| x25emu's PAD calls our app through its XOT trunk | `X25emuPad_CallsOurApp_ThroughItsXotTrunk_AndOurTadAnswers` (Desktop.Tests) | yes |
| x25emu's PAD calls our app through its `hdlc-tcp` trunk, which **dials our port** | `X25emuPad_CallsOurApp_ThroughItsHdlcTcpTrunk_WhichDialsOurListeningPort` (Desktop.Tests) | yes |
| 300 packets through its `hdlc-tcp` trunk, counted exactly by our app | `X25emuStream_ThroughItsHdlcTcpTrunk_IsCountedExactlyByOurApp` (Desktop.Tests) | yes |
| Both sides call at the same time over one peer entry | `BothSidesCall_AtTheSameTime_BothCallsCarryTextAndClearCleanly` (Desktop.Tests) | yes |
| TCP connections for 1, 2 and 10 calls, seen from both machines | `OurCalls_OneTwoAndTenAtOnce_...` and `X25emuCalls_OneTwoAndTenAtOnce_...` (Desktop.Tests) | one TCP connection per call; see below |
| The far end is killed and comes back | `FarEndKilledAndRestarted_...` (Desktop.Tests) | the drop, the failed dial with its reason and the redial are all reported |
| Volume: both implementations send at once | `TrafficFromBothImplementations_AtOnce_EveryCounterAddsUp_AndNothingIsLeftOpen` (Desktop.Tests) | every counter adds up; nothing left open |

### The volume test in numbers

One stream of 2000 packets each way and a storm of 8 calls with 3 packets each, from our tools to x25emu and from
x25emu's tools to us, all at once. The test compares what each side sent with what the other received (2024 data
packets, 9 calls arriving on each side), checks the x25emu monitor counters on ports 0, 3, 4 and 6, requires no error
line in our log and no circuit or connection left open, and fails if it takes more than 150 seconds. It finished in
about 10 seconds (10 repeated runs, all passed).

## What was measured about x25emu

These are facts from runs against the real x25emu, not documentation.

- **One TCP connection per call.** Calls from us: 1, 2 and 10 calls at once gave exactly 1, 2 and 10 established
  connections, the same number in Windows, in WSL (`ss`) and in our own report. Calls from x25emu's XOT trunk: it dials
  one connection per call and also keeps **spare** connections open to us for the next call (0 to 7 seen; x25emu decides how many, so the tests report the number and do not limit it). The spares
  carry no circuit.
- **x25emu answers on its own channel number** (the channel number has no meaning on XOT, RFC 1613 section 6). It
  drops channel-0 packets that arrive over XOT. Our XOT code therefore sends no Restart over XOT (RFC 1613 section 6.4)
  and translates channel numbers.
- **Several calls on one TCP connection:** its access port answers **only the first**. Our `Client` XOT entry, the client
  library (`X25Client`) and so `x25traffic -C` therefore use one TCP connection for each call (RFC 1613 section 5).
- **Ten calls started in the same instant** through its XOT trunk toward us: 6 and 10 of the 10 were answered in two
  runs. It stitches some calls without a Call Request, and the same channel number appears twice in its log. Our side
  stayed clean in both runs: no error line, nothing left open.
- **One fresh x25emu echo, calls started in the same instant:** 2 of 2, 3 of 4 and 5 of 8 were answered. The first
  call started is often the one that is not answered ("no Call Accepted within 10 s").
- **Three calls in a row, 2 seconds apart:** all work.
- **Clears are counted once per call, on the port where the Clear Request arrived.** Our switch counts a clear **once
  per port the call touched**. Both are written down so the two sets of monitor numbers are not compared blindly (see
  [trunk-links.md](trunk-links.md)).
- **Delivery 0 % in its stream tool from our built-in TAD.** Our TAD answers like a PAD (it echoes lines with a
  comment), not byte for byte, so the tool's "delivered" figure is zero. Only the counts are compared.

### What our app does not do (found while testing)

- **It does not forward a call from one interface to another.** A call that arrives on a peer interface for an address
  that is routed back out of the same interface is cleared as "not routed". Use the switch (`x25switch`, or the built-in
  switch) for forwarding.
- **The built-in TAD answers only one address**, the one set as the app's own machine id, not every address in a route
  pattern.

### Two defects the tests found and fixed in RetroIO

- Data for one call arriving on several threads at once corrupted the TAD's line buffer ("Index was out of range").
- The LAPB station could lock up when a handler of received data sent data while the station was also receiving
  (two locks taken in opposite order). Events are now raised after the lock is released.

Both have a test that failed before the fix.

## Not tested

- **x25emu's serial trunk, and our serial trunk against anything real.** WSL here has no `socat`, and there is no
  virtual serial pair on the Windows side, so no serial device could be opened for a test. Serial is tested over an
  in-memory cable only (see [trunk-links.md](trunk-links.md)).

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Every Interop test is skipped | `X25EMU_WSL_HOST` is not set. Use the script, or set it. |
| "x25emu cannot reach us" | The firewall blocks TCP 2210 or 2211 from WSL, or `X25EMU_WINDOWS_HOST` is wrong. |
| A port in the x25emu network does not answer | The network is not running or has stopped. `crossenv-test.ps1 -CheckOnly` names the missing port. |
| It worked yesterday and not today | The WSL address changed after a reboot. Let the script find it again. |
| Tests that need x25emu to dial us fail inside `RetroX25.Tests` | That test host is blocked by a firewall rule on purpose. These tests live in `RetroX25.Desktop.Tests`. |

## The real Cisco router

Everything learned about the router is summarised in [cisco-learnings.md](cisco-learnings.md).

The tests against the real router R1 are marked `CiscoLive` and **Explicit**: they run only when named, and need the
variable `CISCO_R1_HOST` (the router's LAN address). They send test traffic to the router and change nothing on it. The
results, decoded in the order the bytes arrived, are in [xot-peers.md](xot-peers.md). The set-up of the router is in
[cisco-gns3-xot-test.md](cisco-gns3-xot-test.md).

```powershell
$env:CISCO_R1_HOST = '<router address>'
dotnet test src\tests\RetroX25.Tests\RetroX25.Tests.csproj --filter "FullyQualifiedName~CiscoR1MeasurementTests"
dotnet test src\tests\RetroX25.Desktop.Tests\RetroX25.Desktop.Tests.csproj --filter "FullyQualifiedName~CiscoR1PeerTests"
```

### x25emu, our switch and the router together (checked 2026-11-13)

`demo/switch-cisco-x25emu.ini` is `switch-cisco.ini` plus two ports for x25emu (a trunk x25emu dials on 2210, and a trunk
we dial to Switch A's access port 2001; its `host` is the WSL address, which changes after a reboot).
`demo/crossenv-wsl.sh` gives Switch A a route for 2360 out of its XOT trunk, so a call to the router leaves through us.
Start the network with `crossenv-wsl.sh`, then `x25switch demo\switch-cisco-x25emu.ini`. Then:

- x25emu to the router: in WSL x25emu's `x25pad -x 127.0.0.1:2001 -a 23400001`, type `CALL 23600001`, get `R1>` (x25emu's PAD has to send the X.29 call user data by itself, as ours does; if the router refuses the call, that is the first thing to look at).
- The router to x25emu: attach an x25emu echo to Switch A's endpoint port (`x25echo -x 127.0.0.1:2007`) and on the router
  type `pad 23430001` (the router's existing `^2343` route already points at this PC). Without the echo attached,
  x25emu clears the caller after 5 seconds with cause 1, diagnostic 71.

The router's console needs no change for either.

## The Desktop windows against x25emu

`tests/RetroX25.Desktop.Tests/Interop/X25emuWindowsTests.cs` (needs the same variables as the other cross-environment tests):

| Question | Test | Answer |
|---|---|---|
| Do two peers with different sizes put their own sizes in the Call Request? | `TwoPeers_WithDifferentSizes_PutTheirOwnSizesInTheCallRequest` | yes, read from the wire bytes and from the Packet window |
| Does the PAD window reach x25emu's echo, does the XOT panel list the peer, does the Packet window decode x25emu's frames? | `PadWindow_CallsX25emuEcho_TheXotPanelShowsThePeer_AndThePacketWindowDecodesItsFrames` | yes |
| Does an x25emu PAD reach our built-in TAD through x25emu's 2343 route? | `X25emuPad_CallsOurBuiltInTad_ThroughItsTwoThreeFourThreeRoute` | yes: the built-in switch keeps a spare connection to x25emu's access port 2007 and x25emu hands the call to it |
| Does a telnet PAD reach x25emu through our built-in switch? | `TelnetPad_ReachesX25emu_ThroughOurBuiltInSwitch` | yes |

Found by the last test: x25emu answers a call on its own channel number (our switch sent the Call Request on channel 16,
x25emu's Call Accepted came back on channel 1; RFC 1613 section 6 says the number has no significance). The switch port
did not accept that and cleared the call at once with diagnostic 20. It now binds the peer's number to the one call of a
connection dialled for it (`ADiallingPort_ConnectsTheCall_WhenThePeerAnswersOnAnotherChannelNumber` in RetroIO).
