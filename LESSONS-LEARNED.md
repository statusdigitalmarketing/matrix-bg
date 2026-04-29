# Lessons Learned — matrix-bg

> This file is READ at the start of every session and APPENDED TO whenever a mistake is made or a non-obvious pattern is discovered.
> It accumulates institutional knowledge across sessions. Never delete entries — only mark outdated ones.

---

<!-- New lessons are appended below this line -->

### [UI/UX] — 2026-04-29
- **MISTAKE**: Used a recurring `Timer` to rebuild the `NSStatusItem.menu` every 2s in the menu bar app. Reassigning `statusItem.menu` while the menu is open closes/refreshes it under the user's cursor.
- **FIX**: Conform to `NSMenuDelegate` and rebuild the menu inside `menuWillOpen(_:)` instead. State is fresh every click, no flicker.
- **CONTEXT**: matrix-bg-menubar.swift `AppDelegate`. Applies to any AppKit menu whose contents depend on app state (running/stopped, config values).
- **DETECTION**: `grep -n "scheduledTimer.*statusItem.menu" *.swift`

### [Architecture] — 2026-04-29
- **MISTAKE**: Original idle screensaver shipped two pieces (bash watcher + launchd plist) parallel to the rendering binary. Adding the menu bar app made it three components polling `IOHIDSystem` and managing the same state.
- **FIX**: Menu bar app owns idle detection itself via a 5s `Timer` calling `ioreg`. Old launchd watcher is unloaded + removed by the installer (`Install matrix-bg.command`).
- **CONTEXT**: Run-once components > many always-running daemons. The menu bar app is already always-running, so let it own related lifecycle (idle polling, config UI, process management).
- **DETECTION**: `launchctl list | grep matrix-bg.idle-watcher` — should be empty after migration.
