# The Altos chat

RetroIO's runtime can run **CHAT 3.1**, the conference program that ran on an Altos machine reachable over X.25
at NUA 0262 45 8900 40004 (Hans Korneder, Munich, 1987). The original source is at
<https://archive.org/details/altoschat>. A caller dials the chat's address from a PAD and is in the chat as
`guest`, as every X.25 caller was on the Altos. The plan for the whole port is
[altos-chat-port-plan.md](altos-chat-port-plan.md); this page covers the settings.

## Turning it on: the `Chat` section

```json
"Chat": {
  "Address": "26245890040004",
  "MaxUsers": 30,
  "OutputLimitBytes": 4096,
  "WaitPauseMs": 3000,
  "StartingNickname": "guest"
}
```

| Key | What it does | Default |
|---|---|---|
| `Address` | The chat's own X.121 address. Empty means there is no chat. It must differ from `X25.LocalMachineId`, which is the TAD's address: if they are the same, the chat is not started and the log says why. | empty |
| `MaxUsers` | Most users at one time. The next caller sees `open of answerpipe failed.` and is cleared, as on the original (its `NUSERS`). | 30 |
| `OutputLimitBytes` | How many bytes may be on their way to one user. A user who falls this far behind is put out of the chat, as the original did after a write had been stuck for 2 seconds. | 4096 |
| `WaitPauseMs` | The pause between `Waiting for connection.` and `Connection established.` The original slept 3 seconds there. | 3000 |
| `StartingNickname` | The name every caller starts with. | `guest` |

A call reaches the chat only if an incoming route lets its number in. Add a `Local` route for the address:

```json
{ "Name": "AltosChat", "X121AddressPattern": "26245890040004", "Type": "Local", "Destination": "CHAT", "Priority": 200, "Enabled": true }
```

## Which calls are the chat's

A call to the chat's address is the chat's when it comes from a PAD: its call user data starts with the X.29 PAD
identifier `01 00 00 00`, or it has no call user data at all. Any other call (XMSG, IP, another protocol) is not a
terminal and the chat does not take it. The chat ranks above the TAD (priority 110 against 100).

## What the chat asks the PAD to do

On every accepted call the chat first sends one X.29 **set** message (a data packet with the Q bit set), the bytes
`02 02 01 03 02 0F 01 10 7F 11 18 12 12`:

| X.3 parameter | Value | Meaning |
|---|---|---|
| 2, echo | 1 | the PAD echoes what is typed |
| 3, forwarding | 2 | the PAD sends the line when CR is typed |
| 15, editing | 1 | the PAD edits the line before sending it |
| 16, character delete | 127 | DEL |
| 17, line delete | 24 | CAN (Ctrl-X) |
| 18, line display | 18 | DC2 (Ctrl-R) |

The values of 16, 17 and 18 are the ones both standard profiles of X.28 (12/97) Table 1 use. A terminal whose
backspace key sends BS (8) does not delete with them; change parameter 16 at the PAD.

## Running it as the subscriber of a switch

The chat does not need a listening port of its own. It can sit behind a switch port that listens for its
subscriber: an XOT `Client` entry dials that port, and `KeepConnectionForIncomingCalls` keeps one unused
connection open, so the switch has a connection to put an arriving call on. When a call takes it, the entry dials a
new one at once. Every call still gets a TCP connection of its own (RFC 1613 section 5). Note that a switch port
holds one connection per channel, so it needs one channel more than the most calls it carries at one time.

```json
{
  "Name": "Altos",
  "Enabled": true,
  "Mode": "Client",
  "RemoteIPAddress": "127.0.0.1",
  "RemotePort": 2300,
  "KeepConnectionForIncomingCalls": true
}
```

The whole file is [config-samples/x25config-altos-chat.json](config-samples/x25config-altos-chat.json).
