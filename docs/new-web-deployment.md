# Calcpad.Web Local Mode

> **Localhost-only build.** This branch (`calcpad-web`) only supports running the server bound to a loopback address. The startup loopback guard in [Calcpad.Web/backend/Program.cs](../Calcpad.Web/backend/Program.cs) throws `InvalidOperationException` if the resolved bind URL is anything other than `localhost`, `127.0.0.0/8`, or `::1`. Multi-user / hosted / Docker deployment, auth, file storage, and per-user caching live on the `calcpad-experimental` branch.

> Calcpad.Web only. The WPF desktop application is unaffected.

The web backend ([Calcpad.Web/backend](../Calcpad.Web/backend)) is an ASP.NET Core Web API that drives the web editor, the VS Code extension, and the Tauri desktop wrapper. It replaces the former `Calcpad.Server` project with clearer separation of concerns and an explicit endpoint catalog — in this branch, scoped to a single local user.

## Running

```bash
cd Calcpad.Web/backend
dotnet run
```

Defaults to `http://localhost:9420`. Override the port with `CALCPAD_PORT`, or override the full bind URL via `--urls` — as long as it still resolves to a loopback address. Any non-loopback host is rejected at startup.

## Endpoint catalog

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/calcpad/convert` | POST | HTML conversion with theme + settings |
| `/api/calcpad/convert-unwrapped` | POST | Raw code HTML for error navigation |
| `/api/calcpad/sample` | GET | Retrieve a sample document |
| `/api/calcpad/debug-crash` | GET | Append a client-side crash event to the server log |
| `/api/calcpad/pdf` | POST | Generate a PDF |
| `/api/calcpad/pdf/health` | GET | PDF service health check |
| `/api/calcpad/docx` | POST | Generate a Word `.docx` — runs the calcpad → HTML pipeline (`forPrint: true`) and feeds it through `Calcpad.OpenXml.OpenXmlWriter` |
| `/api/calcpad/highlight` | POST | Tokenize full document for syntax highlighting |
| `/api/calcpad/highlight-line` | POST | Tokenize a single line (incremental) |
| `/api/calcpad/lint` | POST | Semantic diagnostics with CPD codes |
| `/api/calcpad/definitions` | POST | Macros, functions, variables, units |
| `/api/calcpad/find-references` | POST | Full symbol occurrence index |
| `/api/calcpad/prettify` | POST | Pretty-print Calcpad source |
| `/api/calcpad/snippets` | GET | Snippets by category |

The full API schema lives at [Calcpad.Web/backend/API_SCHEMA.md](../Calcpad.Web/backend/API_SCHEMA.md).

## Common request fields (`CalcpadRequest`)

- `content` — Calcpad source code
- `settings` — math/plot/units configuration
- `theme` — `"light"` or `"dark"`
- `apiTimeoutMs` — remote fetch timeout (default `10000`)
- `sourceFilePath` — client-side file path; the server uses it to resolve relative `#include` against the parent file's directory
- `forPrint` — when `true`, NoPrint regions are stripped before conversion (used by PDF flows)

## Definitions response shape

Returns four parallel arrays:

- `macros[]` — name, parameters, isMultiline, content, source, description, paramTypes, paramDescriptions, defaults
- `functions[]` — name, parameters, expression, returnType, returnTypeId, hasCommandBlock, commandBlockType, commandBlockStatements, defaults
- `variables[]` — name, expression, type, typeId
- `customUnits[]` — name, expression

`typeId` values: 0 Unknown, 1 Value, 2 Vector, 3 Matrix, 5 Various, 6 Function, 7 InlineMacro, 8 MultilineMacro, 9 CustomUnit.

## Find-references response

Three dictionaries (`variables`, `functions`, `macros`), each mapping symbol name to an array of:

```typescript
{ line, column, length, source, sourceFile?, isAssignment }
```

`isAssignment: true` indicates a definition or reassignment.

## Highlight response

Returns an array of `{ line, column, length, type, typeId, text? }`. Passing `includeText: false` (default) omits the `text` field to reduce payload.

## Snippets response

```typescript
{
  count: number,
  snippets: Array<{
    insert: string,        // § marks cursor placement
    description: string,
    label?: string,
    category: string,      // e.g. "Functions/Trigonometric"
    quickType?: string,    // e.g. "a" for ~a → α
    parameters?: Array<{ name, description? }>
  }>
}
```

Optional query string `?category=Functions/Trigonometric` filters results.

## Remote URL fetching

Remote `#include` and `#read` URLs (`http://`, `https://`) are fetched by [`Router.FetchUrlAsync`](../Calcpad.Web/backend/Services/Router.cs) — a single static helper with a 10-second default timeout (overridable per request via `apiTimeoutMs`). Non-HTTP/HTTPS URLs are rejected.

There is no API routing layer, no JWT/auth headers, no domain allowlist, and no server-side remote-content cache on this branch. The hosted version on `calcpad-experimental` keeps the `<service:endpoint>` routing, per-request `ClientFileCache`, disk-cache offload, and `/refresh-cache` endpoint that local mode no longer needs.

## Content resolution pipeline

A three-stage pipeline in `Calcpad.Highlighter` processes files uniformly for the linter, definitions endpoint, and highlight endpoint. Each stage emits an artifact plus a source map pointing back to the previous stage.

- **Stage 1 — Line continuation.** Explicit (` _`) and implicit (unbalanced delimiters at end-of-line) merging. Produces a `LineContinuationMap` and per-segment column tracking for accurate error positions.
- **Stage 2 — Include resolution & macro collection.** Recursive `#include` substitution, then macro definition tokenization. Computes macro definitions, duplicates, parameter order (for keyword-arg validation), bodies, and which parameters are referenced inside quoted/comment content.
- **Stage 3 — Macro expansion & type tracking.** Removes macro definitions, expands every call. Builds variable/function/macro indices, tracks reassignments and outer-scope (`←`) assignments, runs type inference.

`SourceMapper.MapLineBack()` chains the three source maps plus the include map and macro-expansion map to resolve any Stage 3 line/column back to the original file, original line, and source kind (`local` vs `include`).

## Crash logging

The backend writes unexpected exits and orphaned-server scenarios to disk via `FileLogger` and the server-manager. These surface in the `CalcPad Server Debug` output channel and on disk so the VS Code extension can present them to the user. The frontend reports its own crashes to the server via `GET /api/calcpad/debug-crash`.
