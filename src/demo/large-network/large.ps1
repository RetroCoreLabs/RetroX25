<#
.SYNOPSIS
  A large demo network made from network.ini: any number of countries joined by X.75, each with a PAD line, an echo
  service and a host line, and the hosts of the library (../hosts) spread over them. See README.md here.

.EXAMPLE
  ./large.ps1 generate         # write generated/<country>.ini and generated/machines.ini from network.ini
  ./large.ps1 start            # generate, then start every switch, echo service and the host program
  ./large.ps1 pad Norway       # a PAD on Norway's PAD line; then e.g. CALL 03106003604
  ./large.ps1 hosts            # every host and the number to call it on
  ./large.ps1 status
  ./large.ps1 stop

.NOTES
  -BinDir    the folder with x25switch.exe, x25echo.exe, x25host.exe, x25pad.exe (default src\publish\win-x64,
             made by src\build.ps1 or ../x75-countries/x75.ps1 build).
  -StateDir  where generated/, logs/ and the process-id file go (default: this folder).

  Ports, for country number i (its place in network.ini, counting from 0):
    27000 + 100*i + 1   PAD line            27000 + 100*i + 2   echo service line
    27000 + 100*i + 3   host line           27000 + 100*i + 10 + k   the k-th link this country listens on
    9400 + i            management console  4400 + i            monitor
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet('generate', 'start', 'stop', 'status', 'pad', 'hosts')]
    [string]$Command = 'status',
    [Parameter(Position = 1)]
    [string]$Arg = '',

    [string]$BinDir = '',
    [string]$StateDir = ''
)

$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Root = Split-Path -Parent (Split-Path -Parent $Here)
if (-not $StateDir) { $StateDir = $Here }
$Gen = Join-Path $StateDir 'generated'
$Logs = Join-Path $StateDir 'logs'
$PidFile = Join-Path $StateDir '.pids'
if (-not $BinDir) { $BinDir = Join-Path $Root 'publish/win-x64' }

function Program([string]$Name) {
    $exe = Join-Path $BinDir "$Name.exe"
    if (-not (Test-Path $exe)) { throw "$exe not found - run src\build.ps1 first (or pass -BinDir)" }
    return $exe
}

# Reads an ini file into an ordered table of sections, each an ordered table of keys. '#' starts a comment.
function Read-Ini([string]$Path) {
    $sections = [ordered]@{}
    $current = $null
    $n = 0
    foreach ($raw in Get-Content $Path) {
        $n++
        $line = ($raw -replace '#.*$', '').Trim()
        if (-not $line) { continue }
        if ($line -match '^\[(.+)\]$') {
            $current = [ordered]@{}
            $sections[$Matches[1]] = $current
            continue
        }
        if ($null -eq $current -or $line -notmatch '^([^=]+)=(.*)$') { throw "${Path}:${n}: not 'key = value' inside a [section]: $raw" }
        $current[$Matches[1].Trim()] = $Matches[2].Trim()
    }
    return $sections
}

