using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Xunit;

namespace Calcpad.Server.Tests
{
    /// <summary>
    /// CPD-3310 (Undefined unit): underscore force-unit spellings are accepted, and a line that
    /// starts with a quote is still checked for units that follow the closing quote.
    /// </summary>
    public class LintUndefinedUnitTests
    {
        private const string UndefinedUnit = "CPD-3310";

        private static List<string> UnitErrors(string content)
        {
            var staged = new ContentResolver().GetStagedContent(content);
            var result = new CalcpadLinter().Lint(staged);
            return result.Diagnostics
                .Where(d => d.Code == UndefinedUnit)
                .Select(d => d.Message)
                .ToList();
        }

        [Theory]
        [InlineData("lb_f")]
        [InlineData("oz_f")]
        [InlineData("kip_f")]
        [InlineData("kipf")]
        [InlineData("tonf_US")]
        [InlineData("tonf_UK")]
        [InlineData("lbf")]
        public void ForceUnitSpellings_AreAccepted(string unit)
        {
            Assert.Empty(UnitErrors($"P = 200{unit}\n"));
        }

        [Fact]
        public void QuotePrefixedLine_CodeAfterClosingQuote_IsAccepted()
        {
            Assert.Empty(UnitErrors("'Applied load at guardrail,'P_rail = 200lb_f\n"));
        }

        [Fact]
        public void QuotePrefixedLine_UnknownUnitAfterClosingQuote_IsFlagged()
        {
            var errors = UnitErrors("'Applied load at guardrail,'P_rail = 200notaunit\n");
            Assert.Single(errors);
            Assert.Contains("notaunit", errors[0]);
        }

        [Fact]
        public void TitleLine_CodeAfterClosingQuote_IsChecked()
        {
            var errors = UnitErrors("\"Title\" P = 5badunit\n");
            Assert.Single(errors);
            Assert.Contains("badunit", errors[0]);
        }

        [Fact]
        public void UnitLikeWordsInsideComments_AreNotFlagged()
        {
            Assert.Empty(UnitErrors("'Span is 12 notaunit and 5 lb_f of load, see 3 qwertyunit'\n"));
            Assert.Empty(UnitErrors("\"Load 5 notaunit\"\n"));
        }

        [Fact]
        public void UnknownUnitOnPlainLine_IsStillFlagged()
        {
            Assert.Single(UnitErrors("P = 5badunit\n"));
        }
    }
}
