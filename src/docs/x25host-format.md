# x25host: the host description format

`x25host` is a DTE program that answers X.25 calls and pretends to be a machine or a service: a banner, a
login, a command line or a menu, canned output. What it pretends to be is written in a **host file**, never in
code. The same files are read by the C# `x25host` (RetroX25) and the C `x25host` (x25emu), so one host behaves
the same under both emulators.

Status: this document is the specification. Neither program exists yet (PLAN.md).

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
x25host <host-folder> -x <switch-host:port> [-a <address>] [-v]
```

`-x` is the switch port the host connects to (XOT, as `x25echo`). `-a` overrides `[host] address`. The host keeps
redialling the switch until it answers, and redials after a restart (as `x25echo`).

## host.ini

The same ini style as the switch files: `[section]`, `key = value`, `#` starts a comment. Spaces around a value are dropped; a value in double quotes keeps them (`prompt = "$ "`), and the quotes themselves are not part of it. Unknown sections and
keys stop the program with the file, line and name (a typo is never a silently missing feature).

### [host]

| Key | Meaning | Default |
|---|---|---|
| `name` | Shown in logs, and as `{node}` in text | the folder name |
| `address` | The X.121 address the host answers | required, or `-a` |
| `sessions` | Calls served at once; one more is cleared Number busy | 8 |
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
| `{node}` | `[host] name` | always |
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
## Limits (both programs, not settable from the file)

- A typed line longer than 256 characters is cut there.
- A host folder of more than 1 MB of text files is refused at start.
- Output per typed line is at most 64 KB.
- Only files directly in the host's folder are read, and only when the program starts.

## Examples

`demo/hosts/vax` is a VMS-style login with DCL commands; `demo/hosts/bbs` is a menu BBS with a guest account.
Once `x25host` exists they will be added to the three-country network (`demo/x75-countries`).
