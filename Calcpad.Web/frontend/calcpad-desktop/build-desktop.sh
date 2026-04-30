#!/bin/bash

# Build script for CalcPad Desktop (Neutralino + .NET server)
# Detects OS/arch, builds the .NET server with correct SkiaSharp/Playwright
# assets, finds system browser, and packages via Neutralino CLI.

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

info()  { echo -e "${BLUE}[INFO]${NC}  $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
ok()    { echo -e "${GREEN}[OK]${NC}    $*"; }
err()   { echo -e "${RED}[ERR]${NC}   $*"; }

# ─── Resolve paths ───────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DESKTOP_DIR="$SCRIPT_DIR"
FRONTEND_DIR="$(dirname "$DESKTOP_DIR")"
BACKEND_DIR="$FRONTEND_DIR/../backend"
REPO_ROOT="$(cd "$FRONTEND_DIR/../.." && pwd)"
EXTENSIONS_DIR="$DESKTOP_DIR/extensions/server"

# ─── Detect OS and Architecture ─────────────────────────────────────────────
detect_platform() {
    local os arch

    case "$(uname -s)" in
        Linux*)  os="linux" ;;
        Darwin*) os="osx" ;;
        MINGW*|MSYS*|CYGWIN*) os="win" ;;
        *)
            err "Unsupported OS: $(uname -s)"
            exit 1
            ;;
    esac

    case "$(uname -m)" in
        x86_64|amd64)  arch="x64" ;;
        aarch64|arm64) arch="arm64" ;;
        *)
            err "Unsupported architecture: $(uname -m)"
            exit 1
            ;;
    esac

    DOTNET_RID="${os}-${arch}"
    PLATFORM_OS="$os"
    PLATFORM_ARCH="$arch"

    info "Detected platform: ${DOTNET_RID}"
}

