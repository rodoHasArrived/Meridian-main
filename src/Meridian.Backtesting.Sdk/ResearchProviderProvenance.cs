using System.Collections.Immutable;
using Meridian.Contracts.Operations;

namespace Meridian.Backtesting.Sdk;

/// <summary>Projects research inputs without copying a data path, a script, or account configuration.</summary>
public static class ResearchProviderProvenance
{
    private const string ReplayCapability = "HistoricalMarketDataReplay";
    public const string MissingRevision = "The exact dataset and strategy revision were not retained for reproduction.";

    public static ProviderProvenanceReceipt Create(BacktestRequest request, DateTimeOffset startedUtc,
        DateTimeOffset completedUtc, IReadOnlyDictionary<string, long>? sourceCounts = null,
        bool? preflightEnabled = null, bool? corporateActionsApplied = null)
    {
        var symbols = request.Symbols ?? [];
        var safeSymbols = symbols.Where(ProviderProvenanceLabels.IsSymbol).ToImmutableArray();
        var sources = (sourceCounts ?? ImmutableDictionary<string, long>.Empty)
            .Where(pair => pair.Value > 0)
            .GroupBy(pair => ProviderProvenanceLabels.PublicProviderId(pair.Key) ?? "unrecorded", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ProviderProvenanceSource(group.Key, null, "Local replay", "Retained market events",
                "Entitlement not retained", "Replay input", ReplayEvents: group.Max(pair => pair.Value)))
            .ToImmutableArray();
        if (sources.IsEmpty)
            sources = [new("unrecorded", null, "Unknown", "Source not retained", "Entitlement not retained", "Unattributed research input")];

        return new ProviderProvenanceReceipt(
            ProviderProvenanceReceipt.CurrentSchemaVersion, Guid.NewGuid(), ReplayCapability,
            preflightEnabled.HasValue ? "Local replay" : "Historical routing not recorded",
            sources, [], false,
            new(preflightEnabled.HasValue ? "research-replay/v1" : "research-input-unreported/v1",
                PreflightEnabled: preflightEnabled, CorporateActionsApplied: corporateActionsApplied,
                FailOnUnknownSymbols: request.FailOnUnknownSymbols),
            new(request.From, request.To, safeSymbols, InitialCash: request.InitialCash,
                AdjustForCorporateActions: request.AdjustForCorporateActions, FailOnUnknownSymbols: request.FailOnUnknownSymbols,
                WithheldSymbolCount: symbols.Count - safeSymbols.Length, UsesDiscoveredUniverse: symbols.Count == 0),
            startedUtc, completedUtc, "Simulated research results", MissingRevision,
            RoutingHistoryComplete: preflightEnabled.HasValue);
    }

    /// <summary>Rebuilds a worker-produced receipt at the host boundary using only recognized IDs and typed values.</summary>
    public static ProviderProvenanceReceipt FromWorkerResult(BacktestResult result, DateTimeOffset completedUtc)
    {
        var input = result.ProviderProvenance;
        var start = StartFromElapsed(completedUtc, result.ElapsedTime);
        if (input is null || input.SchemaVersion != ProviderProvenanceReceipt.CurrentSchemaVersion ||
            input.Capability != ReplayCapability || input.Providers.IsDefault || input.ValidationProfile is null)
            return Create(result.Request, start, completedUtc);

        var sources = input.Providers.Where(p => p is not null && p.ReplayEvents > 0)
            .GroupBy(p => ProviderProvenanceLabels.PublicProviderId(p.ProviderId) ?? "unrecorded", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(p => p.ReplayEvents), StringComparer.Ordinal);
        return Create(result.Request, input.StartedUtc, input.CompletedUtc, sources,
            input.ValidationProfile.PreflightEnabled, input.ValidationProfile.CorporateActionsApplied) with
        {
            ReceiptId = input.ReceiptId == Guid.Empty ? Guid.NewGuid() : input.ReceiptId
        };
    }

    public static DateTimeOffset StartFromElapsed(DateTimeOffset end, TimeSpan elapsed) =>
        elapsed > TimeSpan.Zero && elapsed < end - DateTimeOffset.MinValue ? end - elapsed : end;
}
