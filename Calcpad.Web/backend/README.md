# Calcpad.Web Backend (Local Mode)

> **Localhost-only build.** This branch (`calcpad-web`) only supports running the server bound to a loopback address. The startup loopback guard in [Program.cs](Program.cs) throws `InvalidOperationException` if the resolved bind URL is anything other than `localhost`, `127.0.0.0/8`, or `::1`. Multi-user / hosted / Docker deployment, auth, file storage, and per-user caching live on the `calcpad-experimental` branch.

The Calcpad.Web backend is an ASP.NET Core Web API that powers the standalone web editor, the VS Code extension, and the Tauri desktop wrapper. It exposes conversion, linting, highlighting, definitions, find-references, snippet, and PDF endpoints over a single local HTTP listener.

## Running

```bash
cd Calcpad.Web/backend
dotnet run
```

The default bind URL is `http://localhost:9420`. Override the port with the `CALCPAD_PORT` environment variable, or override the full bind URL with `--urls` — as long as it still resolves to a loopback address.

```bash
CALCPAD_PORT=9500 dotnet run
dotnet run --urls http://localhost:9500
```

Any attempt to bind to a non-loopback host (`0.0.0.0`, a LAN address, a domain name) is rejected at startup with `InvalidOperationException`.

## Health check

```
GET  /api/calcpad/pdf/health      → { "status": "ok", "service": "calcpad-pdf", ... }
```

## Endpoints

Documented in [API_SCHEMA.md](API_SCHEMA.md). Summary:

- `POST /api/calcpad/convert`, `/convert-unwrapped` — Calcpad source to HTML
- `POST /api/calcpad/docx`, `/pdf` — document export (`/pdf/health` for readiness)
- `GET  /api/calcpad/sample` — sample document
- `POST /api/calcpad/highlight`, `/highlight-line` — tokenization
- `POST /api/calcpad/lint` — diagnostics with CPD codes
- `POST /api/calcpad/definitions`, `/find-references` — symbol index
- `GET  /api/calcpad/snippets` — autocomplete catalog
- `POST /api/calcpad/prettify` — pretty-print Calcpad source
- `GET  /api/calcpad/debug-crash` — write a crash event from the client to the disk log

## Configuration

`appsettings.json` carries logging configuration and the bind URL. There are no JWT, Auth, Storage, or S3 sections on this branch.

| Variable | Default | Description |
|----------|---------|-------------|
| `CALCPAD_PORT` | `9420` | Bind port (host is always loopback) |
| `ASPNETCORE_URLS` / `--urls` | `http://localhost:9420` | Override the full bind URL (must resolve to loopback) |