# ─── Find system browser for Playwright ─────────────────────────────────────
find_browser() {
    local candidates=()

    case "$PLATFORM_OS" in
        linux)
            candidates=(
                chromium
                chromium-browser
                google-chrome-stable
                google-chrome
                microsoft-edge-stable
                microsoft-edge
            )
            ;;
        osx)
            candidates=(
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
                "/Applications/Chromium.app/Contents/MacOS/Chromium"
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
            )
            # Also check brew-installed chromium
            if command -v chromium &>/dev/null; then
                BROWSER_PATH="$(command -v chromium)"
                return 0
            fi
            ;;
        win)
            # On Windows (MSYS/Git Bash), check common install paths
            candidates=(
                "/c/Program Files/Google/Chrome/Application/chrome.exe"
                "/c/Program Files (x86)/Google/Chrome/Application/chrome.exe"
                "/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
                "/c/Program Files/Microsoft/Edge/Application/msedge.exe"
            )
            ;;
    esac

    for candidate in "${candidates[@]}"; do
        if [[ "$candidate" == /* ]]; then
            # Absolute path
            if [ -x "$candidate" ]; then
                BROWSER_PATH="$candidate"
                return 0
            fi
        else
            # Command name — resolve via PATH
            local resolved
            resolved="$(command -v "$candidate" 2>/dev/null || true)"
            if [ -n "$resolved" ]; then
                BROWSER_PATH="$resolved"
                return 0
            fi
        fi
    done

    BROWSER_PATH=""
    return 1
}

# ─── Build .NET server ──────────────────────────────────────────────────────
# Delegates to the shared sync-bundled-server.mjs script (which lives in
# vscode-calcpad/scripts/) so the desktop and the VS Code extension stay in
# lock-step on dependency layout, deps.json freshness, executable bits, and
# pdb stripping. Runs the script with --keep-skia-natives because the
# desktop ships standalone — there's no runtime download path for the
# SkiaSharp natives that vscode-calcpad downloads on first activation.
build_server() {
    info "Building Calcpad.Server for ${DOTNET_RID}..."

    if ! command -v dotnet &>/dev/null; then
        err "dotnet CLI not found. Install .NET 10 SDK: https://dotnet.microsoft.com/download"
        exit 1
    fi
    if ! command -v node &>/dev/null; then
        err "node not found. Install Node.js to run the shared sync-bundled-server.mjs script."
        exit 1
    fi

    local sync_script="$FRONTEND_DIR/vscode-calcpad/scripts/sync-bundled-server.mjs"
    if [ ! -f "$sync_script" ]; then
        err "Shared sync script not found at $sync_script"
        exit 1
    fi

    local dotnet_version
    dotnet_version="$(dotnet --version 2>/dev/null || echo 'unknown')"
    info "Using dotnet ${dotnet_version}"

    # Skip Playwright browser download — we use the system browser
    export PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1

    info "Publishing self-contained build via shared sync script..."
    node "$sync_script" \
        --target="$EXTENSIONS_DIR" \
        --rid="$DOTNET_RID" \
        --configuration=Release \
        --keep-skia-natives

    # Verify the executable
    local exe_name="Calcpad.Server"
    if [ "$PLATFORM_OS" = "win" ]; then
        exe_name="Calcpad.Server.exe"
    fi

    if [ -f "$EXTENSIONS_DIR/$exe_name" ]; then
        local size
        size="$(du -sh "$EXTENSIONS_DIR" | cut -f1)"
        ok "Server built: ${EXTENSIONS_DIR}/${exe_name} (${size})"
    else
        err "Build failed — ${exe_name} not found in ${EXTENSIONS_DIR}"
        exit 1
    fi
}

# ─── Server launcher (no wrapper script needed) ────────────────────────────
# The .NET server is invoked directly by Neutralino on every platform —
# `Calcpad.Server[.exe]` is a self-contained apphost. All the behaviour
# that used to live in the bash/cmd wrapper now lives in Program.cs:
#
#   - Browser detection: PdfGeneratorService.ResolveBrowserPathAsync
#     searches the user's PATH when BROWSER_PATH is not set.
#   - Stderr/stdout logging: FileLogger writes to logs/CalcpadServer-*.log
#     and crash records to logs/last-crash.txt.
#   - Random free port: Program.cs adds `--urls http://127.0.0.1:0` when
#     no --urls flag and no CALCPAD_PORT env var was given.
#   - Port-file publishing: Program.cs writes the bound URL to
#     `.calcpad-server.port` next to the binary by default.
#   - EOF watchdog: Program.cs exits when stdin closes (default-on for
#     piped-stdin launches; opt out with CALCPAD_DETACHED=1).
#
# This means Neutralino's commandWindows / commandDarwin / commandLinux
# all point at the apphost directly — see neutralino.config.json. The
# only consumer that opts out is the VS Code extension's
# server-manager.ts, which exports CALCPAD_DETACHED=1 because it shares
# one server across multiple windows via the lock-file mechanism.

# ─── Print summary ──────────────────────────────────────────────────────────
print_summary() {
    echo ""
    echo -e "${GREEN}═══════════════════════════════════════════════${NC}"
    echo -e "${GREEN}  CalcPad Desktop build complete${NC}"
    echo -e "${GREEN}═══════════════════════════════════════════════${NC}"
    echo ""
    echo -e "  Platform:     ${BLUE}${DOTNET_RID}${NC}"
    echo -e "  Server:       ${BLUE}${EXTENSIONS_DIR}/Calcpad.Server${NC}"

    if [ -n "$BROWSER_PATH" ]; then
        echo -e "  Browser:      ${GREEN}${BROWSER_PATH}${NC}"
    else
        echo -e "  Browser:      ${YELLOW}Not found — PDF export will not work${NC}"
        echo -e "                ${YELLOW}Install chromium or set BROWSER_PATH${NC}"
    fi

    echo ""
    echo -e "  ${BLUE}Next steps:${NC}"
    echo -e "    Dev mode:   cd $(basename "$DESKTOP_DIR") && npm run dev"
    echo -e "    Build:      cd $(basename "$DESKTOP_DIR") && npx neu build"
    echo ""
}

# ─── Main ────────────────────────────────────────────────────────────────────
main() {
    echo -e "${BLUE}╔══════════════════════════════════════════════╗${NC}"
    echo -e "${BLUE}║     CalcPad Desktop Build Script             ║${NC}"
    echo -e "${BLUE}╚══════════════════════════════════════════════╝${NC}"
    echo ""

    detect_platform

    # Find browser
    if find_browser; then
        ok "Found browser: ${BROWSER_PATH}"
    else
        warn "No Chromium/Chrome/Edge found on PATH"
        warn "PDF generation will be unavailable until BROWSER_PATH is set"
    fi

    # Clean previous server build
    if [ -d "$EXTENSIONS_DIR" ]; then
        info "Cleaning previous server build..."
        # Keep .gitkeep
        find "$EXTENSIONS_DIR" -mindepth 1 ! -name '.gitkeep' -exec rm -rf {} + 2>/dev/null || true
    fi
    mkdir -p "$EXTENSIONS_DIR"

    # Build (no separate launcher step — see comment block above)
    build_server

    print_summary
}

main "$@"
