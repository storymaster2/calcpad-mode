# Using the VS Code Extension

> Part of Calcpad.Web. Everything in this guide applies to the VS Code extension only — the standalone WPF desktop application is not affected.

The **VS Code CalcPad** extension turns Visual Studio Code into a full Calcpad authoring environment. You write `.cpd` files with syntax highlighting, autocomplete, and inline error checking, then render them to a live HTML report and export to PDF, Word, or HTML — all driven by the same calculation engine as the rest of Calcpad.

This page is a task-oriented guide. For a feature-by-feature reference, see [VS Code Extension (reference)](new-vscode-extension.md). For the Calcpad language itself, start with [Writing Math](writing-math.md) and the [Quick Reference](quick-reference.md).

## Requirements

- **Visual Studio Code** 1.82 or newer.
- **.NET runtime** on your PATH. The extension bundles a local Calcpad server that it launches automatically; it needs `dotnet` to run it. Verify with `dotnet --info` in a terminal.
- A **Chromium-based browser** (Chrome, Edge, Chromium) is required for PDF export. See [PDF Export](new-pdf-export.md) for details.

## Installing the extension

The extension is distributed as a `.vsix` package (it is not yet on the Marketplace).

1. Obtain the `vscode-calcpad-<version>.vsix` file, or build it yourself from `Calcpad.Web/frontend/vscode-calcpad` with `npm run package:vsix`.
2. In VS Code, open the **Extensions** view (**Ctrl+Shift+X**).
3. Click the **⋯** menu at the top of the view → **Install from VSIX…** and select the file.
4. Reload the window if prompted.

Alternatively, from a terminal: `code --install-extension vscode-calcpad-<version>.vsix`.

## Your first document

1. Create a new file and save it with a **`.cpd`** extension (for example `beam.cpd`). VS Code will detect the `calcpad` language automatically. The `.cpdz` binary format is also recognized.
2. Type a calculation, for example:

   ```calcpad
   "Cantilever tip deflection
   P = 5kN
   L = 3m
   E = 200GPa
   I = 8.5e-5m^4
   δ = P·L^3/(3·E·I)
   ```

3. Click the **CalcPad Preview** button in the editor's top-right toolbar, or run **CalcPad Preview** from the Command Palette (**Ctrl+Shift+P**).

The preview opens in a side column and re-renders every time you edit. The bundled server starts automatically the first time you render — the first render may take a moment while it spins up.

## The editor

### Syntax highlighting

`.cpd` files get semantic highlighting driven by the server: numbers, units, operators, variables, functions, macros, keywords, commands, file paths, and embedded HTML/Markdown/CSS/JS/SVG in comments are each colored distinctly. Highlighting updates per line as you type. Colors are set by the extension for both dark and light themes.

The extension also sets a few editor defaults for `.cpd` files so the language behaves predictably:

- The monospace font is **JuliaMono** (falls back to Cascadia Code / Consolas). Run **CalcPad: Install JuliaMono Font** from the Command Palette to install it if the glyphs look wrong.
- **Enter always inserts a newline** — it never accepts a suggestion. Press **Tab** to accept a completion instead.
- Bracket matching is always on; bracket-pair colorization is off (Calcpad colors brackets itself).

### Autocomplete

As you type, the completion list offers:

- **Your own symbols first** — variables, functions, macros, and custom units defined in the current document (and its `#include` files) are prioritized above built-ins.
- **Built-in functions** with snippet placeholders — accept one and press **Tab** to jump between arguments.
- **Setting keys** (`decimals`, `degrees`, `complex`, `units`, `colorScale`, …) where a setting is expected.
- **Metadata keys** inside an HTML-comment block placed directly above a definition.

### Quick-type symbols

Type `~` followed by a key and press **space** to insert a Greek letter or math symbol. For example `~a` + space → `α`, `~p` + space → `π`, `~S` + space → `Σ`. The full set is provided by the server and shown in the **Insert** tab's Symbol Palette. Toggle this behavior with the `quickTyping` setting in the CalcPad **Settings** tab.