# Reads network.ini and checks it: the names, the DNICs, that every link and host names a country, that every host
# type is in the library, and that every country can be reached from every other one.
function Read-Network {
    $ini = Read-Ini (Join-Path $Here 'network.ini')
    $countries = New-Object System.Collections.Generic.List[object]
    $byName = @{}
    $links = New-Object System.Collections.Generic.List[object]
    $hosts = New-Object System.Collections.Generic.List[object]
    foreach ($key in $ini.Keys) {
        $s = $ini[$key]
        if ($key -like 'country.*') {
            if ($s['dnic'] -notmatch '^\d{4}$') { throw "[$key]: dnic must be 4 digits" }
            if ($byName.ContainsKey($s['name'])) { throw "[$key]: country $($s['name']) is listed twice" }
            $c = [pscustomobject]@{ Name = $s['name']; Dnic = $s['dnic']; Index = $countries.Count; Links = New-Object System.Collections.Generic.List[object]; Hosts = 0 }
            $countries.Add($c); $byName[$c.Name] = $c
        }
    }
    foreach ($key in $ini.Keys) {
        $s = $ini[$key]
        if ($key -like 'link.*') {
            $a = $byName[$s['a']]; $b = $byName[$s['b']]
            if (-not $a -or -not $b) { throw "[$key]: a and b must be countries in this file" }
            if ($a -eq $b) { throw "[$key]: a country cannot be linked to itself" }
            # The first one listens: its k-th listening link gets port base + 10 + k.
            $listenSlot = 0
            foreach ($l in $a.Links) { if ($l.Listener -eq $a) { $listenSlot++ } }
            $link = [pscustomobject]@{ Listener = $a; Dialler = $b; Port = 27000 + 100 * $a.Index + 10 + $listenSlot }
            $a.Links.Add($link); $b.Links.Add($link); $links.Add($link)
        }
        elseif ($key -like 'host.*') {
            $c = $byName[$s['country']]
            if (-not $c) { throw "[$key]: country $($s['country']) is not in this file" }
            if ($s['type'] -ne 'script' -and -not (Test-Path (Join-Path $Here "../hosts/$($s['type'])/host.ini"))) { throw "[$key]: no host folder ../hosts/$($s['type'])" }
            # nua: the host's whole international number (X.121: the 4-digit DNIC, then the national part; at most
            # 15 digits, the longest an X.25 call carries). It must be in the host's own country and not the echo's.
            $nua = $s['nua']
            if ($nua -notmatch '^\d{5,15}$') { throw "[$key]: nua must be 5 to 15 digits" }
            if (-not $nua.StartsWith($c.Dnic)) { throw "[$key]: nua $nua does not start with $($c.Name)'s DNIC $($c.Dnic)" }
            if ($nua -eq "$($c.Dnic)9001") { throw "[$key]: nua $nua is $($c.Name)'s echo service" }
            foreach ($o in $hosts) { if ($o.Nua -eq $nua) { throw "[$key]: nua $nua is also $($o.Hostname)'s" } }
            $c.Hosts++
            $hosts.Add([pscustomobject]@{ Country = $c; Type = $s['type']; Script = $s['script']; Hostname = $s['hostname']; Nua = $nua })
        }
    }
    if ($countries.Count -eq 0) { throw 'network.ini has no [country.N]' }
    foreach ($c in $countries) { $null = Get-NextHops $c $countries }
    return [pscustomobject]@{ Countries = $countries; Links = $links; Hosts = $hosts }
}

# For one country: for every other country, the link a call to it leaves on - the first link of the shortest chain
# (breadth-first, so fewest hops; on a tie, the link listed first in network.ini). Throws when one cannot be reached.
function Get-NextHops($From, $Countries) {
    $first = @{}
    $first[$From.Name] = $null
    $queue = New-Object System.Collections.Generic.Queue[object]
    $queue.Enqueue($From)
    while ($queue.Count -gt 0) {
        $c = $queue.Dequeue()
        foreach ($l in $c.Links) {
            $other = if ($l.Listener -eq $c) { $l.Dialler } else { $l.Listener }
            if ($first.ContainsKey($other.Name)) { continue }
            $first[$other.Name] = if ($c -eq $From) { $l } else { $first[$c.Name] }
            $queue.Enqueue($other)
        }
    }
    foreach ($c in $Countries) {
        if (-not $first.ContainsKey($c.Name)) { throw "$($From.Name) cannot reach $($c.Name): no chain of links joins them" }
    }
    return $first
}

