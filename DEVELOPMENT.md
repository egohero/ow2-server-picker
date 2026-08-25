# ow2-server-picker

Windows GUI utility for choosing which Overwatch 2 datacenters the game may connect to,
enforced with Windows Firewall rules.

## Stack & commands

- C# 5, WinForms, .NET Framework 4.x. Compiled with the **in-box** compiler
  (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) — no SDK, no NuGet, no VS.
- `build.cmd` → `build\Ow2ServerPicker.exe` (+ `servers.json` copied beside it).
- `tests\run-tests.cmd` → builds and runs all three test binaries; non-zero exit on failure.
- Stay C# 5 compatible: no interpolated strings, no `?.`, no expression-bodied members,
  no `nameof`. The in-box compiler rejects all of them.

## Architecture

| File | Role |
|---|---|
| `src/IpMath.cs` | IPv4 interval parse / merge / **subtract**. The load-bearing logic. |
| `src/ServerCatalog.cs` | Loads `servers.json`; holds `Datacenter.Selected` (source of truth). |
| `src/FirewallManager.cs` | Firewall rules via COM (`HNetCfg.FwPolicy2`), not netsh. |
| `src/OverwatchLocator.cs` | Finds `Overwatch.exe` (running process → registry → drive scan). |
| `src/Sorting.cs` | Column ordering rules. No UI types, so it is testable headlessly. |
| `src/MainForm.cs` | UI; `ComputeBlockSet()` is where selection becomes intervals. |

## Sorting

All four column headers sort on click, cycling ascending → descending → back to the default
region grouping. That third state is not optional: once a sort flattens the list there is
otherwise no way back to the grouped view.

Region captions only appear in the unsorted state. The point of sorting is to compare across
regions, so `RebuildList()` drops the `SectionHeader` rows entirely when a sort is active.

Two rules worth preserving:
- **Rows with no ping reading sink to the bottom in BOTH directions.** An unmeasured
  datacenter is unknown, not slow; letting one head a descending sort asserts something the
  data does not support.
- **Name is the tiebreak everywhere**, which also makes repeated sorts stable rather than
  reshuffling equal rows under the user.

`Sorting` works against `ISortableRow` rather than `ServerRow` so `tests/SelfTest.cs` can
cover the ordering with a plain stub and no WinForms references.

Catalog resolution: `servers.json` beside the exe wins; otherwise the copy embedded via
`/resource:` at build time. Rules are always named `OW2ServerPicker-NN`.

## Gotchas

- **Datacenter ranges overlap.** Singapore's `34.124.0.0-34.124.255.255` contains Sydney's
  `34.124.40.0/23`. Windows Firewall prefers Block over Allow, so per-datacenter block rules
  would kill the datacenter the user kept. Everything must go through
  `IpMath.Subtract(rejected, kept)`. `tests/SelfTest.cs` asserts this against the real catalog.
- **Do not reintroduce a `ListView` for the server list.** It was replaced by custom-drawn
  `ServerRow` controls, partly for theming and partly because `ListView.OnHandleCreated`
  re-inserts items and fires `ItemChecked` while `Tag` is still unmapped — that caused a
  startup `NullReferenceException` inside the message loop. Selection state lives on
  `Datacenter.Selected`, never read back off a control.
- **A WinForms exception in the message loop looks like a hang, not a crash.** To debug, call
  `Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)` — see
  `tests/FormSmoke.cs`.
- **Most datacenters are on Google Cloud**, so their ranges are shared with unrelated GCP
  tenants. Rules must stay scoped to `Overwatch.exe` via `rule.ApplicationName`; a machine-wide
  block is a warned-about fallback only.
- `Interval.Count` returns `long` deliberately — a `0.0.0.0/0` range wraps a `uint` to zero.
- The firewall COM contract can be validated with **zero system writes**: building an
  `HNetCfg.FWRule` and assigning properties needs no elevation and validates
  `RemoteAddresses` on assignment. Only `Rules.Add()` persists. See `tests/FirewallProbe.cs`.

## Data

