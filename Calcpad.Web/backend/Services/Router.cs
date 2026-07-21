using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Calcpad.Server.Services
{
    public class RoutingConfig : Dictionary<string, ServiceConfig>
    {
        public RoutingConfig() : base() { }
    }

    public class ServiceConfig
    {
        public string? BaseUrl { get; set; }
        public string? Auth { get; set; }
        public Dictionary<string, string> Endpoints { get; set; } = new();
    }

    public class AuthSettings
    {
        public string? JWT { get; set; }
        public RoutingConfig? RoutingConfig { get; set; }
    }

    public class Router(RoutingConfig? routingConfig = null, AuthSettings? authSettings = null)
    {
        private readonly RoutingConfig? _routingConfig = routingConfig;
        private readonly AuthSettings? _authSettings = authSettings;

        public static RoutingConfig ParseRoutingConfig(string jsonString)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            return JsonSerializer.Deserialize<RoutingConfig>(jsonString, options) ?? new RoutingConfig();
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

        public static async Task<byte[]> FetchUrlAsync(string url, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            return await FetchUrlAsync(url, cts.Token);
        }

        public static async Task<byte[]> FetchUrlAsync(string url, CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Calcpad/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");

            try
            {
                var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new Exception($"HTTP {response.StatusCode}: {response.ReasonPhrase} | URL: {url} | Response: {responseContent}");
                }

                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new Exception($"Request timeout or cancelled connecting to {url}");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error connecting to {url}: {ex.Message}");
            }
        }

        public async Task<byte[]> FetchFileBytesAsync(string serviceName, string endpointName, string body, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            return await FetchFileBytesAsync(serviceName, endpointName, body, cts.Token);
        }

        public async Task<byte[]> FetchFileBytesAsync(string serviceName, string endpointName, string body, CancellationToken cancellationToken)
        {
            if (_routingConfig == null)
                throw new InvalidOperationException($"API Router configuration is required but not provided. Cannot use API syntax '<{serviceName}:{endpointName}>'.");

            if (!_routingConfig.TryGetValue(serviceName, out var serviceConfig))
                throw new ArgumentException($"Service '{serviceName}' not found in routing config");

            if (!serviceConfig.Endpoints.TryGetValue(endpointName, out var endpointTemplate))
                throw new ArgumentException($"Endpoint '{endpointName}' not found for service '{serviceName}'");

            using var httpClient = new HttpClient();

            var requestUrl = serviceConfig.BaseUrl + endpointTemplate;

            bool isJsonBody = !string.IsNullOrEmpty(body) &&
                             (body.TrimStart().StartsWith("{") || body.TrimStart().StartsWith("["));

            HttpRequestMessage request;

            if (isJsonBody)
            {
                request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            else
            {
                request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            }

            if (serviceConfig.Auth == "jwt")
            {
                string jwtToken = _authSettings?.JWT ?? string.Empty;
                if (string.IsNullOrEmpty(jwtToken))
                    throw new InvalidOperationException("JWT token is required for authenticated API calls but not configured.");

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
            }

            request.Headers.Add("User-Agent", "Calcpad-Server/1.0");
            request.Headers.Add("Accept", "*/*");

            try
            {
                var response = await httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var authHeader = request.Headers.Authorization?.ToString() ?? "None";
                    var method = isJsonBody ? "POST" : "GET";
                    throw new Exception($"HTTP {response.StatusCode}: {response.ReasonPhrase} | Method: {method} | URL: {requestUrl} | Auth: {authHeader} | Response: {responseContent}");
                }

                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new Exception($"Request timeout or cancelled connecting to {requestUrl}");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error connecting to {requestUrl}: {ex.Message}");
            }
        }

        public async Task<string> FetchFileAsync(string serviceName, string endpointName, string body, int timeoutMs)
        {
            var bytes = await FetchFileBytesAsync(serviceName, endpointName, body, timeoutMs);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
