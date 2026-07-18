# Shared Library: calcpad-frontend

The shared library is the core dependency for all three frontends (web editor, Tauri desktop, VS Code extension).

## CalcpadApiClient
Unified fetch-based HTTP client (works in Node.js 18+, Electron, browsers):
```typescript
class CalcpadApiClient {
    constructor(baseUrl: string, logger?: ILogger);
    setBaseUrl(url: string): void;
    getBaseUrl(): string;

    // API methods
    lint(content: string, clientFileCache?: ClientFileCache): Promise<LintResponse | null>;
    highlight(content: string, includeText?: boolean, clientFileCache?: ClientFileCache): Promise<HighlightToken[] | null>;
    definitions(content: string, clientFileCache?: ClientFileCache): Promise<DefinitionsResponse | null>;
    findReferences(content: string, clientFileCache?: ClientFileCache): Promise<FindReferencesResponse | null>;
    snippets(): Promise<SnippetsResponse | null>;
    convert(content: string, settings: unknown, outputFormat?: string): Promise<ArrayBuffer | string | null>;
    checkHealth(): Promise<boolean>;
}
```

## Key Types (types/api.ts)
```typescript
type ClientFileCache = Record<string, string>;  // filename → base64 content

interface LintResponse {
    errorCount: number;
    warningCount: number;
    diagnostics: LintDiagnostic[];
}

interface HighlightToken {
    line: number;      // 0-based
    column: number;    // 0-based
    length: number;
    type: string;
    typeId: number;    // CalcpadTokenType enum
    text?: string;
}

interface DefinitionsResponse {
    macros: MacroDefinition[];
    functions: FunctionDefinition[];
    variables: VariableDefinition[];
    customUnits: CustomUnitDefinition[];
}

enum CalcpadTokenType {
    None = 0, Const = 1, Operator = 2, Bracket = 3, LineContinuation = 4,
    Variable = 5, LocalVariable = 6, Function = 7, Macro = 8,
    MacroParameter = 9, Units = 10, Setting = 11, Keyword = 12,
    ControlBlockKeyword = 13, EndKeyword = 14, Command = 15,
    Include = 16, FilePath = 17, DataExchangeKeyword = 18,
    Comment = 19, HtmlComment = 20, Tag = 21, HtmlContent = 22,
    JavaScript = 23, Css = 24, Svg = 25, Input = 26, Format = 27
}
```

## CalcpadSettings (types/settings.ts)
```typescript
interface CalcpadSettings {
    math: { decimals: number; degrees: boolean; isComplex: boolean; substitute: number; formatEquations: boolean; /* ... */ };
    plot: { isAdaptive: boolean; screenScaleFactor: number; colorScale: string; shadows: boolean; lightDirection: string; /* ... */ };
    server: { url: string; mode: 'auto' | 'local' | 'remote'; };
    units: 'm' | 'i' | 'u';
}
```

## Services
| Service | Purpose |
|---------|---------|
| `CalcpadLintService` | Debounced linting via API, returns diagnostics |
| `CalcpadDefinitionsService` | Symbol extraction (variables, functions, macros, units) |
| `CalcpadSnippetService` | Autocomplete snippet data from server |
| `CalcpadServerManager` | Server process lifecycle (start, stop, health check) |

## Text Processing
| Module | Purpose |
|--------|---------|
| `operators.ts` | Replaces `>=` → `≥`, `<=` → `≤`, `!=` → `≠`, etc. |
| `quick-type.ts` | Replaces `~a` → `α`, `~b` → `β`, `~p` → `π`, etc. |
| `auto-indent.ts` | Auto-indent after `#if`, `#for`, `#def`; dedent on `#end` |
| `file-cache.ts` | Build base64 file cache from workspace files for `#include` |
