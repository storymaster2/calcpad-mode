# The CalcPad Panel & Settings

> Part of Calcpad.Web. The panel described here is the same in the [VS Code extension](using-the-vscode-extension.md), the [desktop app](using-the-desktop-app.md), and the web editor — it is built from one shared set of components (`calcpad-frontend`). Where a host behaves differently, it is called out below.

The **CalcPad panel** is the tabbed sidebar that sits beside the editor. It shows what your document defines, lets you insert symbols and snippets, controls the calculation and plot settings, and drives export. Because every Calcpad front end embeds the same panel, the tabs and settings are identical everywhere; only how you open it differs:

- **VS Code** — click the **CalcPad** icon in the activity bar. The view title bar has **Refresh Document** and **Stop Server** buttons.
- **Desktop app** — **View → Toggle Sidebar**.

## Views

The panel has two top-level views, switched from the icons at its top:

- **Calcpad** — the tabbed working view (below). This is the default.
- **Files** — opens a folder and shows a file tree so you can browse and open `.cpd` files without leaving the panel. Includes *Open Folder*, *Collapse All*, and a *Show all files* toggle.

## Panel tabs

The **Calcpad** view is organized into tabs:

| Tab | What it does |
|-----|--------------|
| **Insert** | Searchable palette of symbols, built-in functions, and snippets. Click an item to insert it at the cursor. Includes an **Insert Image** button and the Symbol Palette used by quick-typing. |
| **TOC** | Live table of contents built from your document headings. Click a heading to jump to that line. |
| **Settings** | All calculation, plot, unit, theme, editor, and linter settings, plus named configurations. See [Settings](#settings). |
| **Variables** | Everything defined in the document — macros, variables, functions, and custom units — with types and signatures. Click an entry to insert it; each is searchable. |
| **PDF** | Header/footer, page size, and layout options applied when you export to PDF. |
| **Formatting** | Prettify options and the **Prettify Document** button. See [Formatting](#formatting-prettify). |
| **Export** | **Save HTML…**, **Save Word…**, and **Download all (.zip)** buttons. See [Export](#export). |
| **Errors** | Full list of calculation errors from the engine, each linking to its source line — including errors that occur inside hidden (`#hide`) regions and so never appear in the preview. |

### Insert

A searchable palette grouped by category. Typing filters the list; clicking an item inserts it at the cursor. Function and snippet entries insert with `${N:param}` placeholders you can Tab through. The **Symbol Palette** section is the same set of symbols reachable via quick-typing (`~a` + space → `α`). An **Insert Image** button opens a file picker and inserts an `<img>` tag with a relative path.

### Variables

Lists everything the current document defines, grouped and counted:

- **Macros** — with parameters and defaults
- **Variables** — with inferred type
- **Functions** — with signature and return type
- **Custom Units** — with their definition

Entries are scoped to the active document (and its `#include` files). Click any entry to insert its name at the cursor.

### TOC

A live outline of the headings in your document. Selecting a heading scrolls the editor to that line. It rebuilds as you edit.

### Formatting (Prettify)

Controls the **Prettify Document** command, which reformats the active file:

- **Indent style** — Tab or Space
- **Spaces per level** — used when the indent style is Space
- **Trim trailing whitespace**

Set your options, then click **Prettify Document**.

### PDF

Configures the PDF export before you run it:

- **Header** — document title (defaults to the file name) and a timestamp format (e.g. `M/d/yyyy h:mm tt`)
- **Page Layout** — page size (Letter, …) and orientation

Use **Generate PDF** to export, or **Reset** to restore the defaults.

### Export

Three actions that render the current document through the backend and save the result:

| Button | Result |
|--------|--------|
| **Save HTML…** | Saves a standalone `.html` of the rendered report. |
| **Save Word…** | Converts the report to a Word `.docx` (via Calcpad.OpenXml). |
| **Download all (.zip)** | Bundles the files produced by any `#write` / `#append` commands. |

On the desktop app these use native save dialogs; in the browser build they download as blobs.

### Errors

Lists every error the calculation engine reports, each with its source line and a link that jumps there. This is the reliable place to see errors that occur inside `#hide` blocks, which are omitted from the rendered preview.

## Settings

The **Settings** tab is the single place to control the calculation engine and the editor. Editing settings here keeps them in sync with the host and the server — in VS Code, do **not** use VS Code's own settings editor for these.

### Math

| Setting | Values | Meaning |
|---------|--------|---------|
| **Decimals** | 0–15 | Decimal places shown in results. |
| **Degrees** | 0–360 | Trigonometric angle setting. |
| **Complex Numbers** | on/off | Enable complex-number arithmetic. |
| **Substitute Variables** | on/off | Substitute variable values into the output. |
| **Format Equations** | on/off | Render equations in formatted math rather than plain text. |

### Plot

| Setting | Values | Meaning |
|---------|--------|---------|
| **Adaptive Plotting** | on/off | Adaptively sample plotted functions. |
| **Screen Scale Factor** | 0.1–5 | Scale of rendered plots/images. |
| **Image Path** | text | Directory used for generated plot images. |
| **Vector Graphics** | on/off | Emit SVG plots instead of raster images. |
| **Color Scale** | Rainbow / Grayscale / Hot / Cool / Jet / Parula | Palette for 3D/surface plots. |
| **Smooth Scale** | on/off | Smooth the color scale. |
| **Shadows** | on/off | Render shadows on 3D surfaces. |
| **Light Direction** | text | Light direction vector for 3D shading. |

### Units

**Units System** — SI (International System) / Imperial / US Customary.

### Server

**Remote Server URL** — the address used when the host is configured to talk to a remote Calcpad server rather than a local one.

### Preview theme

- **Theme** — System / Light / Dark for the rendered preview.
- **Dark Mode Background** — the background color used in dark mode (default `#1e1e1e`), with a **Reset** button.

### Color theme

**Color Theme** — the syntax-highlighting theme, defaulting to *System* with the available dark and light themes grouped in the list.

### Editor features

- **Enable Quick Typing** — `~`-prefixed shortcuts expand to symbols (e.g. `~a` → `α`, `~'` → `′`).
- **Comment Format** — Auto (detect `#md` on/off) / HTML / Markdown; controls what the formatting hotkeys emit.
- **Enable Formatting Hotkeys** — the Ctrl+B / Ctrl+I / Ctrl+1–6 … bindings.
- **Sync Preview to Cursor Line** — scroll the preview to follow the line the cursor is on.

### Library

**Library Path** — a directory of shared `.cpd`/`.txt` files that appear in `#include` / `#read` path completion, so a team can share a common library. Supports `%ENV%` variables.

### Linter

**Minimum Severity** — Error / Warning / Information (all). The lowest severity surfaced as a diagnostic.

### Diagnostics

**Open Logs Folder** — opens the folder holding server logs and the most recent crash dump.

## Named configurations

The **Configuration** section lets you keep more than one named set of settings — for example a metric configuration with 3 decimals and an imperial one in degrees — and switch between them:

- **Active Config** — pick the configuration to apply.
- **Save current settings as** — type a name and click **Save** to store the current settings under it.
- **Open Settings Folder** — reveal where configurations are stored.
- **Reset to Default** — restore the default settings.

Configurations persist between sessions in the VS Code extension and the desktop app. (The pure browser build does not yet mirror named configs.)

## See also

- [Using the VS Code Extension](using-the-vscode-extension.md)
- [Using the Desktop App](using-the-desktop-app.md)
- [PDF Export](new-pdf-export.md) · [Linter](new-linter.md) · [Table of Contents](new-table-of-contents.md)
