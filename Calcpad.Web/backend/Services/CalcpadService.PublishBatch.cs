using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calcpad.Core;
using Calcpad.Server.Models.Pdf;

namespace Calcpad.Server.Services
{
    public sealed class PublishBatchItem
    {
        public string Id { get; set; } = string.Empty;
        public string? Content { get; set; }
    }

    public sealed class PublishBatchRequest
    {
        public Settings? Settings { get; set; }
        public string? Theme { get; set; }
        public bool ForPrint { get; set; } = true;
        public string? PublishCss { get; set; }
        public PdfOptions? Options { get; set; }
        public Dictionary<string, string>? ClientFileCache { get; set; }
        public AuthSettings? AuthSettings { get; set; }
        public int ApiTimeoutMs { get; set; } = 10000;
        public List<PublishBatchItem>? Items { get; set; }
    }

    public sealed class PublishBatchResponse
    {
        public List<PublishItemResponse> Results { get; set; } = new();
    }

    public sealed class PublishItemResponse
    {
        public string Id { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PdfBase64 { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }
    }

    /// <summary>
    /// Hard caps for <c>POST /api/calcpad/publish-batch</c>. Each result
    /// carries a PDF, so the item cap stays under Cloud Run's response limit.
    /// </summary>
    public static class PublishBatchLimits
    {
        public const int MaxItems = 20;
        public const int MaxTotalChars = 2_000_000;
        public const int MaxConcurrency = 2;
        public static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(90);

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

    /// <summary>
    /// The print HTML the page applies between convert and PDF. The page
    /// owns the stylesheet text and sends it as <c>publishCss</c>.
    /// </summary>
    public static class PublishHtml
    {
        private static readonly Regex FontScript = new(
            @"<script\b[^>]*>\s*window\.__calcpadFonts=[\s\S]*?</script>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DefinedFunction = new(
            "<var>([^<]*)</var>(<sub>[^<]*</sub>)?(\u200A*)\\(",
            RegexOptions.Compiled);

        private static readonly Regex HeadOpen = new("<head[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HtmlOpen = new("<html[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BodyOpen = new("<body", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BodyClass = new(
            "<body([^>]*?)\\bclass=\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Prepare(string html, string? css)
        {
            var stripped = FontScript.Replace(html ?? string.Empty, string.Empty);
            var bolded = DefinedFunction.Replace(stripped, "<b>$1</b>$2$3(");
            return Inject(bolded, css ?? string.Empty);
        }

        private static string Inject(string html, string css)
        {
            var style = "<style data-mode-calcpad-publish>\n" + css + "\n</style>";
            var next = BodyOpen.IsMatch(html) ? html : "<body>" + html + "</body>";
            if (HeadOpen.IsMatch(next))
                next = ReplaceFirst(next, HeadOpen, match => match.Value + "\n" + style);
            else if (HtmlOpen.IsMatch(next))
                next = ReplaceFirst(next, HtmlOpen, match => match.Value + "<head>" + style + "</head>");
            else
                next = "<!DOCTYPE html><html><head>" + style + "</head>" + next + "</html>";

            if (BodyClass.IsMatch(next))
                return ReplaceFirst(next, BodyClass, match => "<body" + match.Groups[1].Value + "class=\"mode-calcpad-publish ");
            return ReplaceFirst(next, BodyOpen, _ => "<body class=\"mode-calcpad-publish\"");
        }

        private static string ReplaceFirst(string input, Regex pattern, MatchEvaluator replacement)
        {
            var done = false;
            return pattern.Replace(input, match =>
            {
                if (done)
                    return match.Value;
                done = true;
                return replacement(match);
            });
        }
    }

    public partial class CalcpadService
    {
        /// <summary>
        /// Converts and renders every item. Results stay in input order.
        /// A failed item records <c>error</c> and does not cancel the others.
        /// <paramref name="renderPdf"/> is the PDF service. Tests pass a stub.
        /// </summary>
        public async Task<PublishBatchResponse> PublishBatchAsync(
            PublishBatchRequest request,
            CancellationToken cancellationToken,
            Func<string, PdfOptions?, CancellationToken, Task<byte[]>> renderPdf,
            TimeSpan? itemTimeout = null)
        {
            if (renderPdf is null)
                throw new ArgumentNullException(nameof(renderPdf));

            var items = request.Items ?? new List<PublishBatchItem>();
            var results = new PublishItemResponse[items.Count];
            var timeout = itemTimeout ?? PublishBatchLimits.ItemTimeout;
            var gate = new SemaphoreSlim(PublishBatchLimits.MaxConcurrency);
            var sharedCache = DecodeClientFileCache(request.ClientFileCache);
            var tasks = new Task[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                tasks[i] = PublishOneAsync(
                    index, items[index], request, sharedCache, results, gate, timeout, renderPdf, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return new PublishBatchResponse
            {
                Results = new List<PublishItemResponse>(results),
            };
        }

        private async Task PublishOneAsync(
            int index,
            PublishBatchItem item,
            PublishBatchRequest request,
            Dictionary<string, byte[]> sharedCache,
            PublishItemResponse[] results,
            SemaphoreSlim gate,
            TimeSpan timeout,
            Func<string, PdfOptions?, CancellationToken, Task<byte[]>> renderPdf,
            CancellationToken cancellationToken)
        {
            var id = item.Id ?? string.Empty;
            var content = item.Content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                results[index] = new PublishItemResponse { Id = id, Error = "Content is required" };
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
                results[index] = new PublishItemResponse { Id = id, Error = "Cancelled" };
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
                };
                var theme = string.IsNullOrWhiteSpace(request.Theme) ? "light" : request.Theme;
                var convert = ConvertCoreAsync(
                    content,
                    CopySettings(request.Settings),
                    forceUnwrappedCode: false,
                    ctx,
                    request.ForPrint,
                    storeExports: false);

                ConvertPipelineResult pipeline;
                try
                {
                    pipeline = await convert.WaitAsync(itemCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    results[index] = new PublishItemResponse
                    {
                        Id = id,
                        Error = cancellationToken.IsCancellationRequested ? "Cancelled" : "Timed out",
                    };
                    return;
                }

                var html = PublishHtml.Prepare(WrapHtmlResult(pipeline.HtmlResult, theme), request.PublishCss);
                byte[] pdf;
                try
                {
                    pdf = await renderPdf(html, request.Options, itemCts.Token).WaitAsync(itemCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    results[index] = new PublishItemResponse
                    {
                        Id = id,
                        Error = cancellationToken.IsCancellationRequested ? "Cancelled" : "Timed out",
                    };
                    return;
                }

                if (pdf is null || pdf.Length == 0)
                {
                    results[index] = new PublishItemResponse { Id = id, Error = "Calcpad did not return a PDF." };
                    return;
                }

                results[index] = new PublishItemResponse
                {
                    Id = id,
                    PdfBase64 = Convert.ToBase64String(pdf),
                };
            }
            catch (Exception ex)
            {
                results[index] = new PublishItemResponse { Id = id, Error = PublishError(ex) };
            }
            finally
            {
                if (acquired)
                    gate.Release();
            }
        }

        private static string PublishError(Exception ex)
        {
            var message = ex.GetBaseException().Message?.Trim();
            if (string.IsNullOrEmpty(message))
                message = "Calcpad did not return a PDF.";
            return message.Length > 400 ? message[..400] + "…" : message;
        }
    }
}
