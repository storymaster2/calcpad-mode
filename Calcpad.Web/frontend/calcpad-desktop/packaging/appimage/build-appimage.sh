#!/bin/bash
# Build a CalcpadCE AppImage for Linux.
#
# Assembles dist/calcpad-desktop/{calcpad-desktop-linux_<arch>, resources.neu,
# extensions/} into an AppDir alongside AppRun, the desktop file, and the
# icon, then runs appimagetool to produce a self-contained .AppImage.
#
# Run ../../build-desktop.sh first to populate dist/.

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

info()  { echo -e "${BLUE}[INFO]${NC}  $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
ok()    { echo -e "${GREEN}[OK]${NC}    $*"; }
err()   { echo -e "${RED}[ERR]${NC}   $*"; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DESKTOP_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
DIST_DIR="$DESKTOP_DIR/dist/calcpad-desktop"
REPO_ROOT="$(cd "$DESKTOP_DIR/../../.." && pwd)"

PKGNAME="calcpad-ce"
PKGVER="$(node -p "require('$DESKTOP_DIR/package.json').version" 2>/dev/null || echo "0.0.0")"

# ─── Detect target arch ──────────────────────────────────────────────────────
case "$(uname -m)" in
    x86_64|amd64) NEU_ARCH="linux_x64";   APPIMAGE_ARCH="x86_64" ;;
    aarch64|arm64) NEU_ARCH="linux_arm64"; APPIMAGE_ARCH="aarch64" ;;
    armv7l|armhf)  NEU_ARCH="linux_armhf"; APPIMAGE_ARCH="armhf" ;;
    *) err "Unsupported architecture: $(uname -m)"; exit 1 ;;
esac

NEU_BIN="$DIST_DIR/calcpad-desktop-${NEU_ARCH}"
RESOURCES_NEU="$DIST_DIR/resources.neu"
EXTENSIONS_DIR="$DIST_DIR/extensions"

# ─── Verify build artifacts ──────────────────────────────────────────────────
for path in "$NEU_BIN" "$RESOURCES_NEU" "$EXTENSIONS_DIR"; do
    if [ ! -e "$path" ]; then
        err "Missing $path"
        err "Run $DESKTOP_DIR/build-desktop.sh and 'npx neu build' first."
        exit 1
    fi
done

# ─── Ensure appimagetool is available ────────────────────────────────────────
APPIMAGETOOL="$(command -v appimagetool 2>/dev/null || true)"
if [ -z "$APPIMAGETOOL" ]; then
    cached="$SCRIPT_DIR/.cache/appimagetool-${APPIMAGE_ARCH}.AppImage"
    if [ ! -x "$cached" ]; then
        info "Downloading appimagetool for ${APPIMAGE_ARCH}..."
        mkdir -p "$(dirname "$cached")"
        url="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${APPIMAGE_ARCH}.AppImage"
        if command -v curl &>/dev/null; then
            curl -fL -o "$cached" "$url"
        elif command -v wget &>/dev/null; then
            wget -O "$cached" "$url"
        else
            err "Neither curl nor wget available; install one or place appimagetool on PATH."
            exit 1
        fi
        chmod +x "$cached"
    fi
    APPIMAGETOOL="$cached"
fi

# ─── Assemble AppDir ─────────────────────────────────────────────────────────
APPDIR="$SCRIPT_DIR/build/CalcpadCE.AppDir"
OUTPUT_DIR="$SCRIPT_DIR/out"

info "Cleaning previous AppDir..."
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
         "$APPDIR/usr/share/licenses/$PKGNAME" \
         "$OUTPUT_DIR"

info "Copying Neutralino binary and resources..."
install -Dm755 "$NEU_BIN" "$APPDIR/usr/bin/calcpad-desktop"
install -Dm644 "$RESOURCES_NEU" "$APPDIR/usr/bin/resources.neu"

info "Copying embedded .NET server..."
cp -a "$EXTENSIONS_DIR" "$APPDIR/usr/bin/"

info "Installing AppRun, desktop file, icon..."
install -Dm755 "$SCRIPT_DIR/AppRun" "$APPDIR/AppRun"
install -Dm644 "$SCRIPT_DIR/$PKGNAME.desktop" "$APPDIR/$PKGNAME.desktop"
install -Dm644 "$SCRIPT_DIR/$PKGNAME.desktop" "$APPDIR/usr/share/applications/$PKGNAME.desktop"
install -Dm644 "$DESKTOP_DIR/resources/icon.png" "$APPDIR/$PKGNAME.png"
install -Dm644 "$DESKTOP_DIR/resources/icon.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/$PKGNAME.png"
ln -sf "$PKGNAME.png" "$APPDIR/.DirIcon"

if [ -f "$REPO_ROOT/LICENSE" ]; then
    install -Dm644 "$REPO_ROOT/LICENSE" "$APPDIR/usr/share/licenses/$PKGNAME/LICENSE"
fi

# ─── Build the AppImage ──────────────────────────────────────────────────────
OUTFILE="$OUTPUT_DIR/CalcpadCE-${PKGVER}-${APPIMAGE_ARCH}.AppImage"
info "Running appimagetool..."
ARCH="$APPIMAGE_ARCH" "$APPIMAGETOOL" --no-appstream "$APPDIR" "$OUTFILE"

ok "AppImage built: $OUTFILE"
echo ""
echo -e "  ${BLUE}Run:${NC}    chmod +x $OUTFILE && $OUTFILE"
echo ""
