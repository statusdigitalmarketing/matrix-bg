# MatrixBG 1.0

Matrix rain for Windows 11 — as a **background** behind your desktop icons and/or as a fullscreen **overlay**
screensaver that starts when you go idle. Lives in the tray. Single exe, no dependencies.

- `MatrixBG.exe`: the app (single instance, tray icon, optional launch at login via the tray toggle)
- `MatrixBG.cs` — full source (one file, C# 5 / .NET Framework 4.8)
- `build.cmd` — rebuilds the exe with the `csc.exe` that ships with Windows (no SDK needed)

## Tray menu (left- or right-click the green `0101` icon; it may sit in the `^` overflow)
**BACKGROUND (behind desktop icons)** — Enable background rain · Trigger: show background while a git command runs
(if the background is off, it appears while any `git.exe` is alive and goes away ~1.5 s after it exits) · Pause animation
**OVERLAY (fullscreen screensaver)** — Start overlay now · Stop overlay · Trigger: auto-start when idle ·
Trigger: idle timeout (30 s, 1, 2, 5, 10, 20, 30 min). Any mouse/keyboard input dismisses the overlay.
**SETTINGS** — Colour (green family: Matrix Green, Phosphor, Neon Lime, Emerald, Jade, Deep Forest, Mint, Sea Green;
*More colours* for other hues; Rainbow cycle; Custom… picker) · Second colour (for blends) · Two-colour blend
(Off / Fade between the two over time / Side by side left→right / Top→bottom / Mixed — each streak its own colour /
Mixed + fading — streaks drift between the two) · Speed · Density (two drops per column at Heavy/Downpour,
spawns avoid crowded neighbours) · Tail length (Short → Endless) · Glyph blinks (Off / Subtle / Normal / Lots) ·
Glyph size · Characters (Katakana / Binary / Hex / Latin)
**Behaviour** — Pause during video/audio playback (Core Audio peak meter on the default output) ·
Pause when a fullscreen app is active · Keep awake (blocks sleep/display-off) · Launch at login · Open settings folder
**Philips Hue lights** — Enable light effects · Lights follow the background too (runs while the permanent background
is on and while a git command runs) · Light colour: git/background
(default purple) · Light colour: overlay (default blue) · Pair with bridge… (press the bridge's link button, then OK) ·
Lights to use (default: all colour lights) · Test lights (5 s). Uses the bridge's local API only (no Hue account);
the bulbs' previous state is captured and restored when the effect ends. Bridge IP/key are stored in settings.txt.
**About MatrixBG… · Quit**

Double-click the icon = pause/resume. Settings: `%APPDATA%\MatrixBG\settings.txt` (plain `key=value`).

## MIDI control (tray > MIDI control)
Drive the rain live from an APC, Traktor, or anything that speaks MIDI. Pick an input port (install
[loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html) for a virtual port if the source is software like
Traktor) and enable. Default map, channel-agnostic unless `midichannel=` (1-16) is set in settings.txt:

| Control | Effect |
|---|---|
| CC 20 | Speed (continuous, overrides the preset) |
| CC 21 | Density (starves or floods streaks) |
| CC 22 | Hue (replaces colour A; overrides rainbow; blends and Hue bulbs follow) |
| CC 23 | Blend position (when a two-colour blend is on) |
| CC 24 | Tail length |
| CC 25 | Brightness |
| Any Note On | Burst of drops, velocity-scaled |
| Note 36 (C1) | Toggle the fullscreen overlay |
| MIDI clock | Beat pulse every quarter note (Start resets the phase) |

Overrides stay active until MIDI control is disabled. Tip: give MatrixBG its own loopMIDI port so an existing
Traktor/APC mapping never collides with another app's use of the same controller.

## Command line
- `--fullscreen` (or `--saver`) start the overlay immediately
- `--window` run in a normal window (testing)
- `--debug` write `%APPDATA%\MatrixBG\debug.log`

## How it attaches to the desktop (Windows 11 24H2 / 25H2)
Since build 26100+ `Progman` is created with `WS_EX_NOREDIRECTIONBITMAP`, `SHELLDLL_DefView` is a layered child and the
classic "child of WorkerW" trick no longer renders. What works (same as Lively Wallpaper):
1. send `Progman` `0x052C (0xD, 1)`;
2. create a popup window, then set style `WS_CHILD|WS_VISIBLE|WS_CLIPSIBLINGS|WS_CLIPCHILDREN|WS_TABSTOP` and
   ex-style `WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_CONTROLPARENT|WS_EX_NOACTIVATE`, `SetLayeredWindowAttributes(alpha=255)`;
3. `SetParent` to `Progman`, z-order directly under `SHELLDLL_DefView` and above `WorkerW`;
4. nudge the size (w-1/h-1 then full) — DWM only starts compositing the window after a size change.
Older builds fall back to the classic WorkerW child. A watchdog re-creates the window after Explorer restarts and
rebuilds the buffer on display changes.

## Rendering notes
One GDI DIB section is drawn by GDI+ and blitted into whichever windows are visible. The trail fade is a per-pixel
lookup with a hard floor to 0 — GDI+ alpha fills never reach black and leave ghost outlines at ~12/255.
