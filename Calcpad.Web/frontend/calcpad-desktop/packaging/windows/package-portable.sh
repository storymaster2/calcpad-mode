#!/bin/bash
# Creates a portable Windows ZIP of the CalcPad Desktop app.
# Output: packaging/windows/CalcpadCEWeb-portable[-<version>].zip
#
# Prerequisites: run build-desktop.sh (with win-x64 RID) and `npx neu build` first.

set -e

RED='\033[0;31m'; GREEN='\033[0;32m'; BLUE='\033[0;34m'; NC='\033[0m'
info() { echo -e "${BLUE}[INFO]${NC}  $*"; }
ok()   { echo -e "${GREEN}[OK]${NC}    $*"; }
err()  { echo -e "${RED}[ERR]${NC}   $*"; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_DIR="$SCRIPT_DIR/../../dist/calcpad-desktop"
OUT_DIR="$SCRIPT_DIR"
VERSION="${1:-}"

NEU_EXE="$DIST_DIR/calcpad-desktop-win_x64.exe"
RESOURCES="$DIST_DIR/resources.neu"
SERVER_EXE="$DIST_DIR/extensions/server/Calcpad.Server.exe"

[ -f "$NEU_EXE" ]    || err "calcpad-desktop-win_x64.exe not found. Run 'npx neu build' first."
[ -f "$RESOURCES" ]  || err "resources.neu not found. Run 'npx neu build' first."
[ -f "$SERVER_EXE" ] || err "Calcpad.Server.exe not found. Run: bash build-desktop.sh --rid=win-x64"

command -v zip >/dev/null || err "'zip' not found. Install it (e.g. sudo pacman -S zip)."

ZIPNAME="CalcpadCEWeb-portable${VERSION:+-$VERSION}.zip"
ZIPPATH="$OUT_DIR/$ZIPNAME"

info "Staging portable Windows build..."

STAGE_DIR="$(mktemp -d)"
trap 'rm -rf "$STAGE_DIR"' EXIT

cp "$NEU_EXE"   "$STAGE_DIR/CalcpadCEWeb.exe"
cp "$RESOURCES" "$STAGE_DIR/"
cp -r "$DIST_DIR/extensions" "$STAGE_DIR/"

[ -f "$ZIPPATH" ] && rm -f "$ZIPPATH"
(cd "$STAGE_DIR" && zip -r "$ZIPPATH" .)

SIZE=$(du -sh "$ZIPPATH" | cut -f1)
ok "Created: $ZIPPATH ($SIZE)"
