#!/bin/bash
# make-dmg.sh — wrap MatrixBG.app in a polished drag-to-Applications DMG
set -e

SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"
APP="$SOURCE_DIR/MatrixBG.app"
DMG="$SOURCE_DIR/MatrixBG.dmg"
VOLNAME="MatrixBG"
STAGING="$SOURCE_DIR/.dmg-staging"

GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

if [[ ! -d "$APP" ]]; then
    echo "Error: $APP not found. Run ./build-app.sh first."
    exit 1
fi

echo -e "${CYAN}Building DMG...${NC}"

# Clean previous artifacts
rm -rf "$STAGING" "$DMG"
mkdir -p "$STAGING"

# Copy app + create /Applications symlink for drag-to-install UX
cp -R "$APP" "$STAGING/"
ln -s /Applications "$STAGING/Applications"

# Build a compressed read-only DMG
hdiutil create \
    -volname "$VOLNAME" \
    -srcfolder "$STAGING" \
    -ov \
    -format UDZO \
    -fs HFS+ \
    "$DMG" >/dev/null

# Sign the DMG with the same Developer ID so it's tamper-evident
DEFAULT_SIGN_ID="Developer ID Application: Status Consulting Firm LLC (Z349CC556Z)"
SIGN_ID="${SIGN_ID:-$DEFAULT_SIGN_ID}"
if security find-identity -v -p codesigning | grep -q "$SIGN_ID"; then
    echo -e "${CYAN}Signing DMG...${NC}"
    codesign --force --sign "$SIGN_ID" --timestamp "$DMG"
fi

# Notarize the DMG too — Apple recommends notarizing both the .app AND the .dmg
if [[ "${1:-}" == "--notarize" ]]; then
    KEYCHAIN_PROFILE="${NOTARY_PROFILE:-AC_PASSWORD}"
    echo -e "${CYAN}Submitting DMG to Apple notary...${NC}"
    if xcrun notarytool submit "$DMG" --keychain-profile "$KEYCHAIN_PROFILE" --wait; then
        xcrun stapler staple "$DMG"
        spctl -a -t open --context context:primary-signature -v "$DMG" 2>&1 || true
        echo -e "${GREEN}DMG notarized + stapled.${NC}"
    else
        echo "DMG notarization failed."
        exit 1
    fi
fi

rm -rf "$STAGING"

echo -e "${GREEN}Built${NC} $DMG"
ls -lh "$DMG"
