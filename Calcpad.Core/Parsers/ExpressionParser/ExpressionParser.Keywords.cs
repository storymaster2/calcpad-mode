
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private enum Keyword
        {
            None,
            Hide,
            Show,
            Pre,
            Post,
            Val,
            Equ,
            Noc,
            NoSub,
            NoVar,
            VarSub,
            Const,
            Split,
            Wrap,
            Deg,
            Rad,
            Gra,
            Round,
            Format,
            If,
            Else_If,
            Else,
            End_If,
            While,
            For,
            Repeat,
            Loop,
            Break,
            Continue,
            Local,
            Global,
            Pause,
            Input,
            Md,
            Read,
            Write,
            Append,
            Phasor,
            Complex,
            String,
            Ui,
            Html,
            Cpd,
            Markdown,
            SkipLine
        }
        private enum KeywordResult  
        {
            None,
            Continue,
            Break
        }

        private Keyword _previousKeyword = Keyword.None;
        private static string[] KeywordNames;
        private static Keyword[] KeywordValues;
        private static List<int>[] KeywordIndex;
        private static int MaxKeywordLength;

        private static void InitKeyWordStrings()
        {
            var n = 'z' - 'a';
            KeywordNames = Enum.GetNames<Keyword>().Skip(1).ToArray();
            MaxKeywordLength = KeywordNames.Max(s => s.Length);
            KeywordValues = Enum.GetValues<Keyword>().Skip(1).ToArray();
            KeywordIndex = new List<int>[n];
            for (int i = 0, len = KeywordNames.Length; i < len; ++i)
            {
                var lower = KeywordNames[i].ToLowerInvariant().Replace('_', ' ');
                KeywordNames[i] = lower;
                var j = lower[0] - 'a';
                if (KeywordIndex[j] is null)
                    KeywordIndex[j] = [i];
                else
                    KeywordIndex[j].Add(i);
            }
        }

        private static Keyword GetKeyword(ReadOnlySpan<char> s)
        {
            var n = Math.Min(MaxKeywordLength, s.Length - 1);
            if (n < 3)
                return Keyword.None;

            var i = char.ToLowerInvariant(s[1]) - 'a';
            if (i < 0 || i >= KeywordNames.Length)
                return Keyword.None;

            var ind = KeywordIndex[i];
            if (ind is null)
                return Keyword.None;

            Span<char> lower = stackalloc char[n];
            s.Slice(1, n).ToLowerInvariant(lower);
            Keyword best = Keyword.None;
            int bestLen = 0;
            for (int j = 0; j < ind.Count; ++j)
            {
                var k = ind[j];
                var kwLen = KeywordNames[k].Length;
                if (kwLen > bestLen && lower.StartsWith(KeywordNames[k]))
                {
                    best = KeywordValues[k];
                    bestLen = kwLen;
                }
            }
            return best;
        }

        KeywordResult ParseKeyword(ReadOnlySpan<char> s, ref Keyword keyword)
        {
            if (_isPausedByUser)
                keyword = Keyword.Pause;
            else if (s[0] == '#' && keyword == Keyword.None)
                keyword = GetKeyword(s);

            if (keyword == Keyword.None)
                return KeywordResult.None;

            if (IsNonCpdMode && IsBlockedInNonCpdMode(keyword))
            {
                var modeName = _parseMode == ParseMode.Html ? "#HTML" : "#markdown";
                AppendError(s.ToString(), $"Keyword is not available in {modeName} mode.", _currentLine);
                return KeywordResult.Continue;
            }

            switch (keyword)
            {
                case Keyword.Hide:
                    _isVisible = false;
                    break;
                case Keyword.Show:
                    _isVisible = true;
                    break;
                case Keyword.Pre:
                    _isVisible = !_calculate;
                    break;
                case Keyword.Post:
                    _isVisible = _calculate;
                    break;
                case Keyword.Input:
                    return ParseKeywordInput();
                case Keyword.Pause:
                    return ParseKeywordPause();
                case Keyword.Val:
                    _isVal = 1;
                    break;
                case Keyword.Equ:
                    _isVal = 0;
                    break;
                case Keyword.Noc:
                    _isVal = -1;
                    break;
                case Keyword.NoSub:
                    _parser.VariableSubstitution = MathParser.VariableSubstitutionOptions.VariablesOnly;
                    break;
                case Keyword.NoVar:
                    _parser.VariableSubstitution = MathParser.VariableSubstitutionOptions.SubstitutionsOnly;
                    break;
                case Keyword.VarSub:
                    _parser.VariableSubstitution = MathParser.VariableSubstitutionOptions.VariablesAndSubstitutions;
                    break;
                case Keyword.Const:
                    _parser.IsConst = true;
                    return KeywordResult.None;
                case Keyword.Split:
                    _parser.Split = true;
                    break;
                case Keyword.Wrap:
                    _parser.Split = false;
                    break;
                case Keyword.Deg:
                    _parser.Degrees = 0;
                    break;
                case Keyword.Rad:
                    _parser.Degrees = 1;
                    break;
                case Keyword.Gra:
                    _parser.Degrees = 2;
                    break;
                case Keyword.Round:
                    ParseKeywordRound(s);
                    break;
                case Keyword.Format:
                    ParseKeywordFormat(s);
                    break;
                case Keyword.Repeat:
                    ParseKeywordRepeat(s);
                    break;
                case Keyword.For:
                    ParseKeywordFor(s);
                    break;
                case Keyword.While:
                    ParseKeywordWhile(s);
                    break;
                case Keyword.Loop:
                    ParseKeywordLoop(s);
                    break;
                case Keyword.Break:
                    if (ParseKeywordBreak())
                        return KeywordResult.Break;
                    break;
                case Keyword.Continue:
                    ParseKeywordContinue();
                    break;
                case Keyword.Md:
                    ParseKeywordMd(s);
                    break;
                case Keyword.Read:
                    ParseKeywordRead(s);
                    break;
                case Keyword.Write:
                case Keyword.Append:
                    ParseKeywordWrite(s, keyword);
                    break;
                case Keyword.Phasor:
                    _parser.Phasor = true;
                    break;
                case Keyword.Complex:
                    _parser.Phasor = false;
                    break;
                case Keyword.String:
                    return ParseKeywordString(s);
                case Keyword.Ui:
                    return ParseKeywordUi(s);
                case Keyword.Html:
                    _parseMode = ParseMode.Html;
                    break;
                case Keyword.Cpd:
                    _parseMode = ParseMode.Cpd;
                    break;
                case Keyword.Markdown:
                    _parseMode = ParseMode.Markdown;
                    break;
                default:
                    if (keyword != Keyword.Global && keyword != Keyword.Local)
                        return KeywordResult.None;
                    break;
            }
            return KeywordResult.Continue;
        }

        KeywordResult ParseKeywordInput()
        {
            if (_condition.IsSatisfied)
            {
                _previousKeyword = Keyword.Input;
                if (_calculate)
                {
                    _startLine = _currentLine + 1;
                    _pauseCharCount = _sb.Length;
                    _calculate = false;
                    return KeywordResult.Continue;
                }
                return KeywordResult.Break;
            }
            return _calculate ? KeywordResult.Continue : KeywordResult.Break;
        }

        KeywordResult ParseKeywordPause()
        {
            if (_condition.IsSatisfied && (_calculate || _startLine > 0))
            {
                if (_calculate)
                {
                    if (_isPausedByUser)
                        _startLine = _currentLine;
                    else
                        _startLine = _currentLine + 1;
                }

                if (_previousKeyword != Keyword.Input)
                    _pauseCharCount = _sb.Length;

                _previousKeyword = Keyword.Pause;
                _isPausedByUser = false;
                return KeywordResult.Break;
            }
            if (_isVisible && !_calculate)
                _sb.Append($"<p{HtmlId} class=\"cond\">#pause</p>");

            return KeywordResult.Continue;
        }

        private void ParseKeywordRound(ReadOnlySpan<char> s)
        {
            if (s.Length > 6)
            {
                var expr = s[6..].Trim();
                if (expr.SequenceEqual("default"))
                    Settings.Math.Decimals = _decimals;
                else if (int.TryParse(expr, out int n))
                    Settings.Math.Decimals = n;
                else
                {
                    try
                    {
                        _parser.Parse(expr);
                        _parser.Calculate();
                        Settings.Math.Decimals = (int)Math.Round(_parser.Real, MidpointRounding.AwayFromZero);
                    }
                    catch (MathParserException ex)
                    {
                        AppendError(s.ToString(), ex.Message, _currentLine);
                    }
                }
            }
            else
                Settings.Math.Decimals = _decimals;
        }

        private void ParseKeywordRepeat(ReadOnlySpan<char> s)
        {
            ReadOnlySpan<char> expression = s.Length > 7 ? // #repeat - 7    
                s[7..].Trim() :
                [];

            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    var count = 0d;
                    if (!expression.IsWhiteSpace())
                    {
                        try
                        {
                            _parser.Parse(expression);
                            _parser.Calculate();
                            if (_parser.Real > Loop.MaxCount)
                                AppendError(s.ToString(), string.Format(Messages.Number_of_iterations_exceeds_the_maximum_0, Loop.MaxCount), _currentLine);
                            else
                                count = Math.Round(_parser.Real, MidpointRounding.AwayFromZero);
                        }
                        catch (MathParserException ex)
                        {
                            AppendError(s.ToString(), ex.Message, _currentLine);
                        }
                    }
                    else
                        count = -1d;

                    _loops.Push(new RepeatLoop(_currentLine, count, _condition.Id));
                }
            }
            else if (_isVisible)
            {
                if (expression.IsWhiteSpace())
                    _sb.Append($"<p{HtmlId} class=\"cond\">#repeat</p><div class=\"indent\">");
                else
                {
                    try
                    {
                        _parser.Parse(expression);
                        _sb.Append($"<p{HtmlId}><span class=\"cond\">#repeat</span> <span class=\"eq\">{_parser.ToHtml()}</span></p><div class=\"indent\">");
                    }
                    catch (MathParserException ex)
                    {
                        AppendError(s.ToString(), ex.Message, _currentLine);
                    }
                }
            }
        }

        private void ParseKeywordFor(ReadOnlySpan<char> s)
        {
            ReadOnlySpan<char> expression = s.Length > 4 ? // #for - 4
                s[4..].Trim() :
                [];

            if (expression.IsWhiteSpace())
                throw Exceptions.ExpressionEmpty();

            (int loopStart, int loopEnd) = GetForLoopLimits(expression);
            if (loopStart > -1 &&
                loopEnd > loopStart)
            {
                var varName = expression[..loopStart].Trim().ToString();
                var startExpr = expression[(loopStart + 1)..loopEnd].Trim();
                var endExpr = expression[(loopEnd + 1)..].Trim();
                if (Validator.IsVariable(varName))
                {
                    if (_calculate)
                    {
                        if (_condition.IsSatisfied)
                        {
                            try
                            {
                                _parser.Parse(startExpr);
                                _parser.Calculate();
                                var r1 = _parser.Result;
                                var u1 = _parser.Units;
                                _parser.Parse(endExpr);
                                _parser.Calculate();
                                var r2 = _parser.Result;
                                var u2 = _parser.Units;
                                IScalarValue start, end;
                                if (r1.IsReal && r2.IsReal)
                                {
                                    start = new RealValue(r1.Re, u1);
                                    end = new RealValue(r2.Re, u2);
                                }
                                else
                                {
                                    start = new ComplexValue(r1, u1);
                                    end = new ComplexValue(r2, u2);
                                }
                                var count = Math.Abs((end - start).Re) + 1;
                                if (count > Loop.MaxCount)
                                {
                                    AppendError(s.ToString(), string.Format(Messages.Number_of_iterations_exceeds_the_maximum_0, Loop.MaxCount), _currentLine);
                                    return;
                                }
                                var counter = _parser.GetVariableRef(varName);
                                _loops.Push(new ForLoop(_currentLine, start, end, counter, _condition.Id));
                                _parser.SetVariable(varName, start);
                            }
                            catch (MathParserException ex)
                            {
                                AppendError(s.ToString(), ex.Message, _currentLine);
                            }
                        }
                    }
                    else if (_isVisible)
                    {
                        try
                        {
                            var varHtml = new HtmlWriter(null, _parser.Phasor).FormatVariable(varName, string.Empty, false);
                            _parser.Parse(startExpr);
                            var startHtml = _parser.ToHtml();
                            _parser.Parse(endExpr);
                            var endHtml = _parser.ToHtml();
                            _sb.Append($"<p{HtmlId}><span class=\"cond\">#for</span> <span class=\"eq\">{varHtml} = {startHtml} : {endHtml}</span></p><div class=\"indent\">");
                        }
                        catch (MathParserException ex)
                        {
                            AppendError(s.ToString(), ex.Message, _currentLine);
                        }
                    }
                }
            }
        }

        private void ParseKeywordWhile(ReadOnlySpan<char> s)
        {
            ReadOnlySpan<char> expression = s.Length > 6 ? // #while - 6
                s[7..].Trim() :
                [];

            if (expression.IsWhiteSpace())
                throw Exceptions.ExpressionEmpty();

            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    try
                    {
                        var commentStart = expression.IndexOf('\'');
                        var condition = commentStart < 0 ? expression : expression[..commentStart];
                        _parser.Parse(condition);
                        _parser.Calculate();
                        _condition.SetCondition(Keyword.While - Keyword.If);
                        _condition.Check(_parser.Result);
                        if (_condition.IsSatisfied)
                        {
                            _loops.Push(new WhileLoop(_currentLine, expression.ToString(), _condition.Id));
                            if (commentStart >= 0)
                                ParseTokens(GetTokens(expression[commentStart..]), false, false);
                        }
                    }
                    catch (MathParserException ex)
                    {
                        AppendError(s.ToString(), ex.Message, _currentLine);
                    }
                }
            }
            else if (_isVisible)
            {
                try
                {
                    _sb.Append($"<p{HtmlId}><span class=\"cond\">#while</span> ");
                    ParseTokens(GetTokens(expression), true, false);
                    _sb.Append("</p><div class=\"indent\">");
                }
                catch (MathParserException ex)
                {
                    AppendError(s.ToString(), ex.Message, _currentLine);
                }
            }
        }

        private void ParseKeywordLoop(ReadOnlySpan<char> s)
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    if (_loops.Count == 0)
                        AppendError(s.ToString(), Messages.loop_without_a_corresponding_repeat, _currentLine);
                    else
                    {
                        var next = _loops.Peek();
                        if (next.Id != _condition.Id)
                            AppendError(s.ToString(), Messages.Entangled_if__end_if__and_repeat__loop_blocks, _currentLine);
                        else if (!Iterate(next, true))
                            _loops.Pop();
                    }
                }
                else if (_condition.IsLoop)
                    _condition.SetCondition(Condition.RemoveConditionKeyword);
            }
            else if (_isVisible)
                _sb.Append($"</div><p{HtmlId} class=\"cond\">#loop</p>");
        }

        private bool Iterate(Loop loop, bool removeWhileCondition)
        {
            if (loop is ForLoop forLoop)
                forLoop.IncrementCounter();
            else if (loop is WhileLoop whileLoop)
            {
                var expression = whileLoop.Condition;
                var commentStart = expression.IndexOfAny(['\'', '"']);
                if (commentStart < 0)
                    commentStart = expression.Length;

                var condition = expression.AsSpan(0, commentStart);
                _parser.Parse(condition);
                _parser.Calculate();
                _condition.Check(_parser.Result);
                if (_condition.IsSatisfied)
                {
                    if (commentStart < expression.Length - 1)
                        ParseTokens(GetTokens(expression.AsSpan(commentStart)), false, false);
                }
                else
                {
                    if (removeWhileCondition)
                        _condition.SetCondition(Condition.RemoveConditionKeyword);

                    loop.Break();
                }
            }
            if (loop.Iterate(ref _currentLine))
            {
                _parser.ResetStack();
                return true;
            }
            return false;
        }

        private bool ParseKeywordBreak()
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    if (_loops.Count != 0)
                        _loops.Peek().Break();
                    else
                        return true;
                }
            }
            else if (_isVisible)
                _sb.Append($"<p{HtmlId} class=\"cond\">#break</p>");

            return false;
        }

        internal void ParseKeywordContinue()
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    if (_loops.Count == 0)
                        AppendError("#continue", Messages.continue_without_a_corresponding_repeat, _currentLine);
                    else
                    {
                        var loop = _loops.Peek();
                        if (Iterate(loop, false))
                            while (_condition.Id > loop.Id)
                                _condition.SetCondition(Condition.RemoveConditionKeyword);
                        else
                            loop.Break();
                    }
                }
            }
            else if (_isVisible)
                _sb.Append($"<p{HtmlId} class=\"cond\">#continue</p>");
        }

        private static (int, int) GetForLoopLimits(ReadOnlySpan<char> expression)
        {
            (int start, int end) = (-1, -1);
            int n1 = 0, n2 = 0, n3 = 0;
            for (int i = 0, len = expression.Length; i < len; ++i)
            {
                switch (expression[i])
                {
                    case '=': start = i; break;
                    case ':' when n1 == 0 && n2 == 0 && n3 == 0: end = i; return (start, end);
                    case '(': ++n1; break;
                    case ')': --n1; break;
                    case '{': ++n2; break;
                    case '}': --n2; break;
                    case '[': ++n3; break;
                    case ']': --n3; break;
                }
            }
            return (start, end);
        }

        private void ParseKeywordFormat(ReadOnlySpan<char> s)
        {
            if (s.Length > 7)
            {
                var expr = s[7..].Trim();
                if (expr.SequenceEqual("default"))
                    Settings.Math.FormatString = null;
                else
                {
                    var format = expr.ToString();
                    if (Validator.IsValidFormatString(format))
                        Settings.Math.FormatString = format;
                    else
                        AppendError("#format " + format, Messages.Invalid_format_string_0, _currentLine);
                }
            }
            else
                Settings.Math.FormatString = null;
        }

        private void ParseKeywordMd(ReadOnlySpan<char> s)
        {
            if (s.Length > 3)
            {
                var expr = s[3..].Trim();
                if (expr.Equals("on", StringComparison.OrdinalIgnoreCase))
                    _isMarkdownOn = true;
                else if (expr.Equals("off", StringComparison.OrdinalIgnoreCase))
                    _isMarkdownOn = false;
                else
                    AppendError(s.ToString(), string.Format(Messages.Invalid_keyword_0, expr.ToString()), _currentLine);
            }
            else
                _isMarkdownOn = true;
        }

        /// <summary>
        /// #string handles both scalar strings and string tables. The storage kind is
        /// inferred from the RHS (bracket literal or a table-producing function such as
        /// table$(...), split$(...), etc.) — mirroring how the numeric parser routes
        /// scalar vs. vector/matrix assignments without a separate keyword.
        /// </summary>
        private KeywordResult ParseKeywordString(ReadOnlySpan<char> s)
        {
            const int keywordLength = 7; // "#string"
            var content = s.Length > keywordLength ? s[keywordLength..].Trim() : [];
            if (content.IsEmpty)
            {
                AppendError(s.ToString(), "Expected string variable declaration after #string.", _currentLine);
                return KeywordResult.Continue;
            }

            var eqPos = content.IndexOf('=');
            if (eqPos < 0)
            {
                AppendError(s.ToString(), "Expected '=' in string variable declaration.", _currentLine);
                return KeywordResult.Continue;
            }

            var varName = content[..eqPos].Trim().ToString();
            if (varName.Length < 2 || varName[^1] != '$')
            {
                AppendError(s.ToString(), "String variable name must end with '$'.", _currentLine);
                return KeywordResult.Continue;
            }

            var rhs = content[(eqPos + 1)..].Trim();

            if (_calculate && _condition.IsSatisfied)
            {
                if (IsTableRhs(rhs))
                {
                    _tableVariables[varName] = EvaluateTableExpression(rhs);
                    _stringVariables.Remove(varName);
                    _tableVariablesDirty = true;
                }
                else
                {
                    _stringVariables[varName] = EvaluateStringExpression(rhs);
                    _tableVariables.Remove(varName);
                    _stringVariablesDirty = true;
                }
            }

            if (_isVisible && !_calculate)
            {
                _sb.Append($"<p{HtmlId}><span class=\"cond\">#string</span> {System.Web.HttpUtility.HtmlEncode(varName)} = {System.Web.HttpUtility.HtmlEncode(rhs.ToString())}</p>");
            }

            return KeywordResult.Continue;
        }

        /// <summary>
        /// Detects a string-table RHS: a bracket literal of string cells, or one of the
        /// table-returning string functions. Called from #string and from #UI's string
        /// branch to decide whether to route the assignment to _tableVariables.
        /// </summary>
        private bool IsTableRhs(ReadOnlySpan<char> rhs)
        {
            if (rhs.Length >= 2 && rhs[0] == '[' && rhs[^1] == ']')
                return true;

            // Table-producing string functions
            ReadOnlySpan<string> tableFuncs =
            [
                "table$(", "split$(", "augmentT$(", "stackT$(",
                "rowT$(", "colT$(", "extractRowsT$(", "extractColsT$(",
                "subTable$(", "transposeT$("
            ];
            foreach (var fn in tableFuncs)
            {
                if (rhs.StartsWith(fn, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // string$(matrix-or-vector) / string$(matrix-or-vector; flag) — convert to table.
            // Quick literal check first, then peek at the inner expression's result type.
            if (rhs.StartsWith("string$(", StringComparison.OrdinalIgnoreCase) && rhs[^1] == ')')
            {
                var inner = rhs[8..^1].ToString();
                var args = ParseStringFunctionArgs(inner);
                if (args.Length >= 1)
                {
                    var firstArg = args[0].Trim();
                    if (firstArg.Length >= 2 && firstArg[0] == '[' && firstArg[^1] == ']')
                        return true;

                    try
                    {
                        _parser.Parse(firstArg);
                        _parser.Calculate();
                        var typeName = _parser.ResultTypeName;
                        if (typeName == "matrix" || typeName == "vector")
                            return true;
                    }
                    catch
                    {
                        // Fall through — treat as scalar/string when inner can't be evaluated here.
                    }
                }
            }

            return false;
        }

        private void ParseKeywordRead(ReadOnlySpan<char> s)
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    var sourceDir = !string.IsNullOrEmpty(Settings.SourceFilePath)
                        ? System.IO.Path.GetDirectoryName(Settings.SourceFilePath) : null;
                    var options = new ReadWriteOptions(s, 0, sourceDir);
                    if (options.Name.IsEmpty)
                        return;

                    if (options.Type == 'X')
                    {
                        var varName = options.Name.ToString();
                        var content = DataExchange.ReadString(options, Settings.ClientFileCache);
                        _stringVariables[varName] = content;
                        _tableVariables.Remove(varName);
                        _stringVariablesDirty = true;
                    }
                    else
                    {
                        var data = DataExchange.Read(options, Settings.ClientFileCache);
                        if (options.Type == 'T')
                        {
                            var varName = options.Name.ToString();
                            _tableVariables[varName] = JaggedToRectangular(data);
                            _stringVariables.Remove(varName);
                            _tableVariablesDirty = true;
                        }
                        else if (options.Type == 'V')
                            _parser.SetVector(options.Name, data, options.IsHp);
                        else
                            _parser.SetMatrix(options.Name, data, options.Type, options.IsHp);
                    }

                    if (_isVisible)
                        ReportDataExchageResult(options, "read from");
                }
            }
            else if (_isVisible)
                _sb.Append($"<p><span{HtmlId} class=\"cond\">#read</span> {s[5..]}</p>");
        }

        private void ParseKeywordWrite(ReadOnlySpan<char> s, Keyword keyword)
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    var sourceDir = !string.IsNullOrEmpty(Settings.SourceFilePath)
                        ? System.IO.Path.GetDirectoryName(Settings.SourceFilePath) : null;
                    var options = new ReadWriteOptions(s, keyword - Keyword.Read, sourceDir);
                    if (options.Name.IsEmpty)
                        return;

                    if (options.Type == 'X')
                    {
                        var varName = options.Name.ToString();
                        if (!_stringVariables.TryGetValue(varName, out var content))
                            throw new MathParserException($"String variable \"{varName}\" does not exist.");
                        DataExchange.WriteString(options, content);
                    }
                    else
                    {
                        string[][] m;
                        if (options.Type == 'T')
                        {
                            var varName = options.Name.ToString();
                            if (!_tableVariables.TryGetValue(varName, out var table))
                                throw new MathParserException($"Table variable \"{varName}\" does not exist.");
                            m = RectangularToJagged(table);
                        }
                        else
                            m = _parser.GetMatrix(options.Name.ToString(), options.Type);

                        DataExchange.Write(options, m);
                    }
                    if (_isVisible)
                        ReportDataExchageResult(options, keyword == Keyword.Write ? "written to" : "appended to");
                }
            }
            else if (_isVisible)
                _sb.Append($"<p><span{HtmlId} class=\"cond\">#write</span> {s[6..]}</p>");
        }

        private static string[,] JaggedToRectangular(string[][] jagged)
        {
            int rows = jagged.Length;
            int cols = 0;
            for (int i = 0; i < rows; i++)
                if (jagged[i] != null && jagged[i].Length > cols)
                    cols = jagged[i].Length;

            var result = new string[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < (jagged[i]?.Length ?? 0); j++)
                    result[i, j] = jagged[i][j];

            return result;
        }

        private static string[][] RectangularToJagged(string[,] rect)
        {
            int rows = rect.GetLength(0);
            int cols = rect.GetLength(1);
            var result = new string[rows][];
            for (int i = 0; i < rows; i++)
            {
                result[i] = new string[cols];
                for (int j = 0; j < cols; j++)
                    result[i][j] = rect[i, j] ?? string.Empty;
            }
            return result;
        }

        private static bool IsBlockedInNonCpdMode(Keyword keyword) => keyword switch
        {
            Keyword.Val or Keyword.Equ or Keyword.Noc or
            Keyword.NoSub or Keyword.NoVar or Keyword.VarSub or
            Keyword.Const or Keyword.Split or Keyword.Wrap or
            Keyword.Deg or Keyword.Rad or Keyword.Gra or
            Keyword.Round or Keyword.Format or
            Keyword.For or Keyword.While or Keyword.Repeat or
            Keyword.Loop or Keyword.Break or Keyword.Continue or
            Keyword.Phasor or Keyword.Complex or
            Keyword.Pause or Keyword.Input => true,
            _ => false
        };

        private void ReportDataExchageResult(ReadWriteOptions options, string command)
        {
            var url = $"file:///{options.FullPath.Replace('\\', '/')}";
            _sb.Append($"<p{HtmlId}>")
               .Append($"{(options.Type == 'X' ? "String" : options.Type == 'T' ? "Table" : "Matrix")} <span class=\"eq\">{new HtmlWriter(Settings.Math, false).FormatVariable(options.Name.ToString(), string.Empty, true)}</span>")
               .Append($" was successfully {command} <a href=\"{url}\">{options.Path}.{options.Ext}</a>");
            if (options.IsExcel)
            {
                if (!options.Sheet.IsEmpty)
                    _sb.Append($"@{options.Sheet}");
                if (!options.Start.IsEmpty)
                    _sb.Append($"!{options.Start}");
                if (!options.End.IsEmpty)
                    _sb.Append($":{options.End}");
            }
            else
            {
                if (!options.Start.IsEmpty)
                    _sb.Append($"@{options.Start}");
                if (!options.End.IsEmpty)
                    _sb.Append($":{options.End}");

                _sb.Append($" <small>SEP</small>='{options.Separator}'");
            }
            _sb.Append($" <small>TYPE</small>={options.Type}");
            _sb.Append("</p>");
        }
    }
}