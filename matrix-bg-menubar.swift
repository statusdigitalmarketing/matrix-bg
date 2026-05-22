import AppKit
import ServiceManagement
import Foundation
import IOKit.pwr_mgt

// MARK: - Config (UserDefaults)
let kEnabled     = "screensaverEnabled"
let kIdleSeconds = "idleSeconds"
let kSkipMedia   = "skipMedia"
let kKeepAwake   = "keepAwake"

func defaults() -> UserDefaults { UserDefaults.standard }

func getBool(_ key: String, _ fallback: Bool) -> Bool {
    if defaults().object(forKey: key) == nil { return fallback }
    return defaults().bool(forKey: key)
}
func getInt(_ key: String, _ fallback: Int) -> Int {
    if defaults().object(forKey: key) == nil { return fallback }
    return defaults().integer(forKey: key)
}

// MARK: - Helpers

func bundleBinaryURL() -> URL {
    // Resolve matrix-bg-bin co-located with this binary inside the .app bundle,
    // or fall back to ~/.local/bin/matrix-bg-bin for source-tree runs.
    let exeURL = Bundle.main.executableURL ?? URL(fileURLWithPath: CommandLine.arguments[0])
    let coLocated = exeURL.deletingLastPathComponent().appendingPathComponent("matrix-bg-bin")
    if FileManager.default.isExecutableFile(atPath: coLocated.path) { return coLocated }
    let home = FileManager.default.homeDirectoryForCurrentUser
    return home.appendingPathComponent(".local/bin/matrix-bg-bin")
}

func systemIdleSeconds() -> Int {
    let task = Process()
    task.launchPath = "/usr/sbin/ioreg"
    task.arguments = ["-c", "IOHIDSystem"]
    let pipe = Pipe()
    task.standardOutput = pipe
    task.standardError = Pipe()
    do { try task.run() } catch { return 0 }
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    task.waitUntilExit()
    guard let output = String(data: data, encoding: .utf8) else { return 0 }
    for line in output.split(separator: "\n") where line.contains("HIDIdleTime") {
        let parts = line.split(separator: "=")
        if let last = parts.last, let ns = Int(last.trimmingCharacters(in: .whitespaces)) {
            return ns / 1_000_000_000
        }
    }
    return 0
}

func mediaIsPlaying() -> Bool {
    let task = Process()
    task.launchPath = "/usr/bin/pmset"
    task.arguments = ["-g", "assertions"]
    let pipe = Pipe()
    task.standardOutput = pipe
    task.standardError = Pipe()
    do { try task.run() } catch { return false }
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    task.waitUntilExit()
    guard let s = String(data: data, encoding: .utf8) else { return false }
    for line in s.split(separator: "\n") {
        if line.contains("PreventUserIdleDisplaySleep") && line.contains("1") { return true }
        if line.contains("NoIdleSleepAssertion") && line.contains("Playing") { return true }
    }
    return false
}

// One-shot cleanup: legacy bash watcher predates the menu bar app and would
// race with our Swift idle timer. Unload + remove on first launch.
func unloadLegacyWatcher() {
    let plist = (NSHomeDirectory() as NSString)
        .appendingPathComponent("Library/LaunchAgents/com.matrix-bg.idle-watcher.plist")
    guard FileManager.default.fileExists(atPath: plist) else { return }
    let task = Process()
    task.launchPath = "/bin/launchctl"
    task.arguments = ["unload", plist]
    task.standardOutput = Pipe()
    task.standardError = Pipe()
    try? task.run()
    task.waitUntilExit()
    try? FileManager.default.removeItem(atPath: plist)
}

// MARK: - Rain Process Manager
// Guards mutation of `process` and `_mode` against concurrent access from menu
// actions (main thread) and the idle-watcher background block.
//
// Uses NSRecursiveLock instead of a serial DispatchQueue. The previous
// dispatch-queue-based implementation tripped libdispatch's "dispatch_sync
// called on queue already owned by current thread" assertion and crashed the
// app (matrix-bg #1 follow-up, 2026-05-22, prior crashes May 15 and
// May 22 11:28 / 17:19). The exact recursive caller could not be pinpointed
// without symbolicated frames, but recursion can also slip in through any
// Foundation/AppKit re-entrancy during `Process.run` / `terminate` /
// `waitUntilExit` on the main thread. A recursive lock allows the same thread
// to re-enter without aborting, and the mutual exclusion against the
// background watcher is preserved.

