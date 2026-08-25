# Overwatch 2 Server Picker

A small Windows app that lets you choose which Overwatch 2 datacenters you are willing to
play on. Check the ones you want, hit Apply, and the rest are blocked with Windows Firewall
rules scoped to Overwatch itself.

No runtime to download, no dependencies. One ~100 KB executable and a JSON file.

```
┌─ Overwatch 2 Server Picker ─────────────────────────────────┐
│ [ Play only on checked ][ Block checked ]                   │
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

Download the release archive from [Releases](../../releases), extract it, and run
**`install.cmd`**. It copies the app to `%LOCALAPPDATA%\Programs\Ow2ServerPicker` and adds a
Start Menu entry — no administrator rights needed for the install itself.

Then press Start and type "Overwatch".

The app requests administrator rights when it launches. That is required to create Windows
Firewall rules and is the only thing it uses them for.

Prefer to place it yourself? Just keep `Ow2ServerPicker.exe` and `servers.json` in the same
folder and run the exe — the `servers.json` beside it overrides the catalog embedded in the
build, which is how you edit ranges without rebuilding.

### Uninstall

Run **`uninstall.cmd`** from the install folder. It removes the firewall rules *first* — that
ordering matters, because deleting the app while blocks are active would leave Overwatch
restricted with nothing on the machine that knows how to undo it. It then removes the Start
Menu entry and the files.

To check or clear the rules by hand at any time:

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

On launch the app reads the rules already in your firewall and shows that selection, so a
restart reflects what is actually in force rather than a remembered guess. With no rules
active it opens with everything checked, which means "block nothing" — an accidental Apply on
a fresh install cannot lock you out.

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
powershell -ExecutionPolicy Bypass -File tools\capture-server.ps1 -Seconds 45 -Lookup
```

It filters to UDP, ranks peers by packet count (the game server dominates during a match), and
reports each candidate's address, **ports** and round-trip time. `-Lookup` resolves city and ISP,
sending only the server IP to ipinfo.io.

The ports column matters as much as the address: `gameUdpPorts` in `servers.json` must cover the
game server's port or blocking silently stops working, which is a worse failure than blocking too
much because nothing tells you.

The script only observes — blocking is the app's job, because the app scopes its rules to the
Overwatch executable, to UDP, and to the game port range.

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
