# x25service

The X.25 protocol runtime as a console program, with no window. It reads the same JSON configuration as the
desktop app (`RetroX25.exe`), dials the switch named in its XOT interfaces, and runs the services the file
switches on. Today that is the Altos CHAT conference (a port of the 1987 CHAT 3.1, see
[altos-chat.md](../docs/altos-chat.md)).

It exists so a service can run in a script or a test without a desktop session: the three-country demo
(`demo/x75-countries`) uses it for the chat in Germany.

## Usage

```text
x25service <config.json> [-v]
```

| Argument | Meaning |
|---|---|
| `config.json` | A `RuntimeConfig` file, as the desktop app reads it (samples in `docs/config-samples`) |
| `-v` | Log at Debug level instead of Information |

It logs `x25service started from <file>; chat address <address>` when it is up, and runs until Ctrl+C or until
its process is stopped. The demo script stops it by process ID.

A missing or broken file stops it at once with the reason, exit code 1. It never starts on default settings,
because a service on the wrong settings looks alive and answers nothing.

## Example

```powershell
# from src/demo/x75-countries, with Germany's switch running
..\..\publish\win-x64\x25service.exe altos-chat.json
```

`altos-chat.json` dials Germany's Altos line (TCP 25022) and keeps one connection open for the switch to put
each arriving call on. Calls to `26245890040004` reach the chat.
