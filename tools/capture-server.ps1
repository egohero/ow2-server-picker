<#
.SYNOPSIS
    Identify which Overwatch 2 datacenter you are connected to, and firewall-block
    the ones you do not want, so matchmaking falls through to Sydney (AU/NZ).

.DESCRIPTION
    Overwatch 2 has no server picker. This script lets you:
      1. capture  - sniff the live game-server IP during a match (pktmon, built into Windows)
      2. tag      - record that IP as a named datacenter block in a local JSON file
      3. block    - add outbound UDP firewall rules for the datacenters you want to avoid
      4. status   - see what is currently blocked

    Nothing here is guessed: you build the datacenter list from IPs you actually
    observed on your own connection. Blizzard rotates ranges, so a hardcoded list
    copied off a forum goes stale.

    MUST BE RUN FROM AN ELEVATED POWERSHELL (pktmon and the firewall both need admin).

.EXAMPLE
    # Sitting in a match you suspect is Singapore:
    .\ow-server-control.ps1 capture -Seconds 45 -Lookup

.EXAMPLE
    # Record what you found, then block it:
    .\ow-server-control.ps1 tag -Name SGS -Ip 1.2.3.4 -Prefix 24
    .\ow-server-control.ps1 block -Name SGS
    .\ow-server-control.ps1 status
    .\ow-server-control.ps1 reset      # remove every block
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('capture', 'tag', 'list', 'block', 'unblock', 'status', 'reset')]
    [string]$Action = 'status',

    [string]$Name,
    [string]$Ip,
    [ValidateRange(8, 32)]
    [int]$Prefix = 24,
    [ValidateRange(10, 600)]
    [int]$Seconds = 60,
    [switch]$Lookup
)

$ErrorActionPreference = 'Stop'
$RulePrefix = 'OW2-Block-'
$DataFile   = Join-Path $PSScriptRoot 'ow-datacenters.json'
$WorkDir    = Join-Path $env:TEMP 'ow-server-control'

function Assert-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This action needs an elevated PowerShell. Right-click PowerShell -> Run as administrator.'
    }
}

function Get-Datacenters {
    if (Test-Path $DataFile) {
        $raw = Get-Content $DataFile -Raw
        if ($raw.Trim()) { return @(ConvertFrom-Json $raw) }
    }
    return @()
}

function Save-Datacenters($list) {
    ConvertTo-Json @($list) -Depth 5 | Set-Content $DataFile -Encoding UTF8
}

function Get-NetworkCidr([string]$addr, [int]$len) {
    $bytes = ([Net.IPAddress]::Parse($addr)).GetAddressBytes()
    [Array]::Reverse($bytes)
    $val  = [BitConverter]::ToUInt32($bytes, 0)
    $mask = if ($len -eq 0) { [uint32]0 } else { [uint32]((0xFFFFFFFFL -shl (32 - $len)) -band 0xFFFFFFFFL) }
    $net  = $val -band $mask
    $nb   = [BitConverter]::GetBytes([uint32]$net)
    [Array]::Reverse($nb)
    return ('{0}/{1}' -f (New-Object Net.IPAddress(,$nb)).ToString(), $len)
}

function Test-PublicIPv4([string]$addr) {
    $parts = $addr.Split('.')
    if ($parts.Count -ne 4) { return $false }
    $o = @($parts | ForEach-Object { [int]$_ })
    if ($o[0] -eq 0 -or $o[0] -eq 10 -or $o[0] -eq 127 -or $o[0] -ge 224) { return $false }
    if ($o[0] -eq 172 -and $o[1] -ge 16 -and $o[1] -le 31)  { return $false }
    if ($o[0] -eq 192 -and $o[1] -eq 168) { return $false }
    if ($o[0] -eq 169 -and $o[1] -eq 254) { return $false }
    if ($o[0] -eq 100 -and $o[1] -ge 64 -and $o[1] -le 127) { return $false }
    return $true
}

