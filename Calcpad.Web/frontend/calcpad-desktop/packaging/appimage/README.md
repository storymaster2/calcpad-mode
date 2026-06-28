# AppImage packaging for CalcpadCE

Builds a single-file, portable Linux build of CalcpadCE.

## Build

```sh
cd Calcpad.Web/frontend/calcpad-desktop

# 1. Build the .NET server and Neutralino bundle (only needed once / on changes)
bash ./build-desktop.sh
npx neu build

# 2. Build the AppImage
bash ./packaging/appimage/build-appimage.sh
```

The output lands in `packaging/appimage/out/CalcpadCE-<version>-<arch>.AppImage`.

If `appimagetool` is not on `PATH`, the script downloads the official binary
into `packaging/appimage/.cache/` on first run.

## Run

```sh
chmod +x CalcpadCE-*.AppImage
./CalcpadCE-*.AppImage
```

## Layout

| Path inside AppDir | Source |
| --- | --- |
| `AppRun` | `AppRun` (sets `GDK_BACKEND=x11`, finds a Chromium-family browser, execs the binary) |
| `usr/bin/calcpad-desktop` | `dist/calcpad-desktop/calcpad-desktop-linux_<arch>` |
| `usr/bin/resources.neu` | `dist/calcpad-desktop/resources.neu` |
| `usr/bin/extensions/` | `dist/calcpad-desktop/extensions/` (.NET server) |
| `calcpad-ce.desktop`, `calcpad-ce.png` | This directory |

## Notes

- Runtime deps: `webkit2gtk-4.1`, `gtk3` on the host. PDF export needs a
  Chromium-family browser (`chromium`, `google-chrome`, `microsoft-edge-stable`,
  …) — override detection by exporting `BROWSER_PATH`.
- Wayland is forced off (`GDK_BACKEND=x11`) because WebKitGTK has rendering
  bugs there. Override by exporting `GDK_BACKEND=wayland`.
- Supported host architectures: `x86_64`, `aarch64`, `armhf`. The script
  selects the matching Neutralino binary from `dist/`.
- The bundled .NET server writes its port file, lock file, and logs next to
  its binary; since the AppImage mount is read-only, `AppRun` stages the
  bundle into `~/.cache/CalcpadCE/runtime/` on first launch (and on every
  upgrade). Delete that directory to force a fresh re-stage.
