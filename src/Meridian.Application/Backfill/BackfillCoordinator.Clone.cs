using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Backfill;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Operations;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;

namespace Meridian.Application.Backfill;

public sealed partial class BackfillCoordinator
{
    private static readonly byte[] CloneReviewKey = RandomNumberGenerator.GetBytes(32);

    public BackfillStatusStore GetReceiptStore()
    {
        var cfg = TryLoadCloneConfig();
        return new BackfillStatusStore(cfg is null ? _receiptLookupRoot : _store.GetDataRoot(cfg));
    }
    public async Task<BackfillClonePlan?> GetClonePlanAsync(Guid receiptId, string? tenantId, CancellationToken ct = default)
    {
        var cfg = TryLoadCloneConfig();
        var root = cfg is null ? _receiptLookupRoot : _store.GetDataRoot(cfg);
        var retained = new BackfillStatusStore(root).TryReadRetainedResult(receiptId);
        if (retained?.IsVisibleTo(tenantId) != true)
            return null;
        if (cfg is null)
            return MissingCloneConfiguration(retained.Receipt);

        using var service = await CreateOperationServiceAsync(cfg, tenantId, ct).ConfigureAwait(false);
        var (plan, _) = await AssessCloneAsync(retained, cfg, service, ct).ConfigureAwait(false);
        return plan;
    }

    public async Task<BackfillResult> RunCloneAsync(Guid receiptId, string confirmationCode, string? tenantId, CancellationToken ct = default) =>
        (await RunCoreAsync(null, receiptId, confirmationCode, tenantId, ct).ConfigureAwait(false)).Result;

