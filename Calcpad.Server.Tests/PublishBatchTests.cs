using System.Text;
using Calcpad.Server.Services;
using Xunit;

namespace Calcpad.Server.Tests
{
    public class PublishBatchTests
    {
        [Fact]
        public void Limits_RejectTooManyItems_AndTooMuchSource()
        {
            var tooMany = PublishBatchLimits.Evaluate(PublishBatchLimits.MaxItems + 1, 10);
            Assert.Equal(400, tooMany.StatusCode);

            var tooBig = PublishBatchLimits.Evaluate(2, PublishBatchLimits.MaxTotalChars + 1);
            Assert.Equal(413, tooBig.StatusCode);

            var ok = PublishBatchLimits.Evaluate(PublishBatchLimits.MaxItems, PublishBatchLimits.MaxTotalChars);
            Assert.Equal(200, ok.StatusCode);
        }

        [Fact]
        public void Prepare_StripsFonts_BoldsDefinedFunctions_AndInjectsCss()
        {
            const string html =
                "<html><head></head><body class=\"light\">" +
                "<script>window.__calcpadFonts={\"a\":\"data:font/ttf;base64,AAAA\"}</script>" +
                "<var>woodCouple</var>\u200A\u200A(<var>clrUnits</var>" +
                "</body></html>";

            var prepared = PublishHtml.Prepare(html, "body { color: #111; }");

            Assert.DoesNotContain("__calcpadFonts", prepared);
            Assert.Contains("<b>woodCouple</b>\u200A\u200A(", prepared);
            Assert.Contains("<var>clrUnits</var>", prepared);
            Assert.Contains("data-mode-calcpad-publish", prepared);
            Assert.Contains("body { color: #111; }", prepared);
            Assert.Contains("class=\"mode-calcpad-publish light\"", prepared);
        }

        [Fact]
        public async Task KeepsInputOrder_AndAFailedItemLeavesItsSibling()
        {
            var seen = new List<string>();
            var response = await new CalcpadService().PublishBatchAsync(
                new PublishBatchRequest
                {
                    Theme = "light",
                    ForPrint = true,
                    PublishCss = "ok",
                    Items = new List<PublishBatchItem>
                    {
                        new() { Id = "first", Content = "'MARKER-A'" },
                        new() { Id = "bad", Content = "'MARKER-BAD'" },
                        new() { Id = "third", Content = "'MARKER-C'" },
                    },
                },
                CancellationToken.None,
                (html, _, _) =>
                {
                    lock (seen)
                        seen.Add(html);
                    if (html.Contains("MARKER-BAD", StringComparison.Ordinal))
                        throw new InvalidOperationException("boom");
                    var label = html.Contains("MARKER-A", StringComparison.Ordinal) ? "A" : "C";
                    return Task.FromResult(Encoding.UTF8.GetBytes("PDF-" + label));
                });

            Assert.Equal(3, response.Results.Count);
            Assert.Equal("first", response.Results[0].Id);
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("PDF-A")), response.Results[0].PdfBase64);
            Assert.Null(response.Results[0].Error);

            Assert.Equal("bad", response.Results[1].Id);
            Assert.Null(response.Results[1].PdfBase64);
            Assert.Equal("boom", response.Results[1].Error);

            Assert.Equal("third", response.Results[2].Id);
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("PDF-C")), response.Results[2].PdfBase64);
            Assert.Null(response.Results[2].Error);

            Assert.All(seen, html =>
            {
                Assert.DoesNotContain("__calcpadFonts", html);
                Assert.Contains("data-mode-calcpad-publish", html);
                Assert.Contains("ok", html);
            });
        }

        [Fact]
        public async Task EmptyContent_IsAnItemError()
        {
            var response = await new CalcpadService().PublishBatchAsync(
                new PublishBatchRequest
                {
                    Items = new List<PublishBatchItem>
                    {
                        new() { Id = "blank", Content = "  " },
                        new() { Id = "real", Content = "a = 1" },
                    },
                },
                CancellationToken.None,
                (_, _, _) => Task.FromResult(Encoding.UTF8.GetBytes("%PDF")));

            Assert.Equal("Content is required", response.Results[0].Error);
            Assert.Null(response.Results[0].PdfBase64);
            Assert.NotNull(response.Results[1].PdfBase64);
        }
    }
}
