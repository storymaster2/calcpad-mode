using System.Globalization;

namespace Calcpad.Tests;

public class SignificantFigureFormatTests
{
    public SignificantFigureFormatTests()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    [Theory]
    [InlineData("N")]
    [InlineData("N3")]
    [InlineData("n3")]
    [InlineData("S")]
    [InlineData("S3")]
    [InlineData("S15")]
    public void Validator_AcceptsSignificantFigureFormats(string format) =>
        Assert.True(Validator.IsValidFormatString(format));

    [Theory]
    [InlineData("SX")]
    [InlineData("S100")]
    [InlineData("sig3")]
    [InlineData("NX")]
    public void Validator_RejectsInvalidSignificantFigureFormats(string format) =>
        Assert.False(Validator.IsValidFormatString(format));

    [Fact]
    public void Format_N3_RoundsWithThousandsSeparator()
    {
        var html = Render("#format N3\nx = 9055\n");
        Assert.Contains("9,060", html, StringComparison.Ordinal);
        Assert.DoesNotContain('E', html);
        Assert.DoesNotContain("10<sup>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_N3_SmallMagnitudeStaysFixedPoint()
    {
        var html = Render("#format N3\nx = 0.001234\n");
        Assert.Contains("0.00123", html, StringComparison.Ordinal);
        Assert.DoesNotContain("10<sup>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_N3_Pi()
    {
        var html = Render("#format N3\nx = π\n");
        Assert.Contains("3.14", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PerValue_N3()
    {
        var html = Render("x = 123456:N3\n");
        Assert.Contains("123,000", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_N3_Zero()
    {
        var html = Render("#format N3\nx = 0\n");
        Assert.DoesNotContain("Undefined", html, StringComparison.Ordinal);
        Assert.DoesNotContain("10<sup>", html, StringComparison.Ordinal);
        Assert.Matches(@"=\s*0\b", html);
    }

    [Fact]
    public void Format_N3_NegativeGrouped()
    {
        var html = Render("#format N3\nx = -9055\n");
        Assert.Contains("-9,060", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_N3_KeepsZerosWhenExtraDigitIsMeaningful()
    {
        var html = Render("#format N3\nx = 2.003\n");
        Assert.Contains("2.00", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_N3_StripsZerosWhenExtraDigitIsFuzz()
    {
        var html = Render("#format N3\nx = 2.0000000000001\n");
        Assert.DoesNotContain("2.00", html, StringComparison.Ordinal);
        Assert.Matches(@"=\s*2\b", html);
    }

    [Fact]
    public void Format_N3_RoundsNearTwoFromBelow()
    {
        var html = Render("#format N3\nx = 1.99999999999998\n");
        Assert.DoesNotContain("2.00", html, StringComparison.Ordinal);
        Assert.Matches(@"=\s*2\b", html);
    }

    [Fact]
    public void Format_S3_AliasMatchesN3()
    {
        var withN = Render("#format N3\nx = 9055\n");
        var withS = Render("#format S3\nx = 9055\n");
        Assert.Contains("9,060", withN, StringComparison.Ordinal);
        Assert.Contains("9,060", withS, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_Default_MatchesN3()
    {
        var withDefault = Render("x = 9055\n");
        var withN3 = Render("#format N3\nx = 9055\n");
        Assert.Contains("9,060", withDefault, StringComparison.Ordinal);
        Assert.Contains("9,060", withN3, StringComparison.Ordinal);
        Assert.DoesNotContain('E', withDefault);
        Assert.DoesNotContain("10<sup>", withDefault, StringComparison.Ordinal);
    }

    private static string Render(string source)
    {
        var macroParser = new MacroParser();
        var hasMacroErrors = macroParser.Parse(source, out var expandedSource, null, 0, false);
        Assert.False(hasMacroErrors);

        var parser = new ExpressionParser();
        parser.Parse(expandedSource, true, false);
        return parser.HtmlResult;
    }
}
