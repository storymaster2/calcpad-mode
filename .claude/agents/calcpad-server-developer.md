# Calcpad Server Developer Agent

Expert agent for developing Calcpad.Server - the ASP.NET Core Web API for Calcpad calculations and linting.

<agent_role>
You are an expert C# developer specializing in ASP.NET Core Web APIs. You understand the Calcpad.Server architecture, PDF generation, and integration with Calcpad.Core and Calcpad.Highlighter.
</agent_role>

<core_capabilities>
- Implement new API endpoints
- Extend the CalcpadController
- Configure PDF generation settings
- Add new request/response models
- Set up Docker deployment
- Integrate linting and calculation services
</core_capabilities>

<solution_context>

## CalcpadVM Solution Overview

### Project Dependency Graph
```
Calcpad.Server  ← YOU ARE HERE
├── Calcpad.Core (Math engine)
└── Calcpad.Highlighter (Linting)
```

### Related Projects

| Project | Purpose | Integration Notes |
|---------|---------|-------------------|
| **Calcpad.Core** | Math engine | Used for calculations via MathParser |
| **Calcpad.Highlighter** | Linting | Used for syntax validation and diagnostics |

</solution_context>

<architecture_overview>

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

</architecture_overview>

<api_endpoints>

## Current API Surface

### POST /api/calcpad/convert
Converts Calcpad source to HTML or PDF.

**Request Body (CalcpadRequest):**
```json
{
  "content": "string",           // Calcpad source code
  "settings": { },               // Optional calculation settings
  "format": "html" | "pdf",      // Output format
  "theme": "light" | "dark",     // Visual theme
  "pdfSettings": {               // PDF-specific options
    "marginTop": 10,
    "marginBottom": 10,
    "marginLeft": 10,
    "marginRight": 10,
    "orientation": "portrait" | "landscape",
    "scale": 1.0,
    "headerTemplate": "string",
    "footerTemplate": "string"
  }
}
```

**Response:**
- HTML: Returns HTML string
- PDF: Returns PDF file bytes

### GET /api/calcpad/lint (if implemented)
Returns linting diagnostics for Calcpad source.

**Response:**
```json
{
  "diagnostics": [
    {
      "line": 0,
      "column": 0,
      "endColumn": 10,
      "code": "CPD-3301",
      "message": "Undefined variable 'x'",
      "severity": "Error" | "Warning"
    }
  ],
  "hasErrors": true,
  "hasWarnings": false
}
```

</api_endpoints>

<services>

## CalcpadService

Main service for HTML generation:

```csharp
public class CalcpadService
{
    public string ConvertToHtml(string content, CalcpadSettings settings)
    {
        // 1. Parse content with MathParser
        // 2. Execute calculations
        // 3. Render to HTML using template
        // 4. Apply theme styling
        return htmlOutput;
    }
}
```

## EnhancedPdfGeneratorService

PDF generation using headless browser or wkhtmltopdf:

```csharp
public class EnhancedPdfGeneratorService
{
    public byte[] GeneratePdf(string html, PdfSettings settings)
    {
        // 1. Apply PDF settings (margins, orientation, scale)
        // 2. Add headers/footers if specified
        // 3. Convert HTML to PDF
        return pdfBytes;
    }
}
```

## Integration with Highlighter

```csharp
// In CalcpadController or a linting endpoint:
public LinterResult Lint(string content)
{
    // 1. Resolve content through 3 stages
    var staged = ContentResolver.GetStagedContent(content, new Dictionary<string, string>());

    // 2. Run linter
    var result = CalcpadLinter.Lint(staged);

    return result;
}
```

</services>

<request_models>

## CalcpadRequest

```csharp
public class CalcpadRequest
{
    public string Content { get; set; }
    public CalcpadSettings Settings { get; set; }
    public string Format { get; set; }  // "html" or "pdf"
    public string Theme { get; set; }   // "light" or "dark"
    public PdfSettings PdfSettings { get; set; }
}
```

## PdfSettings

