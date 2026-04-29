using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private readonly Dictionary<string, string> _stringVariables = new(StringComparer.OrdinalIgnoreCase);
        private bool _stringVariablesDirty;
        private readonly Dictionary<string, string[,]> _tableVariables = new(StringComparer.OrdinalIgnoreCase);
        private bool _tableVariablesDirty;

        // --- String variable support ---

        private bool TryParseStringReassignment(ReadOnlySpan<char> line)
        {
            if (_stringVariables.Count == 0)
                return false;

            var s = line.Trim();

            // Check if line starts with a known string variable name followed by optional whitespace and '='
            foreach (var kvp in _stringVariables)
            {
                var varName = kvp.Key;
                if (!s.StartsWith(varName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var afterVar = s[varName.Length..].TrimStart();
                if (afterVar.Length == 0 || afterVar[0] != '=')
                    continue;

                var rhs = afterVar[1..].Trim();
                if (!IsStringExpression(rhs))
                    continue;

                var value = EvaluateStringExpression(rhs);
                _stringVariables[varName] = value;
                _stringVariablesDirty = true;
                return true;
            }
            return false;
        }

        private static bool IsStringExpression(ReadOnlySpan<char> s)
        {
            // String literal: 'value'
            if (s.Length >= 2 && s[0] == '\'')
                return true;

            // String function call: funcName$(...)
            for (int i = 0; i < s.Length - 1; i++)
            {
                if (s[i] == '$' && i + 1 < s.Length && s[i + 1] == '(')
                {
                    var nameStart = i;
                    while (nameStart > 0 && (char.IsLetterOrDigit(s[nameStart - 1]) || s[nameStart - 1] == '_'))
                        nameStart--;
                    var funcName = s[nameStart..(i + 1)].ToString();
                    if (StringCalculator.IsFunction(funcName))
                        return true;
                }
            }

            // String variable reference: varName$
            if (s.Length >= 2 && s[^1] == '$' && !s.Contains('('))
                return true;

            // Concatenation with '+': check if any part is a string expression
            if (s.Contains('+'))
            {
                var parts = SplitStringConcatenation(s.ToString());
                if (parts != null)
                    return true;
            }

            return false;
        }

        private bool IsStringVariableReference(string expression)
        {
            var s = expression.AsSpan().Trim();
            if (s.Length < 2 || s[^1] != '$')
                return false;

            // Check it's a valid variable name followed by $
            for (int i = 0; i < s.Length - 1; i++)
            {
                var c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }
            if (!char.IsLetter(s[0]) && s[0] != '_')
                return false;

            return _stringVariables.ContainsKey(s.ToString());
        }

        private string PreProcessExpression(string expression)
        {
            // First evaluate any string functions, then expand variables
            var result = EvaluateStringFunctionsInExpression(expression);
            // Resolve string comparisons (== / ≡ / != / ≠) before expanding to math
            result = ResolveStringComparisons(result);
            result = ExpandStringVariables(result);
            ThrowIfUndefinedStringVariables(result);
            return result;
        }

        private string ResolveStringComparisons(string expression)
        {
            // Check for == / ≡ / != / ≠ operators with string operands
            foreach (var (op, isEqual) in new[] { ("≡", true), ("==", true), ("≠", false), ("!=", false) })
            {
                var opPos = expression.IndexOf(op, StringComparison.Ordinal);
                if (opPos < 0) continue;

                var lhs = expression[..opPos].Trim();
                var rhs = expression[(opPos + op.Length)..].Trim();

                // Only handle if at least one side is a string expression
                if (!IsStringExpression(lhs.AsSpan()) && !IsStringExpression(rhs.AsSpan()))
                    continue;

                var lhsVal = EvaluateStringExpression(lhs);
                var rhsVal = EvaluateStringExpression(rhs);
                var equal = string.Equals(lhsVal, rhsVal, StringComparison.Ordinal);
                return (isEqual ? equal : !equal) ? "1" : "0";
            }
            return expression;
        }

        private string ExpandStringVariables(string text)
        {
            foreach (var kvp in _stringVariables)
            {
                if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    text = text.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }

        private void ThrowIfUndefinedStringVariables(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '$')
                    continue;

                if (i == 0)
                    continue;

                if (!char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_')
                    continue;

                // $( is a function call — skip
                if (i + 1 < text.Length && text[i + 1] == '(')
                    continue;

                // Walk backwards to find the start of the identifier
                int nameStart = i - 1;
                while (nameStart > 0 && (char.IsLetterOrDigit(text[nameStart - 1]) || text[nameStart - 1] == '_'))
                    nameStart--;

                if (!char.IsLetter(text[nameStart]) && text[nameStart] != '_')
                    continue;

                var name = text[nameStart..(i + 1)];

                if (_stringVariables.ContainsKey(name))
                    continue;

                if (_tableVariables.ContainsKey(name))
                    continue;

                if (StringCalculator.IsFunction(name))
                    continue;

                throw Exceptions.UndefinedVariableOrUnits(name);
            }
        }

        private string EvaluateStringFunctionsInExpression(string expression)
        {
            // Evaluate string functions inside-out (innermost first)
            // Keep iterating until no more string functions are found
            var maxIterations = 50; // safety limit
            for (int iter = 0; iter < maxIterations; iter++)
            {
                var pos = FindInnermostStringFunction(expression, out var funcName, out var argsStart, out var argsEnd);
                if (pos < 0)
                    break;

                var argsText = expression[(argsStart + 1)..argsEnd];
                var args = ParseStringFunctionArgs(argsText);

                string result;

                // Table element access: t$(row; col)
                if (_tableVariables.TryGetValue(funcName, out var table))
                {
                    if (args.Length != 2)
                        throw new MathParserException($"Table access '{funcName}' requires 2 indices.");
                    var row = EvaluateNumericArg(args[0]);
                    var col = EvaluateNumericArg(args[1]);
                    if (row < 1 || row > table.GetLength(0) || col < 1 || col > table.GetLength(1))
                        throw new MathParserException($"Table index ({row}, {col}) out of range.");
                    result = table[row - 1, col - 1];
                }
                // join$(tableVar; rowDelim; colDelim) — intercept before StringCalculator
                else if (funcName.Equals("join$", StringComparison.OrdinalIgnoreCase))
                {
                    result = EvaluateJoin(args);
                }
                // rowToStringArray$(tableVar; row) — extract row as JSON string array
                else if (funcName.Equals("rowToStringArray$", StringComparison.OrdinalIgnoreCase))
                {
                    result = EvaluateRowToStringArray(args);
                }
                // colToStringArray$(tableVar; col) — extract column as JSON string array
                else if (funcName.Equals("colToStringArray$", StringComparison.OrdinalIgnoreCase))
                {
                    result = EvaluateColToStringArray(args);
                }
                // tableToStringArray$(tableVar) — convert entire table to nested JSON string array
                else if (funcName.Equals("tableToStringArray$", StringComparison.OrdinalIgnoreCase))
                {
                    result = EvaluateTableToStringArray(args);
                }
                // typeOf$(expr) — return the type of an expression as a string
                else if (funcName.Equals("typeOf$", StringComparison.OrdinalIgnoreCase))
                {
                    result = EvaluateTypeOf(args);
                }
                // val$(tableVar) — convert table to matrix literal
                else if (funcName.Equals("val$", StringComparison.OrdinalIgnoreCase) &&
                         args.Length == 1 && _tableVariables.ContainsKey(args[0].Trim()))
                {
                    result = EvaluateValForTable(args[0].Trim());
                }
                // val$(s$; 'true'/'false') — parse string with optional unit retention
                else if (funcName.Equals("val$", StringComparison.OrdinalIgnoreCase) && args.Length == 2)
                {
                    var includeUnits = ResolveStringFunctionArg(args[1])
                        .Equals("true", StringComparison.OrdinalIgnoreCase);
                    var firstArg = args[0].Trim();
                    if (_tableVariables.ContainsKey(firstArg))
                    {
                        result = EvaluateValForTable(firstArg, includeUnits);
                    }
                    else
                    {
                        var raw = ResolveStringFunctionArg(firstArg);
                        if (!includeUnits)
                            result = StringCalculator.Evaluate("val$", new[] { raw });
                        else
                            result = raw;
                    }
                }
                // string$(expr; 'true'/'false') — convert to string with/without units
                else if (funcName.Equals("string$", StringComparison.OrdinalIgnoreCase) && args.Length == 2)
                {
                    var includeUnits = ResolveStringFunctionArg(args[1])
                        .Equals("true", StringComparison.OrdinalIgnoreCase);
                    var exprArg = args[0].Trim();
                    if (_stringVariables.Count > 0)
                        exprArg = ExpandStringVariables(exprArg);
                    _parser.Parse(exprArg);
                    _parser.Calculate();
                    result = includeUnits ? _parser.ResultAsValWithUnits : _parser.ResultAsVal;
                }
                else
                {
                    // Resolve each argument: expand string variables, then evaluate numeric expressions via MathParser
                    for (int i = 0; i < args.Length; i++)
                        args[i] = ResolveStringFunctionArg(args[i]);
                    result = StringCalculator.Evaluate(funcName, args);
                }

                expression = string.Concat(expression.AsSpan(0, pos), result, expression.AsSpan(argsEnd + 1));
            }
            return expression;
        }

        private string ResolveStringFunctionArg(string arg)
        {
            arg = arg.Trim();

            // String literal: 'value'
            if (arg.Length >= 2 && arg[0] == '\'' && arg[^1] == '\'')
                return arg[1..^1].Replace("''", "'");

            // String variable reference: check BEFORE expanding
            if (arg.Length >= 2 && arg[^1] == '$' && _stringVariables.TryGetValue(arg, out var strVal))
                return strVal;

            // String concatenation with '+' operator
            var parts = SplitStringConcatenation(arg);
            if (parts != null)
            {
                var sb = new StringBuilder();
                foreach (var part in parts)
                    sb.Append(EvaluateStringExpression(part));
                return sb.ToString();
            }

            // Expand string variables in mixed expressions (e.g., math expressions containing string vars)
            if (_stringVariables.Count > 0)
                arg = ExpandStringVariables(arg);

            // If it contains a nested string function, it will be handled by the outer loop
            // For numeric expressions, evaluate via MathParser
            if (!IsStringExpression(arg.AsSpan()))
            {
                try
                {
                    _parser.Parse(arg);
                    _parser.Calculate();
                    return _parser.ResultAsVal;
                }
                catch
                {
                    // Fall back to returning the raw text
                }
            }
            return arg;
        }

        private int FindInnermostStringFunction(string expression, out string funcName, out int argsStart, out int argsEnd)
        {
            funcName = null;
            argsStart = -1;
            argsEnd = -1;

            // Find the rightmost $( pattern — that's the innermost function call
            var bestPos = -1;
            for (int i = expression.Length - 1; i >= 1; i--)
            {
                if (expression[i] == '(' && expression[i - 1] == '$')
                {
                    // Walk backwards to find function name start
                    var nameEnd = i; // points to '('
                    var nameStart = i - 1; // points to '$'
                    while (nameStart > 0 && (char.IsLetterOrDigit(expression[nameStart - 1]) || expression[nameStart - 1] == '_'))
                        nameStart--;

                    var candidate = expression[nameStart..nameEnd];
                    if (StringCalculator.IsFunction(candidate) || _tableVariables.ContainsKey(candidate))
                    {
                        // Find matching closing paren
                        var depth = 1;
                        var closePos = -1;
                        for (int j = i + 1; j < expression.Length; j++)
                        {
                            if (expression[j] == '(') depth++;
                            else if (expression[j] == ')')
                            {
                                depth--;
                                if (depth == 0) { closePos = j; break; }
                            }
                        }
                        if (closePos >= 0)
                        {
                            funcName = candidate;
                            argsStart = i;
                            argsEnd = closePos;
                            bestPos = nameStart;
                            break; // rightmost innermost found
                        }
                    }
                }
            }
            return bestPos;
        }

        private static string[] ParseStringFunctionArgs(string argsText)
        {
            // Split on ';' respecting nested parentheses, brackets, and string literals
            var args = new List<string>();
            var depth = 0;
            var inString = false;
            var start = 0;
            for (int i = 0; i < argsText.Length; i++)
            {
                var c = argsText[i];
                if (c == '\'' && !inString) inString = true;
                else if (c == '\'' && inString) inString = false;
                else if (!inString)
                {
                    if (c == '(' || c == '[') depth++;
                    else if (c == ')' || c == ']') depth--;
                    else if (c == ';' && depth == 0)
                    {
                        args.Add(argsText[start..i].Trim());
                        start = i + 1;
                    }
                }
            }
            args.Add(argsText[start..].Trim());
            return args.ToArray();
        }

        private static bool IsStringResult(string originalExpression, string result)
        {
            // Check if the original expression was entirely a string function call
            // by verifying the original was a single function call (no operators outside it)
            var s = originalExpression.AsSpan().Trim();
            var dollarParenPos = s.IndexOf("$(");
            if (dollarParenPos < 0)
                return false;

            // Find the function name start
            var nameStart = dollarParenPos;
            while (nameStart > 0 && (char.IsLetterOrDigit(s[nameStart - 1]) || s[nameStart - 1] == '_'))
                nameStart--;

            // Check if the function is at the start of the expression (nothing before it except whitespace)
            if (nameStart > 0 && !s[..nameStart].IsWhiteSpace())
                return false;

            // Check if the function name is a known string function
            var funcName = s[nameStart..(dollarParenPos + 1)].ToString();
            if (!StringCalculator.IsFunction(funcName))
                return false;

            // If the function returns numeric results, don't treat as string output
            if (StringCalculator.ReturnsNumeric(funcName))
                return false;

            return true;
        }

        private bool ExpressionReferencesStringVariable(string expression)
        {
            foreach (var kvp in _stringVariables)
            {
                if (expression.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal string EvaluateStringExpression(ReadOnlySpan<char> expression)
        {
            var expr = expression.ToString().Trim();

            // String literal: 'value'
            if (expr.Length >= 2 && expr[0] == '\'' && expr[^1] == '\'' && IsSimpleStringLiteral(expr))
                return expr[1..^1].Replace("''", "'");

            // String concatenation with '+' operator (must be checked before function calls
            // so that e.g. 't' + ucase$(t) + 't' is split before the $( check fires)
            var parts = SplitStringConcatenation(expr);
            if (parts != null)
            {
                var sb = new StringBuilder();
                foreach (var part in parts)
                    sb.Append(EvaluateStringExpression(part));
                return sb.ToString();
            }

            // String function call
            if (expr.Contains("$("))
            {
                var result = EvaluateStringFunctionsInExpression(expr);
                result = ExpandStringVariables(result);
                // If the result still looks like a string literal, unwrap it
                if (result.Length >= 2 && result[0] == '\'' && result[^1] == '\'')
                    return result[1..^1].Replace("''", "'");
                return result;
            }

            // String variable reference
            if (expr.Length >= 2 && expr[^1] == '$' && _stringVariables.TryGetValue(expr, out var value))
                return value;

            // Numeric expression — evaluate via MathParser and return result as string
            try
            {
                _parser.Parse(expr);
                _parser.Calculate();
                return _parser.ResultAsVal;
            }
            catch
            {
                // Fall back to returning the raw text
                return expr;
            }
        }

        private static bool IsSimpleStringLiteral(string expr)
        {
            // A simple string literal is 'xxx' where internal quotes are escaped as ''
            for (int i = 1; i < expr.Length - 1; i++)
            {
                if (expr[i] == '\'')
                {
                    // Must be an escaped '' pair
                    if (i + 1 < expr.Length - 1 && expr[i + 1] == '\'')
                        i++; // skip the pair
                    else
                        return false; // unmatched quote — not a simple literal
                }
            }
            return true;
        }

        /// <summary>
        /// Splits a string expression on '+' operators that are outside quotes and parentheses.
        /// Returns null if there is no '+' to split on, or if none of the parts are string expressions.
        /// </summary>
        private static string[] SplitStringConcatenation(string expr)
        {
            var parts = new List<string>();
            int depth = 0;
            bool inQuote = false;
            int start = 0;

            for (int i = 0; i < expr.Length; i++)
            {
                var c = expr[i];
                if (c == '\'')
                    inQuote = !inQuote;
                else if (!inQuote)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == '+' && depth == 0)
                    {
                        parts.Add(expr[start..i].Trim());
                        start = i + 1;
                    }
                }
            }

            if (parts.Count == 0)
                return null;

            parts.Add(expr[start..].Trim());

            // Only treat as string concatenation if at least one part is a string expression
            foreach (var part in parts)
            {
                if (IsStringExpression(part.AsSpan()))
                    return parts.ToArray();
            }
            return null;
        }

        // --- Table variable support ---

        private bool IsTableVariableReference(string expression)
        {
            var s = expression.AsSpan().Trim();
            if (s.Length < 2 || s[^1] != '$')
                return false;

            for (int i = 0; i < s.Length - 1; i++)
            {
                var c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }
            if (!char.IsLetter(s[0]) && s[0] != '_')
                return false;

            return _tableVariables.ContainsKey(s.ToString());
        }

        private string[,] EvaluateTableExpression(ReadOnlySpan<char> expression)
        {
            var expr = expression.ToString().Trim();

            // Table literal: ['a'; 'b' | 'c'; 'd']
            if (expr.Length >= 2 && expr[0] == '[' && expr[^1] == ']')
                return ParseTableLiteral(expr[1..^1]);

            // table$(rows; cols) — create empty table
            if (expr.StartsWith("table$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var argsText = expr[7..^1];
                var args = ParseStringFunctionArgs(argsText);
                if (args.Length == 2)
                {
                    var rows = EvaluateNumericArg(args[0]);
                    var cols = EvaluateNumericArg(args[1]);
                    if (rows < 1) rows = 1;
                    if (cols < 1) cols = 1;
                    var table = new string[rows, cols];
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            table[r, c] = string.Empty;
                    return table;
                }
            }

            // split$(string; rowDelim; colDelim)
            if (expr.StartsWith("split$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var argsText = expr[7..^1];
                var args = ParseStringFunctionArgs(argsText);
                if (args.Length >= 2)
                {
                    var input = ResolveStringFunctionArg(args[0]);
                    var rowDelim = args.Length >= 2 ? ResolveStringFunctionArg(args[1]) : string.Empty;
                    var colDelim = args.Length >= 3 ? ResolveStringFunctionArg(args[2]) : string.Empty;
                    return SplitStringToTable(input, rowDelim, colDelim);
                }
            }

            // augmentT$(t1$; t2$; ...) — concatenate tables horizontally
            if (expr.StartsWith("augmentT$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[10..^1]);
                return EvaluateAugmentT(args);
            }

            // stackT$(t1$; t2$; ...) — concatenate tables vertically
            if (expr.StartsWith("stackT$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[8..^1]);
                return EvaluateStackT(args);
            }

            // rowT$(t$; n) — extract single row
            if (expr.StartsWith("rowT$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[6..^1]);
                return EvaluateRowT(args);
            }

            // colT$(t$; n) — extract single column
            if (expr.StartsWith("colT$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[6..^1]);
                return EvaluateColT(args);
            }

            // extractRowsT$(t$; [indices]) — extract multiple rows
            if (expr.StartsWith("extractRowsT$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[14..^1]);
                return EvaluateExtractRowsT(args);
            }

            // extractColsT$(t$; [indices]) — extract multiple columns
            if (expr.StartsWith("extractColsT$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[14..^1]);
                return EvaluateExtractColsT(args);
            }

            // subTable$(t$; r1; c1; r2; c2) — extract rectangular region
            if (expr.StartsWith("subTable$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[10..^1]);
                return EvaluateSubTable(args);
            }

            // transposeT$(t$) — swap rows and columns
            if (expr.StartsWith("transposeT$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var args = ParseStringFunctionArgs(expr[12..^1]);
                return EvaluateTransposeT(args);
            }

            // Table variable copy
            if (expr.Length >= 2 && expr[^1] == '$' && _tableVariables.TryGetValue(expr, out var existing))
                return (string[,])existing.Clone();

            // string$(matrix) or string$(matrix; 'true') — convert matrix to table
            if (expr.StartsWith("string$(", StringComparison.OrdinalIgnoreCase) && expr[^1] == ')')
            {
                var argsText = expr[8..^1];
                var args = ParseStringFunctionArgs(argsText);
                if (args.Length == 2)
                {
                    var includeUnits = ResolveStringFunctionArg(args[1])
                        .Equals("true", StringComparison.OrdinalIgnoreCase);
                    return includeUnits ? MatrixToTableWithUnits(args[0].Trim()) : MatrixToTable(args[0].Trim());
                }
                return MatrixToTable(argsText.Trim());
            }

            throw new MathParserException("Invalid table expression.");
        }

        private string[,] ParseTableLiteral(string content)
        {
            // Split on '|' for rows, then ';' for columns (respecting quotes)
            var rowTexts = SplitRespecting(content, '|');
            var maxCols = 0;
            var rows = new List<string[]>();

            foreach (var rowText in rowTexts)
            {
                var cellTexts = SplitRespecting(rowText.Trim(), ';');
                var cells = new string[cellTexts.Count];
                for (int c = 0; c < cellTexts.Count; c++)
                    cells[c] = EvaluateStringExpression(cellTexts[c].Trim());
                rows.Add(cells);
                if (cells.Length > maxCols) maxCols = cells.Length;
            }

            var table = new string[rows.Count, maxCols];
            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < maxCols; c++)
                    table[r, c] = c < rows[r].Length ? rows[r][c] : string.Empty;
            }
            return table;
        }

        private static List<string> SplitRespecting(string text, char separator)
        {
            var parts = new List<string>();
            int depth = 0;
            bool inQuote = false;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\'') inQuote = !inQuote;
                else if (!inQuote)
                {
                    if (c == '(' || c == '[') depth++;
                    else if (c == ')' || c == ']') depth--;
                    else if (c == separator && depth == 0)
                    {
                        parts.Add(text[start..i]);
                        start = i + 1;
                    }
                }
            }
            parts.Add(text[start..]);
            return parts;
        }

        private static string[,] SplitStringToTable(string input, string rowDelim, string colDelim)
        {
            string[] rows;
            if (string.IsNullOrEmpty(rowDelim))
                rows = [input];
            else
                rows = input.Split(rowDelim);

            var maxCols = 0;
            var allCells = new List<string[]>();
            foreach (var row in rows)
            {
                string[] cells;
                if (string.IsNullOrEmpty(colDelim))
                    cells = [row];
                else
                    cells = row.Split(colDelim);
                allCells.Add(cells);
                if (cells.Length > maxCols) maxCols = cells.Length;
            }

            var table = new string[allCells.Count, maxCols];
            for (int r = 0; r < allCells.Count; r++)
                for (int c = 0; c < maxCols; c++)
                    table[r, c] = c < allCells[r].Length ? allCells[r][c] : string.Empty;
            return table;
        }

        private string[,] MatrixToTable(string matrixExpr)
        {
            _parser.Parse(matrixExpr);
            _parser.Calculate();
            return _parser.ResultAsStringTable();
        }

        private string[,] MatrixToTableWithUnits(string matrixExpr)
        {
            _parser.Parse(matrixExpr);
            _parser.Calculate();
            return _parser.ResultAsStringTableWithUnits();
        }

        private int EvaluateNumericArg(string arg)
        {
            arg = arg.Trim();
            if (_stringVariables.Count > 0)
                arg = ExpandStringVariables(arg);
            try
            {
                _parser.Parse(arg);
                _parser.Calculate();
                return (int)_parser.Real;
            }
            catch
            {
                return 0;
            }
        }

        private bool TryParseTableElementAssignment(ReadOnlySpan<char> line)
        {
            if (_tableVariables.Count == 0)
                return false;

            var s = line.Trim();

            foreach (var kvp in _tableVariables)
            {
                var varName = kvp.Key;
                if (!s.StartsWith(varName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var afterVar = s[varName.Length..].TrimStart();
                if (afterVar.Length == 0 || afterVar[0] != '(')
                    continue;

                // Find matching closing paren
                var depth = 1;
                var closePos = -1;
                for (int i = 1; i < afterVar.Length; i++)
                {
                    if (afterVar[i] == '(') depth++;
                    else if (afterVar[i] == ')')
                    {
                        depth--;
                        if (depth == 0) { closePos = i; break; }
                    }
                }
                if (closePos < 0) continue;

                var afterParen = afterVar[(closePos + 1)..].TrimStart();
                if (afterParen.Length == 0 || afterParen[0] != '=')
                    continue;

                // Parse indices
                var indicesText = afterVar[1..closePos].ToString();
                var indices = ParseStringFunctionArgs(indicesText);
                if (indices.Length != 2) continue;

                var row = EvaluateNumericArg(indices[0]);
                var col = EvaluateNumericArg(indices[1]);
                var table = kvp.Value;
                if (row < 1 || row > table.GetLength(0) || col < 1 || col > table.GetLength(1))
                {
                    AppendError(s.ToString(), $"Table index ({row}, {col}) out of range.", _currentLine);
                    return true;
                }

                var rhs = afterParen[1..].Trim();
                var value = EvaluateStringExpression(rhs);
                table[row - 1, col - 1] = value;
                _tableVariablesDirty = true;
                return true;
            }
            return false;
        }

        private static string RenderTableAsHtml(string[,] table)
        {
            var sb = new StringBuilder();
            sb.Append("<table class=\"bordered\">");
            var rows = table.GetLength(0);
            var cols = table.GetLength(1);
            for (int r = 0; r < rows; r++)
            {
                sb.Append("<tr>");
                for (int c = 0; c < cols; c++)
                    sb.Append($"<td>{HttpUtility.HtmlEncode(table[r, c])}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</table>");
            return sb.ToString();
        }

        private string EvaluateJoin(string[] args)
        {
            if (args.Length < 1)
                throw new MathParserException("join$ requires at least 1 argument.");

            var tableVarName = args[0].Trim();
            if (!_tableVariables.TryGetValue(tableVarName, out var table))
            {
                // Try resolving as a string expression that names a table var
                if (tableVarName.Length >= 2 && tableVarName[^1] == '$')
                    throw new MathParserException($"Table variable '{tableVarName}' not found.");
                throw new MathParserException("join$ first argument must be a table variable.");
            }

            var rowDelim = args.Length >= 2 ? ResolveStringFunctionArg(args[1]) : string.Empty;
            var colDelim = args.Length >= 3 ? ResolveStringFunctionArg(args[2]) : string.Empty;

            var rows = table.GetLength(0);
            var cols = table.GetLength(1);
            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(colDelim))
            {
                // Flatten: all cells joined by rowDelim
                var first = true;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (!first) sb.Append(rowDelim);
                        sb.Append(table[r, c]);
                        first = false;
                    }
            }
            else
            {
                for (int r = 0; r < rows; r++)
                {
                    if (r > 0) sb.Append(rowDelim);
                    for (int c = 0; c < cols; c++)
                    {
                        if (c > 0) sb.Append(colDelim);
                        sb.Append(table[r, c]);
                    }
                }
            }
            return sb.ToString();
        }

        private string EvaluateRowToStringArray(string[] args)
        {
            if (args.Length != 2)
                throw new MathParserException("rowToStringArray$ requires 2 arguments: table variable and row index.");

            var tableVarName = args[0].Trim();
            if (!_tableVariables.TryGetValue(tableVarName, out var table))
                throw new MathParserException($"Table variable '{tableVarName}' not found.");

            var row = EvaluateNumericArg(args[1]);
            if (row < 1 || row > table.GetLength(0))
                throw new MathParserException($"rowToStringArray$: Row index {row} out of range (1..{table.GetLength(0)}).");

            var cols = table.GetLength(1);
            var sb = new StringBuilder("[");
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) sb.Append(", ");
                sb.Append('"');
                sb.Append(table[row - 1, c].Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private string EvaluateColToStringArray(string[] args)
        {
            if (args.Length != 2)
                throw new MathParserException("colToStringArray$ requires 2 arguments: table variable and column index.");

            var tableVarName = args[0].Trim();
            if (!_tableVariables.TryGetValue(tableVarName, out var table))
                throw new MathParserException($"Table variable '{tableVarName}' not found.");

            var col = EvaluateNumericArg(args[1]);
            if (col < 1 || col > table.GetLength(1))
                throw new MathParserException($"colToStringArray$: Column index {col} out of range (1..{table.GetLength(1)}).");

            var rows = table.GetLength(0);
            var sb = new StringBuilder("[");
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) sb.Append(", ");
                sb.Append('"');
                sb.Append(table[r, col - 1].Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private string EvaluateTableToStringArray(string[] args)
        {
            if (args.Length != 1)
                throw new MathParserException("tableToStringArray$ requires 1 argument: table variable.");

            var tableVarName = args[0].Trim();
            if (!_tableVariables.TryGetValue(tableVarName, out var table))
                throw new MathParserException($"Table variable '{tableVarName}' not found.");

            var rows = table.GetLength(0);
            var cols = table.GetLength(1);
            var sb = new StringBuilder("[");
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) sb.Append(", ");
                sb.Append('[');
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append('"');
                    sb.Append(table[r, c].Replace("\\", "\\\\").Replace("\"", "\\\""));
                    sb.Append('"');
                }
                sb.Append(']');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private string EvaluateTypeOf(string[] args)
        {
            if (args.Length != 1)
                throw new MathParserException("typeOf$ requires 1 argument.");

            var arg = args[0].Trim();

            // Check string table variables first
            if (arg.Length >= 2 && arg[^1] == '$' && _tableVariables.ContainsKey(arg))
                return "table";

            // Check string variables
            if (arg.Length >= 2 && arg[^1] == '$' && _stringVariables.ContainsKey(arg))
                return "string";

            // Check numeric variables via MathParser
            var variable = _parser.GetVariableRef(arg);
            if (variable != null && variable.IsInitialized)
            {
                return variable.Value switch
                {
                    Matrix => "matrix",
                    Vector => "vector",
                    ComplexValue c when c.IsComplex => "complex",
                    IScalarValue => "value",
                    _ => "undefined"
                };
            }

            // Try evaluating as an expression
            try
            {
                if (_stringVariables.Count > 0)
                    arg = ExpandStringVariables(arg);
                _parser.Parse(arg);
                _parser.Calculate();
                return _parser.ResultTypeName;
            }
            catch
            {
                return "undefined";
            }
        }

        private string[,] ResolveTableArg(string arg)
        {
            var name = arg.Trim();
            if (!_tableVariables.TryGetValue(name, out var table))
                throw new MathParserException($"Table variable '{name}' not found.");
            return table;
        }

        private string[,] EvaluateAugmentT(string[] args)
        {
            if (args.Length < 2)
                throw new MathParserException("augmentT$ requires at least 2 table arguments.");

            var tables = new string[args.Length][,];
            int maxRows = 0, totalCols = 0;
            for (int i = 0; i < args.Length; i++)
            {
                tables[i] = ResolveTableArg(args[i]);
                maxRows = Math.Max(maxRows, tables[i].GetLength(0));
                totalCols += tables[i].GetLength(1);
            }

            var result = new string[maxRows, totalCols];
            int colOffset = 0;
            for (int t = 0; t < tables.Length; t++)
            {
                var tbl = tables[t];
                var rows = tbl.GetLength(0);
                var cols = tbl.GetLength(1);
                for (int r = 0; r < maxRows; r++)
                    for (int c = 0; c < cols; c++)
                        result[r, colOffset + c] = r < rows ? tbl[r, c] : string.Empty;
                colOffset += cols;
            }
            return result;
        }

        private string[,] EvaluateStackT(string[] args)
        {
            if (args.Length < 2)
                throw new MathParserException("stackT$ requires at least 2 table arguments.");

            var tables = new string[args.Length][,];
            int totalRows = 0, maxCols = 0;
            for (int i = 0; i < args.Length; i++)
            {
                tables[i] = ResolveTableArg(args[i]);
                totalRows += tables[i].GetLength(0);
                maxCols = Math.Max(maxCols, tables[i].GetLength(1));
            }

            var result = new string[totalRows, maxCols];
            int rowOffset = 0;
            for (int t = 0; t < tables.Length; t++)
            {
                var tbl = tables[t];
                var rows = tbl.GetLength(0);
                var cols = tbl.GetLength(1);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < maxCols; c++)
                        result[rowOffset + r, c] = c < cols ? tbl[r, c] : string.Empty;
                rowOffset += rows;
            }
            return result;
        }

        private string[,] EvaluateRowT(string[] args)
        {
            if (args.Length != 2)
                throw new MathParserException("rowT$ requires 2 arguments: table variable and row index.");

            var table = ResolveTableArg(args[0]);
            var row = EvaluateNumericArg(args[1]);
            var rows = table.GetLength(0);
            var cols = table.GetLength(1);

            if (row < 1 || row > rows)
                throw new MathParserException($"rowT$: Row index {row} out of range (1..{rows}).");

            var result = new string[1, cols];
            for (int c = 0; c < cols; c++)
                result[0, c] = table[row - 1, c];
            return result;
        }

        private string[,] EvaluateColT(string[] args)
        {
            if (args.Length != 2)
                throw new MathParserException("colT$ requires 2 arguments: table variable and column index.");

            var table = ResolveTableArg(args[0]);
            var rows = table.GetLength(0);
            var col = EvaluateNumericArg(args[1]);
            var cols = table.GetLength(1);

            if (col < 1 || col > cols)
                throw new MathParserException($"colT$: Column index {col} out of range (1..{cols}).");

            var result = new string[rows, 1];
            for (int r = 0; r < rows; r++)
                result[r, 0] = table[r, col - 1];
            return result;
        }

        private string[,] EvaluateExtractRowsT(string[] args)
        {
            if (args.Length < 2)
                throw new MathParserException("extractRowsT$ requires a table variable and row indices.");

            var table = ResolveTableArg(args[0]);
            var rows = table.GetLength(0);
            var cols = table.GetLength(1);

            var indices = ParseIndexList(args, 1);
            var result = new string[indices.Length, cols];
            for (int i = 0; i < indices.Length; i++)
            {
                var idx = indices[i];
                if (idx < 1 || idx > rows)
                    throw new MathParserException($"extractRowsT$: Row index {idx} out of range (1..{rows}).");
                for (int c = 0; c < cols; c++)
                    result[i, c] = table[idx - 1, c];
            }
            return result;
        }

        private string[,] EvaluateExtractColsT(string[] args)
        {
            if (args.Length < 2)
                throw new MathParserException("extractColsT$ requires a table variable and column indices.");

            var table = ResolveTableArg(args[0]);
            var rows = table.GetLength(0);
            var cols = table.GetLength(1);

            var indices = ParseIndexList(args, 1);
            var result = new string[rows, indices.Length];
            for (int j = 0; j < indices.Length; j++)
            {
                var idx = indices[j];
                if (idx < 1 || idx > cols)
                    throw new MathParserException($"extractColsT$: Column index {idx} out of range (1..{cols}).");
                for (int r = 0; r < rows; r++)
                    result[r, j] = table[r, idx - 1];
            }
            return result;
        }

        private string[,] EvaluateSubTable(string[] args)
        {
            if (args.Length != 5)
                throw new MathParserException("subTable$ requires 5 arguments: table, r1, c1, r2, c2.");

            var table = ResolveTableArg(args[0]);
            var rows = table.GetLength(0);
            var cols = table.GetLength(1);

            var r1 = EvaluateNumericArg(args[1]);
            var c1 = EvaluateNumericArg(args[2]);
            var r2 = EvaluateNumericArg(args[3]);
            var c2 = EvaluateNumericArg(args[4]);

            // Swap if needed (mirror matrix submatrix behavior)
            if (r2 < r1) (r1, r2) = (r2, r1);
            if (c2 < c1) (c1, c2) = (c2, c1);

            if (r1 < 1 || r2 > rows)
                throw new MathParserException($"subTable$: Row indices ({r1}..{r2}) out of range (1..{rows}).");
            if (c1 < 1 || c2 > cols)
                throw new MathParserException($"subTable$: Column indices ({c1}..{c2}) out of range (1..{cols}).");

            var resultRows = r2 - r1 + 1;
            var resultCols = c2 - c1 + 1;
            var result = new string[resultRows, resultCols];
            for (int r = 0; r < resultRows; r++)
                for (int c = 0; c < resultCols; c++)
                    result[r, c] = table[r1 - 1 + r, c1 - 1 + c];
            return result;
        }

        private string[,] EvaluateTransposeT(string[] args)
        {
            if (args.Length != 1)
                throw new MathParserException("transposeT$ requires 1 argument: table variable.");

            var table = ResolveTableArg(args[0]);
            var rows = table.GetLength(0);
            var cols = table.GetLength(1);

            var result = new string[cols, rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    result[c, r] = table[r, c];
            return result;
        }

        /// <summary>
        /// Parses remaining args (after the table var) as a list of integer indices.
        /// Supports both individual numeric args and bracket-delimited vectors like [1; 2; 3].
        /// </summary>
        private int[] ParseIndexList(string[] args, int startIndex)
        {
            var indices = new List<int>();
            for (int i = startIndex; i < args.Length; i++)
            {
                var arg = args[i].Trim();
                // Strip brackets if present
                if (arg.StartsWith('[')) arg = arg[1..];
                if (arg.EndsWith(']')) arg = arg[..^1];

                // Could be a single number or semicolon-separated values within one arg
                var parts = arg.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                    indices.Add(EvaluateNumericArg(part));
            }
            if (indices.Count == 0)
                throw new MathParserException("Expected at least one index.");
            return indices.ToArray();
        }

        private bool ExpressionReferencesTableVariable(string expression)
        {
            foreach (var kvp in _tableVariables)
            {
                if (expression.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private string EvaluateValForTable(string tableVarName, bool includeUnits = false)
        {
            if (!_tableVariables.TryGetValue(tableVarName, out var table))
                return "0/0";

            var rows = table.GetLength(0);
            var cols = table.GetLength(1);
            if (rows == 1 && cols == 1)
                return FormatTableCell(table[0, 0], includeUnits);

            // Build matrix literal: [v1; v2 | v3; v4]
            var sb = new StringBuilder("[");
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) sb.Append(" | ");
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) sb.Append("; ");
                    sb.Append(FormatTableCell(table[r, c], includeUnits));
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string FormatTableCell(string cell, bool includeUnits)
        {
            if (cell is null)
                return "0/0";
            var trimmed = cell.Trim();
            if (!includeUnits)
            {
                return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? d.ToString(CultureInfo.InvariantCulture)
                    : "0/0";
            }

            // Find the longest leading prefix that parses as a number; the remainder (if any) is the unit token.
            int splitIndex = -1;
            for (int i = trimmed.Length; i > 0; i--)
            {
                if (double.TryParse(trimmed.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    splitIndex = i;
                    break;
                }
            }
            if (splitIndex < 0)
                return "0/0";
            return trimmed;
        }
    }
}
