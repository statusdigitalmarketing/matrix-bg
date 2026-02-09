#!/bin/bash
# matrix-bg installer
# Compiles the Swift source and optionally sets up idle screensaver.
set -e

INSTALL_DIR="$HOME/.local/bin"
PLIST_DIR="$HOME/Library/LaunchAgents"
PLIST_NAME="com.matrix-bg.idle-watcher"
PLIST_PATH="$PLIST_DIR/$PLIST_NAME.plist"
WATCHER_PATH="$INSTALL_DIR/matrix-bg-watcher"
BIN_PATH="$INSTALL_DIR/matrix-bg-bin"
WRAPPER_PATH="$INSTALL_DIR/matrix-bg"
SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

echo -e "${GREEN}matrix-bg${NC} — Matrix rain desktop overlay for macOS"
echo ""

# Check macOS
if [[ "$(uname)" != "Darwin" ]]; then
    echo "Error: matrix-bg only works on macOS."
    exit 1
fi

# Check for swiftc
if ! command -v swiftc &>/dev/null; then
    echo "Error: swiftc not found. Install Xcode Command Line Tools:"
    echo "  xcode-select --install"
    exit 1
fi

# Create install directory
mkdir -p "$INSTALL_DIR"

# Compile Swift binary
echo -e "${CYAN}Compiling matrix-bg...${NC}"
swiftc -O -o "$BIN_PATH" "$SOURCE_DIR/matrix-bg.swift" \
    -framework AppKit -framework CoreText

# Create wrapper script with subcommands
cat > "$WRAPPER_PATH" << 'WRAPPER'
#!/bin/bash
# matrix-bg — Matrix rain desktop overlay for macOS
BIN="$HOME/.local/bin/matrix-bg-bin"
PLIST_NAME="com.matrix-bg.idle-watcher"
PLIST_PATH="$HOME/Library/LaunchAgents/$PLIST_NAME.plist"
WATCHER_PATH="$HOME/.local/bin/matrix-bg-watcher"

case "${1:-}" in
    screensaver)
        case "${2:-status}" in
            on)
                if [[ ! -f "$WATCHER_PATH" ]]; then
                    echo "Watcher script not found. Re-run install.sh with screensaver enabled."
                    exit 1
                fi
                if [[ ! -f "$PLIST_PATH" ]]; then
                    # Recreate plist
                    mkdir -p "$(dirname "$PLIST_PATH")"
                    cat > "$PLIST_PATH" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>${PLIST_NAME}</string>
    <key>ProgramArguments</key>
    <array>
        <string>${WATCHER_PATH}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/tmp/matrix-bg-watcher.log</string>
    <key>StandardErrorPath</key>
    <string>/tmp/matrix-bg-watcher.log</string>
</dict>
</plist>
PLIST
                fi
                launchctl load "$PLIST_PATH" 2>/dev/null
                echo "Idle screensaver enabled."
                ;;
            off)
                launchctl unload "$PLIST_PATH" 2>/dev/null || true
                echo "Idle screensaver disabled."
                ;;
            status)
                if launchctl list "$PLIST_NAME" &>/dev/null; then
                    echo "Idle screensaver: RUNNING"
                else
                    echo "Idle screensaver: STOPPED"
                fi
                ;;
            *)
                echo "Usage: matrix-bg screensaver [on|off|status]"
                ;;
        esac
        ;;
    --fullscreen|"")
        exec "$BIN" "$@"
        ;;
    --help|-h)
        echo "matrix-bg — Matrix rain desktop overlay for macOS"
        echo ""
        echo "Usage:"
        echo "  matrix-bg              Desktop wallpaper overlay"
        echo "  matrix-bg --fullscreen Fullscreen screensaver"
        echo "  matrix-bg screensaver [on|off|status]"
        echo ""
        echo "Ctrl+C to quit."
        ;;
    *)
        exec "$BIN" "$@"
        ;;
esac
WRAPPER
chmod +x "$WRAPPER_PATH"

echo -e "${GREEN}Installed${NC} matrix-bg to $INSTALL_DIR/matrix-bg"
echo ""

# Check PATH
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo "Note: $INSTALL_DIR is not in your PATH."
    echo "Add this to your ~/.zshrc:"
    echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
    echo ""
fi

# Usage info
echo "Usage:"
echo "  matrix-bg              Run as desktop wallpaper overlay"
echo "  matrix-bg --fullscreen Run as fullscreen screensaver"
echo "  Ctrl+C to quit"
echo ""

# Ask about screensaver (skip if piped)
if [[ -t 0 ]]; then
    read -p "Enable idle screensaver? (starts after 60s of system idle) [y/N] " -n 1 -r
    echo ""
else
    REPLY="n"
fi

install_watcher() {
    # Create watcher script
    cat > "$WATCHER_PATH" << 'WATCHER'
#!/bin/bash
# matrix-bg idle watcher — monitors system idle and launches screensaver
IDLE_SECONDS="${MATRIX_BG_IDLE:-60}"
BIN="$HOME/.local/bin/matrix-bg-bin"

while true; do
    IDLE=$(ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print int($NF/1000000000); exit}')
    if [[ "$IDLE" -ge "$IDLE_SECONDS" ]]; then
        if ! pgrep -qf "matrix-bg-bin --fullscreen"; then
            "$BIN" --fullscreen &
            MPID=$!
            while true; do
                sleep 0.5
                IDLE=$(ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print int($NF/1000000000); exit}')
                if [[ "$IDLE" -lt 5 ]]; then
                    kill "$MPID" 2>/dev/null
                    wait "$MPID" 2>/dev/null
                    break
                fi
            done
        fi
    fi
    sleep 5
done
WATCHER
    chmod +x "$WATCHER_PATH"

    # Create launchd plist
    mkdir -p "$PLIST_DIR"
    cat > "$PLIST_PATH" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>${PLIST_NAME}</string>
    <key>ProgramArguments</key>
    <array>
        <string>${WATCHER_PATH}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/tmp/matrix-bg-watcher.log</string>
    <key>StandardErrorPath</key>
    <string>/tmp/matrix-bg-watcher.log</string>
</dict>
</plist>
PLIST

    launchctl unload "$PLIST_PATH" 2>/dev/null || true
    launchctl load "$PLIST_PATH"

    echo -e "${GREEN}Idle screensaver enabled.${NC}"
    echo "  Matrix rain starts after 60 seconds of system idle."
    echo "  Move mouse or press any key to dismiss."
    echo ""
    echo "Manage:"
    echo "  matrix-bg screensaver status   — Check if running"
    echo "  matrix-bg screensaver off      — Disable"
    echo "  matrix-bg screensaver on       — Re-enable"
}

if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo -e "${CYAN}Setting up idle watcher...${NC}"
    install_watcher
else
    echo "Skipped. Enable later with: matrix-bg screensaver on"
fi

echo ""
echo -e "${GREEN}Done!${NC}"
