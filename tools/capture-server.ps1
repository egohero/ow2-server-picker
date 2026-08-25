<#
.SYNOPSIS
    Identifies the Overwatch datacenter you are actually connected to, by capturing the
    game's live UDP traffic with pktmon (built into Windows - nothing to install).

.DESCRIPTION
    Use this to check or extend the catalog in servers.json. It reports the game server's
    address, the UDP ports in use, and the round-trip time, so a range that has drifted can
    be corrected and sent back as a PR.

    This script only OBSERVES. Blocking is the app's job: Ow2ServerPicker scopes its rules to
    the Overwatch executable, to UDP, and to the game's port range, which a script writing raw
    firewall rules would not do.

    MUST BE RUN FROM AN ELEVATED POWERSHELL - pktmon requires it.

.EXAMPLE
    # While you are in a live match:
    powershell -ExecutionPolicy Bypass -File capture-server.ps1 -Seconds 45 -Lookup
#>

[CmdletBinding()]
param(
    [ValidateRange(10, 600)]
    [int]$Seconds = 45,

    # Resolve city/ISP for each candidate. Sends only the server IP to ipinfo.io.
    [switch]$Lookup
)

$ErrorActionPreference = 'Stop'
$WorkDir = Join-Path $env:TEMP 'ow2-capture'

function Assert-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'pktmon needs an elevated PowerShell. Right-click PowerShell -> Run as administrator.'
    }
}

function Test-PublicIPv4([string]$addr) {
    $parts = $addr.Split('.')
    if ($parts.Count -ne 4) { return $false }
    $o = @($parts | ForEach-Object { [int]$_ })
    foreach ($n in $o) { if ($n -lt 0 -or $n -gt 255) { return $false } }
    if ($o[0] -eq 0 -or $o[0] -eq 10 -or $o[0] -eq 127 -or $o[0] -ge 224) { return $false }
    if ($o[0] -eq 172 -and $o[1] -ge 16 -and $o[1] -le 31)  { return $false }
    if ($o[0] -eq 192 -and $o[1] -eq 168) { return $false }
    if ($o[0] -eq 169 -and $o[1] -eq 254) { return $false }
    if ($o[0] -eq 100 -and $o[1] -ge 64 -and $o[1] -le 127) { return $false }   # CGNAT
    return $true
}

function Get-RttHint($ms) {
    if ($null -eq $ms) { return 'no ICMP reply - compare against your in-game ping' }
    if ($ms -lt 60)  { return 'likely Sydney (AU/NZ)' }
    if ($ms -lt 115) { return 'likely Singapore' }
    if ($ms -lt 160) { return 'likely Tokyo / Seoul / Taiwan' }
    return 'likely US West or further'
}

Assert-Elevated

$ow = Get-Process Overwatch -ErrorAction SilentlyContinue
if (-not $ow) {
    Write-Host ''
    Write-Host '  Overwatch is not running. Start it, get into a match, then run this again.' -ForegroundColor Yellow
    Write-Host ''
    exit 1
}
Write-Host ''
Write-Host ("  Overwatch running (PID {0})" -f $ow.Id) -ForegroundColor DarkGray

# Local UDP ports the game holds open, for context alongside the remote ports below.
$local = @(Get-NetUDPEndpoint -ErrorAction SilentlyContinue |
           Where-Object { $_.OwningProcess -eq $ow.Id } | ForEach-Object { $_.LocalPort })
if ($local.Count) { Write-Host ("  local UDP ports : {0}" -f ($local -join ', ')) -ForegroundColor DarkGray }

New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
$etl = Join-Path $WorkDir 'ow.etl'
$txt = Join-Path $WorkDir 'ow.txt'
Remove-Item $etl, $txt -ErrorAction SilentlyContinue

Write-Host ("  Capturing UDP for {0} seconds - stay in the match..." -f $Seconds) -ForegroundColor Cyan

& pktmon filter remove | Out-Null
& pktmon filter add OWUDP -t UDP | Out-Null
& pktmon start --capture --pkt-size 128 --file-size 256 -f $etl | Out-Null
try { Start-Sleep -Seconds $Seconds }
finally {
    & pktmon stop | Out-Null
    & pktmon filter remove | Out-Null
}

& pktmon etl2txt $etl -o $txt --brief | Out-Null
if (-not (Test-Path $txt)) { throw "pktmon produced no output at $txt" }

# Tally public IPv4 peers and the ports seen with them. The game server dominates packet
# count during a match, so top-talker ranking is reliable whatever the exact text format.
$tally = @{}
$ports = @{}
foreach ($line in Get-Content $txt) {
    foreach ($m in [regex]::Matches($line, '\b(\d{1,3}(?:\.\d{1,3}){3})(?::(\d{1,5}))?')) {
        $ip = $m.Groups[1].Value
        if (-not (Test-PublicIPv4 $ip)) { continue }
        if ($tally.ContainsKey($ip)) { $tally[$ip]++ } else { $tally[$ip] = 1 }
        if ($m.Groups[2].Success) {
            $p = $m.Groups[2].Value
            if (-not $ports.ContainsKey($ip)) { $ports[$ip] = @{} }
            $ports[$ip][$p] = $true
        }
    }
}

if ($tally.Count -eq 0) {
    Write-Host '  No public UDP peers seen. Were you actually in a match?' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '  Top UDP peers (highest packet count is almost certainly the game server):' -ForegroundColor Green
Write-Host ''

$rows = foreach ($e in ($tally.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 8)) {
    $ms = $null
    try {
        $r = Test-Connection -TargetName $e.Key -Count 2 -TimeoutSeconds 2 -ErrorAction Stop
        $ok = @($r | Where-Object { $_.Status -eq 'Success' })
        if ($ok.Count) { $ms = [int](($ok | Measure-Object -Property Latency -Average).Average) }
    } catch { }

    $org = ''
    if ($Lookup) {
        try {
            $i = Invoke-RestMethod ('https://ipinfo.io/{0}/json' -f $e.Key) -TimeoutSec 6
            $org = '{0}, {1} - {2}' -f $i.city, $i.country, $i.org
        } catch { $org = 'lookup failed' }
    }

    [pscustomobject]@{
        IP      = $e.Key
        Packets = $e.Value
        Ports   = if ($ports.ContainsKey($e.Key)) { (($ports[$e.Key].Keys | Sort-Object) -join ',') } else { '' }
        RTT     = if ($null -ne $ms) { "$ms ms" } else { '-' }
        Guess   = Get-RttHint $ms
        Org     = $org
    }
}
$rows | Format-Table -AutoSize

Write-Host '  If the top address is not covered by servers.json, please open a PR adding it.' -ForegroundColor Cyan
Write-Host '  Include the IP, the ports column above, and roughly where you are.' -ForegroundColor Cyan
Write-Host ''
Write-Host '  The Ports column matters: servers.json "gameUdpPorts" must cover the game' -ForegroundColor DarkGray
Write-Host '  server port, or blocking silently stops working.' -ForegroundColor DarkGray
if (-not $Lookup) {
    Write-Host '  Add -Lookup to resolve city/ISP (sends only the server IP to ipinfo.io).' -ForegroundColor DarkGray
}
Write-Host ''
