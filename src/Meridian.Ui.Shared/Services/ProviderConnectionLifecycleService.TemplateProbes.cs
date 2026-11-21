using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderConnectionLifecycleService
{
    internal const string TemplateProbeClientName = "provider-template-probe";

    private async Task<ProviderTemplateSetupTestResult> ProbeTemplateDataProviderAsync(string providerId, CancellationToken ct)
    {
        if (_httpClientFactory is null)
            return new(false, "The provider verification HTTP service is unavailable.");
        // This service is bound to the reviewed draft. Never use the family runtime client,
        // which may already hold credentials for another connection.
        var read = await _credentialStore.ReadForProviderAsync(providerId, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(read?.Get("ApiKey")))
            return new(false, "Enter the required API key in the credential vault before testing.");

        var key = read.Get("ApiKey")!;
        try
        {
            using var request = CreateTemplateProbeRequest(providerId, key);
            using var client = _httpClientFactory.CreateClient(TemplateProbeClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, $"The provider returned HTTP {(int)response.StatusCode}. Check credentials and data entitlement.");

            var sample = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token).ConfigureAwait(false);
            return HasTemplateProbeSample(providerId, sample)
                ? new(true, "The provider returned a sample for this connection. Use the checklist to verify coverage and data semantics.")
                : new(false, "The provider did not return the expected sample. Check that the credential can access this data.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException ||
            exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Never include vendor response bodies, request headers or credential values.
            return new(false, "The provider sample could not be retrieved. Check the credentials and try again.");
        }
    }

    private static HttpRequestMessage CreateTemplateProbeRequest(string providerId, string key)
    {
        switch (providerId)
        {
            case "tiingo":
                var start = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var history = new HttpRequestMessage(HttpMethod.Get, $"https://api.tiingo.com/tiingo/daily/aapl/prices?startDate={start}");
                history.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
                return history;
            case "polygon":
                var options = new HttpRequestMessage(HttpMethod.Get, "https://api.polygon.io/v3/snapshot/options/AAPL?limit=1");
                options.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                return options;
            default:
                var reference = new HttpRequestMessage(HttpMethod.Post, "https://api.openfigi.com/v3/mapping")
                {
                    Content = JsonContent.Create(new[] { new { idType = "TICKER", idValue = "AAPL", exchCode = "US", marketSecDes = "Equity" } })
                };
                reference.Headers.Add("X-OPENFIGI-APIKEY", key);
                return reference;
        }
    }

    private static bool HasTemplateProbeSample(string providerId, JsonElement sample)
        => providerId switch
        {
            "tiingo" => IsNonemptyArray(sample) && sample[0].ValueKind == JsonValueKind.Object && sample[0].TryGetProperty("close", out _),
            "polygon" => sample.ValueKind == JsonValueKind.Object && sample.TryGetProperty("results", out var results) && IsNonemptyArray(results),
            _ => IsNonemptyArray(sample) && sample[0].ValueKind == JsonValueKind.Object &&
                sample[0].TryGetProperty("data", out var mappings) && IsNonemptyArray(mappings)
        };

    private static bool IsNonemptyArray(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0;
}
