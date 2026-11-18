# What machines, networks and BBSes of the 1970s-1990s showed a caller

Source material for `x25host` host files (`docs/x25host-format.md`): banners, login prompts, failure texts,
prompts, commands, logout texts. Collected 2026-11-18 by web research and from the ND manuals in the
NDInsight repository.

**How to read the marks.** Every text block is marked:

- **[verbatim]**: copied from the named source. Most web pages were read through a fetch tool that may have
  tidied spacing; check the source before relying on exact spaces or line breaks.
- **[reconstructed]**: not found in a source; written from the documented format or from memory. Use it, but
  never present it as what the real machine printed.
- **NOT FOUND**: looked for and not found. Nothing was invented in its place.

Site-specific names in the examples (node names, user names, project names) belong to the site in the source,
not to the system.

## Contents

1. Networks and PADs: the X.28 standard, BT PSS, JANET, Telenet, Tymnet, Austpac, Datapac, the rest
2. DEC: VAX/VMS, TOPS-20, RSX-11M-PLUS, RSTS/E, ULTRIX
3. Unix: 4.2/4.3BSD, SunOS 4, System V, Xenix, AIX 3, HP-UX 9, IRIX
4. IBM: OS/360-370 and MVS TSO, VM/370 CP/CMS, OS/400
5. Other minis and mainframes: Norsk Data SINTRAN III, Prime PRIMOS, Honeywell Multics, HP 3000 MPE,
   Data General AOS/VS
6. BBSes and chat systems: Maximus, WWIV, Citadel, Altos CHAT, the rest
7. What is still missing, and where to look next
8. Sources
9. ARPANET (1969-1983): the machines, TIP/TAC, ITS, TENEX, WAITS, TOPS-10, UCLA TSO
10. From the X.25 Network Atlas: Tandem, Stratus, Pick, CDC NOS, BS2000, CANDE, DIALOG, Dialcom, STN, CompuServe, SNA front ends, DECserver, outdials

---

## 1. Networks and PADs

### 1.1 The X.28 standard PAD (ITU-T X.28, 12/97)

Source: `T-REC-X.28-199712` in the Specs folder (3.5.17 and Table 6/7). **[verbatim]**

A clear indication is `CLR` and a cause, optionally followed by ` C:<cause code> D:<diagnostic>` (decimal,
X.25 values) and, in extended dialogue mode, ` - ` and the text:

| Cause | Extended dialogue text |
|---|---|
| `OCC` | Call cleared, number busy |
| `NC` | Call cleared, temporary network problem |
| `INV` | Call cleared, invalid facility requested |
| `NA` | Call cleared, access to this number is barred |
| `ERR` | Call cleared, network detected local procedure error |
| `RPE` | Call cleared, network detected remote procedure error |
| `NP` | Call cleared, number not assigned |
| `DER` | Call cleared, number out of order |
| `PAD` | Call cleared, remote request |
| `DTE` | Call cleared, by remote device, data may be lost |
| `RNA` | Call cleared, reverse charging refused |
| `ID` | Call cleared, incompatible destination |
| `SA` | Call cleared, ship cannot be contacted |
| `FNA` | Call cleared, fast select refused |
| `ROO` | Call cleared, cannot be routed as requested (X.96) |
| `CLR CONF` | Call cleared, confirmed |

So a full standard line is, for example, `CLR OCC C:1 D:0 - Call cleared, number busy` **[reconstructed from
the format; the cause code 1 = number busy is the X.25 value, check X.25 Table 5-7]**.

Commands typed by the caller, and the PAD's answer, from the 1988 Blue Book X.28 Annex A
(textfiles.com mirror) **[verbatim names, meanings partly paraphrased by the fetch tool]**:

```text
STAT        -> FREE | ENGAGED
CLR         -> CLR CONF   (or CLR ERR on a local procedure error)
PAR? <list> -> PAR ...
SET? <list> -> PAR ...
SET <list>  -> (acknowledgement) | PAR <bad refs> INV
PROF <id>   -> (acknowledgement)
RESET       -> (acknowledgement)
INT         -> (acknowledgement)
ICLR        -> CLR PAD
<address>   -> (acknowledgement), then COM when connected
```

Reset signals the PAD prints (X.28 3.5.7): `RESET DTE` (remote DTE reset the call), `RESET ERR` (local
procedure error), `RESET NC` (network congestion), `RESET RPE` (remote procedure error).

### 1.2 BT PSS and PSS Dialplus (UK), with a Telecom Gold session

Source: BT training module, 1987 (euclideanspace.com copy). **[verbatim; the source says scanning errors were
tidied]**

```text
<cr><cr>A2<cr>                  (wake-up: two CRs, then a profile code)
LON\A001-1920040302             (PAD identification)
N(nui),R,G(cug)-A(address)*D(data)      (call request: NUI, reverse charge, closed group)
[address]+[facilities]COM       (call connected)
CLR DTE 01 00:00:04:05 15 11    (cause, diagnostic, call time, packets)
RESET DTE / RESET ERR / RESET NC
PAR? 1,3,6   ->  PAR 1:1,3:126,8:0
```

Profile codes: `A1`-`A9`, `B1 B2 D1 D2`, `V1`-`V5`, `SP TP`. During a call, commands follow Ctrl-P: `PAR? SET
SET? PROF CLR STAT INT RESET`. Test commands: `TEST E`, `TEST C`, `TEST T`, `TAPE`, `INTD`.

A whole dial-up call to Telecom Gold **[verbatim]**:

```text
BIR\A002-2122240102
ADD?
NUI?
N591LUJ
A21920100485
23421920100485+COM
Please Sign On
>ID MJB
Password:
Off At 19:19 11/06/87 BST
Time used: 00h 01m connect, 00m 01s CPU, 00m 00s I/O.
CLR PAD (00) 00:00:00:32 13 17
```

PSS wrote the diagnostic in brackets after the cause: `CLR PAD (00)`, `CLR DTE (43)`.

### 1.3 JANET (UK academic network)

Source: Edinburgh EMAS PAD notes. **[verbatim]** The prompt is `PAD>`; a call is `PAD>call 2972`. Other
commands: `help`, `help address`, `clear`, `width=nn`, `transparent`, `message`, `printer`, and `break`, after
which `B CR` sends an interrupt.

NOT FOUND: the JANET "Hello" prompt, NRS names (`UK.AC.xxx`), and the PSS gateway texts. The document to find
is the "JNT PAD Terminal Users' Guide".

### 1.4 Telenet / SprintNet (USA)

Source: Phrack 42 file 8. **[verbatim]**

```text
@                                  (the prompt)
CONNECT <nua>    Connects to a network user address
CONTINUE         Continue session (used after breaking)
DISCONNECT       Closes session (same as bye)
BYE              Closes session (same as disconnect)
FULL / HALF      Set full / half duplex
ID <nui>         Sets the network user id for charged calls
STATUS           Shows your current network address and port
DISCONNECTED 00 00 00:00:00:00 000 00
```

The clear line is cause, diagnostic, connect time (days:hours:minutes:seconds), packets received, packets
sent. The cause numbers printed (0 DTE originated clear, 1 Number busy, 9 Out of Order, 11 Access barred,
13 Not obtainable) are the X.25 cause bytes in decimal. NOT FOUND: `TERMINAL=` and `C 31106...`.

### 1.5 Tymnet (USA)

Source: CompuServe access guide (Hacker Chronicles) and Phrack 42 file 5. **[verbatim]**

```text
PLEASE TYPE YOUR TERMINAL IDENTIFIER
PLEASE LOG IN:
```

The guide shortens the first to `PLEASE TYPE YOUR TERMINAL ID:`; the user answered `A`, then a host name such
as `CIS02`. At other speeds than 300 baud the first prompt was garbled until the `A` was typed.

### 1.6 Austpac (Australia)

Source: austpac notes on textfiles.com mirrors. **[reconstructed from a summary]** A call with a NUI is
`NABCDEF123456-<address>`, the NUI not echoed; `COM` after a short pause, then the host's own text; `CLR` when
the link is not available.

