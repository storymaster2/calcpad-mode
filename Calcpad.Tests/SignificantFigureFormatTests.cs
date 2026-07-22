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
    [InlineData("S")]
    [InlineData("S3")]
    [InlineData("s3")]
    [InlineData("S15")]
    public void Validator_AcceptsSignificantFigureFormats(string format) =>
        Assert.True(Validator.IsValidFormatString(format));

    [Theory]
    [InlineData("SX")]
    [InlineData("S100")]
    [InlineData("sig3")]
    public void Validator_RejectsInvalidSignificantFigureFormats(string format) =>
        Assert.False(Validator.IsValidFormatString(format));

    [Fact]
    public void Format_S3_RoundsWithThousandsSeparator()
    {
        var html = Render("#format S3\nx = 9055\n");
        Assert.Contains("9,060", html, StringComparison.Ordinal);
        Assert.DoesNotContain('E', html);
        Assert.DoesNotContain("10<sup>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_S3_SmallMagnitudeStaysFixedPoint()
    {
        var html = Render("#format S3\nx = 0.001234\n");
        Assert.Contains("0.00123", html, StringComparison.Ordinal);
        Assert.DoesNotContain("10<sup>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_S3_Pi()
    {
        var html = Render("#format S3\nx = π\n");
        Assert.Contains("3.14", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PerValue_S3()
    {
        var html = Render("x = 123456:S3\n");
        Assert.Contains("123,000", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_S3_Zero()
    {
        var html = Render("#format S3\nx = 0\n");
        Assert.DoesNotContain("Undefined", html, StringComparison.Ordinal);
        Assert.DoesNotContain("10<sup>", html, StringComparison.Ordinal);
        Assert.Matches(@"=\s*0\b", html);
    }

    [Fact]
    public void Format_S3_NegativeGrouped()
    {
        var html = Render("#format S3\nx = -9055\n");
        Assert.Contains("-9,060", html, StringComparison.Ordinal);
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
