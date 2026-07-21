using System;
using System.Collections.Generic;
using System.Text;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Tokenizer
{
    /// <summary>
    /// Lint-mode definition extraction: captures full variable/function/unit definitions
    /// during tokenization, replacing the regex-based DefinitionCollection.
    /// </summary>
    public partial class CalcpadTokenizer
    {
        // Expression capture state
        private bool _lintCapturingExpression;
        private int _lintExpressionStartColumn;
        private string _lintDefLineText;
        private int _lintDefLine;

        // Pending definition info (set when = confirms a definition)
        private string _lintDefName;
        private int _lintDefNameLine;
        private bool _lintDefIsFunction;
        private bool _lintDefIsCustomUnit;
        private List<string> _lintDefFunctionParams;
        private bool _lintDefIsConst;

        // Function param collection (before = is seen)
        private List<string> _lintPendingFunctionParams = new();

        // Special keyword state
        private bool _lintPendingIsConst;
        private bool _lintExpectingReadVariable;
        private string _lintPendingReadVariableName;

        // Pending metadata from a '<!--{...}--> comment on the preceding line
        private DefinitionMetadata _lintPendingMetadata;

        // Include map for source tracking in Lint and Macro modes
        private Dictionary<int, SourceInfo> _includeMap;

        /// <summary>
        /// Sets the include map for source tracking in Lint mode.
        /// Call this before Tokenize() when you need definitions decorated with source info.
        /// </summary>
        public void SetIncludeMap(Dictionary<int, SourceInfo> includeMap)
        {
            _includeMap = includeMap;
        }

        private void InitLintState()
        {
            _lintCapturingExpression = false;
            _lintExpressionStartColumn = 0;
            _lintDefLineText = null;
            _lintDefLine = -1;
            _lintDefName = null;
            _lintDefNameLine = -1;
            _lintDefIsFunction = false;
            _lintDefIsCustomUnit = false;
            _lintDefFunctionParams = null;
            _lintDefIsConst = false;
            _lintPendingFunctionParams = new List<string>();
            _lintPendingIsConst = false;
            _lintExpectingReadVariable = false;
            _lintPendingReadVariableName = null;
            _lintPendingMetadata = null;
        }

        /// <summary>
        /// Called from TrackDefinitions for all tokens EXCEPT Operator "=".
        /// Handles keyword detection, function param collection, and #read variable tracking.
        /// </summary>
        private void TrackDefinitionsLint(TokenType type, string text)
        {
            // While capturing expression, no lint-specific processing needed
            // (the expression is extracted from line text at finalization)
            if (_lintCapturingExpression)
                return;

            switch (type)
            {
                case TokenType.Keyword:
                    var trimmed = text.TrimEnd();
                    if (trimmed.Equals("#const", StringComparison.OrdinalIgnoreCase))
                        _lintPendingIsConst = true;
                    else if (trimmed.Equals("#read", StringComparison.OrdinalIgnoreCase))
                        _lintExpectingReadVariable = true;
                    break;

                case TokenType.Variable:
                    // Capture #read variable name
                    if (_lintExpectingReadVariable && _lintPendingReadVariableName == null)
                        _lintPendingReadVariableName = text;
                    break;

                case TokenType.StringVariable:
                case TokenType.StringTable:
                    // String/table variables are tracked like regular variables for lint purposes
                    break;

                case TokenType.LocalVariable:
                    // Collect function parameters only when actively defining params (not in default values).
                    // After = inside parens, IsInFunctionParams is false, so default value tokens
                    // that happen to match a local variable name (e.g., 'm' as unit vs param) aren't collected.
                    if (_pendingFunctionName != null && _pendingFunctionParenDepth > 0 && _state.IsInFunctionParams)
                        _lintPendingFunctionParams.Add(text);
                    break;
            }
        }

        /// <summary>
        /// Called from TrackDefinitions when "=" confirms a variable or function definition.
        /// Starts expression capture for the definition.
        /// </summary>
        private void OnEqualsSeenLint(bool isFirstDef)
        {
            // If already capturing (e.g., inside a command block), skip
            if (_lintCapturingExpression)
                return;

            if (!isFirstDef)
            {
                _lintPendingIsConst = false;
                _lintPendingFunctionParams.Clear();
                return;
            }

            bool isCustomUnit = false;
            if (_pendingVariableName != null && _pendingFunctionName == null)
                isCustomUnit = IsCustomUnitLine(_state.Line);

            if (_pendingFunctionName != null && _pendingFunctionParenDepth == 0)
            {
                // Function definition: f(x;y) = expr
                _lintDefName = _pendingFunctionName;
                _lintDefNameLine = _pendingFunctionLine;
                _lintDefIsFunction = true;
                _lintDefIsCustomUnit = false;
                _lintDefFunctionParams = new List<string>(_lintPendingFunctionParams);
                _lintDefIsConst = _lintPendingIsConst;
            }
            else if (_pendingVariableName != null)
            {
                if (isCustomUnit)
                {
                    // Custom unit definition: .unitName = expr
                    _lintDefName = _pendingVariableName;
                    _lintDefNameLine = _pendingVariableLine;
                    _lintDefIsFunction = false;
                    _lintDefIsCustomUnit = true;
                    _lintDefIsConst = false;
                }
                else
                {
                    // Variable definition: var = expr
                    _lintDefName = _pendingVariableName;
                    _lintDefNameLine = _pendingVariableLine;
                    _lintDefIsFunction = false;
                    _lintDefIsCustomUnit = false;
                    _lintDefFunctionParams = null;
                    _lintDefIsConst = _lintPendingIsConst;
                }
            }
            else
            {
                // No pending definition to capture
                _lintPendingIsConst = false;
                _lintPendingFunctionParams.Clear();
                return;
            }

            _lintCapturingExpression = true;
            _lintExpressionStartColumn = _state.TokenStartColumn + 1; // Column after "="
            _lintDefLineText = _state.Text;
            _lintDefLine = _state.Line;
            _lintPendingIsConst = false;
            _lintPendingFunctionParams.Clear();
        }

        /// <summary>
        /// Called at end of each line to finalize any pending lint-mode definitions.
        /// </summary>
        private void FinalizeLineLint()
        {
            bool producedDefinition = false;

            // Finalize expression capture
            if (_lintCapturingExpression && _lintDefName != null)
            {
                var expression = ExtractExpressionFromLine(_lintDefLineText, _lintExpressionStartColumn);
                var sourceInfo = GetSourceInfo(_lintDefNameLine);

                if (_lintDefIsCustomUnit)
                {
                    _result.CustomUnitDefinitions.Add(new CustomUnitDefinition
                    {
                        Name = _lintDefName,
                        Definition = expression,
                        LineNumber = _lintDefNameLine,
                        Source = sourceInfo.Source,
                        SourceFile = sourceInfo.SourceFile
                    });
                    producedDefinition = true;
                }
                else if (_lintDefIsFunction)
                {
                    var funcParams = _lintDefFunctionParams ?? new List<string>();
                    var funcDef = new FunctionDefinition
                    {
                        Name = _lintDefName,
                        Params = funcParams,
                        Defaults = ExtractFunctionParamDefaults(_lintDefLineText, funcParams),
                        LineNumber = _lintDefNameLine,
                        Source = sourceInfo.Source,
                        SourceFile = sourceInfo.SourceFile,
                        Expression = expression,
                        IsConst = _lintDefIsConst,
                        Description = _lintPendingMetadata?.Description,
                        ParamTypes = _lintPendingMetadata?.ParamTypes,
                        ParamDescriptions = _lintPendingMetadata?.ParamDescriptions
                    };

                    // Detect command block pattern in expression
                    var commandBlock = DetectCommandBlock(expression, funcDef);
                    if (commandBlock != null)
                    {
                        funcDef.CommandBlock = commandBlock;
                        _result.CommandBlockFunctions[_lintDefName] = commandBlock;
                    }

                    _result.FunctionDefinitions.Add(funcDef);
                    producedDefinition = true;
                }
                else
                {
                    _result.VariableDefinitions.Add(new VariableDefinition
                    {
                        Name = _lintDefName,
                        Definition = expression,
                        LineNumber = _lintDefNameLine,
                        Source = sourceInfo.Source,
                        SourceFile = sourceInfo.SourceFile,
                        IsConst = _lintDefIsConst,
                        Description = _lintPendingMetadata?.Description
                    });
                    producedDefinition = true;
                }
            }

            // Finalize #read variable (no = on the line, so not captured by expression)
            if (_lintPendingReadVariableName != null)
            {
                // Check if TYPE=V (vector) or default (matrix)
                var lineText = _state.Text;
                var isVector = lineText.Contains("TYPE=V", StringComparison.OrdinalIgnoreCase);
                var definition = isVector ? "#read vector" : "#read matrix";
                var sourceInfo = GetSourceInfo(_state.Line);

                if (!_result.DefinedVariables.ContainsKey(_lintPendingReadVariableName))
                {
                    _result.VariableDefinitions.Add(new VariableDefinition
                    {
                        Name = _lintPendingReadVariableName,
                        Definition = definition,
                        LineNumber = _state.Line,
                        Source = sourceInfo.Source,
                        SourceFile = sourceInfo.SourceFile,
                        Description = _lintPendingMetadata?.Description
                    });
                    producedDefinition = true;
                }
            }

            // Clear metadata if it was consumed by a definition
            if (producedDefinition)
                _lintPendingMetadata = null;

            // Check if the current line is a metadata comment for the next definition
            if (!producedDefinition)
            {
                if (DefinitionMetadata.TryParse(_state.Text, out var metadata))
                    _lintPendingMetadata = metadata;
                else if (!string.IsNullOrWhiteSpace(_state.Text))
                    _lintPendingMetadata = null; // Non-blank, non-metadata line clears pending
            }

            // Reset all lint line state
            _lintCapturingExpression = false;
            _lintDefName = null;
            _lintDefNameLine = -1;
            _lintDefIsFunction = false;
            _lintDefIsCustomUnit = false;
            _lintDefFunctionParams = null;
            _lintDefIsConst = false;
            _lintPendingIsConst = false;
            _lintExpectingReadVariable = false;
            _lintPendingReadVariableName = null;
        }

        /// <summary>
        /// Parses the (params) section of a function definition line to extract default values.
        /// Returns a list parallel to paramNames: null = required, string = default value expression.
        /// Returns null if no parameters have defaults.
        /// </summary>
        private static List<string> ExtractFunctionParamDefaults(string line, List<string> paramNames)
        {
            if (paramNames == null || paramNames.Count == 0 || string.IsNullOrEmpty(line))
                return null;

            int openIdx = line.IndexOf('(');
            if (openIdx < 0) return null;

            // Find matching close paren
            int depth = 1, closeIdx = -1;
            for (int i = openIdx + 1; i < line.Length && depth > 0; i++)
            {
                if (line[i] == '(') depth++;
                else if (line[i] == ')') { depth--; if (depth == 0) { closeIdx = i; break; } }
            }
            if (closeIdx < 0) return null;

            var paramsSpan = line.AsSpan(openIdx + 1, closeIdx - openIdx - 1);
            var defaults = new List<string>(paramNames.Count);
            bool hasDefaults = false;

            // Split by ';' at depth 0, then check each segment for '=' at depth 0
            int segStart = 0, d = 0;
            void AddSeg(ReadOnlySpan<char> seg)
            {
                seg = seg.Trim();
                int eqIdx = -1, dd = 0;
                for (int j = 0; j < seg.Length; j++)
                {
                    if (seg[j] == '(') dd++;
                    else if (seg[j] == ')') dd--;
                    else if (seg[j] == '=' && dd == 0) { eqIdx = j; break; }
                }
                if (eqIdx < 0)
                    defaults.Add(null);
                else
                {
                    defaults.Add(seg[(eqIdx + 1)..].Trim().ToString());
                    hasDefaults = true;
                }
            }
            for (int i = 0; i < paramsSpan.Length; i++)
            {
                char c = paramsSpan[i];
                if (c == '(') d++;
                else if (c == ')') d--;
                else if (c == ';' && d == 0) { AddSeg(paramsSpan[segStart..i]); segStart = i + 1; }
            }
            AddSeg(paramsSpan[segStart..]);

            return hasDefaults ? defaults : null;
        }

        /// <summary>
        /// Extracts the expression text from a line, starting after the = sign.
        /// Stops at the first comment delimiter (' or ") or end of line.
        /// </summary>
        private static string ExtractExpressionFromLine(string lineText, int startColumn)
        {
            if (startColumn >= lineText.Length)
                return string.Empty;

            var afterEquals = lineText.AsSpan(startColumn);
            var result = new StringBuilder(afterEquals.Length);

            for (int i = 0; i < afterEquals.Length; i++)
            {
                var c = afterEquals[i];
                if (c == '\'' || c == '"')
                    break;
                result.Append(c);
            }

            return result.ToString().Trim();
        }

        /// <summary>
        /// Detects if an expression is a command block ($Inline{...}, $Block{...}, $While{...})
        /// and extracts its statements.
        /// </summary>
        private static CommandBlockInfo DetectCommandBlock(string expression, FunctionDefinition funcDef)
        {
            var exprSpan = expression.AsSpan();
            string blockType = null;
            if (exprSpan.StartsWith("$Inline", StringComparison.OrdinalIgnoreCase))
                blockType = "Inline";
            else if (exprSpan.StartsWith("$Block", StringComparison.OrdinalIgnoreCase))
                blockType = "Block";
            else if (exprSpan.StartsWith("$While", StringComparison.OrdinalIgnoreCase))
                blockType = "While";

            if (blockType == null)
                return null;

            var braceIndex = exprSpan.IndexOf('{');
            if (braceIndex < 0)
                return null;

            var blockContent = ExtractBlockContent(expression, braceIndex);
            var statements = SplitBlockStatements(blockContent);

            return new CommandBlockInfo
            {
                FunctionName = funcDef.Name,
                Parameters = funcDef.Params,
                BlockType = blockType,
                Statements = statements,
                FullLine = funcDef.Name + "(" + string.Join(";", funcDef.Params) + ") = " + expression,
                LineNumber = funcDef.LineNumber
            };
        }

        /// <summary>
        /// Extracts content inside a block (between { and matching }).
        /// </summary>
        private static string ExtractBlockContent(string text, int braceStart)
        {
            var textSpan = text.AsSpan();
            var depth = 0;
            var start = braceStart + 1;

            for (int i = braceStart; i < textSpan.Length; i++)
            {
                if (textSpan[i] == '{') depth++;
                else if (textSpan[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return textSpan.Slice(start, i - start).ToString();
                }
            }

            return textSpan[start..].ToString();
        }

        /// <summary>
        /// Splits command block content into statements by semicolon,
        /// ignoring semicolons inside parentheses, braces, or brackets.
        /// </summary>
        private static List<string> SplitBlockStatements(string content)
        {
            var statements = new List<string>();
            var currentStatement = new StringBuilder();
            var contentSpan = content.AsSpan();
            var parenDepth = 0;
            var braceDepth = 0;
            var bracketDepth = 0;

            for (int i = 0; i < contentSpan.Length; i++)
            {
                var c = contentSpan[i];

                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;

                if (c == ';' && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                {
                    var stmt = currentStatement.ToString().Trim();
                    if (!string.IsNullOrEmpty(stmt))
                        statements.Add(stmt);
                    currentStatement.Clear();
                }
                else
                {
                    currentStatement.Append(c);
                }
            }

            var lastStmt = currentStatement.ToString().Trim();
            if (!string.IsNullOrEmpty(lastStmt))
                statements.Add(lastStmt);

            return statements;
        }

        /// <summary>
        /// Gets source info (local vs include) for a line from the include map.
        /// </summary>
        private SourceInfo GetSourceInfo(int line)
        {
            if (_includeMap != null && _includeMap.TryGetValue(line, out var sourceInfo))
                return sourceInfo;
            return new SourceInfo { Source = "local" };
        }

        /// <summary>
        /// Checks if a line is a custom unit definition by looking at the first token.
        /// Custom units start with Operator "." as the first meaningful token.
        /// </summary>
        private bool IsCustomUnitLine(int line)
        {
            if (!_result.TokensByLine.TryGetValue(line, out var tokens))
                return false;

            foreach (var t in tokens)
            {
                // Skip whitespace-only None tokens (leading indentation)
                if (t.Type == TokenType.None && t.Text.TrimStart().Length == 0)
                    continue;
                // First meaningful token must be a dot (Operator or None-typed)
                return t.Text == ".";
            }
            return false;
        }
    }
}
