import AppKit
import CoreText

// MARK: - Color options
// --color RRGGBB  --color2 RRGGBB  --blend 0..5  --rainbow
// Blend modes match the Windows tray app: 0 off, 1 fade over time,
// 2 side by side left->right, 3 top->bottom, 4 mixed per streak, 5 mixed + drifting.
struct RGB { var r: CGFloat; var g: CGFloat; var b: CGFloat }

struct ColorOptions {
    var base = RGB(r: 0.1, g: 1.0, b: 0.2)     // classic matrix green
    var second = RGB(r: 0.0, g: 0.9, b: 1.0)   // cyan
    var blend = 0
    var rainbow = false
    var customBase = false                      // --color was given
    // The classic path preserves the original hardcoded ramp byte for byte
    // (it is also the contract the Windows C port's sim-parity test pins).
    var isClassic: Bool { !rainbow && blend == 0 && !customBase }
}

func parseHexColor(_ s: String) -> RGB? {
    let h = s.hasPrefix("#") ? String(s.dropFirst()) : s
    guard h.count == 6, let v = UInt32(h, radix: 16) else { return nil }
    return RGB(r: CGFloat((v >> 16) & 0xFF) / 255,
               g: CGFloat((v >> 8) & 0xFF) / 255,
               b: CGFloat(v & 0xFF) / 255)
}

func rgbFromHSV(hue: CGFloat) -> RGB {
    let h = (hue.truncatingRemainder(dividingBy: 360) + 360).truncatingRemainder(dividingBy: 360)
    let x = 1 - abs((h / 60).truncatingRemainder(dividingBy: 2) - 1)
    switch h {
    case ..<60:   return RGB(r: 1, g: x, b: 0)
    case ..<120:  return RGB(r: x, g: 1, b: 0)
    case ..<180:  return RGB(r: 0, g: 1, b: x)
    case ..<240:  return RGB(r: 0, g: x, b: 1)
    case ..<300:  return RGB(r: x, g: 0, b: 1)
    default:      return RGB(r: 1, g: 0, b: x)
    }
}

