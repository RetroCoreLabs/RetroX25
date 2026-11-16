# Cisco IOS and X.25: setup options for real routers (25xx and similar)

This page is for putting RetroX25 against **real Cisco hardware**, for example a pair of 25xx routers.
It lists several ways to set the routers up, with the IOS configuration for each, what each one tests,
and how to check it.

**How much of this has been tested.** Read the label on each part:

| Label | Meaning |
|---|---|
| **Tested** | Run on our GNS3 bench (two simulated 7200 routers, IOS 12.4(24)T5). Details in `cisco-gns3-xot-test.md`. |
| **From Cisco** | Copied from a Cisco document (linked at the end). Not run by us. |
| **Not verified** | Our best understanding. **Not run, and not found in a source.** Check it on the router before you trust it. |

Nothing here has been run on a real 25xx. Where a 25xx may differ (interface names, memory, image
names) it says so.

## 1. What you need

### For every option

- A router with an IOS image that has **X.25** and **XOT** (X.25 over TCP). Cisco says XOT has been in IOS since
  9.21 and that the `xot` keyword in `x25 route` is for IOS 11.3 and later; older releases write `ip` instead
  of `xot` (**From Cisco**). Cisco's example page says "with any feature set" (**From Cisco**). Whether a
  given 25xx image in your hands has it is **not verified**: on the router, type `x25 routing` in
  configuration mode and `show x25 ?`. If the router says "% Invalid input", the image has no X.25.
