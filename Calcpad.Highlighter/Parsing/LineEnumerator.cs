using System;

namespace Calcpad.Highlighter.Parsing
{
    /// <summary>
    /// Zero-allocation line enumerator that handles \r\n, \r, and \n line endings.
    /// Replaces string.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).
    /// Returns ReadOnlySpan&lt;char&gt; per line without allocating strings.
    /// </summary>
    public ref struct LineEnumerator(ReadOnlySpan<char> span)
    {
        private ReadOnlySpan<char> _span = span;
        private bool _hasMore = true;

        public readonly LineEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (!_hasMore)
                return false;

            if (_span.IsEmpty)
            {
                // Final empty line after trailing newline
                Current = default;
                _hasMore = false;
                return true;
            }

            var i = _span.IndexOfAny('\r', '\n');
            if (i < 0)
            {
                // Last line, no trailing newline
                Current = _span;
                _span = [];
                _hasMore = false;
                return true;
            }

            Current = _span[..i];

            // Advance past the line ending
            if (i < _span.Length - 1 && _span[i] == '\r' && _span[i + 1] == '\n')
            {
                // \r\n
                _span = _span[(i + 2)..];
            }
            else
            {
                // \r or \n
                _span = _span[(i + 1)..];
            }

            return true;
        }

        public ReadOnlySpan<char> Current { get; private set; } = default;
    }
}
