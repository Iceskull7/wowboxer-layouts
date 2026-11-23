# WoWBoxer Offhand

Free, open-source (MIT) Windows app for **manual** World of Warcraft multiboxing.
It opens your WoW clients and places 1–10 game windows (two parties) in a layout you choose, on one monitor or several, and keeps
them there. This fork adds per-client Offhand window spanning and a low-resource tray mode. It has its own release series starting at 1.0.0.

![The Setup tab: client count, layout cards, a to-scale map of the monitor and the squad list](docs/screenshots/site/setup-main-side.webp)

## What it does, and what it never does

WoWBoxer arranges the game windows. When you extend an Offhand client, it also gives the installed addon the display layout it needs to place the map and other panels.

| It does | It never does |
|---|---|
| Find WoW windows by their process and install folder | Send keystrokes or mouse clicks to the game |
| Move and resize them into your layout, without stealing focus | Broadcast input, round-robin or switch focus with hotkeys |
| Keep a window "always on top" if you ask | Read or write game memory, inject code or hook input |
| Open the login screens of the clients you are missing (you press Play) | Log in for you or store passwords |
| Optionally set WoW's UI Scale in `Config.wtf` so the action bar fits (with a backup) | Send telemetry. The update check is its only network call |
| Turn Windows' "activate a window by hovering over it" on or off for you, with a delay – the WoWBoxer way to switch windows. Off by default, your own values kept | Change any Windows setting without your click |
| Optional, off by default (from v0.7.0, beta first): **window swap hotkeys** that move a client's window into the main slot and the one there into its old place. The hotkey goes to WoWBoxer, not to the game; focus never changes | Send the hotkey, or any other key, to the game |

Window swap: moves windows only – nothing is sent to the game. Blizzard has not ruled on window-swap tools; use at your
own judgement.

**Compliance.** Blizzard allows multiboxing as long as every action in the game comes from one key press or click
of yours on one character. Since 2022 it no longer allows software that broadcasts input to several clients. This
app is built to stay inside that line: it never touches input at all, so you play each character yourself. Using it
is still your own responsibility. Check Blizzard's current rules for the game you play.

## Install

