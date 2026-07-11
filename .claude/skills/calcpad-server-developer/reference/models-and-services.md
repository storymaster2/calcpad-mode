# Models & Services Reference

## API Endpoint Contracts

### POST /api/calcpad/convert
Converts Calcpad source to HTML or PDF.

**Request Body (CalcpadRequest):**
```json
{
  "content": "string",
  "settings": { },
  "format": "html" | "pdf",
  "theme": "light" | "dark",
  "pdfSettings": {
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

### POST /api/calcpad/lint
Returns linting diagnostics for Calcpad source.

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

## Services

### CalcpadService
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

### EnhancedPdfGeneratorService
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

### Integration with Highlighter
```csharp
public LinterResult Lint(string content)
{
    var staged = ContentResolver.GetStagedContent(content, new Dictionary<string, string>());
    var result = CalcpadLinter.Lint(staged);
    return result;
}
```

## Request Models

```csharp
public class CalcpadRequest
{
    public string Content { get; set; }
    public CalcpadSettings Settings { get; set; }
    public string Format { get; set; }  // "html" or "pdf"
    public string Theme { get; set; }   // "light" or "dark"
    public PdfSettings PdfSettings { get; set; }
}

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
