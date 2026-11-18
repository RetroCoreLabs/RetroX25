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