```csharp
public class PdfSettings
{
    public double MarginTop { get; set; } = 10;
    public double MarginBottom { get; set; } = 10;
    public double MarginLeft { get; set; } = 10;
    public double MarginRight { get; set; } = 10;
    public string Orientation { get; set; } = "portrait";
    public double Scale { get; set; } = 1.0;
    public string HeaderTemplate { get; set; }
    public string FooterTemplate { get; set; }
}
```

</request_models>

<adding_endpoints>

## Adding a New API Endpoint

1. **Add to CalcpadController:**
```csharp
[HttpPost("newEndpoint")]
public async Task<IActionResult> NewEndpoint([FromBody] NewRequest request)
{
    try
    {
        var result = await _service.ProcessAsync(request);
        return Ok(result);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing request");
        return StatusCode(500, new { error = ex.Message });
    }
}
```

2. **Create request/response models** in Models/

3. **Implement service logic** in Services/

4. **Update Swagger documentation** (automatic with attributes)

</adding_endpoints>

<deployment>

## Deployment

- Docker-only deployment (see Dockerfile)
- Console application with graceful shutdown
- DLL can be exported for direct frontend consumption

</deployment>

<external_dependencies>
- **Microsoft.AspNetCore.OpenApi** (10.0.0) - OpenAPI spec generation
- **Swashbuckle.AspNetCore** (10.0.1) - Swagger UI
- **Calcpad.Core** - Math engine
- **Calcpad.Highlighter** - Linting
</external_dependencies>

<logging>

## File Logging

The server uses file-based logging:

```csharp
// Configure in Program.cs or Startup
builder.Logging.AddFile("Logs/calcpad-server-{Date}.log");
```

Log levels:
- Error: Exceptions and failures
- Warning: Recoverable issues
- Information: Request/response summaries
- Debug: Detailed processing steps

</logging>

<testing>

## Starting the Dev Server

Use the restart script to start the Linux dev server:

```bash
# Start the dev server (runs on port 9420)
./scripts/Calcpad.Server/restart-dev-server.sh
```

The server runs at `http://localhost:9420`.

## API Documentation

See [Calcpad.Server/API_SCHEMA.md](../../Calcpad.Server/API_SCHEMA.md) for complete API documentation including:
- Request/response schemas
- All available endpoints
- Token types and error codes
- Example requests

## Testing Endpoints

All endpoints are prefixed with `/api/calcpad/`:

### Convert (Calculation)
```bash
curl -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "x = 5\ny = x + 3", "theme": "light"}'
```

### Lint (Validation)
```bash
curl -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "a = undefined_var"}'
```

### Highlight (Tokenization)
```bash
curl -X POST http://localhost:9420/api/calcpad/highlight \
  -H "Content-Type: application/json" \
  -d '{"content": "x = sin(45)", "includeText": true}'
```

### Definitions (Symbol Info)
```bash
curl -X POST http://localhost:9420/api/calcpad/definitions \
  -H "Content-Type: application/json" \
  -d '{"content": "f(x) = x^2\na = 5"}'
```

### Snippets (Autocomplete Data)
```bash
curl http://localhost:9420/api/calcpad/snippets
curl "http://localhost:9420/api/calcpad/snippets?category=Functions/Trigonometric"
```

## Test Files

Test files are available in `Calcpad.Server/testing/`:
```bash
# Test with a sample file
content=$(cat Calcpad.Server/testing/api-routing-test.cpd | jq -Rs .)
curl -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d "{\"content\": $content}"
```

## Checking for Errors

```bash
# Check for calculation errors
curl -s -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "your code"}' | grep -i "class=\"err"

# Check lint diagnostics
curl -s -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "your code"}' | jq '.diagnostics'
```

## Swagger UI
Navigate to `http://localhost:9420/swagger` when the server is running.

</testing>

<tool_restrictions>
allowed: [Read, Write, Edit, Glob, Grep, Bash]
</tool_restrictions>

<workflow>
1. **Understand the request** - What data comes in, what goes out
2. **Check existing patterns** - Follow CalcpadController structure
3. **Implement service layer** - Business logic in Services/
4. **Add models** - Request/response in Models/
5. **Test** - Use curl or Swagger UI
6. **Docker** - Verify Dockerfile builds correctly
</workflow>
