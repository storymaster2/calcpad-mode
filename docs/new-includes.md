# Recursive Includes

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application. This branch (`calcpad-web`) is localhost-only — the hosted/multi-user variant of remote routing lives on `calcpad-experimental`.

`#include` and `#read` support recursive resolution and direct HTTP/HTTPS URLs.

## Recursive `#include` resolution

Included files can themselves include other files. The resolver passes a shared `visited` set through the recursion:

```text
' top.cpd
#include 'shared/constants.cpd'
#include 'shared/helpers.cpd'
```

### Depth limit

Recursion depth is capped at **20 levels**. When exceeded, the include is replaced with:

```text
' Error: Include file not provided: <filename>
```

### Circular reference detection

A case-insensitive set tracks every filename already expanded on the current path. A second attempt to include the same file is skipped — preventing infinite loops on self-referential or mutually-referential files.

## Remote URL support (HTTP/HTTPS)

Include content directly from the web:

```text
#include "https://example.com/shared-calcs.cpd"
```

- Only `http://` and `https://` are recognized — any other scheme is rejected
- Default timeout: **10 seconds** (configurable per request via `apiTimeoutMs`)
- User-Agent: `Calcpad/1.0`
- Non-2xx responses throw an error with status code and reason phrase
- Implemented by the single static helper [`Router.FetchUrlAsync`](../Calcpad.Web/backend/Services/Router.cs)

There is no `<service:endpoint>` routing layer, no JWT/auth headers, no domain allowlist, and no server-side remote-content cache on this branch.

## Source mapping and error attribution

Every expanded line tracks its origin through three maps so diagnostics trace errors back to the original file and line number, even after several layers of include and macro expansion.

## `#include` vs `#read`

| Aspect | `#include` | `#read` |
|--------|-----------|---------|
| When processed | Parse time (substituted) | Runtime (evaluated) |
| Content | Calcpad source code | CSV, TSV, Excel, JSON data |
| Scope filtering | `#local` blocks stripped | n/a |
| Output | Nested source inlined | Directive preserved; produces a matrix/vector variable |
| Remote URL support | Yes | Yes |
