# VS Code Extension: vscode-calcpad

Rollup-bundled extension + Vue webview panel. Depends on calcpad-frontend.

## Key Providers
| Provider | Purpose |
|----------|---------|
| `calcpadCompletionProvider` | IntelliSense with functions, variables, macros, units, snippets |
| `calcpadDefinitionProvider` | Go to Definition for user symbols |
| `calcpadReferenceProvider` | Find All References |
| `calcpadRenameProvider` | Rename Symbol across file |
| `calcpadSemanticTokensProvider` | Server-based semantic highlighting |
| `calcpadIncludeCompletionProvider` | File path completion for `#include` |

## Commands (30+)
Preview, PDF export, insert operations, formatting (bold/italic/heading/sub/super), comment toggle, and more. Defined in `package.json` contributes.commands.

## Custom Semantic Token Types
`const`, `bracket`, `lineContinuation`, `localVariable`, `macroParameter`, `units`, `setting`, `controlBlockKeyword`, `endKeyword`, `command`, `include`, `filePath`, `dataExchangeKeyword`, `htmlComment`, `tag`, `htmlContent`, `javascript`, `css`, `svg`, `input`, `format`

## Extension Settings
```json
{
    "calcpad.settings": {
        "math": { "decimals": 2, "degrees": true, ... },
        "plot": { "isAdaptive": true, "screenScaleFactor": 1.0, ... },
        "server": { "url": "http://localhost:9420", "mode": "auto" },
        "units": "m"
    }
}
```

## Adding a VS Code Command
1. **Define in package.json** contributes.commands:
```json
{ "command": "calcpad.newCommand", "title": "New Command", "category": "Calcpad" }
```

2. **Register in extension.ts**:
```typescript
context.subscriptions.push(
    vscode.commands.registerCommand('calcpad.newCommand', () => {
        // Implementation
    })
);
```