- A way to reach the console (a rollover cable and a serial port on the PC, or a USB adapter).
- The PC that runs the RetroX25 switch, on the same IP network as the router. The router must reach
  **TCP port 1998** on the PC (XOT's port).

### Extra for the serial options (B, D)

- Two routers and a **cable pair** that makes one router the network side (DCE) and the other the terminal
  side (DTE). The DCE end supplies the clock. How the cables are wired, and which of your cables is which, is
  **not verified**: look at the cable label, and `show controllers serial 0` on the router says what it sees
  (DTE or DCE).

### Before you start: look at the router

Run these on each router and write the answers down. They decide what you can do.

```
show version
show flash
show ip interface brief
show controllers serial 0
```

| Look for | Why |
|---|---|
| IOS version and image name in `show version` | X.25 and XOT must be in it. The image name says the feature set. |
| DRAM and Flash sizes | A feature-rich image will not fit a small Flash or DRAM. A search found only general 2500 figures (8 MB Flash normal, 4 MB on some models, up to 16 MB; DRAM 4, 8 or 16 MB) and **no figure for X.25 or XOT images**. Use Cisco Feature Navigator or try the image. |
| Interface names | Cisco's own 2500 examples use `Serial0`, `Serial1` and `Ethernet0`. Your model may differ (**not verified**). Replace the names in the examples below with yours. |
| LAN port type | Some 25xx models have an AUI Ethernet port that needs a transceiver (**not verified** for your model). |

## 2. The four options at a glance

```mermaid
flowchart TD
    Q{"What do you want to test?"} --> A1["Router talks XOT to our switch,<br/>and you type at the router's PAD"]
    Q --> A2["Also test real serial X.25<br/>between two routers"]
    Q --> A3["Router keeps XOT healthy over a long run"]
    Q --> A4["Our side calls the router"]
    A1 --> A["Option A: one router, XOT only"]
    A2 --> B["Option B: two routers, serial cable"]
    A3 --> C["Option C: A or B plus keepalives and a fixed source address"]
    A4 --> D["Option D: calls arriving at the router"]
```

| Option | Routers | Cable between routers | Tests | Label |
|---|---|---|---|---|
| A. One router, XOT only | 1 | no | calls to and from our switch over XOT, the router's PAD, telnet to the PAD | Tested on the bench (R1 without the serial part) |
| B. Two routers, serial cable | 2 | yes | everything in A, plus real X.25 packet layer on a serial line, calls forwarded between line and XOT | Tested on the bench (R1 + R2) |
| C. Keepalives and source address | 1 or 2 | as A or B | a long run; what happens when the TCP connection dies | From Cisco |
| D. Calls that arrive at the router | 1 or 2 | as A or B | our side calls the router's own address or a host behind it | Tested (via R1 and R2) |

On every option the PC runs the same RetroX25 pieces; see `cisco-gns3-xot-test.md` section 6 and its
"five-minute check". The switch file is `src/demo/switch-cisco.ini`; change the `host` in `port.2` to your
router's address.

## 3. Option A: one router, XOT only

```mermaid
flowchart LR
    T["Telnet or console<br/>(you)"] --> R["Router<br/>PAD + XOT"]
    R -- "XOT, TCP 1998" --> S["RetroX25 switch<br/>on the PC"] --> E["x25echo"]
    P["x25pad / x25traffic"] --> S
    S -- "XOT, TCP 1998" --> R
```

This is the router from the bench without the serial part. Replace `FastEthernet0/0` with your LAN
interface, and the addresses with yours. **Tested** (on 12.4; syntax on an older 25xx image is **not verified**).

```
hostname R1
!
service pad to-xot
service pad from-xot
!
x25 routing
!
interface Ethernet0
 ip address 192.168.1.155 255.255.255.0
 no shutdown
!
x25 route ^2350 xot 192.168.1.180
x25 host R1 23600001
!
line vty 0 4
 no login
end
```

| Line | What it does |
|---|---|
| `service pad to-xot` / `from-xot` | Without them the router's PAD cannot call into XOT: `pad` says "Outbound pad-to-xot not allowed". **Tested.** On an old image the commands may not exist (**not verified**). Check with `service pad ?`. |
| `x25 routing` | Lets the router switch X.25 calls between its interfaces and XOT. |
| `x25 route ^2350 xot 192.168.1.180` | A call whose called address starts with 2350 goes by XOT to the PC. Cisco's own form: `x25 route ^xxxx xot a.b.c.d` (**From Cisco**). |
| `x25 host R1 23600001` | Gives the router's own PAD a calling address. **Tested.** |

**What calls to our switch need.** The switch must accept R1's call on a port that is a "trunk" and listens on
1998 (`port.1` in `switch-cisco.ini`), and have a route for the called address (23500001 goes to the echo
service). That is already in `switch-cisco.ini`.

**Test it** (full steps and expected output in `cisco-gns3-xot-test.md` section 8, tests A1 and A4):

1. On the router: `pad 23500001`, type `hello`, Enter. `hello` comes back from the echo service.
2. Leave the call: Ctrl-^ then `x`, then `disconnect` and Enter at the confirm.
3. From the PC: telnet to the router, then `pad 23500001`. (Telnet to the PAD works from user mode. **Tested.**)

## 4. Option B: two routers and a serial cable

```mermaid
flowchart LR
    S["RetroX25 switch<br/>on the PC"] -- "XOT" --- R1["R1<br/>x25 dce, clock<br/>address 23600001"]
    R1 -- "serial cable, X.25" --- R2["R2<br/>x25 (DTE)<br/>address 23600002"]
```

The bench layout. R1 is the same as in option A, plus the serial interface. R2 is an ordinary X.25 host.
**Tested** on 12.4. On a real pair, put the DCE end of the cable on R1 and set the clock there.

R1 (additions to option A):

```
interface Serial0
 no ip address
 encapsulation x25 dce
 x25 address 23600001
 clock rate 64000
 no shutdown
!
x25 route ^23600002$ interface Serial0
```

R2:

```
hostname R2
!
x25 host R2 23600002
!
interface Serial0
 no ip address
 encapsulation x25
 x25 address 23600002
 no shutdown
```

| Point | Detail |
|---|---|
| Which side is DCE | `encapsulation x25 dce` on one end, plain `encapsulation x25` on the other. Cisco's example puts `clockrate` (spelled as one word on some versions, two on others, **not verified** which your image takes) on the DCE interface only (**From Cisco**). |
| The clock | The DCE end sets it. If both ends say `dce`, or neither supplies a clock, the line stays down. Check: `show interfaces serial 0` should say "line protocol is up". |
| Speed | `64000` worked on the bench. On real serial, any speed the cable and port support may be used (**not verified** for your cable). |
| Address rule | Our switch calls R2 with a called address of 23600002; the route `^2360` on our switch sends it to R1, which routes `^23600002$` out of `Serial0`. |

**Test it:** tests B1 to B3 in `cisco-gns3-xot-test.md` section 8. In short: `x25pad` on the PC, `CALL 23600002`,
"Call accepted", then the R2 prompt.

**Calls from R2 to us:** on R2 type `pad 23500001`. R2 sends the call to R1 over the cable; R1 routes it by
XOT to our switch (**Tested**, test A2).

## 5. Option C: keepalives and a fixed source address

For a long run. All from Cisco's XOT keepalive document (**From Cisco**), not run by us.

```
x25 route ^2350 xot 192.168.1.180 xot-keepalive-period 10 xot-keepalive-tries 3 xot-source Loopback0
!
interface Loopback0
 ip address 10.255.255.1 255.255.255.255
```

| Setting | What Cisco says |
|---|---|
| Default | Keepalives are always on, the period is 60 seconds, and 4 tries are made. |
| `xot-keepalive-period 10`, `xot-keepalive-tries 3` | Check every 10 seconds, give up after 3 missed. |
| `xot-source Loopback0` | The TCP connection uses the loopback's address as source, so it survives if one of several physical interfaces fails. |
| When the TCP connection dies | The X.25 circuit is cleared as well. The router prints "XOT cx closed" and sends a Clear on the serial side. |

The router's own TCP keepalives (`service tcp-keepalives-in` and `-out`) appear in Cisco's example too
(**From Cisco**). Use them if a circuit stays "up" after the PC has gone away.

**Test it** (**not verified**, a suggestion): start a long call (`x25traffic ... -n 0 -r 10`, see
`cisco-gns3-xot-test.md` 9.3), then stop the switch on the PC. Expect the router to clear the circuit within
period times tries. Start the switch again and place a new call.

## 6. Option D: calls that arrive at the router

Our side can call the router's own PAD, or a host behind the router.

| Call from the PC | Goes to | Needs | Label |
|---|---|---|---|
| `x25pad`, `CALL 23600001` | R1's own PAD (prompt `R1>`) | the call reaches R1 over XOT: on our switch a route for `2360` to the trunk that dials R1 (`port.2`) | **Tested** |
| `x25pad`, `CALL 23600002` | the PAD of R2, through R1 and the cable | option B | **Tested** |
| `x25traffic -d 23600002` | R2 | Refused: a Cisco PAD wants the X.29 call user data (`01 00 00 00`) and x25traffic, a data generator, sends none. Use `x25pad`, which sends it itself | **Tested** (refusal measured) |

On our switch `port.2` is a "dialling" port: it connects out to the router's TCP port 1998 when a call needs it.
That is why the router's address is in `switch-cisco.ini` (`host` in `port.2`).

## 6b. Testing with the UI apps

The same tests can be done from the Desktop program instead of `x25pad`. **Not verified:** none of this
was run; the config file below was only written, not loaded. Tell us which step fails.

| Program | What it is | How to start |
|---|---|---|
| `RetroX25.Desktop` | Windows desktop application (Avalonia): PAD Client window, Packets window, Routes, XOT interface editor | `dotnet run --project RetroX25.Desktop` from `src`, in a Windows desktop session |

**The idea.** The Desktop app is a caller. It opens one XOT connection to our switch (caller port 0, TCP 2200)
and sends every call for 2350... and 2360... down it. The switch then does what it does for `x25pad`:
2350... goes to the echo service, 2360... goes to the Cisco router.

```mermaid
flowchart LR
    UI["RetroX25.Desktop<br/>PAD Client window"] -- "XOT client, TCP 2200" --> S["RetroX25 switch"]
    S --> E["x25echo 23500001"]
    S -- "XOT" --> R["Cisco router<br/>23600001 / 23600002"]
```

**Config.** `docs/config-samples/x25config-xot-to-switch.json` has the one XOT interface (mode `Both`: it dials out and also accepts)
(`127.0.0.1:2200`) and the two routes (`2350.*` and `2360.*` to it). Its own address is 99990002. If the
switch is on another PC, change `RemoteIPAddress`.

- **Desktop, with the file:** `dotnet run --project RetroX25.Desktop -- docs\config-samples\x25config-xot-to-switch.json`
  from `src`. The program takes the config path as its argument (**checked** in `Program.cs`); running this exact
  command was **not** tested for this page.
- **Desktop, by hand:** in the app, open Configuration → XOT Interfaces and add the same client interface (name
  `XOT_ToSwitch`, mode Both ways (peer), address `127.0.0.1`, remote port `2200`, local port `1999`), then Configuration → Routing Table and add the two
  routes. The configuration is not kept when the app closes; save it with Save... in the Configuration window to load it again.

**Calls to try**, in the PAD Client window:

| Call | Expect |
|---|---|
| `23500001` | The echo service's welcome text, then each line you type comes back. Type `exit` to leave. |
| `23600001` | R1's own PAD, prompt `R1>` |
| `23600002` | R2's PAD, through R1 and the cable (option B) |

Watch the **Packets** window while you do it: you should see the call request, the call connected and the
data packets in both directions.

**Two limits.** The switch and a UI app that runs an XOT *server* cannot both listen on TCP 1998 on the
same PC (two entries of the SAME app can share a port, see `xot-peers.md`; two programs cannot). And the Desktop's built-in TAD service (its TAD Server Monitor window only lists the sessions) answers calls that come to *it*: to test it from the Cisco,
stop the switch, point `x25 route ^2343 xot <PC address>` at the PC and call `23430200` (section 10 of
`cisco-gns3-xot-test.md`).

## 7. Check commands (all options)

| Command | Shows | Label |
|---|---|---|
| `show x25 route` | The routing table. Check the `^2350` line points at the PC. | **Tested** |
| `show x25 vc` | Open circuits: state, window and packet sizes, reset counts. Seen on the bench: a circuit stuck in `P6` with `Resets 1/0` after a window fault. The meaning of each `Pn` state was not looked up by us. | **Tested** |
| `show interfaces serial 0` | "line protocol is up" on the serial line (options B, D). | **Not verified** on 12.4; standard IOS |
| `debug x25 events` and `debug x25 xot`, then `show logging` | Every XOT packet with its sequence numbers. A reset shows cause and diagnostic. | **Tested** |
| `undebug all` | Stops debugging. Do this when finished. | **Tested** |
| `show tcp brief` | Whether the XOT TCP connection is up. | **Not verified** |

Reading a reset: `Cause 5, Diag 1 (Local procedure error/Invalid P(S))` means the peer sent more data packets
than the window allows. Cisco's default window is 2, and the call packet shows it as `Window sizes: 2 2`.

## 8. What can go wrong on real hardware

| Symptom | Likely cause | Label |
|---|---|---|
| `pad` says "Outbound pad-to-xot not allowed" | `service pad to-xot` missing | **Tested** |
| `pad` says "Destination unreachable; gateway or host down" | No `x25 route` for the address, or the PC is not reachable on TCP 1998 | **Tested** (route case); firewall case **not verified** |
| Serial line stays down | No clock (no DCE end, or no `clock rate`), wrong cable, or both ends the same kind | **Not verified** |
| The router accepts a call and then resets it | The called side sent too much data for the window | **Tested** (an old RetroX25 build did exactly this) |
| Image does not take the commands | Image has no X.25/XOT, or an older syntax (`ip` instead of `xot`) | **From Cisco** |
| Long run: circuit up but silent | Dead TCP connection that nobody noticed | **From Cisco** (keepalives) |

## 9. The automatic tests on two real 25xx routers

This is the same layout as the GNS3 bench on 2026-11-15, written for two real routers, so that the tests in
`RetroX25.Desktop.Tests` (class `X25emuPadToOurTadViaCiscoTests`) and `RetroX25.Tests` (class
`CiscoR1SwitchNegotiationTests`) run against hardware unchanged. The one question GNS3 could not answer (`PLAN.md`
A1a) is whether a real Cisco carries full speed in both directions at once; the emulated R1 used a whole laptop core and
let the way back run 3 to 5 times slower than the way out.

**Nothing in this section has been run on a 25xx.** The configs are the GNS3 ones (**Tested** on 12.4) with the 25xx
interface names from Cisco's own 2500 examples (`Ethernet0`, `Serial0`). A 2501 has one Ethernet (AUI on some models,
needs a transceiver) and two serial ports (**not verified** for your units).

