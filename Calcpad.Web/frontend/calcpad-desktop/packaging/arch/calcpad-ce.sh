#!/bin/bash
# CalcpadCE launcher — sets runtime env and execs the Neutralino binary.

INSTALL_DIR="/opt/calcpad-ce"

# Force X11 backend — WebKitGTK on Wayland has known rendering issues.
export GDK_BACKEND="${GDK_BACKEND:-x11}"
export WEBKIT_DISABLE_DMABUF_RENDERER="${WEBKIT_DISABLE_DMABUF_RENDERER:-1}"

# Default browser for PDF export: ungoogled-chromium.
# The AUR `ungoogled-chromium` package installs as /usr/bin/chromium, so try
# that first (most installs), then the explicit binary name, then any other
# Chromium-family browser as a fallback. Override by exporting BROWSER_PATH.
if [ -z "$BROWSER_PATH" ]; then
    if [ -x /usr/bin/chromium ]; then
        export BROWSER_PATH=/usr/bin/chromium
    else
        for cand in ungoogled-chromium chromium-browser \
                    google-chrome-stable google-chrome microsoft-edge-stable; do
            resolved="$(command -v "$cand" 2>/dev/null || true)"
            if [ -n "$resolved" ]; then
                export BROWSER_PATH="$resolved"
                break
            fi
        done
    fi
fi

cd "$INSTALL_DIR" || exit 1
exec ./calcpad-desktop "$@"
