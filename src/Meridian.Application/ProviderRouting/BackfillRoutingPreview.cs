using System.Text.RegularExpressions;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.ProviderRouting;

/// <summary>Construction-only preview. Does not fetch data, consume vendor quota, save config or install providers.</summary>
public static class BackfillRoutingPreview
{
    public static RoutePreviewResponse Preview(AppConfig current, RoutePreviewRequest request,
        Func<AppConfig, IReadOnlyList<IHistoricalDataProvider>> createProviders, CancellationToken ct = default)
    {
        Validate(request, ct);
        var config = ProviderRoutingPreviewConfig.ApplyBackfill(current, request.ProposedConfig);
        var backfill = config.Backfill ?? new BackfillConfig();
        var providers = createProviders(config);
        try
        {
            ct.ThrowIfCancellationRequested();
            var warnings = new List<string>
            {
                "Configuration preview only: no provider requests were sent. Symbol coverage, date availability, current quota and network health remain unverified.",
                $"Query: {request.Symbol} daily, {request.From:yyyy-MM-dd} through {request.To:yyyy-MM-dd}. Existing archives are unchanged."
            };
            var candidates = BackfillRoutingSnapshot.Order(providers.Select(provider => new BackfillRoutingProvider(provider, provider.Priority)))
                .Select(ToCandidate)
                .ToArray();
            var preferred = backfill.Provider;
            var selected = string.IsNullOrWhiteSpace(preferred) || string.Equals(preferred, "composite", StringComparison.OrdinalIgnoreCase)
                ? candidates.FirstOrDefault()
                : candidates.FirstOrDefault(p => string.Equals(p.ConnectionId, preferred, StringComparison.OrdinalIgnoreCase));
            var skipped = BackfillProviderCatalog.Providers
                .Where(p => p.Id != "openfigi" && BackfillProviderCatalog.Read(backfill.Providers ?? new(), p.Id).Enabled &&
                    !providers.Any(actual => string.Equals(actual.ProviderId, p.Id, StringComparison.OrdinalIgnoreCase)))
                .Select(p => $"{p.DisplayName}: enabled but unavailable; required credentials or adapter construction could not be resolved.")
                .ToArray();
            var fabricated = selected is not null && providers.Any(p => p.ProviderId == selected.ConnectionId && p.IsSimulated);
            if (fabricated)
                warnings.Add("FABRICATED DATA WOULD WIN: Synthetic would serve this query. Review enabled real vendors and their credential availability before saving or running research.");
            warnings.AddRange(skipped);
            return new RoutePreviewResponse("HistoricalBars", selected is not null, selected?.ConnectionId,
                selected?.ProviderFamilyId, "ReadOnlyPreview", false,
                selected?.ReasonCodes ?? ["No eligible adapter was constructed for the configured provider selection."], skipped,
                candidates.Where(p => p != selected).Select(p => p.ConnectionId).ToArray(), null, candidates,
                Warnings: warnings.ToArray(), IsProposed: request.ProposedConfig is not null);
        }
        finally
        {
            foreach (var provider in providers)
                provider.Dispose();
        }
    }

    public static RoutePreviewResponse PreviewActive(BackfillRoutingSnapshot routing, RoutePreviewRequest request,
        CancellationToken ct = default)
    {
        Validate(request, ct);
        var ordered = routing.DefaultOrder;
        var candidates = ordered.Select(ToCandidate).ToArray();
        var selected = candidates.FirstOrDefault();
        var warnings = new List<string>
        {
            "Active default backfill order for the next job. No provider requests were sent.",
            $"Query: {request.Symbol} daily, {request.From:yyyy-MM-dd} through {request.To:yyyy-MM-dd}."
        };
        if (ordered.FirstOrDefault()?.Provider.IsSimulated == true)
            warnings.Add("FABRICATED DATA WOULD WIN: Synthetic would serve this query.");
        return new RoutePreviewResponse("HistoricalBars", selected is not null, selected?.ConnectionId,
            selected?.ProviderFamilyId, "ReadOnlyPreview", false,
            selected?.ReasonCodes ?? ["No eligible adapter is available for the configured provider selection."], [],
            candidates.Skip(1).Select(candidate => candidate.ConnectionId).ToArray(), null, candidates, Warnings: warnings);
    }

    private static RoutePreviewCandidateDto ToCandidate(BackfillRoutingProvider entry)
        => new(entry.ProviderId, entry.ProviderId, false, 0, entry.Priority,
            [entry.Provider.IsSimulated ? "FABRICATED DATA: Synthetic bars." : "Credential-authorized adapter constructed; coverage and live health unverified."], []);

    private static void Validate(RoutePreviewRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(request.Capability, "HistoricalBars", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backfill preview supports HistoricalBars only.");
        if (string.IsNullOrWhiteSpace(request.Symbol) || !Regex.IsMatch(request.Symbol, @"^[A-Za-z0-9.\-]{1,20}$"))
            throw new ArgumentException("A valid symbol is required.");
        if (request.From is null || request.To is null || request.From > request.To)
            throw new ArgumentException("An ordered start and end date are required.");
        if (!string.Equals(request.Granularity, "daily", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("This routing preview currently supports daily bars only.");
    }
}
