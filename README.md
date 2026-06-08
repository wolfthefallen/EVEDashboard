# EVE Dashboard

A free, native-Windows dashboard that shows your **live EVE Online mining stats** while you
play - per-pilot yield, volume, and ISK/hour - by reading the game's own log files. No
automation, no account login, no interaction with the game client.

> EVE Dashboard is a read-only observer. It never plays the game for you, never touches the
> EVE client, and never logs into your account.

## What it does

- **Live mining tracker** - mining ticks appear on screen about a second after EVE writes them.
- **Per-pilot stats** - units mined, volume (m3), ISK value, and ISK/hour for every character
  in your fleet.
- **ISK valuations** - converts your ore to refined materials and prices it against Jita (or a
  station you choose) using public market data.
- **Sessions** - group your mining into named sessions you can start, pause, resume, and stop.
- **Reports & export** - review saved sessions and export them to JSON or Excel.
- **A normal window** - size and dock it however you like (works great with FancyZones on an
  ultrawide). It is not an overlay and is never forced on top.

## Download

Get the latest Windows build from the
**[Releases page](https://github.com/wolfthefallen/EVEDashboard/releases/latest)**.

It is a self-contained `.exe` - no installer and no separate .NET runtime to set up. Download
it, run it, done.

**Requirements:** Windows 10 or 11, and EVE Online launched at least once on this PC (so your
gamelog folder exists).

## Your data stays on your PC

EVE Dashboard is built to be private:

- It reads your **local** EVE gamelog files and stores everything it tracks in its own database
  under `%APPDATA%\EveDashboard\` on your machine.
- It **never uploads your data**, never logs into your EVE account, and never sends anything
  about you anywhere.
- The only network traffic is **downloading public reference data**: current market prices
  (from the GoonMetrics API) and EVE's Static Data Export (from everef.net). That traffic is
  read-only and contains nothing personal.

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