### 1.7 Not found yet

Datex-P (Germany): the Bundespost **DATEX-P-Handbuch Teil 6** (osmocom.org) has a section "Meldungen aus dem
Netz" and is the best source to read next. Datapak (Norway) and the other Nordic networks, Transpac and
Minitel (France), Prestel, Datapac's own texts: NOT FOUND.

---

## 2. DEC

### 2.1 VAX/VMS (DCL)

Banners seen by people scanning X.25 addresses (Phrack 42) **[verbatim]**:

```text
This is DTAC02 - VAX/VMS V5.5
System LPCOMB - VAX/VMS V5.5-1
WELCOME TO SOLO - Unathorized use prohibited
```

What a caller met at the X.25 level before VMS (Phrack 42) **[verbatim]**:

```text
Please enter our X.29 Password:
ND X.29 Server - Press 'ESCAPE' to log in
CONNECTED TO PACKET/94
```

Login, VMS 5.4 HELP LOGIN **[verbatim; leading spaces approximate]**:

```text
Username: JONES
Password:
              Welcome to VAX/VMS Version 5.4 on node JUPITER
        Last interactive login on Tuesday, 19-APR-1991 09:16:47.08
        Last non-interactive login on Monday, 18-APR-1991 17:32:34.27
$
```

VMS 4.4 (Phrack 35) **[verbatim]**:

```text
          Welcome to VAX/VMS V4.4
   Last interactive login on Monday, 16-JUL-87  16:12
   Last non-interactive login on Friday, 13-JUL-87  00:14
```

Failure and warnings **[verbatim]**:

```text
User authorization failure
               1 failure since last successful login.
          WARNING - Primary password has expired; update immediately.
```

Prompt `$ `. Commands **[verbatim]**:

```text
$ SHOW DEFAULT
USER$:[HIGGINS]
$ SHOW TIME
12-JUN-1989 14:29:23
$ SHOW PROTECTION
SYSTEM=RWED, OWNER=RWED, GROUP=RE, WORLD=NO ACCESS
$ SUBMIT ACCOUNT
Job ACCOUNT (queue SYS$BATCH, entry 103) started on SYS$BATCH
```

Unknown command, OpenVMS 7.3 **[verbatim]**:

```text
$ CAPY
%DCL-W-IVVERB, unrecognized command verb - check validity and spelling
 \CAPY\
$
```

Logout **[verbatim]**; `LOGOUT/FULL` adds the accounting block:

```text
$ LOGOUT
HARRIS logged out at 11-DEC-1999  12:42:48.12

Accounting information:
 Buffered I/O count:      8005   Peak working set size:    212
 Direct I/O count:         504   Peak virtual size:        770
 Page faults:             1476   Mounted volumes:            0
 Charged CPU time:0 00:00:50.01  Elapsed time:0 02:27:43.06
```

NOT FOUND: `DIRECTORY` and `SHOW USERS` output.

### 2.2 TOPS-20 (Monitor 7, 1988 user's guide)

**[verbatim]** The words in brackets are guide words the system types itself; the password is not echoed.

```text
AURORA, Research and Development, TOPS-20 Monitor 7(7)
@LOGIN (USER) SARTINI (PASSWORD) (ACCOUNT) 341
 Job 57 on TTY127 23-Jul-88 09:48:40, Last Login 22-Jul-88 09:30:27
@DAYTIME
         Thursday, May 26, 1988 08:41:21
@DIRECTORY
       PUBLIC:<LEOPOLD>
     PROG1.PAS         3
     LOGIN.CMD.2       1
     Total of 13 pages in 7 files
?Unrecognized command
@
```

(The DIRECTORY listing is shortened in the source.) Logout and idle logout **[verbatim]**:

```text
Killed Job 57, User SARTINI, Account 341, TTY 127,
 at 23-Mar-88  09:49:36,  Used 0:0:14 in 1:25:56

Autologout
Killed Job 8, TTY 26,
 at 23-Mar-88   10:50:35,   Used 0:0:0 in 5:15
```

Also seen: `?LOGGING IN ON LOCAL TERMINALS IS CURRENTLY NOT ALLOWED`. NOT FOUND: the wrong-password message.

### 2.3 RSX-11M-PLUS (V4.6)

From a 2023 session post; the words **[verbatim]**, the line breaks **[reconstructed]**:

```text
Welcome to TALOS, an RSX-11M-PLUS system!
>hello 1,1
Password:
RSX-11M-PLUS V4.6 BL87  [1,54] System TALOS
23-JAN-2023 07:48  Logged on Terminal TT12: as SYS2
Good Morning
Last interactive login on Monday, January 23, 2023 21:37:35 (TT1:)
```

The message of the day is `LB:[1,2]LOGIN.TXT`. Prompt `>` **[reconstructed]**. NOT FOUND: failure, command
output, unknown command, `BYE` (in the MCR Operations Manual AA-FD10A-TC, not yet read).

### 2.4 RSTS/E

V10.1 emulator session **[verbatim]**:

```text
RSTS V10.1-L 08-Mar-19 11:05 PM
User: 1,2
Password:
Last interactive login on 08-Mar-19, 11:05 PM at KB0:
Last non-interactive login on 08-Mar-19, 11:05 PM
$ mc ncp
^
?Invalid command
```

V8.0 style, from a fake login in Phrack 7 that copies the real one **[verbatim copy of a copy]**:

```text
RSTS V8.0-07 TOK Communications Ltd.  Job 7  <Dial-up> KB41
User:
Password:
Invalid entry - try again
```

### 2.5 ULTRIX

Banner from `/etc/gettytab` (1991 Usenet) **[verbatim]**: `ULTRIX V4.0 (Rev. 179) (%h)`, `%h` = host name.
The rest as BSD (section 3.1) **[reconstructed]**.

---

## 3. Unix

### 3.1 4.2BSD and 4.3BSD

From the 4.3BSD source (login.c, csh, sh) on the unix-history-repo mirror.

```text

4.3 BSD UNIX (hostname) (ttyp0)

login:
Password:
Login incorrect
```

Banner **[reconstructed from the gettytab description]**; the prompts and `Login incorrect` **[verbatim]**.
4.2BSD is the same with `4.2 BSD UNIX`. After 5 failures login waits 10 s and drops the line silently.

Other login messages **[verbatim]**: `No directory!`, `Logging in with home=/`, `No shell`,
`Login timed out after 60 seconds`, `You have too many processes running.`, `Too many users logged on
already.`, `Try again later.`

After login: `Last login: <ctime without year> from <host>` or `on <tty>` **[verbatim format]**, then
`/etc/motd`, then `You have new mail.` or `You have mail.` **[verbatim]**. Example **[reconstructed]**:

```text
Last login: Tue Mar 14 09:12:47 from sun1
4.3 BSD UNIX #1: Fri Jun  6 19:55:29 PDT 1986
You have mail.
%
```

Prompts **[verbatim]**: csh `% ` (root `# `); sh `$ ` (root `# `, continuation `> `). Unknown command: csh
`foo: Command not found.`, sh `foo: not found` **[reconstructed format of verbatim strings]**. csh prints
`logout` on exit **[verbatim]**.

### 3.2 SunOS 4.x

**[verbatim, 1993]**:

```text
SunOS UNIX (csdc_1)

login:
```

motd first line from the kernel version string **[reconstructed]**:
`SunOS Release 4.1.1 (GENERIC) #1: Thu Oct 11 12:00:00 PDT 1990`. The rest as BSD **[reconstructed]**.

### 3.3 AT&T System V (SVR2-SVR4)

SVR4 telnet **[verbatim, 1996]**:

```text
UNIX(r) System V Release 4.0 (joe.foo.com)

login:
```

Serial lines: `/etc/issue`, then the prompt from `/etc/gettydefs`, normally `login: `. `Login incorrect`
**[verbatim]**. Prompt `$ `, `foo: not found` **[reconstructed]**.

### 3.4 SCO Xenix

