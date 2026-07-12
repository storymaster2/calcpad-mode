# Using the Desktop App

> Part of Calcpad.Web. This is the cross-platform Calcpad desktop application — separate from the standalone WPF Calcpad for Windows.

The **Calcpad Desktop App** is a native application that wraps the same web editor used in the browser, packaged with a built-in calculation server. You get the full Monaco-based editor — multi-tab editing, syntax highlighting, autocomplete, live preview, and the CalcPad sidebar — plus native file dialogs, a native menu bar, and drag-and-drop, with no browser or separate server to manage.

This page is a task-oriented guide. For the internal architecture, see [Calcpad Desktop App (reference)](new-desktop-app.md). For the Calcpad language itself, start with [Writing Math](writing-math.md) and the [Quick Reference](quick-reference.md).

## Installing

The app ships as a per-platform installer:

| Platform | Installer |
|----------|-----------|
| Windows | `.exe` Portable .ZIP Build (no install) |
| Linux | `.AppImage` (run directly, no install)

The .NET calculation server and its fonts/templates are bundled inside the app — you do **not** need .NET installed separately.

For PDF export you need a **Chromium-based browser** (Chrome, Edge, or Chromium) installed on the system. On Linux the app will tell you which package to install if none is found — see [PDF Export](new-pdf-export.md).

## Your first document

1. Launch the app. It opens with an empty **Untitled-1** tab.
2. Type a calculation, for example:

   ```calcpad
   "Cantilever tip deflection
   P = 5kN
   L = 3m
   E = 200GPa
   I = 8.5e-5m^4
   δ = P·L^3/(3·E·I)
   ```

3. The **preview pane** renders your report live as you type. Toggle it from **View → Toggle Preview**.
4. Save with **Ctrl+S** (or **File → Save**). A native save dialog lets you choose the location.

## Working with tabs

The app uses VS Code-style tabs so you can keep several `.cpd` documents open at once.

| Action | Shortcut |
|--------|----------|
| New tab | **Ctrl+T** or **Ctrl+N**, or the **`+`** button on the tab strip |
| Open a file | **File → Open…**, or drag a file onto the window |
| Close tab | **Ctrl+W**, the **✕** on the tab, or middle-click the tab |
| Next / previous tab | **Ctrl+Tab** / **Ctrl+Shift+Tab** |
| Jump to tab 1–8 | **Ctrl+1** … **Ctrl+8** |
| Jump to last tab | **Ctrl+9** |

Tab behavior matches VS Code:

- **Cursor and scroll position are remembered** per tab and restored when you switch back.
- **Unsaved changes** are marked with a dot on the tab; undoing back to the last saved state clears it.
- **Opening an already-open file** activates its existing tab instead of duplicating it.
- **Opening a file from an empty Untitled tab** replaces it in place rather than stacking a new tab.
- **Closing a tab with unsaved changes** prompts Save / Don't Save / Cancel. Quitting the app walks through every unsaved tab; cancel any prompt to abort the quit.
- Hover, definitions, references, the linter, the preview, and the TOC are all scoped to the active tab, so symbols and errors never bleed between unrelated documents.

## Opening files

There are several ways to open documents:

- **File → Open…** — native file picker.
- **Drag and drop** — drop one or more files onto the editor; each opens in its own tab. Dropping plain text (e.g. from a browser) opens it as a new untitled tab.
- **Files tab** in the sidebar — open a folder and browse its tree.
- **Recent files** — tracked automatically and available from the File menu.

## The editor

