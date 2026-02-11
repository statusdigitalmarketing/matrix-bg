# matrix-bg

Native macOS Matrix rain desktop overlay. Single-file Swift, zero dependencies beyond system frameworks.

## Features

- **Wallpaper mode** (default) — rain sits behind all windows as a living desktop
- **Fullscreen mode** (`--fullscreen`) — rain covers everything, dismisses on any input
- ASCII + half-width katakana characters that morph while visible
- Color gradient: white head -> bright green -> fading green
- Multi-display support
- Wallpaper auto-save/restore — your desktop background always comes back
- 60-second auto-kill safety net
- Optional idle screensaver via launch agent
- 30fps, hardware-accelerated CoreText rendering

## Install

### Quick Install

```bash
curl -fsSL https://raw.githubusercontent.com/statusdigitalmarketing/matrix-bg/main/install.sh | bash
```

### From Source

```bash
git clone https://github.com/statusdigitalmarketing/matrix-bg.git
cd matrix-bg
./install.sh
```

Or if you just want the binary:

```bash
make install
```

### Requirements

- macOS 13+ (Apple Silicon or Intel)
- Xcode Command Line Tools (`xcode-select --install`)

## Usage

```bash
# Desktop wallpaper overlay (behind all windows)
matrix-bg

# Fullscreen screensaver (covers everything, any input dismisses)
matrix-bg --fullscreen

# Ctrl+C to quit, or it auto-exits after 60 seconds
```

### Idle Screensaver

If you enabled the idle watcher during install, matrix-bg starts automatically after 60 seconds of system inactivity. Manage it with:

```bash
matrix-bg screensaver status    # Check if running
matrix-bg screensaver on        # Enable
matrix-bg screensaver off       # Disable
```

### Shell Integration

Add to `~/.zshrc` to show matrix rain during git operations:

```zsh
git() {
  case "$1" in
    push|pull|clone|fetch)
      matrix-bg &
      local mpid=$!
      command git "$@"; local rc=$?
      kill $mpid 2>/dev/null; wait $mpid 2>/dev/null
      return $rc ;;
    *) command git "$@" ;;
  esac
}
```

**Note for VS Code / Cursor users:** If you use `TMOUT` idle triggers in `.zshrc`, gate them so they don't fire inside IDE terminals (the fullscreen overlay can crash the terminal host):

```zsh
if [[ -z "$VSCODE_PID" && "$TERM_PROGRAM" != "vscode" ]]; then
  export TMOUT=60
  TRAPALRM() { ... }
fi
```

## How It Works

- Creates borderless `NSWindow`s on each screen at desktop level (wallpaper) or screensaver level (fullscreen)
- Renders with CoreText `CTLine` objects and `kCTForegroundColorFromContextAttributeName` — glyphs are created once, color changes per-draw via `CGContext`
- Rain drops advance down columns at randomized speeds with brightness decay trails
- On launch, saves current wallpaper path to `/tmp/.matrix-bg-wallpaper-backup`
- On exit (clean, signal, crash via `atexit`), restores the original wallpaper
- Fullscreen mode uses a passive global event monitor — never steals focus

## Tuning

Edit `matrix-bg.swift` and recompile (`make install`):

| Parameter | Default | Effect |
|-----------|---------|--------|
| `cellW` / `cellH` | 14 / 20 | Character grid size (points) |
| Frame interval | `1.0 / 30.0` | Frame rate |
| Fade rate | `0.02` | Per-frame brightness decay (lower = longer trails) |
| Drop speed | `0.25...1.15` | Range of fall speeds |
| Drops/column | `2...3` | Rain density |

## Uninstall

```bash
./uninstall.sh
```

## License

MIT
