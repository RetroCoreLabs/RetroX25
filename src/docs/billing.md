# Billing: accounts, NUIs and call records

This guide covers how the RetroX25 switch (the C# switch in RetroIO) and the x25emu switch decide who pays for a call,
how they count it, and what they write down. Both switches read the same settings and write the same record line, so
one test can run a call through both and compare the results.

## What X.25 says, and what is the operator's choice

| Item | Where it comes from |
|---|---|
| A caller can name who pays with a network user identifier (NUI) in the call | X.25 6.21 and 7.3.7 (facility code 0xC6) |
| The NUI is never passed on to the called side | X.25 6.21 |
| What a NUI looks like (length, characters) | Not set by X.25: "determined by the Administration" (7.3.7) |
| A NUI the network does not know: the call is cleared "Invalid facility request" (3), diagnostic 84 "NUI problem" | X.25 Annex C, Table C.1 row 15 |
| No NUI on a port that needs one: cleared "Local procedure error" (19), diagnostic 84 | X.25 Annex C, Table C.1 row 16 |
| Charging is counted in segments of 64 octets | ITU-T D.12 (1); D.12 itself says the number is "subject to further study" |
| A part-used segment counts as a whole one, once per message (a complete packet sequence), not once per packet | ITU-T D.12 (2) |
| The charging answer at the end of a call (segments both ways and the duration) | X.25 6.22 and 7.3.8 |
| Reverse charging: the called side pays | X.25 6.18 |
| Which accounts exist, which address ranges and NUIs they cover, whether a port requires a NUI, a default NUI per port | The operator's choice: settings below |
| Connected time is what is charged for time | ITU-T D.11 |
| The prices: fees, price per minute, price per 1000 segments, off-peak hours | Not set by X.25 or D.11/D.12: the Administration's choice. Our price file below |

## Accounts

An account is reached in one of two ways:

- **by a line:** the calling address falls inside the account's address range;
- **by a NUI:** the call names the account's NUI, or the port it came in on has a default NUI.

How a call finds who pays, in order:

1. If the call carries a NUI, that NUI's account pays. If no account has that NUI, the call is cleared (cause 3,
   diagnostic 84) before it is routed.
2. Otherwise, if the incoming port has a `default_nui`, that NUI's account pays.
3. Otherwise, if the port has `nui_required = yes`, the call is cleared (cause 19, diagnostic 84).
4. Otherwise the account whose address range covers the calling address pays. If none does, nobody is billed.

The called side may also name a NUI when it accepts the call (X.25 6.21.3). A known one makes that NUI's account the
called side's account for this call. An unknown one clears the call at both ends, cause 3, diagnostic 84 (X.25 Annex
E, part b, row 13); it never connected, so nothing is charged. The NUI is never passed on to the caller.

A call the switch refuses (for a NUI) is never offered to the far end; it bills nothing and writes no record line.

A call the called side refuses (it answers with a clear) did reach the far end, so it gets a record line. That line
shows it never connected (connect time 0) and carries no packets or segments, so nothing is charged; the attempt does
count in the account's calls made. Both switches do this.

### In the configuration file

```ini
[switch]
cdr_file = calls.cdr          ; the call record file; one line per call
tariff_file = tariff.conf     ; the price list for bills (C# switch and the app; see "Bills" below)
pvc_record_minutes = 15       ; a PVC record line every N minutes; 0 turns them off
segment_size = 64             ; octets in one charging segment, 1 to 4096; D.12's 64 if left out

[port.0]
type = access
transport = xot
port = 1998
nui_required = yes            ; a call on this port must name a NUI
; default_nui = CARD7         ; or: calls with no NUI are billed to this one
; charging_info = yes         ; the DTE here gets the charging answer on every call it pays for,
;                             ; without asking in the call (X.25 6.22)

[account.0]
name = office line
addr_start = 23420000
addr_end = 23420099

[account.1]
name = card 7
nui = CARD7

[pvc.0]
src_port = 0
src_lci = 1
dst_port = 1
dst_lci = 1
account = office line         ; who pays for this permanent circuit
```

A `default_nui` that names no account is refused when the file is loaded.

### At the console

The same commands in both switches:

```
x25 account add office range 23420000 23420099
x25 account add card7 nui CARD7
x25 account del office
show x25 account
write memory switch.conf
```

- To change an account, remove it and add it again.
- Removing an account is refused while a call or PVC is billed to it. In x25emu it is also refused while one is billed
  to an account added after it, because x25emu's calls remember their account by its place in the list.
- `write memory` takes a plain file name and writes it into the configuration folder: `config_dir`, or the folder the
  configuration was loaded from. A name with a path in it, or starting with a dot, is refused.

## The X.28 PAD login

A person at a PAD gives a NUI in one of two ways (X.28 5.2 and 3.5.15.1.1). In both cases the NUI is not echoed.

```
*IDsecret7                 <- the NUI ON command; "secret7" is not shown on the screen
*CALL 23420001             <- this call, and every later one, carries secret7
...
*IDOFF                     <- the NUI OFF command: later calls carry no NUI

*Nother9-23420001          <- a NUI for this one call only, typed before the address
```

