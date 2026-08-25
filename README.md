# Overwatch 2 Server Picker

Tired of getting thrown onto a server on the other side of the world? This little app lets you
pick which Overwatch 2 datacenters you're willing to play on, and blocks the rest.

Tick the servers you want to avoid. Press Apply. Restart Overwatch. Done.

---

## Download

### [⬇ Download Ow2ServerPicker.zip](https://github.com/egohero/ow2-server-picker/releases/latest/download/Ow2ServerPicker.zip)

Nothing to install first — no .NET, no runtime, no redistributables. It's one small program
that runs on any Windows 10 or 11 PC.

---

## How to use it

**1. Unzip the file.** Right-click the download → *Extract All*. Don't run it from inside the
zip.

**2. Double-click `install.cmd`.** This puts the app in your Start Menu. Press a key when it
says *Installed*.

> Windows may show a blue **"Windows protected your PC"** box. That appears for any program
> that isn't code-signed (certificates cost money). Click **More info** → **Run anyway**.
> Prefer not to? Skip `install.cmd` and just double-click `Ow2ServerPicker.exe` in the
> extracted folder — it works exactly the same, it just won't be in your Start Menu.

**3. Press Start, type "Overwatch", and open *Overwatch 2 Server Picker*.**

**4. Say Yes to the admin prompt.** Windows asks because the app changes firewall rules. That
is the only thing it uses admin for.

**5. Choose your servers.**

- Leave the mode on **Block checked**, and tick only the servers you want to *avoid*. This is
  the recommended way — see [Which mode should I use?](#which-mode-should-i-use).
- Not sure which are close to you? Click **Ping all**. Lower is closer — green is good, red is
  far away. You can click the column headings to sort.

**6. Click Apply.**

**7. Fully quit Overwatch and start it again.** It won't affect a game that's already running.

That's it. To undo everything, open the app and click **Remove all blocks**.

---

## Which mode should I use?

**Block checked** — tick the servers you don't want. **Recommended.**

**Play only on checked** — tick the servers you *do* want; everything else gets blocked.

They sound similar, but the second is far more aggressive. Blocking one datacenter affects
around 460,000 addresses. Allowing only one blocks over 6 million, including parts of
Blizzard's own network. Bigger blocks mean more chance of something breaking.

**If you just want to avoid one or two servers, use Block checked.** Only use the other mode if
you really want to lock yourself to a single region — and expect longer queues.

---

## Things worth knowing

**Your queues may get longer.** Overwatch matches you with players near you. Cut out servers
and there are fewer matches you can join, especially late at night. If queues get bad, remove a
block or two.

**It rules servers out, it doesn't force one in.** Overwatch still decides where to put you.
This only takes options off the table.

**Blizzard hasn't blessed this.** Their terms broadly prohibit interfering with how the game
runs. Firewall-based server blocking is common and we're not aware of anyone being banned for
it, but it isn't officially sanctioned either. Your account, your call.

**The server list goes out of date.** Blizzard moves servers around. If one you blocked starts
appearing again, the addresses changed — see [Keeping the list current](#keeping-the-list-current).

---

## If something goes wrong

**Overwatch won't connect, or crashes when joining a match.**
Open the app, click **Remove all blocks**, restart Overwatch. If that fixes it you blocked too
much — block fewer servers, and use **Block checked** rather than **Play only on checked**.

**The app says "Overwatch.exe not found".**
Click **Locate Overwatch.exe** and point it at your install, usually
`Overwatch\_retail_\Overwatch.exe`. Note that's *not* `Overwatch Launcher.exe` — the launcher
just starts the real game and closes.

**Nothing seems to have changed.**
Did you fully quit Overwatch and restart it? Blocks only apply to a fresh connection.

**I want to see exactly what it did.**
In PowerShell:

```
Get-NetFirewallRule -DisplayName 'OW2ServerPicker*'
```

**I want it gone completely.**
Click **Remove all blocks** in the app, then run `uninstall.cmd` from the install folder. It
double-checks for leftover firewall rules and offers to clear them.

---

## What it does under the hood

Every rule is narrowed three ways, so it only touches Overwatch's game traffic:

- **Only Overwatch** — rules are tied to `Overwatch.exe`, so nothing else on your PC is
  affected, even though game servers share addresses with other cloud services.
- **Only UDP** — your Battle.net login, friends list and downloads are TCP, and are never
  touched.
- **Only game ports** — 6250 and 12000-64000. Voice chat, DNS and web traffic are left alone.

It also handles a subtlety most similar tools miss: **datacenter address ranges overlap.**
Singapore's range physically contains part of Sydney's. Windows Firewall always prefers a block
over an allow, so naively blocking Singapore takes Sydney down with it. This app subtracts the
servers you kept from the ones you're blocking before writing a single rule.

It reads your existing rules on startup too, so it always shows what's actually in force rather
than what it thinks it did last time.

---

## Keeping the list current

Server addresses live in `servers.json` next to the app. You can edit it directly — no rebuild
needed.

To find which datacenter you're actually on, run this from an **administrator** PowerShell
while you're in a match:

```
powershell -ExecutionPolicy Bypass -File tools\capture-server.ps1 -Seconds 45 -Lookup
```

It watches the game's traffic for 45 seconds and reports the server's address, ports and ping.
Found an address that isn't in `servers.json`? Please
[open an issue](https://github.com/egohero/ow2-server-picker/issues) with the address and
roughly where you are — that's what keeps the list accurate for everyone.

---

## For developers

Build with nothing but Windows — it uses the C# compiler already inside .NET Framework, so
there's no SDK or NuGet:

```
build.cmd
tests\run-tests.cmd
```

Version numbers are derived from git by `tools/gen-version.ps1`, so they cannot go stale.

Architecture, the reasoning behind each design decision, and the traps found along the way are
in [AGENTS.md](AGENTS.md).

The suite covers the interval arithmetic and sorting rules, and asserts against the real
shipped catalog that a "play only on Sydney" selection leaves no Sydney address in the block
set. `tests/FirewallProbe.cs` validates the Windows Firewall COM contract *without* writing
anything to your system.

---

## Where the server list comes from

Blizzard doesn't publish datacenter addresses. This list is derived from the
community-maintained lists in
[foryVERX/Overwatch-Server-Selector](https://github.com/foryVERX/Overwatch-Server-Selector),
reorganised per-datacenter using Blizzard's own codes (LAX1, ORD1, AMS1, SYD2, GSG1, …). That
project is the original of this idea and deserves the credit for the legwork.

These ranges are community-observed and unverified by Blizzard. They are broad — often whole
cloud regions rather than specific game servers — so treat them as a good starting point rather
than gospel.

---

## License

MIT — see [LICENSE](LICENSE).

Not affiliated with, endorsed by, or connected to Blizzard Entertainment. Overwatch is a
trademark of Blizzard Entertainment, Inc.
