using System.Globalization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Joins retained tenant market revisions to accepted holdings without inventing account evidence.</summary>
internal static class BondMonitoringMarketWatch
{
    internal static async Task<BondMonitoringMarketWatchDto> LoadAsync(IBondMarketDataService market,
        string tenantId, AccountBondHoldingsSnapshotDto snapshot, DateOnly asOf, DateTimeOffset knownAt,
        CancellationToken ct)
    {
        var held = snapshot.Positions.Where(row => row.Bond is not null && row.SecurityId is { } id && id != Guid.Empty)
            .GroupBy(row => row.SecurityId!.Value)
            .Select(group => new BondMonitoringMarketHoldingDto(group.Key,
                group.Select(row => row.Bond!.PrincipalFactor).Distinct().Order().ToArray()))
            .OrderBy(row => row.SecurityId).ToArray();
        var observations = new List<BondMarketObservationDto>();
        foreach (var holding in held)
            foreach (var kind in new[] { BondMarketObservationKind.CreditRating, BondMarketObservationKind.PrincipalFactor })
            {
                var retained = await market.QueryAsync(tenantId, new(Kind: kind, SecurityId: holding.SecurityId,
                    ThroughDate: asOf, KnownAtUtc: knownAt, IncludeSuperseded: true), ct).ConfigureAwait(false);
                // A source adapter that ignores a predicate must not leak another population into this account.
                if (retained.Any(row => row.Input.SecurityId != holding.SecurityId || row.Input.Kind != kind
                    || row.Input.EconomicDate > asOf || row.RecordedAtUtc > knownAt || row.Input.PublishedAtUtc > knownAt))
                    throw new InvalidDataException("Retained market watch returned another security, category or economic/knowledge cutoff.");
                observations.AddRange(retained);
            }
        return Canonicalize(new(snapshot.SourceContentHash, held, observations))!;
    }

    internal static BondMonitoringMarketWatchDto? Canonicalize(BondMonitoringMarketWatchDto? source)
    {
        if (source is null)
            return null;
        if (source.Holdings is null || source.Observations is null || source.Holdings.Any(row => row?.PrincipalFactors is null)
            || source.Observations.Any(row => row?.Input is null))
            throw new ArgumentException("Market watch requires retained holding factors and typed source revisions.");
        return source with
        {
            Holdings = source.Holdings.OrderBy(row => row.SecurityId).Select(row => row with
            { PrincipalFactors = row.PrincipalFactors.Order().ToArray() }).ToArray(),
            Observations = source.Observations.OrderBy(row => row.ObservationId).ToArray()
        };
    }

