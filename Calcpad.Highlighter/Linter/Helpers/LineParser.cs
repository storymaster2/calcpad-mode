using System;
using System.Collections.Generic;
using System.Text;

namespace Calcpad.Highlighter.Linter.Helpers
{
    public class CodeSegment
    {
        public string Text { get; set; }
        public int StartPos { get; set; }
        public int LineNumber { get; set; }
    }

    public class StringSegment
    {
        public string Text { get; set; }
        public int StartPos { get; set; }
        public int EndPos { get; set; }
        public int LineNumber { get; set; }
    }

    public class ParsedLine
    {
        public List<CodeSegment> CodeSegments { get; set; } = new();
        public List<StringSegment> StringSegments { get; set; } = new();
        public int LineNumber { get; set; }
        public string OriginalLine { get; set; }
    }

    public static class LineParser
    {
        public static ParsedLine ParseLine(string line, int lineNumber)
        {
            var result = new ParsedLine
            {
                LineNumber = lineNumber,
                OriginalLine = line
            };

            if (string.IsNullOrEmpty(line))
                return result;

            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            int segmentStart = 0;
            var segmentText = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '\'' && !inDoubleQuote)
                {
                    if (!inSingleQuote)
                    {
                        // Save code segment before string
                        if (segmentText.Length > 0)
                        {
                            result.CodeSegments.Add(new CodeSegment
                            {
                                Text = segmentText.ToString(),
                                StartPos = segmentStart,
                                LineNumber = lineNumber
                            });
                        }
                        inSingleQuote = true;
                        segmentStart = i;
                        segmentText.Clear();
                        segmentText.Append(c);
                    }
                    else
                    {
                        // End string segment
                        segmentText.Append(c);
                        result.StringSegments.Add(new StringSegment
                        {
                            Text = segmentText.ToString(),
                            StartPos = segmentStart,
                            EndPos = i + 1,
                            LineNumber = lineNumber
                        });
                        inSingleQuote = false;
                        segmentStart = i + 1;
                        segmentText.Clear();
                    }
                }
                else if (c == '"' && !inSingleQuote)
                {
                    if (!inDoubleQuote)
                    {
                        // Save code segment before string
                        if (segmentText.Length > 0)
                        {
                            result.CodeSegments.Add(new CodeSegment
                            {
                                Text = segmentText.ToString(),
                                StartPos = segmentStart,
                                LineNumber = lineNumber
                            });
                        }
                        inDoubleQuote = true;
                        segmentStart = i;
                        segmentText.Clear();
                        segmentText.Append(c);
                    }
                    else
                    {
                        // End string segment
                        segmentText.Append(c);
                        result.StringSegments.Add(new StringSegment
                        {
                            Text = segmentText.ToString(),
                            StartPos = segmentStart,
                            EndPos = i + 1,
                            LineNumber = lineNumber
                        });
                        inDoubleQuote = false;
                        segmentStart = i + 1;
                        segmentText.Clear();
                    }
                }
                else
                {
                    segmentText.Append(c);
                }
            }

            // Add remaining segment
            if (segmentText.Length > 0)
            {
                if (inSingleQuote || inDoubleQuote)
                {
                    // Unclosed string - treat as string segment
                    result.StringSegments.Add(new StringSegment
                    {
                        Text = segmentText.ToString(),
                        StartPos = segmentStart,
                        EndPos = line.Length,
                        LineNumber = lineNumber
                    });
                }
                else
                {
                    result.CodeSegments.Add(new CodeSegment
                    {
                        Text = segmentText.ToString(),
                        StartPos = segmentStart,
                        LineNumber = lineNumber
                    });
                }
            }

            return result;
        }

        public static bool IsCommentLine(string line)
        {
            var trimmed = line.AsSpan().TrimStart();
            return trimmed.Length > 0 && (trimmed[0] == '\'' || trimmed[0] == '"');
        }

        public static bool IsEmptyOrWhitespace(string line)
        {
            return string.IsNullOrWhiteSpace(line);
        }

        /// <summary>
        /// Checks if the line should be skipped during validation (empty, whitespace, or comment).
        /// </summary>
        public static bool ShouldSkipLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;

            var trimmed = line.AsSpan().TrimStart();
            return trimmed.Length > 0 && (trimmed[0] == '\'' || trimmed[0] == '"');
        }

        /// <summary>
        /// Checks if the trimmed line is a directive (starts with #).
        /// </summary>
        public static bool IsDirectiveLine(string trimmedLine)
        {
            return trimmedLine.Length > 0 && trimmedLine[0] == '#';
        }

        /// <summary>
        /// Checks if the trimmed line is a directive (starts with #). Span overload.
        /// </summary>
        public static bool IsDirectiveLine(ReadOnlySpan<char> trimmedLine)
        {
            return trimmedLine.Length > 0 && trimmedLine[0] == '#';
        }

        /// <summary>
        /// Checks if the trimmed line is a #def statement.
        /// </summary>
        public static bool IsDefStatement(string trimmedLine)
        {
            return IsDefStatement(trimmedLine.AsSpan());
        }

        /// <summary>
        /// Checks if the trimmed line is a #def statement. Span overload.
        /// </summary>
        public static bool IsDefStatement(ReadOnlySpan<char> trimmedLine)
        {
            return trimmedLine.StartsWith("#def ", StringComparison.OrdinalIgnoreCase) ||
                   trimmedLine.Equals("#def", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if the trimmed line is a #end def statement.
        /// </summary>
        public static bool IsEndDefStatement(string trimmedLine)
        {
            return IsEndDefStatement(trimmedLine.AsSpan());
        }

        /// <summary>
        /// Checks if the trimmed line is a #end def statement. Span overload.
        /// </summary>
        public static bool IsEndDefStatement(ReadOnlySpan<char> trimmedLine)
        {
            return trimmedLine.Equals("#end def", StringComparison.OrdinalIgnoreCase);
        }
    }
}
