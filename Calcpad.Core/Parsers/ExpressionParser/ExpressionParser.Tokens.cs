using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private sealed class Token
        {
            internal string Value { get; set; }
            internal TokenTypes Type;
            internal int CacheID = -1;
            internal Token(string value, TokenTypes type)
            {
                Value = value;
                Type = type;
            }
            public override string ToString() => Value;
        }

        private enum TokenTypes
        {
            Expression,
            Heading,
            Text,
            Html,
            Error
        }

        private List<Token> GetTokens(ReadOnlySpan<char> s)
        {
            var tokens = new List<Token>();
            var ts = new TextSpan(s);
            var currentSeparator = ' ';
            var bracketIsString = new Stack<bool>();
            var inStringFunc = 0;
            for (int i = 0, len = s.Length; i < len; ++i)
            {
                var c = s[i];
                if (currentSeparator == ' ')
                {
                    if (c == '(')
                    {
                        var isString = i > 0 && s[i - 1] == '$';
                        bracketIsString.Push(isString);
                        if (isString) inStringFunc++;
                    }
                    else if (c == ')' && bracketIsString.Count > 0)
                    {
                        if (bracketIsString.Pop()) inStringFunc--;
                    }
                }
                if (c == '\'' || c == '\"')
                {
                    // In expression mode, check if single quote starts a string literal
                    if (c == '\'' && currentSeparator == ' ' && IsStringLiteralContext(s, i, inStringFunc))
                    {
                        // Scan forward for matching closing quote, skipping '' escapes
                        var closePos = -1;
                        for (int j = i + 1; j < len; j++)
                        {
                            if (s[j] == '\'')
                            {
                                if (j + 1 < len && s[j + 1] == '\'')
                                {
                                    j++; // skip '' escape
                                    continue;
                                }
                                closePos = j;
                                break;
                            }
                        }
                        if (closePos >= 0)
                        {
                            // Include opening quote, content, and closing quote in expression
                            var totalChars = closePos - i + 1;
                            for (int j = 0; j < totalChars; j++)
                                ts.Expand();
                            i = closePos; // loop's ++i advances past closing quote
                            continue;
                        }
                    }
                    if (currentSeparator == ' ' || currentSeparator == c)
                    {
                        if (currentSeparator == c)
                        {
                            var i1 = i + 1;
                            if (i1 < len && s[i1] == currentSeparator)
                            {
                                ts.Expand();
                                ts.Expand();
                                i = i1;
                                continue;
                            }
                        }
                        if (!ts.IsEmpty)
                            AddToken(tokens, ts.Cut(), currentSeparator);

                        ts.Reset(i + 1);
                        currentSeparator = currentSeparator == c ? ' ' : c;
                    }
                    else if (currentSeparator != ' ')
                        ts.Expand();
                }
                else
                    ts.Expand();
            }
            if (!ts.IsEmpty)
                AddToken(tokens, ts.Cut(), currentSeparator);

            return tokens;
        }

        private static bool IsStringLiteralContext(ReadOnlySpan<char> s, int quotePos, int inStringFunc)
        {
            if (inStringFunc > 0)
                return true;

            // Scan backwards from quote, skipping whitespace
            var i = quotePos - 1;
            while (i >= 0 && s[i] == ' ')
                i--;

            if (i < 0)
                return false;

            // Check for comparison operators: == / != / ≡ / ≠ preceded by '$'
            if (s[i] == '≡' || s[i] == '≠')
            {
                i--;
                while (i >= 0 && s[i] == ' ')
                    i--;
                return i >= 0 && s[i] == '$';
            }

            if (s[i] == '=' && i > 0 && (s[i - 1] == '=' || s[i - 1] == '!'))
            {
                i -= 2;
                while (i >= 0 && s[i] == ' ')
                    i--;
                return i >= 0 && s[i] == '$';
            }

            // Check for string variable assignment: varName$ = '...'
            if (s[i] != '=')
                return false;

            i--;
            while (i >= 0 && s[i] == ' ')
                i--;

            return i >= 0 && s[i] == '$';
        }

        private void AddToken(List<Token> tokens, ReadOnlySpan<char> value, char separator)
        {
            var tokenValue = value.ToString().Replace("\"\"", "&quot;").Replace("''", "&apos;");
            var tokenType = GetTokenType(separator);
            if (tokenType == TokenTypes.Expression)
            {
                if (value.IsWhiteSpace())
                    return;
            }
            else if (_isVal < 1)
            {
                if (tokens.Count == 0)
                    tokenValue += " ";
                else
                    tokenValue = string.Concat(" ", tokenValue," ");
            }

            var token = new Token(tokenValue, tokenType);
            if (token.Type == TokenTypes.Text)
            {
                tokenValue = tokenValue.TrimStart();
                if (tokenValue.Length > 0 && tokenValue[0] == '<')
                    token.Type = TokenTypes.Html;
            }
            tokens.Add(token);
        }

        private static TokenTypes GetTokenType(char separator)
        {
            return separator switch
            {
                ' ' => TokenTypes.Expression,
                '\"' => TokenTypes.Heading,
                '\'' => TokenTypes.Text,
                _ => TokenTypes.Error,
            };
        }
    }
}