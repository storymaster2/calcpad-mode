# Structure, Config & Deployment Reference

## Project Structure

```
Calcpad.Web/backend/
├── Controllers/
│   ├── CalcpadController.cs        # Main API controller (convert, lint, highlight, definitions, snippets, PDF)
│   ├── AuthController.cs           # Authentication endpoints (register, login)
│   └── UserController.cs           # User management
├── Services/
│   ├── CalcpadApiService.cs        # Shared app builder configuration (DI, CORS, Auth, middleware)
│   ├── CalcpadService.cs           # Core conversion/calculation logic (HTML generation, caching)
│   ├── PdfGeneratorService.cs      # PDF generation (PuppeteerSharp browser pool + PDFsharp)
│   ├── AuthService.cs              # JWT token generation, user auth
│   ├── IncludeResolver.cs          # Resolves #include directive file paths
│   └── Router.cs                   # API route configuration
├── Models/
│   ├── Auth/
│   │   ├── User.cs                 # User entity
│   │   ├── UserRole.cs             # Role enum
│   │   └── AuthDtos.cs             # LoginRequest, RegisterRequest, AuthResponse
│   └── Pdf/
│       └── PdfOptions.cs           # PDF format, orientation, margins, headers/footers
├── Data/
│   └── CalcpadAuthDbContext.cs     # EF Core SQLite context (data/users.db)
├── Program.cs                      # Console entry point, graceful shutdown
├── FileLogger.cs                   # File-based crash/error logging
├── template.html                   # HTML output template for rendered calculations
├── appsettings.json                # JWT, database path, logging config
├── Calcpad.Server.csproj           # .NET 10 project (v7.6.1)
├── Calcpad.Server.sln
├── Dockerfile
├── docker-compose.yml
├── scripts/
│   ├── restart-dev-server.sh       # Start/restart dev server
│   ├── build-linux.sh              # Linux build script
│   ├── build-linux-console.sh      # Linux console build
│   ├── build-slim-bundle.sh        # Slim bundle build (Linux)
│   ├── build-slim-bundle.ps1       # Slim bundle build (Windows)
│   └── deploy-slim-bundle.ps1      # Deploy slim bundle
└── testing/                        # Test .cpd files
```

## Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `CALCPAD_PORT` | `9420` | Server listening port |
| `CALCPAD_HOST` | `0.0.0.0` | Server bind address |
| `CALCPAD_ENABLE_HTTPS` | (unset) | Enable HTTPS |
| `Auth:Enabled` | `false` | Enable JWT authentication |

## External Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.AspNetCore.OpenApi | 10.0.0 | OpenAPI spec generation |
| Swashbuckle.AspNetCore | 10.0.1 | Swagger UI |
| PuppeteerSharp | 21.1.1 | HTML-to-PDF rendering (Chromium) |
| PDFsharp | 6.2.0 | PDF post-processing |
| BCrypt.Net-Next | 4.0.3 | Password hashing |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | JWT auth |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.0 | SQLite ORM |

## Testing

### Starting the Dev Server
```bash
./scripts/Calcpad.Server/restart-dev-server.sh
# Or directly:
dotnet run --project Calcpad.Web/backend/Calcpad.Server.csproj
```

### Testing Endpoints
```bash
# Convert
curl -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "x = 5\ny = x + 3", "theme": "light"}'

# Lint
curl -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "a = undefined_var"}'

# Highlight
curl -X POST http://localhost:9420/api/calcpad/highlight \
  -H "Content-Type: application/json" \
  -d '{"content": "x = sin(45)", "includeText": true}'

# Definitions
curl -X POST http://localhost:9420/api/calcpad/definitions \
  -H "Content-Type: application/json" \
  -d '{"content": "f(x) = x^2\na = 5"}'

# Snippets
curl http://localhost:9420/api/calcpad/snippets

# PDF Health
curl http://localhost:9420/api/calcpad/pdf/health
```

### Swagger UI
Navigate to `http://localhost:9420/swagger` when the server is running.

## Deployment

- **Docker:** `Dockerfile` + `docker-compose.yml` for containerized deployment
- **Self-contained:** Single-file publish via `build-slim-bundle.sh` / `.ps1`
- **Console:** Standalone executable with graceful Ctrl+C shutdown
- **DLL:** Can be referenced directly by frontend projects (e.g., VS Code extension server manager)
