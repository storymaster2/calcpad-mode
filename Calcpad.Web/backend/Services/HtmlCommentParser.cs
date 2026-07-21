using System;
using System.Collections.Generic;
using System.Text.Json;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Server.Services
{
    public enum HtmlCommentParseStatus
    {
        Success,
        InvalidJson
    }

    public sealed class HtmlCommentBlock
    {
        /// <summary>Zero-based start line in the tokenizer result</summary>
        public int StartLine { get; init; }

        /// <summary>Zero-based end line in the tokenizer result (inclusive)</summary>
        public int EndLine { get; init; }

        /// <summary>Raw text between &lt;!-- and --&gt;, trimmed</summary>
        public string? RawJson { get; init; }

        /// <summary>Parsed JSON element (cloned); null if parsing failed</summary>
        public JsonElement? Data { get; init; }

        /// <summary>JSON parse error message; null on success</summary>
        public string? ParseError { get; init; }

        public HtmlCommentParseStatus Status { get; init; }
    }

    /// <summary>
    /// Extracts and parses JSON payloads embedded in Calcpad HTML comment syntax.
    ///
    /// Supported forms (all produce the same result):
    ///   '&lt;!--{json: "hello"}--&gt;
    ///   '&lt;!--{json: "hello"}--&gt;'
    ///   '&lt;!--{          (multi-line, no inner closing quotes)
    ///   'json: "hello"
    ///   '}
    ///   '--&gt;
    ///   '&lt;!--{'         (multi-line, inner closing quotes per line)
    ///   'json: "hello"'
    ///   '}'
    ///   '--&gt;'
    ///
    /// Requires that the tokenizer has been run with <see cref="CalcpadTokenizer._inHtmlComment"/>
    /// state tracking enabled, so all lines of a multi-line block are typed as HtmlComment.
    /// A line gap (non-consecutive line numbers) in the HtmlComment token stream terminates
    /// an open block without emitting a result.
    /// </summary>
    public sealed class HtmlCommentParser
    {
        private const string OpenMarker  = "<!--";
        private const string CloseMarker = "-->";

        public IReadOnlyList<HtmlCommentBlock> Parse(TokenizerResult tokenizerResult)
        {
            if (tokenizerResult == null)
                throw new ArgumentNullException(nameof(tokenizerResult));

            var results = new List<HtmlCommentBlock>();

            var state    = ParseState.Normal;
            var buffer   = new List<string>();
            int startLine = -1;
            int lastLine  = -1;

            foreach (var token in tokenizerResult.Tokens)
            {
                if (token.Type != TokenType.HtmlComment)
                    continue;

                var content = StripCommentQuotes(token.Text);

                if (state == ParseState.Normal)
                {
                    int openIdx = content.IndexOf(OpenMarker, StringComparison.Ordinal);
                    if (openIdx < 0)
                        continue;

                    var afterOpen = content[(openIdx + OpenMarker.Length)..];
                    int closeIdx  = afterOpen.IndexOf(CloseMarker, StringComparison.Ordinal);

                    if (closeIdx >= 0)
                    {
                        // Single-line block
                        var block = BuildBlock(token.Line, token.Line, afterOpen[..closeIdx]);
                        if (block != null)
                            results.Add(block);
                    }
                    else
                    {
                        // Start of multi-line block
                        buffer.Clear();
                        buffer.Add(afterOpen);
                        startLine = token.Line;
                        lastLine  = token.Line;
                        state     = ParseState.InHtmlComment;
                    }
                }
                else // InHtmlComment
                {
                    if (token.Line > lastLine + 1)
                    {
                        // Non-consecutive line — block broken, discard and re-evaluate as new opener
                        buffer.Clear();
                        state = ParseState.Normal;

                        int openIdx = content.IndexOf(OpenMarker, StringComparison.Ordinal);
                        if (openIdx < 0)
                            continue;

                        var afterOpen = content[(openIdx + OpenMarker.Length)..];
                        int closeIdx  = afterOpen.IndexOf(CloseMarker, StringComparison.Ordinal);

                        if (closeIdx >= 0)
                        {
                            var block = BuildBlock(token.Line, token.Line, afterOpen[..closeIdx]);
                            if (block != null)
                                results.Add(block);
                        }
                        else
                        {
                            buffer.Add(afterOpen);
                            startLine = token.Line;
                            lastLine  = token.Line;
                            state     = ParseState.InHtmlComment;
                        }

                        continue;
                    }

                    int closingIdx = content.IndexOf(CloseMarker, StringComparison.Ordinal);
                    if (closingIdx >= 0)
                    {
                        buffer.Add(content[..closingIdx]);
                        var block = BuildBlock(startLine, token.Line, string.Join("\n", buffer));
                        if (block != null)
                            results.Add(block);
                        buffer.Clear();
                        state = ParseState.Normal;
                    }
                    else
                    {
                        buffer.Add(content);
                        lastLine = token.Line;
                    }
                }
            }

            // Any open block at end of stream is silently discarded (unterminated)
            return results;
        }

        /// <summary>
        /// Strips the leading comment-quote character (<c>'</c> or <c>"</c>) and,
        /// if the remainder ends with the same character, the trailing one too.
        /// </summary>
        private static string StripCommentQuotes(string text)
        {
            if (text.Length == 0)
                return text;

            char first = text[0];
            if (first != '\'' && first != '"')
                return text;

            var inner = text[1..];
            if (inner.Length > 0 && inner[^1] == first)
                inner = inner[..^1];

            return inner;
        }

        private static HtmlCommentBlock? BuildBlock(int startLine, int endLine, string rawJson)
        {
            rawJson = rawJson.Trim();
            if (rawJson.Length == 0)
                return null;

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                return new HtmlCommentBlock
                {
                    StartLine = startLine,
                    EndLine   = endLine,
                    RawJson   = rawJson,
                    Data      = doc.RootElement.Clone(),
                    Status    = HtmlCommentParseStatus.Success
                };
            }
            catch (JsonException ex)
            {
                return new HtmlCommentBlock
                {
                    StartLine  = startLine,
                    EndLine    = endLine,
                    RawJson    = rawJson,
                    ParseError = ex.Message,
                    Status     = HtmlCommentParseStatus.InvalidJson
                };
            }
        }

        private enum ParseState
        {
            Normal,
            InHtmlComment
        }
    }
}
