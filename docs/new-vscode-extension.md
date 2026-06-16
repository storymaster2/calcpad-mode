# VS Code Extension

> Part of Calcpad.Web. The features in all `new-*.md` documents apply to Calcpad.Web only — the WPF desktop application is not affected.

The Calcpad VS Code extension delivers IDE-grade language support for `.cpd` files: full symbol navigation, semantic syntax highlighting, autocomplete, hover documentation, three live preview panels, formatting hotkeys, and an embedded local server that runs the same conversion pipeline as the web backend.

## Language server features

### Go to definition

- Trigger: **Ctrl+Click** or **F12**
- Covers variables, functions, macros, and custom units
- Works across `#include` files
- Jumps to the first assignment; reassignments are not targeted

### Find all references

- Trigger: **Shift+Alt+F12** or right-click → *Find All References*
- Covers the same symbol types as Go to Definition
- Each location includes exact column and length for precise selection

### Rename symbol

- Trigger: **F2**
- Renames only locally-defined occurrences
- Does not rename across `#include` files — attempting to rename an imported symbol returns an error

### Path completion for `#include`, `#read`, `#write`, `#append`

- `#include`: `.cpd`, `.txt`
- `#read` / `#write` / `#append`: `.cpd`, `.txt`, `.csv`, `.tsv`, `.xlsx`, `.xlsm`, `.xls`
- Supports `/` and `\` path separators and drills into subdirectories
- Expands `%VAR%` (Windows) and `$VAR` (POSIX) environment variables
- Honors the `calcpad.libraryPath` setting — library files appear alongside workspace paths
- Strips `@sheet`, `type=`, `sep=` options before resolving the path

### Quick-type symbol insertion

Type `~` followed by a key and press **space** to replace with a symbol:

```text
~a   → α         ~A   → Α
~b   → β         ~p   → π
~g   → γ         ~s   → σ
~t   → θ         ~S   → Σ
```

Controlled by the `calcpad.enableQuickTyping` setting (default `true`).

### Hover provider

Rich hover tooltips for macros, functions, variables, and custom units. Content shown:

- **Macros** — signature, source file, description, parameter types & defaults
- **Functions** — signature, source file, description, return type, per-parameter docs
- **Variables** — assignment expression, inferred type, source file
- **Custom units** — definition expression, source file
- **Built-in functions** — signature, description, return type, per-parameter docs, and a runnable example

### Semantic tokens

Server-backed semantic highlighting with 31 token types: Const, Units, Operator, Variable, Function, Keyword, Command, Bracket, Comment, Tag, Input, Include, Macro, HtmlComment, Format, LocalVariable, FilePath, DataExchangeKeyword, and more. Supports incremental per-line updates.

### Completion provider

- User-defined symbols prioritized above built-ins
- Metadata JSON completion inside HTML comment blocks above definitions
- Function/macro invocation snippets with `${N:param}` placeholders
- Settings key completion (`decimals`, `degrees`, `complex`, `units`, `colorScale`, …)

### Diagnostics integration

CPD error codes surface as standard VS Code diagnostics. Severities are Error / Warning / Information. The minimum severity is filtered via `calcpad.linter.minimumSeverity` (default `information`).

## Formatting hotkeys

Content-aware formatting picks HTML or Markdown syntax based on `calcpad.commentFormat` (`auto` | `html` | `markdown`). Toggle the whole feature with `calcpad.enableFormattingHotkeys`.

| Keybinding | Effect |
|------------|--------|
| **Ctrl+B** | `**bold**` or `<b>bold</b>` |
| **Ctrl+I** | `*italic*` or `<i>italic</i>` |
| **Ctrl+U** | `<u>underline</u>` |
| **Ctrl+=** | `<sub>x</sub>` |
| **Ctrl+Shift+=** | `<sup>x</sup>` |
| **Ctrl+1** – **Ctrl+6** | Headings 1–6 |
| **Ctrl+L** | `<p>…</p>` |
| **Ctrl+R** | `<br>` |
| **Ctrl+Shift+L** | Bulleted list |
| **Ctrl+Shift+N** | Numbered list |
| **Ctrl+Q** | Toggle `'` comment prefix |

