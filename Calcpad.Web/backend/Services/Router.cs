using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Calcpad.Server.Services
{
    public static class Router
    {
        // Shared HttpClient for the process lifetime; per-request timeout comes
        // from the CancellationTokenSource passed to GetAsync.
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Calcpad/1.0");
            client.DefaultRequestHeaders.Add("Accept", "*/*");
            return client;
        }

        public static bool IsDirectUrl(string input)
        {
            return input?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ||
                   input?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;
        }

        public static bool IsDirectUrl(ReadOnlySpan<char> input)
        {
            return input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<byte[]> FetchUrlAsync(string url, int timeoutMs, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            try
            {
                var response = await _httpClient.GetAsync(url, linkedCts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                    throw new Exception($"HTTP {response.StatusCode}: {response.ReasonPhrase} | URL: {url} | Response: {responseContent}");
                }

                return await response.Content.ReadAsByteArrayAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new Exception($"Request timeout connecting to {url}");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error connecting to {url}: {ex.Message}");
            }
        }
    }
}
