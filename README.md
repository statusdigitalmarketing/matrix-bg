# matrix-bg

Matrix rain desktop overlay for macOS. Native Swift, ~86KB binary, zero dependencies.

![matrix-bg](https://raw.githubusercontent.com/statusdigitalmarketing/matrix-bg/main/screenshot.png)

## Features

- Full-screen Matrix rain with katakana + ASCII characters
- Desktop wallpaper mode (behind windows) or fullscreen screensaver mode
- Characters morph while visible — the "living code" look
- Color gradient: white head → bright green → fading green
- Multi-display support
- Optional idle screensaver (activates after 60s of inactivity)
- 30fps, hardware-accelerated CoreText rendering

## Install

### Quick install (curl)

```bash
curl -fsSL https://raw.githubusercontent.com/statusdigitalmarketing/matrix-bg/main/install.sh | bash
```

### Homebrew

```bash
brew tap statusdigitalmarketing/tap
brew install matrix-bg
```

### npm

```bash
npx matrix-bg-screensaver
```

### Manual

```bash
git clone https://github.com/statusdigitalmarketing/matrix-bg.git
cd matrix-bg
./install.sh
```

## Usage

```bash
# Desktop wallpaper overlay (behind all windows)
matrix-bg

# Fullscreen screensaver (covers everything)
matrix-bg --fullscreen

# Ctrl+C to quit
```

### Idle Screensaver

The installer can set up an idle watcher that automatically starts the screensaver after 60 seconds of inactivity. Manage it with:

```bash
matrix-bg screensaver status    # Check if running
matrix-bg screensaver on        # Enable
matrix-bg screensaver off       # Disable
```

### Shell Integration (optional)

Add to `~/.zshrc` to show matrix rain during git operations:

```bash
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

## Requirements

- macOS (Apple Silicon or Intel)
- Xcode Command Line Tools (`xcode-select --install`)

## Tuning

Edit `matrix-bg.swift` and recompile:

| Variable | Default | Effect |
|----------|---------|--------|
| `cellW` / `cellH` | 14 / 20 | Character grid density |
| `1.0 / 30.0` | 30fps | Frame rate |
| `0.02` | — | Fade rate (lower = longer trails) |
| `0.25...1.15` | — | Drop speed range |
| `2...3` | — | Drops per column |

Recompile after changes:

```bash
swiftc -O -o ~/.local/bin/matrix-bg matrix-bg.swift -framework AppKit -framework CoreText
```

## Uninstall

```bash
# If installed from source or curl
./uninstall.sh

# If installed via Homebrew
brew uninstall matrix-bg
```

## License

MIT