let colorOpts: ColorOptions = {
    var o = ColorOptions()
    let args = CommandLine.arguments
    var i = 1
    while i < args.count {
        switch args[i] {
        case "--color" where i + 1 < args.count:
            if let c = parseHexColor(args[i + 1]) { o.base = c; o.customBase = true }
            i += 1
        case "--color2" where i + 1 < args.count:
            if let c = parseHexColor(args[i + 1]) { o.second = c }
            i += 1
        case "--blend" where i + 1 < args.count:
            o.blend = min(5, max(0, Int(args[i + 1]) ?? 0))
            i += 1
        case "--rainbow":
            o.rainbow = true
        default:
            break
        }
        i += 1
    }
    return o
}()

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
        var mix: Float   // this streak's position between colour A and B (mixed blend modes)
    }

    // Multicolor state (matches the Windows tray app's blend semantics)
    private var cellMix: [Float] = []   // per-cell streak mix, written when a drop head lights the cell
    private var hue: CGFloat = 120      // rainbow cycle
    private var blendPhase: Double = 0  // time-based blend modes

    // ASCII printable + half-width katakana
    static let charset: [String] = {
        var c: [String] = []
        for v in 33...126 { c.append(String(UnicodeScalar(v)!)) }
        for v in 0xFF66...0xFF9D { c.append(String(UnicodeScalar(v)!)) }
        return c
    }()

    override var isOpaque: Bool { false }
    override var acceptsFirstResponder: Bool { false }

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
        cellMix = [Float](repeating: 0, count: total)

        // 2-3 drops per column, staggered start positions
        for col in 0..<numCols {
            for _ in 0..<Int.random(in: 2...3) {
                drops.append(Drop(
                    col: col,
                    y: Float.random(in: Float(-numRows * 2)...Float(numRows)),
                    speed: Float.random(in: 0.25...1.15),
                    mix: Float.random(in: 0...1)
                ))
            }
        }

        wantsLayer = true
        layer?.drawsAsynchronously = true
        layer?.isOpaque = true
        layer?.backgroundColor = CGColor(red: 0, green: 0, blue: 0, alpha: 1)

        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 20.0, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    private func tick() {
        let nr = numRows
        let total = numCols * nr
        let cc = Self.charset.count

        // Multicolor phases (no-ops visually in classic mode)
        if colorOpts.rainbow { hue += 0.6 }          // ~12 deg/s at 20fps, matches the Windows app
        blendPhase += 0.006

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
                cellMix[idx] = drops[i].mix
            }
            // Brighten cell just behind head
            if row - 1 >= 0 && row - 1 < nr {
                let idx = col * nr + (row - 1)
                if brightness[idx] < 0.87 {
                    brightness[idx] = 0.87
                    cellMix[idx] = drops[i].mix
                }
            }

            // Reset once far enough off-screen
            if row > nr + 25 {
                drops[i].y = Float.random(in: Float(-nr)...(-1))
                drops[i].speed = Float.random(in: 0.25...1.15)
                drops[i].mix = Float.random(in: 0...1)
            }
        }

        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }

        // Clear to transparent — the layer's black backgroundColor shows through.
        // Faster than a manual fill because Core Animation composites on the GPU.
        ctx.clear(bounds)

        let cw = cellW
        let ch = cellH
        let nr = numRows
        let bh = bounds.height
        let allLines = ctLines

        let classic = colorOpts.isClassic

        for col in 0..<numCols {
            let x = CGFloat(col) * cw + 1
            let base = col * nr

            for row in 0..<nr {
                let b = brightness[base + row]
                guard b > 0.02 else { continue }

                if classic {
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
                } else {
                    // Same intensity ramp applied to the streak's colour
                    let c = streakColor(col: col, row: row, cellIndex: base + row)
                    if b > 0.93 {
                        // Whitened head, same idea as the classic (0.85, 1.0, 0.9)
                        ctx.setFillColor(red: c.r + (1 - c.r) * 0.8, green: c.g + (1 - c.g) * 0.8, blue: c.b + (1 - c.b) * 0.8, alpha: 1.0)
                    } else if b > 0.78 {
                        ctx.setFillColor(red: c.r, green: c.g, blue: c.b, alpha: 1.0)
                    } else if b > 0.4 {
                        let m = CGFloat(0.25 + b * 0.75)
                        ctx.setFillColor(red: c.r * m, green: c.g * m, blue: c.b * m, alpha: 1.0)
                    } else {
                        let m = CGFloat(b * 0.8)
                        ctx.setFillColor(red: c.r * m, green: c.g * m, blue: c.b * m, alpha: CGFloat(max(0.3, b * 2.0)))
                    }
                }

                let ci = charIdx[base + row]
                ctx.textPosition = CGPoint(x: x, y: bh - CGFloat(row + 1) * ch + ch * 0.22)
                CTLineDraw(allLines[ci], ctx)
            }
        }
    }

    // Streak colour for the non-classic paths: rainbow, or a blend between colour A and B.
    private func streakColor(col: Int, row: Int, cellIndex: Int) -> RGB {
        if colorOpts.rainbow { return rgbFromHSV(hue: hue) }
        guard colorOpts.blend != 0 else { return colorOpts.base }
        let t: CGFloat
        switch colorOpts.blend {
        case 1:  // fade between the two over time
            t = CGFloat(sin(blendPhase) * 0.5 + 0.5)
        case 2:  // side by side, left -> right
            t = numCols > 1 ? CGFloat(col) / CGFloat(numCols - 1) : 0
        case 3:  // top -> bottom
            t = numRows > 1 ? CGFloat(row) / CGFloat(numRows - 1) : 0
        case 4:  // mixed: each streak its own colour
            t = CGFloat(cellMix[cellIndex])
        default: // mixed + fading: each streak drifts between the two, out of phase
            let ph = (Double(cellMix[cellIndex]) + blendPhase / (2 * .pi)).truncatingRemainder(dividingBy: 1)
            t = CGFloat(ph < 0.5 ? ph * 2 : 2 - ph * 2)
        }
        let a = colorOpts.base, s = colorOpts.second
        return RGB(r: a.r + (s.r - a.r) * t, g: a.g + (s.g - a.g) * t, b: a.b + (s.b - a.b) * t)
    }
}

// MARK: - Non-activating window (never steals focus)
final class NonKeyWindow: NSWindow {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

// MARK: - Wallpaper Save / Restore
let wallpaperBackupPath = "/tmp/.matrix-bg-wallpaper-backup"

func saveCurrentWallpaper() {
    // Don't overwrite an existing backup — another matrix-bg instance may already be running
    // and the current desktop could already be our black overlay.
    guard !FileManager.default.fileExists(atPath: wallpaperBackupPath) else { return }
    guard let mainScreen = NSScreen.main,
          let url = NSWorkspace.shared.desktopImageURL(for: mainScreen) else { return }
    try? url.path.write(toFile: wallpaperBackupPath, atomically: true, encoding: .utf8)
}

func restoreWallpaper() {
    guard FileManager.default.fileExists(atPath: wallpaperBackupPath),
          let path = try? String(contentsOfFile: wallpaperBackupPath, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines),
          FileManager.default.fileExists(atPath: path) else { return }
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
    let w = NonKeyWindow(
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
    w.ignoresMouseEvents = true
    w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]

    let view = MatrixView(frame: screen.frame)
    w.contentView = view
    // orderFront without makeKey — shows the window without stealing focus
    w.orderFront(nil)
    view.start()
    windows.append(w)
}

// Clean shutdown: restore wallpaper, hide windows, then exit.
// Guard prevents double-call from concurrent signal + timer + event monitor.
var isShuttingDown = false
func shutdown() {
    guard !isShuttingDown else { return }
    isShuttingDown = true
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