    private AppConfig? TryLoadCloneConfig()
    {
        try
        {
            return _store.LoadRequired();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static RetainedBackfillResult FindCloneSource(BackfillStatusStore store, Guid id, string? tenantId)
    {
        var source = store.TryReadRetainedResult(id);
        return source?.IsVisibleTo(tenantId) == true ? source : throw new KeyNotFoundException("The saved backfill receipt could not be found.");
    }

    private static BackfillClonePlan MissingCloneConfiguration(ProviderProvenanceReceipt receipt) =>
        new(receipt.Request, "Unavailable", ["The active configuration could not be read. Reproduction requires the original provider configuration."]);

    private async Task<(BackfillClonePlan Plan, BackfillRequest? Request)> AssessCloneAsync(
        RetainedBackfillResult retained, AppConfig cfg, HistoricalBackfillService service, CancellationToken ct)
    {
        var receipt = retained.Receipt;
        var inputs = receipt.Request;
        (BackfillClonePlan, BackfillRequest?) Unavailable(string reason) => (new(inputs, "Unavailable", [reason]), null);

        if (receipt.SchemaVersion != ProviderProvenanceReceipt.CurrentSchemaVersion || receipt.Capability != "HistoricalBars")
            return Unavailable("This receipt version cannot be reproduced by the current backfill runtime.");
        if (receipt.ReproductionUnavailableReason is { } reason)
            return Unavailable(reason);
        if (inputs.ResumeFromCheckpoint == true || inputs.From is null || inputs.To is null)
            return Unavailable("The original checkpoint state or provider-default date range was not retained.");
        if (inputs.WithheldSymbolCount > 0 || inputs.Symbols.IsDefaultOrEmpty || inputs.Symbols.Any(s => !ProviderProvenanceLabels.IsSymbol(s)) ||
            !DataGranularityExtensions.TryParseValue(inputs.Granularity, out var granularity) || !Enum.IsDefined(granularity))
            return Unavailable("The original symbol set or granularity cannot be restored safely.");

        var composite = receipt.RoutingDecision == "Composite failover";
        var provider = service.Providers.SingleOrDefault(p => composite ? p is CompositeHistoricalDataProvider
            : ProviderProvenanceLabels.ProviderId(p.Name) == receipt.Providers.FirstOrDefault()?.ProviderId);
        if (provider is null)
            return Unavailable("The historical provider or failover policy is no longer available.");

        var request = new BackfillRequest(provider.Name, inputs.Symbols, inputs.From, inputs.To, granularity,
            inputs.MaxConcurrentSymbols, inputs.SymbolPriorities?.ToDictionary(p => p.Symbol, p => p.Priority, StringComparer.OrdinalIgnoreCase));
        try
        {
            service.ValidateRequest(request);
            if (inputs.MaxConcurrentSymbols is { } originalConcurrency &&
                BackfillQuotaPlanner.ResolveConcurrency(request, cfg.Backfill, provider) != originalConcurrency)
            {
                return Unavailable("The shared provider budget now caps concurrency below the saved request.");
            }
        }
        catch (InvalidOperationException)
        {
            return Unavailable("The current provider or operational policy cannot support the saved request.");
        }

        var route = service.DescribeRoute(request);
        var historicalProviders = receipt.Providers.Select(p => p with
        {
            Requests = 0, ResponsesWithData = 0, Unavailable = 0, EmptyResponses = 0,
            StaleResponses = 0, StaleResponsesAccepted = 0
        });
        if (route.RoutingDecision != receipt.RoutingDecision || route.Validation != receipt.ValidationProfile ||
            !route.Sources.SequenceEqual(historicalProviders))
            return Unavailable("The historical routing policy, connection, feed, or validation profile has changed and cannot be restored.");

        foreach (var source in route.Sources)
        {
            if (!IsEnabled(cfg, source.ProviderId) || !MatchesFeed(cfg, source))
                return Unavailable($"The historical configuration for {source.ProviderId} is no longer enabled or available.");
            if (!await IsConnectionAvailableAsync(cfg, source, retained.Access!, route.Access, ct).ConfigureAwait(false))
                return Unavailable($"The historical connection or entitlements for {source.ProviderId} cannot be verified.");
        }

        var reachable = await route.CheckAvailabilityAsync(ct).ConfigureAwait(false);
        if (!reachable.Contains(true))
            return Unavailable("None of the historical providers is currently reachable. Check again when the providers recover.");

        List<string> messages = [];
        for (var index = 0; index < reachable.Length; index++)
        {
            var source = receipt.Providers[index];
            if (!reachable[index])
                messages.Add($"{source.ProviderId} did not pass its availability check. The original fallback chain can still be attempted.");
            else if (source.Unavailable > 0)
                messages.Add($"{source.ProviderId} is reachable now and was unavailable during part of the original run.");
        }
        if (receipt.FallbackOccurred)
            messages.Add("The original run used fallback. The same chain will be used, and the provider that responds may differ.");

        var status = messages.Count == 0 ? "Ready" : "ReconfirmationRequired";
        if (messages.Count == 0)
            messages.Add("The saved request and provider configuration can be restored. Review them before running the clone.");

        // The caller confirms the exact availability assessment. Execute only against the
        // same route snapshot used for the recheck under the coordinator's run gate.
        var assessment = $"{receipt.ReceiptId:N}/{string.Concat(reachable.Select(available => available ? '1' : '0'))}";
        var confirmation = Convert.ToHexStringLower(HMACSHA256.HashData(CloneReviewKey, Encoding.UTF8.GetBytes(assessment)));
        return (new(inputs, status, messages, confirmation), request);
    }

    private async Task<bool> IsConnectionAvailableAsync(AppConfig cfg, ProviderProvenanceSource source,
        ProviderProvenanceAccess historicalAccess, ProviderProvenanceAccess currentAccess, CancellationToken ct)
    {
        // Public endpoints have no credential relationship. For other feeds the original
        // relationship must be verified against the active binding, not inferred from a label.
        if (source.ConnectionId is null)
            return source.Environment == "Public";
        if (_cloneCredentials is null || !historicalAccess.RuntimeRevisions.TryGetValue(source.ConnectionId, out var revision) ||
            currentAccess.RuntimeRevisions.GetValueOrDefault(source.ConnectionId) != revision)
            return false;

        try
        {
            var scope = ProviderRuntimeCredentialService.ResolveScope(cfg, source.ProviderId);
            if (scope is null || ProviderProvenanceLabels.ConnectionId(source.ProviderId, scope.TenantId, scope.ConnectionId) != source.ConnectionId ||
                ProviderProvenanceLabels.Environment(scope.Environment) != source.Environment)
                return false;

            var status = await _cloneCredentials.GetScopedStatusAsync(source.ProviderId, scope, ct).ConfigureAwait(false);
            return status.CredentialState is (ProviderCredentialStateDto.Configured or ProviderCredentialStateDto.Verified) &&
                ProviderProvenanceLabels.RuntimeRevision(status.AuditMetadata.GetValueOrDefault("credentialGeneration")) == revision;
        }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsEnabled(AppConfig cfg, string providerId)
    {
        if (cfg.ProviderModules?.Modules?.Any(pair => ProviderProvenanceLabels.ProviderId(pair.Key) == providerId && !pair.Value.Enabled) == true)
            return false;

        var providers = cfg.Backfill?.Providers;
        return providerId switch
        {
            "stooq" => providers?.Stooq?.Enabled != false,
            "yahoo" => providers?.Yahoo?.Enabled != false,
            "alpaca" => providers?.Alpaca?.Enabled != false,
            "polygon" => providers?.Polygon?.Enabled != false,
            "nasdaq" => providers?.Nasdaq?.Enabled != false,
            "tiingo" => providers?.Tiingo?.Enabled != false,
            "finnhub" => providers?.Finnhub?.Enabled != false,
            "alphavantage" => providers?.AlphaVantage?.Enabled == true,
            "fred" => providers?.Fred?.Enabled == true,
            "robinhood" => providers?.Robinhood?.Enabled == true,
            _ => true
        };
    }

    private static bool MatchesFeed(AppConfig cfg, ProviderProvenanceSource source)
    {
        if (source.ProviderId == "nasdaq")
            return source.FeedLabel == "WIKI" && (cfg.Backfill?.Providers?.Nasdaq?.Database ?? "WIKI") == "WIKI";
        if (source.ProviderId != "alpaca")
            return true;

        var provider = cfg.Backfill?.Providers?.Alpaca;
        var module = cfg.ProviderModules?.Modules?.GetValueOrDefault("alpaca");
        var feed = module?.Settings?.GetValueOrDefault("feed") ?? provider?.Feed ?? cfg.Alpaca?.Feed ?? "iex";
        var adjustment = (provider?.Adjustment ?? "all").ToLowerInvariant();
        return source.FeedLabel.Equals(feed, StringComparison.OrdinalIgnoreCase) &&
            source.DataSemantics == $"Historical bars ({adjustment} adjustment)";
    }
}

public sealed class BackfillCloneConflictException(BackfillClonePlan plan)
    : InvalidOperationException("The backfill clone requires a new review.")
{
    public BackfillClonePlan Plan { get; } = plan;
}