function Measure-Rtt([string]$addr) {
    try {
        $r = Test-Connection -ComputerName $addr -Count 3 -ErrorAction Stop
        $ms = ($r | Measure-Object -Property ResponseTime -Average).Average
        if ($null -eq $ms) { $ms = ($r | Measure-Object -Property Latency -Average).Average }
        if ($null -ne $ms) { return [int]$ms }
    } catch { }
    return $null
}

function Get-RttHint($ms) {
    if ($null -eq $ms) { return 'no ICMP reply - compare against your in-game ping' }
    if ($ms -lt 60)  { return 'likely Sydney (AU/NZ)' }
    if ($ms -lt 115) { return 'likely Singapore' }
    if ($ms -lt 160) { return 'likely Tokyo / Seoul / Taiwan' }
    return 'likely US West or further'
}

function Invoke-Capture {
    Assert-Elevated
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
    $etl = Join-Path $WorkDir 'ow.etl'
    $txt = Join-Path $WorkDir 'ow.txt'
    Remove-Item $etl, $txt -ErrorAction SilentlyContinue

    Write-Host ''
    Write-Host '  Get into a live match FIRST, then let this run.' -ForegroundColor Yellow
    Write-Host ("  Capturing UDP for {0} seconds..." -f $Seconds) -ForegroundColor Cyan

    & pktmon filter remove | Out-Null
    & pktmon filter add OWUDP -t UDP | Out-Null
    & pktmon start --capture --pkt-size 128 --file-size 256 -f $etl | Out-Null
    try {
        Start-Sleep -Seconds $Seconds
    } finally {
        & pktmon stop | Out-Null
        & pktmon filter remove | Out-Null
    }

    & pktmon etl2txt $etl -o $txt --brief | Out-Null
    if (-not (Test-Path $txt)) { throw "pktmon produced no output at $txt" }

    # Tally every public IPv4 seen. The game server dominates packet count during a
    # match, so top-talker ranking is reliable regardless of pktmon's exact text format.
    $tally = @{}
    Select-String -Path $txt -Pattern '\b(?:\d{1,3}\.){3}\d{1,3}\b' -AllMatches |
        ForEach-Object { $_.Matches } |
        ForEach-Object {
            $a = $_.Value
            if (Test-PublicIPv4 $a) {
                if ($tally.ContainsKey($a)) { $tally[$a]++ } else { $tally[$a] = 1 }
            }
        }

    if ($tally.Count -eq 0) {
        Write-Host '  No public UDP peers seen. Were you actually in a match?' -ForegroundColor Red
        return
    }

    $top = $tally.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 8
    Write-Host ''
    Write-Host '  Top UDP peers (highest packet count = almost certainly the game server):' -ForegroundColor Green
    Write-Host ''

    $rows = foreach ($e in $top) {
        $ms  = Measure-Rtt $e.Key
        $org = ''
        if ($Lookup) {
            try {
                $info = Invoke-RestMethod ('https://ipinfo.io/{0}/json' -f $e.Key) -TimeoutSec 6
                $org  = '{0}, {1} - {2}' -f $info.city, $info.country, $info.org
            } catch { $org = 'lookup failed' }
        }
        [pscustomobject]@{
            IP      = $e.Key
            Packets = $e.Value
            RTT     = $(if ($null -ne $ms) { "$ms ms" } else { '-' })
            Guess   = Get-RttHint $ms
            Org     = $org
        }
    }
    $rows | Format-Table -AutoSize

    $first = $rows[0].IP
    Write-Host '  Record the one you want to avoid, e.g.:' -ForegroundColor Cyan
    Write-Host ("    .\ow-server-control.ps1 tag -Name SGS -Ip {0} -Prefix 24" -f $first) -ForegroundColor White
    Write-Host ''
    if (-not $Lookup) {
        Write-Host '  Add -Lookup to resolve city/ISP (sends only the server IP to ipinfo.io).' -ForegroundColor DarkGray
    }
}

