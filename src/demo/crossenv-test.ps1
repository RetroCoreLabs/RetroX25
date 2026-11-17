<#
.SYNOPSIS
  Runs the cross-environment tests: RetroX25 against x25emu, the separate C X.25 emulator that runs in WSL.

.DESCRIPTION
  What it does, in order:
    1. finds the address Windows uses to reach WSL (wsl hostname -I; localhost is tried too) and the address WSL
       uses to reach this machine (the WSL default gateway);
    2. checks that the x25emu programs are built (it never builds or changes the x25emu checkout);
    3. starts the x25emu test network inside WSL with demo/crossenv-wsl.sh, holding it by the process number of
       its wsl.exe, and waits until its ports answer;
    4. sets X25EMU_WSL_HOST, X25EMU_TOOLS, X25EMU_WINDOWS_HOST and runs the tests of category Interop in
       RetroX25.Tests and RetroX25.Desktop.Tests;
    5. stops everything it started, by process number (never by name), and checks that nothing is left.

  The guide is docs/crossenv-testing.md: the networking facts, the start order, the firewall note, the address
  table and troubleshooting.

.PARAMETER X25emu
  The folder IN WSL that holds the x25emu checkout, for example /home/me/x25emu. Required: it is never guessed.
  Nothing in it is changed. The built programs must already be there (make).

.PARAMETER WslHost
  Use this address for the x25emu ports instead of looking for one.

.PARAMETER WindowsHost
  Use this as the address WSL uses to reach this machine instead of the WSL default gateway.

.PARAMETER Distro
  The WSL distribution name. The default distribution when left out.

.PARAMETER OurXotPort
  The Windows port x25emu's XOT trunk dials (calls to DNIC 2351). Default 2210.

.PARAMETER OurHdlcPort
  The Windows port x25emu's hdlc-tcp trunk dials (calls to DNIC 2352). Default 2211.

.PARAMETER Filter
  The test filter. Default Category=Interop.

.PARAMETER Configuration
  The build the tests run from, Debug (default) or Release. Release is a separate output folder, so it still builds
  while a program started from the Debug folder holds its files open.

.PARAMETER NoStart
  Do not start the network: it is already running (the checker in the tests says what is missing if it is not).

.PARAMETER CheckOnly
  Start the network, run only the check that says in plain words what is missing, and stop it.

.EXAMPLE
  ./crossenv-test.ps1 -X25emu /home/me/x25emu
#>
param(
    [Parameter(Mandatory = $true)][string]$X25emu,
    [string]$WslHost = '',
    [string]$WindowsHost = '',
    [string]$Distro = '',
    [int]$OurXotPort = 2210,
    [int]$OurHdlcPort = 2211,
    [string]$Filter = 'Category=Interop',
    [string]$Configuration = 'Debug',
    [switch]$NoStart,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$srcRoot = Split-Path -Parent $PSScriptRoot
$distroArgs = @()
if ($Distro) { $distroArgs = @('-d', $Distro) }

function Invoke-Wsl([string]$command) {
    # One short bash command in WSL; returns the text it printed.
    $out = & wsl.exe @distroArgs -e bash -c $command 2>&1
    return ($out | Out-String).Trim()
}

function Test-Port([string]$hostName, [int]$port) {
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $task = $client.ConnectAsync($hostName, $port)
        $ok = $task.Wait(2500) -and $client.Connected
        $client.Dispose()
        return $ok
    }
    catch { return $false }
}

# ---- 1. WSL, and the two addresses -------------------------------------------------------------------------
$probe = Invoke-Wsl 'echo wsl-ok'
if ($probe -notmatch 'wsl-ok') {
    Write-Host "WSL does not answer: $probe" -ForegroundColor Red
    Write-Host 'Start WSL (or pass -Distro), then run this again.' -ForegroundColor Yellow
    exit 1
}

if (-not $WindowsHost) {
    $route = Invoke-Wsl "ip route show default | awk '{print `$3}'"
    if ($route -match '^\d+\.\d+\.\d+\.\d+$') { $WindowsHost = $route }
    else {
        Write-Host "Could not read the Windows address from WSL (ip route said: $route). Pass -WindowsHost." -ForegroundColor Red
        exit 1
    }
}

$wslAddress = ''
$addresses = Invoke-Wsl 'hostname -I'
foreach ($a in ($addresses -split '\s+')) {
    if ($a -match '^\d+\.\d+\.\d+\.\d+$') { $wslAddress = $a; break }
}

# ---- 2. x25emu is built ------------------------------------------------------------------------------------
$tools = $X25emu.TrimEnd('/') + '/tools'
$missing = @()
foreach ($program in 'x25switch', 'x25echo', 'x25traffic', 'x25pad', 'x25trace') {
    $found = Invoke-Wsl "test -x '$tools/$program' && echo yes || echo no"
    if ($found -ne 'yes') { $missing += $program }
}
if ($missing.Count -gt 0) {
    Write-Host "x25emu is not built in $X25emu : missing $($missing -join ', ') in $tools" -ForegroundColor Red
    Write-Host 'Run make in that folder (this script does not build or change it).' -ForegroundColor Yellow
    exit 1
}

