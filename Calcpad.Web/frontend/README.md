# CalcPad Web Frontend

A monorepo containing all frontend projects for CalcPad: a VS Code extension, a standalone web editor, a desktop application, and a shared library that powers them all.

## Projects

| Directory | Description |
|-----------|-------------|
| [calcpad-frontend/](calcpad-frontend/) | Shared TypeScript library (API client, services, Vue components) |
| [vscode-calcpad/](vscode-calcpad/) | VS Code extension with live preview and full language support |
| [calcpad-web/](calcpad-web/) | Standalone web editor built with Monaco Editor + Vue |
| [calcpad-desktop/](calcpad-desktop/) | Desktop application via Neutralino.js wrapping the web editor |

All projects depend on **Calcpad.Server** (the .NET backend at `../backend/`) for computation, linting, and rendering via REST API.

---

## Quick Start

### 1. Install Calcpad.Server

**Calcpad.Server is required** for all frontend projects. Download the latest release from:

[https://github.com/imartincei/CalcpadCE/releases](https://github.com/imartincei/CalcpadCE/releases)

Or build from source:
```bash
cd ../backend
dotnet publish -c Release
```

### 2. Start the Server

Run the Calcpad.Server executable. The default port is **9420** (configurable via the `CALCPAD_PORT` environment variable).

### 3. Choose a Frontend

**VS Code Extension:**
```bash
cd vscode-calcpad
npm install
npm run package    # Build extension + Vue webview
```
Then install the generated `.vsix` file in VS Code.

**Web Editor:**
```bash
cd calcpad-web
npm install
npm run dev        # Start dev server
```

**Desktop App:**
```bash
cd calcpad-desktop
npm install
npm run dev        # Start Neutralino dev mode
```

---

## Architecture

```
calcpad-frontend/          Shared library (TypeScript + Vue components)
    ├── src/api/           HTTP client for Calcpad.Server REST API
    ├── src/services/      Linting, definitions, highlighting, snippets, file cache
    ├── src/text/          Operator replacement, quick typing, auto-indent
    ├── src/types/         TypeScript interfaces (API, settings, PDF, UI)
    └── src/vue/           Shared Vue 3 components (Insert, Settings, TOC, Variables, PDF tabs)

vscode-calcpad/            VS Code extension
    ├── src/               Extension host (language providers, server manager, preview)
    └── src/CalcpadVuePanel/  Vue 3 webview (sidebar UI)

calcpad-web/               Web editor
    ├── src/editor/        Monaco Editor setup, language, themes, completions
    └── src/services/      Message bridge between Monaco and Vue sidebar

calcpad-desktop/           Neutralino.js desktop wrapper
    └── extensions/server/ Placeholder for embedded Calcpad.Server
```

All frontends import `calcpad-frontend` as a local dependency and communicate with Calcpad.Server via the REST API documented in [API_SCHEMA.md](API_SCHEMA.md).

---

## Features

### Editor

- **Live HTML Preview** with automatic updates as you type
- **Syntax Highlighting** with 18 token types (variables, functions, macros, units, keywords, etc.)
- **Comprehensive Linting** with 40+ error codes covering:
  - Parentheses, bracket, and brace balancing
  - Control block matching (`#if`/`#end`, `#for`/`#loop`)
  - Variable and function naming validation
  - Unit checking and operator syntax
  - Keyword argument validation
- **Go to Definition** (Ctrl+Click / F12) for variables, functions, macros, and custom units
- **Find All References** (Shift+Alt+F12) across the entire file and includes
- **Rename Symbol** (F2) with project-wide renaming
- **Autocomplete** with context-aware snippets and parameter documentation
- **File Path Completion** for `#include` and `#read` directives
- **PDF Export** with configurable headers, footers, margins, and background templates

### Language

- **Inline Macros** (`#def`) with parameter substitution
- **Recursive Includes** with cycle detection, remote URLs, and API route support
- **Table of Contents** generated from heading comments

### Editing Aids

- **Operator Replacement** — automatic symbol substitution (e.g., `<=` to `≤`, `sqrt` to `√`)
- **Quick Typing** — type `~` + shortcut + space for Greek letters and symbols (e.g., `~a` -> `α`, `~theta` -> `θ`)
- **Auto-Indentation** for control blocks
- **Comment Formatting** — bold, italic, and other formatting in comment blocks

---

## VS Code Extension Details

### Installation

Download the `.vsix` file from the [releases page](https://github.com/imartincei/CalcpadCE/releases) and install in VS Code:
1. Go to Extensions view (Ctrl+Shift+X)
2. Click the "..." menu -> "Install from VSIX..."
3. Choose the downloaded `.vsix` file

### Configuration

Set the Calcpad.Server URL in VS Code settings:
```json
{
  "calcpad.server.url": "http://localhost:9420"
}
```

### Usage

1. Ensure Calcpad.Server is running
2. Open a `.cpd` file
3. Click the preview button in the editor toolbar or use `Ctrl+Shift+P` -> "CalcPad Preview"
4. Preview updates automatically as you type
5. Linting errors appear as you work

---

## Web Editor Details

The web editor provides the same features as the VS Code extension in a standalone browser environment using Monaco Editor.

### Configuration

Set the server URL via the `VITE_SERVER_URL` environment variable or through the Settings tab in the sidebar.

---

## Desktop App Details

The desktop application wraps the web editor using Neutralino.js, providing native window management and optional embedded server support. Supports macOS, Windows, and Linux.

---

## API Reference

The backend REST API is documented in [API_SCHEMA.md](API_SCHEMA.md). Key endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/calcpad/convert` | POST | Convert to HTML |
| `/api/calcpad/lint` | POST | Lint and return diagnostics |
| `/api/calcpad/definitions` | POST | Extract symbols (variables, functions, macros, units) |
| `/api/calcpad/find-references` | POST | Find all symbol occurrences |
| `/api/calcpad/highlight` | POST | Syntax tokenization |
| `/api/calcpad/snippets` | GET | Autocomplete snippets by category |
| `/api/calcpad/pdf` | POST | Generate PDF from HTML |
| `/api/calcpad/resolve-content` | POST | Resolve includes and macros |
| `/api/calcpad/refresh-cache` | POST | Clear remote content cache |
| `/api/auth/login` | POST | Authenticate (optional) |

---

## Development

### Prerequisites

- Node.js 18+
- npm
- .NET 10 SDK (for building the backend)

### Building

```bash
# Shared library (must build first)
cd calcpad-frontend && npm install && npm run build

# VS Code extension
cd vscode-calcpad && npm install && npm run package

# Web editor
cd calcpad-web && npm install && npm run build

# Desktop app
cd calcpad-desktop && npm install && npm run build
```

### Watching for Changes

```bash
# Shared library (auto-rebuild on changes)
cd calcpad-frontend && npm run watch

# VS Code extension (dev mode)
cd vscode-calcpad && npm run watch

# Web editor (dev server with HMR)
cd calcpad-web && npm run dev
```

## Requirements

- **Calcpad.Server** running at configured URL (required)
- VS Code 1.82.0+ (for the extension)
- Modern browser (for the web editor)

## License

MIT