(`Cmd` instead of `Ctrl` on macOS.)

## Preview panels

Three webview previews, each opening in a new editor column:

| Panel | Command | Endpoint |
|-------|---------|----------|
| **HTML Preview** | `previewHtml` | `/api/calcpad/convert` |
| **Unwrapped Preview** | `previewUnwrapped` | `/api/calcpad/convert-unwrapped` |

Both panels:

- Re-render automatically on document change
- Apply the theme from `calcpad.previewTheme` (`light` / `dark` / `system`)
- Use `calcpad.darkBackground` (default `#1e1e1e`) for dark-mode backgrounds
- Embed local images as base64 (needed for PDF/print fidelity)

## Export and print commands

| Command | Description |
|---------|-------------|
| `exportToPdf` | Full-fidelity PDF export with save dialog and 60-second timeout |
| `printToPdf` | Print-style PDF generated from the live webview |
| `saveSourceHtml` | Renders the active document via `/api/calcpad/convert` and saves the HTML through a native save dialog. Available from the **Save HTML…** button on the sidebar's **Export** tab and from the Command Palette as *CalcPad: Save Source HTML…* |
| `saveDocx` | Renders the active document, then converts to Word `.docx` via `/api/calcpad/docx` (Calcpad.OpenXml). Available from the **Save Word…** button on the sidebar's **Export** tab and from the Command Palette as *CalcPad: Save as Word Document…* |
| `viewWebviewSource` | Opens the rendered HTML in a scratch editor for debugging |
| `insertImage` | File picker to insert an `<img>` tag with relative path |

## Calcpad sidebar

A dedicated activity-bar view (`calcpadVueUI`) displays the document's:

- Macros (with parameters and defaults)
- User-defined functions (with return type and signature)
- Variables (with inferred type)
- Custom units

Includes **Refresh Document** and **Stop Server** buttons in the view title bar.

## Server lifecycle management

The extension bundles a local Calcpad server and manages its lifecycle automatically:

- **Modes** — `calcpad.server.mode` = `auto` (default) | `local` | `remote`
- **URL** — `calcpad.server.url` (default `http://localhost:9420`)
- **dotnet path** — `calcpad.server.dotnetPath` (default `dotnet`)
- **Auto-start** — Local server launches in the background on activation
- **Auto-restart** — Up to 3 crash retries before requiring manual refresh
- **Health fallback** — If local start fails, the extension falls back to the configured remote URL
- **Clean shutdown** — Server process is terminated when the window closes
- **Manual control** — `calcpad.stopServer` command

## Output channels

Four independent VS Code output channels:

| Channel | Purpose |
|---------|---------|
| **CalcPad Extension** | Extension lifecycle, command execution, errors |
| **Calcpad Output HTML** | Rendered HTML diagnostics |
| **Calcpad Webview Console** | `console.log` intercepted from preview webviews |
| **CalcPad Server Debug** | stdout/stderr from the spawned server process |

## Calculation & plot settings

All Calcpad runtime settings are surfaced under `calcpad.settings`:

- **Math** — `decimals` (0–15), `degrees` (0 radians / 1 degrees / 2 gradians), `isComplex`, `substitute`, `formatEquations`, `zeroSmallMatrixElements`, `maxOutputCount`, `formatString`
- **Plot** — `isAdaptive`, `screenScaleFactor`, `imagePath`, `imageUri`, `vectorGraphics`, `colorScale`, `smoothScale`, `shadows`, `lightDirection`

These are forwarded to the server in every conversion request.

## Library path for shared files

`calcpad.libraryPath` points at a directory of reusable `.cpd`/`.txt` files. Files under that directory show up alongside workspace files in `#include` / `#read` path completion, letting teams share a common library without copying into each project.

## Paste as comment

`pasteAsComment` pastes clipboard content with each line prefixed by `'`, useful when copying text from docs or emails into a Calcpad document.

## Operator replacer and auto-indenter

Typing standard ASCII operators triggers automatic replacement with Unicode equivalents (`<=` → `≤`, `>=` → `≥`, `!=` → `≠`). The auto-indenter handles `#if`/`#else`/`#end if`, `#for`/`#end for`, and `#def`/`#end def` block indentation.