function Invoke-Tag {
    if (-not $Name -or -not $Ip) { throw 'tag requires -Name and -Ip (optionally -Prefix, default 24).' }
    $cidr = Get-NetworkCidr $Ip $Prefix
    $list = @(Get-Datacenters | Where-Object { $_.Name -ne $Name })
    $list += [pscustomobject]@{
        Name   = $Name
        Cidr   = $cidr
        SeenIp = $Ip
        Added  = (Get-Date).ToString('yyyy-MM-dd')
    }
    Save-Datacenters $list
    Write-Host ("  Tagged {0} = {1} (from {2})" -f $Name, $cidr, $Ip) -ForegroundColor Green
    Write-Host ("  Block it with:  .\ow-server-control.ps1 block -Name {0}" -f $Name) -ForegroundColor Cyan
}

function Invoke-List {
    $list = Get-Datacenters
    if (-not $list) {
        Write-Host '  No datacenters recorded yet. Run: .\ow-server-control.ps1 capture' -ForegroundColor Yellow
        return
    }
    $list | Select-Object Name, Cidr, SeenIp, Added | Format-Table -AutoSize
}

function Invoke-Block {
    Assert-Elevated
    if (-not $Name) { throw 'block requires -Name (see: list).' }
    $dc = Get-Datacenters | Where-Object { $_.Name -eq $Name }
    if (-not $dc) { throw ("No datacenter named '{0}'. Run 'list' to see what is recorded." -f $Name) }

    $rule = $RulePrefix + $Name
    Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $rule -Description ("Overwatch 2: block {0} datacenter ({1})" -f $Name, $dc.Cidr) -Direction Outbound -Action Block -Protocol UDP -RemoteAddress $dc.Cidr -Profile Any -Enabled True | Out-Null

    Write-Host ("  Blocked {0} ({1}) - outbound UDP only." -f $Name, $dc.Cidr) -ForegroundColor Green
    Write-Host '  Fully quit and relaunch Overwatch for it to take effect.' -ForegroundColor Yellow
}

function Invoke-Unblock {
    Assert-Elevated
    if (-not $Name) { throw 'unblock requires -Name, or use: reset' }
    $rule = $RulePrefix + $Name
    $r = Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
    if (-not $r) {
        Write-Host ("  No active rule for {0}." -f $Name) -ForegroundColor Yellow
        return
    }
    $r | Remove-NetFirewallRule
    Write-Host ("  Unblocked {0}." -f $Name) -ForegroundColor Green
}

function Invoke-Status {
    $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like ($RulePrefix + '*') })
    Write-Host ''
    if (-not $rules) {
        Write-Host '  No Overwatch server blocks are active.' -ForegroundColor Yellow
    } else {
        Write-Host '  Active blocks:' -ForegroundColor Green
        foreach ($r in $rules) {
            $addr = ($r | Get-NetFirewallAddressFilter).RemoteAddress -join ', '
            $on   = $(if ($r.Enabled -eq 'True') { 'enabled' } else { 'disabled' })
            Write-Host ("    {0,-14} {1,-20} {2}" -f $r.DisplayName.Replace($RulePrefix, ''), $addr, $on)
        }
    }
    Write-Host ''
    Write-Host '  Recorded datacenters:' -ForegroundColor Cyan
    Invoke-List
}

function Invoke-Reset {
    Assert-Elevated
    $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like ($RulePrefix + '*') })
    if (-not $rules) {
        Write-Host '  Nothing to remove.' -ForegroundColor Yellow
        return
    }
    $rules | Remove-NetFirewallRule
    Write-Host ("  Removed {0} block rule(s). All servers reachable again." -f $rules.Count) -ForegroundColor Green
}

switch ($Action) {
    'capture' { Invoke-Capture }
    'tag'     { Invoke-Tag }
    'list'    { Invoke-List }
    'block'   { Invoke-Block }
    'unblock' { Invoke-Unblock }
    'status'  { Invoke-Status }
    'reset'   { Invoke-Reset }
}
