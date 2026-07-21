# PDF Export

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

The PDF pipeline uses **PuppeteerSharp** (headless Chromium) for HTML rendering, plus **PDFsharp** for post-processing headers, footers, and background overlays.

## Rendering pipeline

1. Launch headless browser (singleton, locked with a semaphore)
2. Create page and set HTML content (`WaitUntil: Networkidle0`)
3. Inject print-specific CSS to force color-accurate output and fit datagrid tables
4. Transform DOM via JavaScript (convert inputs to underlined text, etc.)
5. Generate PDF bytes with Puppeteer
6. Post-process with PDFsharp (headers, footers, background) if enabled

## Supported paper formats

Letter, Legal, Tabloid, Ledger, **A0, A1, A2, A3, A4, A5, A6**. Default: A4. Unrecognized values fall back to A4.

## Margins and orientation

- Margins accept CSS unit strings: `"2cm"`, `"1.5cm"`, `"0.5in"`
- `orientation: "portrait"` (default) or `"landscape"`
- Independent per-edge margin control: `marginTop`, `marginRight`, `marginBottom`, `marginLeft`

## Headers (when `enableHeader: true`)

Laid out by PDFsharp:

- Document **title** (top-left, bold 12pt)
- Document **subtitle** (below title, gray)
- **Center text** from `headerCenter`
- **Timestamp** (top-right, 8pt, light gray) formatted per `dateTimeFormat` (defaults to `"g"`)
- 1px gray separator line below the header

## Footers (when `enableFooter: true`)

- **Left** — author and company
- **Center** — custom `footerCenter` text
- **Right** — page numbers (`Page N of Total`) and project field
- 1px gray separator line above the footer

## Background PDF overlay

```text
options.backgroundPdf = "C:/templates/letterhead.pdf";
```

PDFsharp loads the background and draws it **behind** each page, stretching to fit page dimensions.

## Image base64 embedding

The VS Code extension scans generated HTML for `<img src="…">` and, for each local file path (not `http://`, `https://`, or `data:`), embeds the file as a base64 data URI before sending to `/api/calcpad/pdf`. Supported MIME types: `png`, `jpg`/`jpeg`, `gif`, `webp`, `svg`. Required because headless Chromium cannot read from the client's disk.

## Browser detection order

1. Explicit `browserPath` (request field, env var, or `appsettings.json`)
2. **Microsoft Edge** (Windows: Program Files, Program Files (x86), LocalAppData)
3. **Google Chrome** (same three locations)
4. **Linux** — `chromium`, `chromium-browser`, `ungoogled-chromium`, `google-chrome`, `google-chrome-stable`, `/snap/bin/chromium`
5. **macOS** — Chrome, Edge, Chromium in `/Applications`
6. **Fallback** — auto-download `ChromeHeadlessShell` via `BrowserFetcher` into `{AppContext.BaseDirectory}/chromium`

## Desktop pre-flight check (Calcpad-Desktop)

The Neutralino desktop launcher (`extensions/server/start-server.sh`) probes for a Chromium binary on `PATH` at startup and writes the result to `extensions/server/logs/server-stderr.log`. Before issuing a PDF request, the desktop UI scans that log; if the launcher reported "no Chromium-family browser found" (or PuppeteerSharp logged a launch failure on a previous attempt), the user gets a native message box with **per-distribution install instructions** instead of waiting for a 10-second timeout:

- **Arch / CachyOS / Manjaro / EndeavourOS / Garuda** — `yay -S ungoogled-chromium-bin` (or `sudo pacman -S chromium`)
- **Debian / Ubuntu / Mint** — `sudo apt install chromium`
- **Fedora / RHEL / Rocky / Alma** — `sudo dnf install chromium`
- **openSUSE** — `sudo zypper install chromium`
- **Alpine** — `sudo apk add chromium`
- **macOS** — `brew install --cask google-chrome`
- **Windows** — install Microsoft Edge or Google Chrome