    internal static IReadOnlyList<BondWatchItemDto> Build(BondMonitoringCaptureRequest inputs,
        Action<string, string, string?> issue)
    {
        if (inputs.MarketWatch is not { } source)
            return [];
        if (inputs.CalculationContext is not { KnownAt: { } knownAt } context
            || knownAt == default || knownAt.Offset != TimeSpan.Zero || knownAt > inputs.RecordedAtUtc
            || context.AsOf == default || context.AsOf.Offset != TimeSpan.Zero || context.AsOf > inputs.RecordedAtUtc
            || !Sha256Digest.IsCanonical(source.HoldingsContentSha256)
            || source.Holdings.Any(row => row.SecurityId == Guid.Empty || row.PrincipalFactors.Count == 0
                || row.PrincipalFactors.Any(value => value is < 0m or > 1m)
                || row.PrincipalFactors.Distinct().Count() != row.PrincipalFactors.Count)
            || source.Holdings.Select(row => row.SecurityId).Distinct().Count() != source.Holdings.Count
            || source.Observations.Select(row => row.ObservationId).Distinct().Count() != source.Observations.Count)
        {
            issue("bond-monitoring.market-source-invalid", "Market watch requires accepted held-security factors, source identity and pinned knowledge time.", null);
            return [];
        }
        var asOf = DateOnly.FromDateTime(context.AsOf.UtcDateTime);
        var valid = true;
        foreach (var observation in source.Observations)
        {
            try
            { BondMarketDataRules.ValidateRetained(observation); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException)
            {
                issue("bond-monitoring.market-source-invalid", error.Message, observation.ObservationId.ToString("D"));
                valid = false;
                continue;
            }
            if (observation.Input.Kind is not (BondMarketObservationKind.CreditRating or BondMarketObservationKind.PrincipalFactor)
                || !source.Holdings.Any(row => row.SecurityId == observation.Input.SecurityId)
                || observation.Input.EconomicDate > asOf || observation.RecordedAtUtc > knownAt
                || observation.Input.PublishedAtUtc > knownAt)
            {
                issue("bond-monitoring.market-source-scope", "Market watch must retain only held securities and eligible tenant rating/factor revisions.", observation.ObservationId.ToString("D"));
                valid = false;
            }
        }
        if (source.Observations.GroupBy(row => BondMarketDataRules.SourceRevisionKey(row.Input), StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            issue("bond-monitoring.market-revision-ambiguous", "Retained market source revisions must be unique.", null);
            valid = false;
        }
        if (!valid)
            return [];
        var watch = new List<BondWatchItemDto>();
        var active = source.Observations.GroupBy(row => BondMarketDataRules.SourceKey(row.Input), StringComparer.Ordinal)
            .Select(group => group.MaxBy(row => row.Input.Revision)!).ToArray();
        var current = active.GroupBy(row => (row.Input.SecurityId, row.Input.Kind, row.Input.ProviderId,
                row.Input.SeriesKey, row.Input.RatingAgency))
            .SelectMany(group => group.Where(row => row.Input.EconomicDate == group.Max(value => value.Input.EconomicDate))).ToArray();
        foreach (var observation in current)
        {
            var value = observation.Input;
            var security = value.SecurityId!.Value;
            var factors = source.Holdings.Single(row => row.SecurityId == security).PrincipalFactors;
            var factorConflict = value.Kind == BondMarketObservationKind.PrincipalFactor
                && (factors.Count != 1 || factors[0] != value.Value);
            var kind = value.Kind == BondMarketObservationKind.CreditRating ? "Rating" : factorConflict ? "FactorConflict" : "Factor";
            var summary = value.Kind == BondMarketObservationKind.CreditRating
                ? $"{value.RatingAgency}: {value.Rating}; source {value.SourceName}, revision {value.Revision}."
                : $"Observed factor {value.Value?.ToString(CultureInfo.InvariantCulture)}; accepted holding factor(s) {string.Join(", ", factors.Select(f => f.ToString(CultureInfo.InvariantCulture)))}; source {value.SourceName}, revision {value.Revision}.";
            watch.Add(new("market:" + observation.ObservationId.ToString("D"), security, kind, value.EconomicDate,
                summary, value.RatingAgency, value.ProviderId, factorConflict,
                Affected(security, value.Kind == BondMarketObservationKind.CreditRating ? null : value.EconomicDate)));
        }
        foreach (var group in source.Observations.GroupBy(row => BondMarketDataRules.SourceKey(row.Input), StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            var latest = group.MaxBy(row => row.Input.Revision)!;
            var previous = group.Where(row => row.Input.Revision != latest.Input.Revision).ToArray();
            var security = latest.Input.SecurityId!.Value;
            var affected = Affected(security, latest.Input.Kind == BondMarketObservationKind.CreditRating ? null : latest.Input.EconomicDate);
            watch.Add(new("market-revision:" + latest.ObservationId.ToString("D"), security,
                latest.Input.Kind == BondMarketObservationKind.CreditRating ? "RatingRevision" : "FactorRevision", latest.Input.EconomicDate,
                $"{latest.Input.SourceName} source revision {latest.Input.Revision} supersedes retained revision(s) {string.Join(", ", previous.Select(row => row.Input.Revision).Order())}.",
                latest.Input.RatingAgency, latest.Input.ProviderId, true, affected));
            var stale = inputs.Obligations.Where(row => row.IsCurrentHolding && row.SecurityId == security
                && row.Inputs.Any(input => previous.Any(prior => input.ContentSha256 == prior.ContentSha256
                    || input.ContentSha256 == prior.Input.SourceEvidenceSha256))).Select(row => row.ObligationId).ToArray();
            if (stale.Length > 0)
                watch.Add(new("market-stale:" + latest.ObservationId.ToString("D"), security, "MarketDependencyStale", latest.Input.EconomicDate,
                    "Retained calculation inputs reference a superseded market source revision; rebuild and compare before relying on those obligations.",
                    latest.Input.RatingAgency, latest.Input.ProviderId, true, stale));
        }
        foreach (var group in current.GroupBy(row => (row.Input.SecurityId, row.Input.Kind, row.Input.RatingAgency))
            .Where(group => group.Select(row => row.Input.Kind == BondMarketObservationKind.CreditRating
                ? row.Input.Rating : row.Input.Value?.ToString(CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            var security = group.Key.SecurityId!.Value;
            watch.Add(new($"market-conflict:{security:D}:{group.Key.Kind}:{group.Key.RatingAgency}", security,
                group.Key.Kind == BondMarketObservationKind.CreditRating ? "RatingSourceConflict" : "FactorSourceConflict", asOf,
                "Retained sources disagree; review each provider/agency observation separately. No combined value was selected.", group.Key.RatingAgency,
                string.Join(", ", group.Select(row => row.Input.ProviderId).Distinct().Order(StringComparer.Ordinal)), true, Affected(security, null)));
        }
        return watch;

        string[] Affected(Guid security, DateOnly? from) => inputs.Obligations.Where(row => row.IsCurrentHolding
            && row.SecurityId == security && (from is null || row.DueDate >= from)).Select(row => row.ObligationId).ToArray();
    }
}
