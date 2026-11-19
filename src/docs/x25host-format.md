# x25host: the host description format

`x25host` is a DTE program that answers X.25 calls and pretends to be a machine or a service: a banner, a
login, a command line or a menu, canned output. What it pretends to be is written in a **host file**, never in
code. The same files are read by the C# `x25host` (RetroX25) and the C `x25host` (x25emu), so one host behaves
the same under both emulators.

Status (2026-11-19): the C# `x25host` reads and runs this format (the engine is `DescribedHost` in the RetroIO
package `RetroIO.Protocols.X25.Host`); the C `x25host` in x25emu is not written yet (PLAN.md G3). What is not
built yet is listed under "Many machines at once".

## Why a description and not a script

A host file only says what to print and which typed words lead where. Nothing in it is run. What a remote
user types is only ever compared with the words in the file and never run either. So a host file, or a user
typing at a host, cannot reach the machine the host runs on: no files outside the host's own folder, no
programs, no network. This is the model of the Cowrie honeypot, which fakes a whole Unix shell the same way.

A host that needs real logic (a game, a calculator) is a later step, in C# only, as a JavaScript script under
hard limits (Jint). It is not part of this format.

## Files

One host is one folder:

```text
vax/
  host.ini        the description (required)
  banner.txt      text files the description names (any name)
  motd.txt
  help.txt
```

`host.ini` names its text files by name; a name may not contain `/`, `\` or `..`, so only files in the host's
own folder can be read. Text files are sent as they are, with every line end turned into the host's line end.

## Running

```text
x25host <host-folder> -x <switch-host:port> [-a <address>] [-n <hostname>] [-v]
```

`-x` is the switch port the host connects to (XOT, as `x25echo`). `-a` overrides `[host] address`, `-n`
overrides `[host] hostname`. A host folder is a TYPE (a VMS machine, a BBS); the hostname and address make it
one particular machine, so the same folder can run as many machines at once (see "Many machines at once"). The host keeps
redialling the switch until it answers, and redials after a restart (as `x25echo`).

## host.ini

The same ini style as the switch files: `[section]`, `key = value`, `#` starts a comment. Spaces around a value are dropped; a value in double quotes keeps them (`prompt = "$ "`), and the quotes themselves are not part of it. Unknown sections and
keys stop the program with the file, line and name (a typo is never a silently missing feature).

### [host]

| Key | Meaning | Default |
|---|---|---|
| `hostname` | The machine's name: `{hostname}` in every text (the banner, the prompt, the logout line). Normally given by `-n` or the machine list, so one type folder serves many machines | the folder name |
| `address` | The X.121 address the host answers | required, or `-a` |
| `sessions` | Calls served at once; one more is cleared with cause 0 "DTE originated" (a DTE cannot give the network's cause Number busy, X.25 Table 5-7; the host's log says why) | 8 |
| `line_end` | `crlf` or `cr` or `lf`, sent after every line | `crlf` |
| `idle_minutes` | Clear a call after this many minutes with no input; 0 = never | 15 |
| `pad_calls_only` | `yes`: answer only calls whose call user data starts `01 00 00 00` (X.29) or is empty | `yes` |
| `banner` | Text file sent when the call is connected | none |
| `start` | Where a call starts: `login`, `shell` or `menu.<name>` | `login` if there is a `[login]`, else `shell` |

### [login]

| Key | Meaning | Default |
|---|---|---|
| `user_prompt` | Text before the user name | `Username: ` |
| `password_prompt` | Text before the password | `Password: ` |
| `hide_password` | `yes`: tell the PAD to stop echoing while the password is typed (X.29 set parameter 2 = 0) | `yes` |
| `case` | `upper`, `lower` or `exact`: how the user name is compared | `exact` |
| `attempts` | Wrong logins before the call is cleared | 3 |
| `fail_text` | Text after a wrong login | `Login incorrect` |
| `motd` | Text file sent after a good login | none |
| `then` | Where a good login goes: `shell` or `menu.<name>` | `shell` |

### [user.N]

One section per account, N from 0.

