#!/bin/bash
# build-app.sh — compiles MatrixBG.app bundle (menu bar app + rain renderer)
set -e

SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_NAME="MatrixBG"
APP_DIR="$SOURCE_DIR/$APP_NAME.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"
RES_DIR="$APP_DIR/Contents/Resources"

GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

echo -e "${CYAN}Building $APP_NAME.app...${NC}"

if ! command -v swiftc &>/dev/null; then
    echo "Error: swiftc not found. Install Xcode Command Line Tools:"
    echo "  xcode-select --install"
    exit 1
fi

# Fresh bundle
rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RES_DIR"

# Compile menu bar app — main executable
swiftc -O \
    -o "$MACOS_DIR/$APP_NAME" \
    "$SOURCE_DIR/matrix-bg-menubar.swift" \
    -framework AppKit -framework ServiceManagement

# Compile rain renderer — co-located so menu bar app finds it
swiftc -O \
    -o "$MACOS_DIR/matrix-bg-bin" \
    "$SOURCE_DIR/matrix-bg.swift" \
    -framework AppKit -framework CoreText

# Copy app icon if present
if [[ -f "$SOURCE_DIR/AppIcon.icns" ]]; then
    cp "$SOURCE_DIR/AppIcon.icns" "$RES_DIR/AppIcon.icns"
fi

# Info.plist — LSUIElement=true hides dock icon (menu bar only)
cat > "$APP_DIR/Contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDisplayName</key>
    <string>MatrixBG</string>
    <key>CFBundleName</key>
    <string>MatrixBG</string>
    <key>CFBundleIdentifier</key>
    <string>com.matrix-bg.app</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleExecutable</key>
    <string>MatrixBG</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

chmod +x "$MACOS_DIR/$APP_NAME" "$MACOS_DIR/matrix-bg-bin"

# Signing identity (Developer ID = signed for distribution, eligible for notarization)
# Override with: SIGN_ID="-" ./build-app.sh   for ad-hoc dev builds.
DEFAULT_SIGN_ID="Developer ID Application: Status Consulting Firm LLC (Z349CC556Z)"
SIGN_ID="${SIGN_ID:-$DEFAULT_SIGN_ID}"

if [[ "$SIGN_ID" == "-" ]]; then
    echo -e "${CYAN}Ad-hoc signing...${NC}"
    codesign --force --deep --sign - "$APP_DIR"
else
    if ! security find-identity -v -p codesigning | grep -q "$SIGN_ID"; then
        echo "Signing identity not found: $SIGN_ID"
        echo "Falling back to ad-hoc."
        codesign --force --deep --sign - "$APP_DIR"
    else
        echo -e "${CYAN}Signing with Developer ID + hardened runtime...${NC}"
        # Sign the inner Mach-O binary first (deepest first)
        codesign --force --options runtime --timestamp \
            --sign "$SIGN_ID" "$MACOS_DIR/matrix-bg-bin"
        # Then the main app executable
        codesign --force --options runtime --timestamp \
            --sign "$SIGN_ID" "$MACOS_DIR/$APP_NAME"
        # Then the bundle itself
        codesign --force --options runtime --timestamp \
            --sign "$SIGN_ID" "$APP_DIR"
        # Verify
        codesign --verify --deep --strict --verbose=2 "$APP_DIR" 2>&1 | tail -5
    fi
fi

echo -e "${GREEN}Built${NC} $APP_DIR"
echo ""

# Notarize (only if --notarize flag is passed; takes 5-15 minutes)
if [[ "${1:-}" == "--notarize" ]]; then
    KEYCHAIN_PROFILE="${NOTARY_PROFILE:-AC_PASSWORD}"
    echo -e "${CYAN}Submitting to Apple notary service ($KEYCHAIN_PROFILE)...${NC}"
    ZIP="$SOURCE_DIR/$APP_NAME.zip"
    rm -f "$ZIP"
    ditto -c -k --keepParent "$APP_DIR" "$ZIP"
    if xcrun notarytool submit "$ZIP" --keychain-profile "$KEYCHAIN_PROFILE" --wait; then
        echo -e "${CYAN}Stapling notarization ticket...${NC}"
        xcrun stapler staple "$APP_DIR"
        spctl -a -vvv "$APP_DIR" 2>&1 || true
        echo -e "${GREEN}Notarized.${NC} App is ready for distribution."
    else
        echo "Notarization failed. Check the log with:"
        echo "  xcrun notarytool log <submission-id> --keychain-profile $KEYCHAIN_PROFILE"
        exit 1
    fi
    rm -f "$ZIP"
fi

echo "Install with:"
echo "  cp -R \"$APP_DIR\" /Applications/"
echo "Or run directly:"
echo "  open \"$APP_DIR\""
