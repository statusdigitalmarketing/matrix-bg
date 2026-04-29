#!/bin/bash
# Double-click installer for matrix-bg.
# Builds MatrixBG.app, copies to /Applications, and launches it.
set -e

GREEN='\033[0;32m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'

# cd to the folder this .command lives in (works wherever AirDrop drops it)
cd "$(dirname "$0")"

clear
echo -e "${CYAN}===================================="
echo -e " matrix-bg installer"
echo -e "====================================${NC}"
echo ""

# Check macOS
if [[ "$(uname)" != "Darwin" ]]; then
    echo -e "${RED}Error:${NC} matrix-bg only runs on macOS."
    read -p "Press Enter to close..."
    exit 1
fi

# Check Xcode CLT
if ! command -v swiftc &>/dev/null; then
    echo -e "${RED}Xcode Command Line Tools not installed.${NC}"
    echo ""
    echo "A dialog will open to install them. Click 'Install', wait for it to finish,"
    echo "then double-click this installer again."
    echo ""
    xcode-select --install 2>/dev/null || true
    read -p "Press Enter to close..."
    exit 1
fi

# Disable legacy bash watcher if a previous version installed it.
# The new menu bar app handles idle detection itself.
LEGACY_PLIST="$HOME/Library/LaunchAgents/com.matrix-bg.idle-watcher.plist"
if [[ -f "$LEGACY_PLIST" ]]; then
    echo "Removing legacy idle watcher..."
    launchctl unload "$LEGACY_PLIST" 2>/dev/null || true
    rm -f "$LEGACY_PLIST"
fi

# Build .app
./build-app.sh

# Install to /Applications
APP_SRC="$(pwd)/MatrixBG.app"
APP_DST="/Applications/MatrixBG.app"

if [[ -d "$APP_DST" ]]; then
    echo "Removing previous installation..."
    rm -rf "$APP_DST"
fi

echo "Copying MatrixBG.app to /Applications..."
cp -R "$APP_SRC" "$APP_DST"

# Also install the CLI binaries for power users
"$(pwd)/install.sh" < /dev/null > /dev/null 2>&1 || true

# Launch it
echo ""
echo -e "${GREEN}Installed!${NC}"
echo ""
echo "Look for the ✨ icon in your menu bar (top-right of screen)."
echo ""
echo "From the menu you can:"
echo "  • Run as Wallpaper / Run Fullscreen"
echo "  • Toggle the idle screensaver"
echo "  • Set the idle timeout"
echo "  • Launch at login"
echo ""

open "$APP_DST"

echo "Press Enter to close this window..."
read