function Write-Network {
    $net = Read-Network
    New-Item -ItemType Directory -Force -Path $Gen | Out-Null
    foreach ($c in $net.Countries) {
        $base = 27000 + 100 * $c.Index
        $hops = Get-NextHops $c $net.Countries
        $o = New-Object System.Text.StringBuilder
        $null = $o.AppendLine("# $($c.Name) - DNIC $($c.Dnic). Written by large.ps1 from network.ini; edit that file, not this one.")
        $null = $o.AppendLine('')
        $null = $o.AppendLine("[switch]`nname = $($c.Name)`ndnic = $($c.Dnic)`nmgmt_port = $(9400 + $c.Index)`nmonitor_port = $(4400 + $c.Index)`nmgmt_addr = $($c.Dnic)0000`ncdr_file = logs/$($c.Name.ToLowerInvariant())-calls.txt")
        $null = $o.AppendLine('')
        # Users dial another country with the international prefix 0 (X.121 7.8.1), as in ../x75-countries.
        $null = $o.AppendLine("[network]`nown_dnic = $($c.Dnic)`ninternational_prefix = 0")
        $null = $o.AppendLine('')
        $null = $o.AppendLine("[port.0]`nname = PAD users`ntype = access`ntransport = xot`nmode = dce`nhost = 0.0.0.0`nport = $($base + 1)`nlci_count = 64`ncalling_address_base = $($c.Dnic)7000")
        $null = $o.AppendLine('')
        $null = $o.AppendLine("[port.1]`nname = Echo $($c.Dnic)9001`ntype = access`ntransport = xot`nmode = dce`nhost = 0.0.0.0`nport = $($base + 2)`nlci_count = 64")
        $null = $o.AppendLine('')
        $null = $o.AppendLine("[port.2]`nname = Hosts`ntype = access`ntransport = xot`nmode = dce`nhost = 0.0.0.0`nport = $($base + 3)`nlci_count = 64")
        $portOf = @{}
        $p = 3
        foreach ($l in $c.Links) {
            $listen = $l.Listener -eq $c
            $peer = if ($listen) { $l.Dialler } else { $l.Listener }
            $portOf[$l] = $p
            $null = $o.AppendLine('')
            # The listening end is the DCE and searches channels from the lowest, the dialling end the DTE (X.75 3.1.4).
            $null = $o.AppendLine("[port.$p]`nname = X.75 $(if ($listen) { 'from' } else { 'to' }) $($peer.Name)`ntype = trunk`ntrunk_kind = network`ntransport = hdlc-tcp`nmode = $(if ($listen) { 'dce' } else { 'dte' })`nhost = $(if ($listen) { '0.0.0.0' } else { '127.0.0.1' })`nport = $($l.Port)`nlci_count = 64`npeer_name = $($peer.Name)`npeer_monitor_port = $(4400 + $peer.Index)`npeer_mgmt_addr = $($peer.Dnic)0000")
            $p++
        }
        $null = $o.AppendLine('')
        $null = $o.AppendLine("[route.0]`nname = Echo`nnumber = $($c.Dnic)9001`nport = 1")
        $null = $o.AppendLine('')
        # One exact route per host: the numbers are real ones from the X.25 Network Atlas and share no common prefix.
        $r = 1
        foreach ($h in $net.Hosts) {
            if ($h.Country -ne $c) { continue }
            $null = $o.AppendLine('')
            $null = $o.AppendLine("[route.$r]`nname = $($h.Hostname)`nnumber = $($h.Nua)`nport = 2")
            $r++
        }
        foreach ($other in $net.Countries) {
            if ($other -eq $c) { continue }
            $l = $hops[$other.Name]
            $via = if ($l.Listener -eq $c) { $l.Dialler } else { $l.Listener }
            $null = $o.AppendLine('')
            $null = $o.AppendLine("[route.$r]`nname = $($other.Name)$(if ($via -ne $other) { " via $($via.Name)" })`ndnic = $($other.Dnic)`nport = $($portOf[$l])")
            $r++
        }
        $null = $o.AppendLine('')
        $null = $o.AppendLine("[user.0]`nname = admin`npassword = admin`nlevel = 1")
        Set-Content -Path (Join-Path $Gen "$($c.Name.ToLowerInvariant()).ini") -Value $o.ToString().Replace("`r`n", "`n") -NoNewline
    }

    $m = New-Object System.Text.StringBuilder
    $null = $m.AppendLine('# The hosts of network.ini, all run by one x25host. Written by large.ps1; edit network.ini, not this file.')
    $null = $m.AppendLine('')
    # Relative to generated/, where this file is written (x25host reads it from beside the file, as in ../x75-countries).
    $null = $m.AppendLine("[defaults]`nlibrary = ../../hosts")
    $i = 0
    foreach ($h in $net.Hosts) {
        $null = $m.AppendLine('')
        $null = $m.AppendLine("[machine.$i]`ntype = $($h.Type)")
        if ($h.Type -eq 'script') { $null = $m.AppendLine("script = $($h.Script)") }
        $null = $m.AppendLine("hostname = $($h.Hostname)`nnua = $($h.Nua)`nswitch = 127.0.0.1:$(27000 + 100 * $h.Country.Index + 3)`ngroup = $($h.Country.Name.ToLowerInvariant())")
        $i++
    }
    Set-Content -Path (Join-Path $Gen 'machines.ini') -Value $m.ToString().Replace("`r`n", "`n") -NoNewline
    Write-Host "Wrote $($net.Countries.Count) switch files, $($net.Links.Count) links, $($net.Hosts.Count) hosts into $Gen"
    return $net
}

function Wait-Port([System.Diagnostics.Process]$Proc, [int]$Port, [string]$State, [string]$Log) {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if ($Proc.HasExited) { break }
        if (Get-NetTCPConnection -State $State -LocalPort $Port -ErrorAction SilentlyContinue) { return }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "  $Log : port $Port never $State (exited: $($Proc.HasExited)). Its output:" -ForegroundColor Red
    Get-Content (Join-Path $Logs $Log) -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" }
    Get-Content (Join-Path $Logs "$Log.err") -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" }
    Stop-All
    throw "start failed - see $Logs"
}