![The Windows PC with x25emu and our switch, two Cisco 25xx routers on the LAN joined by a serial X.25 cable, and the two call paths the tests check](images/cisco-25xx-bench.svg)

### 9.1 The two configurations

Use free addresses on your LAN; the ones below are the bench's. 192.168.1.180 is the Windows PC.

R1:

```
hostname R1
service pad to-xot
service pad from-xot
no logging console
logging buffered 64000
x25 routing
!
interface Ethernet0
 ip address 192.168.1.155 255.255.255.0
 no shutdown
!
interface Serial0
 no ip address
 encapsulation x25 dce
 x25 address 23600001
 x25 win 7
 x25 wout 7
 clock rate 64000
 no shutdown
!
x25 route ^2350 xot 192.168.1.180
x25 route ^23600002$ interface Serial0
x25 route ^2343 xot 192.168.1.180
x25 host R1 23600001
!
line vty 0 4
 no login
end
```

R2:

```
hostname R2
no logging console
logging buffered 64000
x25 routing
!
interface Ethernet0
 ip address 192.168.1.156 255.255.255.0
 no shutdown
!
interface Serial0
 no ip address
 encapsulation x25
 x25 address 23600002
 x25 win 7
 x25 wout 7
 no shutdown
!
x25 route ^2343 interface Serial0
x25 host R2 23600002
!
line vty 0 4
 no login
end
```