| Key | Meaning |
|---|---|
| `name` | The user name |
| `password` | The password. Empty: any password is taken (an open guest account) |
| `greeting` | Text file sent after the motd, for this user only |

### [shell]

A command line.

| Key | Meaning | Default |
|---|---|---|
| `prompt` | The prompt; may hold `{user}`, `{node}`, `{cwd}` | `$ ` |
| `case` | `upper`, `lower` or `exact`: how typed command words are compared | `exact` |
| `unknown` | Text for a command not in the file; `{word}` is what was typed | `{word}: command not found` |
| `logout_text` | Text sent before the call is cleared by a `logout` command | none |
| `unknown_file` | Text for `cat` of a name not in `[file.N]`; `{args}` is the name | `{args}: no such file` |
| `time_format` | How `do = time` and `{date}`/`{time}` are written: `vms` (18-NOV-2026 15:04:05), `unix` (Wed Nov 18 15:04:05 2026) or `iso` | `iso` |

### [command.N]

One section per command, N from 0. The first word typed is compared with `name` (and each `alias`); the rest of
the line is `{args}`.

| Key | Meaning |
|---|---|
| `name` | The command word |
| `alias` | Other words for the same command, comma-separated |
| `text` | One line of output (with variables) |
| `file` | A text file as the output |
| `do` | One of the built-in actions below |

Exactly one of `text`, `file` or `do` per command.

Built-in actions. This list is closed: a file cannot add one, and each is a few lines of code in the program.

| `do` | What it does |
|---|---|
| `logout` | Sends `logout_text`, then clears the call |
| `echo` | Sends `{args}` back |
| `time` | The current date and time, in the form `time_format` gives |
| `who` | One line per call to this host now: user name and calling address |
| `ls` | The names in `[file.N]` under the current directory |
| `cat` | The text file of `[file.N]` named by `{args}`, or `unknown_file` |
| `cd` | Changes `{cwd}` to a directory named in `[file.N]` |
| `menu.<name>` | Goes to that menu |
| `clear` | Clears the call at once, no text |

### Users on the same machine

Every machine keeps one list of its sessions (who is logged in, from which address, since when, idle how long).
Everything users of one machine do to each other goes through that list, never through files or the host's
operating system. Machines never see each other's lists, even inside one process.

More built-in actions, on top of the table above:

| `do` | What it does | Real-system model |
|---|---|---|
| `who` | One line per session: user, calling address, logged in since, idle time (`who_format` in `[shell]` shapes the line) | VMS `SHOW USERS`, Unix `who`, TENEX `SYSTAT` |
| `finger` | `{args}` names a user: their sessions, or `user_unknown` | Unix `finger` |
| `wall` | `{args}` goes to every session on the machine, framed by `wall_text` | Unix `wall`, VMS `REPLY/ALL` |
| `send` | First word of `{args}` is a user, the rest goes to their sessions, framed by `send_text`; `user_not_on` if they are not logged in | VMS `REPLY/USER`, PRIMOS `MESSAGE`, ITS `:SEND`, Dialcom `MAIL`-less message |
| `room.<name>` | The session joins the chat room `<name>`: every line it types goes to the others in the room (`room_text`), until it types `room_leave` (default `.x`); joins and leaves are announced (`room_join_text`, `room_leave_text`) | Altos CHAT, CompuServe CB, BBS teleconference |
| `mesg` | `{args}` `y` or `n`: whether `wall` and `send` reach this session | Unix `mesg` |

New `[shell]` keys for their texts (placeholders as usual, plus `{from}` the sending user, `{text}` the message):

| Key | Default |
|---|---|
| `wall_text` | `{nl}Broadcast message from {from} at {time}...{nl}{text}{nl}` |
| `send_text` | `{nl}Message from {from}: {text}{nl}` |
| `user_not_on` | `{args}: not logged in` |
| `user_unknown` | `{args}: no such user` |
| `who_format` | `{user} {calling} {since} {idle}` (the per-line placeholders `{since}` and `{idle}` exist only here) |
| `room_text` | `{from} says: {text}` |
| `room_join_text` / `room_leave_text` | `+++{from}+++` / `---{from}---` |
| `room_leave` | `.x` |