final class RainController {
    private let lock = NSRecursiveLock()
    private var process: Process?
    private var _mode: String?

    var mode: String? {
        lock.lock(); defer { lock.unlock() }
        return _mode
    }

    func isRunning() -> Bool {
        lock.lock(); defer { lock.unlock() }
        if let p = process, p.isRunning { return true }
        process = nil
        _mode = nil
        return false
    }

    func start(fullscreen: Bool) {
        lock.lock(); defer { lock.unlock() }
        if let p = process, p.isRunning {
            p.terminate()
            p.waitUntilExit()
        }
        process = nil
        _mode = nil

        let bin = bundleBinaryURL()
        guard FileManager.default.isExecutableFile(atPath: bin.path) else {
            NSLog("matrix-bg-bin not found at \(bin.path)")
            return
        }
        let p = Process()
        p.executableURL = bin
        p.arguments = fullscreen ? ["--fullscreen"] : []
        p.standardOutput = Pipe()
        p.standardError = Pipe()
        do {
            try p.run()
            process = p
            _mode = fullscreen ? "fullscreen" : "wallpaper"
        } catch {
            NSLog("Failed to launch matrix-bg-bin: \(error)")
        }
    }

    func stop() {
        lock.lock(); defer { lock.unlock() }
        if let p = process, p.isRunning {
            p.terminate()
            p.waitUntilExit()
        }
        process = nil
        _mode = nil
    }
}

// MARK: - Keep Awake Controller
// When enabled, periodically tells macOS the user is active so the display,
// screen saver, and lock-screen idle timers never elapse. Uses
// IOPMAssertionDeclareUserActivity, the same signal the system receives from a
// real mouse or keyboard event, so it needs no Accessibility permission and
// never moves the cursor. A virtual mouse jiggle.
//
// Main-thread only. start()/stop() are driven from menu actions and the app
// delegate (both main thread), and the timer runs on the main run loop. No
// background access touches this class, so unlike RainController it needs no
// internal locking.

final class KeepAwakeController {
    // Fire well under the shortest practical screen-lock timeout (1 minute).
    private let interval: TimeInterval = 30
    private var timer: Timer?
    private var assertionID: IOPMAssertionID = 0

    var isActive: Bool { timer != nil }

    func start() {
        guard timer == nil else { return }
        declareActivity()
        // Add to .common modes so the timer keeps firing while a menu or other
        // modal tracking loop is open. Timer.scheduledTimer runs in .default
        // mode only and would stall there.
        let t = Timer(timeInterval: interval, repeats: true) { [weak self] _ in
            self?.declareActivity()
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        // The UserIsActive assertion is held until released, so dropping the
        // timer is not enough: release it now or the Mac stays awake after
        // the toggle is switched off. Reset the id so a later start() opens a
        // fresh assertion.
        if assertionID != 0 {
            IOPMAssertionRelease(assertionID)
            assertionID = 0
        }
    }

    private func declareActivity() {
        let result = IOPMAssertionDeclareUserActivity(
            "matrix-bg Keep Awake" as CFString,
            kIOPMUserActiveLocal,
            &assertionID)
        if result != kIOReturnSuccess {
            NSLog("matrix-bg: Keep Awake user-activity declaration failed (\(result))")
        }
    }
}

// MARK: - App Delegate

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    var statusItem: NSStatusItem!
    let rain = RainController()
    let keepAwake = KeepAwakeController()
    var idleTimer: Timer?
    // Prevents idleTick from spawning a new background watcher on every fire
    var watchingForActivity = false

