using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Calcpad.Core
{
    internal static class StringCalculator
    {
        private static readonly FrozenDictionary<string, Func<string, string>> Functions =
        new Dictionary<string, Func<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "len$", Len },
            { "trim$", Trim },
            { "ltrim$", LTrim },
            { "rtrim$", RTrim },
            { "ucase$", UCase },
            { "lcase$", LCase },
            { "string$", StringFrom },
            { "val$", Val },
            { "space$", Space },
            { "chr$", Chr },
            { "tableToStringArray$", TableToStringArrayStub },
            { "typeOf$", TypeOfStub },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, Func<string, string, string>> Functions2 =
        new Dictionary<string, Func<string, string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "left$", Left },
            { "right$", Right },
            { "compare$", Compare },
            { "find$", Find },
            { "parsejson$", ParseJson },
            { "table$", TableStub },
            { "rowToStringArray$", RowToStringArrayStub },
            { "colToStringArray$", ColToStringArrayStub },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, Func<string, string, string, string>> Functions3 =
        new Dictionary<string, Func<string, string, string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "mid$", Mid },
            { "replace$", Replace },
            { "instr$", InStr },
            { "split$", SplitStub },
            { "join$", JoinStub },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, Func<string[], string>> MultiFunctions =
        new Dictionary<string, Func<string[], string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "concat$", Concat },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        internal static readonly FrozenSet<string> NumericResultFunctions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "len$",
            "val$",
            "compare$",
            "instr$",
            "find$",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        internal static bool IsFunction(string name) =>
            Functions.ContainsKey(name) ||
            Functions2.ContainsKey(name) ||
            Functions3.ContainsKey(name) ||
            MultiFunctions.ContainsKey(name);

        internal static bool ReturnsNumeric(string name) =>
            NumericResultFunctions.Contains(name);

        internal static int GetArity(string name)
        {
            if (Functions.ContainsKey(name)) return 1;
            if (Functions2.ContainsKey(name)) return 2;
            if (Functions3.ContainsKey(name)) return 3;
            if (MultiFunctions.ContainsKey(name)) return -1; // variadic
            return 0;
        }

        internal static string Evaluate(string name, string[] args)
        {
            if (args.Length == 1 && Functions.TryGetValue(name, out var f1))
                return f1(args[0]);
            if (args.Length == 2 && Functions2.TryGetValue(name, out var f2))
                return f2(args[0], args[1]);
            if (args.Length == 3 && Functions3.TryGetValue(name, out var f3))
                return f3(args[0], args[1], args[2]);
            if (MultiFunctions.TryGetValue(name, out var fm))
                return fm(args);
            throw new MathParserException($"Unknown string function: {name}");
        }

        // --- 1-arg functions ---

        private static string Len(string a) =>
            a.Length.ToString(CultureInfo.InvariantCulture);

        private static string Trim(string a) => a.Trim();

        private static string LTrim(string a) => a.TrimStart();

        private static string RTrim(string a) => a.TrimEnd();

        private static string UCase(string a) => a.ToUpperInvariant();

        private static string LCase(string a) => a.ToLowerInvariant();

        private static string StringFrom(string a) => a;

        private static string Val(string a)
        {
            if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d.ToString(CultureInfo.InvariantCulture);
            return "0/0";
        }

        // --- 2-arg functions ---

        private static string Left(string a, string b)
        {
            var count = ParseInt(b);
            if (count < 0) count = 0;
            if (count > a.Length) count = a.Length;
            return a[..count];
        }

        private static string Right(string a, string b)
        {
            var count = ParseInt(b);
            if (count < 0) count = 0;
            if (count > a.Length) count = a.Length;
            return a[^count..];
        }

        private static string Compare(string a, string b)
        {
            var result = string.Compare(a, b, StringComparison.Ordinal);
            return Math.Sign(result).ToString(CultureInfo.InvariantCulture);
        }

        private static string Space(string a)
        {
            var count = ParseInt(a);
            if (count < 0) count = 0;
            return new string(' ', count);
        }

        // Named-character lookup. Returns the literal character that string
        // literals can't carry directly (newline, tab, etc.). Add new entries
        // here and a matching snippet in FunctionSnippets.cs.
        private static readonly FrozenDictionary<string, string> NamedChars =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "newline", "\n" },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static string Chr(string a)
        {
            if (NamedChars.TryGetValue(a, out var c))
                return c;
            throw new MathParserException(
                $"chr$: unknown character name \"{a}\". Known names: {string.Join(", ", NamedChars.Keys)}.");
        }

        // --- 3-arg functions ---

        private static string Mid(string a, string b, string c)
        {
            var start = ParseInt(b) - 1; // 1-based to 0-based
            var count = ParseInt(c);
            if (start < 0) start = 0;
            if (start >= a.Length) return string.Empty;
            if (start + count > a.Length) count = a.Length - start;
            if (count < 0) count = 0;
            return a.Substring(start, count);
        }

        private static string Replace(string a, string b, string c) =>
            a.Replace(b, c);

        private static string InStr(string a, string b, string c)
        {
            var startPos = ParseInt(a);
            if (startPos < 1) startPos = 1;
            var startIndex = startPos - 1; // 1-based to 0-based
            if (startIndex >= b.Length)
                return "0";
            var pos = b.IndexOf(c, startIndex, StringComparison.Ordinal);
            return (pos < 0 ? 0 : pos + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static string Find(string needle, string haystack)
        {
            var indices = new List<int>();
            var offset = 0;
            while (offset < haystack.Length)
            {
                var pos = haystack.IndexOf(needle, offset, StringComparison.Ordinal);
                if (pos < 0) break;
                indices.Add(pos + 1); // 1-based
                offset = pos + 1;
            }
            if (indices.Count == 0)
                return "0";
            if (indices.Count == 1)
                return indices[0].ToString(CultureInfo.InvariantCulture);
            return "[" + string.Join("; ", indices) + "]";
        }

        // --- JSON functions ---

        private static string ParseJson(string json, string path)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new MathParserException($"parsejson$: Invalid JSON - {ex.Message}");
            }

            using (doc)
            {
                var current = doc.RootElement;

                if (!string.IsNullOrEmpty(path))
                {
                    foreach (var segment in ParseJsonPath(path))
                    {
                        if (segment.IsIndex)
                        {
                            if (current.ValueKind != JsonValueKind.Array)
                                throw new MathParserException(
                                    $"parsejson$: Cannot index non-array with [{segment.Index}].");
                            if (segment.Index < 0 || segment.Index >= current.GetArrayLength())
                                throw new MathParserException(
                                    $"parsejson$: Array index [{segment.Index}] out of range.");
                            current = current[segment.Index];
                        }
                        else
                        {
                            if (current.ValueKind != JsonValueKind.Object)
                                throw new MathParserException(
                                    $"parsejson$: Cannot access property \"{segment.Name}\" on non-object.");
                            if (!current.TryGetProperty(segment.Name, out var next))
                                throw new MathParserException(
                                    $"parsejson$: Property \"{segment.Name}\" not found.");
                            current = next;
                        }
                    }
                }

                return current.ValueKind switch
                {
                    JsonValueKind.String => current.GetString() ?? string.Empty,
                    JsonValueKind.Number => current.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => string.Empty,
                    _ => current.GetRawText(),
                };
            }
        }

        private readonly struct JsonPathSegment
        {
            public string Name { get; init; }
            public int Index { get; init; }
            public bool IsIndex { get; init; }
        }

        private static List<JsonPathSegment> ParseJsonPath(string path)
        {
            var segments = new List<JsonPathSegment>();
            int i = 0;

            while (i < path.Length)
            {
                if (path[i] == '[')
                {
                    i++;
                    if (i < path.Length && (path[i] == '"' || path[i] == '\''))
                    {
                        var quote = path[i];
                        i++;
                        int start = i;
                        while (i < path.Length && path[i] != quote) i++;
                        segments.Add(new JsonPathSegment { Name = path[start..i] });
                        if (i < path.Length) i++;
                        if (i < path.Length && path[i] == ']') i++;
                    }
                    else
                    {
                        int start = i;
                        while (i < path.Length && path[i] != ']') i++;
                        var indexStr = path[start..i];
                        if (i < path.Length) i++;
                        if (!int.TryParse(indexStr, out var idx))
                            throw new MathParserException(
                                $"parsejson$: Invalid array index \"{indexStr}\".");
                        segments.Add(new JsonPathSegment { Index = idx, IsIndex = true });
                    }
                }
                else if (path[i] == '.')
                {
                    i++;
                }
                else
                {
                    int start = i;
                    while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                    segments.Add(new JsonPathSegment { Name = path[start..i] });
                }
            }

            return segments;
        }

        // --- Variadic functions ---

        private static string Concat(string[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
                sb.Append(values[i]);
            return sb.ToString();
        }

        // --- Stubs (dispatched by ExpressionParser before reaching here) ---

        private static string TableStub(string a, string b) =>
            throw new MathParserException("table$ must be used in #table declaration.");

        private static string SplitStub(string a, string b, string c) =>
            throw new MathParserException("split$ must be used in #table declaration.");

        private static string JoinStub(string a, string b, string c) =>
            throw new MathParserException("join$ is handled by ExpressionParser.");

        private static string RowToStringArrayStub(string a, string b) =>
            throw new MathParserException("rowToStringArray$ is handled by ExpressionParser.");

        private static string ColToStringArrayStub(string a, string b) =>
            throw new MathParserException("colToStringArray$ is handled by ExpressionParser.");

        private static string TableToStringArrayStub(string a) =>
            throw new MathParserException("tableToStringArray$ is handled by ExpressionParser.");

        private static string TypeOfStub(string a) =>
            throw new MathParserException("typeOf$ is handled by ExpressionParser.");

        // --- Helpers ---

        private static int ParseInt(string s)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return (int)d;
            return 0;
        }
    }
}
