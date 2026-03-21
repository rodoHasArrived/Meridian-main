using System.Net.Http.Headers;

namespace Meridian.Infrastructure.Http;

/// <summary>
/// Shared HTTP helpers for provider implementations.
/// </summary>
public static class ProviderHttpUtilities
{
    /// <summary>
    /// Applies standard headers for provider HTTP clients.
    /// </summary>
    public static void ConfigureDefaultHeaders(HttpClient client, string? userAgent = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }
    }

    /// <summary>
    /// Adds an API key header if present.
    /// </summary>
    public static void AddApiKeyHeader(HttpRequestMessage request, string headerName, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        request.Headers.Remove(headerName);
        request.Headers.Add(headerName, apiKey);
    }
}