| Line | Why it is there (all measured on the GNS3 bench) |
|---|---|
| `x25 win 7`, `x25 wout 7` on both serial ends | The serial default is 2. With 2/2, R1 kept only 2 or 3 packets unacknowledged towards R2 although the call asked for 7. A change waits for a restart of the line: `clear x25 Serial0` (it says "Parameter change held until a RESTART event"). |
| `x25 routing` on R2 | R2 must switch calls between XOT and the cable; with it on, R2 also accepts XOT on TCP 1998. |
| `x25 route ^2343 interface Serial0` on R2 | x25emu calls R2; R2 sends the call over the cable to R1. A Cisco never passes a call from XOT straight to XOT, so x25emu → R1 → PC alone is cleared with cause 13, diagnostic 64 (`cisco-learnings.md`). |
| `logging buffered 64000` | The packet debug is read with `show logging`; a small buffer holds only a fraction of a second at full speed (R2 on the bench kept 0.25 s). On a 25xx with little DRAM a smaller number may be needed (**not verified**). |
| `clock rate 64000` | The DCE end supplies the clock. On a real line this is the real line speed: 128-octet packets at full speed both ways need about 130 kbit/s each way, so 64 000 will be the limit on hardware (the emulator did not enforce it). Try the highest speed your cable and ports allow. |

