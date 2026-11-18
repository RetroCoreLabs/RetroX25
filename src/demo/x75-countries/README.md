# Three countries over X.75: Norway, England, Germany

Three X.25 networks joined by X.75 links, each a real `x25switch` process with its own ini file:

```text
 PAD users                                                              Altos chat
 (x25pad)                                                               (x25service)
    |                                                                        |
 Norway 2422  ==X.75==>  England 2342  ==X.75==>  Germany 2624 --------------+
    |                       |                        |
 echo 24229001           echo 23429001            echo 26249001
```

A PAD user in Norway calls `026245890040004` (the leading `0` is Norway's international prefix) and lands in
the Altos CHAT 3.1 conference in Germany, through England. Each country also has an echo service, so every
hop can be tested on its own.

The unit test `X75CountriesScriptTests` (in `tests/RetroX25.Tests`) starts this same folder with this same
script and these same files, then dials in. What you start by hand is what the test tests.

## Files

| File | What it is |
|---|---|
| `desktop-norway.json` | The desktop app as one more PAD user on Norway's line (see "The desktop app as a PAD user") |
| `norway.ini` | Norway's switch: PAD users' line (NUI required), echo, X.75 to England, the NUI accounts |
| `england.ini` | England's switch: X.75 from Norway, X.75 to Germany, echo |
| `germany.ini` | Germany's switch: X.75 from England, the Altos host line, echo |
| `altos-chat.json` | The Altos chat service (`x25service`), dialling Germany's Altos line |
| `x75.ps1` | Start, stop, status, PAD, management console, call records, logs |
| `logs/` | Made on start: one log per program, and each switch's call records (not in git) |

## Build first

The script builds nothing. Build once from `src`:

```powershell
./x75.ps1 build          # takes the newest RetroIO packages, builds, tests, publishes to src/publish/win-x64
```

`./x75.ps1 build` is `dotnet restore --force --no-cache` followed by `src/build.ps1` (which you can also run
yourself, `-SkipTests` to leave the tests out). It refuses while the network runs, because a running program
cannot be replaced. `./x75.ps1 start` refuses programs published before the newest source, so an old build is
never mistaken for a fix that does not work.

The script runs `x25switch.exe`, `x25echo.exe`, `x25service.exe`, `x25pad.exe` and `x25monitor.exe` from
`src/publish/win-x64`. Another folder: `-BinDir <folder>`.

## Start, stop, status

```powershell
cd src/demo/x75-countries
./x75.ps1 start      # Germany, England, Norway, the Altos chat, then the 3 echo services
./x75.ps1 status     # each process id (running / EXITED) and each TCP port (listening / NOT listening)
./x75.ps1 stop       # stops exactly the processes it started, by process id
```

`start` waits until each switch listens before starting the next one, and stops everything again (with the
failing program's output) if one does not come up. It refuses to start twice: run `stop` first.

It stays up until you run `stop`. Logs and call records stay in `logs/` after `stop`.

## Every address (NUA)

Dial from Norway. A number for another country is dialled with the prefix `0`; a number in Norway is dialled
without the DNIC (the national number).

| Address (international) | Dial from Norway | What answers | Where |
|---|---|---|---|
| 26245890040004 | `026245890040004` | Altos CHAT 3.1 (the conference) | Germany port 1, `x25service` |
| 2624589004xxxx (other) | `02624589004xxxx` | Routed to the Altos host; the chat answers only 0004, so the others are cleared (to verify) | Germany port 1 |
| 26249001 | `026249001` | echo: sends back each line you type | Germany port 2, `x25echo` |
| 23429001 | `023429001` | echo | England port 2, `x25echo` |
| 24229001 | `9001` | echo | Norway port 1, `x25echo` |
| 24220000 | `0000` | Norway's in-band management (`x25nmc`) | Norway switch |
| 23420000 | `023420000` | England's in-band management | England switch |
| 26240000 | `026240000` | Germany's in-band management | Germany switch |
| 24221xxx | `1xxx` | Norway's hosts (one host line for all of them) | Norway port 3, TCP 25003 |
| 23421xxx | `023421xxx` | England's hosts | England port 3, TCP 25013 |
| 26241xxx | `026241xxx` | Germany's hosts | Germany port 3, TCP 25024 |

Any other number in 2624… goes to Germany and is cleared there (no route); 2342… to England; any other DNIC
is cleared in Norway (no route).

The PAD users get their address from the network, as on a real public network: Norway writes
`24227000` + the call's channel number into every call on its PAD line (`calling_address_base` in `norway.ini`),
whatever address the PAD sent. So callers show up as `24227001`, `24227002`, ... in the chat's `.s` list, in
the call records and in the other countries. Two calls at the same time never share an address; the same user
may get another one on the next call, as a dial-in user got another port of the network's PAD.

The address and the NUI are two different things. The address says where a call comes from and reaches the
called machine. The NUI only tells Norway who pays and never leaves it: X.25 (10/96) 6.21, "Network user
identifier is never transmitted to the remote DTE".

## Every NUI (who pays)

Norway's PAD line has `nui_required = yes`: every call must name a NUI (X.25 6.21.3). Each NUI is its own
account in `norway.ini`, so its calls and charges are counted apart.

| Test user | NUI | Account | Start a PAD as this user |
|---|---|---|---|
| 1 | 242226500000101 | uit-1 | `./x75.ps1 pad 1` |
| 2 | 242226500000102 | uit-2 | `./x75.ps1 pad 2` |
| 3 | 242226500000103 | uit-3 | `./x75.ps1 pad 3` |
| 4 | 242226500000104 | uit-4 | `./x75.ps1 pad 4` |
| none | - | - | `./x75.ps1 pad nonui` (every call is refused) |

Add a user: a new `[account.N]` with a `name` and a `nui` in `norway.ini`, then `stop` and `start`. The NUI
never leaves Norway: it is not sent on to England or Germany (X.25 6.21).

## Using the PAD

`./x75.ps1 pad 1` runs `x25pad -x 127.0.0.1:25001 -n 242226500000101`: an X.28 PAD on Norway's PAD line,
sending NUI 242226500000101 with every call. Run `x25pad` directly for other options:

```text
x25pad [-x host:port] [-n nui] [--telnet-port N [--telnet-bind addr]] [-v]
```

With `--telnet-port 2323` the PAD listens for telnet instead (`telnet 127.0.0.1 2323`), so any terminal
program can be the user.

At the `*` prompt:

| Command | What it does |
|---|---|
| `CALL 026245890040004` (or `C 0262…`) | Place a call |
| `CLR` | Clear the call |
| `STAT` | The state of the call |
| `SET 2:0` | Set an X.3 parameter (here: echo off) |
| `HELP` | The commands |

Ctrl+C ends `x25pad`.

## The Altos chat

Open two PADs, as two users, and call the chat from both:

```text
./x75.ps1 pad 1        ./x75.ps1 pad 2
* CALL 026245890040004  * CALL 026245890040004
```

Each gets the 1987 welcome, `Waiting for connection.`, a pause of `WaitPauseMs` (3 s in `altos-chat.json`),
`Connection established.` and `+++guest+++`. What one types, the other reads as `guest says: <text>`. The
chat does not echo your own line back (the PAD does). `.h` is help, `.x` leaves (the others see
`---guest---`). It takes up to 30 users (`MaxUsers`).

When you are connected, the chat sets up your PAD for typing, the way a host did it on the real networks: one
X.29 "set" message at the start of every call. From then on, during this call only:

- the PAD echoes what you type, and Enter echoes as a new line (X.3 parameter 13 = 4), so the replies no longer
  print over your own line;
- the line is sent when you press Enter, not key by key;
- you can edit the line before you send it: DEL deletes a character (shown as backspace, space, backspace),
  Ctrl+X deletes the whole line, Ctrl+R shows it again.

When the call ends, for any reason, the PAD goes back to the settings it had before the call, so the next call
starts the same way whatever the chat changed. Settings you made yourself with `SET` at the `*` prompt before the
call are kept. No X.25 standard says what a PAD must do here; this is our choice. Both PADs work this way, the
C# one and x25emu's. The message's exact bytes are in `src/docs/altos-chat.md`.

Whispering works exactly as in the 1987 program, which surprises people:

- `!` takes the **place number** from `.s` (the first column), not a name: `!3 hello` goes to place 3. A name
  after `!` reads as the number 0, which is usually yourself, so you get your own whisper back.
- The receiver sees the **whole line as typed**: `ronny whispers: !3 hello`.
- Nothing tells you when the place is empty; the whisper simply goes nowhere.

Other commands: `.n name` changes your nickname, `.c 5` moves you to channel 5, and `.p word` puts a password on
your channel when you are alone on it.

## The desktop app as a PAD user

The desktop app (`RetroX25.exe`, the Avalonia program) can join the network as one more PAD user on Norway's
PAD line, the same line `x25pad` uses. `desktop-norway.json` in this folder is its configuration: one XOT
entry that dials Norway's port 0 (TCP 25001) for each call, and one route that sends every call there.

1. Start the network: `./x75.ps1 start`.
2. Start the app on the file, from this folder:

   ```powershell
   ..\..\publish\win-x64\RetroX25.exe desktop-norway.json
   ```

   Or start it as usual, then **Tools → Configuration → Load...**, pick `desktop-norway.json`, **Apply**.
3. Open the **PAD Client** window (Ctrl+5).
4. Give a NUI. Norway refuses every call without one, cleared with Local procedure error, diagnostic 84.
   Either type it once for the session:

   ```text
   ID242226500000103
   026245890040004
   ```

   or put it in the call itself (X.28 3.5.15): `N242226500000103-026245890040004`. The NUI is not echoed while
   it is typed. Take a NUI of its own (uit-3 or uit-4) so the app's calls are counted apart from the
   `x25pad` users in `show x25 account`.
5. The chat answers as in "The Altos chat" above: talk to an `./x75.ps1 pad 1` user in another window.

The **Packets** window (Ctrl+2) shows the CALL REQUEST with the NUI facility, the CALL CONNECTED and every
DATA packet as you chat. Every other address in "Every address (NUA)" can be called the same way.

### The same setup by hand, in the app's windows

Without the file, starting from the app's own defaults. Everything is in **Tools → Configuration** (Ctrl+,):

1. **X.25** tab: **Local Machine ID** = `24227001` (the app's own address on Norway's network).
2. **XOT Interfaces** tab → **Add**:

   | Field | Value | Why |
   |---|---|---|
   | Interface Name | `Norway PAD line` | Any name; the route below points at it |
   | Interface Enabled | on | |
   | XOT Mode | **Client** | The app only places calls; Norway's PAD line (port 0) listens |
   | Remote IP Address | `127.0.0.1` | The computer the network runs on |
   | Remote Port | `25001` | Norway's PAD line |
   | Remote Machine ID | `24220000` | Norway's switch (only a label here) |
   | Window size / Packet size | empty | Use the X.25 tab's values (2 and 128, as Norway) |
   | TCP Keepalive Enabled | on | |

   OK.
3. **Routing** tab → **Add**: *What do you want to reach?* = **Everything else (default gateway)**,
   Name `Everything to Norway`, *Send the calls to* = `Norway PAD line`. OK.
   Every number then goes to Norway, which routes it on; there is no need for a route per country.
4. **Apply**. The engine restarts on these settings; open windows from before close.
5. **Save...** to keep it (the app does not remember settings between runs). Saved over
   `desktop-norway.json`, it is the same file.

Then the PAD window as in steps 3 to 5 above.

### Watching the network in the monitor app

`x25monitor` (or `./x75.ps1 monitor`) shows the switches themselves, not the desktop app: add an endpoint
`127.0.0.1` port **4201** (Norway), **4202** (England) or **4203** (Germany) and **Connect**. Each switch names its
neighbours, so one connection draws all three on the map. The **Packet Analyzer** then shows the call from the
desktop app crossing Norway, the X.75 links and Germany; **Link Status** shows each port's counters, including the
desktop app's connection on Norway's PAD line.

So a full GUI session is: `./x75.ps1 start`, the desktop app on `desktop-norway.json` for typing, `x25monitor` on
4201 for watching, and an `./x75.ps1 pad 2` window as the other chat user.

The app sends `24227001` as its own address (`X25.LocalMachineId` in the file), but Norway's PAD line replaces
it like any other PAD's: the chat and the call records show `24227000` + the call's channel.

Not yet checked by hand. The file uses the same kind of XOT entry as `altos-chat.json`, and the PAD window
runs the same X.28 code as `x25pad`, which the script tests use. No test starts the app on this file yet.

## Management consoles

```powershell
./x75.ps1 cli norway      # telnet localhost 9201   (england 9202, germany 9203); login admin / admin
```

| Command | What it shows |
|---|---|
| `show info` | The switch: name, DNIC, uptime |
| `show x25 ports` | Each port: up or down, its channels |
| `show x25 route` | The routing table |
| `show x25 vc` | The calls up now, port and channel on each side |
| `show x25 stats` | Packet and call counters |
| `show x25 account` | Each NUI account: its calls and use (Norway) |
| `show x25 invoice uit-1` | The bill for one account (`[yyyy-mm]` for another month) |
| `show running-config` | The configuration as the switch holds it |
| `clear x25 vc <port> <lci>` | Clear one call by hand |

## Call records and usage

```powershell
./x75.ps1 calls          # logs/norway-calls.txt, england-calls.txt, germany-calls.txt
```

Each switch writes one line per finished call to its `cdr_file`: who called whom, how long, the data
counts, and in Norway the paying NUI. Together with `show x25 account` and `show x25 invoice` on Norway's
console, that is the usage per test user.

## Watching it run

| How | What |
|---|---|
| `./x75.ps1 logs norway` | Follow one program's log (norway, england, germany, altos-chat, echo-norway, ...) |
| `./x75.ps1 monitor` | The monitor window (`x25monitor`); connect it to 127.0.0.1 port 4201, 4202 or 4203. Each switch names its neighbours, so the map shows all three |
| `x25switch <ini> -v -v` | Every packet in hex in the log. To use it, start that one switch by hand instead of with the script |

## Ports

| TCP port | What listens |
|---|---|
| 25001 | Norway: PAD users (XOT) |
| 25002 | Norway: echo line (XOT) |
| 25011 | England: X.75 from Norway (LAPB over HDLC on TCP) |
| 25012 | England: echo line (XOT) |
| 25021 | Germany: X.75 from England (LAPB over HDLC on TCP) |
| 25022 | Germany: Altos host line (XOT) |
| 25023 | Germany: echo line (XOT) |
| 9201 / 9202 / 9203 | Management console: Norway / England / Germany (telnet) |
| 4201 / 4202 / 4203 | Monitor stream: Norway / England / Germany |

## What to check by hand

Checked by the unit test as well: 1, 2, 3, 4 and 6. The rest is for a person to run and look at.

1. **The chat across three countries.** Two users call `026245890040004` and see each other's lines.
2. **Every hop on its own.** `CALL 9001`, `CALL 023429001`, `CALL 026249001`: each echoes your line back.
   If Germany fails and England works, the fault is on the England-Germany link.
3. **No NUI.** `./x75.ps1 pad nonui`, any call: cleared, cause Local procedure error, diagnostic 84.
4. **Unknown NUI.** `x25pad -x 127.0.0.1:25001 -n 242299999999999`, any call: cleared.
5. **Usage per user.** Make calls as users 1 and 2, then on Norway's console `show x25 account` and
   `show x25 invoice uit-1`: each account counts only its own calls.
6. **Call records.** After calls, `./x75.ps1 calls`: one line per call, in Norway with the paying NUI.
7. **The prefix.** `CALL 26245890040004` without the `0`: Norway reads it as a national number. Write down
   what happens (to verify).
8. **The transit list.** With `x25switch germany.ini -v -v` the Call Request from England shows the X.75
   utility field naming 2342 as the transit network (X.75 5.3.1).
9. **A link going down.** Find England's process id with `./x75.ps1 status` and stop it
   (`Stop-Process -Id <id>`): calls in progress through England are cleared, and new calls to Germany are
   refused in Norway (out of order). Then `./x75.ps1 stop` and `start` again.
10. **The chat full.** 31 users at once: the 31st is refused (`MaxUsers` 30).
11. **Leaving.** `.x` in the chat: the call is cleared and the others see `---guest---`. `CLR` from the PAD
    in the middle of a chat: the others see the user leave as well (to verify).
12. **In-band management.** `x25nmc -x 127.0.0.1:25001 -a 24220000 -u admin -p admin`: Norway's management
    over an X.25 call (to verify with the NUI requirement on).