A message arriving while the user is typing is sent at once on its own line, and the line being typed is
shown again after it (as the real systems did). Limits: a message is cut at 256 characters; a session gets at
most 20 messages a second from others, the rest are dropped and counted (`show stats`), so one user cannot
flood another.

**For the JavaScript hosts (G4)**: the engine exists since 2026-11-18 as `ScriptedHost` in the RetroIO package
`RetroIO.Protocols.X25.Host` (its README has the full list). One script runs per machine, so its own variables are
shared by every caller on that machine. Built and tested so far: the events `onStart`, `onConnect(s)`,
`onInput(s, text)`, `onDisconnect(s)`; the session's `id`, `login`, `calling`, `connectedAt`, `send`, `clear`,
`pending`, `isOpen`; and `machine.settings`, `log`, `now`, `after`/`cancel` (timers), `wireLength`, `session`,
`sessions`, `broadcast`. The Altos chat runs on these alone (`scripts/altos-chat.js` in that package) and is checked
byte for byte against the C# chat. Still to build from the table below: `send(user, ...)` by user name, rooms,
`store`, and `session.onMessage`.

The planned shape, of which the above is the first part:

| Name | What it is |
|---|---|
| `machine.sessions()` | A copy of the session list: `user`, `calling`, `since`, `idle`, `room` per entry |
| `machine.send(user, text)` | To one user's sessions; false if not logged in |
| `machine.broadcast(text, exceptSelf)` | To every session |
| `machine.room(name)` | A room: `join()`, `leave()`, `say(text)`, `members()`, and an `onMessage(fn)` the script sets |
| `machine.store` | A small key/value store shared by the sessions of this machine only (strings, at most 64 KB in total, gone when the machine stops): a high-score table, a bulletin list |
| `session.onMessage(fn)` | Called when a `send`, `broadcast` or room message reaches this session |

With these the Altos chat (PLAN G6) is a room plus a few commands, a scripted `WHO` is `machine.sessions()`,
and nothing in the interface reaches a file, the network or another machine.

### [menu.<name>]

A menu: a title, numbered or lettered choices, a prompt.

| Key | Meaning |
|---|---|
| `title` | Text file sent before the choices (each time the menu is shown) |
| `prompt` | The prompt |
| `item.N` | `<key> \| <label> \| <action>`: what to type, what is shown, and `text:<line>`, `file:<name>`, `menu.<name>`, `shell` or `logout` |
| `unknown` | Text for a key not in the menu |

### [file.N]

A fake file tree for `ls`, `cat` and `cd`.

| Key | Meaning |
|---|---|
| `path` | The name the user sees, e.g. `/usr/guest/notes.txt` or `SYS$LOGIN:LOGIN.COM` |
| `content` | A text file of the host folder that `cat` sends; empty for a directory |

### Placeholders

Every text the host sends can hold placeholders, worked out at the moment the text is sent: the banner and
every other text file, and the `text`, `prompt`, `unknown`, `fail_text`, `logout_text` and `unknown_file`
values. `{{` and `}}` are a literal brace.

| Placeholder | Value | Has a value |
|---|---|---|
| `{date}` | Today's date, in the form `time_format` gives | always |
| `{time}` | The time now, in the form `time_format` gives | always |
| `{hostname}` | The machine's name (`-n`, the machine list, or `[host] hostname`) | always |
| `{node}` | The same as `{hostname}` (the VMS word for it) | always |
| `{address}` | The host's X.121 address | always |
| `{calling}` | The caller's X.121 address, as the Call Request gave it | always |
| `{sessions}` | Calls to this host right now, this one included | always |
| `{uptime}` | How long the host has been running, `d hh:mm:ss` | always |
| `{nl}` | A line end (`line_end`), for a value of more than one line | always |
| `{user}` | The name the caller logged in with, as typed | after login (empty in the banner and at the login prompts) |
| `{cwd}` | The current directory of the fake file tree | after login |
| `{word}` | The command word just typed | in `unknown` |
| `{args}` | The rest of the line after the command word | in a command's `text`, and in `unknown_file` |

Rules:

- A placeholder that is not in this table stops the program at start, naming the file, the line and the
  placeholder. A misspelling is never sent to the caller as it is.
- A placeholder used where it has no value (`{user}` in the banner) is replaced by nothing.
- Placeholders are only ever filled from this table. There is no way to name a file, a setting, an
  environment variable or anything on the machine the host runs on.
- New placeholders are added to this table, in both programs, with a test; the table is the whole list.

### Logins

The valid logins are the `[user.N]` sections of `host.ini`: `name` and `password`, one section per account.
Nothing else can log in. The name a caller logged in with is kept for the whole call: it is `{user}` in every
text, it is what `who` shows, it is written to the host's log with the calling address, and a JavaScript host
(the later step) gets it as `session.user`, with `session.calling`, `session.address` and `session.node`.
## Many machines at once: the machine list

**Built so far (2026-11-19):** `x25host --machines <list.ini> [--group <name>] [-v]`. A machine is a host
folder (`type = <folder name>` in `[defaults] library`, relative to the list file) or a script (`type = script`
with `script = hosts/clock.js`, which may pass values with `setting.<name> = value`; the script reads
`machine.settings.<name>`, and `hostname` and `nua` are always there). Keys read: `[defaults] library`, and per
machine `type`, `script`, `hostname`, `nua`, `switch`, `group`, `setting.*`. A mistake in the list or in a host
folder stops the program at start with the file and line; two machines with one `nua` are refused. Machines that
share a switch port share one XOT connection, and each call goes to the machine whose `nua` is exactly the
called number; any other number is cleared (cause 0, diagnostic 67). The switch gives the full number
(`called_address = international`, the default for every port). Not built yet: `nui`, and the console command `reload`. `log` sets a machine's lowest log level; the program's
own level (`-v`) still applies on top of it. The console (on stdin, and over telnet
with `--console-port` and the `[operator.N]` logins; three wrong logins close the connection) has `show hosts`, `show host`, `show stats`, `show calls`, `show users` (without bytes per user), `clear call`, `logout`,
`send`, `stop host`, `start host`, `restart host`, `help` and `quit`; a script machine does not report its users, so `show users`, `logout` and `send` say
so for it; the other commands answer that they are not built yet.
`--stats-log` is built: each
machine's line of the `show stats` counters, then a `total` line, go to the log once a minute. The working example is
`demo/x75-countries/machines.ini`.

One file lists every machine of a demo network; one command starts them all. The list says which type folder
each machine is, its hostname, its X.121 address (NUA) and which switch port it hangs on:

```ini
# machines.ini
[defaults]
library = ../hosts            # where the type folders are

[machine.0]
type = vms                    # library folder hosts/vms
hostname = TOMVAX
nua = 23429002
switch = 127.0.0.1:25012      # the XOT port of the switch it hangs on
nui = 234212345678            # optional: pays for calls TOMVAX itself places

[machine.1]
type = sintran
hostname = NDTROMSO
nua = 24229003
switch = 127.0.0.1:25002

[machine.2]
type = bbs
hostname = NORDBBS
nua = 24229002
switch = 127.0.0.1:25002
```

`x25host --machines machines.ini` runs every `[machine.N]` inside **one process**: each machine has its own
XOT connection to its switch and its own sessions, each with its hostname and address filled in. One process
means one PID for the demo script to stop, so Ctrl+C or a stop by PID takes the whole set of machines down at
once, and one log that says which machine answered which call (each line starts with the hostname).

Kept apart inside the process:

- A machine whose host folder is broken stops the program at start, naming the machine, the file and the line.
  Nothing starts half-configured.
- A fault while serving one call clears that call only (cause DTE originated, logged with the hostname). The
  other calls, on that machine and the others, go on.
- A switch that is down makes only the machines on it redial; the rest keep answering.
- The limits below apply per machine; `sessions` is per machine.

The calls are handled as asynchronous tasks, not one operating-system thread per machine: hundreds of idle
machines then cost almost nothing. The single-machine form `x25host <host-folder> -x ...` stays, for a test or
for one host by hand. Several machines may share one switch port: the
switch routes each NUA, and each host answers only its own. The switch ini files can be generated from the same
list (plan G5), so a large network is one list.

