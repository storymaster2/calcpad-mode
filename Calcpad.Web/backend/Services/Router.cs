using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Calcpad.Server.Services
{
    public static class Router
    {
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

        public static async Task<byte[]> FetchUrlAsync(string url, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Calcpad/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");

            try
            {
                var response = await httpClient.GetAsync(url, cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    throw new Exception($"HTTP {response.StatusCode}: {response.ReasonPhrase} | URL: {url} | Response: {responseContent}");
                }

                return await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new Exception($"Request timeout or cancelled connecting to {url}");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error connecting to {url}: {ex.Message}");
            }
        }
    }
}
