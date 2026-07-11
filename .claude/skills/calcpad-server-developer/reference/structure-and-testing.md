# Structure, Deployment & Testing Reference

## Project Structure

```
Calcpad.Web/
├── backend/                        # .NET API server (Calcpad.Server)
│   ├── Controllers/
│   │   └── CalcpadController.cs    # Main API controller
│   ├── Services/
│   │   ├── CalcpadService.cs       # Calculation service
│   │   └── CalcpadApiService.cs    # API routing service
│   ├── scripts/                    # Build scripts
│   ├── testing/                    # Test files
│   ├── Program.cs                  # Console entry point
│   ├── FileLogger.cs               # File-based logging
│   ├── template.html               # HTML output template
│   ├── Calcpad.Server.csproj
│   ├── Calcpad.Server.sln
│   ├── Dockerfile
│   └── docker-compose.yml
├── PdfService/                     # Node.js PDF generation service
└── CalcpadAuth/                    # Node.js authentication service
```

## Deployment

- Docker-only deployment (see Dockerfile)
- Console application with graceful shutdown
- DLL can be exported for direct frontend consumption

## External Dependencies

- **Microsoft.AspNetCore.OpenApi** (10.0.0) - OpenAPI spec generation
- **Swashbuckle.AspNetCore** (10.0.1) - Swagger UI
- **Calcpad.Core** - Math engine
- **Calcpad.Highlighter** - Linting

## Testing

### Starting the Dev Server
```bash
# Start the dev server (runs on port 9420)
./scripts/Calcpad.Server/restart-dev-server.sh
```

See [Calcpad.Server/API_SCHEMA.md](../../Calcpad.Server/API_SCHEMA.md) for complete API documentation.

### Testing Endpoints
```bash
# Convert (Calculation)
curl -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "x = 5\ny = x + 3", "theme": "light"}'

# Lint (Validation)
curl -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "a = undefined_var"}'

# Highlight (Tokenization)
curl -X POST http://localhost:9420/api/calcpad/highlight \
  -H "Content-Type: application/json" \
  -d '{"content": "x = sin(45)", "includeText": true}'

# Definitions (Symbol Info)
curl -X POST http://localhost:9420/api/calcpad/definitions \
  -H "Content-Type: application/json" \
  -d '{"content": "f(x) = x^2\na = 5"}'

# Snippets (Autocomplete Data)
curl http://localhost:9420/api/calcpad/snippets
```

### Swagger UI
Navigate to `http://localhost:9420/swagger` when the server is running.
