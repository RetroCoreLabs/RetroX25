# Altos CHAT 3.1 port - plan

**Next:** step 1, the C# chat engine and its tests.

The goal is to bring back the chat that ran on the Altos system in Munich in the 1980s, reached over X.25. It becomes a
new X.25 service beside the TAD and the echo service, in C# (RetroIO / RetroX25) and as a separate C program in
x25emu. A user at a terminal must see exactly what they saw then. The internals are new: the original used Unix
named pipes, fork and signals between its processes; the port uses one engine in one process, with no operating
system calls.

The source is `chat.c`, `chat.h`, `serv.c`, `README`, `README.linux`, `INSTALL` and `Makefile` of CHAT 3.1 (Hans Korneder,
DataVision, Munich, 29 May 1987; 3.0 was put in the public domain; Linux patch by Paolo Ventafridda, 1995). The
original Altos address was X.25 NUA 0262 45 8900 40004, login `guest` (README). The source is kept outside this
repository; every `file:line` below refers to it. **The original source can be found here:
<https://archive.org/details/altoschat>** (Internet Archive).

## Decisions (Ronny, 2026-11-18)

| Question | Decision |
|---|---|
| How many users | A setting, default **30** like the original. User 31 gets the original refusal. Tests raise it to 60. |
| Echo and line editing | **The PAD does it**, as in 1987. On connect the chat server sends an X.29 Set (Q bit) telling the PAD: echo on, forward on CR, editing on. The PAD must obey that as X.3 / X.29 say. Our PADs (C# and x25emu `x25pad`) are checked and fixed where they do not. |
| C# shape | **Built in**, like the TAD: a service inside the X.25 program with its own address. |
| C shape | **A separate program**, like `x25tadsrv`: `tools/x25chat.c` dials the switch over XOT. |

Chosen without asking (change them if wrong):

- **Bugs a user could see are fixed, the look is kept** (details in "Original bugs" below).
- **Starting nickname** `guest`, as every X.25 caller got on the Altos. **"From"** is the calling X.121 address, cut
  to 16 characters (the original took it from the `FROM` variable set by the Altos X.25 driver, README.linux).
- **`.w`** lists the calls on this chat service in the `who` layout, then `--- back in chat ---`.
  **`.b`** sends `+++ requesting for a chat +++` (the text of `/usr/lib/chat_msg`, INSTALL) to every chat user,
  then `--- back in chat ---`. The original ran `who` and `wall` on the Unix host; there is no Unix host here.
- **Output line end CR LF.** The original wrote LF and the Unix terminal driver added the CR.

## What the user sees (from the source)

All texts are copied exactly; `\n` goes out as CR LF.

**Connect** (chat.c:248-250, 277, 270; serv.c:253):

```
Welcome to the original, evergreen
Conference-utility "CHAT"  ver. 3.1
Copyright (C) 1987 Hans Korneder, Muenchen
Waiting for connection.
Connection established.
Get help with:    .h
+++guest+++
```

The original waited about 3 seconds between "Waiting" and "Connection established" (chat.c:278, `alarm(3); pause()`).
The port keeps a short pause; the engine is given the time, it never reads a clock.

**Help, `.h`** (chat.c:87-99):

```
.x           exit this program
.s           get the list of users on the chat
.n name      change your nickname
.h           get this page of text
.w           get a list of users on the system
.b           bell all users
.m           send mail to the sysop
.c #         switch to channel no <#>
.p passwd    set a passwd for a channel
!# text      send a private message to user <#>
```

**Commands.** Each is matched on the start of the line, case matters, anything after the prefix is ignored, and an
unknown dot command is sent as ordinary text.

| Input | Result |
|---|---|
| `.x`, `.q` | Leave. Nothing printed; the others see `---nick---`. `.q` is not in the help. |
| `.h` (and `.hello`) | The help. |
| `.s` | `No Chan       From             User     Called`, then one row `%2d %10ld %s %s %s` per user on every channel (slot, channel, from padded to 16, login padded to 8, nick). `.so...` prints `Function is being disabled by default` instead. |
| `.n name` (with the space) | `>>>old>>>name>>>` to everyone on the sender's channel, the sender included. Up to 32 characters kept; empty and duplicate names allowed. `.nfoo` is text. |
| `.c N`, `.cN`, `.cN,pw` | Change channel (number read like `atol`, so `.c`, `.cool` mean channel 0; a minus sign is dropped). `>>>nick>>> chan N` to the old and the new channel. Same channel: silent. Protected: `Channel is protected. Use a passwd` (no comma) or `Wrong passwd.` |
| `.p pw` | Protect your channel: `Channel 0 cannot be protected.`, `You are not alone on the channel.`, or silent success. 8 characters kept. Freed when the channel empties. |
| `!N text` | `nick whispers: !N text` to slot N on any channel; a bad or empty slot is silent; no confirmation. |
| `.m` | `This is disabled by default- use SMTP!` |
| `--!` | `Function is being disabled by default` |
| `.w`, `.b` | See the decisions above; both end with `--- back in chat ---`. |
| anything else | `nick says: text` to the others on the same channel, not to the sender. |

**Input rules** (chat.c:161-170): the top bit is removed, control characters are dropped, and a line is at most 118
characters. A longer line becomes several messages, each handled as its own line, so a remainder that starts with
`.x` makes the user leave. An empty line is ignored; a line of only spaces is sent (serv.c:273 strips nothing in
practice).

**Join and leave** go to every channel; **nick changes** to the sender's channel; **channel changes** to the old and
the new channel. There is no history, no timestamp, no idle timeout.

**Table full** (serv.c:225-229): user 31 sees the banner, `Waiting for connection.`, then
`open of answerpipe failed.`, and the call is cleared.

**A slow user** (serv.c:200-204): a write that blocks more than 2 seconds threw the user out: the others see
`---nick---` and the slow user's session stays open but receives nothing more until they type `.x`. The port keeps
this, with a per-user output limit in bytes instead of the 2-second alarm.

**Server log** (`-l`): `Chat-Server Starting At: ...`, `Incoming Call From <from> At <time>`, `+++nick+++`,
`---nick---`, `>>>old>>>new>>>`, `>>>nick>>> chan N`, `[slot:chan]nick says: text`, `[slot:chan]nick whispers: text`.
Kept, through a log interface.

## Original bugs

| Bug | Visible effect | Port |
|---|---|---|
| a. The shared `buf` is overwritten when a slow user is thrown out in the middle of a broadcast (serv.c:127, 204) | Later users get `---X---` twice and never the line | **Fixed**: each broadcast keeps its own text |
| b. A line of exactly 118 characters has no end marker (chat.c:169) | Rubbish after the line, or a crash | **Fixed**: treated as 118 characters |
| c. `blankstrip` does not strip | A line of spaces is sent | **Kept** (it is the look) |
| d. The most negative channel number stays negative (serv.c:338) | That user's text reaches every channel | **Fixed**: channel separation holds |
| e. Possible write past the channel table (serv.c:323-326) | None in practice | **Guarded** |
| f-i. pid field width, utmp, FIFO security, start-up race | Only in the old process design | Gone with the design |

## Design

**One engine, two languages.** The same design in C# and in C, with the same test list:

- **Engine** (no I/O, no clock, no threads, plain arrays): `Connect(session, login, from)`, `Input(session, bytes)`,
  `Disconnect(session)`, `Tick(now)`. It writes actions to a list: send bytes to a session, clear a session, a log
  line. A line reader per session (as `get_line`) and a server core (as `process()`) with the same order of command
  checks and the same texts.
- **Settings:** maximum users (default 30), output line end (CR LF), output limit per user, starting nickname,
  pause after "Waiting".
- **X.3 setup on connect:** the service sends an X.29 Set with the Q bit: echo on (parameter 2 = 1), forward on CR
  (3 = 2), editing on (15 = 1), and the erase, kill and display characters (16, 17, 18). Exact values are taken from
  X.3 / X.29 when the code is written, with the clause cited.

**C# (built in).** In `RetroIO.Protocols.X25`: `AltosChatEngine` (pure) and `ChatProtocolService : IProtocolService`
registered next to the TAD in `ProtocolRuntime.RegisterProtocolServices` (`ProtocolRuntime.cs`). A new config setting
for its address (only `X25.LocalMachineId` exists today), a local route in the sample configs, and the call user data
rule set so the TAD does not take its calls (the TAD accepts all call user data it does not recognise). Data comes
through `HandleData(lcn, data, qBit)` packet by packet, so the engine joins characters into lines itself. Output goes
through `DeliverServiceOutput`. The Desktop app gets a chat monitor window like "TAD Server Monitor" (users, channels,
nicks). The new code follows the house rules (no LINQ, no foreach); `ProtocolServiceRegistry` already breaks them and
is not copied.

**C (separate program).** `tools/x25chat.c` in x25emu, built like `tools/x25tadsrv.c`: `x25_client` callbacks for
incoming call, data, clear and drain, `-x host:port -a address`, a `TOOL_BINS` entry, and `main()` fenced by
`X25CHAT_NO_MAIN` so unit tests link the engine without the program. The engine itself in its own file so the tests
drive it directly.

**PADs follow X.3.** Both PADs must do what the chat server sets: the C# PAD (`X3.cs` and the PAD window) and
x25emu's `src/pad/pad.c`. Tests: the server's Set arrives, echo and editing switch on, a line is sent on CR, backspace
edits locally and never reaches the server. Anything a PAD does not obey is fixed in that PAD, with the X.3 clause.

## Steps

1. **C# engine and its tests** (all cases below), driven directly, no X.25.
2. **C# service:** `ChatProtocolService`, address setting, route, call user data rule; tests through the runtime.
   A test must be able to start the RetroIO runtime in-process with the chat, as the XOT client of a switch's
   subscriber port on `127.0.0.1:<port chosen at run time>`, with the chat answering 26245890040004 (the Altos
   address). This is for RetroIO's three-country X.75 test (`X75ThreeCountryTests`), which that test's owner wires
   in; if the runtime cannot be the XOT client side, write down what it needs.
3. **The C# PAD follows the X.29 Set**; tests.
4. **Desktop chat monitor window**; view-model tests.
5. **C engine and `tools/x25chat`**, the same test list in `tests/unit`, plus an integration test over the switch.
6. **x25pad follows the X.29 Set**; tests.
7. **Cross tests:** x25emu's pad to the C# chat; the C# PAD to x25chat; 40 users across both.
8. **Docs:** a user page (commands, settings) in RetroX25 `docs/` and in x25emu `docs/`; config samples. Each page
   says where the original source is: <https://archive.org/details/altoschat>.

## Tests (each written to fail first)

1. The connect text, exact bytes and order, CR LF.
2. Help byte for byte; `.hello` gives help.
3. `.x` and `.q` leave with nothing printed, the others see `---nick---`; `.X` is text.
4. Text reaches the others on the same channel only, not the sender.
5. An empty line is ignored; a line of spaces is sent.
6. 118 characters is one message; 200 is two; a remainder starting `.x` leaves.
7. Control characters and the top bit are removed.
8. `.n`: the announcement, the 32-character cut, empty name, `.nfoo` is text.
9. `.s`: header and rows exact, every channel; `.so` gives the disabled text.
10. `.c`: `.c5`, `.c 5`, `.c-5`, `.c`, `.cool`, same channel silent, old and new channel told.
11. `.p`: channel 0, not alone, silent success, no comma, wrong, right, longer than 8, empty, freed when empty.
12. `!N`: exact whisper text, across channels, bad or empty slot silent, `!hi` to slot 0, to yourself.
13. `.m`, `--!`, `.w`, `.b` replies and `--- back in chat ---`.
14. Unknown `.foo` and upper case commands are text.
15. The lowest free slot is reused; `.s` numbering after people leave.
16. Limit 30: user 31 gets `open of answerpipe failed.` and is cleared.
17. Slow user over the output limit: `---nick---` to the others, nothing more to the slow user, and everyone else
    still gets the original line (bug a).
18. A cleared call gives `---nick---` once.
19. Log lines exact.
20. **60 users** (limit 60), 3 channels, 20 lines each: every user gets exactly the lines of their channel in order,
    join and leave counts right, `.s` lists 60 rows, users leaving in the middle keep everything right. Also 40 users
    with the limit raised to 40, and 40 through the X.25 service (C#) and through the switch (C).
21. The X.29 Set on connect, exact bytes.