`data/servers.json` — 15 datacenters, 322 ranges, keyed by Blizzard's codes (LAX1, ORD1, AMS1,
SYD2, GSG1, GTK1, ICN1, GAN3, TPE1, GEN1, GUW2, GBR1, MES1, GMEC1, GMEC2). Seeded from
[foryVERX/Overwatch-Server-Selector](https://github.com/foryVERX/Overwatch-Server-Selector)
(no license file at time of use — factual address data only, no code). Blizzard publishes
nothing official. Regenerate with `build-catalog.ps1` if the upstream lists are refreshed.

`tools/capture-server.ps1` discovers the live server IP via `pktmon` (in-box); that is the
supported way to verify or extend the catalog.

`tools/find-probes.cmd` refreshes every `pingTarget` by pinging addresses inside each
datacenter's own ranges. Dry run by default; `--write` updates `data/servers.json`, rewriting
only the `pingTarget` lines so the diff stays reviewable. It reuses `IpMath` rather than
reimplementing interval subtraction — a throwaway PowerShell reimplementation of `Subtract`
silently produced a *negative* exclusive-space count during development, which is exactly the
kind of bug a second implementation buys you.

Probe-selection rules, each of which exists because the naive version was wrong:
- Prefer each datacenter's **exclusive** space (its ranges minus every other datacenter's),
  since an address in shared space cannot be attributed to one datacenter.
- But only when that space is at least `MinExclusiveAddresses` (4096). Taiwan has just 256
  exclusive addresses; probing them reported 209 ms against neighbours at 121-133 ms, because
  the sliver is not physically in Taipei. Below the threshold, fall back to the full ranges.
- When nothing answers, **clear** the target to null rather than leaving a stale one. A dead
  address renders as `n/a`, implying a real target was tried; null renders as `–`, which
  honestly says there is no verified probe.

Ping readings are best-effort regional estimates, not a reachability test — ICMP is widely
dropped at cloud edges while the game's UDP traffic connects fine.

## State restore

On launch, `RestoreFromFirewall()` reconstructs the selection from the live rules instead of
defaulting to everything-checked. **The firewall is the source of truth, deliberately** - a
settings file would drift the moment rules were changed by another elevated instance, removed
by hand, or cleared by uninstall.

`Apply` writes the playable codes into each rule's `Description` ("... playable: SYD2, GSG1"),
and restore parses that back. Addresses are only a fallback: datacenter ranges overlap, so a
datacenter wholly inside another's space (Qatar sits entirely within Saudi Arabia's ranges)
cannot be told apart by address alone.

Restored state is always presented in "play only on checked" terms, since that is what the
rules encode - the set that stayed reachable. Which mode originally produced them is not
recorded and does not need to be; both reduce to the same block set.

## Icon

`assets/app.ico` is generated by `assets/make-icon.ps1` and committed, so a build never
depends on running PowerShell. It is a hand-assembled multi-resolution ICO (9 sizes, PNG
entries) because System.Drawing can only save single-size icons. The glyph drops from two
latitude bands plus a meridian to a single equator below 24px, where the detail turns to mush.

Both wiring steps are needed: `/win32icon` gives the exe its Explorer icon, and the same file
is embedded via `/resource:assets\app.ico,app.ico` because WinForms otherwise shows its own
default in the title bar regardless of the exe icon.

Note: `System.Drawing.Icon` returns the 128px entry when asked for 256 — a legacy GDI+ quirk
with PNG-compressed entries, not a malformed file. The Windows shell reads it correctly.

## Verification status

Verified on Windows 11 (2026-08-26): build, all 30 unit assertions, firewall COM probe, form
construction, and the **live apply path end to end** — `FirewallManager.Apply` with a real
"SYD2 only" selection produced one rule (`OW2ServerPicker-01`, outbound / block / UDP / 149
ranges) scoped to `D:\Program Files (x86)\Overwatch\_retail_\Overwatch.exe`.

Cross-checked two ways rather than trusting the writing API: read back via `Get-NetFirewallRule`
(NetSecurity module, independent of the COM path that created it), then all 63 SYD2 boundary and
midpoint addresses probed against the live rule's actual `RemoteAddress` list — none blocked.

Still unverified: the **GitHub Actions workflow has never run**, in particular whether
`FormSmoke` can create a window on a `windows-latest` runner.
