# Overwatch 2 Server Picker

A small Windows app that lets you choose which Overwatch 2 datacenters you are willing to
play on. Check the ones you want, hit Apply, and the rest are blocked with Windows Firewall
rules scoped to Overwatch itself.

No installer, no runtime to download, no dependencies. One ~40 KB executable and a JSON file.

```
┌─ Overwatch 2 Server Picker ─────────────────────────────────┐
│ ● Play only on checked servers  (block everything else)     │
│ ○ Block checked servers  (leave the rest alone)             │
│                                                             │
│ [Select all] [Deselect all] [Invert] [Ping all]             │
│                                                             │
│ ▾ Oceania                                                   │
│   ☑ Australia (Sydney)          SYD2     31 ms    21 ranges │
│ ▾ Asia                                                      │
│   ☐ Singapore                   GSG1    112 ms    59 ranges │
│   ☐ Japan (Tokyo)               GTK1      -       35 ranges │
│   ☐ South Korea (Incheon)       ICN1    138 ms     3 ranges │
│ ▾ North America                                             │
│   ☐ USA West (Los Angeles)      LAX1    154 ms     3 ranges │
│   ...                                                       │
│                                                             │
│ Playable: 1 datacenter. Blocking 14 of 15                   │
│ (6,176,512 addresses across 1 rule set).                    │
│ Rules apply only to: D:\...\Overwatch\_retail_\Overwatch.exe│
│                                                             │
│ [ Apply ] [Remove all blocks] [Locate Overwatch.exe]        │
└─────────────────────────────────────────────────────────────┘
```

## Why another one of these

Two things this does differently, both of which turned out to matter.

**It subtracts overlapping ranges instead of blocking datacenters wholesale.** Overwatch
datacenter ranges genuinely overlap — Singapore's `34.124.0.0-34.124.255.255` *contains*
Sydney's `34.124.40.0/23`. Windows Firewall resolves block-vs-allow in favour of block, so a
tool that writes one block rule per unwanted datacenter will happily block Singapore *and take
Sydney down with it*. This app computes `(everything you rejected) − (everything you kept)` as
interval arithmetic before writing a single rule, so kept datacenters are never collateral.

**Rules are scoped to `Overwatch.exe`.** Most Overwatch datacenters now run on Google Cloud, so
their address ranges are shared with everything else hosted in that GCP region. A machine-wide
block of "Singapore" is really a block of a large slice of GCP Singapore, which can break
unrelated software. This app finds your Overwatch install and confines every rule to that
executable. If it cannot find the game it says so and asks before writing anything broader.

## Install

Download `Ow2ServerPicker.exe` and `servers.json` from
[Releases](../../releases), put them in the same folder, and run the exe. It requests
administrator rights, because creating firewall rules requires them.

To uninstall: click **Remove all blocks**, then delete the two files. Every rule the app
creates is named `OW2ServerPicker-NN`, so you can also verify or remove them yourself:

```bash
powershell -Command "Get-NetFirewallRule -DisplayName 'OW2ServerPicker*' | Format-Table DisplayName,Enabled,Action"
```

## Use

1. Pick a mode. **Play only on checked servers** is the usual one — check the datacenters you
   want and everything else gets blocked. **Block checked servers** is the inverse, for when
   you only want to exclude one or two.
2. Optionally hit **Ping all** to see which datacenters are actually close to you. Entries
   showing `–` have no verified probe address in `servers.json`; `n/a` means the probe did not
   answer ICMP. Neither means the datacenter is unreachable — cloud edges routinely drop or
   rate-limit ICMP, while the game itself talks UDP and connects fine. The ping column is a
   convenience for choosing servers, not a reachability test.
3. **Apply**, then fully quit and relaunch Overwatch.

Everything starts checked, which means "block nothing" — an accidental Apply on first launch
cannot lock you out.

## What this costs you

Be realistic about the trade-off before you narrow your selection.

- **Queue times.** Matchmaking pools are regional. Cutting yourself down to one datacenter in a
  low-population region can mean noticeably longer queues off-peak, and the matchmaker may
  reach for a much more distant server rather than leave you unmatched.
- **Terms of service.** Blizzard's terms broadly prohibit interfering with the normal operation
  of the game. Firewall-based server selection is widely used and we are not aware of bans for
  it, but it is not explicitly sanctioned. Your call, your account.
- **The list goes stale.** Blizzard rotates address ranges. If a datacenter you blocked starts
  appearing again, the ranges moved — see below.

## Keeping the ranges current

`servers.json` sits next to the executable and overrides the copy embedded in the build, so you
can update ranges without waiting for a release.

`tools/capture-server.ps1` finds the datacenter you are *actually* connected to, using `pktmon`
(built into Windows — nothing to install). Run it from an elevated PowerShell while you are in
a live match:

```bash
powershell -File tools\capture-server.ps1 capture -Seconds 45 -Lookup
```

It filters to UDP, ranks peers by packet count (the game server dominates during a match), and
pings each candidate. `-Lookup` resolves city and ISP, sending only the server IP to ipinfo.io.

If you find a range that is not in `servers.json`, please open a PR adding it. Include the IP
you observed and roughly where you are — that is what makes the entry checkable by someone else.

## Build from source

Requires nothing but Windows. The build uses the C# compiler that ships inside .NET Framework,
already present on every Windows 10/11 machine.

```bash
build.cmd
```

Output lands in `build\`. To run the tests:

```bash
tests\run-tests.cmd
```

The suite covers the interval arithmetic (parsing, merging, subtraction, and the
`255.255.255.255` boundary that wraps a 32-bit cut) and asserts against the real shipped
catalog that a "play only on Sydney" selection leaves no Sydney address in the block set while
still blocking every Singapore-only range.

`tests/FirewallProbe.cs` validates the Windows Firewall COM contract — property names, types,
and the address-range string format — *without* writing anything to the system, by building a
rule object and never calling `Rules.Add`.

## Where the data comes from

Blizzard does not publish datacenter IP ranges. The seed catalog in `data/servers.json` is
derived from the community-maintained lists in
[foryVERX/Overwatch-Server-Selector](https://github.com/foryVERX/Overwatch-Server-Selector),
reorganised per-datacenter with Blizzard's own codes (LAX1, ORD1, AMS1, SYD2, GSG1, …). That
project is the original of this idea and deserves the credit for the legwork; it had no license
file at the time of writing, so only the factual address data was used, not any of its code.

Ranges are community-observed and unverified by Blizzard. Treat them as a good starting point,
not as ground truth.

## License

MIT — see [LICENSE](LICENSE).

Not affiliated with, endorsed by, or connected to Blizzard Entertainment. Overwatch is a
trademark of Blizzard Entertainment, Inc.