### 9.2 Loading the configuration by script

The console is a serial port at 9600 8N1 on most Cisco routers (**not verified** for your units), and after the first
config `line vty 0 4` / `no login` lets a script use telnet instead. Either way the script only types lines and reads
what comes back, waiting for the prompt (`#` or `(config...)#`) before the next line. Pasting a whole config at once can
drop characters on a 9600 console; one line per prompt does not.

A sketch in Python (**not run**; it is the same send-a-line, wait-for-the-prompt pattern that drove the GNS3
consoles, with pyserial for the console port; `pip install pyserial`):

```python
import re, socket, sys, time
import serial   # pyserial; only for the console port

PROMPT = re.compile(rb"[>#]\s*$")

def open_line(where):
    if where.startswith("COM") or where.startswith("/dev/"):
        return serial.Serial(where, 9600, timeout=0.2)            # console cable
    host, port = (where.split(":") + ["23"])[:2]
    s = socket.create_connection((host, int(port)), 5)             # telnet to the vty
    s.settimeout(0.2)
    return s

def read(line):
    try:
        return line.read(4096) if hasattr(line, "read") else line.recv(4096)
    except (socket.timeout, TimeoutError):
        return b""

def send(line, text, wait=10.0):
    data = (text + "\r").encode()
    line.write(data) if hasattr(line, "write") else line.sendall(data)
    out, end = b"", time.time() + wait
    while time.time() < end:
        chunk = read(line)
        out += chunk
        if PROMPT.search(out):
            break
    return out.decode("latin-1")

# usage: python cisco_load.py COM3 r1.txt     or     python cisco_load.py 192.168.1.155 r1.txt
line = open_line(sys.argv[1])
send(line, "")                       # wake the prompt
send(line, "enable")
send(line, "terminal length 0")
send(line, "configure terminal")
for cmd in open(sys.argv[2]).read().splitlines():
    if cmd.strip() and not cmd.startswith("!") and cmd.strip() != "end":
        print(send(line, cmd), end="")
send(line, "end")
send(line, "clear x25 Serial0")      # makes x25 win / wout take effect
send(line, "")                       # answers the [confirm]
print(send(line, "write memory", 30))
print(send(line, "show x25 route"))
```

