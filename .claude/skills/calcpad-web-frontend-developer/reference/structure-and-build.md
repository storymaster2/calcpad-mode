# Project Structure & Build Reference

## Full Project Structure

```
Calcpad.Web/frontend/
├── calcpad-frontend/               # Shared TypeScript library
│   ├── src/
│   │   ├── index.ts                # Barrel exports (all public API)
│   │   ├── api/
│   │   │   └── client.ts           # CalcpadApiClient (fetch-based HTTP client)
│   │   ├── services/
│   │   │   ├── definitions.ts      # Variable/macro/function definitions extraction
│   │   │   ├── file-cache.ts       # File caching, #include resolution, base64 encoding
│   │   │   ├── headings.ts         # TOC heading extraction from source
│   │   │   ├── highlight.ts        # Semantic token type mapping
│   │   │   ├── linter.ts           # CalcpadLintService (debounced linting)
│   │   │   ├── server-manager.ts   # CalcpadServerManager (server lifecycle)
│   │   │   └── snippets.ts         # CalcpadSnippetService (autocomplete data)
│   │   ├── text/
│   │   │   ├── auto-indent.ts      # Auto-indentation logic (#if/#for blocks)
│   │   │   ├── operators.ts        # Operator replacement (>= → ≥, <= → ≤)
│   │   │   └── quick-type.ts       # Quick-type shortcuts (~a → α, ~b → β)
│   │   ├── types/
│   │   │   ├── api.ts              # API request/response interfaces + enums
│   │   │   ├── interfaces.ts       # ILogger, IFileSystem abstractions
│   │   │   ├── pdf-settings.ts     # PdfSettings interface + defaults
│   │   │   ├── settings.ts         # CalcpadSettings (math/plot/server/units)
│   │   │   ├── snippets.ts         # Snippet/InsertItem types
│   │   │   └── ui.ts               # UI component types
│   │   └── vue/
│   │       ├── components/
│   │       │   └── CalcpadApp.vue  # Reusable Vue component
│   │       ├── services/
│   │       │   └── messaging.ts    # Vue messaging service
│   │       └── types/
│   │           └── index.ts        # Vue-specific types
│   ├── package.json                # Peer dep: vue ^3.5.0
│   └── tsconfig.json
│
├── calcpad-web/                    # Web editor (Vite + Vue 3 + Monaco)
│   ├── src/
│   │   ├── main.ts                 # Entry point, bootstrap
│   │   ├── App.vue                 # Main layout (sidebar + editor + bottom panel)
│   │   ├── editor/
│   │   │   ├── setup.ts            # registerCalcpadLanguage(), createCalcpadEditor()
│   │   │   ├── language.ts         # Monarch tokenizer grammar
│   │   │   ├── semantic-tokens.ts  # SemanticTokensProvider (server-based)
│   │   │   ├── completions.ts      # CompletionItemProvider (snippets + symbols)
│   │   │   ├── diagnostics.ts      # Linting → Monaco markers integration
│   │   │   ├── theme.ts            # calcpad-dark theme definition
│   │   │   ├── workers.ts          # Web Worker setup for Monaco
│   │   │   └── index.ts            # Editor module barrel
│   │   ├── services/
│   │   │   ├── message-bridge.ts   # IPC for web environment
│   │   │   └── tauri-bridge.ts     # IPC for Tauri desktop (uses @tauri-apps/api)
│   │   └── styles/
│   │       └── app.css             # Global styles
│   ├── vite.config.ts              # Dev proxy to :9420
│   ├── package.json                # monaco-editor ^0.52.0, vue ^3.5.0
│   └── tsconfig.json
│
├── calcpad-desktop/                # Tauri desktop wrapper
│   ├── src-tauri/
│   │   ├── src/lib.rs              # Rust shell: window, menu, sidecar spawn, events
│   │   ├── src/main.rs             # Rust entry
│   │   ├── tauri.conf.json         # Window, bundle targets, sidecar externalBin, sign command
│   │   ├── capabilities/           # Plugin capability grants
│   │   ├── icons/                  # App icons for each platform
│   │   └── binaries/               # Staged Calcpad.Server sidecar (.gitkeep only in repo)
│   ├── stage-sidecar.sh / .ps1     # Publish Calcpad.Server → src-tauri/binaries/
│   ├── build-desktop.sh / .ps1     # Full bundle (stage + tauri build)
│   └── package.json                # devDep: @tauri-apps/cli
│
└── vscode-calcpad/                 # VS Code extension
    ├── src/
    │   ├── extension.ts            # Main extension entry (activate/deactivate)
    │   ├── adapters.ts             # VS Code API adapters
    │   ├── calcpadCompletionProvider.ts     # IntelliSense completions
    │   ├── calcpadDefinitionProvider.ts     # Go to Definition
    │   ├── calcpadDefinitionsService.ts     # Symbol extraction service
    │   ├── calcpadIncludeCompletionProvider.ts # #include file path completion
    │   ├── calcpadInsertManager.ts          # Snippets/insertion UI
    │   ├── calcpadReferenceProvider.ts      # Find References
    │   ├── calcpadRenameProvider.ts         # Rename Symbol
    │   ├── calcpadSemanticTokensProvider.ts # Semantic highlighting
    │   ├── calcpadServerLinter.ts           # Linter integration
    │   ├── calcpadServerManager.ts          # Server process lifecycle
    │   ├── calcpadSettings.ts               # VS Code settings manager
    │   ├── calcpadVueUIProvider.ts          # Webview panel (Vue sidebar)
    │   ├── commentFormatter.ts              # Formatting hotkeys
    │   └── imageInserter.ts                 # Insert Image command
    ├── CalcpadVuePanel/                     # Vue sidebar webview
    │   └── main.ts
    ├── package.json                # Extension manifest (commands, keybindings, settings, themes)
    ├── rollup.config.js            # Extension bundler
    └── tsconfig.json
```

