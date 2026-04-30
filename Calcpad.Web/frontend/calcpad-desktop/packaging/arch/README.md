# Arch / pacman packaging for CalcpadCE

Builds and installs CalcpadCE as a system pacman package.

## Install

```sh
cd Calcpad.Web/frontend/calcpad-desktop/packaging/arch
makepkg -si
```

This compiles the .NET server, packages the Neutralino frontend, and installs:

| Path | Purpose |
| --- | --- |
| `/opt/calcpad-ce/` | Binary, `resources.neu`, embedded .NET server |
| `/usr/bin/calcpad-ce` | Launcher wrapper (sets `GDK_BACKEND=x11`, finds browser) |
| `/usr/share/applications/calcpad-ce.desktop` | Desktop menu entry |
| `/usr/share/icons/hicolor/256x256/apps/calcpad-ce.png` | Icon |

## Run

From a terminal: `calcpad-ce`
From your launcher: search for **CalcpadCE**.

## Uninstall

```sh
sudo pacman -R calcpad-ce
```

## Notes

- Build deps: `npm`, `dotnet-sdk` (>= 10), `imagemagick`.
- Runtime deps: `webkit2gtk`, `gtk3`. PDF export needs a Chromium-family browser
  (`chromium`, `google-chrome`, or `microsoft-edge-stable`) — listed as optdeps.
- Wayland is forced off (`GDK_BACKEND=x11`) because WebKitGTK has rendering bugs
  there. Override by exporting `GDK_BACKEND=wayland` before launching.
