using System;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Output-value mode set by the #equ / #val / #noc directives (mirrors ExpressionParser's
    /// _isVal of 0 / 1 / -1). Under <see cref="NoCalculation"/> equations are rendered but not
    /// evaluated, so identifiers need not resolve to a definition.
    /// </summary>
    public enum OutputMode
    {
        Equations,
        Values,
        NoCalculation
    }

    /// <summary>
    /// Scope mode set by the #local / #global directives. A #local section is excluded when the
    /// file is pulled in through #include (mirrors Core's CalcpadReader.Include filtering).
    /// </summary>
    public enum ScopeMode
    {
        Global,
        Local
    }

    /// <summary>
    /// Tracks the running state of Calcpad's mode directives as lines are visited in order.
    /// Calcpad has several independent directive categories, and within each the most recent
    /// directive wins (see ExpressionParser.ParseKeyword):
    ///   - output value:  #equ / #val / #noc
    ///   - scope:          #global / #local
    ///   - markdown:       #md [on] / #md off
    ///   - substitution:   #varsub / #nosub / #novar
    ///   - angle:          #rad / #deg / #gra
    ///   - line breaking:  #wrap / #split
    ///   - number type:    #complex / #phasor
    ///   - visibility:     #show / #hide / #pre / #post
    /// Only the categories consumed by the tooling are tracked; the rest are ignored.
    /// </summary>
    public sealed class DirectiveState
    {
        public OutputMode Output { get; private set; } = OutputMode.Equations;
        public ScopeMode Scope { get; private set; } = ScopeMode.Global;
        public bool IsMarkdownOn { get; private set; }

        /// <summary>
        /// Updates the tracked state from a trimmed directive line. Non-tracked directives are ignored.
        /// Prefix matching mirrors Core's Validator.IsKeyword / ExpressionParser.GetKeyword.
        /// </summary>
        public void Apply(ReadOnlySpan<char> trimmedLine)
        {
            if (Matches(trimmedLine, "#equ"))
                Output = OutputMode.Equations;
            else if (Matches(trimmedLine, "#val"))
                Output = OutputMode.Values;
            else if (Matches(trimmedLine, "#noc"))
                Output = OutputMode.NoCalculation;
            else if (Matches(trimmedLine, "#global"))
                Scope = ScopeMode.Global;
            else if (Matches(trimmedLine, "#local"))
                Scope = ScopeMode.Local;
            else if (Matches(trimmedLine, "#md"))
                ApplyMarkdown(trimmedLine);
        }

        // #md and #md on enable markdown; #md off disables it (mirrors ExpressionParser.ParseKeywordMd).
        private void ApplyMarkdown(ReadOnlySpan<char> trimmedLine)
        {
            var arg = trimmedLine.Length > 3 ? trimmedLine[3..].Trim() : ReadOnlySpan<char>.Empty;
            if (arg.Equals("off", StringComparison.OrdinalIgnoreCase))
                IsMarkdownOn = false;
            else if (arg.IsEmpty || arg.Equals("on", StringComparison.OrdinalIgnoreCase))
                IsMarkdownOn = true;
        }

        private static bool Matches(ReadOnlySpan<char> trimmedLine, ReadOnlySpan<char> directive) =>
            trimmedLine.StartsWith(directive, StringComparison.OrdinalIgnoreCase);
    }
}
