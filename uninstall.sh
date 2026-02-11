#!/bin/bash
# matrix-bg uninstaller
# Removes binary, watcher, and launchd agent.
set -e

INSTALL_DIR="$HOME/.local/bin"
PLIST_DIR="$HOME/Library/LaunchAgents"
PLIST_NAME="com.matrix-bg.idle-watcher"
PLIST_PATH="$PLIST_DIR/$PLIST_NAME.plist"
WATCHER_PATH="$INSTALL_DIR/matrix-bg-watcher"

GREEN='\033[0;32m'
NC='\033[0m'

echo "Uninstalling matrix-bg..."

# Kill any running instance
pkill -f "matrix-bg" 2>/dev/null || true

# Unload launchd agent
if [[ -f "$PLIST_PATH" ]]; then
    launchctl unload "$PLIST_PATH" 2>/dev/null || true
    rm -f "$PLIST_PATH"
    echo "  Removed idle watcher agent"
fi

# Remove files
for f in "$INSTALL_DIR/matrix-bg" "$INSTALL_DIR/matrix-bg-bin" "$WATCHER_PATH"; do
    if [[ -f "$f" ]]; then
        rm -f "$f"
        echo "  Removed $f"
    fi
done

# Clean up temp files
rm -f /tmp/matrix-bg-watcher.log
rm -f /tmp/.matrix-bg-wallpaper-backup
rm -f /tmp/.matrix-idle-pid

echo -e "${GREEN}matrix-bg uninstalled.${NC}"
