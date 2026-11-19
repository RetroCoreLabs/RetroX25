# A large demo network, made from one file

`network.ini` lists countries, the X.75 links between them, and which hosts of the library (`../hosts`) sit in
which country. `large.ps1` turns it into one switch file per country and one host file, and starts it all. The
three-country network in `../x75-countries` is the hand-written model this follows.

## Use

```powershell
./large.ps1 start          # writes generated/, then starts every switch, echo service and the host program
./large.ps1 hosts          # every host and its number
./large.ps1 pad Norway     # a PAD on Norway's PAD line; at the * prompt: CALL 03106003604
./large.ps1 stop
```

The programs come from `src\build.ps1` (`src\publish\win-x64`); `-BinDir` names another folder.

## What each country gets

| What | Number | TCP port (country number i, from 0) |
|---|---|---|
| PAD line, no NUI asked | callers are `<dnic>7000` + channel | 27000 + 100i + 1 |
| Echo service | `<dnic>9001` | 27000 + 100i + 2 |
| Host line | each host's real number from the X.25 Network Atlas, the `nua` in `network.ini` (e.g. Tymnet's VM370: `3106003604`) | 27000 + 100i + 3 |
| Links it listens on | - | 27000 + 100i + 10, + 11, ... |
| Management console (admin / admin) | `<dnic>0000` | 9400 + i |
| Monitor | - | 4400 + i |

From another country a number is dialled with the international prefix 0: `CALL 03106003604`. Inside its own
country a host is dialled without the DNIC, except when what is left starts with 0 (Tymnet's `003604`, Sweden's
`02032832`): the switch reads a leading 0 as the international prefix, so such a host is always dialled in full.

## Routes

A call to a country with no direct link takes the shortest chain of links (fewest countries crossed). When two
chains are equally short, the one through the link written first in `network.ini` wins. `generate` refuses a file
in which some country cannot reach another.

## Adding to it

Add a `[country.N]`, a `[link.N]` to join it to the rest, and `[host.N]` lines (each with a real `nua` from the Atlas, starting with its country's DNIC), then `./large.ps1 stop` and
`./large.ps1 start`. Never edit `generated/`: it is written again on every start.