A machine line may also carry `nui = <NUI>`: the account billed for calls the machine itself places (a gateway
host that connects onward, a later JavaScript host that calls out). It is sent in the NUI selection facility
(X.25 6.21) of every call the machine makes; the switch must know it as an `[account.N]`. It is optional: in
the real networks a host on its own line was billed by its line and address, and NUIs were mostly for dial-up
PAD users. Calls TO the machine are paid by the caller (or by the machine when the caller asks for reverse
charging and the machine accepts it).

### One x25host or several

How many `x25host` processes run is a choice of the setup, not of the program. A machine line may carry
`group = <name>` (for example the country), and `--group` picks which machines one process runs:

```text
x25host --machines machines.ini                    # every machine, one process
x25host --machines machines.ini --group norway     # only the machines with group = norway
x25host --machines norway-hosts.ini                # or one list file per process
```

So one large process for all countries, one per country, or any other mix (all BBSes in one, the
mainframes in another) is the same program with other arguments. A machine with no `group` is in the group
`default`. Two processes may never run the same machine: each process registers its NUAs with the switch,
and a second one that tries the same NUA is refused at start with the name of the machine.

### The operator console

`x25host` has a console for the person running it: on its own terminal (stdin) and, with `--console-port <N>`,
over telnet on that port (bound to 127.0.0.1 unless `--console-bind` says otherwise, with the login of an
`[operator.N]` section in the list file: `name`, `password`). Several processes have several ports
(`--console-port 9301` for Norway's, `9302` for England's ...). Commands:

| Command | What it does |
|---|---|
| `show hosts` | One line per machine: hostname, type, NUA, group, state (`up`, `stopped`, `redialling`, `failed`), calls now / `sessions`, logged-in users |
| `show host <hostname>` | One machine in full: the above plus its switch, uptime, calls since start, refused logins, and the counters below |
| `show stats` | Per machine: calls in, calls refused (busy, not a PAD call), bytes in, bytes out, data packets in, data packets out, wrong logins |
| `show users` | Every logged-in session: hostname, user, calling address, logged in since, idle time, bytes in/out |
| `show calls` | Every call now, logged in or not: hostname, calling address, logical channel, since, state (banner, login, shell, menu) |
| `stop host <hostname>` | Clears the machine's calls (each user first gets the `logout_text`) and stops answering: a new call to it is cleared with cause 0 (DTE originated), as a DTE may give no other cause (X.25 Table 5-7); the line stays up for the other machines on it, so the switch does not see the machine as out of order |
| `start host <hostname>` | Starts a stopped machine again (re-reads its host folder; a broken folder leaves it stopped, with the reason) |
| `restart host <hostname>` | `stop` and `start` |
| `logout <hostname> <user>` | Logs that user out: sends the `logout_text`, clears the call. `logout <hostname> *` logs out everybody on it |
| `clear call <hostname> <lcn>` | Clears one call at once, logged in or not |
| `send <hostname> <user\|*> <text>` | A line of text to one user or to every user on the machine (an operator message, as `REPLY` / `SEND` on the real systems) |
| `reload` | Re-reads every host folder and the list file: new machines start, removed ones stop, changed ones restart only when they have no calls |
| `log <hostname\|*> <level>` | The log level for one machine or all |
| `help` | The commands |
| `quit` | Leaves the telnet console (on stdin it asks, then stops the process) |

The counters are kept per machine from the moment it starts and are cleared by `start`; `show stats` also gives
the totals for the process. The same figures go to the process log once a minute when `--stats-log` is given,
so a test can read them without a console.

## Limits (both programs, not settable from the file)

- A typed line longer than 256 characters is cut there.
- A host folder of more than 1 MB of text files is refused at start.
- Output per typed line is at most 64 KB.
- Only files directly in the host's folder are read, and only when the program starts.

## Examples

`demo/hosts/vax` is a VMS-style login with DCL commands; `demo/hosts/bbs` is a menu BBS with a guest account.
Once `x25host` exists they will be added to the three-country network (`demo/x75-countries`).
