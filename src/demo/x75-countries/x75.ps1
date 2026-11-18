<#
.SYNOPSIS
  Three countries joined by X.75: Norway (2422) - England (2342) - Germany (2624), with the Altos chat in
  Germany and an echo service in each country. Start it, use it by hand, stop it. See README.md here.

.EXAMPLE
  ./x75.ps1 build            # take the newest RetroIO packages, build, test and publish to src\publish\win-x64
  ./x75.ps1 start            # start 3 switches, 3 echo services and the Altos chat; refuses programs older
                             # than the code (run build first)
  ./x75.ps1 pad 1            # a PAD as test user 1 (NUI 242226500000101); then: CALL 026245890040004
  ./x75.ps1 cli norway       # the management console of a switch (login admin / admin)
  ./x75.ps1 calls            # the call records each switch has written
  ./x75.ps1 status           # what is running, and which ports listen
  ./x75.ps1 stop

.NOTES
  -BinDir     the folder with the programs (x25switch.exe, x25echo.exe, x25service.exe, x25pad.exe).
              Default: src\publish\win-x64, where src\build.ps1 puts them. The unit test passes its own output folder.
  -StateDir   where the logs, call records and the process-id file go (default: this folder)
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'start', 'stop', 'status', 'pad', 'cli', 'calls', 'logs', 'monitor')]
    [string]$Command = 'status',
    [Parameter(Position = 1)]
    [string]$Arg = '',

    [string]$BinDir = '',
    [string]$StateDir = ''
)

$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
# $Root is src: the solution and every project folder.
$Root = Split-Path -Parent (Split-Path -Parent $Here)
if (-not $StateDir) { $StateDir = $Here }
$Logs = Join-Path $StateDir 'logs'
$PidFile = Join-Path $StateDir '.pids'

# The test users. Each has its own NUI and so its own account in Norway (norway.ini [account.N]).
$Nuis = @{ '1' = '242226500000101'; '2' = '242226500000102'; '3' = '242226500000103'; '4' = '242226500000104' }
$Mgmt = @{ 'norway' = 9201; 'england' = 9202; 'germany' = 9203 }
$Monitor = @{ 'norway' = 4201; 'england' = 4202; 'germany' = 4203 }
# Every TCP port this network listens on (README.md, "Ports").
# 25003, 25013 and 25024 are the host lines (24221xxx, 23421xxx, 26241xxx), for x25host.
$AllPorts = 25001, 25002, 25003, 25011, 25012, 25013, 25021, 25022, 25023, 25024, 9201, 9202, 9203, 4201, 4202, 4203

$script:BinDirGiven = [bool]$BinDir
if (-not $BinDir) { $BinDir = Join-Path $Root 'publish/win-x64' }

# The programs come from src\build.ps1 (./x75.ps1 build runs it), or from the folder given as -BinDir.
function Program([string]$Name) {
    $exe = Join-Path $BinDir "$Name.exe"
    if (-not (Test-Path $exe)) { throw "$exe not found - run src\build.ps1 first (or pass -BinDir)" }
    return $exe
}

# Waits until a TCP port listens on this machine, or the program has exited, or the time is up.
function Wait-Listen([System.Diagnostics.Process]$Proc, [int]$Port, [string]$Log) {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if ($Proc.HasExited) { break }
        if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { return }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "  $Log : not listening on $Port (exited: $($Proc.HasExited)). Its output:" -ForegroundColor Red
    Get-Content (Join-Path $Logs $Log) -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" }
    Get-Content (Join-Path $Logs "$Log.err") -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" }
    Stop-All
    throw "start failed - see $Logs"
}

# Waits until a service has connected to its switch port: a TCP connection to that port is established. Calls
# for a service whose line is not up yet are cleared Out of order (0x09); the first test run found exactly that.
function Wait-Connected([System.Diagnostics.Process]$Proc, [int]$Port, [string]$Log) {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if ($Proc.HasExited) { break }
        if (Get-NetTCPConnection -State Established -LocalPort $Port -ErrorAction SilentlyContinue) { return }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "  $Log : never connected to switch port $Port (exited: $($Proc.HasExited)). Its output:" -ForegroundColor Red
    Get-Content (Join-Path $Logs $Log) -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" }
    Stop-All
    throw "start failed - see $Logs"
}