Download **WoWBoxer-Offhand-1.0.1-Setup.exe** from the fork’s latest release for a Windows x64 installation. Close any running
copy of WoWBoxer, run Setup and open **WoWBoxer Offhand** from the Start menu. Setup includes the .NET
runtime and installs for your Windows user. Installed copies of 1.0.0 can update to 1.0.1 with update checks enabled.
To uninstall, use **Settings → Apps → Installed apps**.
[Windows releases](https://github.com/Iceskull7/wowboxer-layouts/releases/latest)

The portable ZIP is also available. Extract it in full and run `WoWBoxer.exe`. For a portable update, quit
WoWBoxer from the tray and extract the new ZIP into a fresh folder.

## Offhand on one WoW client

1. Install the **Offhand addon** for the WoW installation you play, enable it on your main character, and set that client to **Windowed**. Quit the standalone Offhand Companion if it is running. [Offhand addon](https://github.com/N4UX-GIT/Offhand-DualMonitor)
2. Arrange your squad in WoWBoxer. Under **Settings → Offhand workspace**, pick the running client by slot, title or process ID. **Identify** shows the ID on that window. Choose the gameplay monitor, an adjacent workspace monitor and the share of that monitor to use. Click **Extend window**.
3. In the extended client, type `/reload` to load the new display layout. For first-time setup, or if you previously chose **Configure Manually**, open `/offhand guide` and choose **Use Companion**. Follow **Confirm placement** and **Start Calibration** in the guide. You can then place your map in the Offhand workspace.

**Example with three clients and four monitors.** Choose two side-by-side displays. Use the first for gameplay and choose **50%** of the second for Offhand. Stack the other two clients in the outer half of the second display using the custom layout. With equal-width displays, the workspace then occupies one third of the extended window.

**Which clients see the layout?** WoWBoxer resizes only the selected window. It writes the layout to `Interface/AddOns/Offhand/Core/CompanionTopology.lua` in that client's WoW installation. Every client using that addon folder can read the layout at login or `/reload`. To use Offhand on the main character alone, disable the addon for your other characters in WoW’s AddOns menu. If the addon is absent, **Extend window** still resizes the client.

Choose **Restore window** to return the client to its previous size and position. The addon file keeps the last layout for your next session. After restarting WoW, select the new running client before extending it.

## Minimize while playing

Use the minimize button to keep WoWBoxer in the Windows notification area. Low-resource mode is on by default.
The app refreshes hidden UI less often. Active features such as hover to activate, client placement, Offhand and
configured hotkeys continue working. Double-click the tray icon to reopen the window. **Quit** exits the app and
applies your configured exit behavior for hover to activate.

## Using it

- **Setup** – how many clients (1–10; 6–10 = two parties), a layout card (Main + side, Overlays, Grid,
  Custom) and how many monitors to use, then fine-tune on a to-scale map of the chosen monitors: drag (also
  across monitors), resize, snap, nudge, undo. One button opens the missing
  clients; new WoW windows drop into the next free slot by themselves. Layouts save automatically; share them as
  `WBX1:` codes or files, or capture an arrangement you made by hand.
- **Guide** – WoW display settings, switching windows, troubleshooting.
- **Help** – common problems with their fixes, **Report a problem** (a report you see first, names hidden, pasted by
  you into `#setup-help` on the [WoWBoxer Discord](https://discord.gg/Q5xwbqhKzQ)), Diagnose and the logs.
- **Settings** – window rules, action bar (UI Scale), titles, tools (Identify, Diagnose, title bars back), shortcut,
  updates (Stable / Beta channel), Experimental, and Advanced (window swap hotkeys, off by default).
- **Start / Stop placing windows** (main screen and tray). The app opens stopped, so it never touches the windows of
  another game until you press Start. A desktop shortcut starts a layout in one click.

Tips:
- Set WoW's Display Mode to **Windowed**. Title bars stay on by default (removing them made WoW draw its UI at the
  wrong size in our tests).
- Classic Era's action bar is only complete when a window is **4:3 or wider** at the UI Scale you use; *Auto-fit
  action bar* handles that. Details: [docs/wow-display-findings.md](docs/wow-display-findings.md).
- If windows do not move, Battle.net or WoW probably runs as administrator. Start them normally; Diagnose tells you.

## Command line (`wbx`)

The same engine without a window, and our test harness. Installed next to the app.

```
wbx start      start Battle.net, then place each WoW window as it opens (you press Play yourself)
wbx arrange    place the WoW windows that are open now, oldest first
wbx diagnose   write a report (monitors, windows, processes, admin rights, Config.wtf) and try one placement
wbx info       show monitors, the planned slots, the open WoW windows and the size each renders at
wbx capture    save the current window arrangement as a layout:  wbx capture --save my.json
wbx snap       save a picture of what each WoW window shows (read-only)
wbx restore    give WoW windows their title bars back
wbx layout     print a layout as JSON and as a WBX1: share code
```

Examples: `wbx arrange --preset grid`, `wbx start --profile my.json --names Paladin,Priest,Warlock`,
`wbx arrange --preset dual --clients 5`. Run `wbx help` for every option.

## Build from source

Needs Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version pinned in
`global.json`).

```
git clone https://github.com/Iceskull7/wowboxer-layouts.git
cd wowboxer-layouts
dotnet build WoWBoxer.Layouts.slnx
dotnet test WoWBoxer.Layouts.slnx
dotnet run --project src/WoWBoxer.Layouts.App
```

A copy run from source never updates itself. Layout of the code: `src/WoWBoxer.Layouts.Core` (layout maths, no
Windows calls, fully unit-tested), `src/WoWBoxer.Layouts.Win32` (finding and placing windows), `src/WoWBoxer.Layouts.App`
(WPF app), `src/WoWBoxer.Layouts.Cli` (`wbx`). Plan and design notes: [docs/plan.md](docs/plan.md).
Releases: [docs/releasing.md](docs/releasing.md).

## Contributing, security, privacy

- [CONTRIBUTING.md](CONTRIBUTING.md) – bug reports, pull requests, the hard rules every change must keep.
- [SECURITY.md](SECURITY.md) – how to report a vulnerability privately.
- [docs/privacy.md](docs/privacy.md) – no telemetry; what stays on your PC.
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Licence and trademarks

MIT – see [LICENSE](LICENSE). Third-party parts: [docs/third-party.md](docs/third-party.md).

World of Warcraft® and Battle.net® are trademarks or registered trademarks of Blizzard Entertainment, Inc. in the U.S.
and/or other countries. WoWBoxer is an independent fan project. It is not affiliated with, endorsed, sponsored or
approved by Blizzard Entertainment, and it uses no Blizzard art, logos or code.
