import AppKit
import ServiceManagement
import Foundation

// MARK: - Config (UserDefaults)
let kEnabled        = "screensaverEnabled"
let kIdleSeconds    = "idleSeconds"
let kSkipMedia      = "skipMedia"
let kLaunchAtLogin  = "launchAtLogin"

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
    // ioreg -c IOHIDSystem -> HIDIdleTime in nanoseconds
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
    // Check active assertions used by video/audio playback
    for line in s.split(separator: "\n") {
        if line.contains("PreventUserIdleDisplaySleep") && line.contains("1") { return true }
        if line.contains("NoIdleSleepAssertion") && line.contains("Playing") { return true }
    }
    return false
}

// MARK: - Rain Process Manager

final class RainController {
    private var process: Process?
    private(set) var mode: String? // "wallpaper" | "fullscreen" | nil

    func isRunning() -> Bool {
        if let p = process, p.isRunning { return true }
        process = nil
        mode = nil
        return false
    }

    func start(fullscreen: Bool) {
        stop()
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
            mode = fullscreen ? "fullscreen" : "wallpaper"
        } catch {
            NSLog("Failed to launch matrix-bg-bin: \(error)")
        }
    }

    func stop() {
        if let p = process, p.isRunning {
            p.terminate()
            p.waitUntilExit()
        }
        process = nil
        mode = nil
    }
}

// MARK: - App Delegate

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    var statusItem: NSStatusItem!
    let rain = RainController()
    var idleTimer: Timer?

    func applicationDidFinishLaunching(_ note: Notification) {
        NSApp.setActivationPolicy(.accessory)

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
    }

    // Rebuild on every open so checkmarks/labels reflect current state
    func menuWillOpen(_ menu: NSMenu) {
        let fresh = buildMenu()
        fresh.delegate = self
        statusItem.menu = fresh
    }

    func applicationWillTerminate(_ note: Notification) {
        rain.stop()
    }

    // MARK: Menu

    func buildMenu() -> NSMenu {
        let menu = NSMenu()

        // Run / Stop
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

        // Idle screensaver toggle
        let enabled = getBool(kEnabled, true)
        let toggle = NSMenuItem(title: "Idle Screensaver",
                                action: #selector(toggleEnabled), keyEquivalent: "")
        toggle.target = self
        toggle.state = enabled ? .on : .off
        menu.addItem(toggle)

        // Idle timeout submenu
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

        // Skip during video
        let skip = NSMenuItem(title: "Pause During Video Playback",
                              action: #selector(toggleSkipMedia), keyEquivalent: "")
        skip.target = self
        skip.state = getBool(kSkipMedia, true) ? .on : .off
        menu.addItem(skip)

        menu.addItem(.separator())

        // Launch at login
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

            github.com/<your-repo>/matrix-bg
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

        if rain.isRunning() && rain.mode == "fullscreen" {
            // Already running fullscreen — let the rain process self-dismiss on input
            return
        }

        if idle >= threshold {
            rain.start(fullscreen: true)
            // Watch for activity to stop ours (the rain process also dismisses itself
            // on movement, but we keep this as a safety net)
            DispatchQueue.global(qos: .background).async { [weak self] in
                while self?.rain.isRunning() == true {
                    Thread.sleep(forTimeInterval: 0.5)
                    if systemIdleSeconds() < 3 {
                        DispatchQueue.main.async { self?.rain.stop() }
                        break
                    }
                }
            }
        }
    }

    // MARK: Launch at Login (SMAppService, macOS 13+)

    func launchAtLoginEnabled() -> Bool {
        if #available(macOS 13.0, *) {
            return SMAppService.mainApp.status == .enabled
        }
        return getBool(kLaunchAtLogin, false)
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        if #available(macOS 13.0, *) {
            do {
                if enabled {
                    try SMAppService.mainApp.register()
                } else {
                    try SMAppService.mainApp.unregister()
                }
                defaults().set(enabled, forKey: kLaunchAtLogin)
            } catch {
                NSLog("SMAppService error: \(error)")
                let alert = NSAlert()
                alert.messageText = "Couldn't update Login Items"
                alert.informativeText = "\(error.localizedDescription)\n\nYou can manage this manually in System Settings → General → Login Items."
                alert.runModal()
            }
        }
    }
}

// MARK: - Main

// CLI flags for headless register/unregister of Launch at Login.
// Useful for installers and the .command file.
let args = CommandLine.arguments
if args.contains("--register-login") {
    if #available(macOS 13.0, *) {
        do { try SMAppService.mainApp.register(); print("registered") }
        catch { print("error: \(error)"); exit(1) }
    }
    exit(0)
}
if args.contains("--unregister-login") {
    if #available(macOS 13.0, *) {
        do { try SMAppService.mainApp.unregister(); print("unregistered") }
        catch { print("error: \(error)"); exit(1) }
    }
    exit(0)
}
if args.contains("--login-status") {
    if #available(macOS 13.0, *) {
        switch SMAppService.mainApp.status {
        case .enabled: print("enabled")
        case .requiresApproval: print("requiresApproval")
        case .notRegistered: print("notRegistered")
        case .notFound: print("notFound")
        @unknown default: print("unknown")
        }
    }
    exit(0)
}

let delegate = AppDelegate()
NSApplication.shared.delegate = delegate
NSApplication.shared.run()
