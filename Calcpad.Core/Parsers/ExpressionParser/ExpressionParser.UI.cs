using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private sealed class UiPropertyMetadata
        {
            public string Type { get; set; }        // "entry", "datagrid", "dropdown", "radio", "checkbox"
            public string Style { get; set; }       // CSS class (nullable)
            public string Mode { get; set; }        // "string" | "number" (resolved)
            public string VariableName { get; set; } // Extracted from expression
            public int Rows { get; set; }           // For datagrid (0 = auto-detect)
            public int Columns { get; set; }        // For datagrid (0 = auto-detect)
            public string[] ColumnHeaders { get; set; } // Custom column headers (nullable)
            public string[] RowHeaders { get; set; }    // Custom row headers (nullable)
            public string[] Keys { get; set; }      // Display labels for dropdown/radio (nullable)
            public string[] Values { get; set; }    // Substitution values for dropdown/radio (nullable)
        }

        private UiPropertyMetadata _pendingUi;
        private int _uiSkipChars;

        /// <summary>
        /// Parses the #UI keyword arguments from the line.
        /// JSON block is optional — type/size is auto-detected from the expression if omitted.
        /// Always computes _uiSkipChars so the prefix is stripped.
        /// Only sets pending UI state when Settings.EnableUi is true.
        /// When the RHS is a string expression (or the LHS name ends with '$'),
        /// the keyword stores the value into _stringVariables / _tableVariables (like #string / #table)
        /// and returns KeywordResult.Continue so the math parser does not tokenize a string literal.
        /// </summary>
        private KeywordResult ParseKeywordUi(ReadOnlySpan<char> s)
        {
            // Expand string variables so e.g. #UI UIJSON$ becomes #UI {"type": "entry"}
            var expanded = s.ToString();
            if (_stringVariables.Count > 0)
                expanded = ExpandStringVariables(expanded);

            // Skip past "#ui" + whitespace to find what follows
            var cursor = 3; // "#ui"
            while (cursor < expanded.Length && expanded[cursor] == ' ')
                cursor++;

            string uiType = null;
            string uiStyle = null;
            string uiMode = null;
            string[] uiColumnHeaders = null;
            string[] uiRowHeaders = null;
            string[] uiKeys = null;
            string[] uiValues = null;
            int uiRows = 0;
            int uiColumns = 0;

            // Check if there's a JSON block
            if (cursor < expanded.Length && expanded[cursor] == '{')
            {
                var braceEnd = expanded.IndexOf('}', cursor);
                if (braceEnd < 0)
                {
                    AppendError(s.ToString(), "Improper format for #UI keyword. Missing closing brace '}'.", _currentLine);
                    _uiSkipChars = s.Length;
                    _pendingUi = null;
                    return KeywordResult.None;
                }

                var jsonString = expanded[cursor..(braceEnd + 1)];

                try
                {
                    using var doc = JsonDocument.Parse(jsonString);
                    var root = doc.RootElement;

                    uiType = root.TryGetProperty("type", out var tp) && tp.ValueKind == JsonValueKind.String
                        ? tp.GetString() : null;
                    uiStyle = root.TryGetProperty("style", out var sp) && sp.ValueKind == JsonValueKind.String
                        ? sp.GetString() : null;
                    uiMode = root.TryGetProperty("mode", out var mp) && mp.ValueKind == JsonValueKind.String
                        ? mp.GetString() : null;
                    uiRows = root.TryGetProperty("rows", out var rp) && rp.ValueKind == JsonValueKind.Number
                        ? rp.GetInt32() : 0;
                    uiColumns = root.TryGetProperty("columns", out var cp) && cp.ValueKind == JsonValueKind.Number
                        ? cp.GetInt32() : 0;
                    if (root.TryGetProperty("columnHeaders", out var chp) && chp.ValueKind == JsonValueKind.Array)
                        uiColumnHeaders = ParseJsonStringArray(chp);
                    if (root.TryGetProperty("rowHeaders", out var rhp) && rhp.ValueKind == JsonValueKind.Array)
                        uiRowHeaders = ParseJsonStringArray(rhp);
                    if (root.TryGetProperty("keys", out var kp) && kp.ValueKind == JsonValueKind.Array)
                        uiKeys = ParseJsonStringArray(kp);
                    if (root.TryGetProperty("values", out var vp) && vp.ValueKind == JsonValueKind.Array)
                        uiValues = ParseJsonStringArray(vp);
                }
                catch (JsonException)
                {
                    AppendError(s.ToString(), "Improper format for #UI keyword. Invalid JSON.", _currentLine);
                    // Still compute skip chars so the expression can be parsed
                    ComputeSkipCharsFromOriginal(s);
                    _pendingUi = null;
                    return KeywordResult.None;
                }

                // Compute _uiSkipChars from the ORIGINAL span
                var origBraceEnd = s.IndexOf('}');
                if (origBraceEnd >= 0)
                    _uiSkipChars = origBraceEnd + 1;
                else
                {
                    // String variable reference — skip past "#ui", whitespace, variable name
                    _uiSkipChars = 3;
                    while (_uiSkipChars < s.Length && s[_uiSkipChars] == ' ')
                        _uiSkipChars++;
                    while (_uiSkipChars < s.Length && s[_uiSkipChars] != ' ')
                        _uiSkipChars++;
                }
                while (_uiSkipChars < s.Length && s[_uiSkipChars] == ' ')
                    _uiSkipChars++;
            }
            else
            {
                // No JSON block — skip past "#ui" + whitespace only
                _uiSkipChars = 3;
                while (_uiSkipChars < s.Length && s[_uiSkipChars] == ' ')
                    _uiSkipChars++;
            }

            // Extract variable name and RHS from the expression after the UI block
            var expressionPart = s[_uiSkipChars..];
            var eqIndex = expressionPart.IndexOf('=');
            string varName = null;
            if (eqIndex > 0)
                varName = expressionPart[..eqIndex].Trim().ToString();

            // Resolve string vs number mode. Explicit "mode" wins; otherwise
            // autodetect from the variable suffix and RHS shape (single- or double-quoted
            // literal, string function call, concatenation, etc.).
            var rhsSpan = eqIndex > 0 ? expressionPart[(eqIndex + 1)..].Trim() : ReadOnlySpan<char>.Empty;
            bool isStringMode = uiMode switch
            {
                "string" => true,
                "number" => false,
                _ => (varName != null && varName.EndsWith('$'))
                    || IsStringExpression(rhsSpan)
                    || IsDoubleQuotedLiteral(rhsSpan)
            };

            if (isStringMode)
            {
                return ParseKeywordUiString(
                    s, varName, rhsSpan,
                    uiType, uiStyle, uiRows, uiColumns,
                    uiColumnHeaders, uiRowHeaders, uiKeys, uiValues);
            }

            if (!Settings.EnableUi)
            {
                _pendingUi = null;
                return KeywordResult.None;
            }

            // Auto-detect type and/or grid size from the RHS
            if (eqIndex > 0)
            {
                if (uiType == null)
                {
                    // No explicit type — infer from expression
                    uiType = IsDatagridRhs(rhsSpan) ? "datagrid" : "entry";
                }
                // Fill in missing rows/columns for datagrid from the expression
                if (uiType == "datagrid" && (uiRows == 0 || uiColumns == 0))
                {
                    if (rhsSpan.Length >= 2 && rhsSpan[0] == '[' && rhsSpan[^1] == ']')
                        AutoDetectGridSize(rhsSpan, ref uiRows, ref uiColumns);
                    else
                        AutoDetectGridSizeFromFunction(rhsSpan, ref uiRows, ref uiColumns);
                }
            }

            uiType ??= "entry";

            // Validate keys/values for dropdown and radio types
            if (uiType == "dropdown" || uiType == "radio")
            {
                if (uiKeys == null || uiValues == null)
                {
                    AppendError(s.ToString(), $"#UI {uiType}: both 'keys' and 'values' arrays are required.", _currentLine);
                    _pendingUi = null;
                    return KeywordResult.None;
                }
                if (uiKeys.Length != uiValues.Length)
                {
                    AppendError(s.ToString(), $"#UI {uiType}: 'keys' and 'values' arrays must have the same length.", _currentLine);
                    _pendingUi = null;
                    return KeywordResult.None;
                }
            }

            _pendingUi = new UiPropertyMetadata
            {
                Type = uiType,
                Style = uiStyle,
                Mode = "number",
                VariableName = varName,
                Rows = uiRows,
                Columns = uiColumns,
                ColumnHeaders = uiColumnHeaders,
                RowHeaders = uiRowHeaders,
                Keys = uiKeys,
                Values = uiValues
            };
            return KeywordResult.None;
        }

        /// <summary>
        /// Handles the string-mode branch of #UI. Stores the value into _stringVariables
        /// (or _tableVariables for datagrid) and emits HTML with UI control markup.
        /// Returns KeywordResult.Continue so the math parser does not try to tokenize
        /// a string literal.
        /// </summary>
        private KeywordResult ParseKeywordUiString(
            ReadOnlySpan<char> s,
            string varName,
            ReadOnlySpan<char> rhsSpan,
            string uiType,
            string uiStyle,
            int uiRows,
            int uiColumns,
            string[] uiColumnHeaders,
            string[] uiRowHeaders,
            string[] uiKeys,
            string[] uiValues)
        {
            if (string.IsNullOrEmpty(varName) || !varName.EndsWith('$'))
            {
                AppendError(s.ToString(), "#UI in string mode requires a variable name ending with '$'.", _currentLine);
                _pendingUi = null;
                _uiSkipChars = 0;
                return KeywordResult.Continue;
            }

            if (rhsSpan.IsEmpty)
            {
                AppendError(s.ToString(), "Expected '=' in #UI string variable declaration.", _currentLine);
                _pendingUi = null;
                _uiSkipChars = 0;
                return KeywordResult.Continue;
            }

            var rhsText = rhsSpan.ToString();
            // Accept double-quoted string literals on the RHS by normalizing to
            // Calcpad's native single-quoted form before evaluation.
            var normalizedRhsText = NormalizeDoubleQuotedLiteral(rhsText);
            var normalizedRhsSpan = normalizedRhsText.AsSpan();

            // Auto-detect datagrid from the RHS shape when no explicit type was given,
            // matching the routing #string uses.
            uiType ??= IsTableRhs(normalizedRhsSpan) ? "datagrid" : "entry";

            // Validate dropdown/radio options
            if (uiType == "dropdown" || uiType == "radio")
            {
                if (uiKeys == null || uiValues == null)
                {
                    AppendError(s.ToString(), $"#UI {uiType}: both 'keys' and 'values' arrays are required.", _currentLine);
                    _pendingUi = null;
                    _uiSkipChars = 0;
                    return KeywordResult.Continue;
                }
                if (uiKeys.Length != uiValues.Length)
                {
                    AppendError(s.ToString(), $"#UI {uiType}: 'keys' and 'values' arrays must have the same length.", _currentLine);
                    _pendingUi = null;
                    _uiSkipChars = 0;
                    return KeywordResult.Continue;
                }
            }

            // Preview mode: emit #UI-labeled preview HTML and do not store.
            if (!_calculate)
            {
                if (_isVisible)
                {
                    var attrs = Settings.EnableUi
                        ? BuildUiStringAttributes(uiType, uiStyle, varName, uiRows, uiColumns)
                        : string.Empty;
                    _sb.Append($"<p{HtmlId}{attrs}><span class=\"cond\">#UI</span> {HttpUtility.HtmlEncode(varName)} = {HttpUtility.HtmlEncode(rhsText)}</p>");
                }
                _pendingUi = null;
                _uiSkipChars = 0;
                return KeywordResult.Continue;
            }

            // Calculate mode: only act when the enclosing condition is satisfied.
            if (!_condition.IsSatisfied)
            {
                _pendingUi = null;
                _uiSkipChars = 0;
                return KeywordResult.Continue;
            }

            try
            {
                if (uiType == "datagrid")
                {
                    string[,] table;
                    if (Settings.UiOverrides != null && Settings.UiOverrides.TryGetValue(varName, out var overrideTable))
                        table = ParseStringDatagridOverride(overrideTable);
                    else
                        table = EvaluateTableExpression(normalizedRhsSpan);

                    _tableVariables[varName] = table;
                    _stringVariables.Remove(varName);
                    _tableVariablesDirty = true;

                    if (uiRows == 0) uiRows = table.GetLength(0);
                    if (uiColumns == 0) uiColumns = table.GetLength(1);

                    if (_isVisible && Settings.EnableUi)
                    {
                        BuildUiStringDatagrid(varName, table, uiStyle, uiRows, uiColumns, uiColumnHeaders, uiRowHeaders);
                    }
                    else if (_isVisible)
                    {
                        _sb.Append($"<p{HtmlId}>{HttpUtility.HtmlEncode(varName)} = {RenderTableAsHtml(table)}</p>");
                    }
                }
                else
                {
                    string value;
                    if (Settings.UiOverrides != null && Settings.UiOverrides.TryGetValue(varName, out var overrideValue))
                        value = overrideValue ?? string.Empty;
                    else
                        value = EvaluateStringExpression(normalizedRhsSpan);

                    if (uiType == "checkbox")
                        value = NormalizeBooleanString(value);

                    _stringVariables[varName] = value;
                    _tableVariables.Remove(varName);
                    _stringVariablesDirty = true;

                    if (_isVisible && Settings.EnableUi)
                    {
                        switch (uiType)
                        {
                            case "dropdown":
                                BuildUiStringDropdown(varName, value, uiStyle, uiKeys, uiValues);
                                break;
                            case "radio":
                                BuildUiStringRadio(varName, value, uiStyle, uiKeys, uiValues);
                                break;
                            case "checkbox":
                                BuildUiStringCheckbox(varName, value, uiStyle);
                                break;
                            default:
                                BuildUiStringEntry(varName, value, uiStyle);
                                break;
                        }
                    }
                    else if (_isVisible)
                    {
                        _sb.Append($"<p{HtmlId}>{HttpUtility.HtmlEncode(varName)} = {HttpUtility.HtmlEncode(value)}</p>");
                    }
                }
            }
            catch (MathParserException ex)
            {
                AppendError(s.ToString(), ex.Message, _currentLine);
            }

            _pendingUi = null;
            _uiSkipChars = 0;
            return KeywordResult.Continue;
        }

        private static string NormalizeBooleanString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "false";
            var v = value.Trim();
            if (v.Length == 0 || v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase))
                return "false";
            return "true";
        }

        /// <summary>
        /// True when rhs is a simple double-quoted string literal like "text".
        /// Used by the auto-detect path so #UI name = "text" resolves to string mode.
        /// </summary>
        private static bool IsDoubleQuotedLiteral(ReadOnlySpan<char> rhs)
        {
            return rhs.Length >= 2 && rhs[0] == '"' && rhs[^1] == '"';
        }

        /// <summary>
        /// Converts a double-quoted literal ("text") to Calcpad's native single-quoted form
        /// ('text'), escaping any embedded single quotes. Non-literal input is returned unchanged.
        /// </summary>
        private static string NormalizeDoubleQuotedLiteral(string rhs)
        {
            if (string.IsNullOrEmpty(rhs) || rhs.Length < 2)
                return rhs;
            if (rhs[0] != '"' || rhs[^1] != '"')
                return rhs;
            var inner = rhs[1..^1].Replace("'", "''");
            return $"'{inner}'";
        }

        private static string[,] ParseStringDatagridOverride(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
                return new string[0, 0];
            var rows = serialized.Split('|');
            var maxCols = 0;
            var cells = new string[rows.Length][];
            for (int r = 0; r < rows.Length; r++)
            {
                cells[r] = rows[r].Split(';');
                if (cells[r].Length > maxCols) maxCols = cells[r].Length;
            }
            var table = new string[rows.Length, maxCols];
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < maxCols; c++)
                    table[r, c] = c < cells[r].Length ? cells[r][c] : string.Empty;
            return table;
        }

        private string BuildUiStringAttributes(string uiType, string uiStyle, string varName, int rows, int columns)
        {
            var sb = new StringBuilder();
            sb.Append($" data-ui-type=\"{uiType}\"");
            sb.Append(" data-ui-mode=\"string\"");
            sb.Append($" data-ui-line=\"{_currentLine}\"");
            if (!string.IsNullOrEmpty(varName))
                sb.Append($" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(varName)}\"");
            if (!string.IsNullOrEmpty(uiStyle))
                sb.Append($" data-ui-style=\"{HttpUtility.HtmlAttributeEncode(uiStyle)}\"");
            if (uiType == "datagrid")
            {
                sb.Append($" data-ui-rows=\"{rows}\"");
                sb.Append($" data-ui-columns=\"{columns}\"");
            }
            return sb.ToString();
        }

        private void BuildUiStringEntry(string varName, string value, string uiStyle)
        {
            var cls = string.IsNullOrEmpty(uiStyle) ? "calcpad-ui-input" : $"calcpad-ui-input {uiStyle}";
            var attrs = BuildUiStringAttributes("entry", uiStyle, varName, 0, 0);
            _sb.Append($"<p{HtmlId}{attrs}>{HttpUtility.HtmlEncode(varName)} = ")
               .Append($"<input type=\"text\" class=\"{HttpUtility.HtmlAttributeEncode(cls)}\" value=\"{HttpUtility.HtmlAttributeEncode(value)}\"")
               .Append($" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(varName)}\" data-ui-line=\"{_currentLine}\" data-ui-mode=\"string\"></p>");
        }

        private void BuildUiStringDropdown(string varName, string value, string uiStyle, string[] keys, string[] values)
        {
            var cls = string.IsNullOrEmpty(uiStyle) ? "calcpad-ui-dropdown" : $"calcpad-ui-dropdown {uiStyle}";
            var attrs = BuildUiStringAttributes("dropdown", uiStyle, varName, 0, 0);
            _sb.Append($"<p{HtmlId}{attrs}>{HttpUtility.HtmlEncode(varName)} = ")
               .Append($"<select class=\"{HttpUtility.HtmlAttributeEncode(cls)}\"")
               .Append($" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(varName)}\" data-ui-line=\"{_currentLine}\" data-ui-mode=\"string\">");
            for (int i = 0; i < keys.Length; i++)
            {
                var selected = string.Equals(values[i], value, StringComparison.Ordinal) ? " selected" : string.Empty;
                _sb.Append($"<option value=\"{HttpUtility.HtmlAttributeEncode(values[i])}\"{selected}>{HttpUtility.HtmlEncode(keys[i])}</option>");
            }
            _sb.Append("</select></p>");
        }

        private void BuildUiStringRadio(string varName, string value, string uiStyle, string[] keys, string[] values)
        {
            var cls = string.IsNullOrEmpty(uiStyle) ? "calcpad-ui-radio" : $"calcpad-ui-radio {uiStyle}";
            var attrs = BuildUiStringAttributes("radio", uiStyle, varName, 0, 0);
            var groupName = $"ui-radio-{varName}-{_currentLine}";
            _sb.Append($"<p{HtmlId}{attrs}>{HttpUtility.HtmlEncode(varName)} = ")
               .Append($"<span class=\"{HttpUtility.HtmlAttributeEncode(cls)}\"")
               .Append($" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(varName)}\" data-ui-line=\"{_currentLine}\" data-ui-mode=\"string\">");
            for (int i = 0; i < keys.Length; i++)
            {
                var checkedAttr = string.Equals(values[i], value, StringComparison.Ordinal) ? " checked" : string.Empty;
                _sb.Append("<label class=\"calcpad-ui-radio-label\">")
                   .Append($"<input type=\"radio\" name=\"{HttpUtility.HtmlAttributeEncode(groupName)}\" value=\"{HttpUtility.HtmlAttributeEncode(values[i])}\"{checkedAttr}>")
                   .Append($" {HttpUtility.HtmlEncode(keys[i])}</label>");
            }
            _sb.Append("</span></p>");
        }

        private void BuildUiStringCheckbox(string varName, string value, string uiStyle)
        {
            var cls = string.IsNullOrEmpty(uiStyle) ? "calcpad-ui-checkbox" : $"calcpad-ui-checkbox {uiStyle}";
            var attrs = BuildUiStringAttributes("checkbox", uiStyle, varName, 0, 0);
            var isChecked = value == "true" ? " checked" : string.Empty;
            _sb.Append($"<p{HtmlId}{attrs}>{HttpUtility.HtmlEncode(varName)} = ")
               .Append($"<input type=\"checkbox\" class=\"{HttpUtility.HtmlAttributeEncode(cls)}\"")
               .Append($" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(varName)}\" data-ui-line=\"{_currentLine}\" data-ui-mode=\"string\"{isChecked}></p>");
        }

        private void BuildUiStringDatagrid(
            string varName, string[,] table, string uiStyle,
            int rows, int columns, string[] columnHeaders, string[] rowHeaders)
        {
            var cls = string.IsNullOrEmpty(uiStyle) ? "calcpad-ui-datagrid" : $"calcpad-ui-datagrid {uiStyle}";
            var attrs = BuildUiStringAttributes("datagrid", uiStyle, varName, rows, columns);
            _sb.Append($"<p{HtmlId}{attrs}>{HttpUtility.HtmlEncode(varName)} = </p>");

            var values = SerializeStringTable(table);
            var divAttrs = new StringBuilder();
            divAttrs.Append($"<div class=\"{HttpUtility.HtmlAttributeEncode(cls)}\"")
                    .Append($" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(varName)}\"")
                    .Append($" data-ui-line=\"{_currentLine}\"")
                    .Append(" data-ui-mode=\"string\"")
                    .Append($" data-ui-rows=\"{rows}\"")
                    .Append($" data-ui-columns=\"{columns}\"")
                    .Append($" data-ui-values=\"{HttpUtility.HtmlAttributeEncode(values)}\"");
            if (columnHeaders != null)
                divAttrs.Append($" data-ui-col-headers=\"{HttpUtility.HtmlAttributeEncode(string.Join(",", columnHeaders))}\"");
            if (rowHeaders != null)
                divAttrs.Append($" data-ui-row-headers=\"{HttpUtility.HtmlAttributeEncode(string.Join(",", rowHeaders))}\"");
            divAttrs.Append("></div>");
            _sb.AppendLine(divAttrs.ToString());
        }

        private static string SerializeStringTable(string[,] table)
        {
            var rows = table.GetLength(0);
            var cols = table.GetLength(1);
            var sb = new StringBuilder();
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) sb.Append('|');
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) sb.Append(';');
                    sb.Append(table[r, c] ?? string.Empty);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Determines if a RHS expression represents a vector/matrix value
        /// (bracket literal or vector/matrix function call).
        /// </summary>
        private static bool IsDatagridRhs(ReadOnlySpan<char> rhs)
        {
            // Case 1: Vector/matrix literal — trimmed RHS starts with '[' and ends with ']'
            // This excludes e.g. "5'[test]'" where '[' appears inside a unit label
            if (rhs.Length >= 2 && rhs[0] == '[' && rhs[^1] == ']')
                return true;

            // Case 2: vector(...) or matrix(...) function calls (with possible spaces before parenthesis)
            if (StartsWithFunction(rhs, "vector") || StartsWithFunction(rhs, "matrix"))
                return true;

            return false;
        }

        /// <summary>
        /// Auto-detects rows and columns from a vector(n) or matrix(m;n) function call.
        /// </summary>
        private static void AutoDetectGridSizeFromFunction(ReadOnlySpan<char> rhs, ref int rows, ref int columns)
        {
            var parenStart = rhs.IndexOf('(');
            var parenEnd = rhs.LastIndexOf(')');
            if (parenStart < 0 || parenEnd <= parenStart)
                return;

            var args = rhs[(parenStart + 1)..parenEnd].Trim();

            if (StartsWithFunction(rhs, "vector"))
            {
                // vector(n) → 1 row, n columns (displayed as a horizontal row)
                if (int.TryParse(args, out var n) && n > 0)
                {
                    if (rows == 0) rows = 1;
                    if (columns == 0) columns = n;
                }
            }
            else if (StartsWithFunction(rhs, "matrix"))
            {
                // matrix(m;n) → m rows, n columns
                var semicolonIdx = args.IndexOf(';');
                if (semicolonIdx > 0)
                {
                    var mSpan = args[..semicolonIdx].Trim();
                    var nSpan = args[(semicolonIdx + 1)..].Trim();
                    if (int.TryParse(mSpan, out var m) && m > 0 &&
                        int.TryParse(nSpan, out var n) && n > 0)
                    {
                        if (rows == 0) rows = m;
                        if (columns == 0) columns = n;
                    }
                }
            }
        }

        /// <summary>
        /// Auto-detects rows and columns from a vector/matrix RHS.
        /// In Calcpad syntax: | separates rows, ; separates elements within a row.
        /// Vector: [1; 2; 3] → 3 rows, 1 column
        /// Matrix: [1;2;3 | 4;5;6] → 2 rows, 3 columns
        /// </summary>
        private static void AutoDetectGridSize(ReadOnlySpan<char> rhs, ref int rows, ref int columns)
        {
            var bracketStart = rhs.IndexOf('[');
            var bracketEnd = rhs.LastIndexOf(']');
            if (bracketStart < 0 || bracketEnd <= bracketStart)
                return;

            var content = rhs[(bracketStart + 1)..bracketEnd];

            // Count rows: number of '|' + 1 (| separates rows in Calcpad)
            int pipes = 0;
            foreach (var c in content)
                if (c == '|') pipes++;

            if (pipes > 0)
            {
                // Matrix: rows = pipes + 1, columns = semicolons in first row + 1
                if (rows == 0)
                    rows = pipes + 1;

                int semicolons = 0;
                foreach (var c in content)
                {
                    if (c == '|') break;
                    if (c == ';') semicolons++;
                }
                if (columns == 0)
                    columns = semicolons + 1;
            }
            else
            {
                // Vector: single row, columns = semicolons + 1 (displayed horizontally)
                int semicolons = 0;
                foreach (var c in content)
                    if (c == ';') semicolons++;

                if (rows == 0)
                    rows = 1;
                if (columns == 0)
                    columns = semicolons + 1;
            }
        }

        private void ComputeSkipCharsFromOriginal(ReadOnlySpan<char> s)
        {
            _uiSkipChars = 3;
            while (_uiSkipChars < s.Length && s[_uiSkipChars] == ' ')
                _uiSkipChars++;
        }

        /// <summary>
        /// Returns data-ui-* attribute string to inject into the wrapping HTML element.
        /// </summary>
        private string GetUiAttributes(int sourceLine)
        {
            var attrs = $" data-ui-type=\"{_pendingUi.Type}\" data-ui-line=\"{sourceLine}\"";
            if (_pendingUi.VariableName != null)
                attrs += $" data-ui-var=\"{_pendingUi.VariableName}\"";
            if (_pendingUi.Style != null)
                attrs += $" data-ui-style=\"{_pendingUi.Style}\"";
            if (_pendingUi.Type == "datagrid")
            {
                attrs += $" data-ui-rows=\"{_pendingUi.Rows}\"";
                attrs += $" data-ui-columns=\"{_pendingUi.Columns}\"";
            }
            return attrs;
        }

        /// <summary>
        /// Injects a UI control into the equation HTML based on the pending UI type.
        /// </summary>
        private string InjectUiInput(string equationHtml, int sourceLine)
        {
            if (_pendingUi?.VariableName == null)
                return equationHtml;

            return _pendingUi.Type switch
            {
                "datagrid" => InjectUiDatagrid(equationHtml, sourceLine),
                "dropdown" => InjectUiDropdown(equationHtml, sourceLine),
                "radio" => InjectUiRadio(equationHtml, sourceLine),
                "checkbox" => InjectUiCheckbox(equationHtml, sourceLine),
                _ => InjectUiEntry(equationHtml, sourceLine)
            };
        }

        private string InjectUiEntry(string equationHtml, int sourceLine)
        {
            const string assignOp = " = ";
            var lastAssign = equationHtml.LastIndexOf(assignOp, StringComparison.Ordinal);
            if (lastAssign < 0)
                return equationHtml;

            var resultStart = lastAssign + assignOp.Length;
            var resultPart = equationHtml[resultStart..];

            SplitValueAndUnit(resultPart, out var numericValue, out var unitHtml);

            var styleClass = _pendingUi.Style != null
                ? $"calcpad-ui-input {_pendingUi.Style}"
                : "calcpad-ui-input";

            var input = $"<input type=\"text\" class=\"{styleClass}\" value=\"{numericValue}\"" +
                        $" data-ui-var=\"{_pendingUi.VariableName}\" data-ui-line=\"{sourceLine}\">";

            return equationHtml[..resultStart] + input + unitHtml;
        }

        private string InjectUiDropdown(string equationHtml, int sourceLine)
        {
            const string assignOp = " = ";
            var lastAssign = equationHtml.LastIndexOf(assignOp, StringComparison.Ordinal);
            if (lastAssign < 0)
                return equationHtml;

            var resultStart = lastAssign + assignOp.Length;
            var resultPart = equationHtml[resultStart..];

            SplitValueAndUnit(resultPart, out var numericValue, out var unitHtml);

            var styleClass = _pendingUi.Style != null
                ? $"calcpad-ui-dropdown {_pendingUi.Style}"
                : "calcpad-ui-dropdown";

            var sb = new System.Text.StringBuilder();
            sb.Append($"<select class=\"{styleClass}\"");
            sb.Append($" data-ui-var=\"{_pendingUi.VariableName}\" data-ui-line=\"{sourceLine}\">");

            for (int i = 0; i < _pendingUi.Keys.Length; i++)
            {
                var selected = _pendingUi.Values[i] == numericValue ? " selected" : "";
                sb.Append($"<option value=\"{WebUtility.HtmlEncode(_pendingUi.Values[i])}\"{selected}>{WebUtility.HtmlEncode(_pendingUi.Keys[i])}</option>");
            }
            sb.Append("</select>");

            return equationHtml[..resultStart] + sb.ToString() + unitHtml;
        }

        private string InjectUiRadio(string equationHtml, int sourceLine)
        {
            const string assignOp = " = ";
            var lastAssign = equationHtml.LastIndexOf(assignOp, StringComparison.Ordinal);
            if (lastAssign < 0)
                return equationHtml;

            var resultStart = lastAssign + assignOp.Length;
            var resultPart = equationHtml[resultStart..];

            SplitValueAndUnit(resultPart, out var numericValue, out var unitHtml);

            var styleClass = _pendingUi.Style != null
                ? $"calcpad-ui-radio {_pendingUi.Style}"
                : "calcpad-ui-radio";

            var groupName = $"ui-radio-{_pendingUi.VariableName}-{sourceLine}";
            var sb = new System.Text.StringBuilder();
            sb.Append($"<span class=\"{styleClass}\"");
            sb.Append($" data-ui-var=\"{_pendingUi.VariableName}\" data-ui-line=\"{sourceLine}\">");

            for (int i = 0; i < _pendingUi.Keys.Length; i++)
            {
                var checkedAttr = _pendingUi.Values[i] == numericValue ? " checked" : "";
                sb.Append($"<label class=\"calcpad-ui-radio-label\">");
                sb.Append($"<input type=\"radio\" name=\"{groupName}\" value=\"{WebUtility.HtmlEncode(_pendingUi.Values[i])}\"{checkedAttr}>");
                sb.Append($" {WebUtility.HtmlEncode(_pendingUi.Keys[i])}</label>");
            }
            sb.Append("</span>");

            return equationHtml[..resultStart] + sb.ToString() + unitHtml;
        }

        private string InjectUiCheckbox(string equationHtml, int sourceLine)
        {
            const string assignOp = " = ";
            var lastAssign = equationHtml.LastIndexOf(assignOp, StringComparison.Ordinal);
            if (lastAssign < 0)
                return equationHtml;

            var resultStart = lastAssign + assignOp.Length;
            var resultPart = equationHtml[resultStart..];

            SplitValueAndUnit(resultPart, out var numericValue, out var unitHtml);

            var styleClass = _pendingUi.Style != null
                ? $"calcpad-ui-checkbox {_pendingUi.Style}"
                : "calcpad-ui-checkbox";

            var isChecked = numericValue.Trim() == "1" ? " checked" : "";

            var input = $"<input type=\"checkbox\" class=\"{styleClass}\"" +
                        $" data-ui-var=\"{_pendingUi.VariableName}\" data-ui-line=\"{sourceLine}\"{isChecked}>";

            return equationHtml[..resultStart] + input + unitHtml;
        }

        /// <summary>
        /// Strips the matrix/vector rendering from equation HTML for datagrid lines,
        /// keeping only the LHS (e.g. "v =") so the datagrid table appears below.
        /// </summary>
        private static string StripDatagridRhs(string equationHtml)
        {
            // Find the " = " in the HTML (rendered as " = " with spaces)
            var eqIndex = equationHtml.IndexOf(" = ", StringComparison.Ordinal);
            if (eqIndex >= 0)
                return equationHtml[..(eqIndex + 3)];

            // Fallback: try just "="
            eqIndex = equationHtml.IndexOf('=');
            if (eqIndex >= 0)
                return equationHtml[..(eqIndex + 1)] + " ";

            return equationHtml;
        }

        /// <summary>
        /// Returns a datagrid div element. Called after the </p> line end
        /// so it's a block-level sibling, not nested inside inline elements.
        /// </summary>
        private string InjectUiDatagrid(string _, int sourceLine)
        {
            var values = _pendingUiDataValues ?? "";

            var styleClass = _pendingUi.Style != null
                ? $"calcpad-ui-datagrid {_pendingUi.Style}"
                : "calcpad-ui-datagrid";

            var attrs = $"<div class=\"{styleClass}\"" +
                       $" data-ui-var=\"{_pendingUi.VariableName}\" data-ui-line=\"{sourceLine}\"" +
                       $" data-ui-rows=\"{_pendingUi.Rows}\" data-ui-columns=\"{_pendingUi.Columns}\"" +
                       $" data-ui-values=\"{values}\"";

            if (_pendingUi.ColumnHeaders != null)
                attrs += $" data-ui-col-headers=\"{string.Join(",", _pendingUi.ColumnHeaders)}\"";
            if (_pendingUi.RowHeaders != null)
                attrs += $" data-ui-row-headers=\"{string.Join(",", _pendingUi.RowHeaders)}\"";

            return attrs + "></div>";
        }

        private string _pendingUiDataValues;

        /// <summary>
        /// Captures matrix/vector values from an expression.
        /// Supports bracket literals like [1;2;3] or [1;2|3;4], and
        /// function calls like vector(n) or matrix(m;n) (generates zeros).
        /// </summary>
        private void CaptureDatagridValues(string expressionText)
        {
            var eqIdx = expressionText.IndexOf('=');
            if (eqIdx < 0) return;

            var rhs = expressionText[(eqIdx + 1)..].Trim();

            // Case 1: bracket literal [...]
            var bracketStart = rhs.IndexOf('[');
            var bracketEnd = rhs.LastIndexOf(']');
            if (bracketStart >= 0 && bracketEnd > bracketStart)
            {
                var content = rhs[(bracketStart + 1)..bracketEnd];
                _pendingUiDataValues = content.Replace(" ", "");
                return;
            }

            // Case 2: vector(n) — generate n zeros as a single row
            if (StartsWithFunction(rhs.AsSpan(), "vector"))
            {
                var parenStart = rhs.IndexOf('(');
                var parenEnd = rhs.IndexOf(')');
                if (parenStart >= 0 && parenEnd > parenStart)
                {
                    var inner = rhs[(parenStart + 1)..parenEnd].Trim();
                    if (int.TryParse(inner, out var n) && n > 0)
                        _pendingUiDataValues = string.Join(";", new string[n].Select(_ => "0"));
                }
                return;
            }

            // Case 3: matrix(m;n) — generate m rows of n zeros
            if (StartsWithFunction(rhs.AsSpan(), "matrix"))
            {
                var parenStart = rhs.IndexOf('(');
                var parenEnd = rhs.IndexOf(')');
                if (parenStart >= 0 && parenEnd > parenStart)
                {
                    var inner = rhs[(parenStart + 1)..parenEnd].Trim();
                    var semi = inner.IndexOf(';');
                    if (semi > 0 &&
                        int.TryParse(inner[..semi].Trim(), out var m) && m > 0 &&
                        int.TryParse(inner[(semi + 1)..].Trim(), out var n) && n > 0)
                    {
                        var row = string.Join(";", new string[n].Select(_ => "0"));
                        _pendingUiDataValues = string.Join("|", Enumerable.Repeat(row, m));
                    }
                }
                return;
            }
        }

        /// <summary>
        /// Splits a result HTML fragment like "5 <i>ft</i>" into numeric value and unit HTML.
        /// </summary>
        private static void SplitValueAndUnit(string resultHtml, out string numericValue, out string unitHtml)
        {
            var unitStart = resultHtml.IndexOf("<i>", StringComparison.Ordinal);
            if (unitStart < 0)
                unitStart = resultHtml.IndexOf("<sup", StringComparison.Ordinal);

            if (unitStart > 0)
            {
                numericValue = resultHtml[..unitStart].TrimEnd('\u2009', ' ');
                unitHtml = "\u2009" + resultHtml[unitStart..];
            }
            else if (unitStart == 0)
            {
                numericValue = string.Empty;
                unitHtml = resultHtml;
            }
            else
            {
                numericValue = resultHtml.Trim();
                unitHtml = string.Empty;
            }
        }

        /// <summary>
        /// Applies a UI override to an expression text before MathParser processes it.
        /// </summary>
        private string ApplyUiOverride(ReadOnlySpan<char> expressionText)
        {
            if (_pendingUi?.VariableName == null ||
                Settings.UiOverrides == null ||
                !Settings.UiOverrides.TryGetValue(_pendingUi.VariableName, out var overrideValue))
                return null;

            var expr = expressionText.ToString();
            var eqIndex = expr.IndexOf('=');
            if (eqIndex < 0)
                return null;

            var lhs = expr[..(eqIndex + 1)];
            var rhs = expr[(eqIndex + 1)..].TrimStart();

            if (_pendingUi.Type == "datagrid")
            {
                // Replace the [...] block, preserving anything after ]
                var bracketStart = rhs.IndexOf('[');
                if (bracketStart < 0) return null;
                var bracketEnd = rhs.LastIndexOf(']');
                if (bracketEnd < 0) return null;
                var afterBracket = rhs[(bracketEnd + 1)..];
                return lhs + " " + overrideValue + afterBracket;
            }

            // Scalar entry: replace numeric value, preserve unit
            var i = 0;
            while (i < rhs.Length && IsNumericChar(rhs[i]))
                i++;

            if (i == 0)
                return null;

            var unit = rhs[i..];
            return lhs + " " + overrideValue + unit;
        }

        private static bool IsNumericChar(char c) =>
            c >= '0' && c <= '9' || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E';

        /// <summary>
        /// Checks if rhs starts with a function name followed by optional whitespace then '('.
        /// Handles cases like "vector ( 4 )" where spaces appear before the parenthesis.
        /// </summary>
        private static bool StartsWithFunction(ReadOnlySpan<char> rhs, ReadOnlySpan<char> funcName)
        {
            if (!rhs.StartsWith(funcName, StringComparison.OrdinalIgnoreCase))
                return false;
            var rest = rhs[funcName.Length..].TrimStart();
            return rest.Length > 0 && rest[0] == '(';
        }

        private void ResetUiState()
        {
            _pendingUi = null;
            _pendingUiDataValues = null;
            _uiSkipChars = 0;
        }

        private static string[] ParseJsonStringArray(JsonElement arrayElement)
        {
            var headers = new System.Collections.Generic.List<string>();
            foreach (var el in arrayElement.EnumerateArray())
                headers.Add(el.GetString() ?? "");
            return headers.ToArray();
        }
    }
}