# Waits until a log file holds a line, or the time is up.
function Wait-Log([System.Diagnostics.Process]$Proc, [string]$Log, [string]$Text) {
    $file = Join-Path $Logs $Log
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if ($Proc.HasExited) { break }
        if ((Test-Path $file) -and (Select-String -Path $file -SimpleMatch $Text -Quiet)) { return }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "  $Log : never logged '$Text' (exited: $($Proc.HasExited)). Its output:" -ForegroundColor Red
    Get-Content $file -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" }
    Stop-All
    throw "start failed - see $Logs"
}

function Start-One([string]$Log, [string]$Exe, [object[]]$ProgramArgs) {
    $out = Join-Path $Logs $Log
    # The switches write their call records relative to the working folder (cdr_file = logs/...).
    $proc = Start-Process -FilePath $Exe -ArgumentList $ProgramArgs -PassThru -WorkingDirectory $StateDir `
        -RedirectStandardOutput $out -RedirectStandardError "$out.err" -WindowStyle Hidden
    Add-Content -Path $PidFile -Value $proc.Id
    return $proc
}

# The newest source the published programs are made from: every .cs, .csproj and .props under src (build output
# left out), and the restore record of the switch, which changes when a newer RetroIO package is taken.
function Get-NewestSource {
    $newest = [datetime]::MinValue
    foreach ($f in Get-ChildItem $Root -Recurse -File -Include *.cs, *.csproj, *.props) {
        if ($f.FullName -match '\\(bin|obj|publish)\\') { continue }
        if ($f.LastWriteTime -gt $newest) { $newest = $f.LastWriteTime }
    }
    $assets = Join-Path $Root 'RetroX25.Switch/obj/project.assets.json'
    if ((Test-Path $assets) -and (Get-Item $assets).LastWriteTime -gt $newest) { $newest = (Get-Item $assets).LastWriteTime }
    return $newest
}

# Refuses to start programs older than the code: running an old switch while the code says otherwise looks
# exactly like a fix that does not work (2026-11-18: a console fix was tested against a switch published hours
# before it). Only for the default folder; a folder given with -BinDir is the caller's own build.
function Assert-Fresh {
    if ($script:BinDirGiven) { return }
    $newest = Get-NewestSource
    foreach ($name in 'x25switch', 'x25echo', 'x25service', 'x25pad') {
        $exe = Program $name
        $built = (Get-Item $exe).LastWriteTime
        if ($built -lt $newest) {
            throw "$exe was published $built, before the newest source ($newest). Run './x75.ps1 build' first."
        }
    }
}

# Takes the newest RetroIO packages from the local feed, then builds, tests and publishes everything with
# src\build.ps1. The programs cannot be replaced while they run, so the network must be stopped.
function Build-All {
    if (Test-Path $PidFile) { throw "The network is running (see $PidFile): './x75.ps1 stop' first - running programs cannot be replaced." }
    Push-Location $Root
    try {
        # A plain restore once kept an older RetroIO package (docs/cisco-gns3-xot-test.md, "Why the forced restore").
        dotnet restore --force --no-cache
        if ($LASTEXITCODE -ne 0) { throw 'restore failed' }
        & (Join-Path $Root 'build.ps1')
    }
    finally { Pop-Location }
}

function Start-All {
    if (Test-Path $PidFile) {
        Write-Host "Already running (see $PidFile). Run './x75.ps1 stop' first."
        return
    }

    Assert-Fresh

    # A port already taken (another run of this network, with another -StateDir) would make the wait below see
    # the OTHER run's listener and report a start that did not happen. Refuse instead.
    $busy = @()
    foreach ($port in $AllPorts) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) { $busy += $port }
    }
    if ($busy.Count -gt 0) { throw "TCP port(s) $($busy -join ', ') already in use - is this network already running? ./x75.ps1 status" }

    New-Item -ItemType Directory -Force -Path $Logs | Out-Null
    Set-Content -Path $PidFile -Value $null

    $sw = Program 'x25switch'
    $ec = Program 'x25echo'
    $sv = Program 'x25service'

    # The listening ends first: Germany, then England, then Norway, which dials England.
    Write-Host 'Germany (2624)  mgmt 9203  monitor 4203'
    $p = Start-One 'germany.log' $sw @((Join-Path $Here 'germany.ini')); Wait-Listen $p 25021 'germany.log'; Wait-Listen $p 9203 'germany.log'
    Write-Host 'England (2342)  mgmt 9202  monitor 4202'
    $p = Start-One 'england.log' $sw @((Join-Path $Here 'england.ini')); Wait-Listen $p 25011 'england.log'; Wait-Listen $p 9202 'england.log'
    Write-Host 'Norway  (2422)  mgmt 9201  monitor 4201'
    $p = Start-One 'norway.log' $sw @((Join-Path $Here 'norway.ini')); Wait-Listen $p 25001 'norway.log'; Wait-Listen $p 9201 'norway.log'

    Write-Host 'Altos chat 26245890040004 (x25service, Germany port 1)'
    $chat = Start-One 'altos-chat.log' $sv @((Join-Path $Here 'altos-chat.json')); Wait-Log $chat 'altos-chat.log' 'x25service started'

    Write-Host 'Echo services 24229001, 23429001, 26249001'
    $p = Start-One 'echo-norway.log' $ec @('-x', '127.0.0.1:25002', '-a', '24229001'); Wait-Connected $p 25002 'echo-norway.log'
    $p = Start-One 'echo-england.log' $ec @('-x', '127.0.0.1:25012', '-a', '23429001'); Wait-Connected $p 25012 'echo-england.log'
    $p = Start-One 'echo-germany.log' $ec @('-x', '127.0.0.1:25023', '-a', '26249001'); Wait-Connected $p 25023 'echo-germany.log'
    # The chat dials Germany's Altos line the same way: up only once that connection stands.
    Wait-Connected $chat 25022 'altos-chat.log'

    Write-Host ''
    Write-Host 'Running. Try:'
    Write-Host '  ./x75.ps1 pad 1          then at the * prompt:  CALL 026245890040004'
    Write-Host '  ./x75.ps1 cli norway     (admin / admin)  show x25 account'
    Write-Host '  ./x75.ps1 stop'
}

function Stop-All {
    if (-not (Test-Path $PidFile)) {
        Write-Host 'Not running.'
        return
    }
    Write-Host 'Stopping...'
    foreach ($line in Get-Content $PidFile) {
        $id = "$line".Trim()
        if (-not $id) { continue }
        # A process-id file outlives its processes and Windows hands the numbers out again, so an id is only
        # stopped when the process holding it now is still ours: a program started from this repository.
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$id)" -ErrorAction SilentlyContinue
        if (-not $proc) { Write-Host "  PID $id has already exited."; continue }
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

function Show-Status {
    if (-not (Test-Path $PidFile)) { Write-Host 'Not running.'; return }
    foreach ($line in Get-Content $PidFile) {
        $id = "$line".Trim()
        if (-not $id) { continue }
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$id)" -ErrorAction SilentlyContinue
        if ($proc) { Write-Host "  PID $id  running   $(($proc.CommandLine -split ' ')[-1])" }
        else { Write-Host "  PID $id  EXITED" -ForegroundColor Red }
    }
    foreach ($port in $AllPorts) {
        $l = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
        Write-Host ("  TCP {0,5}  {1}" -f $port, $(if ($l) { 'listening' } else { 'NOT listening' }))
    }
}

switch ($Command) {
    'build' { Build-All }
    'start' { Start-All }
    'stop' { Stop-All }
    'status' { Show-Status }
    'pad' {
        $user = if ($Arg) { $Arg } else { '1' }
        $pad = Program 'x25pad'
        if ($Nuis.ContainsKey($user)) {
            Write-Host "Test user $user, NUI $($Nuis[$user]). At the * prompt: CALL 026245890040004  (help: HELP)"
            & $pad -x 127.0.0.1:25001 -n $Nuis[$user]
        }
        elseif ($user -eq 'nonui') {
            Write-Host 'No NUI: every call should be cleared (diagnostic 84).'
            & $pad -x 127.0.0.1:25001
        }
        else { throw "pad: user is 1, 2, 3, 4 or nonui" }
    }
    'cli' {
        if (-not $Mgmt.ContainsKey($Arg)) { throw 'cli: norway, england or germany' }
        telnet localhost $Mgmt[$Arg]
    }
    'calls' {
        foreach ($c in 'norway', 'england', 'germany') {
            Write-Host "== $c ($(Join-Path $Logs "$c-calls.txt"))"
            Get-Content (Join-Path $Logs "$c-calls.txt") -ErrorAction SilentlyContinue
        }
    }
    'logs' {
        $name = if ($Arg) { $Arg } else { 'norway' }
        Get-Content (Join-Path $Logs "$name.log") -Wait -Tail 40
    }
    'monitor' {
        & (Program 'x25monitor')
    }
}
