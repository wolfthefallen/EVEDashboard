# EVE Dashboard

A free, native-Windows dashboard that shows your **live EVE Online mining stats** while you
play - per-pilot yield, volume, and ISK/hour - by reading the game's own log files. No
automation, no account login, no interaction with the game client.

> EVE Dashboard is a read-only observer. It never plays the game for you, never touches the
> EVE client, and never logs into your account.

## What it does

- **Live mining tracker** - mining ticks appear on screen about a second after EVE writes them.
- **Per-pilot stats** - volume mined (total m3 and m3/sec), ISK value, ISK/hour, crit stats, and
  refined output for every character in your fleet.
- **ISK valuations** - converts your ore to refined materials and prices it against Jita (or a
  station you choose) using public market data.
- **Sessions** - group your mining into named sessions you can start, pause, continue, and stop.
  Idle sessions auto-pause, and the first new mining tick auto-resumes them.
- **Fleets & roles** - save fleets of pilots with roles (Miner / Booster / Cyno / Combat) and
  start sessions from them.
- **Reports & export** - review saved sessions, compare them, see lifetime totals, and export
  them to JSON or Excel.
- **Haul Value** - paste an EVE inventory (or pick saved sessions) and see what it is worth at
  each of your market stations.
- **Build from history** - forgot to press Start? Build a session from a past date/time window
  straight from your gamelogs, or combine saved sessions into one. The originals are archived,
  never deleted, and nothing is ever counted twice.
- **A session for every day you mined** - point it at a date range (or at every log you have) and
  it works out which days you were out, who was out with you, and where the breaks were, then
  builds one session per day named for that day. No fleet to pick: the roster comes from the logs.
- **A normal window** - size and dock it however you like (works great with FancyZones on an
  ultrawide). It is not an overlay and is never forced on top.

## Download

Get the latest Windows build from the
**[Releases page](https://github.com/wolfthefallen/EVEDashboard/releases/latest)**.

It is a self-contained `.exe` - no installer and no separate .NET runtime to set up. Download
it, run it, done.

Every release is built by GitHub Actions from the tagged commit on `master`; what changed in each
version is in [`CHANGELOG.md`](CHANGELOG.md).

**Staying current:** on launch EVE Dashboard checks GitHub for a newer release and, if one is
out, asks whether to open the download page. It never downloads or installs anything itself -
you stay in control of updating.

**Requirements:** Windows 10 or 11, and EVE Online launched at least once on this PC (so your
gamelog folder exists).

## Your data stays on your PC

EVE Dashboard is built to be private:

- It reads your **local** EVE gamelog files and stores everything it tracks in its own database
  under `%APPDATA%\EveDashboard\` on your machine.
- It **never uploads your data**, never logs into your EVE account, and never sends anything
  about you anywhere.
- The only network traffic is **read-only public data**: current market prices (from the
  GoonMetrics API by default, or CCP's public ESI if you select it in Settings - neither needs
  a login), EVE's Static Data Export (from everef.net), and a check against the public GitHub
  Releases API on launch to see if a newer version is out. None of it is personal.

## Is this allowed? (EVE Online ToS)

Yes. EVE Dashboard only does things CCP's Terms of Service permit for third-party tools: it
**reads log files the game writes** and **calls public data APIs**. It never reads the game's
memory, never modifies the client, and never sends input (clicks or keys) to EVE. See the
[technical reference](docs/technical.md#eve-online-tos-posture) for the full breakdown.

## For developers

The architecture, build steps, data schema, and internals live in
**[docs/technical.md](docs/technical.md)**.

```powershell
dotnet build                                     # build
dotnet test                                      # run the test suite
dotnet run --project src/EveDashboard.App.Wpf    # launch from source
```

## License

Released under the [MIT License](LICENSE) - (c) 2026 Erik Daguerre. Third-party dependency
licenses are listed in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).

## Legal

EVE Online and the EVE logo are the registered trademarks of CCP hf. All rights are reserved
worldwide. All other trademarks are the property of their respective owners. EVE Online, the
EVE logo, EVE and all associated logos and designs are the intellectual property of CCP hf.
All artwork, screenshots, characters, vehicles, storylines, world facts or other recognizable
features of the intellectual property relating to these trademarks are likewise the
intellectual property of CCP hf. CCP hf. has granted permission to EVE Dashboard to use EVE
Online and all associated logos and designs for promotional and information purposes but does
not endorse, and is not in any way affiliated with, EVE Dashboard. CCP is in no way
responsible for the content on or functioning of this application, nor can it be liable for
any damage arising from the use of this application.