- A NUI keeps its upper and lower case as typed.
- Spaces and control characters are dropped. A `-`, `,` or `+` in a NUI is answered `ERR`.
- `ID` on its own is answered `ERR`: these PADs do not prompt for the NUI.
- A good `ID` or `IDOFF` gets no answer, because X.28 defines none.
- The NUI is forgotten when the terminal disconnects.
- Both PAD programs take a NUI at start-up, as if `ID` and the NUI had been typed: `x25pad -n CARD7`. A NUI that is
  not valid is refused before the PAD connects. With `--telnet-port`, each telnet session starts with it, and one
  session's `IDOFF` does not change another's.

## How the segments are counted

The counting follows D.12:

- A message is a complete packet sequence: packets with the M bit set, then the last one without it.
- Each message is rounded up to whole 64-octet segments once.
- Each direction is counted on its own.

Worked examples:

- Two full 128-octet packets and a final 1-octet packet make one 257-octet message: ceil(257 / 64) = 5 segments.
- Three 1-octet packets as one message (the first two with the M bit set): ceil(3 / 64) = 1 segment. Counting packet by
  packet would wrongly give 3.
- The same three octets sent as three separate messages: 3 segments, one for each.

## The call record file

Each line has 24 comma-separated columns. "Forward" means from the calling side towards the called side. Lines written
before 17 November 2026 have 23 (no column 24); the bill code still reads them.

| # | Column | Notes |
|---|---|---|
| 1 | start | ms on the switch clock, when the call was routed |
| 2 | duration | ms from start to clear |
| 3 | calling address | |
| 4 | called address | |
| 5 | in port | |
| 6 | out port | |
| 7 | packets forward | |
| 8 | packets back | |
| 9 | bytes forward | |
| 10 | bytes back | |
| 11 | clearing cause | |
| 12 | diagnostic | |
| 13 | start, wall clock | seconds since 1970 |
| 14 | X.75 call identifier | six hex digits; always `000000` in the C# switch, which has no X.75 |
| 15 | connect time | ms on the switch clock; 0 if the call never connected |
| 16 | connected duration | ms from connect to clear, the duration ITU-T D.11 charges; 0 if never connected |
| 17 | segments forward | D.12 |
| 18 | segments back | |
| 19 | paying account | empty: none |
| 20 | NUI billed by | the NUI the call named, or the port's default; empty: none |
| 21 | reverse charging asked | 1 or 0 (X.25 6.18) |
| 22 | who is charged | `caller` or `called` |
| 23 | charging answer given | 1 or 0 (X.25 6.22) |
| 24 | called side's account | by the called address, or the NUI the called side named in its Call Accepted; empty: none. With reverse charging this account pays |

A comma in an account name or NUI is written as a space, so every line keeps 24 columns.

### One real call, worked through

From x25emu's test that sends a call through its switch (`tests/integration/test_call_through_switch.c`). The
caller 23420001 is inside the address range of account `lineA`. It sends "Hello from A!" (13 octets, one packet, M bit
off) and gets "Hello from B!" (13 octets) back; then its line drops.

```
1857149720,254,23420001,23420100,0,1,1,1,13,13,9,0,1794882704,000000,1857149731,243,1,1,lineA,,0,caller,0,
```