### Operator replacement

Typing ASCII operators auto-converts them to their Unicode equivalents: `<=` → `≤`, `>=` → `≥`, `!=` → `≠`. Multiplication and other operators follow Calcpad conventions as you type.

### Auto-indentation

Block keywords indent automatically: `#if` / `#else` / `#end if`, `#for` / `#end for`, and `#def` / `#end def`.

### Formatting hotkeys

When the cursor is in a `.cpd` file, these hotkeys wrap the selection in HTML or Markdown markup. Whether HTML or Markdown is emitted depends on the `commentFormat` setting (`auto` / `html` / `markdown`).

| Keybinding | Effect |
|------------|--------|
| **Ctrl+B** | Bold |
| **Ctrl+I** | Italic |
| **Ctrl+U** | Underline |
| **Ctrl+=** | Subscript |
| **Ctrl+Shift+=** | Superscript |
| **Ctrl+1** … **Ctrl+6** | Headings 1–6 |
| **Ctrl+L** | Paragraph |
| **Ctrl+R** | Line break |
| **Ctrl+Shift+L** | Bulleted list |
| **Ctrl+Shift+N** | Numbered list |
| **Ctrl+Q** | Toggle `'` comment prefix |
| **Ctrl+Shift+Q** | Uncomment |
| **Ctrl+Shift+V** | Paste as comment (each line prefixed with `'`) |

On macOS use **Cmd** instead of **Ctrl**. Turn the whole set off with the `enableFormattingHotkeys` setting if it conflicts with your other bindings.

## Navigating symbols

The extension provides IDE-grade navigation across variables, functions, macros, and custom units — including across `#include` files:

| Action | Trigger |
|--------|---------|
| **Go to Definition** | **Ctrl+Click** or **F12** — jumps to the first assignment |
| **Find All References** | **Shift+Alt+F12**, or right-click → *Find All References* |
| **Rename Symbol** | **F2** — renames local occurrences only (not across `#include` files) |
| **Hover** | Point at a symbol for its signature, type, source file, and docs |

Hovering over a built-in function shows its signature, description, return type, per-parameter documentation, and a runnable example.

## The CalcPad panel

Click the **CalcPad** icon in the activity bar (left edge) to open the panel. It has **Files** and **Calcpad** views, and the Calcpad view is organized into tabs (Insert, TOC, Settings, Variables, PDF, Formatting, Export, Errors). The view title bar has **Refresh Document** (re-parses the active file) and **Stop Server** buttons.

The panel is the same across every Calcpad front end — see **[The CalcPad Panel & Settings](calcpad-panel-and-settings.md)** for a full walkthrough of each tab.

## Live preview

Two preview panels are available, each opening in its own editor column:

| Panel | How to open |
|-------|-------------|
| **HTML Preview** | Preview button in the editor toolbar, or *CalcPad Preview* in the Command Palette |
| **Unwrapped Preview** | Eye button in the editor toolbar, or *CalcPad Preview Unwrapped* |

The **Unwrapped** preview shows the fully expanded source (macros and includes resolved) — useful for debugging what the engine actually computes.

Both panels:

- Re-render automatically as you type.
- Follow the `previewTheme` setting (`light` / `dark` / `system`), using `darkBackground` for the dark background color.
- Embed local images as base64 so PDF/print output matches the preview.

Right-click a preview → **View Webview Source HTML** to inspect the rendered markup (also available as *CalcPad: View Webview Source HTML*).

## Errors and diagnostics

Calcpad errors surface in VS Code's **Problems** panel (**Ctrl+Shift+M**) as standard diagnostics with Error / Warning / Information severities. Click one to jump to the offending line. The minimum severity shown is controlled by the linter minimum-severity setting.

For errors that occur inside hidden (`#hide`) regions — which don't appear in the preview — use the **Errors** tab in the sidebar to see the full list with source-line links.

## Exporting

All exports run through the same backend conversion pipeline as the preview, so what you see is what you get.

