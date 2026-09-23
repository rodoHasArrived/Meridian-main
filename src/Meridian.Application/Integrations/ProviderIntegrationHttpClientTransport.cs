using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationHttpClientTransport : IProviderIntegrationHttpTransport
{
    /// <summary>Creates the production client with connection-time DNS validation and address pinning.</summary>
    public static HttpClient CreateHttpClient(IProviderIntegrationHostResolver? resolver = null)
    {
        resolver ??= new DnsProviderIntegrationHostResolver();
        return new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (context, ct) =>
            {
                var addresses = (await resolver.ResolveAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false)).ToArray();
                if (addresses.Length == 0 || addresses.Any(IsBlockedAddress))
                    throw new InvalidOperationException("Provider connection resolved to a non-public address.");

                // Connect to the validated numeric addresses. A second DNS lookup must not
                // occur between validation and connection; TLS still uses the request host.
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        });
    }

    private const int MaximumRedirects = 3;
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient httpClient;
    private readonly ILogger<ProviderIntegrationHttpClientTransport> logger;
    private readonly IProviderIntegrationHostResolver hostResolver;

    public ProviderIntegrationHttpClientTransport(
        HttpClient httpClient,
        ILogger<ProviderIntegrationHttpClientTransport>? logger = null,
        IProviderIntegrationHostResolver? hostResolver = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.logger = logger ?? NullLogger<ProviderIntegrationHttpClientTransport>.Instance;
        this.hostResolver = hostResolver ?? new DnsProviderIntegrationHostResolver();
    }

    public async Task<ProviderIntegrationHttpResponse> SendAsync(
        ProviderIntegrationHttpRequest request,
        CancellationToken ct = default)
        => await ProviderIntegrationServiceBoundary.RunAsync(
            logger,
            "http-transport-send",
            new ProviderIntegrationBoundaryContext(
                EndpointKey: request?.Path,
                Capability: request is null ? null : request.Method.ToString()),
            () => SendCoreAsync(request, ct)).ConfigureAwait(false);

    private async Task<ProviderIntegrationHttpResponse> SendCoreAsync(
        ProviderIntegrationHttpRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        var approvedBaseUri = ParseApprovedBaseUri(request.ApprovedBaseUri);
        var requestUri = BuildRequestUri(approvedBaseUri, request);
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            await ValidateApprovedTargetAsync(approvedBaseUri, requestUri, timeoutCts.Token).ConfigureAwait(false);
            using var message = BuildMessage(request, requestUri);
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                if (redirect == MaximumRedirects)
                {
                    throw new HttpRequestException($"Provider endpoint exceeded the {MaximumRedirects}-redirect limit.");
                }

                requestUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(requestUri, response.Headers.Location);
                continue;
            }

            var body = await ReadBoundedBodyAsync(response.Content, timeoutCts.Token).ConfigureAwait(false);
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

        throw new InvalidOperationException("Provider request redirect processing terminated unexpectedly.");
    }

    private static HttpRequestMessage BuildMessage(ProviderIntegrationHttpRequest request, Uri requestUri)
    {
        var message = new HttpRequestMessage(ToHttpMethod(request.Method), requestUri);
        if (!string.IsNullOrWhiteSpace(request.BodyTemplate))
        {
            message.Content = new StringContent(request.BodyTemplate, Encoding.UTF8);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        foreach (var header in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content ??= new StringContent(string.Empty, Encoding.UTF8);
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return message;
    }

    private static Uri BuildRequestUri(Uri approvedBaseUri, ProviderIntegrationHttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || request.Path.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider endpoint path must be a non-empty URI path without backslashes.");
        }

        // On Unix a rooted path like "/v1/data" parses as an absolute file:// URI, so only
        // honor pre-parsed absolute targets when they are actually HTTP(S); everything else
        // is resolved against the approved base origin.
        var target = Uri.TryCreate(request.Path, UriKind.Absolute, out var absolute) && IsHttpScheme(absolute)
            ? absolute
            : new Uri(approvedBaseUri, request.Path);
        if (request.Query.Count == 0)
        {
            return target;
        }

        var builder = new UriBuilder(target);
        var existing = builder.Query.TrimStart('?');
        var added = string.Join(
            "&",
            request.Query.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        builder.Query = string.IsNullOrEmpty(existing) ? added : $"{existing}&{added}";
        return builder.Uri;
    }

    private static bool IsHttpScheme(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private async Task ValidateApprovedTargetAsync(Uri approvedBaseUri, Uri target, CancellationToken ct)
    {
        if (!target.IsAbsoluteUri ||
            !string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(target.UserInfo) ||
            !string.Equals(target.IdnHost, approvedBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            target.Port != approvedBaseUri.Port)
        {
            throw new InvalidOperationException(
                $"Provider target '{target}' is outside the approved HTTPS origin '{approvedBaseUri.GetLeftPart(UriPartial.Authority)}'.");
        }

        var addresses = await hostResolver.ResolveAsync(target.IdnHost, ct).ConfigureAwait(false);
        if (addresses.Count == 0 || addresses.Any(IsBlockedAddress))
        {
            throw new InvalidOperationException(
                $"Provider target '{target.IdnHost}' resolved to a private, loopback, link-local, multicast, or otherwise non-public address.");
        }
    }

    private static Uri ParseApprovedBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Provider approved base URI must be an absolute HTTPS origin without user information.");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new InvalidOperationException($"Provider response exceeds the {MaximumResponseBytes}-byte limit.");
        }

        await using var input = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaximumResponseBytes)
            {
                throw new InvalidOperationException($"Provider response exceeds the {MaximumResponseBytes}-byte limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        var charset = content.Headers.ContentType?.CharSet?.Trim('"');
        var encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        return encoding.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is 0 or 10 or 127 ||
                   (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   bytes[0] >= 224;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
               (address.GetAddressBytes()[0] & 0xfe) == 0xfc;
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

public interface IProviderIntegrationHostResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default);
}

internal sealed class DnsProviderIntegrationHostResolver : IProviderIntegrationHostResolver
{
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
        => await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
}
