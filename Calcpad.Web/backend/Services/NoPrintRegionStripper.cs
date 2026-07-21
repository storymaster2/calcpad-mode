using System;
using System.Collections.Generic;
using System.Text.Json;
using Calcpad.Highlighter.Tokenizer;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Strips regions of Calcpad source delimited by <c>NoPrintStart</c> / <c>NoPrintEnd</c>
    /// HTML comment markers. Used to omit sections from PDF output while keeping them in
    /// the on-screen render.
    ///
    /// Supported syntax:
    /// <code>
    ///   '&lt;!--{"NoPrintStart": true}--&gt;
    ///   ... lines that should not appear in PDF ...
    ///   '&lt;!--{"NoPrintEnd": true}--&gt;
    /// </code>
    /// Rules:
    /// - Matching is stack-based; nested regions collapse to the outermost on close.
    /// - The marker lines themselves are also removed.
    /// - An unmatched <c>NoPrintStart</c> without a closing <c>NoPrintEnd</c> strips
    ///   from the marker through end-of-file.
    /// - The JSON value is unused; only the property's presence matters.
    /// </summary>
    internal sealed class NoPrintRegionStripper
    {
        private const string StartKey = "NoPrintStart";
        private const string EndKey   = "NoPrintEnd";

        private static readonly HtmlCommentParser _parser = new();
        private static readonly CalcpadTokenizer _tokenizer = new();

        /// <summary>
        /// Returns <paramref name="content"/> with all NoPrint regions removed.
        /// If no markers are present, the original string is returned unchanged.
        /// </summary>
        public string Strip(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            var tokens = _tokenizer.Tokenize(content);
            var blocks = _parser.Parse(tokens);

            var openStack = new Stack<int>();      // Block.StartLine of unmatched NoPrintStart
            var ranges    = new List<(int Start, int End)>();

            foreach (var block in blocks)
            {
                if (block.Status != HtmlCommentParseStatus.Success || !block.Data.HasValue)
                    continue;

                var data = block.Data.Value;
                if (data.ValueKind != JsonValueKind.Object)
                    continue;

                if (HasProperty(data, StartKey))
                {
                    openStack.Push(block.StartLine);
                }
                else if (HasProperty(data, EndKey))
                {
                    if (openStack.Count == 0)
                        continue;

                    // Collapse nested opens — only emit a range when the outermost closes.
                    int outermostStart = openStack.Pop();
                    while (openStack.Count > 0)
                        outermostStart = openStack.Pop();

                    ranges.Add((outermostStart, block.EndLine));
                }
            }

            // Unmatched NoPrintStart → strip to EOF
            if (openStack.Count > 0)
            {
                int outermostStart = openStack.Pop();
                while (openStack.Count > 0)
                    outermostStart = openStack.Pop();

                ranges.Add((outermostStart, int.MaxValue));
            }

            if (ranges.Count == 0)
                return content;

            return RemoveLines(content, ranges);
        }

        private static bool HasProperty(JsonElement obj, string name)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string RemoveLines(string content, List<(int Start, int End)> ranges)
        {
            var lines = content.Split('\n');
            var keep = new bool[lines.Length];
            for (int i = 0; i < keep.Length; i++)
                keep[i] = true;

            foreach (var (start, end) in ranges)
            {
                int s = Math.Max(0, start);
                int e = Math.Min(lines.Length - 1, end);
                for (int i = s; i <= e; i++)
                    keep[i] = false;
            }

            var sb = new System.Text.StringBuilder(content.Length);
            bool first = true;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!keep[i]) continue;
                if (!first) sb.Append('\n');
                sb.Append(lines[i]);
                first = false;
            }
            return sb.ToString();
        }
    }
}
