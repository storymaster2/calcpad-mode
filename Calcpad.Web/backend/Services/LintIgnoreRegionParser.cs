using System;
using System.Collections.Generic;
using System.Text.Json;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Extracts <see cref="LintIgnoreRegion"/> list from HTML comment blocks embedded in
    /// raw Calcpad source. Regions are expressed in original source line numbers (0-based)
    /// and are passed directly to <c>CalcpadLinter.Lint()</c>.
    ///
    /// Supported syntax:
    /// <code>
    ///   '&lt;!--{"LintIgnore": ["CPD-3301", "CPD-3302"]}--&gt;
    ///   code
    ///   '&lt;!--{"EndLintIgnore": ["CPD-3301", "CPD-3302"]}--&gt;
    /// </code>
    /// Rules:
    /// - Codes are case-insensitive. Empty array = suppress all codes in the region.
    /// - Matching is stack-based; codes in the EndLintIgnore tag are ignored for pairing.
    /// - An unmatched LintIgnore without EndLintIgnore suppresses until EOF.
    /// </summary>
    internal sealed class LintIgnoreRegionParser
    {
        private static readonly HtmlCommentParser _parser = new();
        private static readonly CalcpadTokenizer _tokenizer = new();

        /// <summary>
        /// Tokenizes <paramref name="content"/> and returns all lint-ignore regions
        /// found in HTML comment blocks.
        /// </summary>
        public IReadOnlyList<LintIgnoreRegion> ExtractRegions(string content)
        {
            if (string.IsNullOrEmpty(content))
                return [];

            var tokens = _tokenizer.Tokenize(content);
            var blocks = _parser.Parse(tokens);

            var openStack = new Stack<(int EndLine, IReadOnlySet<string> Codes)>();
            var result = new List<LintIgnoreRegion>();

            foreach (var block in blocks)
            {
                if (block.Status != HtmlCommentParseStatus.Success || !block.Data.HasValue)
                    continue;

                var data = block.Data.Value;

                if (data.TryGetProperty("LintIgnore", out var openEl)
                    && openEl.ValueKind == JsonValueKind.Array)
                {
                    var codes = ParseCodes(openEl);
                    openStack.Push((block.EndLine, codes));
                }
                else if (data.TryGetProperty("EndLintIgnore", out _))
                {
                    if (openStack.Count == 0)
                        continue;

                    var open = openStack.Pop();
                    var startLine = open.EndLine + 1;
                    var endLine = block.StartLine - 1;

                    if (startLine <= endLine)
                        result.Add(new LintIgnoreRegion(startLine, endLine, open.Codes));
                }
            }

            // Unmatched opens → suppress to EOF
            while (openStack.Count > 0)
            {
                var open = openStack.Pop();
                result.Add(new LintIgnoreRegion(open.EndLine + 1, int.MaxValue, open.Codes));
            }

            return result;
        }

        private static IReadOnlySet<string> ParseCodes(JsonElement arrayElement)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var code = item.GetString();
                    if (!string.IsNullOrWhiteSpace(code))
                        set.Add(code.Trim());
                }
            }
            return set;
        }
    }
}
