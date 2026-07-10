namespace Calcpad.Core
{
    public sealed class CalcpadError
    {
        public int SourceLine { get; init; }
        // 1-based position in MacroParser's expanded output — the row a code-view
        // highlighter should mark. 0 when unknown (no expansion context).
        public int OutputLine { get; init; }
        public string Message { get; init; }
        public CalcpadErrorSource Source { get; init; }
    }

    public enum CalcpadErrorSource
    {
        Macro,
        Expression
    }
}