Order: `/etc/issue`, then the `/etc/gettydefs` prompt ending `@!login: `; telnet shows `/etc/telbanner` first.
No banner found: do not invent a "Welcome to SCO" line. The rest as System V **[reconstructed]**.

### 3.5 AIX 3

**[reconstructed from a summary]**:

```text
AIX Version 3
(C) Copyrights by IBM and by others 1982, 1991.
login:
```

The text is `herald` in `/etc/security/login.cfg`.

### 3.6 HP-UX 9

login(1) messages, HP-UX 9.00 **[verbatim]**: `Login incorrect`, `dialup password:`, `Your password has expired.
Choose a new one`, `No directory`, `Consult system administrator.`, `No shell`, `Sorry, single-user`. Banner
by analogy with 11.x **[reconstructed]**: `HP-UX hostname A.09.05 E 9000/715 (ttyp1)`.

### 3.7 IRIX (6.5 console capture)

**[verbatim]**:

```text
IRIX Release 6.5 IP22 IRIS
Copyright 1987-2003 Silicon Graphics, Inc. All Rights Reserved.
Last login: Wed Jul 21 08:44:19 PDT 1993 on ttyd1
TERM = (vt100)
IRIS 1#
```

Console prompt `IRIS console login: `; over telnet `IRIS login: ` **[reconstructed]**.

---

## 4. IBM

### 4.1 TSO on OS/360 (MVT), OS/370 and MVS 3.8j

