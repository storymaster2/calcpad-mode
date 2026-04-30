# Calcpad.Web Backend and Deployment

> Calcpad.Web only. The WPF desktop application is unaffected.

The web backend (`Calcpad.Web/backend`) is an ASP.NET Core Web API that drives the web editor, the VS Code extension, and the Neutralino desktop wrapper. It replaces the former `Calcpad.Server` project with clearer separation of concerns and an explicit endpoint catalog.

## Endpoint catalog

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/calcpad/convert` | POST | HTML conversion with theme + settings |
| `/api/calcpad/convert-unwrapped` | POST | Raw code HTML for error navigation |
| `/api/calcpad/convert-ui` | POST | HTML with interactive `#UI` overrides applied |
| `/api/calcpad/debug-raw-code` | POST | Macro-expanded source (debugging aid) |
| `/api/calcpad/sample` | GET | Retrieve a sample document |
| `/api/calcpad/pdf` | POST | Generate a PDF |
| `/api/calcpad/pdf/health` | GET | PDF service health check |
| `/api/calcpad/docx` | POST | Generate a Word `.docx` from the source — runs the calcpad → HTML pipeline (`forPrint: true`) and feeds it through `Calcpad.OpenXml.OpenXmlWriter` |
| `/api/calcpad/resolve-content` | POST | Three-stage content resolution, all stages returned |
| `/api/calcpad/highlight` | POST | Tokenize full document for syntax highlighting |
| `/api/calcpad/highlight-line` | POST | Tokenize a single line (incremental) |
| `/api/calcpad/lint` | POST | Semantic diagnostics with CPD codes |
| `/api/calcpad/definitions` | POST | Macros, functions, variables, units, UI overrides |
| `/api/calcpad/find-references` | POST | Full symbol occurrence index |
| `/api/calcpad/snippets` | GET | Snippets by category |
| `/api/calcpad/refresh-cache` | POST | Invalidate remote content cache |

A machine-readable API schema lives at [Calcpad.Web/backend/API_SCHEMA.md](../Calcpad.Web/backend/API_SCHEMA.md).

## Common request fields (`CalcpadRequest`)

- `content` — Calcpad source code
- `settings` — math/plot/units configuration
- `theme` — `"light"` or `"dark"`
- `clientFileCache` — base64-encoded file contents keyed by path
- `authSettings` — JWT token and routing config
- `apiTimeoutMs` — remote fetch timeout (default `10000`)
- `sourceFilePath` — client-side file path for resolving relative `#include`
- `forPrint` — when `true`, NoPrint regions are stripped before conversion (used by PDF flows)

`CalcpadUiRequest` adds `uiOverrides: Dictionary<string, string>` and forces `settings.EnableUi = true` server-side.

## Definitions response shape

Returns four parallel arrays plus persisted UI overrides:

- `macros[]` — name, parameters, isMultiline, content, source, description, paramTypes, paramDescriptions, defaults
- `functions[]` — name, parameters, expression, returnType, returnTypeId, hasCommandBlock, commandBlockType, commandBlockStatements, defaults
- `variables[]` — name, expression, type, typeId
- `customUnits[]` — name, expression
- `uiOverrides?` — `{ overrides: Dictionary<string,string>, commentLine: number }`

`typeId` values: 0 Unknown, 1 Value, 2 Vector, 3 Matrix, 4 StringVariable, 5 Various, 6 Function, 7 InlineMacro, 8 MultilineMacro, 9 CustomUnit.

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

## Refresh-cache request

```typescript
{ key?: string, keys?: string[] }  // null/empty → clear entire cache
```

## API router service

API routing lives in `Calcpad.Web/backend/Services/Router.cs`:

- Parses `<service:endpoint>` syntax
- Supports JWT-authenticated requests via `AuthSettings`
- Configurable endpoint mappings via `RoutingConfig` (snake_case JSON)
- Auto-selects GET vs POST based on body contents

See [new-includes.md](new-includes.md) for the routing config structure.

## Disk caching and performance

- **In-memory remote cache** — `ConcurrentDictionary` shared across requests, keyed by URL or `<service:endpoint>` token
- **Disk cache offload** — files larger than ~1 MB are written to `{AppContext.BaseDirectory}/cache/` as SHA-256-keyed `.cache` files
- **Cleanup service** — runs every hour, deletes any `.cache` file older than 24 hours
- **Base64 truncation for lint** — embedded base64 blobs above a size threshold are truncated before lint analysis to prevent memory bloat (the original content is preserved for rendering)
- **Remote pre-fetching** — all remote `#include` / `#read` targets are fetched in parallel before the main conversion

## Content resolution pipeline

A three-stage pipeline in `Calcpad.Highlighter` processes files uniformly for the linter, definitions endpoint, and highlight endpoint. Each stage emits an artifact plus a source map pointing back to the previous stage.

- **Stage 1 — Line continuation.** Explicit (` _`) and implicit (unbalanced delimiters at end-of-line) merging. Produces a `LineContinuationMap` and per-segment column tracking for accurate error positions.
- **Stage 2 — Include resolution & macro collection.** Recursive `#include` substitution, then macro definition tokenization. Computes macro definitions, duplicates, parameter order (for keyword-arg validation), bodies, and which parameters are referenced inside quoted/comment content.
- **Stage 3 — Macro expansion & type tracking.** Removes macro definitions, expands every call. Builds variable/function/macro indices, tracks reassignments and outer-scope (`←`) assignments, runs type inference.

`SourceMapper.MapLineBack()` chains the three source maps plus the include map and macro-expansion map to resolve any Stage 3 line/column back to the original file, original line, and source kind (`local` vs `include`).

## Storage backends (S3)

The web backend integrates with S3-compatible storage for shared file libraries.

- **Default Docker backend** — [Garage](https://garagehq.deuxfleurs.fr/) with native bucket versioning
- **Client SDK** — `AWSSDK.S3` (replaces the previous MinIO client)
- **External S3** — any S3-compatible provider (AWS, MinIO, Cloudflare R2, etc.) can be configured via the same env vars

The migration to AWSSDK.S3 unifies the client API across providers; Garage is the default in the bundled Docker compose stack because of its bucket versioning and lighter footprint.

## Crash logging

The backend writes unexpected exits and orphaned-server scenarios to disk via `FileLogger` and the server-manager. Previously these failures were silently lost; they now surface in the `CalcPad Server Debug` output channel and on disk so the VS Code extension can present them to the user.

## To-do (deployment)

- File-size and rate limits on API endpoints (DDoS hardening)
- File-size exception handling on upload endpoints
- CalcpadAuth SSO for accessing API resources (S3, etc.)
- Docker config switches between MinIO, Garage, or external S3
- Password / OAuth in Docker
- Optional Cloudflare tunnel config in Docker

These are tracked in [Calcpad.Web/To-Do/To-Do.md](../Calcpad.Web/To-Do/To-Do.md).
