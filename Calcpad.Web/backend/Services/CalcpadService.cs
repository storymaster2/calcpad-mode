using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Calcpad.Core;
using Calcpad.Server.Controllers;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Bundles all state needed for server-side remote content fetching.
    /// Created per-request from the incoming request properties.
    /// </summary>
    public class WebFetchContext
    {
        /// <summary>Content cache: filename → raw file bytes. Populated by frontend (local files) and server (remote files).</summary>
        public Dictionary<string, byte[]> ClientFileCache { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fetch errors: filename → error message. Passed to Core's ClientFileCache.Errors.</summary>
        public Dictionary<string, string> FetchErrors { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Authentication settings for API routing.</summary>
        public AuthSettings? AuthSettings { get; set; }

        /// <summary>Timeout in milliseconds for remote fetches.</summary>
        public int ApiTimeoutMs { get; set; } = 10000;

        /// <summary>Full path of the source file on the client, for resolving relative paths.</summary>
        public string? SourceFilePath { get; set; }
    }

    public class CalcpadService
    {
        private readonly string _tempDirectory;
        private readonly string _htmlTemplate;
        private static readonly FileSettingsExtractor _fileSettingsExtractor = new();
        private static readonly NoPrintRegionStripper _noPrintRegionStripper = new();

        /// <summary>
        /// Global cache of pre-fetched remote content.
        /// Maps referenceKey (URL or &lt;service:endpoint&gt;) to raw file bytes.
        /// Shared across all requests — URLs are the same regardless of source file.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte[]> _remoteContentCache
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// <summary>Clears all cached remote content and disk-cached files.</summary>
        public static void ClearRemoteContentCache()
        {
            _remoteContentCache.Clear();
            ClearDiskCache();
        }

        /// <summary>Deletes all .cache files from the disk cache folder.</summary>
        public static void ClearDiskCache()
        {
            var folder = DiskCacheCleanupService.CacheFolder;
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.cache"))
            {
                try { File.Delete(file); } catch { }
            }
        }

        /// <summary>Removes a specific entry from the remote content cache.</summary>
        public static void RemoveFromRemoteContentCache(string key) => _remoteContentCache.TryRemove(key, out _);

        public CalcpadService()
        {
            _tempDirectory = Path.GetTempPath();
            _htmlTemplate = LoadHtmlTemplate();
        }

        internal static Router CreateApiRouter(AuthSettings? authSettings = null)
        {
            RoutingConfig? config = authSettings?.RoutingConfig;
            return new Router(config, authSettings);
        }

        /// <summary>
        /// Creates a synchronous Include delegate for MacroParser.
        /// Handles filesystem reads and API/URL fetching via Router.
        /// Core only calls this when the file isn't found locally or in cache.
        /// </summary>
        internal static Func<string, Queue<string>, string> CreateIncludeDelegate(Router? apiRouter, int timeoutMs, ClientFileCache? fileCache)
        {
            return (fileName, fields) =>
            {
                try
                {
                    string rawContent;

                    // Filesystem access
                    if (File.Exists(fileName))
                    {
                        rawContent = File.ReadAllText(fileName);
                        return ProcessIncludedContent(rawContent);
                    }

                    // Direct URL fetch (no special syntax needed)
                    if (Router.IsDirectUrl(fileName))
                    {
                        FileLogger.LogInfo($"Fetching from direct URL: {fileName}");
                        var contentBytes = Router.FetchUrlAsync(fileName, timeoutMs).GetAwaiter().GetResult();

                        FileLogger.LogInfo($"Fetch successful, received {contentBytes.Length} bytes");
                        rawContent = System.Text.Encoding.UTF8.GetString(contentBytes);

                        AddToCache(fileCache, fileName, rawContent);
                        return ProcessIncludedContent(rawContent);
                    }

                    // API router syntax: <service:endpoint>body
                    if (apiRouter != null && fileName.StartsWith('<'))
                    {
                        int closeIndex = fileName.IndexOf('>');
                        if (closeIndex == -1)
                            throw new InvalidOperationException("Invalid syntax: Missing '>' in specification");

                        string spec = fileName.Substring(1, closeIndex - 1);
                        string bodyContent = fileName.Substring(closeIndex + 1);

                        int colonIndex = spec.IndexOf(':');
                        if (colonIndex == -1)
                            throw new InvalidOperationException("Invalid API syntax: Missing ':' in service specification");

                        string apiService = spec.Substring(0, colonIndex);
                        string apiEndpoint = spec.Substring(colonIndex + 1);

                        FileLogger.LogInfo($"Fetching via API: service={apiService}, endpoint={apiEndpoint}, body={bodyContent}");
                        var contentBytes = apiRouter.FetchFileBytesAsync(apiService, apiEndpoint, bodyContent, timeoutMs).GetAwaiter().GetResult();

                        FileLogger.LogInfo($"Fetch successful, received {contentBytes.Length} bytes");
                        rawContent = System.Text.Encoding.UTF8.GetString(contentBytes);

                        AddToCache(fileCache, fileName, rawContent);
                        return ProcessIncludedContent(rawContent);
                    }

                    return $"' File not found: {fileName}";
                }
                catch (Exception ex)
                {
                    FileLogger.LogError($"Error reading include file: {fileName}", ex);

                    // Write error to cache so Core can show it instead of "File not found"
                    AddErrorToCache(fileCache, fileName, ex.Message);

                    return $"' Error reading file: {fileName} - {ex.Message}";
                }
            };
        }

        /// <summary>
        /// Adds a successful fetch result to the file cache, offloading to disk if > 1 MB.
        /// </summary>
        private static void AddToCache(ClientFileCache? cache, string filename, string content)
        {
            if (cache == null) return;
            cache.AddEntry(filename, System.Text.Encoding.UTF8.GetBytes(content), null);
        }

        /// <summary>
        /// Adds an error entry to the file cache so Core can show the
        /// fetch error instead of a generic "File not found" message.
        /// </summary>
        private static void AddErrorToCache(ClientFileCache? cache, string filename, string errorMessage)
        {
            if (cache == null) return;
            cache.AddEntry(filename, null, errorMessage);
        }

        /// <summary>
        /// Creates a fetch delegate that handles both direct URLs and API router syntax.
        /// </summary>
        private static Func<string, Task<string>> CreateFetchDelegate(Router? apiRouter, int timeoutMs)
        {
            return async target =>
            {
                if (IncludeResolver.IsUrl(target))
                {
                    FileLogger.LogInfo($"Pre-fetching URL: {target}");
                    var bytes = await Router.FetchUrlAsync(target, timeoutMs).ConfigureAwait(false);
                    return System.Text.Encoding.UTF8.GetString(bytes);
                }

                if (target.StartsWith('<') && apiRouter != null)
                {
                    int closeIndex = target.IndexOf('>');
                    if (closeIndex == -1)
                        throw new InvalidOperationException("Invalid syntax: Missing '>' in specification");

                    string spec = target[1..closeIndex];
                    string body = target[(closeIndex + 1)..];

                    int colonIndex = spec.IndexOf(':');
                    if (colonIndex == -1)
                        throw new InvalidOperationException("Invalid API syntax: Missing ':' in service specification");

                    string service = spec[..colonIndex];
                    string endpoint = spec[(colonIndex + 1)..];

                    FileLogger.LogInfo($"Pre-fetching API: {service}:{endpoint}");
                    var bytes = await apiRouter.FetchFileBytesAsync(service, endpoint, body, timeoutMs).ConfigureAwait(false);
                    return System.Text.Encoding.UTF8.GetString(bytes);
                }

                throw new InvalidOperationException($"Unknown remote target: {target}");
            };
        }

        /// <summary>
        /// Extracts remote targets (URLs and API routes) from #read directives.
        /// Returns the cache key for each target (URL up to @ separator).
        /// </summary>
        internal static List<string> ExtractReadTargets(string content)
        {
            var results = new List<string>();

            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.AsSpan().Trim();
                if (!trimmed.StartsWith("#read ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fromIndex = trimmed.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
                if (fromIndex < 0)
                    continue;

                var afterFrom = trimmed[(fromIndex + 6)..].Trim();
                if (afterFrom.IsEmpty)
                    continue;

                // Target ends at @ (range separator) or space (TYPE/SEP keywords)
                var atIdx = afterFrom.IndexOf('@');
                var spaceIdx = afterFrom.IndexOf(' ');
                int end = afterFrom.Length;
                if (atIdx > 0) end = Math.Min(end, atIdx);
                if (spaceIdx > 0) end = Math.Min(end, spaceIdx);

                var target = afterFrom[..end].Trim().ToString();

                if (!string.IsNullOrEmpty(target) && IncludeResolver.IsRemoteTarget(target))
                    results.Add(target);
            }

            return results;
        }

        /// <summary>
        /// Pre-fetches all remote references (#include and #read) from content
        /// and populates the clientFileCache dictionary (raw bytes) before processing.
        /// Uses a global cache to avoid re-fetching the same URLs across requests.
        /// Used by both the convert endpoint (for Core) and lint/resolve endpoints (for Highlighter).
        /// </summary>
        public static async Task PreFetchRemoteContentAsync(string content, WebFetchContext ctx)
        {
            var apiRouter = CreateApiRouter(ctx.AuthSettings);
            var rawFetchDelegate = CreateFetchDelegate(apiRouter, ctx.ApiTimeoutMs);

            // Wrap fetch delegate to check global cache before making HTTP calls
            Func<string, Task<string>> fetchDelegate = async target =>
            {
                if (_remoteContentCache.TryGetValue(target, out var cachedBytes))
                {
                    FileLogger.LogInfo($"Cache hit for: {target}");
                    return System.Text.Encoding.UTF8.GetString(cachedBytes);
                }

                var result = await rawFetchDelegate(target).ConfigureAwait(false);
                _remoteContentCache.TryAdd(target, System.Text.Encoding.UTF8.GetBytes(result));
                return result;
            };

            // 1. Resolve #include remote targets recursively
            var includeResults = await IncludeResolver.ResolveRemoteIncludesAsync(
                content, fetchDelegate).ConfigureAwait(false);

            foreach (var kvp in includeResults)
            {
                if (kvp.Value.content != null && !ctx.ClientFileCache.ContainsKey(kvp.Key))
                    ctx.ClientFileCache[kvp.Key] = System.Text.Encoding.UTF8.GetBytes(kvp.Value.content);
                else if (kvp.Value.error != null)
                {
                    ctx.FetchErrors[kvp.Key] = kvp.Value.error;
                    FileLogger.LogError($"Pre-fetch failed for #include target: {kvp.Key}: {kvp.Value.error}");
                }
            }

            // 2. Extract #read remote targets from main content and fetched includes
            var readTargets = ExtractReadTargets(content);
            foreach (var kvp in includeResults)
            {
                if (kvp.Value.content != null)
                    readTargets.AddRange(ExtractReadTargets(kvp.Value.content));
            }

            // 3. Fetch #read targets (fetch delegate handles cache internally)
            foreach (var target in readTargets)
            {
                if (ctx.ClientFileCache.ContainsKey(target))
                    continue;

                try
                {
                    var fetchedContent = await fetchDelegate(target).ConfigureAwait(false);
                    ctx.ClientFileCache[target] = System.Text.Encoding.UTF8.GetBytes(fetchedContent);
                    FileLogger.LogInfo($"Pre-fetched #read target: {target}");
                }
                catch (Exception ex)
                {
                    ctx.FetchErrors[target] = ex.Message;
                    FileLogger.LogError($"Pre-fetch failed for #read target: {target}", ex);
                }
            }
        }

        public async Task<string> GetRawCodeFromMacroParser(string calcpadContent)
        {
            if (string.IsNullOrWhiteSpace(calcpadContent))
            {
                throw new ArgumentException("Content cannot be null or empty", nameof(calcpadContent));
            }

            var macroParser = new MacroParser();
            string outputText;
            macroParser.Parse(calcpadContent, out outputText, null, 0, true);
            return outputText;
        }

        public async Task<string> ConvertAsync(string calcpadContent, Settings? settings = null, bool forceUnwrappedCode = false, string theme = "light", WebFetchContext? ctx = null, bool forPrint = false)
        {
            if (string.IsNullOrWhiteSpace(calcpadContent))
            {
                FileLogger.LogWarning("ConvertAsync called with empty content");
                throw new ArgumentException("Content cannot be null or empty", nameof(calcpadContent));
            }

            try
            {
                Console.WriteLine($"=== CALCPAD SERVICE: Starting conversion, length: {calcpadContent.Length} ===");
                FileLogger.LogInfo("Starting conversion", $"Content length: {calcpadContent.Length}, Has settings: {settings != null}, Force unwrapped: {forceUnwrappedCode}, For print: {forPrint}");
                FileLogger.LogInfo("Content preview:", calcpadContent.Substring(0, Math.Min(200, calcpadContent.Length)));

                // When generating for PDF, strip NoPrintStart/NoPrintEnd regions from the source
                // before any further processing so they never enter the macro/expression pipeline.
                if (forPrint)
                    calcpadContent = _noPrintRegionStripper.Strip(calcpadContent);

                // 1. Use Calcpad.Core settings directly (defaults are set in constructors)
                Settings coreSettings = settings ?? new Settings();

                // Apply any per-file settings overrides embedded in HTML comments
                coreSettings = _fileSettingsExtractor.ApplyFileSettings(calcpadContent, coreSettings);

                // 2. Pre-fetch all remote references (URLs and API routes) into dictionary
                ctx ??= new WebFetchContext();
                await PreFetchRemoteContentAsync(calcpadContent, ctx).ConfigureAwait(false);

                // 3. Convert dictionary to typed ClientFileCache for Core
                ClientFileCache typedFileCache = ConvertToClientFileCache(ctx.ClientFileCache, ctx.FetchErrors) ?? new ClientFileCache();
                typedFileCache.DiskCacheFolder ??= DiskCacheCleanupService.CacheFolder;
                typedFileCache.RefetchDelegate = filename =>
                {
                    if (_remoteContentCache.TryGetValue(filename, out var cached))
                        return cached;
                    if (Router.IsDirectUrl(filename))
                    {
                        var bytes = Router.FetchUrlAsync(filename, ctx.ApiTimeoutMs).GetAwaiter().GetResult();
                        _remoteContentCache.TryAdd(filename, bytes);
                        return bytes;
                    }
                    return null;
                };
                coreSettings.ClientFileCache = typedFileCache;

                // 4. Configure API Router (server-side, not from Core settings)
                Router apiRouter = CreateApiRouter(ctx.AuthSettings);
                FileLogger.LogInfo($"API Router created, AuthSettings: {(ctx.AuthSettings != null ? "configured" : "null")}");

                // 5. Parse macros and includes
                var macroParser = new MacroParser();
                macroParser.Include = CreateIncludeDelegate(apiRouter, ctx.ApiTimeoutMs, typedFileCache);
                macroParser.ClientFileCache = typedFileCache;
                macroParser.SourceFilePath = ctx.SourceFilePath;
                coreSettings.SourceFilePath = ctx.SourceFilePath;

                string outputText;
                var hasMacroErrors = macroParser.Parse(calcpadContent, out outputText, null, 0, true);

                string htmlResult;

                if (hasMacroErrors || forceUnwrappedCode)
                {
                    htmlResult = ConvertCodeToHtml(outputText);
                }
                else
                {
                    try
                    {
                        var parser = new ExpressionParser { Settings = coreSettings };
                        parser.Parse(outputText, true, false);
                        htmlResult = RemoveEmptyParagraphs(parser.HtmlResult);
                    }
                    catch (Exception parseEx)
                    {
                        FileLogger.LogWarning("Expression parsing failed, falling back to unwrapped code", parseEx.Message);
                        htmlResult = ConvertCodeToHtml(outputText);
                    }
                }

                // 6. Apply HTML wrapper with theme support
                var finalHtml = WrapHtmlResult(htmlResult, theme);
                FileLogger.LogInfo("Conversion completed successfully", $"Output length: {finalHtml.Length}");

                return finalHtml;
            }
            catch (MathParserException ex)
            {
                FileLogger.LogError("Math parsing error during conversion", ex);
                throw new InvalidOperationException($"Math parsing error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Calcpad conversion failed", ex);
                throw new InvalidOperationException($"Calcpad conversion failed: {ex.Message}", ex);
            }
        }

        public string GetSampleContent()
        {
            return @"""Sample Mathematical Calculations
'<hr/>
'Basic arithmetic operations:
a = 10
b = 5
'Addition:
sum = a + b
'Subtraction:  
diff = a - b
'Multiplication:
prod = a * b
'Division:
ratio = a / b
'Power:
power = a^2
'Square root:
sqrt_a = sqr(a)
'Trigonometric functions (angle in degrees):
angle = 45
sin_angle = sin(angle°)
cos_angle = cos(angle°)
tan_angle = tan(angle°)";
        }


        private string ConvertCodeToHtml(string code)
        {
            // Convert code to HTML with line numbers and syntax highlighting (following WPF pattern)
            const string ErrorString = "#Error";
            
            var errors = new Queue<int>();
            var stringBuilder = new System.Text.StringBuilder();
            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            stringBuilder.AppendLine("<div class=\"code\">");
            var lineNumber = 0;
            
            foreach (var line in lines)
            {
                ++lineNumber;
                var i = line.IndexOf('\v');
                var lineText = i < 0 ? line : line.Substring(0, i);
                var sourceLine = i < 0 ? lineNumber.ToString() : line.Substring(i + 1);
                
                // Format line number with proper padding
                var lineNumText = lineNumber.ToString();
                var n = lineNumText.Length;
                var spaceCount = Math.Max(0, 6 - n);
                var paddedSpaces = new string(' ', spaceCount).Replace(" ", "&nbsp;");
                
                stringBuilder.Append($"<p class=\"line-text\" id=\"line-{lineNumber}\"><span class=\"line-num\" title=\"Source line {sourceLine}\">{paddedSpaces}{lineNumber}</span>&emsp;│&emsp;");

                if (lineText.StartsWith(ErrorString))
                {
                    errors.Enqueue(lineNumber);
                    var errorText = lineText.Length > ErrorString.Length ? lineText.Substring(ErrorString.Length) : "";
                    // MacroParser already HTML encoded the error text, but we need to preserve HTML links
                    // So we decode it first, then re-encode only the parts that aren't HTML links
                    var decodedError = System.Web.HttpUtility.HtmlDecode(errorText);
                    stringBuilder.Append($"<span class=\"err\">{decodedError}</span>");
                }
                else if (lineText.Contains("<a href=\"#0\" data-text=\"") || lineText.StartsWith("Error in"))
                {
                    // Line contains error HTML with data-text links - don't apply syntax highlighting
                    // The HTML tags are NOT encoded in the raw text from MacroParser
                    errors.Enqueue(lineNumber);
                    stringBuilder.Append($"<span class=\"err\">{lineText}</span>");
                }
                else
                {
                    // Apply syntax highlighting like WPF version
                    var highlightedLine = ApplySyntaxHighlighting(lineText);
                    stringBuilder.Append(highlightedLine);
                }
                stringBuilder.AppendLine("</p>");
            }
            
            stringBuilder.AppendLine("</div>");
            
            // Add error summary if there are many errors
            if (errors.Count != 0 && lineNumber > 30)
            {
                stringBuilder.AppendLine($"<div class=\"errorHeader\">Found <b>{errors.Count}</b> errors in modules and macros:");
                var count = 0;
                while (errors.Count != 0 && ++count < 20)
                {
                    var errorLine = errors.Dequeue();
                    stringBuilder.Append($" <span class=\"roundBox\" data-line=\"{errorLine}\">{errorLine}</span>");
                }
                if (errors.Count > 0)
                    stringBuilder.Append(" ...");
                
                stringBuilder.AppendLine("</div>");
                stringBuilder.AppendLine("<style>body {padding-top:1.1em;} .code p {margin:0; line-height:1.15em;}</style>");
            }
            else
            {
                stringBuilder.AppendLine("<style>.code p {margin:0; line-height:1.15em;}</style>");
            }
            
            return stringBuilder.ToString();
        }

        private string ApplySyntaxHighlighting(string line)
        {
            if (string.IsNullOrEmpty(line))
                return "";

            var result = new System.Text.StringBuilder();
            var i = 0;
            var len = line.Length;

            while (i < len)
            {
                var c = line[i];
                
                // Comments (pink/magenta for includes, green for text, purple for HTML tags)
                if (c == '\'' || c == '"')
                {
                    var commentEnd = FindCommentEnd(line, i);
                    var commentText = line.Substring(i, commentEnd - i + 1);
                    
                    // Check if it's an include comment
                    if (commentText.Contains("#include"))
                        result.Append($"<span class=\"include\">{System.Web.HttpUtility.HtmlEncode(commentText)}</span>");
                    // Check if it contains HTML tags
                    else if (commentText.Contains("<") && commentText.Contains(">"))
                        result.Append($"<span class=\"htmltag\">{System.Web.HttpUtility.HtmlEncode(commentText)}</span>");
                    else
                        result.Append($"<span class=\"comment\">{System.Web.HttpUtility.HtmlEncode(commentText)}</span>");
                    
                    i = commentEnd + 1;
                    continue;
                }
                
                // Keywords (magenta/pink)
                if (c == '#')
                {
                    var keywordEnd = FindKeywordEnd(line, i);
                    var keyword = line.Substring(i, keywordEnd - i + 1);
                    result.Append($"<span class=\"keyword\">{System.Web.HttpUtility.HtmlEncode(keyword)}</span>");
                    i = keywordEnd + 1;
                    continue;
                }
                
                // Commands (magenta)
                if (c == '$')
                {
                    var commandEnd = FindCommandEnd(line, i);
                    var command = line.Substring(i, commandEnd - i + 1);
                    result.Append($"<span class=\"command\">{System.Web.HttpUtility.HtmlEncode(command)}</span>");
                    i = commandEnd + 1;
                    continue;
                }
                
                // Numbers (black)
                if (char.IsDigit(c))
                {
                    var numberEnd = FindNumberEnd(line, i);
                    var number = line.Substring(i, numberEnd - i + 1);
                    result.Append($"<span class=\"number\">{System.Web.HttpUtility.HtmlEncode(number)}</span>");
                    i = numberEnd + 1;
                    continue;
                }
                
                // Variables and functions (blue)
                if (char.IsLetter(c) || c == '_')
                {
                    var identifierEnd = FindIdentifierEnd(line, i);
                    var identifier = line.Substring(i, identifierEnd - i + 1);
                    
                    // Check if it's followed by '(' to identify functions
                    var isFunction = identifierEnd + 1 < len && line[identifierEnd + 1] == '(';
                    if (isFunction)
                        result.Append($"<span class=\"function\">{System.Web.HttpUtility.HtmlEncode(identifier)}</span>");
                    else
                        result.Append($"<span class=\"variable\">{System.Web.HttpUtility.HtmlEncode(identifier)}</span>");
                    
                    i = identifierEnd + 1;
                    continue;
                }
                
                // Operators (goldenrod)
                if (IsOperator(c))
                {
                    result.Append($"<span class=\"operator\">{System.Web.HttpUtility.HtmlEncode(c)}</span>");
                    i++;
                    continue;
                }
                
                // Default: just encode the character
                result.Append(System.Web.HttpUtility.HtmlEncode(c));
                i++;
            }

            return result.ToString();
        }

        private int FindCommentEnd(string line, int start)
        {
            var quote = line[start];
            for (int i = start + 1; i < line.Length; i++)
            {
                if (line[i] == quote)
                    return i;
            }
            return line.Length - 1;
        }

        private int FindKeywordEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]))
                    return i - 1;
            }
            return line.Length - 1;
        }

        private int FindCommandEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (!char.IsLetterOrDigit(line[i]) && line[i] != '_')
                    return i - 1;
            }
            return line.Length - 1;
        }

        private int FindNumberEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (!char.IsDigit(line[i]) && line[i] != '.')
                    return i - 1;
            }
            return line.Length - 1;
        }

        private int FindIdentifierEnd(string line, int start)
        {
            for (int i = start + 1; i < line.Length; i++)
            {
                if (!char.IsLetterOrDigit(line[i]) && line[i] != '_')
                    return i - 1;
            }
            return line.Length - 1;
        }

        private bool IsOperator(char c)
        {
            return "!^/÷\\⦼*-+<>≤≥≡≠=∧∨⊕(){}[]|&@:;".Contains(c);
        }

        private string LoadHtmlTemplate()
        {
            try
            {
                // First try to read from embedded resource (for single-file deployments)
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "Calcpad.Server.template.html";
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var template = reader.ReadToEnd();
                    FileLogger.LogInfo("Loaded HTML template from embedded resource");
                    return template;
                }
                
                // Fallback to file system (for development and non-single-file deployments)
                var templatePath = Path.Combine(AppContext.BaseDirectory, "template.html");
                if (File.Exists(templatePath))
                {
                    var template = File.ReadAllText(templatePath);
                    FileLogger.LogInfo("Loaded HTML template from file system", templatePath);
                    return template;
                }
                
                throw new FileNotFoundException($"Template file not found at {templatePath}");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("Failed to load HTML template, using fallback", ex);
                return GetFallbackTemplate();
            }
        }

        private string GetFallbackTemplate()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Calcpad Calculation</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; line-height: 1.6; }
        .math { font-family: 'Times New Roman', serif; }
        .error { color: red; }
        table { border-collapse: collapse; margin: 10px 0; }
        td, th { padding: 5px 10px; border: 1px solid #ccc; }
        .center { text-align: center; }
    </style>
</head>
<body>
    {{CONTENT}}
</body>
</html>";
        }

        private string RemoveEmptyParagraphs(string htmlContent)
        {
            // Remove lines that only contain <p>&nbsp;</p>
            return System.Text.RegularExpressions.Regex.Replace(
                htmlContent,
                @"<p>&nbsp;</p>(\r?\n)?",
                string.Empty
            );
        }

        private string WrapHtmlResult(string htmlContent, string theme = "light")
        {
            // Use the comprehensive HTML template with theme support
            var themeClass = theme.ToLower() == "dark" ? " class=\"dark-theme\"" : "";
            var templateWithTheme = _htmlTemplate.Replace("<body>", $"<body{themeClass}>");

            // Expose bundled fonts as window.__calcpadFonts so client scripts
            // (e.g. the DXF render module) can use them instead of hitting a CDN.
            // Injected before </head> so it runs before any module scripts in body.
            var fontScript = BundledFonts.GetInjectionScript();
            if (!string.IsNullOrEmpty(fontScript))
            {
                var headCloseIdx = templateWithTheme.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                if (headCloseIdx >= 0)
                    templateWithTheme = templateWithTheme.Insert(headCloseIdx, fontScript);
            }

            return templateWithTheme.Replace("{{CONTENT}}", htmlContent);
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private static ClientFileCache? ConvertToClientFileCache(
            Dictionary<string, byte[]>? clientFileCache,
            Dictionary<string, string>? fetchErrors = null)
        {
            if ((clientFileCache == null || clientFileCache.Count == 0) &&
                (fetchErrors == null || fetchErrors.Count == 0))
                return null;

            var cacheFolder = DiskCacheCleanupService.CacheFolder;

            // Merge content and error entries into parallel arrays
            var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (clientFileCache != null)
                foreach (var key in clientFileCache.Keys)
                    allKeys.Add(key);
            if (fetchErrors != null)
                foreach (var key in fetchErrors.Keys)
                    allKeys.Add(key);

            var filenames = new string[allKeys.Count];
            var contents = new byte[]?[allKeys.Count];
            var errors = new string?[allKeys.Count];
            var diskGuids = new string?[allKeys.Count];

            int i = 0;
            foreach (var key in allKeys)
            {
                filenames[i] = key;
                errors[i] = fetchErrors != null && fetchErrors.TryGetValue(key, out var e) ? e : null;

                var bytes = clientFileCache != null && clientFileCache.TryGetValue(key, out var b) ? b : null;
                if (bytes != null && bytes.Length > 51_200) // 50 KB
                {
                    // Use deterministic key so the same file always maps to the same cache file
                    var cacheKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
                    var cachePath = Path.Combine(cacheFolder, cacheKey + ".cache");
                    if (File.Exists(cachePath))
                    {
                        // Already cached — just touch to prevent cleanup
                        try { File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow); } catch { }
                    }
                    else
                    {
                        Directory.CreateDirectory(cacheFolder);
                        File.WriteAllBytes(cachePath, bytes);
                    }
                    contents[i] = null;
                    diskGuids[i] = cacheKey;
                }
                else
                {
                    contents[i] = bytes;
                    diskGuids[i] = null;
                }
                i++;
            }

            return new ClientFileCache
            {
                Filenames = filenames,
                Contents = contents,
                Errors = errors,
                DiskGuids = diskGuids,
                DiskCacheFolder = cacheFolder
            };
        }

        /// <summary>
        /// Processes included file content to respect #local and #global directives.
        /// Following the pattern from Calcpad.Cli.CalcpadReader.Include
        /// </summary>
        private static string ProcessIncludedContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            var isLocal = false;
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var outputLines = new List<string>();

            foreach (var line in lines)
            {
                if (Validator.IsKeyword(line, "#local"))
                {
                    isLocal = true;
                }
                else if (Validator.IsKeyword(line, "#global"))
                {
                    isLocal = false;
                }
                else
                {
                    // Only include lines that are not marked as local
                    if (!isLocal)
                    {
                        outputLines.Add(line);
                    }
                }
            }

            return string.Join(Environment.NewLine, outputLines);
        }
    }
}