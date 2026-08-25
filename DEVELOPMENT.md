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
| `src/MainForm.cs` | UI; `ComputeBlockSet()` is where selection becomes intervals. |

Catalog resolution: `servers.json` beside the exe wins; otherwise the copy embedded via
`/resource:` at build time. Rules are always named `OW2ServerPicker-NN`.

## Gotchas

- **Datacenter ranges overlap.** Singapore's `34.124.0.0-34.124.255.255` contains Sydney's
  `34.124.40.0/23`. Windows Firewall prefers Block over Allow, so per-datacenter block rules
  would kill the datacenter the user kept. Everything must go through
  `IpMath.Subtract(rejected, kept)`. `tests/SelfTest.cs` asserts this against the real catalog.
- **Never read selection state off the ListView.** `ListView.OnHandleCreated` re-inserts items
  and fires `ItemChecked` while `Tag` is still unmapped — that caused a startup
  `NullReferenceException` inside the message loop, which surfaces only as a modal
  "Microsoft .NET Framework" dialog. State lives on `Datacenter.Selected`; `_uiReady` gates
  recomputation until `OnShown`.
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