| Output | How | Notes |
|--------|-----|-------|
| **PDF** | **Export CalcPad to PDF** button in the editor toolbar, or *Export CalcPad to PDF* | Full-fidelity export with a native save dialog. Requires a Chromium browser — see [PDF Export](new-pdf-export.md). |
| **PDF (print style)** | Right-click editor → *Print CalcPad Preview to PDF* | Generated from the live webview. |
| **HTML** | **Save HTML…** on the sidebar's **Export** tab, or *CalcPad: Save Source HTML…* | Renders and saves standalone HTML via a native dialog. |
| **Word (.docx)** | **Save Word…** on the sidebar's **Export** tab, or *CalcPad: Save as Word Document…* | Renders, then converts to Word via Calcpad.OpenXml. |

Use the **PDF** tab in the panel to set the document title, timestamp format, page size, and header/footer before exporting. The **Save HTML…** / **Save Word…** buttons live on the panel's **Export** tab — see [The CalcPad Panel & Settings → Export](calcpad-panel-and-settings.md#export).

## Settings

All Calcpad settings — math, plot, units, preview and color themes, editor features, linter severity, library path, and named configurations — live in the **Settings** tab of the CalcPad panel, **not** in VS Code's normal settings editor. Editing them there keeps them in sync with the extension and the server.

See **[The CalcPad Panel & Settings → Settings](calcpad-panel-and-settings.md#settings)** for the full list.

## Path completion for includes and data files

When you type a path after `#include`, `#read`, `#write`, or `#append`, the extension completes filenames from the workspace and from your configured library path:

- `#include`: `.cpd`, `.txt`
- `#read` / `#write` / `#append`: `.cpd`, `.txt`, `.csv`, `.tsv`, `.xlsx`, `.xlsm`, `.xls`
- Both `/` and `\` separators work, and it drills into subdirectories.
- Environment variables expand: `%VAR%` on Windows, `$VAR` on POSIX.

## The bundled server

The extension runs a local Calcpad server to do all conversion and linting. You don't normally need to think about it:

- It **starts automatically** in the background when the extension activates.
- It **auto-restarts** on a crash (up to 3 retries) before asking you to refresh manually.
- It **shuts down cleanly** when the window closes.

If something goes wrong, use **Stop CalcPad Server** and then **Refresh Document** to restart it. The server mode (`auto` / `local` / `remote`), URL (default `http://localhost:9420`), and `dotnet` path are configurable if you need a custom setup.

### Output channels for troubleshooting

Four VS Code output channels help diagnose problems (open the Output panel and pick from the dropdown):

| Channel | Shows |
|---------|-------|
| **CalcPad Extension** | Extension lifecycle, commands, errors |
| **Calcpad Output HTML** | Rendered HTML diagnostics |
| **Calcpad Webview Console** | `console.log` from preview webviews |
| **CalcPad Server Debug** | stdout/stderr from the server process |

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Preview never renders / "server not ready" | Confirm `dotnet --info` works. Then **Stop CalcPad Server** → **Refresh Document**. Check the **CalcPad Server Debug** output channel. |
| PDF export fails | Install a Chromium browser (Chrome/Edge/Chromium). See [PDF Export](new-pdf-export.md). |
| Greek letters / symbols render as boxes | Run **CalcPad: Install JuliaMono Font** and reload. |
| Symbols not found across files | Confirm the `#include` path resolves; set the library path if the file lives in a shared folder. |
| Formatting hotkeys conflict with other bindings | Disable `enableFormattingHotkeys` in the Settings tab. |

## See also

- [The CalcPad Panel & Settings](calcpad-panel-and-settings.md) — the shared sidebar and all settings
- [VS Code Extension (feature reference)](new-vscode-extension.md)
- [Using the Desktop App](using-the-desktop-app.md)
- [PDF Export](new-pdf-export.md) · [Includes](new-includes.md) · [Linter](new-linter.md) · [Table of Contents](new-table-of-contents.md)
- [Writing Math](writing-math.md) · [Quick Reference](quick-reference.md)