## Build Commands

### Shared Library
```bash
cd Calcpad.Web/frontend/calcpad-frontend
npm run build     # Compile TypeScript to dist/
npm run watch     # Watch mode
```

### Web Editor
```bash
cd Calcpad.Web/frontend/calcpad-web
npm run dev       # Vite dev server on :5173 (proxies API to :9420)
npm run build     # Production build to dist/
npm run preview   # Preview production build
```

### Desktop App
```bash
cd Calcpad.Web/frontend/calcpad-desktop
./stage-sidecar.sh                # (First run / after backend changes) publish Calcpad.Server → src-tauri/binaries/
npm run dev                       # tauri dev (hot-reload Vue + rebuild Rust on change)
./build-desktop.sh                # Full bundle (stage + tauri build → src-tauri/target/release/bundle/)
```

### VS Code Extension
```bash
cd Calcpad.Web/frontend/vscode-calcpad
npm run compile    # Rollup build
npm run watch      # Watch mode (Rollup + Vue)
npm run build:vue  # Build Vue webview panel
npm run package    # Package for distribution
```

## External Dependencies

### calcpad-frontend
| Package | Purpose |
|---------|---------|
| vue ^3.5.0 | Peer dependency for Vue components |
| typescript ^5.9.0 | TypeScript compiler |

### calcpad-web
| Package | Version | Purpose |
|---------|---------|---------|
| monaco-editor | ^0.52.0 | Code editor |
| vue | ^3.5.0 | UI framework |
| @tauri-apps/api | ^2 | Desktop bridge (loaded dynamically only when window.__TAURI_INTERNALS__ is defined) |
| @tauri-apps/plugin-* | ^2 | fs, dialog, process, clipboard-manager, shell, store — same conditional-load pattern |
| vite | ^5.4.0 | Build tool / dev server |
| @vitejs/plugin-vue | ^5.0.0 | Vue SFC support |

### calcpad-desktop
| Package | Purpose |
|---------|---------|
| @tauri-apps/cli ^2 | Tauri CLI (`tauri dev` / `tauri build`) |

### vscode-calcpad
| Package | Purpose |
|---------|---------|
| calcpad-frontend | Shared library (file: link) |
| vue ^3.5.0 | Webview UI |
| rollup ^4.53.0 | Extension bundler |
| vite ^7.3.0 | Vue panel builder |
