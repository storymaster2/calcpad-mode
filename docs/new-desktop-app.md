# Calcpad Desktop App

> Calcpad.Web only. The WPF desktop application is unaffected.

The Calcpad desktop app (`Calcpad.Web/frontend/calcpad-desktop`) is a [Tauri](https://tauri.app/)-packaged build of the same web editor that runs in the browser, bundled with the .NET server as a sidecar binary. It exists to give users a native filesystem + native menu / save-dialog experience without giving up the Monaco editor stack.

## Multi-tab editing

VS Code-style tabs let you keep several `.cpd` documents open at once.

| Action | Shortcut |
|--------|----------|
| New tab | **Ctrl+T** or **Ctrl+N**, or the **`+`** button on the tab strip |
| Close tab | **Ctrl+W**, the **✕** on the tab, or middle-click the tab |
| Cycle tabs | **Ctrl+Tab** (next), **Ctrl+Shift+Tab** (previous) |
| Jump to Nth tab | **Ctrl+1** … **Ctrl+8** |
| Jump to last tab | **Ctrl+9** |

Behaviour matches VS Code:

- **One editor, many models** — the single Monaco editor swaps its underlying model on tab activation; cursor and scroll position are saved in view state and restored on switch-back.
- **Per-tab dirty tracking** — based on `model.getAlternativeVersionId()`, so undoing back to the saved version clears the dirty dot.
- **Open-existing focus** — opening a file that's already in a tab activates that tab instead of duplicating it.
- **Untitled scratch reuse** — opening a file from an empty `Untitled-N` tab replaces it in place rather than stacking another tab.
- **Per-tab close prompt** — closing a dirty tab opens a Save / Don't Save / Cancel dialog. Window close walks every dirty tab in turn; cancel any prompt to abort the exit.
- **Per-tab providers** — hover, definitions, find-references, and the linter cache are scoped per tab, so symbols don't bleed between unrelated documents.

The Problems panel re-emits the active tab's markers on every switch, so it never shows stale data from another tab. The preview pane and TOC sidebar also repaint against the new active model.

## Editor key behaviour

Two Monaco options are flipped to match the most common keyboard expectations and to stop the suggest widget from intercepting newlines:

- **Enter is always a newline.** `acceptSuggestionOnEnter: 'off'` and `acceptSuggestionOnCommitCharacter: false` together guarantee that pressing Enter never accepts a suggestion — it always inserts a line break.
- **Tab accepts suggestions.** `tabCompletion: 'on'` makes Tab the accept key when the suggest widget is open, and triggers completion when typing a partial word.

## Export tab

The sidebar's **Export** tab now has three actions in addition to the existing per-file download buttons:

| Button | Behaviour (desktop) | Behaviour (web) |
|--------|---------------------|------------------|
| **Save HTML…** | Native save dialog → writes `.html` via Tauri's `plugin-fs` | Browser blob download (`calcpad-output.html`) |
| **Save Word…** | Native save dialog → writes `.docx` via Tauri's `plugin-fs` | Browser blob download (`calcpad-output.docx`) |
| **Download all (.zip)** | Existing zipped `#write`/`#append` exports | Same |

Both new actions go through the same backend pipeline:

1. `POST /api/calcpad/convert` (HTML) or `POST /api/calcpad/docx` (Word)
2. The DOCX endpoint runs the calcpad → HTML pipeline with `forPrint: true`, then feeds the HTML through `Calcpad.OpenXml.OpenXmlWriter` and returns the `.docx` bytes
3. Frontend writes the result via the platform's save mechanism

The same buttons fire on the VS Code sidebar's Export tab, where they execute the `vscode-calcpad.saveSourceHtml` and `vscode-calcpad.saveDocx` commands (also available from the Command Palette).

## Embedded server lifecycle

The `.NET` server runs as a Tauri sidecar spawned by the Rust shell at app start (see `spawn_sidecar` in [src-tauri/src/lib.rs](../Calcpad.Web/frontend/calcpad-desktop/src-tauri/src/lib.rs)). The apphost is a framework-independent `Calcpad.Server` published for the host RID and renamed to Tauri's target-triple sidecar format (`calcpad-server-<target-triple>[.exe]`), staged into `src-tauri/binaries/` by `stage-sidecar.sh` / `stage-sidecar.ps1` before dev, and picked up by `tauri.conf.json`'s `bundle.externalBin` at build time.

- **Startup args** — the shell passes `--no-exit-on-stdin-close`, `--parent-pid`, and `--port-file <temp>` so the server writes its bound port to a temp file and exits cleanly when the parent Tauri process dies. Rust polls the port file for readiness (faster than parsing Kestrel's stdout, which ASP.NET Core's ConsoleLogger can buffer for hundreds of ms).
- **CWD** — set to the apphost's directory so .NET's dependency resolver finds the sibling DLLs regardless of where Tauri was launched from.
- **Server URL** — the frontend reads the bound port from the port file. Default port is `9420` when unbound.
- **Stderr / logs** — captured into `<serverDir>/logs/server-stderr.log`; the `Server → Show Server Log` menu item opens this file.

See [PDF export](new-pdf-export.md) for how the desktop detects a missing Chromium browser and the recommended Chromium packages per distribution.

## Native menu

Built in Rust via Tauri's `MenuBuilder` (see `build_menu` in [src-tauri/src/lib.rs](../Calcpad.Web/frontend/calcpad-desktop/src-tauri/src/lib.rs)). Menu-item ids are dispatched to the frontend as Tauri events; the corresponding TypeScript listeners live in the Tauri bridge. Menu surface:

- **File** — New Tab · Open… · Save · Save As… · Close Tab · Export PDF… · Quit
- **Edit** — Undo · Redo · Cut · Copy · Paste · Select All · Find · Replace
- **View** — Toggle Sidebar · Toggle Preview · Preview Mode: Wrapped / Unwrapped
- **Server** — Refresh · Show Server Log · Stop Server · Restart Server · Restart App

Recent files are tracked by the frontend via Tauri's `plugin-store`.

## Drag-and-drop

Dropping one or more files on the editor opens each in its own tab. Native filesystem drops use the OS path; non-OS drops (e.g. text from a browser) open as untitled tabs with the dropped contents.

## Packaging

Tauri's own bundler produces per-platform installers via `tauri build`. Configured targets (see [tauri.conf.json](../Calcpad.Web/frontend/calcpad-desktop/src-tauri/tauri.conf.json) `bundle.targets`):

| Platform | Formats | Notes |
|----------|---------|-------|
| Windows | `msi`, `nsis` | `nsis` uses `installMode: perMachine`. WiX language `en-US`. |
| macOS   | `app`, `dmg` | `minimumSystemVersion: 11.0`. |
| Linux   | `deb`, `appimage` | The AppImage bundles the WebKitGTK dependency implicitly; the `.deb` package's `depends` list is left empty (WebKitGTK is expected on the host). |

The Calcpad.Server sidecar and its fonts / template resources are staged into `src-tauri/binaries/` and included via `bundle.resources`. Cross-compilation for Windows from Linux is wired up through the `Desktop: Bundle Windows (cross)` VS Code task (calls `build-desktop.sh --rid=win-x64 --target=x86_64-pc-windows-msvc`).
