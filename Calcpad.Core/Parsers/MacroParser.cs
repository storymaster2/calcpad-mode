using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace Calcpad.Core
{
    public class MacroParser
    {
        private readonly struct Macro
        {
            private readonly string _contents;
            private readonly string[] _parameters;
            private readonly string[] _defaults;  // null element = required, string = default value
            private readonly int[] _order;

            internal Macro(string contents, List<string> parameters, List<string> defaults)
            {
                _contents = contents;
                if (parameters is null)
                {
                    _parameters = null;
                    _defaults = null;
                    _order = null;
                }
                else
                {
                    _parameters = [.. parameters];
                    _defaults = defaults is null ? new string[parameters.Count] : [.. defaults];
                    _order = Sort(_parameters);
                }
            }

            private static int[] Sort(string[] parameters)
            {
                var n = parameters.Length;
                var sorted = new SortedList<string, int>();
                for (int i = 0; i < n; ++i)
                {
                    var s = new string(parameters[i].Reverse().ToArray());
                    try
                    {
                        sorted.Add(s, i);
                    }
                    catch (ArgumentException)
                    {
                        throw Exceptions.DuplicateMacroParameters(s);
                    }
                }
                return sorted.Values.Reverse().ToArray();
            }

            internal int GetParameterIndex(string name)
            {
                if (_parameters is null) return -1;
                for (int i = 0; i < _parameters.Length; i++)
                    if (_parameters[i] == name) return i;
                return -1;
            }

            internal string[] ResolveArguments(List<string> positional, Dictionary<string, string> keywords)
            {
                if (positional.Count > ParameterCount)
                    throw Exceptions.InvalidNumberOfArguments();

                var result = new string[ParameterCount];

                for (int i = 0; i < positional.Count; i++)
                    result[i] = positional[i];

                foreach (var kv in keywords)
                {
                    var idx = GetParameterIndex(kv.Key);
                    if (idx < 0) throw Exceptions.UnknownKeywordArgument(kv.Key);
                    if (result[idx] is not null) throw Exceptions.DuplicateArgument(kv.Key);
                    result[idx] = kv.Value;
                }

                for (int i = 0; i < ParameterCount; i++)
                {
                    if (result[i] is null)
                    {
                        if (_defaults[i] is null) throw Exceptions.InvalidNumberOfArguments();
                        result[i] = _defaults[i];
                    }
                }
                return result;
            }

            internal string Run(string[] arguments)
            {
                if (ParameterCount == 0)
                    return _contents;

                var sb = new StringBuilder(_contents);
                for (int i = 0, count = _order.Length; i < count; ++i)
                {
                    var j = _order[i];
                    var s = arguments[j];
                    if (s.Length > 1 && s[0] == ' ')
                        sb.Replace(_parameters[j], s[1..]);
                    else
                        sb.Replace(_parameters[j], s);
                }
                return sb.ToString();
            }

            internal bool IsEmpty => _contents is null;
            internal int ParameterCount => _parameters?.Length ?? 0;
            internal int RequiredCount
            {
                get
                {
                    if (_defaults is null) return 0;
                    int count = 0;
                    foreach (var d in _defaults) if (d is null) count++;
                    return count;
                }
            }
        }

        private enum Keywords
        {
            None,
            Def,
            EndDef,
            Include,
        }
        private readonly List<int> _lineNumbers = [];
        private readonly HashSet<string> _includeStack = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Macro> Macros = new(StringComparer.Ordinal);
        public Func<string, Queue<string>, string> Include;
        public ClientFileCache ClientFileCache;
        public string SourceFilePath;

        private static Keywords GetKeyword(ReadOnlySpan<char> s)
        {
            if (s.Length < 4)
                return Keywords.None;
            if (s.StartsWith("#def", StringComparison.OrdinalIgnoreCase))
                return Keywords.Def;
            if (s.StartsWith("#end def", StringComparison.OrdinalIgnoreCase))
                return Keywords.EndDef;
            if (s.StartsWith("#include", StringComparison.OrdinalIgnoreCase))
                return Keywords.Include;

            return Keywords.None;
        }

        private int _parsedLineNumber;
        public bool Parse(string sourceCode, out string outCode, StringBuilder sb, int includeLine, bool addLineNumbers)
        {
            var sourceLines = sourceCode.EnumerateLines();
            if (includeLine == 0)
            {
                sb = new StringBuilder(sourceCode.Length);
                Macros.Clear();
                _lineNumbers.Clear();
                _includeStack.Clear();
                _parsedLineNumber = 0;
            }
            var macroBuilder = new StringBuilder(1000);
            var macroName = string.Empty;
            var lineNumber = includeLine;
            var macroDefCount = 0;
            var hasErrors = false;
            ReadOnlySpan<char> lineContent = "code";
            List<string> macroParameters = null;
            List<string> macroDefaults = null;
            try
            {
                foreach (ReadOnlySpan<char> sourceLine in sourceLines)
                {
                    if (includeLine == 0)
                    {
                        _lineNumbers.Add(_parsedLineNumber);
                        ++lineNumber;
                    }

                    lineContent = sourceLine.TrimStart();
                    if (lineContent.IsEmpty)
                    {
                        AppendLine(sourceLine);
                        continue;
                    }
                    if (lineContent[0] == '#' && ParseKeyword(lineContent))
                        continue;

                    if (macroDefCount == 1)
                    {
                        macroBuilder.Append(sourceLine).AppendLine();
                        continue;
                    }

                    if (Macros.Count != 0)
                    {
                        try
                        {
                            var insertCode = ApplyMacros(sourceLine);
                            var insertLines = insertCode.EnumerateLines();
                            foreach (var line in insertLines)
                                AppendLine(line);
                        }
                        catch (Exception ex)
                        {
                            AppendError(lineContent, ex.Message);
                        }
                        continue;
                    }
                    AppendLine(sourceLine);
                }
                if (includeLine == 0)
                    _lineNumbers.Add(_parsedLineNumber);

                if (macroDefCount > 0)
                {
                    sb.Append(Messages.Macro_definition_block_not_closed_Missing_end_def);
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                AppendError(lineContent, ex.Message);
            }
            finally
            {
                outCode = sb.ToString();
            }
            return hasErrors;

            bool ParseKeyword(ReadOnlySpan<char> lineContent)
            {
                var keyword = GetKeyword(lineContent);
                switch (keyword)
                {
                    case Keywords.Include:
                        ParseInclude(lineContent);
                        return true;
                    case Keywords.Def:
                        ParseDef(lineContent);
                        return true;
                    case Keywords.EndDef:
                        ParseEndDef(lineContent);
                        return true;
                    default:
                        return false;
                }
            }

            void ParseInclude(ReadOnlySpan<char> lineContent)
            {
                int n = lineContent.Length;
                if (n < 9)
                    AppendError(lineContent, Messages.Missing_source_file_for_include);
                n = lineContent.IndexOfAny('\'', '"');
                var nf1 = lineContent.LastIndexOf('#');
                if (n < 9 || nf1 > 0 && nf1 < n)
                    n = nf1;

                if (n < 9)
                    n = lineContent.Length;

                var rawFileName = lineContent[8..n].Trim().ToString();

                Queue<string> fields = new();
                if (nf1 > 0)
                {
                    var nf2 = lineContent.LastIndexOf('}');
                    if (nf2 < 0)
                        AppendError(lineContent, Messages.Brackets_not_closed);
                    else
                    {
                        SplitEnumerator split = lineContent[(nf1 + 2)..nf2].EnumerateSplits(';');
                        foreach (var item in split)
                            fields.Enqueue(item.Trim().ToString());
                    }
                }

                // Resolve relative to source file directory when available
                var sourceDir = !string.IsNullOrEmpty(SourceFilePath)
                    ? Path.GetDirectoryName(SourceFilePath) : null;

                // Try filesystem first
                bool fileExists = false;
                string resolvedPath = null;
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(rawFileName);
                    resolvedPath = sourceDir != null
                        ? Path.GetFullPath(expanded, sourceDir)
                        : Path.GetFullPath(expanded);
                    fileExists = File.Exists(resolvedPath);
                }
                catch { /* Not a valid filesystem path (e.g., URLs, API syntax) */ }

                // Detect circular includes
                var includeKey = resolvedPath ?? rawFileName;
                if (!_includeStack.Add(includeKey))
                {
                    AppendError(lineContent, $"Circular #include detected: {rawFileName}");
                    return;
                }

                try
                {
                    if (fileExists)
                    {
                        var savedSourcePath = SourceFilePath;
                        SourceFilePath = resolvedPath;
                        try { Parse(Include(resolvedPath, fields), out _, sb, lineNumber, addLineNumbers); }
                        finally { SourceFilePath = savedSourcePath; }
                        return;
                    }

                    var cacheKey = resolvedPath ?? rawFileName;
                    var fallbackKey = resolvedPath != null ? rawFileName : null;
                    if (ClientFileCache != null && ClientFileCache.TryGetContentMultiKey(cacheKey, fallbackKey, out var cachedContent))
                    {
                        var savedSourcePath = SourceFilePath;
                        SourceFilePath = resolvedPath;
                        try { Parse(cachedContent, out _, sb, lineNumber, addLineNumbers); }
                        finally { SourceFilePath = savedSourcePath; }
                        return;
                    }

                    if (ClientFileCache != null && ClientFileCache.TryGetErrorMultiKey(cacheKey, fallbackKey, out var cachedError))
                    {
                        AppendError(lineContent, cachedError);
                        return;
                    }

                    AppendError(lineContent, Messages.File_not_found);
                }
                finally
                {
                    _includeStack.Remove(includeKey);
                }
            }

            void ParseDef(ReadOnlySpan<char> lineContent)
            {
                var textSpan = new TextSpan(lineContent);
                if (macroDefCount == 0)
                {
                    int i = 4, len = lineContent.Length;
                    var c = EatSpace(lineContent, ref i);
                    textSpan.Reset(i);
                    while (i < len)
                    {
                        c = lineContent[i];
                        if (c == '$')
                        {
                            textSpan.Expand();
                            macroName = textSpan.ToString();
                            break;
                        }
                        if (Validator.IsMacroLetter(c, textSpan.Length))
                            textSpan.Expand();
                        else
                        {
                            SymbolError(lineContent, c);
                            break;
                        }
                        ++i;
                    }
                    c = EatSpace(lineContent, ref i);
                    if (c == '(')
                    {
                        macroParameters = [];
                        macroDefaults = [];
                        bool seenOptional = false;
                        c = EatSpace(lineContent, ref i);
                        textSpan.Reset(i);
                        while (i < len)
                        {
                            c = lineContent[i];
                            if (c == ' ') { c = EatSpace(lineContent, ref i); continue; }
                            if (c == '$')
                            {
                                textSpan.Expand();
                                var paramName = textSpan.ToString();
                                string defaultValue = null;
                                c = EatSpace(lineContent, ref i);
                                if (c == '=')
                                {
                                    seenOptional = true;
                                    c = EatSpace(lineContent, ref i);
                                    textSpan.Reset(i);
                                    int depth = 0;
                                    while (i < len)
                                    {
                                        c = lineContent[i];
                                        if (c == '(') depth++;
                                        else if (c == ')') { if (depth == 0) break; depth--; }
                                        else if (c == ';' && depth == 0) break;
                                        textSpan.Expand();
                                        if (++i < len) c = lineContent[i]; else break;
                                    }
                                    defaultValue = textSpan.Cut().ToString().Trim();
                                }
                                else if (seenOptional)
                                    throw Exceptions.RequiredParameterAfterOptional(paramName);

                                macroParameters.Add(paramName);
                                macroDefaults.Add(defaultValue);

                                if (c == ')') break;
                                c = EatSpace(lineContent, ref i);
                                textSpan.Reset(i);
                                continue;
                            }
                            if (Validator.IsMacroLetter(c, textSpan.Length))
                                textSpan.Expand();
                            else if (c != '\n')
                                SymbolError(lineContent, c);
                            c = lineContent[++i];
                        }
                        c = EatSpace(lineContent, ref i);
                    }
                    else
                        macroParameters = null;

                    if (c == '=')
                    {
                        c = EatSpace(lineContent, ref i);
                        AddMacro(lineContent, macroName, new Macro(lineContent[i..].ToString(), macroParameters, macroDefaults));
                        macroName = string.Empty;
                        macroDefaults = null;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(macroName))
                        {
                            macroName = textSpan.ToString();
                            AppendError(lineContent, string.Format(Messages.Invalid_macro_name_0, macroName));

                            macroName = string.Empty;
                            textSpan.Reset(i);
                        }
                        ++macroDefCount;
                    }
                }
                else
                {
                    AppendError(lineContent, Messages.Invalid_inside_macro_definition);
                    ++macroDefCount;
                }
            }

            void ParseEndDef(ReadOnlySpan<char> lineContent)
            {
                if (macroDefCount < 1)
                {
                    AppendError(lineContent, Messages.There_is_no_matching_def);
                }
                else
                {
                    macroBuilder.RemoveLastLineIfEmpty();
                    var macroContent = macroBuilder.ToString();
                    AddMacro(lineContent, macroName, new Macro(macroContent, macroParameters, macroDefaults));
                    macroName = string.Empty;
                    macroDefaults = null;
                    macroBuilder.Clear();
                }
                --macroDefCount;
            }

            void AppendLine(ReadOnlySpan<char> line)
            {
                if (addLineNumbers)
                    sb.Append(line).Append('\v').Append(lineNumber).AppendLine();
                else
                    sb.Append(line).AppendLine();

                ++_parsedLineNumber;
            }

            void SymbolError(ReadOnlySpan<char> lineContent, char c)
            {
                AppendError(lineContent, string.Format(Messages.Invalid_symbol_0_in_macro_name, c));
            }

            void AppendError(ReadOnlySpan<char> lineContent, string errorMessage)
            {
                sb.AppendLine(string.Format(Messages.Error_in_0_on_line_1_2, HttpUtility.HtmlEncode(lineContent.ToString()), LineHtml(lineNumber), errorMessage));
                hasErrors = true;
            }

            void AddMacro(ReadOnlySpan<char> lineContent, string name, Macro macro)
            {
                if (StringCalculator.IsFunction(name))
                {
                    AppendError(lineContent, string.Format(Messages.Macro_name_0_is_a_built_in_string_function, name));
                    return;
                }
                if (!Macros.TryAdd(name, macro))
                    AppendError(lineContent, string.Format(Messages.Duplicate_macro_name_0, name));
            }
            static string LineHtml(int line) => $"[<a href=\"#0\" data-text=\"{line}\">{line}</a>]";
        }

        public static Queue<string> GetFields(ReadOnlySpan<char> s, char delimiter)
        {
            var fields = new Queue<string>();
            var split = s.EnumerateSplits(delimiter);
            foreach (var item in split)
                fields.Enqueue(item.Trim().ToString());

            return fields;
        }

        private static string ApplyMacros(ReadOnlySpan<char> lineContent)
        {
            var index = lineContent.IndexOf("$");
            if (index < 0)
                return lineContent.ToString();

            index = lineContent.IndexOf("#{");
            var stringBuilder = new StringBuilder(200);
            var macroArguments = new List<string>();
            var keywordArguments = new Dictionary<string, string>();
            var bracketCount = 0;
            var emptyMacro = new Macro(null, null, null);
            var macro = emptyMacro;
            bool insideArgList = false;
            bool seenKeyword = false;
            Queue<string> fields = null;
            if (index >= 0)
            {
                var s = lineContent[(index + 2)..];
                lineContent = lineContent[..index];
                var n = s.IndexOf('}');
                if (n < 0)
                    n = s.Length;
                fields = GetFields(s[..n], ';');
            }
            var textSpan = new TextSpan(lineContent);
            for (int i = 0, len = lineContent.Length; i < len; ++i)
            {
                var c = lineContent[i];
                if (insideArgList)
                {
                    if (c == '(')
                    {
                        if (bracketCount == 0)
                            textSpan.Reset(i + 1);
                        ++bracketCount;
                    }
                    else if (c == ')')
                        --bracketCount;

                    if (c == ';' && bracketCount == 1 || c == ')' && bracketCount == 0)
                    {
                        var rawArg = textSpan.Cut();
                        if (TryParseKeywordArgSpan(rawArg.Trim(), out var pName, out var pValue))
                        {
                            seenKeyword = true;
                            var paramKey = pName.ToString();
                            if (keywordArguments.ContainsKey(paramKey))
                                throw Exceptions.DuplicateArgument(paramKey);
                            keywordArguments[paramKey] = ApplyMacros(pValue);
                        }
                        else
                        {
                            if (seenKeyword) throw Exceptions.InvalidNumberOfArguments();
                            var applied = ApplyMacros(rawArg);
                            macroArguments.Add(string.IsNullOrWhiteSpace(applied) ? string.Empty : applied);
                        }
                        textSpan.Reset(i + 1);

                        if (c == ')')
                        {
                            insideArgList = false;
                            var resolved = macro.ResolveArguments(macroArguments, keywordArguments);
                            var s = ApplyMacros(macro.Run(resolved));
                            var sbLength = stringBuilder.Length;
                            SetLineInputFields(s, stringBuilder, fields, false);
                            if (stringBuilder.Length == sbLength)
                                stringBuilder.Append(s);
                            textSpan.Reset(i + 1);
                            macro = emptyMacro;
                            macroArguments.Clear();
                            keywordArguments.Clear();
                            seenKeyword = false;
                        }
                    }
                    else if (bracketCount > 1 || c != '(')
                        textSpan.Expand();
                }
                else if (c == '$' && !textSpan.IsEmpty)
                {
                    textSpan.Expand();
                    var macroName = textSpan.ToString();
                    int j, mlen = macroName.Length - 1;
                    for (j = 0; j < mlen; ++j)
                        if (Macros.TryGetValue(macroName[j..], out macro))
                            break;

                    if (macro.IsEmpty)
                    {
                        stringBuilder.Append(textSpan.Cut());
                        textSpan.Reset(i);
                        continue;
                    }
                    else if (j > 0)
                        stringBuilder.Append(macroName[..j]);

                    bracketCount = 0;
                    macroArguments.Clear();
                    keywordArguments.Clear();
                    seenKeyword = false;
                    insideArgList = macro.ParameterCount > 0;
                    textSpan.Reset(i);
                }
                else
                {
                    if (!macro.IsEmpty)
                    {
                        var resolved = macro.ResolveArguments(macroArguments, keywordArguments);
                        var s = ApplyMacros(macro.Run(resolved));
                        var sbLength = stringBuilder.Length;
                        SetLineInputFields(s, stringBuilder, fields, false);
                        if (stringBuilder.Length == sbLength)
                            stringBuilder.Append(s);

                        textSpan.Reset(i);
                        macro = emptyMacro;
                    }
                    if (Validator.IsMacroLetter(c, textSpan.Length))
                    {
                        if (textSpan.IsEmpty)
                            textSpan.Reset(i);

                        textSpan.Expand();
                    }
                    else
                    {
                        if (!textSpan.IsEmpty)
                        {
                            stringBuilder.Append(textSpan.Cut());
                            textSpan.Reset(i);
                        }
                        stringBuilder.Append(c);
                    }
                }
            }
            if (macro.IsEmpty)
            {
                if (!textSpan.IsEmpty)
                    stringBuilder.Append(textSpan.Cut());
            }
            else if (!insideArgList)
            {
                var resolved = macro.ResolveArguments(macroArguments, keywordArguments);
                var s = ApplyMacros(macro.Run(resolved));
                stringBuilder.Append(s);
            }
            return stringBuilder.ToString();
        }


        private static char EatSpace(ReadOnlySpan<char> s, ref int index)
        {
            var len = s.Length - 1;
            while (index < len)
            {
                ++index;
                if (s[index] != ' ')
                    return s[index];
            }
            return '\0';
        }

        private static bool TryParseKeywordArgSpan(
            ReadOnlySpan<char> arg,
            out ReadOnlySpan<char> paramName,
            out ReadOnlySpan<char> value)
        {
            int i = 0;
            while (i < arg.Length && Validator.IsMacroLetter(arg[i], i)) i++;
            if (i > 0 && i + 1 < arg.Length && arg[i] == '$' && arg[i + 1] == '=')
            {
                paramName = arg[..(i + 1)];
                value = arg[(i + 2)..];
                return true;
            }
            paramName = default;
            value = default;
            return false;
        }

        public static int CountInputFields(ReadOnlySpan<char> s) =>
            CountOrHasInputFields(s, false);

        public static bool HasInputFields(ReadOnlySpan<char> s) =>
            CountOrHasInputFields(s, true) > 0;

        private static int CountOrHasInputFields(ReadOnlySpan<char> s, bool hasAny)
        {
            if (s.IsEmpty)
                return 0;

            var count = 0;
            var commentEnumerator = s.EnumerateComments();
            foreach (var item in commentEnumerator)
            {
                if (!item.IsEmpty && item[0] != '"' && item[0] != '\'')
                {
                    foreach (var c in item)
                    {
                        if (c == '?')
                        {
                            if (hasAny)
                                return 1;

                            ++count;
                        }
                    }
                }
            }
            return count;
        }

        public static bool SetLineInputFields(string s, StringBuilder sb, Queue<string> fields, bool forceLine)
        {
            if (string.IsNullOrEmpty(s) || fields is null || fields.Count == 0)
                return false;

            var inputChar = '\0';
            var count = fields.Count;
            var commentEnumerator = s.AsSpan().EnumerateComments();
            foreach (var item in commentEnumerator)
            {
                if (!item.IsEmpty)
                {
                    if (item[0] == '"' || item[0] == '\'')
                        sb.Append(item);
                    else
                    {
                        var j0 = 0;
                        var len = item.Length;
                        for (int j = 0; j < len; ++j)
                        {
                            var c = item[j];
                            if (c == '?')
                                inputChar = c;
                            else if (c == '{' && inputChar == '?')
                            {
                                inputChar = c;
                                sb.Append(item[j0..(j + 1)]);
                            }
                            else if (c == '}' && inputChar == '{')
                            {
                                inputChar = '\0';
                                if (!fields.TryDequeue(out string val))
                                    return false;

                                sb.Append(val);
                                j0 = j;
                            }
                            else if (inputChar == '{')
                                continue;
                            else if (c != ' ' && AddField(item[j0..j]))
                                j0 = j;
                        }
                        if (!AddField($"{item[j0..]} "))
                            sb.Append($"{item[j0..]}");
                    }
                }
            }
            if (forceLine && fields.Count != 0)
            {
                RemoveLineFields(sb);
                AddLineFields(sb, fields);
            }
            return fields.Count < count;

            bool AddField(ReadOnlySpan<char> s)
            {
                if (inputChar != '?') return false;
                inputChar = '\0';
                if (!fields.TryDequeue(out var val))
                    return false;

                sb.Append($"{s}{{{val}}}");
                return true;
            }
        }

        private static void RemoveLineFields(StringBuilder sb)
        {
            var len = sb.Length;
            var i = len;
            while (--i > 0)
                if (sb[i] == '{')
                    break;

            if (i > 1 && sb[i - 1] == '#')
            {
                while (--i > 0)
                    if (sb[i] != ' ')
                        break;
            }
            else
                i = -1;

            if (i > -1)
            {
                len -= i - 1;
                if (len > 0)
                    sb.Remove(i - 1, len);
            }
        }

        private static void AddLineFields(StringBuilder sb, Queue<string> fields)
        {
            sb.Append(HasUnclosedComment(sb) ? "' #{" : " #{");

            while (fields.TryDequeue(out string val))
            {
                sb.Append(val);
                sb.Append(';');
            }
            sb[^1] = '}';
        }

        private static bool HasUnclosedComment(StringBuilder sb)
        {
            var commentChar = '\0';
            var commentCount = 0;
            for (int i = 0, len = sb.Length; i < len; ++i)
            {
                var c = sb[i];
                if (commentChar == '\0')
                {
                    if (c == '\'' || c == '"')
                    {
                        commentChar = c;
                        ++commentCount;
                    }
                }
                else if (c == commentChar)
                    ++commentCount;
            }
            return commentCount % 2 == 1;
        }

        public int GetUnwarpedLineNumber(int sourceLineNumber)
        {
            if (sourceLineNumber < 1 || sourceLineNumber >= _lineNumbers.Count)
                return sourceLineNumber;

            return _lineNumbers[sourceLineNumber];
        }
    }
}