# ---- 3. start the network ----------------------------------------------------------------------------------
$supervisor = $null
$stateDir = "/tmp/x25emu-crossenv-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$pidsSeen = ''
$exitCode = 1
try {
    if (-not $NoStart) {
        $wslScript = Invoke-Wsl "wslpath -a '$(($PSScriptRoot -replace '\\', '/'))/crossenv-wsl.sh'"
        Write-Host "Starting the x25emu network in WSL (state folder $stateDir)..."
        $argList = @() + $distroArgs + @('--', 'bash', $wslScript, $X25emu, $WindowsHost, "$OurXotPort", "$OurHdlcPort", $stateDir)
        $supervisor = Start-Process -FilePath 'wsl.exe' -ArgumentList $argList -PassThru -WindowStyle Hidden
        Write-Host "  held by wsl.exe, Windows process $($supervisor.Id)"

        $deadline = (Get-Date).AddSeconds(60)
        $ready = $false
        while ((Get-Date) -lt $deadline) {
            $state = Invoke-Wsl "if [ -e '$stateDir/ready' ]; then echo ready; elif [ -e '$stateDir/not-ready' ]; then cat '$stateDir/not-ready'; else echo waiting; fi"
            if ($state -eq 'ready') { $ready = $true; break }
            if ($state -like 'NOT READY*') { Write-Host $state -ForegroundColor Red; break }
            if ($supervisor.HasExited) { Write-Host 'The WSL start script ended early.' -ForegroundColor Red; break }
            Start-Sleep -Milliseconds 700
        }
        if (-not $ready) {
            Write-Host (Invoke-Wsl "tail -n 20 '$stateDir'/*.log 2>&1") -ForegroundColor Yellow
            throw 'The x25emu network did not come up.'
        }
        $pidsSeen = Invoke-Wsl "cat '$stateDir/pids'"
        Write-Host "  running: $(($pidsSeen -split "`n" | ForEach-Object { $_.Trim() }) -join '; ')"
    }

    # The address Windows can use for the x25emu ports: the WSL address first, then localhost.
    if (-not $WslHost) {
        foreach ($candidate in @($wslAddress, 'localhost', '127.0.0.1')) {
            if ($candidate -and (Test-Port $candidate 2001)) { $WslHost = $candidate; break }
        }
        if (-not $WslHost) {
            throw "No address reaches the x25emu access port 2001 (tried $wslAddress, localhost, 127.0.0.1). See docs/crossenv-testing.md, Troubleshooting."
        }
    }
    Write-Host "x25emu ports are reached at $WslHost ; WSL reaches this machine at $WindowsHost"

    $env:X25EMU_WSL_HOST = $WslHost
    $env:X25EMU_TOOLS = $tools
    $env:X25EMU_WINDOWS_HOST = $WindowsHost
    $env:X25EMU_OUR_XOT_PORT = "$OurXotPort"
    $env:X25EMU_OUR_HDLC_PORT = "$OurHdlcPort"
    # The WSL folder the network runs in; Switch A writes its call records there (calls.cdr).
    $env:X25EMU_STATE = $stateDir
    if ($Distro) { $env:X25EMU_WSL_DISTRO = $Distro }

    # ---- 4. the tests --------------------------------------------------------------------------------------
    $projects = @('tests/RetroX25.Tests/RetroX25.Tests.csproj', 'tests/RetroX25.Desktop.Tests/RetroX25.Desktop.Tests.csproj')
    $filter = $Filter
    if ($CheckOnly) { $filter = 'FullyQualifiedName~X25emuEnvironmentTests' ; $projects = @($projects[0]) }
    $exitCode = 0
    foreach ($project in $projects) {
        Write-Host "dotnet test $project --filter $filter"
        & dotnet test (Join-Path $srcRoot $project) -c $Configuration --filter $filter -nodeReuse:false --nologo
        if ($LASTEXITCODE -ne 0) { $exitCode = $LASTEXITCODE }
    }
}
finally {
    # ---- 5. stop what we started, by process number ---------------------------------------------------------
    if ($supervisor) {
        $sup = Invoke-Wsl "cat '$stateDir/supervisor.pid' 2>/dev/null"
        if ($sup -match '^\d+$') {
            Write-Host "Stopping the x25emu network (supervisor $sup in WSL)..."
            Invoke-Wsl "kill -TERM $sup" | Out-Null
        }
        if (-not $supervisor.WaitForExit(15000)) {
            Write-Host "wsl.exe $($supervisor.Id) did not end; stopping it by its process number." -ForegroundColor Yellow
            Stop-Process -Id $supervisor.Id -Force
        }
        # Check that nothing is left: the state folder is gone and none of the recorded numbers is alive.
        $leftovers = @()
        if ((Invoke-Wsl "[ -e '$stateDir' ] && echo there || echo gone") -eq 'there') { $leftovers += 'state folder still there' }
        foreach ($line in ($pidsSeen -split "`n")) {
            $number = ($line.Trim() -split '\s+')[-1]
            if ($number -match '^\d+$' -and (Invoke-Wsl "kill -0 $number 2>/dev/null && echo alive || echo gone") -eq 'alive') { $leftovers += "process $number still alive" }
        }
        if ($leftovers.Count -gt 0) { Write-Host "Left behind: $($leftovers -join '; ')" -ForegroundColor Red; $exitCode = 1 }
        else { Write-Host 'Everything started was stopped and removed.' }
    }
}
exit $exitCode