(Written before column 24 existed, then with an empty 24th column added: 23420100 is in no account's range.)

| Columns | Value | Why |
|---|---|---|
| 7, 8 | 1, 1 | one data packet each way |
| 9, 10 | 13, 13 | 13 octets each way |
| 11 | 9 | cleared "out of order": the caller's line went away (X.25 4.6.3) |
| 15, 16 | connected 11 ms after routing, for 243 ms | the call time D.11 charges |
| 17, 18 | 1, 1 | ceil(13 / 64) = 1 segment each way (D.12) |
| 19, 20 | `lineA`, empty | billed by address range, no NUI |
| 22 | `caller` | no reverse charging |

The account `lineA` then shows exactly these figures. The test checks both against the values worked out here.

### Lines for a permanent circuit (PVC)

A PVC has no call set-up or clear, so it gets a line for each period instead:

- every `pvc_record_minutes` minutes;
- when the PVC is reset, with the reset cause in column 11;
- when a port it uses goes down, with cause 1 ("out of order").

The address columns are empty. A message that is still open at the end of a timed period is carried into the next
period. A reset throws it away, because a reset discards the data in flight.

## The read-outs

- **`show x25 account`** at the console: one line per account. Columns: name; how it is reached (the address range, or
  `nui` and the identifier); calls up now; calls made/received; packets sent/received; segments sent/received; and the
  summed call time.
- **`GET_ACCOUNTS`** on the management connection (x25nmc): one `ACCOUNT` line per account, fields separated by `|`
  because an account name may contain a space:

```
ACCOUNT name=card7|addr_start=|addr_end=|nui=CARD7|active=0|calls_made=3|calls_recv=0|pkts_sent=40|pkts_recv=38|bytes_sent=4100|bytes_recv=3900|segs_sent=70|segs_recv=66|up=95
```

`up` is the summed call time in seconds.

- **The Status window** (Ctrl+1) of the RetroX25 app, when it runs the built-in switch: a column for each account,
  with how it is reached, calls up now, calls made and received, packets, bytes and segments each way, and the call
  time. Reset Statistics does not reset these: they are the bill.

## Bills

A bill is one account's charges for one month, worked out from the call record file and a price list. The C# code in
RetroIO does the sums; the C# switch console prints the bill and the RetroX25 app shows it. x25emu only writes the
record file, which the C# code reads the same way. The bill is laid out like a printed statement from a 1980s packet
network: a heading, the monthly fee, a line per call, calls to other networks in their own part, and the total.

How bills were made then (from memory, not checked against a source): each exchange wrote a record for each call, the
records went to a central billing system, and a bill usually had a monthly fee, a fee per call, a price per minute and
a price per 1000 segments, with lower prices at night. ITU-T D.11 and D.12 say what is counted (connected time,
segments); the prices were each network's own. D.10, the general tariff principles, has not been read.

### The price list

All money is in whole minor units (øre, pence, cents), never fractions, so a bill adds up to the last unit.

```ini
[tariff]
currency = NOK
; minor units in one major unit (100 øre in a krone)
minor_units = 100
; our network: a call to any other DNIC is a call to another network
home_dnic = 2342
; per account, per month: 250.00
monthly_fee = 25000
; seconds; each call's connected time is rounded up to whole units
time_unit = 60
; local time, minutes from UTC: for the bill month and the off-peak hours
utc_offset = 60
; off-peak from 18:00 to 08:00 local, 50% off the time and data charges
; (equal start and end means no off-peak)
off_peak_start = 18
off_peak_end = 8
off_peak_discount = 50

; calls within our network: 0.50 a call, 0.30 a minute, 2.00 per 1000 segments
[tariff.home]
call = 50
per_unit = 30
per_kilosegment = 200

; calls to and from other networks (through an X.75 gateway)
[tariff.other]
call = 300
per_unit = 150
per_kilosegment = 1000
```

`currency`, `home_dnic` and both price sections are required. A setting that is missing, out of range or not a whole
number is refused with its name; no bill is made from a price list with a mistake in it. A comment must be on a line
of its own: text after a value is read as part of the value, and the setting is then refused.

### How a call is charged

- **Who pays:** the caller's account (column 19), or the called side's (column 24) when the call was reverse charged.
- **Which calls:** only calls that connected. A call refused or cleared before it connected is not charged (D.11).
- **Which month:** the month it connected, local time.
- **Another network:** the other end's address (the called one, or the caller's for a reverse-charged call this account
  received) starts with a DNIC that is not `home_dnic`. The record has no column saying a call went through a
  gateway, and the X.75 call identifier (column 14) is kept by a call passing through, so the address is what decides.
  The call identifier is printed on each line of that part. No settlement between networks is worked out.
- **Time:** the connected time (column 16) rounded up to whole time units, times the price per unit.
- **Data:** the segments both ways (columns 17 and 18), because the paying side pays for what it sent and what it
  received, times the price per 1000, rounded up to a whole minor unit.
- **Off-peak:** when the call connected in off-peak hours, the discount comes off the time and data charges, rounded
  down. The call fee is never discounted.
- **PVC:** a PVC period line (no addresses) is charged time and data, and no call fee, because a PVC is never set up.
- Rounding is once per call: per-call charges are what the record holds.

Worked example, the figures a test checks: a call of `lineA` within the network, connected at 10:00 local for 150 s,
4 segments sent and 2 received. Time: 150 s is 3 minutes started, 3 × 0.30 = 0.90. Data: 6 × 2.00 / 1000 = 0.012,
rounded up to 0.02. Call fee 0.50. The call costs 1.42; with the monthly fee the bill is 251.42.

A call to DNIC 3134 connected at 21:00 local (off-peak) for exactly 60 s, with 1500 segments: 3.00 + 1 × 1.50 +
1500 × 10.00 / 1000 = 15.00, less 50% of (1.50 + 15.00) = 8.25: 11.25.

### At the switch console

```
show x25 invoice lineA 2026-11
show x25 invoice lineA          <- this month, local time
```

It needs `cdr_file` and `tariff_file` in `[switch]`. Both files are read when the command runs, so a changed price
list needs no restart. A line in either file that is wrong is reported (`Error: record line 8: ...`) and no bill is
printed, rather than a bill that quietly leaves a call out. Only the C# switch has this command.

### In the RetroX25 app

**Windows > Bill...** (Ctrl+7). The call record file and the price list start as the built-in switch's `cdr_file`
and `tariff_file`; either can be changed, for example to bill a record file x25emu wrote. **Read accounts** lists the
accounts that pay for a call in the file; choose one and a month, then **Make the bill**. The bill appears on a sheet
of paper, in a typewriter font, and can be selected and copied.

## Not yet done

- The Status window does not show the accounts of a switch running outside the app (decided 2026-11-17: read them
  with `show x25 account` or x25nmc).
- No settlement between networks for X.75 calls (decided 2026-11-17).
- x25emu's console has no `show x25 invoice` (decided 2026-11-17: one set of sums, in C#).
