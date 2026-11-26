using System.Net;
using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Ui.Shared.Services;

// Fixed read-only requests. Keep credentials in headers, and return safe messages only.
internal sealed class ProviderCredentialProbe(IHttpClientFactory? clients)
{
    internal const string ClientName = "provider-credential-probe";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private const int MaximumResponseBytes = 32 * 1024;

    internal static bool Supports(string providerId)
        => providerId is "alpaca" or "polygon" or "tiingo" or "finnhub";

    internal async Task<ProviderCredentialDraftVerificationResultDto> VerifyAsync(
        string providerId, IReadOnlyDictionary<string, string> credentials, string environment,
        string? retainedAccountId, CancellationToken ct)
    {
        var (url, header, prefix) = providerId switch
        {
            "alpaca" => (environment == AlpacaCredentialEnvironment.LiveEnvironment
                ? "https://api.alpaca.markets/v2/account"
                : "https://paper-api.alpaca.markets/v2/account", "", ""),
            "polygon" => ("https://api.polygon.io/v3/reference/tickers?ticker=AAPL&limit=1", "Authorization", "Bearer "),
            "tiingo" => ("https://api.tiingo.com/api/test/", "Authorization", "Token "),
            "finnhub" => ("https://finnhub.io/api/v1/quote?symbol=AAPL", "X-Finnhub-Token", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(providerId))
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var authorized = providerId == "alpaca"
            ? AddAlpacaHeaders(request, credentials)
            : AddCredentialHeader(request, header, credentials.GetValueOrDefault("ApiKey"), prefix);
        if (!authorized)
            return Rejected(providerId, "Enter all required credential fields before testing.");

        return await SendAsync(providerId, request, payload => providerId switch
        {
            "alpaca" => VerifyAlpacaAccount(payload, retainedAccountId),
            "polygon" when ReadString(payload, "status") is "OK" or "DELAYED" => Verified(providerId),
            "tiingo" when ReadString(payload, "message") == "You successfully sent a request" => Verified(providerId),
            "finnhub" when payload.TryGetProperty("c", out var close) && close.ValueKind == JsonValueKind.Number
                => Verified(providerId),
            _ => Unavailable(providerId)
        }, ct).ConfigureAwait(false);
    }

    internal async Task<ProviderCredentialDraftVerificationResultDto> VerifySipAsync(
        IReadOnlyDictionary<string, string> credentials, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://data.alpaca.markets/v2/stocks/AAPL/trades/latest?feed=sip");
        if (!AddAlpacaHeaders(request, credentials))
            return Rejected("alpaca", "Save an Alpaca key ID and secret key before selecting SIP.");

        return await SendAsync("alpaca", request, payload =>
            payload.TryGetProperty("trade", out var trade) && trade.ValueKind == JsonValueKind.Object &&
                trade.TryGetProperty("t", out var timestamp) && timestamp.ValueKind == JsonValueKind.String &&
                timestamp.TryGetDateTimeOffset(out _)
                    ? new("alpaca", ProviderCredentialDraftStatusDto.Verified, "Alpaca confirmed access to the SIP feed.")
                    : Unavailable("alpaca"),
            ct, sip: true).ConfigureAwait(false);
    }

    private async Task<ProviderCredentialDraftVerificationResultDto> SendAsync(
        string providerId, HttpRequestMessage request, Func<JsonElement, ProviderCredentialDraftVerificationResultDto> interpret,
        CancellationToken ct, bool sip = false)
    {
        if (clients is null)
            return Unavailable(providerId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Rejected(providerId, sip
                    ? "Alpaca refused SIP access for the selected credentials. Check the account and its data subscription."
                    : "The provider refused these credentials or the requested access.");
            if (!response.IsSuccessStatusCode)
                return Unavailable(providerId);

            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            return payload.RootElement.ValueKind == JsonValueKind.Object ? interpret(payload.RootElement) : Unavailable(providerId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unavailable(providerId);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            return Unavailable(providerId);
        }
    }

    private static ProviderCredentialDraftVerificationResultDto VerifyAlpacaAccount(JsonElement payload, string? retainedAccountId)
    {
        var accountNumber = ReadString(payload, "account_number");
        var accountId = ReadString(payload, "id");
        if (string.IsNullOrWhiteSpace(accountNumber) && string.IsNullOrWhiteSpace(accountId))
            return Unavailable("alpaca");
        return retainedAccountId is null || retainedAccountId == accountNumber || retainedAccountId == accountId
            ? Verified("alpaca")
            : Rejected("alpaca", "These credentials belong to a different Alpaca account.");
    }

    private static string? ReadString(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static bool AddAlpacaHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> credentials)
        => AddCredentialHeader(request, "APCA-API-KEY-ID", credentials.GetValueOrDefault("KeyId")) &&
            AddCredentialHeader(request, "APCA-API-SECRET-KEY", credentials.GetValueOrDefault("SecretKey"));

    private static bool AddCredentialHeader(HttpRequestMessage request, string header, string? value, string prefix = "")
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 && !value.Any(char.IsControl) &&
            request.Headers.TryAddWithoutValidation(header, prefix + value);

    private static ProviderCredentialDraftVerificationResultDto Verified(string providerId)
        => new(providerId, ProviderCredentialDraftStatusDto.Verified, "The provider accepted the draft credentials.");

    internal static ProviderCredentialDraftVerificationResultDto Rejected(string providerId, string message)
        => new(providerId, ProviderCredentialDraftStatusDto.Rejected, message);

    internal static ProviderCredentialDraftVerificationResultDto Unavailable(string providerId)
        => new(providerId, ProviderCredentialDraftStatusDto.Unavailable, "Live verification is unavailable. Retry when the provider is reachable.");
}
