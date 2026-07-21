# Calcpad Desktop App

> Calcpad.Web only. The WPF desktop application is unaffected.

The Calcpad desktop app (`Calcpad.Web/frontend/calcpad-desktop`) is a Neutralino-packaged build of the same web editor that runs in the browser, bundled with the .NET server as a sidecar extension. It exists to give users a native filesystem + native menu / save-dialog experience without giving up the Monaco editor stack.

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
| **Save HTML…** | Native save dialog → writes `.html` via `filesystem.writeFile` | Browser blob download (`calcpad-output.html`) |
| **Save Word…** | Native save dialog → writes `.docx` via `filesystem.writeBinaryFile` | Browser blob download (`calcpad-output.docx`) |
| **Download all (.zip)** | Existing zipped `#write`/`#append` exports | Same |

Both new actions go through the same backend pipeline:

1. `POST /api/calcpad/convert` (HTML) or `POST /api/calcpad/docx` (Word)
2. The DOCX endpoint runs the calcpad → HTML pipeline with `forPrint: true`, then feeds the HTML through `Calcpad.OpenXml.OpenXmlWriter` and returns the `.docx` bytes
3. Frontend writes the result via the platform's save mechanism

The same buttons fire on the VS Code sidebar's Export tab, where they execute the `vscode-calcpad.saveSourceHtml` and `vscode-calcpad.saveDocx` commands (also available from the Command Palette).

## Embedded server lifecycle

The `.NET` server runs as a Neutralino extension launched at app start.

- **Linux launcher** — `extensions/server/start-server.sh` probes for a Chromium-family browser, sets `BROWSER_PATH`, exports `CALCPAD_PORT=9420`, then `exec`s `Calcpad.Server` with stderr tee'd into `extensions/server/logs/server-stderr.log`.
- **Windows / macOS** — the apphost binary is launched directly (`CalcpadServer.exe` / `CalcpadServer`).
- **Server URL** — the frontend defaults to `http://localhost:9420`; the launcher binds to whatever the `CALCPAD_PORT` environment variable says.

See [PDF export](new-pdf-export.md) for the desktop-specific browser pre-flight check and the recommended Chromium packages per distribution.

## Native menu

Built via Neutralino's `setMainMenu` API. Roughly mirrors the VS Code menu surface:

- **File** — New Tab · Open… · Open Recent · Save · Save As… · Close Tab · Export PDF… · Quit
- **View** — Toggle Sidebar · Toggle Preview · Preview Mode (Wrapped / Unwrapped / Interactive)
- **Server** — Refresh · Show Server Log · Restart App

The Recent submenu is rebuilt every time a file opens, capped at the most recent 10 entries.

## Drag-and-drop

Dropping one or more files on the editor opens each in its own tab. Native filesystem drops use the OS path; non-OS drops (e.g. text from a browser) open as untitled tabs with the dropped contents.

## Packaging

- **Arch** — `packaging/arch/PKGBUILD` builds with `npm`, `dotnet-sdk>=10.0`, and `imagemagick`. Runtime depends on `webkit2gtk-4.1` + `gtk3`. Optional dependencies cover the supported Chromium variants for PDF export, with `ungoogled-chromium-bin` listed as the preferred prebuilt option.
- The packaged tree lays out `/opt/calcpad-ce/calcpad-desktop`, `/opt/calcpad-ce/resources.neu`, the bundled `extensions/server/` tree, a `/usr/bin/calcpad-ce` shim, and a desktop entry under `/usr/share/applications/`.
