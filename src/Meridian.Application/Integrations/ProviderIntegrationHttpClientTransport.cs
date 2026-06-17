using System.Net.Http.Headers;
using System.Text;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationHttpClientTransport : IProviderIntegrationHttpTransport
{
    private readonly HttpClient httpClient;

    public ProviderIntegrationHttpClientTransport(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ProviderIntegrationHttpResponse> SendAsync(
        ProviderIntegrationHttpRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(ToHttpMethod(request.Method), BuildRequestUri(request));
        foreach (var header in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content ??= new StringContent(string.Empty, Encoding.UTF8);
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.BodyTemplate))
        {
            message.Content = new StringContent(request.BodyTemplate, Encoding.UTF8);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var response = await httpClient.SendAsync(message, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new ProviderIntegrationHttpResponse(
            (int)response.StatusCode,
            response.Headers
                .Concat(response.Content.Headers)
                .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(",", group.SelectMany(header => header.Value)),
                    StringComparer.OrdinalIgnoreCase),
            body);
    }

    private static string BuildRequestUri(ProviderIntegrationHttpRequest request)
    {
        if (request.Query.Count == 0)
        {
            return request.Path;
        }

        var separator = request.Path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var query = string.Join(
            "&",
            request.Query.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        return $"{request.Path}{separator}{query}";
    }

    private static HttpMethod ToHttpMethod(ProviderIntegrationHttpMethodDto method)
        => method switch
        {
            ProviderIntegrationHttpMethodDto.Get => HttpMethod.Get,
            ProviderIntegrationHttpMethodDto.Post => HttpMethod.Post,
            ProviderIntegrationHttpMethodDto.Put => HttpMethod.Put,
            ProviderIntegrationHttpMethodDto.Patch => HttpMethod.Patch,
            ProviderIntegrationHttpMethodDto.Delete => HttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported provider integration HTTP method.")
        };
}
