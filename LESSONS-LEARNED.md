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

### [API] — 2026-05-22
- **MISTAKE**: `KeepAwakeController.stop()` first only invalidated the Timer. The `UserIsActive` assertion from `IOPMAssertionDeclareUserActivity` is held until explicitly released, not auto-expired, so toggling Keep Awake off would have left the Mac awake forever.
- **FIX**: `stop()` must call `IOPMAssertionRelease(assertionID)` and reset the id to 0 so a later `start()` opens a fresh assertion. Verified with a standalone probe: declare gives an assertion in `pmset -g assertions`, release removes it.
- **CONTEXT**: matrix-bg-menubar.swift `KeepAwakeController`. Any IOKit power assertion (`IOPMAssertionDeclareUserActivity`, `IOPMAssertionCreateWithName`) is a held resource and needs a paired release.
- **DETECTION**: `grep -nE 'IOPMAssertionDeclareUserActivity|IOPMAssertionCreateWithName' *.swift` then confirm each has a matching `IOPMAssertionRelease`.

### [UI/UX] — 2026-05-22
- **MISTAKE**: Scheduled the Keep Awake refresh timer with `Timer.scheduledTimer`, which installs it in `.default` run-loop mode only. While an NSMenu is open the run loop is in event-tracking mode, so the timer stalls and the keep-awake re-declare can lapse.
- **FIX**: Build the timer with `Timer(timeInterval:repeats:)` then `RunLoop.main.add(t, forMode: .common)` so it fires during modal tracking too.
- **CONTEXT**: matrix-bg-menubar.swift. The existing `startIdleTimer()` has the same `.default`-only pattern, non-critical there but the same gotcha.
- **DETECTION**: `grep -n 'Timer.scheduledTimer' *.swift` — for any timer that must fire while menus are open, switch to `RunLoop.add` with `.common`.

### [Architecture] — 2026-05-22
- **MISTAKE**: `RainController` used `private let queue = DispatchQueue(label: "matrix-bg.rain")` with `queue.sync { ... }` around every public method. Some main-thread path recurses into a sync block on the same queue (suspected: re-entrancy through `Process.run`/`terminate`/`waitUntilExit` interacting with `idleTick` or `buildMenu`), tripping libdispatch's "dispatch_sync called on queue already owned by current thread" assertion. App crashed with `EXC_BREAKPOINT / SIGTRAP`. Three prior crash reports (May 15, May 22 11:28, May 22 17:19) all share the same `asi` string. When the menu bar app dies the Keep Awake assertion dies with it and the Mac locks.
- **FIX**: Swap the serial DispatchQueue for `NSRecursiveLock`. Same mutual-exclusion semantics, but the same thread can re-enter without libdispatch's abort. Belt-and-suspenders: a `~/Library/LaunchAgents/com.matrix-bg.watchdog.plist` with `KeepAlive={SuccessfulExit=false}` respawns MatrixBG within 1s if any other crash path ever fires (verified by SIGKILL).
- **CONTEXT**: matrix-bg-menubar.swift `RainController`. Release build has no dSYM, so the exact recursive caller could not be symbolicated. `NSRecursiveLock` is the right primitive whenever same-thread re-entry is plausible but cross-thread mutual exclusion is still required.
- **DETECTION**: `grep -n 'queue.sync' *.swift` for any DispatchQueue protecting state that crosses methods that may call each other (directly or through framework callbacks like Process/Pipe); prefer `NSRecursiveLock` or flatten the call graph.

### [API] - 2026-08-21
- **MISTAKE**: First draft of the GDI font-fallback loop in matrix-bg-windows.c called `DeleteObject` on the font that was still selected into the memory DC (created candidate, selected it, then deleted it on mismatch before selecting the next one).
- **FIX**: Select the NEXT candidate first (which deselects the previous one), then delete the previous candidate. Same rule for every GDI object: never delete a bitmap/font/brush while a DC has it selected.
- **CONTEXT**: matrix-bg-windows.c font fallback chain (MS Gothic, Yu Gothic UI, Meiryo, Consolas). Deleting a selected GDI object often silently works in testing and corrupts rendering in the field.
- **DETECTION**: `grep -n "DeleteObject" matrix-bg-windows.c` and confirm each deletion happens only after a SelectObject swapped the object out.

### [API] - 2026-08-21
- **MISTAKE**: Fullscreen dismiss first shipped as a 20Hz `GetAsyncKeyState` poll over all VKs. Codex found two real gaps: a key pressed and released between two 50ms ticks is missed entirely, and scroll wheel input has no VK state at all, so wheel never dismissed.
- **FIX**: Raw Input (`RegisterRawInputDevices` with `RIDEV_INPUTSINK` on a hidden window) delivers key/button/wheel events passively without focus or hooks, mirroring the macOS `addGlobalMonitorForEvents` model. Event-driven beats state-polling for input detection.
- **CONTEXT**: matrix-bg-windows.c WM_INPUT handler. Any "react to input without stealing focus" requirement on Windows should reach for raw input, not GetAsyncKeyState scans.
- **DETECTION**: `grep -n "GetAsyncKeyState" *.c` in a loop over VK codes is the smell; check whether tap-between-polls and wheel input are handled.

### [Testing] - 2026-08-21
- **MISTAKE**: Playwright screenshots of the dobepros /matrix hero were taken right after `networkidle`, catching the page mid entrance animation (matrixFadeIn staggers delays up to 1.1s), so the download buttons were invisible in the evidence screenshots even though DOM assertions passed.
- **FIX**: `page.wait_for_timeout(2500)` after `networkidle` before screenshotting any page with CSS entrance animations, then read the screenshot back to confirm the elements are actually visible.
- **CONTEXT**: Ship-test screenshots are evidence for humans. A passing DOM assertion with an empty-looking screenshot fails the "read the screenshot back" gate.
- **DETECTION**: If a screenshot shows missing UI that DOM assertions say exists, grep the page CSS for `animation.*both` / `fadeIn` delays before assuming a bug.

### [Architecture] - 2026-08-22
- **MISTAKE**: The C Windows port's wallpaper mode silently draws nothing on Windows 11 24H2/25H2 (build 26100+). Progman is now created with WS_EX_NOREDIRECTIONBITMAP and SHELLDLL_DefView is a layered child, so a plain non-layered WS_CHILD (our Progman-direct fallback) is never composited. Report came from a real 24H2 machine via Jacob.
- **FIX**: The technique that works (same as Lively Wallpaper, now in windows/MatrixBG.cs): send Progman 0x052C (0xD,1); make the window WS_CHILD|WS_CLIPSIBLINGS|WS_CLIPCHILDREN with WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_CONTROLPARENT|WS_EX_NOACTIVATE + SetLayeredWindowAttributes(alpha 255); SetParent to Progman z-ordered directly under SHELLDLL_DefView; then nudge the size (w-1/h-1 then full) because DWM only starts compositing after a size change. Classic WorkerW child remains the fallback for older builds.
- **CONTEXT**: Any "draw behind desktop icons" feature must branch on GetWindowLong(Progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP to pick the attach strategy. The windows/ C# app does this in Native.Probe(); matrix-bg-windows.c retains the old approach and is superseded as the shipped Windows artifact.
- **DETECTION**: On any new Windows build, run with --debug and check the attach log line; visually confirm rain BEHIND icons, not absent and not covering them.
