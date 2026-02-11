import AppKit
import CoreText

// MARK: - Matrix Rain View
// Uses CoreText directly with pre-created CTLine objects.
// kCTForegroundColorFromContextAttributeName makes each CTLineDraw
// use whatever fill color is currently set on the CGContext,
// so we create the lines once and just change the color before each draw.
final class MatrixView: NSView {
    private var timer: Timer?
    private let cellW: CGFloat = 14
    private let cellH: CGFloat = 20
    private var numCols = 0
    private var numRows = 0

    // One pre-created CTLine per character in the charset
    private var ctLines: [CTLine] = []

    // Flat grid arrays — index = col * numRows + row
    private var brightness: [Float] = []   // 0 = invisible, 1 = head
    private var charIdx: [Int] = []        // which character to draw

    // Rain streams
    private var drops: [Drop] = []
    struct Drop {
        var col: Int
        var y: Float
        var speed: Float
    }

    // ASCII printable + half-width katakana
    static let charset: [String] = {
        var c: [String] = []
        for v in 33...126 { c.append(String(UnicodeScalar(v)!)) }
        for v in 0xFF66...0xFF9D { c.append(String(UnicodeScalar(v)!)) }
        return c
    }()

    override var isOpaque: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    func start() {
        numCols = Int(bounds.width / cellW)
        numRows = Int(bounds.height / cellH)
        let total = numCols * numRows

        // Build CTLine cache
        let font = CTFontCreateWithName("Menlo" as CFString, cellH * 0.72, nil)
        for ch in Self.charset {
            let s = CFAttributedStringCreateMutable(nil, 0)!
            CFAttributedStringReplaceString(s, CFRangeMake(0, 0), ch as CFString)
            let r = CFRangeMake(0, CFAttributedStringGetLength(s))
            CFAttributedStringSetAttribute(s, r, kCTFontAttributeName, font)
            CFAttributedStringSetAttribute(s, r, kCTForegroundColorFromContextAttributeName, kCFBooleanTrue)
            ctLines.append(CTLineCreateWithAttributedString(s))
        }

        // Init grid
        brightness = [Float](repeating: 0, count: total)
        charIdx = (0..<total).map { _ in Int.random(in: 0..<Self.charset.count) }

        // 2-3 drops per column, staggered start positions
        for col in 0..<numCols {
            for _ in 0..<Int.random(in: 2...3) {
                drops.append(Drop(
                    col: col,
                    y: Float.random(in: Float(-numRows * 2)...Float(numRows)),
                    speed: Float.random(in: 0.25...1.15)
                ))
            }
        }

        wantsLayer = true
        layer?.drawsAsynchronously = true
        layer?.isOpaque = true

        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    private func tick() {
        let nr = numRows
        let total = numCols * nr
        let cc = Self.charset.count

        // Fade visible cells + randomly morph their characters
        for i in 0..<total where brightness[i] > 0 {
            brightness[i] = max(0, brightness[i] - 0.02)
            // ~17% chance to swap to a different character each frame
            if Int.random(in: 0..<6) == 0 {
                charIdx[i] = Int.random(in: 0..<cc)
            }
        }

        // Advance each rain drop
        for i in drops.indices {
            drops[i].y += drops[i].speed
            let row = Int(drops[i].y)
            let col = drops[i].col

            // Light up head
            if row >= 0 && row < nr {
                let idx = col * nr + row
                brightness[idx] = 1.0
                charIdx[idx] = Int.random(in: 0..<cc)
            }
            // Brighten cell just behind head
            if row - 1 >= 0 && row - 1 < nr {
                let idx = col * nr + (row - 1)
                brightness[idx] = max(brightness[idx], 0.87)
            }

            // Reset once far enough off-screen
            if row > nr + 25 {
                drops[i].y = Float.random(in: Float(-nr)...(-1))
                drops[i].speed = Float.random(in: 0.25...1.15)
            }
        }

        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }

        // Black background
        ctx.setFillColor(red: 0, green: 0, blue: 0, alpha: 1)
        ctx.fill(bounds)

        let cw = cellW
        let ch = cellH
        let nr = numRows
        let bh = bounds.height
        let allLines = ctLines

        for col in 0..<numCols {
            let x = CGFloat(col) * cw + 1
            let base = col * nr

            for row in 0..<nr {
                let b = brightness[base + row]
                guard b > 0.02 else { continue }

                // Color: white head → bright green → fading green → invisible
                if b > 0.93 {
                    ctx.setFillColor(red: 0.85, green: 1.0, blue: 0.9, alpha: 1.0)
                } else if b > 0.78 {
                    ctx.setFillColor(red: 0.1, green: 1.0, blue: 0.2, alpha: 1.0)
                } else if b > 0.4 {
                    let g = CGFloat(0.25 + b * 0.75)
                    ctx.setFillColor(red: 0, green: g, blue: 0, alpha: 1.0)
                } else {
                    let g = CGFloat(b * 0.8)
                    ctx.setFillColor(red: 0, green: g, blue: 0, alpha: CGFloat(max(0.3, b * 2.0)))
                }

                let ci = charIdx[base + row]
                ctx.textPosition = CGPoint(x: x, y: bh - CGFloat(row + 1) * ch + ch * 0.22)
                CTLineDraw(allLines[ci], ctx)
            }
        }
    }
}

