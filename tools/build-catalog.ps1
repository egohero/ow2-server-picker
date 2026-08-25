# Generates data/servers.json for ow2-server-picker from the per-datacenter cfg files.
# Source data: foryVERX/Overwatch-Server-Selector ip_lists (community-maintained).
param(
    [Parameter(Mandatory)][string]$CfgDir,
    [Parameter(Mandatory)][string]$OutFile
)
$ErrorActionPreference = 'Stop'

# code -> display name, region, ping anchor (null = unknown, UI shows a dash)
$meta = @{
    'LAX1'  = @{ Name = 'USA West (Los Angeles)';   Region = 'North America'; Ping = '24.105.30.129' }
    'GUW2'  = @{ Name = 'USA West 2';               Region = 'North America'; Ping = $null }
    'ORD1'  = @{ Name = 'USA Central (Chicago)';    Region = 'North America'; Ping = '24.105.62.129' }
    'AMS1'  = @{ Name = 'Netherlands (Amsterdam)';  Region = 'Europe';        Ping = '185.60.114.159' }
    'GEN1'  = @{ Name = 'Finland';                  Region = 'Europe';        Ping = $null }
    'ICN1'  = @{ Name = 'South Korea (Incheon)';    Region = 'Asia';          Ping = '211.234.110.1' }
    'GAN3'  = @{ Name = 'South Korea 3';            Region = 'Asia';          Ping = $null }
    'GTK1'  = @{ Name = 'Japan (Tokyo)';            Region = 'Asia';          Ping = $null }
    'GSG1'  = @{ Name = 'Singapore';                Region = 'Asia';          Ping = $null }
    'TPE1'  = @{ Name = 'Taiwan (Taipei)';          Region = 'Asia';          Ping = $null }
    'SYD2'  = @{ Name = 'Australia (Sydney)';       Region = 'Oceania';       Ping = '172.105.168.123' }
    'GBR1'  = @{ Name = 'Brazil (Sao Paulo)';       Region = 'South America'; Ping = $null }
    'MES1'  = @{ Name = 'Bahrain';                  Region = 'Middle East';   Ping = $null }
    'GMEC1' = @{ Name = 'Qatar';                    Region = 'Middle East';   Ping = $null }
    'GMEC2' = @{ Name = 'Saudi Arabia';             Region = 'Middle East';   Ping = $null }
}

$datacenters = @()
foreach ($f in Get-ChildItem -Path $CfgDir -Filter 'cfg*.txt' | Sort-Object Name) {
    # "cfg - <Region> - <Label> - <CODE>.txt"
    if ($f.BaseName -notmatch '-\s*([A-Z0-9]+)\s*$') {
        Write-Warning "Skipping unparseable name: $($f.Name)"
        continue
    }
    $code = $Matches[1]
    if (-not $meta.ContainsKey($code)) {
        Write-Warning "No metadata for code '$code' ($($f.Name)) - skipping"
        continue
    }

    $ranges = @()
    foreach ($line in Get-Content $f.FullName) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        # Accept CIDR (a.b.c.d/n) or explicit range (a.b.c.d-e.f.g.h); both are
        # valid for netsh remoteip=. Anything else is dropped rather than guessed.
        if ($t -match '^\d{1,3}(\.\d{1,3}){3}/\d{1,2}$' -or
            $t -match '^\d{1,3}(\.\d{1,3}){3}-\d{1,3}(\.\d{1,3}){3}$') {
            $ranges += $t
        } else {
            Write-Warning "Dropping malformed entry in $($f.Name): '$t'"
        }
    }
    $ranges = @($ranges | Select-Object -Unique)
    if ($ranges.Count -eq 0) { Write-Warning "No ranges for $code"; continue }

    $m = $meta[$code]
    $datacenters += [ordered]@{
        code       = $code
        name       = $m.Name
        region     = $m.Region
        pingTarget = $m.Ping
        ranges     = $ranges
    }
}

$catalog = [ordered]@{
    schema      = 1
    updated     = (Get-Date).ToString('yyyy-MM-dd')
    # Overwatch game-server UDP ports. Scoping rules to these keeps the block off QUIC
    # (443), voice STUN/SIP (3478-3479, 5060-5062) and DNS, which share the same
    # addresses but are not game traffic.
    gameUdpPorts = '6250,12000-64000'
    note        = 'Ranges are community-observed, not published by Blizzard. Verify with tools/capture-server.ps1 and open a PR when they drift.'
    datacenters = @($datacenters | Sort-Object { $_.region }, { $_.name })
}

New-Item -ItemType Directory -Path (Split-Path $OutFile) -Force | Out-Null
$catalog | ConvertTo-Json -Depth 6 | Set-Content $OutFile -Encoding UTF8

Write-Host ("Wrote {0} datacenters, {1} ranges -> {2}" -f `
    $datacenters.Count, ($datacenters | ForEach-Object { $_.ranges.Count } | Measure-Object -Sum).Sum, $OutFile) -ForegroundColor Green
$datacenters | ForEach-Object { '{0,-6} {1,-28} {2,-15} {3,3} ranges' -f $_.code, $_.name, $_.region, $_.ranges.Count }
