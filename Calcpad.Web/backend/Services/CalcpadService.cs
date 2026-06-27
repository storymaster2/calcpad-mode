using Calcpad.Core;
using Calcpad.Server.Controllers;

namespace Calcpad.Server.Services
{
    public class CalcpadService
    {
        private readonly string _tempDirectory;
        private readonly string _htmlTemplate;
        private static readonly FileSettingsExtractor _fileSettingsExtractor = new();
        private static readonly NoPrintRegionStripper _noPrintRegionStripper = new();

        public CalcpadService()
        {
            _tempDirectory = Path.GetTempPath();
            _htmlTemplate = LoadHtmlTemplate();
        }

        /// <summary>
        /// Creates a synchronous Include delegate for MacroParser.
        /// Resolves #include targets against disk and direct URLs.
        /// Core calls this only when File.Exists is true on the resolved path; the delegate
        /// itself also short-circuits to disk for safety and falls through to URL fetch otherwise.
        /// </summary>
        internal static Func<string, Queue<string>, string> CreateIncludeDelegate(int timeoutMs)
        {
            return (fileName, fields) =>
            {
                try
                {
                    if (File.Exists(fileName))
                        return ProcessIncludedContent(File.ReadAllText(fileName));

                    if (Router.IsDirectUrl(fileName))
                    {
                        var bytes = Router.FetchUrlAsync(fileName, timeoutMs).GetAwaiter().GetResult();
                        return ProcessIncludedContent(System.Text.Encoding.UTF8.GetString(bytes));
                    }

                    return $"' File not found: {fileName}";
                }
                catch (Exception ex)
                {
                    FileLogger.LogError($"Error reading include file: {fileName}", ex);
                    return $"' Error reading file: {fileName} - {ex.Message}";
                }
            };
        }

        public Task<string> ConvertAsync(string calcpadContent, Settings? settings = null, bool forceUnwrappedCode = false, string theme = "light", string? sourceFilePath = null, bool forPrint = false, List<string>? openXmlExpressions = null) =>
            Task.FromResult(Convert(calcpadContent, settings, forceUnwrappedCode, theme, sourceFilePath, forPrint, openXmlExpressions));

        private string Convert(string calcpadContent, Settings? settings, bool forceUnwrappedCode, string theme, string? sourceFilePath, bool forPrint, List<string>? openXmlExpressions)
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

                // 2. Parse macros and includes (server reads referenced files from disk).
                var macroParser = new MacroParser
                {
                    Include = CreateIncludeDelegate(timeoutMs: 10000),
                    SourceFilePath = sourceFilePath
                };

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
                        var parser = new ExpressionParser { Settings = coreSettings, SourceFilePath = sourceFilePath };
                        parser.Parse(outputText, true, openXmlExpressions != null);
                        htmlResult = RemoveEmptyParagraphs(parser.HtmlResult);
                        openXmlExpressions?.AddRange(parser.OpenXmlExpressions);
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