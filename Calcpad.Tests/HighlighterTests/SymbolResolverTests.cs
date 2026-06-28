using Calcpad.Highlighter.ContentResolution;

namespace Calcpad.Tests.HighlighterTests
{
    public class SymbolResolverTests
    {
        private static SymbolHit Resolve(string content, int line, int column)
        {
            var resolver = new ContentResolver();
            var staged = resolver.GetStagedContent(content);
            return SymbolResolver.ResolveSymbolAt(staged.Stage3, line, column);
        }

        [Fact]
        public void Resolves_Variable_AtAssignment()
        {
            var src = "x = 5\ny = x + 1\n";
            var hit = Resolve(src, line: 0, column: 0);
            Assert.NotNull(hit);
            Assert.Equal("x", hit.Name);
            Assert.Equal(SymbolKind.Variable, hit.Kind);
            Assert.Equal(2, hit.Locations.Count); // definition + usage
        }

        [Fact]
        public void Resolves_Variable_AtUsage()
        {
            var src = "x = 5\ny = x + 1\n";
            // 'x' usage is on line 1, column 4
            var hit = Resolve(src, line: 1, column: 4);
            Assert.NotNull(hit);
            Assert.Equal("x", hit.Name);
            Assert.Equal(SymbolKind.Variable, hit.Kind);
        }

        [Fact]
        public void Resolves_Function_AtDefinition()
        {
            var src = "f(a) = a + 1\ny = f(2)\n";
            var hit = Resolve(src, line: 0, column: 0);
            Assert.NotNull(hit);
            Assert.Equal("f", hit.Name);
            Assert.Equal(SymbolKind.Function, hit.Kind);
        }

        [Fact]
        public void Resolves_Function_AtCallSite()
        {
            var src = "f(a) = a + 1\ny = f(2)\n";
            // 'f' call is on line 1 column 4
            var hit = Resolve(src, line: 1, column: 4);
            Assert.NotNull(hit);
            Assert.Equal("f", hit.Name);
            Assert.Equal(SymbolKind.Function, hit.Kind);
        }

        [Fact]
        public void Resolves_InlineMacro_AtCallSite()
        {
            var src = "#def greet$ = 'hi\ngreet$\n";
            // 'greet$' is on line 1, columns 0..5 inclusive
            var hit = Resolve(src, line: 1, column: 0);
            Assert.NotNull(hit);
            Assert.Equal("greet$", hit.Name);
            Assert.Equal(SymbolKind.Macro, hit.Kind);
        }

        [Fact]
        public void Resolves_MultilineMacro_CursorJustPastDollar()
        {
            // Regression: cursor placed between the `$` and `(` of a macro call
            // (column == start + length) used to fail the client-side overlap
            // check, causing F12 to fall through to find-references.
            var src =
                "#def accumulate$(result$; v$)\n" +
                "\tresult$ = 0\n" +
                "\t#for k_acc = 1 : len(v$)\n" +
                "\t\tresult$ = result$ + v$.k_acc\n" +
                "\t#loop\n" +
                "#end def\n" +
                "accumulate$(runningTotal; 2)\n";

            // `accumulate$` on line 6 starts at column 0 and is 11 chars long.
            // Column 11 sits on the `(` — must still resolve to the macro.
            var hit = Resolve(src, line: 6, column: 11);
            Assert.NotNull(hit);
            Assert.Equal("accumulate$", hit.Name);
            Assert.Equal(SymbolKind.Macro, hit.Kind);
        }

        [Fact]
        public void Returns_Null_WhenCursorOutsideAnyToken()
        {
            var src = "x = 5\n";
            // Column 10 is past the end of the line
            Assert.Null(Resolve(src, line: 0, column: 10));
        }

        [Fact]
        public void Returns_Null_OnBlankLine()
        {
            var src = "x = 5\n\ny = 7\n";
            Assert.Null(Resolve(src, line: 1, column: 0));
        }

        [Fact]
        public void DefinitionAndUsage_BothMarkedCorrectly()
        {
            var src = "x = 5\ny = x + 1\n";
            var hit = Resolve(src, line: 0, column: 0);
            Assert.NotNull(hit);
            Assert.Contains(hit.Locations, l => l.IsAssignment && l.Line == 0);
            Assert.Contains(hit.Locations, l => !l.IsAssignment && l.Line == 1);
        }
    }
}