Telnet also sends option bytes (IAC sequences) at the start; IOS answers them and the script can ignore them. A raw
console needs none of that.

### 9.3 Check before the tests

| On | Command | Must show |
|---|---|---|
| R1 and R2 | `show interfaces serial 0` | "line protocol is up" and `input/output window sizes 7/7` |
| R1 | `show x25 route` | `^2350` and `^2343` to the PC, `^23600002$` to Serial0 |
| R2 | `show x25 route` | `^2343` to Serial0 |
| PC | `Test-Connection 192.168.1.155`, `.156` | both answer |

### 9.4 Running the tests

From `src` on the Windows PC (x25emu built in WSL, `X25EMU_TOOLS` is its `tools` folder):

```powershell
$env:CISCO_R1_HOST = '192.168.1.155'
$env:CISCO_R2_HOST = '192.168.1.156'
$env:X25EMU_TOOLS  = '/home/<you>/repos/os/x25emu/tools'
dotnet test tests\RetroX25.Desktop.Tests -nodeReuse:false --filter "FullyQualifiedName~X25emuPadToOurTadViaCiscoTests"
dotnet test tests\RetroX25.Tests -nodeReuse:false --filter "FullyQualifiedName~CiscoR1SwitchNegotiationTests"
```

The tests are marked Explicit, so they run only when named like this. One load case alone:
`--filter "Name~_FullSpeed20000"` (the case names are `_FullSpeed`, `_20PerSecond`, `_30PerSecond`, `_40PerSecond`,
`_16Calls`, `_TenMinutes`, `_TenMinutesAt40`, `_FullSpeed20000`, `_OneWayFullSpeed`).

| Test | GNS3 result (2026-11-15) | What to look for on hardware |
|---|---|---|
| `..._IsClearedByR1_...` (3 tests) | cleared, cause 13, diagnostic 64 | the same: it is Cisco's documented rule |
| `..._ThroughR2AndR1_OverTheSerialLine_Connects` | `CON`, TAD banner | the same |
| `_20PerSecond` to `_40PerSecond`, `_TenMinutesAt40` | 100% back | the same, if the line clock allows it |
| `_OneWayFullSpeed` | about 119 packets a second | the line speed or the router is now the limit; write down the rate |
| `_FullSpeed20000` | 100% back, but the last echo about 8 minutes after a 205 s send | **the answer to A1a**: does the way back keep up with the way out? Compare the x25traffic Duration with the test's total time |

### 9.5 Reading the packets if the way back is slow

The same method that found the window and the emulator limit on GNS3:

1. Start `_FullSpeed20000` with `$env:X25_TRUNK_TRACE = '90000-93000'`. The test then prints every packet on our trunk
   to R1 between 90 and 93 seconds, decoded (P(S), P(R), RR, RNR, REJ, the Q and D bits).
2. At about 90 seconds, on R1 and R2 at the same time: `clear logging`, `debug x25 interface Serial0`, wait 2 seconds,
   `undebug all`, then `show logging`.
3. Count, per router and direction, the data packets and RRs, and after each data packet a router sends, how many are
   unacknowledged. A window that is full on one side and not on the next shows which leg holds the traffic back.

On GNS3 this showed our side keeping to the window and acknowledging at once, and R1 taking our packets in at about 24
a second while its window towards R2 was open.

## Sources

- Cisco, "Configuring X.25 over TCP/IP (XOT)":
  https://www.cisco.com/c/en/us/support/docs/wan/x25-protocols/9363-x25-over-tcpip.html
  (IOS 9.21 or later, any feature set; the `xot` versus `ip` keyword; example configs on 2504, 2502 and 7010 routers.)
- Cisco, "XOT keepalives":
  https://www.cisco.com/c/en/us/support/docs/ip/x25-over-tcp-xot/21120-xot-keepalives.html
- Cisco, "X.25 PAD and XOT":
  https://www.cisco.com/c/en/us/support/docs/wan/x25-protocols/14188-x25-pad-xot.html
  (listed in search results; **not read** by us.)
- Cisco 2500 series memory figures (general only):
  https://en.wikipedia.org/wiki/Cisco_2500_series