function Start-One([string]$Log, [string]$Exe, [object[]]$ProgramArgs) {
    $out = Join-Path $Logs $Log
    $proc = Start-Process -FilePath $Exe -ArgumentList $ProgramArgs -PassThru -WorkingDirectory $StateDir `
        -RedirectStandardOutput $out -RedirectStandardError "$out.err" -WindowStyle Hidden
    Add-Content -Path $PidFile -Value $proc.Id
    return $proc
}

function Start-All {
    if (Test-Path $PidFile) { Write-Host "Already running (see $PidFile). Run './large.ps1 stop' first."; return }
    $net = Write-Network
    $sw = Program 'x25switch'; $ec = Program 'x25echo'; $hs = Program 'x25host'
    New-Item -ItemType Directory -Force -Path $Logs | Out-Null
    Set-Content -Path $PidFile -Value $null

    # Every switch first; a dialling end retries until the far end listens, so the order is free. Then the
    # services, each once its line is up: a call to a line that is not connected is cleared Out of order.
    foreach ($c in $net.Countries) {
        $name = $c.Name.ToLowerInvariant()
        Write-Host "$($c.Name) ($($c.Dnic))  PAD $(27000 + 100 * $c.Index + 1)  mgmt $(9400 + $c.Index)"
        $p = Start-One "$name.log" $sw @((Join-Path $Gen "$name.ini"))
        Wait-Port $p (27000 + 100 * $c.Index + 1) 'Listen' "$name.log"
    }
    foreach ($c in $net.Countries) {
        $line = 27000 + 100 * $c.Index + 2
        $p = Start-One "echo-$($c.Name.ToLowerInvariant()).log" $ec @('-x', "127.0.0.1:$line", '-a', "$($c.Dnic)9001")
        Wait-Port $p $line 'Established' "echo-$($c.Name.ToLowerInvariant()).log"
    }
    if ($net.Hosts.Count -gt 0) {
        $p = Start-One 'hosts.log' $hs @('--machines', (Join-Path $Gen 'machines.ini'))
        foreach ($c in $net.Countries) {
            if ($c.Hosts -gt 0) { Wait-Port $p (27000 + 100 * $c.Index + 3) 'Established' 'hosts.log' }
        }
    }
    Write-Host ''
    Write-Host "Running. './large.ps1 hosts' lists the numbers; './large.ps1 pad $($net.Countries[0].Name)' to call one."
}

function Stop-All {
    if (-not (Test-Path $PidFile)) { Write-Host 'Not running.'; return }
    foreach ($line in Get-Content $PidFile) {
        $id = "$line".Trim()
        if (-not $id) { continue }
        # Only a process that is still one of ours (started from this repository): Windows reuses process ids.
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$id)" -ErrorAction SilentlyContinue
        if (-not $proc) { continue }
        if (-not $proc.CommandLine -or $proc.CommandLine.IndexOf($Root, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Write-Host "  PID $id is now '$($proc.Name)', not ours - left running."
            continue
        }
        Stop-Process -Id ([int]$id) -Force
        Write-Host "  stopped PID $id"
    }
    Remove-Item $PidFile -Force
    Write-Host 'Stopped.'
}

switch ($Command) {
    'generate' { $null = Write-Network }
    'start' { Start-All }
    'stop' { Stop-All }
    'status' {
        if (-not (Test-Path $PidFile)) { Write-Host 'Not running.'; break }
        foreach ($line in Get-Content $PidFile) {
            $id = "$line".Trim()
            if (-not $id) { continue }
            $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$id)" -ErrorAction SilentlyContinue
            Write-Host ("  PID {0}  {1}" -f $id, $(if ($proc) { 'running' } else { 'EXITED' }))
        }
    }
    'hosts' {
        $net = Read-Network
        foreach ($h in $net.Hosts) { Write-Host ("  {0,-10} {1,-12} {2,-12} from another country: 0{3}" -f $h.Country.Name, $h.Hostname, $h.Type, $h.Nua) }
        foreach ($c in $net.Countries) { Write-Host ("  {0,-10} echo         {1}9001" -f $c.Name, $c.Dnic) }
    }
    'pad' {
        $net = Read-Network
        $c = $null
        foreach ($x in $net.Countries) { if ($x.Name -eq $Arg) { $c = $x } }
        if (-not $c) { throw "pad: one of $(($net.Countries | ForEach-Object { $_.Name }) -join ', ')" }
        & (Program 'x25pad') -x "127.0.0.1:$(27000 + 100 * $c.Index + 1)"
    }
}