The editor is the same Monaco-based editor described in the [VS Code extension guide](using-the-vscode-extension.md#the-editor), with the same behavior:

- **Syntax highlighting** for numbers, units, operators, variables, functions, macros, keywords, commands, and embedded HTML/Markdown in comments.
- **Autocomplete** that prioritizes your own symbols over built-ins, with snippet placeholders for function arguments.
- **Quick-type symbols** — `~a` + space → `α`, `~p` + space → `π`, etc.
- **Operator replacement** — `<=` → `≤`, `>=` → `≥`, `!=` → `≠`.
- **Auto-indentation** for `#if` / `#for` / `#def` blocks.
- **Go to Definition**, **Find All References**, **Rename**, and **hover** for symbols across `#include` files.

Two key behaviors worth knowing:

- **Enter always inserts a newline** — it never accepts a suggestion. Press **Tab** to accept a completion.
- **Tab accepts suggestions** and triggers completion on a partial word.

## The CalcPad sidebar

Toggle the sidebar with **View → Toggle Sidebar**. It has a **Files** view and a **Calcpad** view; the Calcpad view is split into tabs (Insert, TOC, Settings, Variables, PDF, Formatting, Export, Errors).

The sidebar is the same across every Calcpad front end — see **[The CalcPad Panel & Settings](calcpad-panel-and-settings.md)** for a full walkthrough of each tab, including Prettify options and the Export buttons.

## Live preview

The preview pane renders your report live and re-renders as you type. From **View** you can:

- **Toggle Preview** — show/hide the pane.
- **Preview Mode: Wrapped** — the normal report view.
- **Preview Mode: Unwrapped** — the fully expanded source, with macros and includes resolved. Useful for debugging what the engine computes.

### Manual run (Auto-Run off)

By default the preview re-renders continuously as you type. If you turn **Settings → Auto-Run Preview** off, the preview only re-renders when:

- The preview pane is first opened.
- You click **▶ Run** on the editor toolbar.
- You press **Ctrl+Alt+X**.
- You right-click in the editor → **Run Preview**.
- You use **Server → Refresh** in the native menu (same shortcut).

A manual run also re-lints the document, refreshes definitions/TOC, and re-populates the Export tab's plot list — so long-running documents can be edited freely and only re-computed on demand.

## Splitting the editor

The **Split ⬓** button in the editor toolbar (also **View → Split Editor**) opens a second editor group stacked below the first. Each group has its own tabs, tab strip, preview iframe, and Problems markers. Click **Unsplit** (same button) to close the bottom group. The active group is the one that most recently had focus — hover, definitions, references, and preview sync target the active group.

## Errors

### Linter
Calcpad Web uses a linter to catch calculation errors before they are converted to HTML.These get highlighted in red, yellow, or blue at the problematic location based on the severity of the issue. These linting errors appear in the **Problems** panel and link to the offending line. 

### Preview Errors
Errors that occur in the calculation engine (including inside hidden code) are listed in the **Errors** tab of the sidebar, each with a link to its source line.

## Exporting

All exports use the app's built-in server, so the output matches the preview exactly.

| Output | How | Notes |
|--------|-----|-------|
| **PDF** | **File → Export PDF…** | Full-fidelity export via a native save dialog. Requires a Chromium browser — see [PDF Export](new-pdf-export.md). |
| **HTML** | **Save HTML…** on the sidebar's **Export** tab | Native save dialog writes standalone `.html`. |
| **Word (.docx)** | **Save Word…** on the sidebar's **Export** tab | Native save dialog writes `.docx` (converted via Calcpad.OpenXml). |

Set the document title, timestamp format, page size, and header/footer in the sidebar's **PDF** tab before exporting. The **Save HTML…** and **Save Word…** buttons live on the **Export** tab, along with a **Plots** section that lists every plot the document produces so you can save each one individually or as a ZIP archive — see [The CalcPad Panel & Settings → Export](calcpad-panel-and-settings.md#export).

## The native menu

The menu bar drives the whole app:

- **File** — New Tab · Open… · Save · Save As… · Close Tab · Export PDF… · Quit
- **Edit** — Undo · Redo · Cut · Copy · Paste · Select All · Find · Replace
- **View** — Toggle Sidebar · Toggle Preview · Split Editor · Preview Mode: Wrapped / Unwrapped
- **Server** — Refresh (Ctrl+Alt+X) · Show Server Log · Stop Server · Restart Server
- **Help** — Documentation (opens the docs site in your default browser)

## Settings and configurations

All calculation, plot, unit, theme, editor, and linter settings live in the **Settings** tab of the sidebar. The desktop app also supports **named configurations** — save different sets of settings (e.g. one for metric with 3 decimals, one for imperial with degrees) and switch the active configuration from the Settings tab; configurations persist between sessions.

See **[The CalcPad Panel & Settings → Settings](calcpad-panel-and-settings.md#settings)** for the full list, and **[→ Formatting](calcpad-panel-and-settings.md#formatting-prettify)** for the Prettify options.

## The built-in server

The app runs a .NET calculation server bundled inside it. It starts automatically when the app launches and shuts down when the app closes — you never launch or configure it yourself.

If calculations stop working, use the **Server** menu:

- **Refresh** *(Ctrl+Alt+X)* — re-run the active document (re-lint, re-render previews, refresh plots and definitions).
- **Show Server Log** — open the server's log file to diagnose problems.
- **Stop Server** / **Restart Server** — cycle the server process.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Preview blank or not updating | **Server → Refresh**, then **Server → Restart Server** if needed. Check **Server → Show Server Log**. |
| PDF export fails | Install a Chromium browser. On Linux the app names the package to install — see [PDF Export](new-pdf-export.md). |
| Unsaved work after a crash | The app writes backup copies of unsaved files; reopen them from the Files tab. |
| Symbols not found across files | Confirm the `#include` path resolves relative to the current document. |

## See also

- [The CalcPad Panel & Settings](calcpad-panel-and-settings.md) — the shared sidebar and all settings
- [Calcpad Desktop App (architecture reference)](new-desktop-app.md)
- [Using the VS Code Extension](using-the-vscode-extension.md)
- [PDF Export](new-pdf-export.md) · [Includes](new-includes.md) · [Table of Contents](new-table-of-contents.md)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
