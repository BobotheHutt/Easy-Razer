# Easy Razer

A small Windows tray app that replaces the parts of Razer Synapse I actually use:

- **Per-game keybind profiles** for devices with onboard memory (Tartarus Pro, Naga Trinity).
  Profiles switch automatically when a linked game comes to the front. No window has to be open.
- **One static color** for every RGB device, set from one place (via OpenRGB).

- **Turbo** on any button (e.g. fast left/right click while held), stored on the mouse.

Not included: Chroma effects, general macros, cloud sync, firmware updates, audio settings.

## How it works

| Part | How |
|---|---|
| Spotting the game | Windows notifies the app whenever a different window comes to the front (`SetWinEventHook`). The app reads that program's exe name and looks for a profile. Nothing is injected into games. The only keyboard hook is while you're recording a bind in the settings window; it's removed as soon as the bind is set, cancelled, or you leave the window. |
| Keybinds | Written into the device's own memory over USB (HID feature reports, same channel Synapse uses). The device itself sends the remapped key, so games and anti-cheat just see a normal key press. Game profiles go to the device's live memory (no flash wear); the Default profile is also stored permanently, but only after a live write has been read back correctly, so the device behaves even when the app isn't running. |
| Lighting | OpenRGB runs hidden as a local server; the app asks it to set every device to your color. Prefers each device's hardware "Static" mode so the device remembers it. |
| Re-applying | At startup, after sleep, and when a device is plugged back in. |

Profiles can be active **only while the game is in front** (alt-tab = desktop binds) or **as long as
the game is running**. Binds not set in a game profile fall back to the Default profile, then to factory.

## Status

| | |
|---|---|
| Tray app, profiles, game switching, settings window | Done, needs testing on real hardware |
| Lighting through OpenRGB | Done, needs testing |
| Naga Trinity keybinds | Done, using the Naga Pro button table (assumed the same) |
| Tartarus Pro keybinds | Done; run **Settings → Devices → Learn keys** once first |
| Import profiles from Synapse | Planned, needs a sample exported profile |

Button-binding commands come from public reverse-engineering work, not guesswork:
[OpenSnek](https://github.com/gh123man/OpenSnek) (USB protocol notes, Naga Pro button table, turbo/DPI blocks),
[openrazer-win](https://github.com/GavinViney/openrazer-win), and
[OpenRazer issue #2031](https://github.com/openrazer/openrazer/issues/2031) (keyboard variants).

## Setup

Do this in order:

1. **While Synapse is still installed**
   - Update firmware on all devices.
   - Export your Synapse profiles (profile list → `…` → Export) and screenshot your binds and linked games.
   - Set lighting/settings for any device OpenRGB doesn't support. They keep what's saved on the device.
2. **Install OpenRGB** from <https://openrgb.org> (the Windows build is a folder; put it somewhere permanent, e.g. `C:\Tools\OpenRGB`).
3. **Uninstall Synapse** (and Razer Central / Razer services). They fight over the devices.
4. **Download Easy Razer**: go to [Releases](../../releases), open **Latest build**, download `EasyRazer.exe`,
   and put it somewhere permanent (e.g. `C:\Tools\EasyRazer`). The first run shows a SmartScreen warning
   because it isn't code-signed: **More info → Run anyway**.
5. In Easy Razer's settings:
   - **Lighting**: browse to `OpenRGB.exe`, pick your color, Save.
   - **Profiles**: add a profile per game, link the game (start it, then "Add running game…"), set binds, Save.
   - **Devices**: select the Tartarus Pro, click **Learn keys…**, and press each key as it lights up (about a minute).
   - **General**: tick "Start Easy Razer with Windows".

Settings and the log are in `%APPDATA%\EasyRazer`. The log is the first place to look if something
doesn't work (tray icon → Open log).

## Building from source

Needs the .NET 10 SDK.

```
dotnet publish src/EasyRazer/EasyRazer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Every push to `main` also builds the exe on GitHub Actions and updates the **Latest build** release.

## Adding a device

Lighting needs nothing: anything OpenRGB supports gets the color. For keybinds, add a class next to
`src/EasyRazer/Devices/Razer/NagaTrinity.cs` with the USB product id and the button list, and register it
in `ProfileEngine.Devices`.
