# matrix-bg — Native macOS Matrix Rain Desktop Overlay

## Tech Stack
- **Language**: Swift (primary), Python (legacy/alternative)
- **Frameworks**: AppKit, CoreText (hardware-accelerated rendering)
- **Build**: Makefile + swiftc compiler
- **Platform**: macOS 13+ (Apple Silicon and Intel)
- **Package**: npm (install-only, no runtime deps)

## Project Structure
```
matrix-bg/
├── matrix-bg.swift          # Main Swift source — single-file app
├── matrix-bg.py             # Python alternative (PyObjC)
├── Makefile                 # Build: swiftc -O with AppKit + CoreText
├── install.sh               # Compile + install + optional idle screensaver
├── uninstall.sh             # Remove binary, watcher, launchd agent
├── matrix-idle-watcher.sh   # Idle detection via IOHIDSystem
├── package.json             # npm wrapper (postinstall triggers install.sh)
├── LICENSE                  # MIT
└── README.md
```

## Build/Dev Commands
```bash
# Build binary
make build

# Build and install to ~/.local/bin
make install

# Full install with optional screensaver setup
./install.sh

# Clean build artifacts
make clean

# Uninstall everything
./uninstall.sh

# Run directly
matrix-bg              # Desktop wallpaper overlay (behind windows)
matrix-bg --fullscreen # Fullscreen screensaver (covers everything)

# Manage idle screensaver
matrix-bg screensaver status
matrix-bg screensaver on
matrix-bg screensaver off
```

## Key Conventions
- **Single-file architecture**: Entire app lives in `matrix-bg.swift` -- no dependencies beyond system frameworks
- **CoreText rendering**: Pre-creates CTLine objects once, changes color per-draw via CGContext fill color (`kCTForegroundColorFromContextAttributeName`)
- **Non-activating windows**: Uses `NonKeyWindow` (canBecomeKey = false) and `orderFront` to never steal focus from active app
- **Wallpaper save/restore**: Saves current wallpaper to `/tmp/.matrix-bg-wallpaper-backup` on launch, restores via `atexit` on exit (clean, signal, or crash)
- **60-second auto-kill**: Safety net prevents runaway processes via `DispatchQueue.main.asyncAfter`
- **Fullscreen dismiss**: Uses passive global event monitor (`addGlobalMonitorForEvents`) -- never intercepts input from other apps
- **Idle screensaver**: launchd agent (`com.matrix-bg.idle-watcher`) polls `IOHIDSystem` HIDIdleTime every 5 seconds, triggers fullscreen after 60s idle
- **Install location**: `~/.local/bin/matrix-bg` (wrapper script) and `~/.local/bin/matrix-bg-bin` (compiled binary)
- **Character set**: ASCII printable (33-126) + half-width katakana (0xFF66-0xFF9D) with per-frame random morphing
- **Tuning**: Edit constants in `matrix-bg.swift` (cellW/cellH, frame rate, fade rate, drop speed) and recompile