// MARK: - Wallpaper Save / Restore
let wallpaperBackupPath = "/tmp/.matrix-bg-wallpaper-backup"

func saveCurrentWallpaper() {
    guard let mainScreen = NSScreen.main,
          let url = NSWorkspace.shared.desktopImageURL(for: mainScreen) else { return }
    try? url.path.write(toFile: wallpaperBackupPath, atomically: true, encoding: .utf8)
}

func restoreWallpaper() {
    guard FileManager.default.fileExists(atPath: wallpaperBackupPath),
          let path = try? String(contentsOfFile: wallpaperBackupPath, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines),
          !path.isEmpty else { return }
    let url = URL(fileURLWithPath: path)
    for screen in NSScreen.screens {
        try? NSWorkspace.shared.setDesktopImageURL(url, for: screen, options: [:])
    }
    try? FileManager.default.removeItem(atPath: wallpaperBackupPath)
}

// MARK: - App Setup
let fullscreen = CommandLine.arguments.contains("--fullscreen")

let app = NSApplication.shared
app.setActivationPolicy(.accessory)

// Save wallpaper before we put black windows over the desktop
saveCurrentWallpaper()
atexit { restoreWallpaper() }

let windowLevel: NSWindow.Level = fullscreen
    ? .screenSaver
    : NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopWindow)) + 1)

var windows: [NSWindow] = []
for screen in NSScreen.screens {
    let w = NSWindow(
        contentRect: screen.frame,
        styleMask: [.borderless],
        backing: .buffered,
        defer: false,
        screen: screen
    )
    w.level = windowLevel
    w.backgroundColor = .black
    w.isOpaque = true
    w.hasShadow = false
    w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

    let view = MatrixView(frame: screen.frame)
    w.contentView = view
    // orderFront without makeKey — shows the window without stealing focus
    w.orderFront(nil)
    view.start()
    windows.append(w)
}

// Clean shutdown: restore wallpaper, hide windows, then exit
func shutdown() {
    restoreWallpaper()
    for w in windows { w.orderOut(nil) }
    DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
        NSApp.terminate(nil)
    }
}

// In fullscreen mode, dismiss on mouse movement or keypress.
// Uses ONLY the global event monitor — never steals focus from the active app.
if fullscreen {
    var origin = NSEvent.mouseLocation
    var armed = false

    // Wait 0.5s before arming — avoids instant dismiss from residual input
    DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
        origin = NSEvent.mouseLocation
        armed = true
    }

    // Global monitor — passively watches events going to other apps without
    // intercepting them. The active app keeps focus and receives all input normally.
    NSEvent.addGlobalMonitorForEvents(matching: [.mouseMoved, .keyDown, .leftMouseDown, .rightMouseDown, .scrollWheel]) { event in
        guard armed else { return }
        if event.type == .mouseMoved {
            let cur = NSEvent.mouseLocation
            let dx = cur.x - origin.x
            let dy = cur.y - origin.y
            if dx * dx + dy * dy > 25 { shutdown() }
        } else {
            shutdown()
        }
    }
}

var signalSources: [DispatchSourceSignal] = []
for sig: Int32 in [SIGTERM, SIGINT] {
    signal(sig, SIG_IGN)
    let src = DispatchSource.makeSignalSource(signal: sig, queue: .main)
    src.setEventHandler { shutdown() }
    src.resume()
    signalSources.append(src)
}

// Auto-kill after 60s — prevents glitchy runaway processes
DispatchQueue.main.asyncAfter(deadline: .now() + 60) {
    shutdown()
}

app.run()
