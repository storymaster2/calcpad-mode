using Markdig;
using Markdig.Renderers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private enum ParseMode { Cpd, Html, Markdown }

        private const int MaxHtmlLines = 200000;
        private int _errorCount;
        private int _isVal;
        private int _startLine;
        private int _currentLine;
        private int _htmlLines;
        private int _decimals;
        private bool _calculate;
        private bool _isVisible;
        private bool _isPausedByUser;
        private int _pauseCharCount;
        private bool _isMarkdownOn;
        private ParseMode _parseMode;
        private MathParser _parser;
        private readonly StringBuilder _sb = new(10000);
        private Queue<int> _errors;
        private LineInfo[] _lineCache;
        private static bool[] IsLineExtension = new bool[128];

        public Settings Settings { get; set; } = new();
        public string HtmlResult { get; private set; }
        public static bool IsUs
        {
            get => Unit.IsUs;
            set => Unit.IsUs = value;
        }
        public bool IsPaused => _startLine > 0;
        private bool IsNonCpdMode => _parseMode != ParseMode.Cpd;
        public bool Debug { get; set; }
        public bool ShowWarnings { get; set; } = true;
        public readonly List<string> OpenXmlExpressions = new(100);

        static ExpressionParser()
        {
            foreach (var c in ";|&@:({[") IsLineExtension[c] = true;
            InitKeyWordStrings();
        }

        public void Cancel() => _parser?.Cancel();
        public void Pause() => _isPausedByUser = true;

        private string HtmlId =>
            Debug && (_loops.Count == 0 || _loops.Peek().Iteration == 1) ?
            $" id=\"line-{_currentLine + 1}\" class=\"line\"" :
            string.Empty;

        public void Parse(string sourceCode, bool calculate = true, bool getXml = true) =>
            Parse(sourceCode.AsSpan(), calculate, getXml);

        private void Parse(ReadOnlySpan<char> code, bool calculate, bool getXml)
        {
            var lines = new List<int> { 0 };
            var len = code.Length;
            for (int i = 0; i < len; ++i)
                if (code[i] == '\n')
                    lines.Add(i + 1);

            if (lines[^1] < len)
                lines.Add(len);

            Initialize(calculate, lines.Count);
            var lineCount = lines.Count - 1;
            var s = string.Empty;
            var textSpan = s.AsSpan();
            try
            {
                while (++_currentLine < lineCount)
                {
                    ref var currentLineCache = ref _lineCache[_currentLine];
                    var keyword = currentLineCache.Keyword;
                    if (keyword == Keyword.SkipLine)
                        continue;
                    if (keyword == Keyword.Continue)
                    {
                        ParseKeywordContinue();
                        continue;
                    }
                    if (!IsNonCpdMode && currentLineCache.IsCached && keyword == Keyword.None)
                    {
                        if (IsEnabled())
                        {
                            _condition.SetCondition(-1);
                            _parser.IsCalculation = _isVal != -1;
                            ParseLine(currentLineCache.Tokens, Keyword.None);
                        }
                        continue;
                    }
                    var i1 = lines[_currentLine];
                    var i2 = lines[_currentLine + 1];
                    var lineSpan = code[i1..i2];
                    var eolIndex = lineSpan.IndexOf('\v');
                    if (eolIndex > -1)
                    {
                        _parser.Line = int.Parse(lineSpan[(eolIndex + 1)..]);
                        lineSpan = lineSpan[..eolIndex];
                    }
                    else
                        _parser.Line = _currentLine + 1;

                    lineSpan = lineSpan.Trim();
                    if (HasLineExtension(textSpan.TrimEnd()))
                    {
                        var c = textSpan[^1];
                        if (c == '_')
                            s = textSpan[0..^2].ToString() + lineSpan.ToString();
                        else
                            s = $"{textSpan} {lineSpan}";

                        textSpan = s.AsSpan();
                    }
                    else
                        textSpan = lineSpan;

                    if (HasLineExtension(textSpan.TrimEnd()))
                    {
                        _lineCache[_currentLine] = new(null, Keyword.SkipLine);
                        continue;
                    }

                    if (_parser.IsCanceled)
                        break;

                    if (textSpan.IsEmpty)
                    {
                        if (_isVisible && _isVal != 1 && _htmlLines < MaxHtmlLines && IsEnabled())
                            _sb.AppendLine($"<p{HtmlId}>&nbsp;</p>");

                        continue;
                    }
                    var lineCache = _currentLine;
                    _parser.IsConst = false;
                    var result = ParseKeyword(textSpan, ref keyword);
                    if (keyword != currentLineCache.Keyword)
                        _lineCache[lineCache] = new(currentLineCache.Tokens, keyword);

                    if (result == KeywordResult.Continue)
                        continue;
                    else if (result == KeywordResult.Break)
                        break;

                    // Handle table element assignment at the line level
                    if (_calculate && _condition.IsSatisfied && TryParseTableElementAssignment(textSpan))
                        continue;

                    // Handle string variable reassignment at the line level (like keywords)
                    if (_calculate && _condition.IsSatisfied && TryParseStringReassignment(textSpan))
                        continue;

                    if (IsNonCpdMode)
                    {
                        if (ParseCondition(textSpan, keyword))
                            ParseNonCpdModeLine(textSpan, keyword);
                    }
                    else
                    {
                        _parser.IsCalculation = _isVal != -1;
                        if ((textSpan[0] != '$' || !ParsePlot(textSpan)) &&
                            ParseCondition(textSpan, keyword))
                        {
                            List<Token> tokens;
                            if (_lineCache[_currentLine].IsCached)
                                tokens = _lineCache[_currentLine].Tokens;
                            else
                            {
                                var skipChars = keyword == Keyword.Ui ? _uiSkipChars :
                                    keyword == Keyword.Const ? 7 : _condition.KeywordLength;
                                tokens = GetTokens(textSpan[skipChars..]);
                                if (_isMarkdownOn)
                                    ParseMarkdown(tokens);

                                _lineCache[_currentLine] = new(tokens, keyword);
                            }
                            _parser.HasInputFields = false;
                            ParseLine(tokens, keyword);
                            // If the line has input fields, the line cach is cleared, to allow #input to work
                            if (_parser.HasInputFields)
                                _lineCache[_currentLine] = new(null, keyword);
                        }
                    }
                }
                ApplyUnits(_sb, _calculate);
                if (_currentLine == lineCount && (_calculate || !IsPaused))
                {
                    if (_condition.Id > 0 && !_condition.IsLoop)
                        _sb.Append(ErrHtml(Messages.if_block_not_closed_Missing_end_if, _currentLine));
                    if (_loops.Count != 0)
                        _sb.Append(ErrHtml(Messages.Iteration_block_not_closed_Missing_loop, _currentLine));
                    if (Debug && (_condition.Id > 0 || _loops.Count != 0))
                        _errors.Enqueue(_currentLine);
                }
            }
            catch (MathParserException ex)
            {
                AppendError(textSpan.ToString(), ex.Message, _currentLine);
            }
            catch (Exception ex)
            {
                _sb.Append(ErrHtml(string.Format(Messages.Unexpected_error_0_Please_check_the_expression_consistency, ex.Message), _currentLine));
                if (Debug)
                    _errors.Enqueue(_currentLine);
            }
            finally
            {
                Finalize(lineCount);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool IsEnabled() => _condition.IsSatisfied &&
                (_loops.Count == 0 || !_loops.Peek().IsBroken) ||
                !_calculate;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool HasLineExtension(ReadOnlySpan<char> s) => s.EndsWith(" _") || s.Length > 0 && CheckIsLineExtension(s[^1]) && !Validator.IsComment(s);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool CheckIsLineExtension(char c) => c < 128 && IsLineExtension[c];

            bool ParsePlot(ReadOnlySpan<char> s)
            {
                if (s.StartsWith("$plot", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("$map", StringComparison.OrdinalIgnoreCase))
                {
                    if (_isVisible && IsEnabled())
                    {
                        PlotParser plotParser;
                        if (s.StartsWith("$p", StringComparison.OrdinalIgnoreCase))
                            plotParser = new ChartParser(_parser, Settings.Plot);
                        else
                            plotParser = new MapParser(_parser, Settings.Plot);

                        try
                        {
                            _parser.IsPlotting = true;
                            var s1 = plotParser.Parse(s, _calculate);
                            _sb.Append(InsertAttribute(s1, HtmlId));
                            _parser.IsPlotting = false;
                        }
                        catch (MathParserException ex)
                        {
                            AppendError(s.ToString(), ex.Message, _currentLine);
                        }
                    }
                    return true;
                }
                return false;
            }

            void ParseMarkdown(List<Token> tokens)
            {
                if (tokens.Count == 0)
                    return;

                const char rs = '\u001E';
                StringBuilder sb = new();
                var startsWithExpression = tokens[0].Type == TokenTypes.Expression;
                if (startsWithExpression)
                    sb.Append(rs);

                var n = tokens.Count;
                for (int i = 0; i < n; ++i)
                {
                    var token = tokens[i];
                    if (token.Type != TokenTypes.Expression)
                    {
                        if (n == 1)
                            sb.Append(token.Value.TrimEnd());
                        else
                            sb.Append(token.Value).Append(rs);
                    }
                }
                var pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().UseListExtras().Build();
                var document = Markdown.Parse(sb.ToString(), pipeline);
                using StringWriter writer = new();
                HtmlRenderer renderer = new(writer)
                {
                    ImplicitParagraph = true
                };
                pipeline.Setup(renderer);
                renderer.Render(document); // using the renderer directly
                var result = writer.ToString();
                var sections = result.AsSpan().EnumerateSplits(rs);
                var cs = sections.Current;
                if (startsWithExpression)
                {
                    if (cs.IsEmpty)
                        sections.MoveNext();
                    else
                    {
                        tokens.Insert(0, new Token(cs.ToString(), TokenTypes.Html));
                        ++n;
                    }
                }
                for (int i = 0; i < n; ++i)
                {
                    var t = tokens[i].Type;
                    if (t != TokenTypes.Expression)
                    {
                        if (!sections.MoveNext())
                            break;

                        cs = sections.Current;
                        if (tokens[i].Value.StartsWith('#'))
                            t = TokenTypes.Html;

                        tokens[i] = new Token(cs.ToString(), t);
                    }
                }
                while (sections.MoveNext())
                {
                    cs = sections.Current;
                    if (!cs.IsEmpty)
                        tokens.Add(new Token(cs.ToString(), TokenTypes.Html));

                }
            }

            bool ParseCondition(ReadOnlySpan<char> s, Keyword keyword)
            {

                if (IsPaused && !_calculate)
                {
                    _condition.SetCondition(-1);
                    return keyword == Keyword.None;
                }
                _condition.SetCondition(keyword - Keyword.If);
                if (IsEnabled())
                {
                    if (_condition.KeywordLength == s.Length)
                    {
                        if (_condition.IsUnchecked)
                            throw Exceptions.ConditionEmpty();

                        if (_isVisible && !_calculate)
                        {
                            if (keyword == Keyword.Else)
                                _sb.Append($"</div><p{HtmlId}>{_condition.ToHtml()}</p><div class = \"indent\">");
                            else
                                _sb.Append($"</div><p{HtmlId}>{_condition.ToHtml()}</p>");
                        }
                    }
                    else if (_condition.KeywordLength > 0 &&
                             _condition.IsFound &&
                             _condition.IsUnchecked &&
                             _calculate)
                        _condition.Check(0.0);
                    else
                        return true;
                }
                return false;
            }

            void ParseLine(List<Token> tokens, Keyword keyword)
            {
                var kwdLength = _condition.KeywordLength;
                var isOutput = _isVisible &&
                    (!_calculate || kwdLength == 0) &&
                    _htmlLines < MaxHtmlLines;

                if (isOutput)
                {
                    ++_htmlLines;
                    if (_htmlLines == MaxHtmlLines)
                        AppendError(string.Concat(tokens), string.Format(Messages.The_output_is_longer_than_0_lines_The_rest_will_be_skipped, MaxHtmlLines), _currentLine);
                    else
                    {
                        bool isIndent = keyword == Keyword.Else_If || keyword == Keyword.End_If;
                        var lineType = tokens.Count != 0 ?
                            tokens[0].Type :
                            TokenTypes.Text;


                        string htmlId = null;
                        if (_isVal != 1)
                        {
                            htmlId = HtmlId;
                            if (_pendingUi != null && Settings.EnableUi)
                                htmlId += GetUiAttributes(_currentLine);
                            AppendHtmlLineStart(lineType, isIndent);
                        }
                        if (lineType == TokenTypes.Html && !string.IsNullOrEmpty(htmlId))
                            tokens[0] = new Token(InsertAttribute(tokens[0].Value, htmlId), TokenTypes.Html);

                        if (kwdLength > 0)
                            _sb.Append(_condition.ToHtml());

                        var sbLenBefore = _sb.Length;
                        ParseTokens(tokens, true, getXml);
                        if (_sb.Length == sbLenBefore && _isVal != 1)
                        {
                            // Nothing was output — roll back the opening tag
                            _sb.Length = htmlId is not null ? sbLenBefore - htmlId.Length - 3 : sbLenBefore;
                            --_htmlLines;
                        }
                        else if (_isVal != 1)
                            AppendHtmlLineEnd(lineType, keyword == Keyword.If);

                        // Append datagrid div AFTER the </p> so it's a sibling, not nested inside
                        if (_pendingUi != null && Settings.EnableUi && _pendingUi.Type == "datagrid")
                        {
                            _sb.AppendLine(InjectUiDatagrid(string.Empty, _currentLine));
                            ResetUiState();
                        }
                    }
                }
                else
                    ParseTokens(tokens, false, getXml);

                if (_condition.IsUnchecked)
                {
                    if (_calculate)
                        _condition.Check(_parser.Result);
                    else
                        _condition.Check();
                }
            }

            void AppendHtmlLineStart(TokenTypes lineType, bool isIndent)
            {
                if (isIndent)
                    _sb.Append("</div>");

                if (lineType == TokenTypes.Heading)
                    _sb.Append($"<h3{HtmlId}>");
                else if (lineType != TokenTypes.Html)
                    _sb.Append($"<p{HtmlId}>");
            }

            void AppendHtmlLineEnd(TokenTypes lineType, bool indent)
            {
                if (lineType == TokenTypes.Heading)
                    _sb.Append("</h3>");
                else if (lineType != TokenTypes.Html)
                    _sb.Append("</p>");

                if (indent)
                    _sb.Append("<div class = \"indent\">");

                _sb.AppendLine();
            }
        }

        private void Initialize(bool calculate, int lineCount)
        {
            _htmlLines = 0;
            _errorCount = 0;
            _calculate = calculate;
            _errors = new();
            if (!_calculate)
                _startLine = 0;

            if (_startLine == 0)
            {
                Settings.Math.FormatString = null;
                _parser = new MathParser(Settings.Math)
                {
                    ShowWarnings = ShowWarnings
                };
                _decimals = Settings.Math.Decimals;
                _lineCache = new LineInfo[lineCount];
                _sb.Clear();
                _condition = new();
                _loops.Clear();
                _stringVariables.Clear();
                _stringVariablesDirty = false;
                _tableVariables.Clear();
                _tableVariablesDirty = false;
                _isVal = 0;
                _parser.SetVariable("Units", new RealValue(UnitsFactor()));
                _previousKeyword = Keyword.None;
                _isMarkdownOn = false;
                _parseMode = ParseMode.Cpd;
                OpenXmlExpressions.Clear();
                ResetUiState();
            }
            else
            {
                if (_lineCache.Length < lineCount)
                    Array.Resize(ref _lineCache, lineCount);

                var n = _sb.Length - _pauseCharCount;
                if (n > 0)
                    _sb.Remove(_pauseCharCount, n);
            }
            _parser.IsEnabled = _calculate;
            _currentLine = _startLine - 1;
            _isVisible = true;
        }

        private void Finalize(int lineCount)
        {
            if (_currentLine == lineCount && _calculate)
                _startLine = 0;

            if (_startLine > 0)
                _sb.Append(Messages.Paused_Press_F5_to_continue);

            if (Debug && lineCount > 30 && _errors.Count != 0)
                AppendErrors();

            HtmlResult = _sb.ToString();

            if (_calculate && _startLine == 0)
            {
                _parser.ClearCache();
                _parser = null;
            }
        }

        private void ParseNonCpdModeLine(ReadOnlySpan<char> textSpan, Keyword keyword)
        {
            var kwdLength = _condition.KeywordLength;
            var content = kwdLength > 0 && kwdLength < textSpan.Length
                ? textSpan[kwdLength..].ToString()
                : kwdLength > 0 ? string.Empty : textSpan.ToString();

            // Expand string variables and functions
            if (_calculate)
            {
                if (_stringVariables.Count > 0)
                    content = ExpandStringVariables(content);
                if (content.Contains("$("))
                    content = EvaluateStringFunctionsInExpression(content);
            }

            // Handle condition checking (#if with string tests)
            if (_condition.IsUnchecked)
            {
                if (_calculate)
                {
                    var condExpr = content;
                    if (_stringVariables.Count > 0 || _tableVariables.Count > 0 || condExpr.Contains("$("))
                        condExpr = PreProcessExpression(condExpr);
                    _parser.Parse(condExpr);
                    _parser.Calculate();
                    _condition.Check(_parser.Result);
                }
                else
                    _condition.Check();
                return;
            }

            // Output
            var isOutput = _isVisible &&
                (!_calculate || kwdLength == 0) &&
                _htmlLines < MaxHtmlLines;

            if (!isOutput)
                return;

            ++_htmlLines;
            if (_htmlLines == MaxHtmlLines)
            {
                AppendError(content, string.Format(
                    Messages.The_output_is_longer_than_0_lines_The_rest_will_be_skipped, MaxHtmlLines), _currentLine);
                return;
            }

            // In Markdown mode, process content through Markdig
            if (_parseMode == ParseMode.Markdown)
                content = RenderMarkdown(content);

            var trimmed = content.TrimStart();
            var isHtmlContent = trimmed.Length > 0 && trimmed[0] == '<';
            bool isIndent = keyword == Keyword.Else_If || keyword == Keyword.End_If;

            if (isIndent)
                _sb.Append("</div>");

            string htmlId = HtmlId;
            if (_pendingUi != null && Settings.EnableUi)
                htmlId += GetUiAttributes(_currentLine);

            if (kwdLength > 0)
                _sb.Append(_condition.ToHtml());

            if (isHtmlContent || _parseMode == ParseMode.Html)
                _sb.Append(InsertAttribute(content, htmlId));
            else
                _sb.Append($"<p{htmlId}>{content}</p>");

            if (keyword == Keyword.If)
                _sb.Append("<div class=\"indent\">");

            _sb.AppendLine();

            // Handle UI datagrid if pending
            if (_pendingUi != null && Settings.EnableUi && _pendingUi.Type == "datagrid")
            {
                _sb.AppendLine(InjectUiDatagrid(string.Empty, _currentLine));
                ResetUiState();
            }
        }

        private static string RenderMarkdown(string content)
        {
            var pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().UseListExtras().Build();
            return Markdown.ToHtml(content, pipeline);
        }

        private void AppendErrors()
        {
            if (_errors.Count == 1)
                _sb.AppendLine(Messages.Error_found_on_line);
            else
                _sb.AppendLine(string.Format(Messages.Errors_found_on_lines, _errors.Count));
            var count = 0;
            var prevLine = 0;
            while (_errors.Count != 0 && count < 20)
            {
                var errLine = _errors.Dequeue() + 1;
                if (errLine != prevLine)
                {
                    ++count;
                    _sb.Append($" <span class=\"roundBox\" data-line=\"{errLine}\">{errLine}</span>");
                }
                prevLine = errLine;
            }
            if (_errors.Count > 0)
                _sb.Append(" ...");

            _sb.Append("</div>");
            _sb.AppendLine("<style>body {padding-top:1em;}</style>");
            _errors.Clear();
        }

        private void ParseTokens(List<Token> tokens, bool isOutput, bool getXml)
        {
            var isLoop = _loops.Count > 0 && _calculate && _isVal > -1;
            _stringVariablesDirty = false;
            _tableVariablesDirty = false;
            for (int i = 0, count = tokens.Count; i < count; ++i)
            {
                var token = tokens[i];
                if (token.Type == TokenTypes.Expression)
                {
                    try
                    {
                        var expressionText = token.Value;

                        // Check if expression is a bare table variable reference
                        if (_tableVariables.Count > 0 && IsTableVariableReference(expressionText))
                        {
                            if (isOutput && _calculate)
                                _sb.Append(RenderTableAsHtml(_tableVariables[expressionText.Trim()]));
                            continue;
                        }

                        // Check if expression is a bare string variable reference
                        if (_stringVariables.Count > 0 && IsStringVariableReference(expressionText))
                        {
                            if (isOutput && _calculate)
                                _sb.Append(HttpUtility.HtmlEncode(_stringVariables[expressionText.Trim()]));
                            continue;
                        }

                        // Pre-process: expand string functions and variables before MathParser
                        var hadStringFunctions = expressionText.Contains("$(");
                        if (_stringVariables.Count > 0 || _tableVariables.Count > 0 || hadStringFunctions)
                            expressionText = PreProcessExpression(expressionText);

                        // If the expression was entirely a string function and the result
                        // is not a valid math expression, output it directly as text
                        if (hadStringFunctions && !expressionText.Contains("$(") &&
                            IsStringResult(token.Value, expressionText))
                        {
                            if (isOutput && _calculate)
                                _sb.Append(HttpUtility.HtmlEncode(expressionText));
                            continue;
                        }

                        var cacheID = token.CacheID;

                        // Invalidate cache if string/table variables changed and expression references them
                        if (cacheID >= 0 && _stringVariablesDirty && ExpressionReferencesStringVariable(token.Value))
                            cacheID = -1;
                        if (cacheID >= 0 && _tableVariablesDirty && ExpressionReferencesTableVariable(token.Value))
                            cacheID = -1;
                        if (cacheID < 0)
                        {
                            if (_pendingUi != null && Settings.EnableUi)
                            {
                                // When explicit rows/columns are given, replace the RHS bracket
                                // literal with a zero matrix of that size before any other handling.
                                if (_pendingUi.Type == "datagrid")
                                    expressionText = ResizeDatagridMatrixToFit(expressionText);

                                // Capture matrix/vector values for datagrid before override
                                if (_pendingUi.Type == "datagrid")
                                    CaptureDatagridValues(expressionText);

                                var overridden = ApplyUiOverride(expressionText.AsSpan());
                                if (overridden != null)
                                {
                                    expressionText = overridden;
                                    // Re-capture values from the overridden expression
                                    if (_pendingUi.Type == "datagrid")
                                        CaptureDatagridValues(expressionText);
                                }
                            }
                            _parser.Parse(expressionText);
                            if (isLoop)
                                tokens[i].CacheID = _parser.WriteEquationToCache(isOutput);
                        }
                        else
                            _parser.ReadEquationFromCache(cacheID);

                        if (_calculate && _isVal > -1)
                            _parser.Calculate(isOutput, cacheID);
                        else
                            _parser.DefineCustomUnits();

                        if (isOutput)
                        {
                            if (_isVal == 1 && _calculate)
                                _sb.Append(_parser.ResultAsVal);
                            else
                            {
                                var html = _parser.ToHtml();
                                if (_pendingUi != null && Settings.EnableUi)
                                {
                                    if (_pendingUi.Type == "datagrid")
                                    {
                                        // Strip matrix/vector output, keep only "v ="
                                        html = StripDatagridRhs(html);
                                    }
                                    else
                                    {
                                        html = InjectUiInput(html, _currentLine);
                                        ResetUiState();
                                    }
                                }
                                if (getXml && Settings.Math.FormatEquations)
                                {
                                    var xml = _parser.ToXml();
                                    OpenXmlExpressions.Add(xml);
                                    _sb.Append($"<span class=\"eq\" id=\"eq-{OpenXmlExpressions.Count - 1}\">{html}</span>");
                                }
                                else
                                    _sb.Append($"<span class=\"eq\">{html}</span>");
                            }
                        }
                    }
                    catch (MathParserException ex)
                    {
                        _parser.ResetStack();
                        string errText;
                        if (!_calculate && token.Value.Contains('?'))
                            errText = token.Value.Replace("?", "<input type=\"text\" size=\"2\" name=\"Var\">");
                        else
                            errText = HttpUtility.HtmlEncode(token.Value);
                        errText = string.Format(Messages.Error_in_0_on_line_1_2, errText, LineHtml(_currentLine), ex.Message);
                        _sb.Append($"<span class=\"err\"{Id(_currentLine)}>{errText}</span>");
                        if (Debug)
                            _errors.Enqueue(_currentLine);

                        if (++_errorCount == 40)
                            throw new MathParserException(Messages.Too_many_errors);
                    }
                }
                else if (isOutput)
                {
                    // Expand string variables in text tokens too
                    var text = token.Value;
                    if (_stringVariables.Count > 0 && _calculate)
                        text = ExpandStringVariables(text);
                    _sb.Append(text);
                }
            }
        }

        void AppendError(string lineContent, string text, int line)
        {
            string s = lineContent.Replace("<", "&lt;").Replace(">", "&gt;");
            _sb.Append(ErrHtml(string.Format(Messages.Error_in_0_on_line_1_2, s, LineHtml(line), text), line));

            if (Debug)
                _errors.Enqueue(line);
        }

        private static string LineHtml(int line) => $"[<a href=\"#0\" data-text=\"{line + 1}\">{line + 1}</a>]";
        private string ErrHtml(string text, int line) => $"<p class=\"err\"{Id(line)}\">{text}</p>";
        private string Id(int line) => Debug ? $" id=\"line-{line + 1}\"" : string.Empty;

        private static string InsertAttribute(ReadOnlySpan<char> s, string attr)
        {
            if (s.Length > 2 && s[0] == '<' && char.IsLetter(s[1]))
            {
                var i = s.IndexOf('>');
                if (i > 1)
                {
                    var j = i;
                    while (j > 1)
                    {
                        --j;
                        if (s[j] != ' ')
                        {
                            if (s[j] == '/')
                                i = j;

                            break;
                        }
                    }
                    return s[..i].ToString() + attr + s[i..].ToString();
                }
            }
            return s.ToString();
        }

        private void ApplyUnits(StringBuilder sb, bool calculate)
        {
            string unitsHtml = calculate ?
                Settings.Units :
                string.Concat("<span class=\"Units\">", Settings.Units, "</span>");

            long len = sb.Length;
            sb.Replace("%u", unitsHtml);
            if (calculate || sb.Length == len)
                return;

            sb.Insert(0, "<select id=\"Units\" name=\"Units\"><option value=\"m\"> m </option><option value=\"cm\"> cm </option><option value=\"mm\"> mm </option></select>");
        }

        private double UnitsFactor() => Settings.Units switch
        {
            "mm" => 1000,
            "cm" => 100,
            "m" => 1,
            _ => 0
        };
    }
}