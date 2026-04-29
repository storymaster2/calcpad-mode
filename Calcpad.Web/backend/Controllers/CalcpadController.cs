using Calcpad.Server.Services;
using Calcpad.Server.Models.Pdf;
using Microsoft.AspNetCore.Mvc;
using Calcpad.Core;
using ServerAuthSettings = Calcpad.Server.Services.AuthSettings;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Prettifier;
using Calcpad.Highlighter.Snippets;
using Calcpad.Highlighter.Snippets.Models;
using Calcpad.Highlighter.Tokenizer;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalcpadController : ControllerBase
    {
        private readonly CalcpadService _calcpadService;
        private readonly PdfGeneratorService _pdfService;
        private static readonly LintIgnoreRegionParser _lintIgnoreRegionParser = new();

        public CalcpadController(CalcpadService calcpadService, PdfGeneratorService pdfService)
        {
            _calcpadService = calcpadService;
            _pdfService = pdfService;
        }

        /// <summary>
        /// Decodes a base64-encoded client file cache dictionary into raw bytes.
        /// Frontend sends base64 strings over JSON; this converts at the boundary.
        /// </summary>
        private static Dictionary<string, byte[]> DecodeClientFileCache(Dictionary<string, string>? base64Cache)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (base64Cache == null) return result;
            foreach (var kvp in base64Cache)
            {
                try { result[kvp.Key] = Convert.FromBase64String(kvp.Value); }
                catch (FormatException) { /* skip malformed entries */ }
            }
            return result;
        }

        [HttpPost("convert")]
        public async Task<IActionResult> ConvertToHtml([FromBody] CalcpadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return BadRequest("Content is required");
                }

                var ctx = new WebFetchContext
                {
                    ClientFileCache = DecodeClientFileCache(request.ClientFileCache),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs,
                    SourceFilePath = request.SourceFilePath
                };
                var htmlResult = await _calcpadService.ConvertAsync(request.Content, request.Settings, request.ForceUnwrappedCode, request.Theme, ctx, request.ForPrint);

                return Content(htmlResult, "text/html");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Convert request failed", ex);
                return StatusCode(500, $"Error processing Calcpad content: {ex.Message}");
            }
        }

        [HttpPost("convert-unwrapped")]
        public async Task<IActionResult> ConvertToUnwrappedHtml([FromBody] CalcpadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return BadRequest("Content is required");
                }

                var ctx = new WebFetchContext
                {
                    ClientFileCache = DecodeClientFileCache(request.ClientFileCache),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs,
                    SourceFilePath = request.SourceFilePath
                };
                var result = await _calcpadService.ConvertAsync(request.Content, request.Settings, forceUnwrappedCode: true, request.Theme, ctx, request.ForPrint);

                // Process data-text links to make them functional
                var processedResult = ProcessDataTextLinks(result);

                return Content(processedResult, "text/html");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error processing Calcpad content: {ex.Message}");
            }
        }

        [HttpPost("convert-ui")]
        public async Task<IActionResult> ConvertWithUi([FromBody] CalcpadUiRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return BadRequest("Content is required");
                }

                var ctx = new WebFetchContext
                {
                    ClientFileCache = DecodeClientFileCache(request.ClientFileCache),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs,
                    SourceFilePath = request.SourceFilePath
                };

                // Ensure EnableUi is set and pass overrides through Settings
                var settings = request.Settings ?? new Settings();
                settings.EnableUi = true;
                settings.UiOverrides = request.UiOverrides;

                var htmlResult = await _calcpadService.ConvertAsync(request.Content, settings, request.ForceUnwrappedCode, request.Theme, ctx, request.ForPrint);

                return Content(htmlResult, "text/html");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Convert-UI request failed", ex);
                return StatusCode(500, $"Error processing Calcpad UI content: {ex.Message}");
            }
        }

        [HttpPost("debug-raw-code")]
        public async Task<IActionResult> GetRawCode([FromBody] CalcpadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return BadRequest("Content is required");
                }

                var rawCode = await _calcpadService.GetRawCodeFromMacroParser(request.Content);
                return Content(rawCode, "text/plain");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error processing Calcpad content: {ex.Message}");
            }
        }

        private string ProcessDataTextLinks(string html)
        {
            // Replace links with data-text attribute to point to actual line anchors
            // Pattern: <a href="#0" data-text="123">
            // Replace with: <a href="#line-123" data-text="123">
            var pattern = @"<a\s+href=""#0""\s+data-text=""(\d+)""";
            var replacement = @"<a href=""#line-$1"" data-text=""$1""";
            return System.Text.RegularExpressions.Regex.Replace(html, pattern, replacement);
        }

        [HttpGet("sample")]
        public IActionResult GetSample()
        {
            var sampleContent = _calcpadService.GetSampleContent();
            return Ok(new CalcpadRequest { Content = sampleContent });
        }

        // Debug-only: intentionally crashes the server to verify which crash paths
        // are picked up by FileLogger / AppDomain.UnhandledException / TaskScheduler.
        // Different modes exercise different failure paths because not all of them
        // route through the same handlers (e.g. StackOverflow and FailFast bypass
        // managed exception handling entirely).
        [HttpGet("debug-crash")]
        public IActionResult DebugCrash([FromQuery] string mode = "background-thread")
        {
            FileLogger.LogInfo("debug-crash invoked", $"mode={mode}");

            switch (mode)
            {
                case "throw":
                    // Caught by ASP.NET Core's exception middleware; process keeps running.
                    // Useful baseline to confirm the request pipeline logs at all.
                    throw new InvalidOperationException("debug-crash: synchronous controller throw");

                case "background-thread":
                    // Unhandled exception on a non-pool thread -> AppDomain.UnhandledException -> process terminates.
                    new Thread(() =>
                    {
                        Thread.Sleep(100);
                        throw new InvalidOperationException("debug-crash: background thread");
                    })
                    { IsBackground = false }.Start();
                    return Accepted(new { mode, note = "Server should terminate shortly via AppDomain.UnhandledException." });

                case "unobserved-task":
                    // Fire-and-forget Task whose exception is never observed.
                    // Surfaces via TaskScheduler.UnobservedTaskException only after GC finalizes the Task.
                    _ = Task.Run(() => throw new InvalidOperationException("debug-crash: unobserved task"));
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    return Accepted(new { mode, note = "Unobserved task exception raised; process continues unless rethrown." });

                case "stackoverflow":
                    // StackOverflowException cannot be caught and bypasses UnhandledException handlers entirely.
                    // This is the "silent crash" case — expect no FileLogger entry beyond the INFO above.
                    return Recurse(0);

                case "accessviolation":
                    // Corrupted-state exception; by default not delivered to managed handlers.
                    throw new AccessViolationException("debug-crash: simulated AV");

                case "failfast":
                    // Environment.FailFast bypasses AppDomain.UnhandledException. The message is written
                    // to the Windows Event Log / stderr but FileLogger handlers do NOT run.
                    Environment.FailFast("debug-crash: FailFast invoked");
                    return StatusCode(500); // unreachable

                case "exit":
                    // Clean exit (no exception). Useful to confirm graceful-shutdown logging.
                    Task.Run(async () => { await Task.Delay(100); Environment.Exit(1); });
                    return Accepted(new { mode, note = "Environment.Exit(1) scheduled." });

                default:
                    return BadRequest(new
                    {
                        error = $"Unknown mode '{mode}'",
                        modes = new[] { "throw", "background-thread", "unobserved-task", "stackoverflow", "accessviolation", "failfast", "exit" }
                    });
            }
        }

        private static IActionResult Recurse(int depth) => Recurse(depth + 1);

        /// <summary>
        /// Generate PDF from HTML content using Playwright and PDFsharp.
        /// </summary>
        [HttpPost("pdf")]
        public async Task<IActionResult> GeneratePdf([FromBody] PdfGenerateRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Html))
                    return BadRequest(new { error = "HTML content is required" });

                FileLogger.LogInfo("PDF generation request received", $"HTML length: {request.Html.Length}");

                var pdfBytes = await _pdfService.GeneratePdfAsync(request.Html, request.Options, request.BrowserPath);

                FileLogger.LogInfo("PDF generated successfully", $"Size: {pdfBytes.Length} bytes");

                return File(pdfBytes, "application/pdf", "document.pdf");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("PDF generation failed", ex);
                return StatusCode(500, new { error = "PDF generation failed", message = ex.Message });
            }
        }

        [HttpGet("pdf/health")]
        public IActionResult PdfHealth()
        {
            return Ok(new { status = "ok", service = "calcpad-pdf", version = "2.0.0" });
        }

        /// <summary>
        /// Clears the server-side remote content cache.
        /// If keys are provided, only those entries are removed. Otherwise, the entire cache is cleared.
        /// </summary>
        [HttpPost("refresh-cache")]
        public IActionResult RefreshCache([FromBody] RefreshCacheRequest request)
        {
            if (request?.Keys != null && request.Keys.Count > 0)
            {
                foreach (var key in request.Keys)
                    CalcpadService.RemoveFromRemoteContentCache(key);
                FileLogger.LogInfo($"Remote content cache: {request.Keys.Count} entries removed");
            }
            else if (!string.IsNullOrEmpty(request?.Key))
            {
                CalcpadService.RemoveFromRemoteContentCache(request.Key);
                FileLogger.LogInfo($"Remote content cache entry removed: {request.Key}");
            }
            else
            {
                CalcpadService.ClearRemoteContentCache();
                FileLogger.LogInfo("Remote content cache cleared");
            }
            return Ok(new { status = "ok" });
        }

        [HttpPost("resolve-content")]
        public async Task<IActionResult> ResolveContent([FromBody] ContentResolverRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                FileLogger.LogInfo("Content resolution request received",
                    $"Length: {request.Content.Length}, Staged: {request.Staged}");

                // Pre-fetch remote content (URLs and API routes) into the cache
                var ctx = new WebFetchContext
                {
                    ClientFileCache = DecodeClientFileCache(request.ClientFileCache),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs
                };
                await CalcpadService.PreFetchRemoteContentAsync(request.Content, ctx);

                var resolver = new ContentResolver();
                var result = resolver.GetStagedContent(request.Content, request.IncludeFiles, ctx.ClientFileCache, request.SourceFilePath);

                return Ok(result);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Content resolution failed", ex);
                return StatusCode(500, $"Error resolving content: {ex.Message}");
            }
        }

        /// <summary>
        /// Get syntax highlighting tokens for Calcpad source code.
        /// Returns tokens with line/column positions and types for frontend colorization.
        /// </summary>
        [HttpPost("highlight")]
        public IActionResult GetHighlightTokens([FromBody] HighlightRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                FileLogger.LogInfo("Highlight request received", $"Length: {request.Content.Length}");

                var tokenizer = new CalcpadTokenizer();

                // Run content resolution to collect macro info from includes
                if (request.IncludeFiles != null || request.ClientFileCache != null)
                {
                    var clientCache = DecodeClientFileCache(request.ClientFileCache);
                    var resolver = new ContentResolver();
                    var staged = resolver.GetStagedContent(request.Content, request.IncludeFiles, clientCache, request.SourceFilePath);
                    tokenizer.SetMacroCommentParameters(
                        staged.Stage2.MacroCommentParameters,
                        staged.Stage2.MacroParameterOrder,
                        staged.Stage2.MacroBodies);
                }

                var result = tokenizer.Tokenize(request.Content);

                // Convert to response format
                var response = new HighlightResponse
                {
                    Tokens = result.Tokens.Select(t => new HighlightToken
                    {
                        Line = t.Line,
                        Column = t.Column,
                        Length = t.Length,
                        Type = t.Type.ToString(),
                        TypeId = (int)t.Type,
                        Text = request.IncludeText ? t.Text : null
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Highlight request failed", ex);
                return StatusCode(500, $"Error tokenizing content: {ex.Message}");
            }
        }

        /// <summary>
        /// Get syntax highlighting tokens for a single line (for incremental updates).
        /// </summary>
        [HttpPost("highlight-line")]
        public IActionResult GetHighlightTokensForLine([FromBody] HighlightLineRequest request)
        {
            try
            {
                if (request.Line == null)
                    return BadRequest("Line content is required");

                var tokenizer = new CalcpadTokenizer();
                var result = tokenizer.TokenizeSingleLine(request.Line, request.LineNumber);

                var response = new HighlightResponse
                {
                    Tokens = result.Tokens.Select(t => new HighlightToken
                    {
                        Line = t.Line,
                        Column = t.Column,
                        Length = t.Length,
                        Type = t.Type.ToString(),
                        TypeId = (int)t.Type,
                        Text = request.IncludeText ? t.Text : null
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Highlight line request failed", ex);
                return StatusCode(500, $"Error tokenizing line: {ex.Message}");
            }
        }

        /// <summary>
        /// Lint Calcpad source code and return diagnostics (errors and warnings).
        /// </summary>
        [HttpPost("lint")]
        public async Task<IActionResult> LintContent([FromBody] LintRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                FileLogger.LogInfo("Lint request received", "Length: " + request.Content.Length);

                // Pre-fetch remote content (URLs and API routes) into the cache
                var ctx = new WebFetchContext
                {
                    ClientFileCache = DecodeClientFileCache(request.ClientFileCache),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs
                };
                await CalcpadService.PreFetchRemoteContentAsync(request.Content, ctx);

                var resolver = new ContentResolver();
                var linter = new CalcpadLinter();

                // Resolve content with includes and client file cache
                var staged = resolver.GetStagedContent(request.Content, request.IncludeFiles, ctx.ClientFileCache, request.SourceFilePath);

                // Extract LintIgnore regions from raw source, then lint with suppression
                var ignoreRegions = _lintIgnoreRegionParser.ExtractRegions(request.Content);
                var lintResult = linter.Lint(staged, ignoreRegions);

                var response = new LintResponse
                {
                    ErrorCount = lintResult.ErrorCount,
                    WarningCount = lintResult.WarningCount,
                    Diagnostics = lintResult.Diagnostics.Select(d => new LintDiagnostic
                    {
                        Line = d.Line,
                        Column = d.Column,
                        EndColumn = d.EndColumn,
                        Code = d.Code,
                        Message = d.Message,
                        Severity = d.Severity == Highlighter.Linter.Models.LinterSeverity.Error ? "error"
                            : d.Severity == Highlighter.Linter.Models.LinterSeverity.Information ? "information" : "warning",
                        SeverityId = (int)d.Severity,
                        Source = d.Source
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Lint request failed", ex);
                return StatusCode(500, "Error linting content: " + ex.Message);
            }
        }

        /// <summary>
        /// Get detailed definitions (macros, functions, variables, custom units) from Calcpad source code.
        /// Returns type information, parameters, return types, and source locations.
        /// </summary>
        [HttpPost("definitions")]
        public async Task<IActionResult> GetDefinitions([FromBody] DefinitionsRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                FileLogger.LogInfo("Definitions request received", "Length: " + request.Content.Length);

                // Pre-fetch remote content (URLs and API routes) into the cache
                var ctx = new WebFetchContext
                {
                    ClientFileCache = DecodeClientFileCache(request.ClientFileCache),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs
                };
                await CalcpadService.PreFetchRemoteContentAsync(request.Content, ctx);

                var resolver = new ContentResolver();
                var staged = resolver.GetStagedContent(request.Content, request.IncludeFiles, ctx.ClientFileCache, request.SourceFilePath);

                var typeTracker = staged.Stage3.TypeTracker;

                // Extract persisted uiOverrides from HTML comment blocks
                PersistedUiOverridesDto? persistedUiOverrides = null;
                try
                {
                    var commentTokenizer = new CalcpadTokenizer();
                    var commentTokens = commentTokenizer.Tokenize(request.Content);
                    var commentParser = new HtmlCommentParser();
                    var commentBlocks = commentParser.Parse(commentTokens);

                    foreach (var block in commentBlocks)
                    {
                        if (block.Status != HtmlCommentParseStatus.Success || !block.Data.HasValue)
                            continue;
                        if (!block.Data.Value.TryGetProperty("uiOverrides", out var overridesElement))
                            continue;
                        if (overridesElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                            continue;

                        var overrides = new Dictionary<string, string>();
                        foreach (var prop in overridesElement.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                                overrides[prop.Name] = prop.Value.GetString() ?? "";
                        }

                        persistedUiOverrides = new PersistedUiOverridesDto
                        {
                            Overrides = overrides,
                            CommentLine = block.StartLine
                        };
                        break;
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.LogWarning("Failed to extract persisted uiOverrides", ex.Message);
                }

                var response = new DefinitionsResponse
                {
                    Macros = staged.Stage2.MacroDefinitions.Select(m => new MacroDefinitionDto
                    {
                        Name = m.Name,
                        Parameters = m.Params ?? new List<string>(),
                        IsMultiline = m.Content.Count > 1 || (m.Content.Count == 1 && string.IsNullOrWhiteSpace(m.Content[0])),
                        Content = m.Content,
                        LineNumber = m.LineNumber,
                        Source = m.Source ?? "local",
                        SourceFile = m.SourceFile,
                        Description = m.Description,
                        ParamTypes = m.ParamTypes,
                        ParamDescriptions = m.ParamDescriptions,
                        Defaults = m.Defaults
                    }).ToList(),

                    Functions = staged.Stage3.FunctionsWithParams.Select(f =>
                    {
                        var funcInfo = typeTracker.Functions.GetValueOrDefault(f.Name);
                        return new FunctionDefinitionDto
                        {
                            Name = f.Name,
                            Parameters = f.Params ?? new List<string>(),
                            Expression = f.Expression,
                            ReturnType = funcInfo?.ReturnType.ToString() ?? "Unknown",
                            ReturnTypeId = (int)(funcInfo?.ReturnType ?? CalcpadType.Unknown),
                            HasCommandBlock = f.CommandBlock != null,
                            CommandBlockType = f.CommandBlock?.BlockType,
                            CommandBlockStatements = f.CommandBlock?.Statements,
                            LineNumber = f.LineNumber,
                            Source = f.Source ?? "local",
                            SourceFile = f.SourceFile,
                            Description = f.Description,
                            ParamTypes = f.ParamTypes,
                            ParamDescriptions = f.ParamDescriptions,
                            Defaults = f.Defaults
                        };
                    }).ToList(),

                    Variables = staged.Stage3.VariablesWithDefinitions.Select(v =>
                    {
                        var varInfo = typeTracker.Variables.GetValueOrDefault(v.Name);
                        return new VariableDefinitionDto
                        {
                            Name = v.Name,
                            Expression = v.Definition,
                            Type = varInfo?.Type.ToString() ?? "Unknown",
                            TypeId = (int)(varInfo?.Type ?? CalcpadType.Unknown),
                            LineNumber = v.LineNumber,
                            Source = v.Source ?? "local",
                            SourceFile = v.SourceFile,
                            Description = v.Description
                        };
                    }).ToList(),

                    CustomUnits = staged.Stage3.CustomUnits.Select(u => new CustomUnitDefinitionDto
                    {
                        Name = u.Name,
                        Expression = u.Definition,
                        LineNumber = u.LineNumber,
                        Source = u.Source ?? "local",
                        SourceFile = u.SourceFile
                    }).ToList(),

                    UiOverrides = persistedUiOverrides
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Definitions request failed", ex);
                return StatusCode(500, "Error getting definitions: " + ex.Message);
            }
        }

        /// <summary>
        /// Get all variable occurrence locations (definitions, reassignments, and usages) for go-to-definition
        /// and find-all-references features. Returns a dictionary mapping variable name to all its occurrences,
        /// with original source line positions and include file info.
        /// </summary>
        [HttpPost("find-references")]
        public async Task<IActionResult> FindReferences([FromBody] DefinitionsRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest("Content is required");

                FileLogger.LogInfo("Find references request received", "Length: " + request.Content.Length);

                // Pre-fetch remote content (URLs and API routes) into the cache
                var ctx = new WebFetchContext
                {
                    ClientFileCache = DecodeClientFileCache(request.ClientFileCache),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs
                };
                await CalcpadService.PreFetchRemoteContentAsync(request.Content, ctx);

                var resolver = new ContentResolver();
                var staged = resolver.GetStagedContent(request.Content, request.IncludeFiles, ctx.ClientFileCache, request.SourceFilePath);

                SymbolLocationDto ToDto(Calcpad.Highlighter.ContentResolution.SymbolLocation loc) => new SymbolLocationDto
                {
                    Line = loc.Line,
                    Column = loc.Column,
                    Length = loc.Length,
                    Source = loc.Source,
                    SourceFile = loc.SourceFile,
                    IsAssignment = loc.IsAssignment
                };

                Dictionary<string, List<SymbolLocationDto>> MapIndex(Dictionary<string, List<Calcpad.Highlighter.ContentResolution.SymbolLocation>> src)
                    => src.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(ToDto).ToList());

                var response = new FindReferencesResponse
                {
                    Variables = MapIndex(staged.Stage3.VariableIndex),
                    Functions = MapIndex(staged.Stage3.FunctionIndex),
                    Macros = MapIndex(staged.Stage3.MacroIndex)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Find references request failed", ex);
                return StatusCode(500, "Error finding references: " + ex.Message);
            }
        }

        /// <summary>
        /// Re-indent Calcpad source by tracking control-block depth across
        /// #if/#else/#end if, #for/#while/#repeat/#loop, and multiline #def/#end def.
        /// Only leading whitespace is adjusted; line endings and content are preserved.
        /// </summary>
        [HttpPost("prettify")]
        public IActionResult PrettifyContent([FromBody] PrettifyRequest request)
        {
            try
            {
                if (request.Content == null)
                    return BadRequest("Content is required");

                var options = new PrettifierOptions
                {
                    IndentUnit = string.IsNullOrEmpty(request.IndentUnit) ? "\t" : request.IndentUnit,
                    TrimTrailingWhitespace = request.TrimTrailingWhitespace
                };

                var formatted = CalcpadPrettifier.Prettify(request.Content, options);
                return Ok(new PrettifyResponse { Content = formatted });
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Prettify request failed", ex);
                return StatusCode(500, "Error prettifying content: " + ex.Message);
            }
        }

        /// <summary>
        /// Get all available snippets for autocomplete/intellisense.
        /// Returns simplified snippet definitions with insert text, descriptions, and categories.
        /// </summary>
        [HttpGet("snippets")]
        public IActionResult GetSnippets([FromQuery] string? category = null)
        {
            try
            {
                FileLogger.LogInfo("Snippets request received", "Category: " + (category ?? "all"));

                SnippetItem[] snippets;
                if (string.IsNullOrWhiteSpace(category))
                {
                    snippets = SnippetRegistry.GetAllSnippetsArray();
                }
                else
                {
                    snippets = SnippetRegistry.GetSnippetsByCategory(category);
                }

                // Map to simplified DTOs for the API response
                var simplifiedSnippets = snippets.Select(s => new SnippetDto
                {
                    Insert = s.Insert,
                    Description = s.Description,
                    Documentation = s.Documentation,
                    Example = s.Example,
                    Label = s.Label,
                    Category = s.Category,
                    QuickType = s.QuickType,
                    KeywordType = s.KeywordType,
                    ReturnType = s.ReturnType?.ToString(),
                    ReturnTypeDescription = s.ReturnTypeDescription,
                    IsElementWise = s.IsElementWise,
                    AcceptsAnyCount = s.AcceptsAnyCount,
                    Parameters = s.Parameters?.Select(p => new SnippetParameterDto
                    {
                        Name = p.Name,
                        Description = p.Description,
                        Type = p.Type.ToString(),
                        TypeDescription = p.TypeDescription,
                        IsOptional = p.IsOptional,
                        IsVariadic = p.IsVariadic
                    }).ToArray()
                }).ToArray();

                return Ok(new SnippetsResponse
                {
                    Count = simplifiedSnippets.Length,
                    Snippets = simplifiedSnippets
                });
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Snippets request failed", ex);
                return StatusCode(500, "Error getting snippets: " + ex.Message);
            }
        }
    }

    public class SnippetsResponse
    {
        /// <summary>Total number of snippets returned</summary>
        public int Count { get; set; }

        /// <summary>Array of snippet definitions</summary>
        public SnippetDto[] Snippets { get; set; } = [];
    }

    public class SnippetDto
    {
        /// <summary>Text to insert (use '§' as cursor placeholder)</summary>
        public string Insert { get; set; } = string.Empty;

        /// <summary>Short label shown in tooltips and completion detail (e.g. "Sine")</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Long-form Markdown description for hover/completion docstrings. Null when not authored.</summary>
        public string? Documentation { get; set; }

        /// <summary>Optional Calcpad usage example, rendered as a fenced code block in hover/completion docs.</summary>
        public string? Example { get; set; }

        /// <summary>Optional display label (defaults to description if null)</summary>
        public string? Label { get; set; }

        /// <summary>Category path (e.g., "Functions/Trigonometric")</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Quick typing shortcut without ~ prefix (e.g., "a" means ~a → α)</summary>
        public string? QuickType { get; set; }

        /// <summary>Keyword classification ("Function", "Keyword", "Command", "Constant", "Unit", "Operator", "Setting", "ControlBlockKeyword", "EndKeyword"). Null for UI-only snippets.</summary>
        public string? KeywordType { get; set; }

        /// <summary>Return type name from the CalcpadType enum (e.g. "Scalar", "Vector"). Null for non-functions.</summary>
        public string? ReturnType { get; set; }

        /// <summary>Human-readable description of the return value (e.g. "Angle in radians").</summary>
        public string? ReturnTypeDescription { get; set; }

        /// <summary>Whether the function operates element-wise on vectors/matrices.</summary>
        public bool IsElementWise { get; set; }

        /// <summary>When true, parameter count validation is skipped (e.g. switch, gcd, lcm).</summary>
        public bool AcceptsAnyCount { get; set; }

        /// <summary>Parameter info for functions (null for non-functions)</summary>
        public SnippetParameterDto[]? Parameters { get; set; }
    }

    public class SnippetParameterDto
    {
        /// <summary>Parameter name (e.g., "x", "M", "v")</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Description of the parameter's purpose</summary>
        public string? Description { get; set; }

        /// <summary>Expected type from the ParameterType enum (e.g. "Scalar", "Vector", "Matrix", "Any").</summary>
        public string? Type { get; set; }

        /// <summary>Human-readable type description (e.g. "Angle in radians"). Falls back to Type when null.</summary>
        public string? TypeDescription { get; set; }

        /// <summary>Whether this parameter is optional.</summary>
        public bool IsOptional { get; set; }

        /// <summary>Whether this parameter is variadic (the type applies to all remaining arguments).</summary>
        public bool IsVariadic { get; set; }
    }

    public class PrettifyRequest
    {
        /// <summary>The Calcpad source code to prettify</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>String emitted per indent level. Defaults to a single tab when null/empty.</summary>
        public string? IndentUnit { get; set; }

        /// <summary>Trim trailing whitespace on each line. Default true.</summary>
        public bool TrimTrailingWhitespace { get; set; } = true;
    }

    public class PrettifyResponse
    {
        /// <summary>Prettified source code</summary>
        public string Content { get; set; } = string.Empty;
    }

    public class RefreshCacheRequest
    {
        /// <summary>Specific cache key (URL or API route) to invalidate. If null/empty, clears entire cache.</summary>
        public string? Key { get; set; }

        /// <summary>List of cache keys to invalidate. Takes precedence over Key if provided.</summary>
        public List<string>? Keys { get; set; }
    }

    public class CalcpadRequest
    {
        public string Content { get; set; } = string.Empty;
        public Settings? Settings { get; set; }
        public bool ForceUnwrappedCode { get; set; } = false;
        public string Theme { get; set; } = "light"; // "light" or "dark"

        /// <summary>
        /// When true, strip <c>NoPrintStart</c>/<c>NoPrintEnd</c> regions from the source
        /// before conversion. The frontend should set this for renders destined for PDF
        /// so those sections do not appear in print output.
        /// </summary>
        public bool ForPrint { get; set; } = false;

        /// <summary>
        /// Client-side file cache with base64-encoded file contents.
        /// Key is the filename, value is the file content encoded as base64.
        /// Used to resolve #include and #read directives from client-cached files.
        /// </summary>
        public Dictionary<string, string>? ClientFileCache { get; set; }

        /// <summary>
        /// Authentication settings for API routing (JWT token and routing config).
        /// Used by the server-side Router to fetch remote files for #include and #read.
        /// </summary>
        public ServerAuthSettings? AuthSettings { get; set; }

        /// <summary>
        /// Timeout in milliseconds for API calls (used by #include, #read, etc.).
        /// Default is 10000ms (10 seconds).
        /// </summary>
        public int ApiTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Full path of the source file on the client. Used to resolve relative
        /// #include and #read paths so they match the client's cache keys.
        /// </summary>
        public string? SourceFilePath { get; set; }
    }

    public class CalcpadUiRequest : CalcpadRequest
    {
        /// <summary>
        /// Maps variable names to override values for UI input fields.
        /// When a user changes an input value in the preview, the frontend
        /// sends the updated values here to re-run the calculation.
        /// </summary>
        public Dictionary<string, string>? UiOverrides { get; set; }
    }

    public class ContentResolverRequest
    {
        public string Content { get; set; } = string.Empty;
        public ServerAuthSettings? AuthSettings { get; set; }
        public int ApiTimeoutMs { get; set; } = 10000;
        public bool Staged { get; set; } = false;
        public Dictionary<string, string>? IncludeFiles { get; set; }

        /// <summary>
        /// Client-side file cache with base64-encoded file contents.
        /// Key is the filename, value is the file content encoded as base64.
        /// Used to resolve #include and #read directives from client-cached files.
        /// </summary>
        public Dictionary<string, string>? ClientFileCache { get; set; }

        /// <summary>
        /// Full path of the source file on the client.
        /// </summary>
        public string? SourceFilePath { get; set; }
    }

    public class HighlightRequest
    {
        /// <summary>The Calcpad source code to tokenize</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Whether to include the token text in the response (default: false to reduce payload size)</summary>
        public bool IncludeText { get; set; } = false;

        /// <summary>Optional dictionary of include file contents (filename -> content)</summary>
        public Dictionary<string, string>? IncludeFiles { get; set; }

        /// <summary>
        /// Client-side file cache with base64-encoded file contents.
        /// Used to resolve #include directives from client-cached files.
        /// </summary>
        public Dictionary<string, string>? ClientFileCache { get; set; }

        /// <summary>
        /// Full path of the source file on the client.
        /// </summary>
        public string? SourceFilePath { get; set; }
    }

    public class HighlightLineRequest
    {
        /// <summary>The line content to tokenize</summary>
        public string Line { get; set; } = string.Empty;

        /// <summary>The zero-based line number</summary>
        public int LineNumber { get; set; } = 0;

        /// <summary>Whether to include the token text in the response</summary>
        public bool IncludeText { get; set; } = false;
    }

    public class HighlightResponse
    {
        /// <summary>List of tokens with position and type information</summary>
        public List<HighlightToken> Tokens { get; set; } = new();
    }

    public class HighlightToken
    {
        /// <summary>Zero-based line number</summary>
        public int Line { get; set; }

        /// <summary>Zero-based column (character offset from start of line)</summary>
        public int Column { get; set; }

        /// <summary>Length of the token in characters</summary>
        public int Length { get; set; }

        /// <summary>Token type name for display/debugging</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Token type ID for efficient frontend processing.
        /// 0=None, 1=Const, 2=Units, 3=Operator, 4=Variable, 5=Function,
        /// 6=Keyword, 7=Command, 8=Bracket, 9=Comment, 10=Tag, 11=Input,
        /// 12=Include, 13=Macro, 14=HtmlComment, 15=Format
        /// </summary>
        public int TypeId { get; set; }

        /// <summary>The actual token text (only included if IncludeText is true)</summary>
        public string? Text { get; set; }
    }

    public class LintRequest
    {
        /// <summary>The Calcpad source code to lint</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Optional dictionary of include file contents (filename -> content)</summary>
        public Dictionary<string, string>? IncludeFiles { get; set; }

        /// <summary>
        /// Client-side file cache with base64-encoded file contents.
        /// Key is the filename, value is the file content encoded as base64.
        /// Used to resolve #include and #read directives from client-cached files.
        /// </summary>
        public Dictionary<string, string>? ClientFileCache { get; set; }

        /// <summary>Authentication settings for API routing (JWT token and routing config).</summary>
        public ServerAuthSettings? AuthSettings { get; set; }

        /// <summary>Timeout in milliseconds for remote fetches. Default is 10000ms.</summary>
        public int ApiTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Full path of the source file on the client.
        /// </summary>
        public string? SourceFilePath { get; set; }
    }

    public class LintResponse
    {
        /// <summary>Total number of errors</summary>
        public int ErrorCount { get; set; }

        /// <summary>Total number of warnings</summary>
        public int WarningCount { get; set; }

        /// <summary>List of diagnostics (errors and warnings)</summary>
        public List<LintDiagnostic> Diagnostics { get; set; } = new();
    }

    public class LintDiagnostic
    {
        /// <summary>Zero-based line number</summary>
        public int Line { get; set; }

        /// <summary>Zero-based column (start position)</summary>
        public int Column { get; set; }

        /// <summary>Zero-based end column position</summary>
        public int EndColumn { get; set; }

        /// <summary>Error code (e.g., "CPD-3301")</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Human-readable error/warning message</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Severity name: "error" or "warning"</summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>Severity ID: 0=Error, 1=Warning</summary>
        public int SeverityId { get; set; }

        /// <summary>Source of the diagnostic (default: "Calcpad Linter")</summary>
        public string Source { get; set; } = "Calcpad Linter";
    }

    public class DefinitionsRequest
    {
        /// <summary>The Calcpad source code to analyze</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Optional dictionary of include file contents (filename -> content)</summary>
        public Dictionary<string, string>? IncludeFiles { get; set; }

        /// <summary>
        /// Client-side file cache with base64-encoded file contents.
        /// Key is the filename, value is the file content encoded as base64.
        /// Used to resolve #include and #read directives from client-cached files.
        /// </summary>
        public Dictionary<string, string>? ClientFileCache { get; set; }

        /// <summary>Authentication settings for API routing (JWT token and routing config).</summary>
        public ServerAuthSettings? AuthSettings { get; set; }

        /// <summary>Timeout in milliseconds for remote fetches. Default is 10000ms.</summary>
        public int ApiTimeoutMs { get; set; } = 10000;

        /// <summary>Full path of the source file on the client.</summary>
        public string? SourceFilePath { get; set; }
    }

    public class DefinitionsResponse
    {
        /// <summary>All macro definitions (inline and multiline)</summary>
        public List<MacroDefinitionDto> Macros { get; set; } = new();

        /// <summary>All function definitions</summary>
        public List<FunctionDefinitionDto> Functions { get; set; } = new();

        /// <summary>All variable definitions</summary>
        public List<VariableDefinitionDto> Variables { get; set; } = new();

        /// <summary>All custom unit definitions</summary>
        public List<CustomUnitDefinitionDto> CustomUnits { get; set; } = new();

        /// <summary>Persisted UI overrides extracted from an HTML comment block, if present</summary>
        public PersistedUiOverridesDto? UiOverrides { get; set; }
    }

    public class PersistedUiOverridesDto
    {
        /// <summary>Variable name to override value mapping</summary>
        public Dictionary<string, string> Overrides { get; set; } = new();

        /// <summary>Zero-based line number of the HTML comment block containing the overrides</summary>
        public int CommentLine { get; set; }
    }

    public class MacroDefinitionDto
    {
        /// <summary>Macro name (including $ suffix)</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Parameter names</summary>
        public List<string> Parameters { get; set; } = new();

        /// <summary>True if multiline macro (#def...#end def), false if inline macro</summary>
        public bool IsMultiline { get; set; }

        /// <summary>Macro content lines (single line for inline, multiple for multiline)</summary>
        public List<string> Content { get; set; } = new();

        /// <summary>Zero-based line number where the macro is defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment</summary>
        public string? Description { get; set; }

        /// <summary>User-provided type hints per parameter</summary>
        public List<string>? ParamTypes { get; set; }

        /// <summary>User-provided descriptions per parameter</summary>
        public List<string>? ParamDescriptions { get; set; }

        /// <summary>Default values parallel to Parameters. null = required, string = default expression.</summary>
        public List<string?>? Defaults { get; set; }
    }

    public class FunctionDefinitionDto
    {
        /// <summary>Function name</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Parameter names</summary>
        public List<string> Parameters { get; set; } = new();

        /// <summary>Function body expression (right side of =)</summary>
        public string? Expression { get; set; }

        /// <summary>
        /// Inferred return type name.
        /// Values: Unknown, Value, Vector, Matrix, StringVariable, Various, Function, InlineMacro, MultilineMacro, CustomUnit
        /// </summary>
        public string ReturnType { get; set; } = "Unknown";

        /// <summary>
        /// Return type ID for efficient frontend processing.
        /// 0=Unknown, 1=Value, 2=Vector, 3=Matrix, 4=StringVariable, 5=Various, 6=Function, 7=InlineMacro, 8=MultilineMacro, 9=CustomUnit
        /// </summary>
        public int ReturnTypeId { get; set; }

        /// <summary>True if this function uses a command block ($Inline, $Block, $While)</summary>
        public bool HasCommandBlock { get; set; }

        /// <summary>Command block type: "Inline", "Block", or "While" (null if no command block)</summary>
        public string? CommandBlockType { get; set; }

        /// <summary>Statements inside the command block, split by semicolon (null if no command block)</summary>
        public List<string>? CommandBlockStatements { get; set; }

        /// <summary>Zero-based line number where the function is defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment</summary>
        public string? Description { get; set; }

        /// <summary>User-provided type hints per parameter</summary>
        public List<string>? ParamTypes { get; set; }

        /// <summary>User-provided descriptions per parameter</summary>
        public List<string>? ParamDescriptions { get; set; }

        /// <summary>Default values parallel to Parameters. null = required, string = default expression.</summary>
        public List<string?>? Defaults { get; set; }
    }

    public class VariableDefinitionDto
    {
        /// <summary>Variable name</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Initial expression (right side of first assignment)</summary>
        public string? Expression { get; set; }

        /// <summary>
        /// Inferred type name.
        /// Values: Unknown, Value, Vector, Matrix, StringVariable, Various
        /// </summary>
        public string Type { get; set; } = "Unknown";

        /// <summary>
        /// Type ID for efficient frontend processing.
        /// 0=Unknown, 1=Value, 2=Vector, 3=Matrix, 4=StringVariable, 5=Various
        /// </summary>
        public int TypeId { get; set; }

        /// <summary>Zero-based line number where the variable is first defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>User-provided description from a metadata comment</summary>
        public string? Description { get; set; }
    }

    public class CustomUnitDefinitionDto
    {
        /// <summary>Unit name (without leading dot)</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Unit definition expression</summary>
        public string? Expression { get; set; }

        /// <summary>Zero-based line number where the unit is defined</summary>
        public int LineNumber { get; set; }

        /// <summary>Source: "local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>Source file path if from include, null otherwise</summary>
        public string? SourceFile { get; set; }
    }

    public class FindReferencesResponse
    {
        /// <summary>
        /// Maps variable name to all its occurrences (definitions, reassignments, and usages).
        /// Each occurrence has original source line/column, source file info, and whether it's an assignment.
        /// </summary>
        public Dictionary<string, List<SymbolLocationDto>> Variables { get; set; } = new();

        /// <summary>
        /// Maps user-defined function name to all its occurrences (definition and call sites).
        /// </summary>
        public Dictionary<string, List<SymbolLocationDto>> Functions { get; set; } = new();

        /// <summary>
        /// Maps macro name to all its occurrences (definition and call sites).
        /// </summary>
        public Dictionary<string, List<SymbolLocationDto>> Macros { get; set; } = new();
    }

    public class SymbolLocationDto
    {
        /// <summary>Original source line (0-based, mapped back through all pipeline stages)</summary>
        public int Line { get; set; }

        /// <summary>Column in the source line (0-based)</summary>
        public int Column { get; set; }

        /// <summary>Token length in characters</summary>
        public int Length { get; set; }

        /// <summary>"local" or "include"</summary>
        public string Source { get; set; } = "local";

        /// <summary>File path if from an #include, null otherwise</summary>
        public string? SourceFile { get; set; }

        /// <summary>True for definitions and reassignments, false for read-only usages</summary>
        public bool IsAssignment { get; set; }
    }
}