A real 1972 MVT session at UCLA (RFC 390) **[verbatim; the author's margin notes removed]**:

```text
logon uid
LOGON UID
ENTER PASSWORD
xyz
UID LOGON IN PROGRESS AT 09:59:18 ON AUGUST 28, 1972
LOGON PROCEEDING
WELCOME TO TSO.  GOOD LUCK.
= CCN NEWS =
THE DEFAULT LOGON PROCEDURE NO LONGER PRE-ALLOCATES FILES -
  SYSUT1, SYSUT2, AND SYSUT3.
READY
status
 ABC123P  WAITING FOR READER
IEF404I ABC123P  ENDED
READY
submit sample
ENTER JOBNAME CHARACTER-
p
JOB ABC123P SUBMITTED
READY
print output.list
DATASET OUTPUT.LIST HAS BEEN ENQUEUED FOR PRINTING IN CLASS C
READY
logoff
UID LOGGED OFF TSO AT 10:40:18 ON AUGUST 28,  1972
```

In 1972 the message numbers were not printed. "CCN NEWS" is that site's own message.

MVS 3.8j (TK4- user's manual) **[verbatim]**: before logon on a line terminal under TCAM
`IKJ54012A ENTER LOGON -` (on a 2741: `IKJ53020A ENTER  LOGON`); under VTAM on a 3767
`Enter logon command for application TSO` / `Enter command>`; the password prompt
`ENTER CURRENT PASSWORD FOR <user>-`; the prompt `READY`. On a 2741 line RETURN must be pressed twice and READY
comes twice (a known TK4- quirk).

With message numbers (later MVS and z/OS manuals) **[verbatim]**:

```text
IKJ56455I IBMUSER LOGON IN PROGRESS AT 18:53:51 ON FEBRUARY 19, 1907
IKJ56421I PASSWORD NOT AUTHORIZED FOR USERID
IKJ56710I INVALID USERID
IKJ56705I MISSING PASSWORD FOR user_id
IKJ56420I USERID user_id NOT AUTHORIZED TO USE TSO
IKJ56457I LOGON FAILED JCL ERROR IN LOGON PROCEDURE
IKJ56500I COMMAND xxxxxxxx NOT FOUND
IKJ56470I SYSADM LOGGED OFF TSO AT 21:17:16 ON NOVEMBER 3, 2015
```

(1907 is a clock fault in that log.) `IKJ56700A` is NOT the user-ID prompt: z/OS gives it as `ENTER STRING TO BE
USED FOR ATTENTION`. NOT FOUND as text: LISTCAT, LISTDS, TIME output (screenshots only, Jay Moseley's
tutorial).

### 4.2 VM/370 CP/CMS (Release 6, 1980)

Source: VM/370 Terminal User's Guide GC20-1810-9 (bitsavers). **[verbatim]**

```text
VM/370 ONLINE
logon userid
ENTER PASSWORD:
LOGMSG 09:34:54  02/15/72
*FOR CMS, IPL 190 UNTIL FURTHER NOTICE
*QUERY LOG FOR ADDITIONAL INFORMATION
FILES: xxx RDR, xxx PRT, xxx PCH
LOGON AT hh:mm:ss zzz weekday mm/dd/yy
ipl cms
CMS...VERSION v.1 mm/dd/yy
```

After the banner CP types `.` to mean "type now". Failures **[verbatim]**: `DMKxxx050E PASSWORD INCORRECT`,
`DMKxxx054E ALREADY LOGGED ON LINE raddr`. Unknown command **[verbatim]**: `?CP: xxxxx` (CP mode),
`UNKNOWN CP/CMS COMMAND` (CMS), `UNKNOWN CP COMMAND`. Logoff **[verbatim]**:

```text
CONNECT=hh:mm:ss VIRTCPU=mmm:ss.hs TOTCPU=mmm:ss.hs
LOGOFF AT hh:mm:ss zone weekday mm/dd/yy
```

CMS ready message `R;`, with timing `R; T=0.01/0.02 hh:mm:ss` **[reconstructed]**. Message to another user
**[verbatim]**: `message jones need my virtual machine plz logoff` arrives as
`MSG FROM LOGONxxx: NEED MY VIRTUAL MACHINE PLZ LOGOFF`.

### 4.3 OS/400 (AS/400)

OS/400 has no line mode: an ASCII or X.25 caller still got the full 5250 screen through a converter or the
ASCII workstation controller **[reconstructed, no source]**. A simulation sends these screens as plain text.

Sign-on screen, from IBM's QDSIGNON sample **[verbatim]**:

```text
                      Sign On
                                    System  . . . . . :   XXXXXXXX
                                    Subsystem . . . . :   XXXXXXXXXX
                                    Display . . . . . :   XXXXXXXXXX

        User  . . . . . . . . . . . . . .   ___________
        Password  . . . . . . . . . . . .

        Program/procedure . . . . . . . .   ___________
        Menu  . . . . . . . . . . . . . .   ___________
        Current library . . . . . . . . .   ___________

                              (C) COPYRIGHT IBM CORP. 19XX, 20YY.
```

Typical values: Subsystem `QINTER`, Display `QPADEVxxxx`. Failures: `CPF1107  Password not correct for user
profile.` **[verbatim, thread title]**, `CPF1120  User &1 does not exist.` **[from a snippet]**.

MAIN menu (CA Automation Point manual) **[verbatim]**:

```text
MAIN                    AS/400 Main Menu
                                                     System:   S44A0060
Select one of the following:
     1. User tasks
     2. Office tasks
     3. General system tasks
     4. Files, libraries, and folders
     5. Programming
     6. Communications
     7. Define or change the system
     8. Problem handling
     9. Display a menu
    10. Information Assistant options
    90. Sign off
Selection or command
===>
F3=Exit   F4=Prompt   F9=Retrieve   F12=Cancel   F13=User support
F23=Set initial menu
```

`DSPMSG MSGQ(*SYSOPR)` **[verbatim]**:

```text
                        Display Messages
                                                      System:   S44A0060
Queue . . . . . :   QSYSOPR                 Program . . . . :   *DSPMSG
  Library . . . :     QSYS                    Library . . . :
Severity  . . . :   40                      Delivery  . . . :   *HOLD
Type reply (if required), press Enter.
  Load form type '*STD' device PRT02 writer PRT02.  (H C G I R)
    Reply . . .   _____________________________________________________
                                                                  Bottom
F3=Exit             F11=Remove a message                  F12=Cancel
F13=Remove all      F16=Remove all except unanswered      F24=More keys
```

NOT FOUND: WRKACTJOB, WRKSPLF, the unknown-command message (memory says `CPD0030`, not verified).

---

## 5. Other minis and mainframes

### 5.1 Norsk Data SINTRAN III

Source: ND manuals in the NDInsight repository. Boot banner, SINTRAN III System Supervisor
(ND-30.003.007) **[verbatim]**:

```text
09.13.44    17. MAY 1986
SINTRAN III - VSX/500 K REVISION 0B
CPU (SYSTEM NUMBER): 6586
GENERATED: 16.39.00   15 MAY 1986

SINTRAN III RUNNING -
```

Logging in: the caller presses ESC, SINTRAN prints `ENTER`, the user name is typed after it, then
`PASSWORD:`. The Operations Guide writes it in two ways, both quoted here **[verbatim]**; which one is the
screen exactly (a colon after the typed name or not) is to be checked on a live system:

```text
ENTER
PASSWORD:
```

```text
ENTER SYSTEM:
PASSWORD:
@
```

The prompt is `@`. `@TERMINAL-STATUS` lists the logged-in users (COSMOS User Guide ND-60.163.4, shown there
as a table): LOG.NO, USER, MODE, CPU-MIN, OUT OF, LAST COMMAND. Logging out (Operations Guide; COSMOS guide
for a batch user) **[verbatim]**:

```text
@LOGOUT
-- EXIT --
```

```text
BATCH USER LOGGED OFF AT 14.21.27 8 NOVEMBER 1985

TIME USED IS 0 SECS OUT OF 1 SECS
```

Also seen in the manuals: `AMBIGUOUS COMMAND`, `NO MAIN DIRECTORY`, ` ND-100 CPU TIME USED IS    0 SECS`.
Better than any of these: a live SINTRAN boot on nd100x (RetroCore), which prints the real texts.

### 5.2 Prime PRIMOS (19.4, Phrack 10 and 16)

**[verbatim]**

```text
PRIMENET 19.2.7F PPOA1
LOGIN
User ID?
Password?
Invalid user id or password; please try again.
PPOA1 (user 39) logged in Monday, 15 Dec 86 02:29:16.
Welcome to PRIMOS version 19.4.9.
Last login Friday, 12 Dec 86 08:29:04.
OK,
```

Over a dial line: `CONNECT` / `Primenet V 2.3 (system)`. Prompts: `OK,` after a command that worked, `ER!`
after one that failed. Before logging in, an unknown command gives:

```text
Invalid command "XXZZZUUU".  (logo$cp)
Login please.
ER!
```

Idle: `Maximum Inactive Time Limit Exceeded`. Logout command `LOGOUT`; the logout text NOT FOUND
(`PPOA1 (user 39) logged out ...` **[reconstructed]**).

### 5.3 Honeywell Multics (MR10.2)

Source: MIT Multics source archive (tut_logging_in, login.info). **[verbatim]**

```text
Multics MR10.2: Honeywell LCPD Phoenix, System M (Channel c.h118)
Load - 140.0 out of 210.0 units: users - 154, 12/14/83 1313.5 mst Wed
l Smith
Password:
```

**[reconstructed]**: `Smith.Project logged in 12/14/83 1314.0 mst Wed from ASCII terminal "none".`, ready
message `r 13:14 0.123 5` (time, CPU seconds, page faults), logout
`Smith.Project logged out 12/14/83 1320.4 mst Wed` / `CPU usage 3 sec, memory usage 12.1 units, cost $1.25.`
/ `hangup`.

### 5.4 HP 3000 MPE

**[verbatim from manual examples, via search summaries]**: `MPE XL:HELLO USER.TECHPUBS`,
`:HELLO SESSIONA,STUDENT.INTRO`; syntax
`HELLO [sessionname,]username[/userpass].acctname[/acctpass][,groupname[/grouppass]]`. The prompt is `:` on
MPE V and `MPE XL:` on MPE XL.

**[reconstructed, not checked]**:

```text
:HELLO USER.ACCT
ENTER ACCOUNT (ACCT) PASSWORD:
HP3000 / MPE V G.02.00 (BASE G.02.00).  MON, DEC  5, 1994, 10:30 AM
:
```

`EXPECTED HELLO, :JOB, :DATA, OR (CMD) AS LOGON. (CIERR 1402)`, `UNKNOWN COMMAND NAME. (CIERR 975)`, and `BYE`
giving `CPU=1. CONNECT=1. MON, DEC  5, 1994, 10:31 AM` / `END OF SESSION`.

### 5.5 Data General AOS/VS

NOT FOUND. Memory says `Username:`, `Password:`, a `)` prompt, `BYE` **[reconstructed, unchecked]**.

---

## 6. BBSes and chat systems

### 6.1 Maximus (FidoNet)

Source: `lang/english.mad` in the GPL source release. **[verbatim; `%s` and `%d` are filled in, `\a` is a
bell]**

```text
What is your name%s:
What is your LAST name%s:
Password:
Please enter the password you wish to use:
Please re-enter your password for verification:
YOU HAVE ONLY 5 MIN. LEFT.
TIME ALMOST UP.
TIME LIMIT.
Warning!  Only %d minutes left.
%s is yelling...
Sorry, but there's no answer.
Bye %s; thanks for calling.  Please hang up now.
```

Menu prompt `[MAIN]: ` **[reconstructed from the format strings]**.

### 6.2 WWIV 4.x

Source: WWIV 4 user manual. Prompts **[verbatim]**:

```text
[Sub 1] [Messages from The Management] :
T - 02:13:01
Select [ABCD, <space> to quit] :
A-msg: R:ead, W:rite, A:uto-reply, Q:uit :
Defaults: (1-9,A-C,W,L,?,Q) :
Mail {?} :
```

Main menu keys **[verbatim]**: A Auto-Message, B View BBS List, C Page Sysop for Chat, D User Default Section,
E E-Mail a User, F Feedback to Sysop, G General Text Files, H Hop to Another Sub, I System News and Info,
J Join a Conference, L Caller Log for Today, M Read Your Mail, N New-Scan All Subs, O Log Off the System,
P Post a Message, Q New-Scan Current Sub, R Remove a Message, S Scan Message Titles, T Transfer Section,
U View User List, V Voting Section, W Slash Command Menu, X Expert/Novice Toggle, Y Personal User Stats,
Z Continuous N-Scan, $ Time Bank, * List Available Subs, . On-Line Programs.

Login (described, not quoted): name or user number, `NEW` to sign up, then password and phone number, three
tries.

### 6.3 Citadel

Source: Citadel NOVICE.HLP and citadel.org. The prompt is the room name and `>`; the first room is `Lobby>`.
Commands **[verbatim]**: `<N>ew.`, `<G>oto.`, `<E>nter.`, `<L>ogin.`, `<T>erminate.`, `<H>elp.`;
`<T>erminate` "lets you leave the system gracefully".

### 6.4 Altos CHAT (Munich), reachable on Datex-P 026245890040004

Source: the 1987 CHAT 3.1 program by Hans Korneder, ported to RetroIO from its source
(`AltosChatEngine`, `docs/altos-chat.md`). **[verbatim from the program]**

```text
Welcome to the original, evergreen
Conference-utility "CHAT"  ver. 3.1
Copyright (C) 1987 Hans Korneder, Muenchen
Waiting for connection.
Connection established.
Get help with:    .h
+++guest+++
```

What one user types, the others read as `guest says: <text>`; `.x` leaves, the others see `---guest---`.
Phrack 21's NUA list has `D 02624 5890040004 ACS.MUNICH`.

### 6.5 Not found yet

PCBoard, Opus, RemoteAccess, Wildcat!, TBBS, GT Power (need each package's language file or source); Unix
"bbs" logins; QSD (France), CHAOS/CCC boxes, Lutzifer, the JANET/PSS chats, Minitel messageries.

---

## 7. What is still missing, and where to look next

| Gap | Where to look |
|---|---|
| SINTRAN: the exact login and failure texts | A live SINTRAN boot on nd100x / RetroCore |
| Datex-P network messages | DATEX-P-Handbuch Teil 6 (osmocom.org) |
| JANET "Hello", NRS names, PSS gateway | JNT PAD Terminal Users' Guide (archive.org, Coloured Books archives) |
| Datapak (Norway), Transpac, Minitel | Televerket / France Télécom user guides |
| VMS DIRECTORY, SHOW USERS; RSX failure and BYE | OpenVMS user's manual; RSX MCR Operations Manual (bitsavers) |
| TSO LISTCAT / LISTDS / TIME | A captured TK4- session |
| 4.3BSD gettytab banner line, uname/finger/who output | tuhs.org (was down during the research) |
| PCBoard, Wildcat!, RemoteAccess screens | Their language files and default .ASC/.ANS screens |
| QSD, CHAOS, Lutzifer | textfiles.com (refused the connection), Phrack World News, Datenschleuder archives (ds.ccc.de) |

---

## 8. Sources

Networks:
ITU-T X.28 (12/97), Specs folder; X.28 and X.29 1988 Blue Book,
https://discmaster.textfiles.com/view/13618/InfoMagic%20Standards%20-%20January%201994.iso/ccitt/1988/troff/8_2_09.tro ;
BT PSS training module, https://euclideanspace.com/coms/network/convert/pad/pad_2.htm ;
Northern UTA220 manual, https://www.manualsdir.com/manuals/191827/northern-uta220_uta220k.html?page=91 ;
Edinburgh EMAS PAD notes, https://history.dcs.ed.ac.uk/archive/os/deimos/ercm09/emas-2900/paddoc.txt ;
Phrack 42/8 and 42/5, https://archives.phrack.org/issues/42/8.txt , https://archives.phrack.org/issues/42/5.txt ;
CompuServe/Tymnet guide, https://gopherproxy.meulie.net/infinitelyremote.com/0/HackerChronicles/HACK/CISS2.TXT ;
Austpac notes, https://cassini.mirrorservice.org/sites/ftp.wiretapped.net/pub/security/info/textfiles/australia-misc/austpac.notes.txt ;
DATEX-P-Handbuch Teil 6, https://osmocom.org/attachments/download_inline/5470/datex-p-teil6.pdf

DEC:
https://typewritten.org/Manual/DEC/VMS/5.4/helplib.hlb/LOGIN.html ;
https://archives.phrack.org/issues/35/7.txt ;
https://www.zx.net.nz/mirror/h71000.www7.hp.com/doc/73final/6489/6489pro_004.html ;
https://huggingface.co/datasets/Devilishcode/hacking/blob/main/phrack/issue42/10.txt ;
https://pdp-10.trailing-edge.com/BB-PBQUC-BM_1990/01/7-documentation/users.mem.html ;
https://classiccmp.org/mailman3/hyperkitty/list/cctalk@classiccmp.org/thread/YMTACBMTOGUXXVM4POVXK7LTQVMJT5K3 ;
https://gist.github.com/tuklusan/e7fecafecb077637da6c8ff0002c35a0 ;
https://phrack.org/issues/7/hacking-rsts-part-1.html ;
https://www.tuhs.org/Usenet/comp.unix.ultrix/1991-January/001193.html

Unix:
https://github.com/dspinellis/unix-history-repo (tag BSD-4_3) ;
https://www.cs.toronto.edu/sunmanagers/1993/0681.html ;
https://mirrors.nycbug.org/pub/The_Unix_Archive/Unix_Usenet/comp.sys.sun/1989-June/000389.html ;
https://www.cs.toronto.edu/sunmanagers/1996/0089.html ;
https://www.tuhs.org/Usenet/comp.unix.sysv386/1991-February/000860.html ;
https://mail.typewritten.org/Manual/HP/HP-UX/9.00/man1/login.html ;
https://gist.github.com/dillera/4505557f8dff497549ec0a466cb9b660 ;
https://www.tuhs.org/Usenet/comp.unix.xenix.sco/1991-May/002344.html

IBM:
https://www.rfc-editor.org/rfc/rfc390.txt ;
https://peppe8o.com/download/python/mainframe/mvs_tk4-_v1.00_users_manual.pdf ;
https://www.ibm.com/docs/SSLTBW_2.2.0/com.ibm.zos.v2r2.ikjm100/ikj56700r.htm (and cd02362, cd02353, cd02113) ;
https://knowledge.broadcom.com/external/article/267453/ikj56710i-invalid-userid.html ;
https://knowledge.broadcom.com/external/article/53764/ikj56421i-with-tso-signon.html ;
https://knowledge.broadcom.com/external/article/205832/ikj56500i-command-ls-not-found-running-l.html ;
https://dinoland.emuframe.com/viewtopic.php?t=2850 ;
VM/370 Terminal User's Guide GC20-1810-9,
https://archive.decromancer.ca/bitsavers.org/pdf/ibm/370/VM_370/Release_6/GC20-1810-9_VM370_Terminal_Users_Guide_Rel_6_PLC_9_Mar80.pdf ;
https://www.ibm.com/support/pages/interactive-sign-screen ;
https://ftpdocs.broadcom.com/cadocs/0/CA%20Automation%20Point%2011%205-ENU/Bookshelf_Files/HTML/AP--AdminGd/1280309.html ;
https://community.ibm.com/community/user/discussion/regarding-cpf1107-password-not-correct-for-user-profile

Other systems:
NDInsight: `Operations/SINTRAN/ND-30.003.007 EN SINTRAN III System Supervisor.md`,
`Operations/SINTRAN/SINTRAN-Operations-Guide.md`, `Operations/Cosmos/ND-60.163.4 EN COSMOS User Guide.md` ;
https://archives.phrack.org/issues/10/6.txt ; https://archives.phrack.org/issues/16/3.txt ;
https://web.mit.edu/multics-history/source/Multics/doc/facilities_data_dir/tut_logging_in ;
https://web.mit.edu/multics-history/source/Multics/doc/info_segments/login.info

BBSes and chat:
https://raw.githubusercontent.com/matjam/maximus/master/lang/english.mad ;
https://discmaster.textfiles.com/file/7505/ftp.wwiv.com.zip/ftp.wwiv.com/pub/WW4SHARE/W4USRTXT.ZIP/W4USRTXT.DOC ;
https://discmaster.textfiles.com/view/553/EuroCD%203.iso/Communication/Citadel_BBS/Docs/cit_helps.lha/cit_hlp.lha/NOVICE.HLP ;
https://citadel.org/room_and_message_commands.html ;
https://huggingface.co/datasets/Devilishcode/hacking/blob/main/phrack/issue21/4.txt

---

## 9. ARPANET (1969-1983)

Added 2026-11-18. The best source is **NIC 11863, "Scenarios for using the ARPANET at the ICCC" (October
1972)**: a booklet of real sessions typed from a TIP, read page by page (marked [verbatim NIC 11863]). Others
are marked [verbatim per fetch summary] when the fetch tool reported an exact quote that was not seen on the
page itself.

### 9.1 The machines

The first four nodes (1969): IMP 1 UCLA, SDS Sigma 7, SEX; IMP 2 SRI (Engelbart's ARC), SDS 940, Genie
(Berkeley Timesharing) running NLS; IMP 3 UCSB, IBM 360/75, OS/MVT (online service "OLS"); IMP 4 University of
Utah, DEC PDP-10 (TENEX from about 1970). The IMPs were Honeywell DDP-516s. On 29 Oct 1969 the first login,
UCLA to SRI, crashed after "LO".

Hosts in **RFC 597 "Host Status" (December 1973)**, host numbers octal:

| Host name | Site | Machine | OS |
|---|---|---|---|
| UCLA-NMC (1) | UCLA | Sigma 7; PDP-11/45 | SEX; ANTS |
| UCLA-CCN (101) | UCLA | IBM 360/91 | TSO (session below) |
| SRI-ARC (2) | SRI | PDP-10 | TENEX, NLS |
| SRI-AI (102) | SRI | PDP-10 | TENEX |
| UCSB-MOD75 (3) | UCSB | IBM 360/75 | OLS |
| UTAH-10 (4) | Utah | PDP-10 | TENEX |
| BBN-TENEX (105), BBN-TENEXB (205) | BBN | PDP-10 | TENEX |
| BBN-NCC (50) | BBN | Honeywell H-316 | Network Control Center |
| MIT-MULTICS (6) | MIT | Honeywell 6180 | Multics |
| MIT-DMS (106), MIT-AI (206), MIT-ML (306) | MIT | PDP-10 | ITS |
| RAND-RCC (7) | RAND | IBM 370/158 | - |
| SDC-LAB (10) | SDC | IBM 370/145 | - |
| HARV-10 (11), HARV-1 (111), HARV-11 (211) | Harvard | PDP-10, PDP-1, PDP-11 | - |
| LL-67 (12), LL-TX2 (112) | Lincoln Lab | IBM 360/67, TX-2 | - |
| SU-AI (13) | Stanford | PDP-10 | SAIL (later called WAITS) |
| ILL-CAC (14), ILL-NTS (114) | Illinois | PDP-11 | ANTS |
| CASE-10 (15) | Case | PDP-10 | TENEX |
| CMU-10A (116), CMU-10B (16) | CMU | PDP-10 | - |
| I4-TENEX (17) | ILLIAC IV | PDP-10 front end | TENEX |
| AMES-67 (20) | NASA Ames | IBM 360/67 | - |
| USC-ISI (126), ISI-DEVTENEX (226) | ISI | PDP-10 | TENEX (TOPS-20 later) |
| USC-44 (27) | USC | IBM 360/44 | - |
| CCA-TENEX (37) | CCA | PDP-10 | TENEX + Datacomputer |
| PARC-MAXC (40) | Xerox PARC | MAXC | TENEX |
| FNWC (41) | Fleet Numerical Weather Central | CDC 6500 | - |
| UCSD-CC (43) | UCSD | Burroughs B6700 | - |
| HAWAII-ALOHA (44) | Hawaii | HP 2100 | - |
| OFFICE-1 (53) | Office-1 | PDP-10 | TENEX, NLS |
| UKICS-360 (52) | UK (via UCL) | IBM 360/195 through a PDP-9 | - |

`-` means RFC 597 gives no OS. TIPs (terminal boxes) at Utah, Ames, MITRE, RADC, NBS, USC, BBN, CCA, FNWC,
ALOHA, NORSAR, UKICS, Tymshare, Rutgers and others. In June 1973 a satellite link joined **NORSAR in Norway**
(a TIP, NORSAR-TIP 251) through the Tanum earth station in Sweden, with a line on to the **UCL TIP in London**.
These were IMP links, not X.25. Not yet checked: RFC 820, RFC 952/953 and the March 1977 logical map
(MIT-MC, SRI-NIC on TOPS-20).

### 9.2 TIP and TAC: how a user got onto the network

TIP, 1972 [verbatim NIC 11863]: `@r` (reset), `@e r` (echo remote), `@L <host#>` (log in to a host), `@c`
(close). Replies:

```text
LOGGER
T R OPEN
R OPEN T OPEN
T R CLOSED
```

TIP messages (TIP user's guide) [verbatim per fetch summary]: `Trying...`, `Open`, `Closed`, `Can't`,
`Refused`, `Net Trouble`, `Host not responding`, `Host Scheduled Down Until Sat. at 1850 GMT.`,
`BAD` (unknown command), `NO`, `Host broke the connection`, `TIP GOING DOWN`. A host that cannot be reached:
`DEAD`.

TAC, 1982 (BBN Report 4780) [verbatim per fetch summary]: banner template `<sitename> TAC <version #>:<port #>`;
`@O <host>` gives `TCP Trying...` then `Open`; failures `Destination host dead`, `Host down until <day> at
<hour>:<minutes> <timezone>.`, `Refused`, `Destination Unreachable`, `TAC's IMP down`; close `Closed`,
`Host closing connection`; unknown command `Bad`; `TAC going down in <mins> mins for <hrs> hrs <mins> mins.`

### 9.3 MIT ITS (MIT-AI, MIT-DMCG)

MIT-AI 1972, after `@L 134` and Ctrl-Z (ITS shows nothing until Ctrl-Z) [verbatim NIC 11863]:

```text
ITS.761.  DDT.460.
7. USERS
```

MIT-DMCG 1972 [verbatim NIC 11863]: `MIT PROJECT MAC DMCG PDP-10.` Log in with `:login iccXXX` (no prompt: "you
must type the ':'"). `who`:

```text
TTY UNAME  JNAME  CORE TOTAL IDX
T07 SYS    SYS    035  035   01
T13 PDL    T      007  008   03
T31 ICCXXX HACTRN 001  001   23
FREE CORE 117/208
```

Mail to an unknown user: `USER HAS NO DIRECTORY, MAIL GOING TO COM.` Logout: `ITS 761 CONSOLE 31 FREE`.

Modern KA ITS 1651 (PiDP-10) [verbatim per fetch summary]:

```text
KA ITS.1651. DDT.1548
TTY 52
You're all alone, Fair share = 99%
Welcome to ITS!
```

### 9.4 TENEX (BBN, SRI-ARC)

[verbatim NIC 11863]

```text
BBN-TENEX 1.29.6, SYSTEM-A EXEC 1.43
@ login iccc
(PASSWORD) iccc
(ACCOUNT #) 11514
JOB 1 ON TTY103 2-SEP-72 16:39
@ sys
UP 87:19:48  3 JOBS
LOAD AV   0.34   0.45   1.00
JOB  TTY  USER       SUBSYS
1    103  ICCC       EXEC
9    101  TEITELMAN  LISP
12   7    TOMLINSON  (PRIV)
@ day
SATURDAY SEPTEMBER 2, 1972 17:00:20
@ logout
KILLED JOB 1 USER ICCC, ACCT 11514, TTY 103 AT 9/2/72 15:01
  USED 0:0:9 IN 0:22:10
```

SRI-ARC: `ARC-TENEX 1.29.00 DATE  ARC EXEC 1.38.00`. `?` lists `COMMANDS ARE:`; `where tomlinson` gives
`TTY7, JOB 12`; `telnet` gives `USER TELNET 29 MAR 72.  TYPE HELP CR FOR HELP.` and the prompt `#`. The NIC
Resource Query on ARC-TENEX:

```text
@nic (Resource Query)
TYPE ? IF YOU NEED HELP AT ANY POINT.
-show sri-arc
(SRI-ARC)        Stanford Research Institute
                 Augmentation Research Center (ARC)
                 Network Information Center (NIC)
```

### 9.5 Stanford WAITS (SU-AI, 1979)

SUAI.TXT[S,NET] [verbatim per fetch summary; the brackets mark what the system typed]:

```text
[SU-AI WAITS 9.08/C  Assembled 12/16/79]
[.]login PRJ/PRG
[Job 2 SU-AI WAITS 9.08/C Assembled 12/16/79]
[Password=] PASSWORD
[Saturday 22-Dec-79 1735 71F 22C]
[.]
```

Prompt `.`; "There is no general guest account." Logout: Ctrl-C twice, then `kjob`:

```text
[job NN PRG,PRG Logged off TTY64 17:38 22-Dec-79]
[0.05 hours, console time]
[0.00 minutes, cpu time]
[Kjob]
```

### 9.6 DEC TOPS-10

[verbatim per fetch summary]

```text
PITT DEC-1099/A 63A.41B 15:36:41 TTY43 system 1237/1240
PLEASE LOGIN OR ATTACH
.LOGIN 10/2162
JOB 35 PITT DEC-1099/B 63A.431B TTY43 Wed 7-May-80 1619
Password:
System ready!
.
```

(The `.LOGIN` line is from DEC's LOGIN.DOC example; Pittsburgh's own login line is not shown.) If jobs are
left detached: `Other jobs detached with same PPN: 37` / `Type job number to ATTACH or carriage-return to LOGIN
a new job:`. Logout `K/F`:

```text
JOB 16 [115103,320571] off TTY43 at 2032 9-May-80 Connect=29 Min
Disk R+W=83+76 Tape IO=0 Saved all files (450 blocks)
```

### 9.7 UCLA CCN IBM 360/91 TSO (1972)

`@L 65` [verbatim NIC 11863; `X.X`, dates and the `IKJ...` codes are placeholders printed in the booklet]:

```text
UCLA CCN 360/91 SERVER TELNET.
VERSION X.X DD DDD 1972
ENTER COMMAND OR 'HELP':
tso
WELCOME TO UCLA CCN TSO
IKJNJJJA ENTER LOGON -
logon icc
ENTER PASSWORD
IKJNNNNI UUU LOGON IN PROCESS AT T:TT:TT ON DD DD, 1972
READY
```

### 9.8 Still to read

NIC 11863 pages 17-20, 23-29, 33-60 (Multics, Harvard PDP-10, SAIL, MATHLAB, UCSB-75 OLS, UCLA-NMC Sigma 7);
the SDS 940 Terminal User's Guide (901118B, 1968, "Sample Session at the Teletype"); TENEX failure and
unknown-command texts; a TOPS-10 SYSTAT.

Sources: NIC 11863,
https://archive.computerhistory.org/resources/access/text/2019/07/102784024-05-001-acc.pdf ;
RFC 597, https://www.rfc-editor.org/rfc/rfc597.txt ; RFC 386, https://www.rfc-editor.org/rfc/rfc386.html ;
BBN TIP guide, https://www.walden-family.com/dave/archive/bbn-tip-man.txt ;
TAC Users' Guide, https://www.saildart.org/TAC[UP,DOC]1 ; SUAI.TXT, https://www.saildart.org/SUAI.TXT[S,NET] ;
PiDP-10 guide, https://groups.google.com/g/pidp-10/c/-UTX6Jx0bXc ;
TOPS-10 LOGIN.DOC, https://pdp-10.trailing-edge.com/bb-d549g-sb/01/login.doc.html ;
Sze, Introduction to DEC System-10,
https://archive.org/stream/bitsavers_decpdp10Szystem101974_17096596/Sze_Introduction_to_DEC_System-10_1974_djvu.txt ;
Introduction to ITS, https://josephoswald.nfshost.com/itsinfo/its-intro.html ;
TENEX 134.DOC, https://bitsavers.mirrorservice.org/bits/BBN/Tenex/134-tenex/134.DOC ;
SDS 940 guide, https://ftp.mirrorservice.org/sites/www.bitsavers.org/pdf/sds/9xx/940/901118B_940_TerminalUsersGuide_Apr68.pdf ;
Wikipedia ARPANET, https://en.wikipedia.org/wiki/ARPANET

---

## 10. From the X.25 Network Atlas: what else was on the networks

Added 2026-11-18. The "X.25 Network Atlas" (a standalone HTML page kept outside this repository) holds 4870 host
records built from Phrack (27/4, 21/4, 42, 43/26, 45/8), textfiles.com (`dpacnuas.txt`, `telenet2.txt`,
`tymnet.txt`), the TYMNET docs repository and Wikipedia. Each record has a name, NUA, DNIC, network, country,
category, operator, hardware (rarely), year and source. For the big scan lists the name is the first line the
host sent when called; the file holds no whole login dialogues.

### 10.1 Counts by system (keyword match on name and hardware; groups overlap)

PRIMOS/PRIMENET ~401, VAX/VMS ~370, Citibank systems 272, Unix ~172, VM/CMS 89, AOS/VS 86, TSO/MVS 34,
MPE 35, CDC NOS/Cyber 34, Dialcom 34, Stratus VOS ~36, DIALOG 28, ORBIT 24, Westlaw 24, VU/TEXT 35, ND (NORD/
SINTRAN) 19, Lexis/Nexis 17, DEC-10/TOPS-20 16, RSX/RSTS 14, ICL 14, DECserver 13, Univac 11, Honeywell/GCOS 11,
Tandem TACL 11, STN 10, Multics ~9, Pick 8, Altos ~5, Burroughs 4, CICS 7, outdials ~469 (marked "infra").
By network: SprintNet/Telenet ~2055, Datapac 1442, Tymnet 746, PSS ~233, Datex-P ~160, DATAPAK (Norway) 57.

First lines from the scan lists [verbatim, Atlas `n` field]:

```text
PRIMOS    22.0.3.R37    BLTCAD
FCSA System VAX/VMS 4.2
Dell UNIX SysV Rel 4.(i386/486)Sysname:Atlantis
HP-UX     lenny A.08.02 E 9000/847
enter v for vtam(roscoe or tso) d for dobis
Enter "CMS userid", "TSO userid ", "SIMVTAM termid"
CANADA.MT2.SALOMON    GENIUS:  (MPE XL)
CYBER     (7/E/1)
Trinity College Dublin TOPS-20
UNITEL DIALCOM 19.4Q.111  System 20
STARMASTER   STC XMUX PAD  USER NAME?
X.29 Link Established
DATAPAC : SYSTEM READY
TACL 1>
System/32 VOS Module %can_bryker#M1
Recku Univac (Enter @@ENQ)
BMS==>
ILLEGAL SOURCE ADDRESS 0B 80
PLEASE ENTER SUBSCRIBERID;PASSWORD
WHAT SERVICE PLEASE????
You are not authorized to connect to this machine.
Your call cannot be completed (unknown destination).
Minitel  USER ID:
```

Norwegian entries: `Oslo SCANNET NSI Nord-100` (2422110000002, DATAPAK), `Tromso University NORD-500 (UNINETT)`,
`SCANNET Node (NORD-12)`, `IFE Kjeller - Cyber 170 RBK UNINETT`, `Bergen By Byte (BBB) Mailbox`, `Chatsystem
Norway`, and Univacs in Bergen and Trondheim.

### 10.2 Tandem NonStop Guardian (TACL)

HP NonStop G-Series user manual, D10, 1992 [verbatim]:

```text
Good Morning. Welcome to \MEL
TACL (T9205D10 - 08JUN92), Operating System D10
COPYRIGHT TANDEM COMPUTERS INCORPORATED 1985, 1987, 1988, 1989, 1991, 1992
CPU 1, backup process in CPU 2
February 20, 1992 9:15:23
1>
2> LOGON SUPPORT.ALICE
Password:
```

Over Datapac a caller often saw only `TACL 1>` (dpacnuas.txt). The prompt number counts the commands. After
three wrong logons TACL ignores the terminal for a minute; `LOGOFF`, `EXIT` or Ctrl-Y end the session
[from a summary of the Guardian manual]. NOT FOUND: the failure text, STATUS and WHO output.

### 10.3 Stratus VOS

OpenVOS 17.1 (2013), the same format as the 1991 description in Phrack 37/8 [verbatim]:

```text
OpenVOS Release 17.1.0ax, Module %phx_vos#m17
Please login  14:04:40
login nd
Password?
Noah_Davids.CAC logged in on %phx_vos#m17 at 13-01-09 13:06:35 mdt.
ready  13:06:35
```

Older form (Phrack 37/8): `System ???, VOS Release v.(version), Module ???`, then `Login` with `User_name:` and
`Password?`. User names are `Person.Group`. Commands: `WHO`, `LIST_MODULES`, `DISPLAY_CURRENT_DIR`, `LIST`,
`DISPLAY`, `SEND_MESSAGE`, `HELP`. NOT FOUND: unknown-command text, logout text.

### 10.4 Pick

guidehak.txt, 1993 [verbatim]: `07 JUN 1993 04:00:21 Logon please:`. The TCL prompt is `>`. Logout, from
mvBase, a later Pick (2014) [verbatim per search summary]:

```text
>OFF
Connect time=1 mins.; CPU= 1 units; LPTR pages= 0
Logged off at 08:40:10 on 26 MAR 2014
Logon Please:
```

### 10.5 CDC NOS (Cyber)

cyberhac.txt, NOS 2.5.2 on a Cyber 170-730, 1988 [verbatim; "WeLCOME" is in the original]:

```text
WeLCOME TO THE NOS SOFTWARE SYSTEM.
COPYRIGHT CONTROL DATA 1978, 1987.
88/02/16. 02.36.53. N265100
CSUS CYBER 170-730.                     NOS 2.5.2-678/3.
FAMILY:
USER NAME:
JSN: APXV, NAMIAF
/
```

Everything may be typed on one line at `FAMILY:` (`,user,password`). Three password tries. Commands
`CATLIST`, `ENQUIRE,F`, `LIMITS`, `INFO`, `GET,fn`, `LIST,F=fn`, `BYE`. NOT FOUND: failure text, NOS/VE.

### 10.6 Siemens BS2000

Logout, BS2000 V19 manual [verbatim; a modern version, the `%` message form is the old one]:

```text
/LOGOFF NOSPOOL,BUT
%  EXC0419 /LOGOFF AT 1505 ON <date> FOR TSN '52IS'
%  EXC0421 CPU TIME USED: 1.5734
```

Commands start with `/`. NOT FOUND: the `/LOGON` dialogue.

### 10.7 Burroughs / Unisys MCP (CANDE)

BYE, CANDE manual example from 1978 [verbatim per search summary]:

```text
BYE
#END SESSION 3003 ET=1:18:19.6 PT=1:08.9 IO=1:02.0
#USER = UZER 11:27:06 04/20/78
```

NOT FOUND: the login and the `#` prompt. NOT FOUND at all: Honeywell GCOS TSS, Univac OS 1100 (`@@ENQ`,
`@RUN`, `@FIN`), ICL VME, Wang VS, Sequent DYNIX. Sources to read: bitsavers GCOS TSS Reference DJ31 (1979),
Georgia Tech 1108 Demand Users Manual (1972).

### 10.8 Online services

**DIALOG** (Phrack 39/5, 1992, through Dialog's own network) [verbatim]:

```text
DIALNET: call connected
DIALOG INFORMATION SERVICES
PLEASE LOGON:
ENTER PASSWORD:
Enter Service: dialog
```

Network errors: `ERROR, RE-ENTER SERVICE`, `ALL PORTS BUSY`, `HOST DOWN`, `CIRCUITS BUSY`, `DIALNET: CALL
CLEARED`, `BY REQUEST`, `DROPPED BY HOST SYSTEM`. 1986 (Phrack 9/5): `? B296`, then
`$0.12      Estimated Total Cost` and `File296:ONTAP TRADEMARKSCAN - O.G.`. A search (1992):

```text
? SELECT (BICMOS OR CMOS) AND SRAM
       138 BICMOS
      1378 CMOS
       681 SRAM
S1     203 (BICMOS OR CMOS) AND SRAM
```

NOT FOUND: the LOGOFF cost screen.

**Dialcom** (the Prime-based mail system behind Telecom Gold; Phrack 46/14, 1994, dates garbled on purpose by
the author) [verbatim]:

```text
Primecom Network 19.4Q.111 System 666
 Please Sign On
>ID HBT007 IMEL8
Dialcom Computer Services 19.4Q.111(666)
On At 14:44 07/32/94 EDT
Last On At 4:09 06/44/94 EDT
>WHO
Herd Beast <S666-6>HBT007
>SY
41 Users on sys 666
>off
Off At 14:45 07/32/94 EDT
Time used: 00h 00m connect, 00m 01s CPU, 00m 00s I/O.
```

`MAIL` asks `Send, Read or Scan:`. A dropped line gives `You Have a Disconnected Job:` / `Do You Want to
Reconnect?`. Compare Telecom Gold in 1.2: the same software.

**STN International** (ACS training guide, 1993) [verbatim]:

```text
Welcome to STN International! Please enter X:
LOGINID:
PASSWORD:
TERMINAL (ENTER 1,2,3 OR ?):
FILE 'HOME' ENTERED AT 14:06:01 ON 10 FEB1993
=> LOGOFF
ALL L# QUERIES AND ANSWER SETS ARE DELETED AT LOGOFF
LOGOFF? (Y)/N/HOLD:Y
COST IN U.S. DOLLARS            SINCE FILE      TOTAL
                                ENTRY           SESSION
FULL ESTIMATED COST             15.28           15.53
STN INTERNATIONAL LOGOFF AT 14:12:03 ON 10 FEB 93
```

**CompuServe**: through Tymnet `PLEASE TYPE YOUR TERMINAL ID:` `A`, `PLEASE LOG IN` `CIS02`; then
`User ID:` (form `76003,5`) and `Password:`. 1982 menu: `COMPUSERVE INFORMATION SERVICE`, `Key digit or H for
description`, `Go HOM-1`. `BYE` or `OFF` logs off. NOT FOUND: the `!` prompt and the logoff text.

NOT FOUND: ESA-IRS, Data-Star, ORBIT, BRS, LEXIS/NEXIS, Westlaw, VU/TEXT, MEDLINE, The Major BBS, Minitel
screens.

### 10.9 Network front ends, gateways, outdials

SprintNet host replies, Phrack 42/9 (1992) [verbatim]:

```text
MHP1201I TERMINAL CONNECTED TO PACKET/74
MHP201A A00B1101  * VERSION 5.5.3 *.
MHP201A UPK12X01 APPLICATION:
CONNECTED TO PACKET/94
*** ENTER LOGON
WESTINGHOUSE SNA NETWORK - ENTER: L APPLNAME
SPRINT.COM SunLink X.29 service
enter system id --
This is SKMIC4 - Authorized use only
PRIMENET 23.2.0vB PROD-A
```

`MHP...` sits in front of IBM SNA sites; the product that prints it is NOT FOUND. A CICS site answered only
`DFH2001I` (Phrack 21/4). DECserver [verbatim, DECserver 700]: `Local> `, then
`Local -010- Session 1 to U1VAX1CP on node LAT_08002BBBED29 established`.

Telenet outdials (Lorax, "How To Use Outdials", textfiles): after `CONNECTED`, Ctrl-E a few times and Return
gives a message like `*Hello, I'm Ready*`; `D` asks for the number; `Dialing...`; `@` and Return leaves.
NOT FOUND: Hayes `AT` commands on these outdials, "WHAT SERVICE PLEASE????" and "ILLEGAL SOURCE ADDRESS" owners,
VTAM USS screens, CESN, Sim3278, WYLBUR, Novell, CERN Multigate, CCC/netmbx/QSD/Lutzifer screens, the Norwegian
mailboxes, the ND X.29 server text beyond `ND X.29 Server - Press 'ESCAPE' to log in` (section 2.1).

Sources: https://www.manualsdir.com/manuals/396922/hp-nonstop-g-series.html?page=35 ;
http://www.textfiles.com/hacking/dpacnuas.txt ; http://www.textfiles.com/hacking/guidehak.txt ;
http://www.textfiles.com/hacking/cyberhac.txt ; https://www.stratus.com/?p=1908 ;
http://phrack.org/archives/issues/37/8.txt ;
https://www3.rocketsoftware.com/rocketd3/support/documentation/mvb/32/refman/fileacct/logging_on_and_off2.htm ;
https://bs2manuals.ts.fujitsu.com/psBS2000OPERATINGSYSTEMV210en/lgoff-terminate-job-osd_makroat_en-sp23-1-126-5104443.html ;
https://www.support.unisys.com/aseries/docs/ClearPath-MCP-20.0/86001500-511/section-000023826.html ;
https://archives.phrack.org/issues/39/5.txt ; https://archives.phrack.org/issues/9/5.txt ;
https://archives.phrack.org/issues/46/14.txt ; https://web.stanford.edu/group/swain/cinf/casreg/app01.html ;
https://web.stanford.edu/group/swain/cinf/casreg/app02.html ;
https://gopherproxy.meulie.net/infinitelyremote.com/0/HackerChronicles/HACK/CISS2.TXT ;
https://www.trs-80.org/compuserve-menus-in-1982.html ;
https://huggingface.co/datasets/Devilishcode/hacking/raw/main/phrack/issue42/9.txt ;
https://huggingface.co/datasets/Devilishcode/hacking/raw/main/phrack/issue21/4.txt ;
http://www.textfiles.com/phreak/outdial.txt ;
https://community.hpe.com/t5/operating-system-openvms/decserv-700-16-re-2/m-p/3642833