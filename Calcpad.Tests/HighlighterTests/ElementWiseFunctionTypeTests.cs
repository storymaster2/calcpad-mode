using System.Linq;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Tests.HighlighterTests
{
    /// <summary>
    /// Element-wise built-in functions (sin, sqrt, floor, ...) map over vectors and
    /// matrices in Core, so passing a vector/matrix where the signature declares a
    /// scalar must NOT raise CPD-3309 ('expects scalar but got vector/matrix').
    /// </summary>
    public class ElementWiseFunctionTypeTests
    {
        private static LinterResult Lint(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var ignoreRegions = new LintIgnoreRegionParser().ExtractRegions(content);
            return new CalcpadLinter().Lint(staged, ignoreRegions);
        }

        private static int TypeWarnings(LinterResult result) =>
            result.Diagnostics.Count(d => d.Code == "CPD-3309");

        [Theory]
        [InlineData("sin")]
        [InlineData("cos")]
        [InlineData("sqrt")]
        [InlineData("ln")]
        [InlineData("exp")]
        [InlineData("floor")]
        [InlineData("abs")]
        [InlineData("sign")]
        public void ElementWiseFunction_AcceptsMatrix_NoTypeWarning(string func)
        {
            var result = Lint($"M = [1; 2; 3 | 4; 5; 6]\nr = {func}(M)\n");
            Assert.Equal(0, TypeWarnings(result));
        }

        [Fact]
        public void ElementWiseFunction_AcceptsVectorLiteral_NoTypeWarning()
        {
            var result = Lint("r = sin([1; 2; 3])\n");
            Assert.Equal(0, TypeWarnings(result));
        }

        [Fact]
        public void NonElementWiseFunction_StillWarnsOnShapeMismatch()
        {
            // n_rows expects a matrix and is NOT element-wise, so passing a vector is a
            // real mismatch that must still warn.
            var result = Lint("r = n_rows([1; 2; 3])\n");
            Assert.True(TypeWarnings(result) > 0);
        }
    }
}