    func applicationDidFinishLaunching(_ note: Notification) {
        NSApp.setActivationPolicy(.accessory)
        unloadLegacyWatcher()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            let img = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "matrix-bg")
            img?.isTemplate = true
            button.image = img
            if button.image == nil { button.title = "▦" }
        }

        let menu = buildMenu()
        menu.delegate = self
        statusItem.menu = menu
        startIdleTimer()

        if getBool(kKeepAwake, false) { keepAwake.start() }
    }

    func menuWillOpen(_ menu: NSMenu) {
        let fresh = buildMenu()
        fresh.delegate = self
        statusItem.menu = fresh
    }

    func applicationWillTerminate(_ note: Notification) {
        idleTimer?.invalidate()
        idleTimer = nil
        rain.stop()
        keepAwake.stop()
    }

    // MARK: Menu

    func buildMenu() -> NSMenu {
        let menu = NSMenu()

        let running = rain.isRunning()
        let runWallpaper = NSMenuItem(title: running && rain.mode == "wallpaper" ? "Stop Wallpaper" : "Run as Wallpaper",
                                      action: #selector(toggleWallpaper), keyEquivalent: "")
        runWallpaper.target = self
        menu.addItem(runWallpaper)

        let runFullscreen = NSMenuItem(title: "Run Fullscreen Now",
                                       action: #selector(runFullscreenNow), keyEquivalent: "")
        runFullscreen.target = self
        menu.addItem(runFullscreen)

        if running {
            let stop = NSMenuItem(title: "Stop", action: #selector(stopRain), keyEquivalent: "")
            stop.target = self
            menu.addItem(stop)
        }

        menu.addItem(.separator())

        let enabled = getBool(kEnabled, true)
        let toggle = NSMenuItem(title: "Idle Screensaver",
                                action: #selector(toggleEnabled), keyEquivalent: "")
        toggle.target = self
        toggle.state = enabled ? .on : .off
        menu.addItem(toggle)

        let timeoutItem = NSMenuItem(title: "Idle Timeout: \(getInt(kIdleSeconds, 60))s", action: nil, keyEquivalent: "")
        let timeoutSub = NSMenu()
        let cur = getInt(kIdleSeconds, 60)
        for s in [15, 30, 60, 120, 300, 600] {
            let mi = NSMenuItem(title: "\(s)s", action: #selector(setTimeout(_:)), keyEquivalent: "")
            mi.target = self
            mi.tag = s
            mi.state = (s == cur) ? .on : .off
            timeoutSub.addItem(mi)
        }
        timeoutItem.submenu = timeoutSub
        menu.addItem(timeoutItem)

        let skip = NSMenuItem(title: "Pause During Video Playback",
                              action: #selector(toggleSkipMedia), keyEquivalent: "")
        skip.target = self
        skip.state = getBool(kSkipMedia, true) ? .on : .off
        menu.addItem(skip)

        menu.addItem(.separator())

        let keepAwakeItem = NSMenuItem(title: "Keep Awake",
                                       action: #selector(toggleKeepAwake), keyEquivalent: "")
        keepAwakeItem.target = self
        keepAwakeItem.state = getBool(kKeepAwake, false) ? .on : .off
        keepAwakeItem.toolTip = "Stops the display from sleeping or locking while enabled."
        menu.addItem(keepAwakeItem)

        menu.addItem(.separator())

        let launch = NSMenuItem(title: "Launch at Login",
                                action: #selector(toggleLaunchAtLogin), keyEquivalent: "")
        launch.target = self
        launch.state = launchAtLoginEnabled() ? .on : .off
        menu.addItem(launch)

        menu.addItem(.separator())

        let about = NSMenuItem(title: "About matrix-bg", action: #selector(showAbout), keyEquivalent: "")
        about.target = self
        menu.addItem(about)

        let quit = NSMenuItem(title: "Quit", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        return menu
    }

    // MARK: Menu actions

    @objc func toggleWallpaper() {
        if rain.isRunning() && rain.mode == "wallpaper" {
            rain.stop()
        } else {
            rain.start(fullscreen: false)
        }
        statusItem.menu = buildMenu()
    }

    @objc func runFullscreenNow() {
        rain.start(fullscreen: true)
        statusItem.menu = buildMenu()
    }

    @objc func stopRain() {
        rain.stop()
        statusItem.menu = buildMenu()
    }

    @objc func toggleEnabled() {
        defaults().set(!getBool(kEnabled, true), forKey: kEnabled)
        statusItem.menu = buildMenu()
    }

    @objc func setTimeout(_ sender: NSMenuItem) {
        defaults().set(sender.tag, forKey: kIdleSeconds)
        statusItem.menu = buildMenu()
    }

    @objc func toggleSkipMedia() {
        defaults().set(!getBool(kSkipMedia, true), forKey: kSkipMedia)
        statusItem.menu = buildMenu()
    }

    @objc func toggleKeepAwake() {
        let enabled = !getBool(kKeepAwake, false)
        defaults().set(enabled, forKey: kKeepAwake)
        if enabled { keepAwake.start() } else { keepAwake.stop() }
        statusItem.menu = buildMenu()
    }

    @objc func toggleLaunchAtLogin() {
        let cur = launchAtLoginEnabled()
        setLaunchAtLogin(!cur)
        statusItem.menu = buildMenu()
    }

    @objc func showAbout() {
        let alert = NSAlert()
        alert.messageText = "matrix-bg"
        alert.informativeText = """
            Matrix rain desktop overlay for macOS.

            • Idle screensaver triggers after \(getInt(kIdleSeconds, 60))s of inactivity
            • Click "Run Fullscreen Now" to preview
            • Move mouse or press a key to dismiss fullscreen rain
            • "Keep Awake" stops the display from sleeping or locking

            github.com/statusdigitalmarketing/matrix-bg
            """
        alert.alertStyle = .informational
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }

    @objc func quit() {
        NSApp.terminate(nil)
    }

    // MARK: Idle detection loop

    func startIdleTimer() {
        idleTimer = Timer.scheduledTimer(withTimeInterval: 5.0, repeats: true) { [weak self] _ in
            self?.idleTick()
        }
    }

    func idleTick() {
        guard getBool(kEnabled, true) else { return }
        if getBool(kSkipMedia, true) && mediaIsPlaying() { return }

        let threshold = getInt(kIdleSeconds, 60)
        let idle = systemIdleSeconds()

        if rain.isRunning() && rain.mode == "fullscreen" { return }

        if idle >= threshold && !watchingForActivity {
            watchingForActivity = true
            rain.start(fullscreen: true)
            // Background watcher: dismiss our rain when the user wakes the system.
            // The flag prevents stacking watchers on subsequent idle ticks.
            DispatchQueue.global(qos: .background).async { [weak self] in
                while self?.rain.isRunning() == true {
                    Thread.sleep(forTimeInterval: 0.5)
                    if systemIdleSeconds() < 3 {
                        DispatchQueue.main.async {
                            self?.rain.stop()
                            self?.watchingForActivity = false
                        }
                        return
                    }
                }
                DispatchQueue.main.async { self?.watchingForActivity = false }
            }
        }
    }

    // MARK: Launch at Login (SMAppService, macOS 13+)

    func launchAtLoginEnabled() -> Bool {
        SMAppService.mainApp.status == .enabled
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            NSLog("SMAppService error: \(error)")
            let alert = NSAlert()
            alert.messageText = "Couldn't update Login Items"
            alert.informativeText = "\(error.localizedDescription)\n\nYou can manage this manually in System Settings → General → Login Items."
            alert.runModal()
        }
    }
}

// MARK: - Main

let args = CommandLine.arguments
if args.contains("--register-login") {
    do { try SMAppService.mainApp.register(); print("registered") }
    catch { print("error: \(error)"); exit(1) }
    exit(0)
}
if args.contains("--unregister-login") {
    do { try SMAppService.mainApp.unregister(); print("unregistered") }
    catch { print("error: \(error)"); exit(1) }
    exit(0)
}
if args.contains("--login-status") {
    switch SMAppService.mainApp.status {
    case .enabled: print("enabled")
    case .requiresApproval: print("requiresApproval")
    case .notRegistered: print("notRegistered")
    case .notFound: print("notFound")
    @unknown default: print("unknown")
    }
    exit(0)
}

let delegate = AppDelegate()
NSApplication.shared.delegate = delegate
NSApplication.shared.run()
