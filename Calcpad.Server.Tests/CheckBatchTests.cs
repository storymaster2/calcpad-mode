using Calcpad.Core;
using Calcpad.Server.Services;
using Xunit;

namespace Calcpad.Server.Tests
{
    public class CheckBatchTests
    {
        private static CalcpadService NewService() => new();

        private static async Task<CheckItemResponse> One(string content)
        {
            var response = await NewService().CheckBatchAsync(new CheckBatchRequest
            {
                Items = new List<CheckBatchItem>
                {
                    new() { Id = "only", Content = content },
                },
            }, CancellationToken.None);
            Assert.Single(response.Results);
            return response.Results[0];
        }

        [Fact]
        public async Task OkOnly_IsOk()
        {
            var item = await One("'<span class=\"ok\">OK</span>'\n");
            Assert.Equal("ok", item.Status);
            Assert.True(item.OkCount >= 1);
            Assert.Equal(0, item.FailCount);
            Assert.Equal(0, item.ErrorCount);
        }

        [Fact]
        public async Task FailOnly_IsFail_AndReportsSourceLine()
        {
            var item = await One("a = 1\n'<span class=\"err\">NG</span>'\n");
            Assert.Equal("fail", item.Status);
            Assert.True(item.FailCount >= 1);
            Assert.Equal(0, item.ErrorCount);
            Assert.Contains(2, item.FailLines);
        }

        [Fact]
        public async Task Mixed_IsFail_AndKeepsBothCounts()
        {
            var content = "'<span class=\"ok\">Shear</span>'\n'<span class=\"err\">Moment</span>'\n";
            var item = await One(content);
            Assert.Equal("fail", item.Status);
            Assert.True(item.OkCount >= 1);
            Assert.True(item.FailCount >= 1);
        }

        [Fact]
        public async Task UserCheckPlusUndefined_ReportsTheEngineMessage()
        {
            var item = await One("'<span class=\"err\">NG</span>'\nb = unknownVar\n");
            Assert.Equal("error", item.Status);
            Assert.Equal(0, item.FailCount);
            Assert.Contains("unknownVar", item.FirstError);
            Assert.DoesNotContain("NG", item.FirstError);
        }

        [Fact]
        public async Task UndefinedVariable_IsError_NotFail()
        {
            var item = await One("b = unknownVar\n");
            Assert.Equal("error", item.Status);
            Assert.Equal(0, item.FailCount);
            Assert.True(item.ErrorCount >= 1);
            Assert.False(string.IsNullOrWhiteSpace(item.FirstError));
        }

        [Fact]
        public async Task UnclosedIf_IsError()
        {
            var item = await One("#if 1\na = 1\n");
            Assert.Equal("error", item.Status);
            Assert.Equal(0, item.FailCount);
            Assert.True(item.ErrorCount >= 1);
        }

        [Fact]
        public async Task UnclosedMacro_IsError()
        {
            var item = await One("#def foo$\n");
            Assert.Equal("error", item.Status);
            Assert.Equal(0, item.FailCount);
            Assert.True(item.ErrorCount >= 1);
        }

        [Fact]
        public async Task Empty_IsNone()
        {
            var blank = await One("");
            var spaces = await One("   \n");
            Assert.Equal("none", blank.Status);
            Assert.Equal("none", spaces.Status);
            Assert.Equal(0, blank.OkCount);
            Assert.Equal(0, blank.FailCount);
            Assert.Equal(0, blank.ErrorCount);
        }

        [Fact]
        public async Task PlainArithmetic_IsNone()
        {
            var item = await One("a = 2 + 3\n");
            Assert.Equal("none", item.Status);
            Assert.Equal(0, item.ErrorCount);
            Assert.Equal(0, item.FailCount);
        }

        [Fact]
        public async Task ResultsStayInInputOrder()
        {
            var response = await NewService().CheckBatchAsync(new CheckBatchRequest
            {
                Items = new List<CheckBatchItem>
                {
                    new() { Id = "c", Content = "a = 1\n" },
                    new() { Id = "a", Content = "'<span class=\"err\">NG</span>'\n" },
                    new() { Id = "b", Content = "'<span class=\"ok\">OK</span>'\n" },
                },
            }, CancellationToken.None);

            Assert.Equal(new[] { "c", "a", "b" }, response.Results.Select(r => r.Id).ToArray());
            Assert.Equal("none", response.Results[0].Status);
            Assert.Equal("fail", response.Results[1].Status);
            Assert.Equal("ok", response.Results[2].Status);
        }

        [Fact]
        public async Task ParallelConvertsDoNotCrossMacros()
        {
            var service = NewService();
            var tasks = new List<Task<string>>();
            for (var i = 0; i < 12; i++)
            {
                var word = i % 2 == 0 ? "ALPHA" : "BETA";
                tasks.Add(service.ConvertAsync($"#def marker$ = '{word}'\nmarker$\n"));
            }

            var htmls = await Task.WhenAll(tasks);
            for (var i = 0; i < htmls.Length; i++)
            {
                var word = i % 2 == 0 ? "ALPHA" : "BETA";
                var other = word == "ALPHA" ? "BETA" : "ALPHA";
                Assert.Contains(word, htmls[i]);
                Assert.DoesNotContain(other, htmls[i]);
            }
        }

        [Fact]
        public void TooManyItems_Is400()
        {
            var decision = CheckBatchLimits.Evaluate(CheckBatchLimits.MaxItems + 1, 10);
            Assert.Equal(400, decision.StatusCode);
        }

        [Fact]
        public void TooMuchSource_Is413()
        {
            var decision = CheckBatchLimits.Evaluate(2, CheckBatchLimits.MaxTotalChars + 1);
            Assert.Equal(413, decision.StatusCode);
        }

        [Fact]
        public void WithinCaps_Is200()
        {
            var decision = CheckBatchLimits.Evaluate(CheckBatchLimits.MaxItems, CheckBatchLimits.MaxTotalChars);
            Assert.Equal(200, decision.StatusCode);
            Assert.Null(decision.Error);
        }

        [Fact]
        public async Task CancelledToken_IsErrorPerItem()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var response = await NewService().CheckBatchAsync(new CheckBatchRequest
            {
                Items = new List<CheckBatchItem>
                {
                    new() { Id = "x", Content = "a = 1\n" },
                },
            }, cts.Token);

            Assert.Equal("error", response.Results[0].Status);
            Assert.Equal("Cancelled", response.Results[0].FirstError);
        }

        [Fact]
        public void EngineErrorCount_CountsRuntimeError_NotUserSpan()
        {
            var broken = new ExpressionParser();
            broken.Parse("b = unknownVar");
            Assert.True(broken.EngineErrorCount >= 1);

            var check = new ExpressionParser();
            check.Parse("'<span class=\"err\">NG</span>'");
            Assert.Equal(0, check.EngineErrorCount);
            Assert.Contains("class=\"err\"", check.HtmlResult);
        }
    }
}
