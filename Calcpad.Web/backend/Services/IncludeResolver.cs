using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Utilities for extracting and resolving remote #include references.
    /// Handles pre-fetching of URLs and API router targets before Core/Highlighter processing.
    /// </summary>
    public static class IncludeResolver
    {
        private const int MaxRecursionDepth = 20;

        /// <summary>
        /// Extract filename from #include directive line content.
        /// Matches Core's MacroParser.ParseInclude extraction logic:
        /// finds first comment marker ('/") or field parameter (#), takes text before it.
        /// </summary>
        public static string ExtractFilename(ReadOnlySpan<char> lineContent)
        {
            int n = lineContent.Length;
            var quoteIdx = lineContent[8..].IndexOfAny('\'', '"');
            if (quoteIdx >= 0) quoteIdx += 8;
            var hashIdx = lineContent.Slice(1).LastIndexOf('#');
            if (hashIdx >= 0) hashIdx += 1;

            if (quoteIdx >= 9)
                n = quoteIdx;
            if (hashIdx > 0 && (n == lineContent.Length || hashIdx < n))
                n = hashIdx;
            if (n < 9)
                n = lineContent.Length;

            return lineContent[8..n].Trim().ToString();
        }

        /// <summary>
        /// Tests whether a filename looks like a direct URL (http:// or https://).
        /// </summary>
        public static bool IsUrl(string filename)
        {
            return filename.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   filename.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Tests whether a filename is a remote target (URL or API router syntax).
        /// </summary>
        public static bool IsRemoteTarget(string filename)
        {
            return IsUrl(filename) || filename.StartsWith('<');
        }

        /// <summary>
        /// Scans content for #include directives targeting remote resources (URLs or API routes),
        /// fetches them via the provided delegate, and recursively resolves nested includes.
        /// Returns a dictionary mapping cache key to (content, error) tuples.
        /// </summary>
        public static async Task<Dictionary<string, (string? content, string? error)>> ResolveRemoteIncludesAsync(
            string content,
            Func<string, Task<string>> fetchAsync,
            HashSet<string>? visited = null,
            int depth = 0)
        {
            var results = new Dictionary<string, (string? content, string? error)>(
                StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(content) || depth > MaxRecursionDepth)
                return results;

            visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var reader = new System.IO.StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.AsSpan().Trim();

                if (trimmed.StartsWith("'"))
                    continue;

                if (!trimmed.StartsWith("#include", StringComparison.OrdinalIgnoreCase) ||
                    (trimmed.Length > 8 && !char.IsWhiteSpace(trimmed[8])))
                    continue;

                var raw = ExtractFilename(trimmed);

                if (string.IsNullOrEmpty(raw) || !IsRemoteTarget(raw) || !visited.Add(raw))
                    continue;

                try
                {
                    var fetchedContent = await fetchAsync(raw).ConfigureAwait(false);
                    results[raw] = (fetchedContent, null);

                    var nested = await ResolveRemoteIncludesAsync(
                        fetchedContent, fetchAsync, visited, depth + 1)
                        .ConfigureAwait(false);

                    foreach (var kvp in nested)
                        results.TryAdd(kvp.Key, kvp.Value);
                }
                catch (Exception ex)
                {
                    results[raw] = (null, ex.Message);
                }
            }

            return results;
        }
    }
}
