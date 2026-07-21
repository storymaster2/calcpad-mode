# Recursive Includes and API Routing

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

`#include` and `#read` now support recursive resolution, remote URLs, and a structured `<service:endpoint>` syntax for API calls.

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

- Only `http://` and `https://` are recognized
- Default timeout: **10 seconds** (configurable per request via `apiTimeoutMs`)
- User-Agent: `Calcpad/1.0` (static fetch) or `Calcpad-Server/1.0` (routed API calls)
- Non-2xx responses throw an error with status code and reason phrase

## `<service:endpoint>` routing syntax

Structured remote calls are written as:

```text
#include "<weather_api:forecast>{\"city\":\"Seattle\"}"
```

Parsed as `serviceName:endpointName` plus a trailing request body. The body determines the HTTP method:

- Body starts with `{` or `[` → **POST** with `Content-Type: application/json`
- Otherwise → **GET**

### Routing config structure

```json
{
  "weather_api": {
    "base_url": "https://api.weather.com",
    "auth": "jwt",
    "endpoints": {
      "current":  "/v1/current",
      "forecast": "/v1/forecast"
    }
  }
}
```

- Keys use `snake_case`
- `auth: "jwt"` causes `Authorization: Bearer <token>` to be added from the request's auth settings
- Final URL is `base_url + endpoint_template`

## Pre-fetching and caching

Before the main conversion runs, all remote `#include` and `#read` targets are fetched asynchronously in parallel.

- **Global cache** — shared across all requests, keyed by URL or `<service:endpoint>` token
- **Per-request `ClientFileCache`** — base64-encoded file contents supplied by the client
- **Disk cache** — files over ~1 MB are offloaded to `{AppContext.BaseDirectory}/cache/` as SHA-256-keyed `.cache` files
- Cache files older than 24 hours are deleted by an hourly cleanup service
- Cache cleared manually via `POST /api/calcpad/refresh-cache` (single key, multiple keys, or full flush)

## Source mapping and error attribution

Every expanded line tracks its origin through three maps so diagnostics trace errors back to the original file and line number, even after several layers of include and macro expansion.

## `#include` vs `#read`

| Aspect | `#include` | `#read` |
|--------|-----------|---------|
| When processed | Parse time (substituted) | Runtime (evaluated) |
| Content | Calcpad source code | CSV, TSV, Excel, JSON data |
| Scope filtering | `#local` blocks stripped | n/a |
| Output | Nested source inlined | Directive preserved; produces a matrix/vector variable |
| Pre-fetching | Yes | Yes |