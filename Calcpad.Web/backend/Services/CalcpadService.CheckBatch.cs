using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Calcpad.Core;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Pre-wrap result of one calculation. The HTML template is applied only
    /// by <see cref="CalcpadService.ConvertAsync"/>.
    /// </summary>
    internal sealed class ConvertPipelineResult
    {
        public string HtmlResult { get; init; } = string.Empty;
        public bool HadMacroErrors { get; init; }
        public bool FellBack { get; init; }
        public string? FallbackMessage { get; init; }
        public int EngineErrorCount { get; init; }
    }

    public sealed class CheckBatchItem
    {
        public string Id { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? SourceFilePath { get; set; }
    }

    public sealed class CheckBatchRequest
    {
        public Settings? Settings { get; set; }
        public Dictionary<string, string>? ClientFileCache { get; set; }
        public AuthSettings? AuthSettings { get; set; }
        public int ApiTimeoutMs { get; set; } = 10000;
        public List<CheckBatchItem>? Items { get; set; }
    }

    public sealed class CheckBatchResponse
    {
        public List<CheckItemResponse> Results { get; set; } = new();
        public long ElapsedMs { get; set; }
    }

    public sealed class CheckItemResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = "none";
        public int OkCount { get; set; }
        public int FailCount { get; set; }
        public int ErrorCount { get; set; }
        public List<int> FailLines { get; set; } = new();
        public string? FirstError { get; set; }
        public long Ms { get; set; }
    }

    /// <summary>
    /// Hard caps for <c>POST /api/calcpad/check-batch</c>. The endpoint is
    /// unauthenticated, so one request must not be able to pin the process.
    /// </summary>
    public static class CheckBatchLimits
    {
        public const int MaxItems = 200;
        public const int MaxTotalChars = 2_000_000;
        public static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(20);

        public readonly record struct Decision(int StatusCode, string? Error);

        public static Decision Evaluate(int itemCount, long totalChars)
        {
            if (itemCount < 0)
                return new Decision(400, "items is required");
            if (itemCount > MaxItems)
                return new Decision(400, $"At most {MaxItems} calculations per request.");
            if (totalChars > MaxTotalChars)
                return new Decision(413, $"Combined source exceeds {MaxTotalChars} characters.");
            return new Decision(200, null);
        }
    }

    public partial class CalcpadService
    {
        /// <summary>
        /// Runs every item and returns one summary, in input order.
        /// Empty source is <c>none</c> and is not sent through the parser.
        /// A per-item timeout or exception is <c>error</c> and does not fail the batch.
        /// </summary>
        public async Task<CheckBatchResponse> CheckBatchAsync(
            CheckBatchRequest request,
            CancellationToken cancellationToken,
            TimeSpan? itemTimeout = null)
        {
            var items = request.Items ?? new List<CheckBatchItem>();
            var results = new CheckItemResponse[items.Count];
            var timeout = itemTimeout ?? CheckBatchLimits.ItemTimeout;
            var gate = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));
            var batchClock = Stopwatch.StartNew();

            var sharedCache = DecodeClientFileCache(request.ClientFileCache);
            var tasks = new Task[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                tasks[i] = CheckOneAsync(
                    index, items[index], request, sharedCache, results, gate, timeout, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return new CheckBatchResponse
            {
                Results = new List<CheckItemResponse>(results),
                ElapsedMs = batchClock.ElapsedMilliseconds,
            };
        }

        private async Task CheckOneAsync(
            int index,
            CheckBatchItem item,
            CheckBatchRequest request,
            Dictionary<string, byte[]> sharedCache,
            CheckItemResponse[] results,
            SemaphoreSlim gate,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var id = item.Id ?? string.Empty;
            var content = item.Content ?? string.Empty;
            var clock = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(content))
            {
                results[index] = new CheckItemResponse { Id = id, Status = "none", Ms = clock.ElapsedMilliseconds };
                return;
            }

            var acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
            }
            catch (OperationCanceledException)
            {
                results[index] = new CheckItemResponse
                {
                    Id = id,
                    Status = "error",
                    ErrorCount = 1,
                    FirstError = "Cancelled",
                    Ms = clock.ElapsedMilliseconds,
                };
                return;
            }

            try
            {
                using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                itemCts.CancelAfter(timeout);

                var ctx = new WebFetchContext
                {
                    ClientFileCache = new Dictionary<string, byte[]>(sharedCache, StringComparer.OrdinalIgnoreCase),
                    AuthSettings = request.AuthSettings,
                    ApiTimeoutMs = request.ApiTimeoutMs,
                    SourceFilePath = item.SourceFilePath,
                };

                var work = ConvertCoreAsync(
                    content,
                    CopySettings(request.Settings),
                    forceUnwrappedCode: false,
                    ctx,
                    forPrint: false,
                    storeExports: false);

                try
                {
                    await work.WaitAsync(itemCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    results[index] = new CheckItemResponse
                    {
                        Id = id,
                        Status = "error",
                        ErrorCount = 1,
                        FirstError = cancellationToken.IsCancellationRequested ? "Cancelled" : "Timed out",
                        Ms = clock.ElapsedMilliseconds,
                    };
                    return;
                }

                var pipeline = await work.ConfigureAwait(false);
                results[index] = Classify(id, content, pipeline, clock.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                results[index] = new CheckItemResponse
                {
                    Id = id,
                    Status = "error",
                    ErrorCount = 1,
                    FirstError = "Cancelled",
                    Ms = clock.ElapsedMilliseconds,
                };
            }
            catch (Exception ex)
            {
                results[index] = new CheckItemResponse
                {
                    Id = id,
                    Status = "error",
                    ErrorCount = 1,
                    FirstError = ex.Message,
                    Ms = clock.ElapsedMilliseconds,
                };
            }
            finally
            {
                if (acquired)
                    gate.Release();
            }
        }

        internal static CheckItemResponse Classify(string id, string content, ConvertPipelineResult pipeline, long ms)
        {
            var html = pipeline.HtmlResult ?? string.Empty;
            var okCount = CountLiteral(html, "class=\"ok\"");
            var errCount = CountLiteral(html, "class=\"err\"");
            var warningCount = CountLiteral(html, "<b class=\"err\"");
            var valueErrorCount = CountValueErrors(html);

            var engineCount = pipeline.EngineErrorCount + valueErrorCount;
            var broken = pipeline.HadMacroErrors || pipeline.FellBack || engineCount > 0;

            // Macro-error HTML is the unwrapped source, not user checks.
            // A function-redefined warning (<b class="err">) is not a check.
            var failCount = broken
                ? 0
                : Math.Max(0, errCount - engineCount - warningCount);

            string status;
            if (broken)
                status = "error";
            else if (failCount > 0)
                status = "fail";
            else if (okCount > 0)
                status = "ok";
            else
                status = "none";

            var errorCount = 0;
            if (pipeline.HadMacroErrors)
                errorCount += CountLiteral(html, "<span class=\"err\"");
            errorCount += engineCount;
            if (pipeline.FellBack && errorCount == 0)
                errorCount = 1;

            string? firstError = null;
            if (status == "error")
                firstError = pipeline.FallbackMessage ?? FirstErrorText(html);

            return new CheckItemResponse
            {
                Id = id,
                Status = status,
                OkCount = okCount,
                FailCount = failCount,
                ErrorCount = status == "error" ? Math.Max(1, errorCount) : 0,
                FailLines = status == "fail" ? SourceFailLines(content) : new List<int>(),
                FirstError = firstError,
                Ms = ms,
            };
        }

        private static int CountValueErrors(string html)
        {
            // HtmlWriter wraps NaN / infinity. These are engine output, not checks.
            return CountLiteral(html, "<span class=\"err\"> Undefined </span>")
                + CountLiteral(html, "<span class=\"err\"> ∞ </span>")
                + CountLiteral(html, "<span class=\"err\">+∞</span>")
                + CountLiteral(html, "<span class=\"err\">-∞</span>")
                + CountLiteral(html, "<span class=\"err\">∞</span>");
        }

        private static List<int> SourceFailLines(string content)
        {
            var lines = new List<int>();
            var start = 0;
            var line = 1;
            while (start <= content.Length)
            {
                var nl = content.IndexOf('\n', start);
                var end = nl < 0 ? content.Length : nl;
                var slice = content.AsSpan(start, end - start);
                if (slice.Contains("class=\"err\"".AsSpan(), StringComparison.Ordinal) ||
                    slice.Contains("class='err'".AsSpan(), StringComparison.Ordinal))
                {
                    lines.Add(line);
                    if (lines.Count == 20)
                        break;
                }
                if (nl < 0)
                    break;
                start = nl + 1;
                line++;
            }
            return lines;
        }

        private static string? FirstErrorText(string html)
        {
            var search = 0;
            while (search < html.Length)
            {
                var idx = html.IndexOf("class=\"err\"", search, StringComparison.Ordinal);
                if (idx < 0)
                    return null;
                var tagStart = html.LastIndexOf('<', idx);
                var gt = html.IndexOf('>', idx);
                if (gt < 0 || gt + 1 >= html.Length)
                    return null;
                var end = html.IndexOf("</", gt, StringComparison.Ordinal);
                if (end < 0)
                    end = Math.Min(html.Length, gt + 240);
                var raw = html.Substring(gt + 1, Math.Min(end - gt - 1, 500));
                var text = Regex.Replace(raw, "<[^>]+>", " ");
                text = WebUtility.HtmlDecode(text);
                text = Regex.Replace(text, @"\s+", " ").Trim();
                var tag = tagStart >= 0 ? html.Substring(tagStart, idx - tagStart) : "";
                // A user check span can sit ahead of the engine message. Skip it.
                if (text.Length > 0 && (tag.Contains("<p", StringComparison.OrdinalIgnoreCase) || IsEngineErrorText(text)))
                    return text.Length > 240 ? text.Substring(0, 240) : text;
                search = end + 1;
            }
            return null;
        }

        private static bool IsEngineErrorText(string text)
        {
            return text.StartsWith("Error", StringComparison.Ordinal)
                || text.Contains("Undefined", StringComparison.Ordinal)
                || text.Contains('∞');
        }

        private static int CountLiteral(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || haystack.Length < needle.Length)
                return 0;
            var count = 0;
            var i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += needle.Length;
            }
            return count;
        }

        private static Dictionary<string, byte[]> DecodeClientFileCache(Dictionary<string, string>? base64Cache)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (base64Cache == null)
                return result;
            foreach (var kvp in base64Cache)
            {
                try { result[kvp.Key] = Convert.FromBase64String(kvp.Value); }
                catch (FormatException) { /* skip malformed entries */ }
            }
            return result;
        }

        private static Settings CopySettings(Settings? source)
        {
            if (source is null)
                return new Settings();

            var math = source.Math ?? new MathSettings();
            var plot = source.Plot ?? new PlotSettings();
            return new Settings
            {
                Units = source.Units,
                EnableUi = source.EnableUi,
                UiOverrides = source.UiOverrides is null
                    ? null
                    : new Dictionary<string, string>(source.UiOverrides),
                Math = new MathSettings
                {
                    Decimals = math.Decimals,
                    Degrees = math.Degrees,
                    IsComplex = math.IsComplex,
                    Substitute = math.Substitute,
                    FormatEquations = math.FormatEquations,
                    ZeroSmallMatrixElements = math.ZeroSmallMatrixElements,
                    MaxOutputCount = math.MaxOutputCount,
                    FormatString = math.FormatString,
                },
                Plot = new PlotSettings
                {
                    IsAdaptive = plot.IsAdaptive,
                    ScreenScaleFactor = plot.ScreenScaleFactor,
                    ImagePath = plot.ImagePath,
                    ImageUri = plot.ImageUri,
                    VectorGraphics = plot.VectorGraphics,
                    ColorScale = plot.ColorScale,
                    SmoothScale = plot.SmoothScale,
                    Shadows = plot.Shadows,
                    LightDirection = plot.LightDirection,
                },
            };
        }
    }
}