Distribution detection reads `ID` and `ID_LIKE` from `/etc/os-release`. The Arch packaging (`packaging/arch/PKGBUILD`) lists `ungoogled-chromium-bin` as the preferred `optdepend`.

## Server stderr log

The desktop launcher tees the .NET server's stderr into `extensions/server/logs/server-stderr.log` (truncated per launch, preserved on the original stderr stream too). The desktop app's **Server → Show Server Log** menu item — and any failed PDF request — pipes the most recent 200 lines into the **Output** panel under the **Server** channel, mirroring how the VS Code extension's spawned-process stderr surfaces in its Output channel.

## Complete `PdfOptions` reference

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `format` | string | `"A4"` | Paper format |
| `orientation` | string | `"portrait"` | `portrait` or `landscape` |
| `printBackground` | bool | `true` | Render background colors/images |
| `scale` | float | `1.0` | Zoom factor (0.1 – 2.0) |
| `marginTop` | string | `"2cm"` | Top margin |
| `marginRight` | string | `"1.5cm"` | Right margin |
| `marginBottom` | string | `"2cm"` | Bottom margin |
| `marginLeft` | string | `"1.5cm"` | Left margin |
| `enableHeader` | bool | `false` | Render header |
| `enableFooter` | bool | `false` | Render footer |
| `documentTitle` | string? | `null` | Title (header, bold) |
| `documentSubtitle` | string? | `null` | Subtitle (header, gray) |
| `author` | string? | `null` | Author (footer left) |
| `company` | string? | `null` | Company (footer left) |
| `project` | string? | `null` | Project (footer right) |
| `headerCenter` | string? | `null` | Custom center header text |
| `footerCenter` | string? | `null` | Custom center footer text |
| `dateTimeFormat` | string? | `null` | .NET format string (null → `"g"`) |
| `backgroundPdf` | string? | `null` | Path to background PDF |

## VS Code export flow

Command `vscode-calcpad.exportToPdf`:

1. Read `calcpad.pdf.*` settings
2. Build a `ClientFileCache` from the document's `#include`/`#read` references
3. `POST /api/calcpad/convert` → HTML
4. Embed local images as base64
5. `POST /api/calcpad/pdf` with 60-second timeout
6. Show save dialog and write the PDF
7. Offer to open the saved file

## Health check

`GET /api/calcpad/pdf/health` returns:

```json
{ "status": "ok", "service": "calcpad-pdf", "version": "2.0.0" }
```

## NoPrint regions

Mark sections of a Calcpad source file to be excluded from PDF output (while remaining visible in the on-screen preview) using paired HTML-comment markers:

```text
'<!--{"NoPrintStart": true}-->
'These lines are visible in the preview but stripped from the PDF.
debug_x = 5
debug_y = x + 1
'<!--{"NoPrintEnd": true}-->
```

The markers reuse the existing JSON-payload comment syntax already used by `LintIgnore`/`EndLintIgnore` and per-file `settings`.

**Behavior:**

- Marker lines themselves are also removed
- Pairing is stack-based; nested regions collapse to the outermost on close
- An unmatched `NoPrintStart` strips through end-of-file
- Property-name matching is case-insensitive
- The JSON value of each marker is unused; only the property's presence matters

**Wiring:**

- The `forPrint` parameter on `CalcpadService.ConvertAsync(...)`, when `true`, runs the stripper on the source *before* macro and expression parsing, so stripped sections never enter the rendered HTML
- The `ForPrint` property on `CalcpadRequest` is threaded through `/api/calcpad/convert`, `/api/calcpad/convert-unwrapped`, and `/api/calcpad/convert-ui`
- The PDF flows in the VS Code extension and `calcpad-web` send `forPrint: true` when converting source for PDF output. On-screen preview converts default to `forPrint: false`, so NoPrint regions remain visible in the live preview
