using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingHttpRelayClientOptions(
    Uri Endpoint,
    string BearerCredential,
    TimeSpan Timeout);

/// <summary>
/// Concrete outbound relay adapter. It intentionally does not log request or response bodies
/// because the request contains the one-time fragment bearer delivered to the recipient.
/// </summary>
public sealed class ConfiguredReportingHttpRelayClient : IReportingHttpRelayClient
{
    internal const string ClientName = "meridian-reporting-http-relay";
    private const int MaximumResponseBytes = 65_536;
    private static readonly JsonSerializerOptions RelayJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReportingHttpRelayClientOptions _options;

    public ConfiguredReportingHttpRelayClient(
        IHttpClientFactory httpClientFactory,
        ReportingHttpRelayClientOptions options)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = ValidateOptions(options);
    }

    public async Task<ReportingHttpRelayResult> SendAsync(
        ReportingHttpRelayMessage message,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(message, RelayJsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerCredential);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", message.IdempotencyKey);
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        requestTimeout.CancelAfter(_options.Timeout);
        try
        {
            using var response = await _httpClientFactory
                .CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                return new ReportingHttpRelayResult(
                    IsSuccess: false,
                    IsTransientFailure: IsTransient(response.StatusCode),
                    Code: $"HTTP_{status}",
                    Detail: $"Configured reporting relay returned HTTP {status}.");
            }

            RelayAcceptance? acceptance;
            try
            {
                var bytes = await ReadBoundedAsync(response.Content, requestTimeout.Token).ConfigureAwait(false);
                acceptance = JsonSerializer.Deserialize<RelayAcceptance>(bytes, RelayJsonOptions);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                return new ReportingHttpRelayResult(
                    IsSuccess: false,
                    IsTransientFailure: true,
                    Code: "RELAY_OUTCOME_UNKNOWN",
                    Detail: "Configured reporting relay returned an invalid acceptance receipt; its acceptance outcome is unknown.");
            }

            var providerMessageId = Normalize(
                acceptance?.ProviderMessageId,
                ReportingDistributionValueLimits.ProviderMessageIdLength);
            if (providerMessageId is null)
            {
                return new ReportingHttpRelayResult(
                    IsSuccess: false,
                    IsTransientFailure: true,
                    Code: "PROVIDER_MESSAGE_ID_INVALID",
                    Detail: "Configured reporting relay returned a missing or invalid durable provider message id; its acceptance outcome is unknown.");
            }

            return new ReportingHttpRelayResult(
                IsSuccess: true,
                IsTransientFailure: false,
                Code: Normalize(acceptance?.Code, 128) ?? "ACCEPTED",
                ProviderMessageId: providerMessageId);
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Configured reporting relay timed out before returning an acceptance receipt.",
                exception);
        }
    }

    private static ReportingHttpRelayClientOptions ValidateOptions(ReportingHttpRelayClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Endpoint.IsAbsoluteUri
            || options.Endpoint.Scheme != Uri.UriSchemeHttps && !(options.Endpoint.Scheme == Uri.UriSchemeHttp && options.Endpoint.IsLoopback)
            || !string.IsNullOrEmpty(options.Endpoint.Query)
            || !string.IsNullOrEmpty(options.Endpoint.Fragment)
            || !string.IsNullOrEmpty(options.Endpoint.UserInfo))
        {
            throw new ArgumentException(
                "Reporting relay endpoint must use HTTPS outside loopback and cannot contain credentials, a query, or a fragment.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.BearerCredential) || options.BearerCredential.Trim().Length < 32)
        {
            throw new ArgumentException("Reporting relay bearer credentials must contain at least 32 characters.", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Reporting relay timeout must be positive and no more than two minutes.");
        }

        return options with { BearerCredential = options.BearerCredential.Trim() };
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException("Reporting relay receipt exceeded the maximum response size.");
        }

        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var target = new MemoryStream();
        var buffer = new byte[8_192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (target.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException("Reporting relay receipt exceeded the maximum response size.");
            }

            target.Write(buffer, 0, read);
        }

        return target.ToArray();
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
        || (int)status is >= 500 and <= 599;

    private static string? Normalize(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > maximumLength
            || normalized.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }

    private sealed record RelayAcceptance(string? Code, string? ProviderMessageId);
}
