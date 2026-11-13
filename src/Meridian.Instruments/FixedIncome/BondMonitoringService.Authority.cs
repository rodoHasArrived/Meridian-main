using System.Text.Json;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Instruments.FixedIncome;

public sealed partial class BondMonitoringService
{
    private async Task VerifyObligationsAsync(BondMonitoringCaptureRequest request, CancellationToken ct)
    {
        // Isolated synthetic engines may omit source adapters. Production composition supplies both.
        if (holdings is null && bonds is null)
        {
            if (request.MarketWatch is not null || market is not null)
                throw new InvalidOperationException("Retained market watch requires canonical accepted holding and market-store authority.");
            return;
        }
        if (holdings is null || bonds is null || request.CalculationContext is not { KnownAt: { } knownAt } context
            || !Guid.TryParse(request.Scope.AccountId, out var accountId)
            || knownAt > request.RecordedAtUtc || context.AsOf > request.RecordedAtUtc)
            throw new ArgumentException("Canonical monitoring capture requires accepted holdings and a pinned retained calculation context.");
        var asOf = DateOnly.FromDateTime(context.AsOf.UtcDateTime);
        var snapshot = await holdings.GetHoldingsAsync(accountId, asOf, knownAt, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Accepted account holdings are unavailable at the retained capture cutoffs.");
        if (snapshot.AccountId != accountId || snapshot.AsOfDate > asOf)
            throw new InvalidDataException("Accepted monitoring holdings returned another account or future economic snapshot.");
        if (market is not null)
        {
            var expectedMarket = await BondMonitoringMarketWatch.LoadAsync(market, request.Scope.TenantId,
                snapshot, asOf, knownAt, ct).ConfigureAwait(false);
            if (JsonSerializer.Serialize(expectedMarket) != JsonSerializer.Serialize(request.MarketWatch))
                throw new InvalidOperationException("Captured rating/factor watch differs from exact retained tenant market revisions and accepted held securities.");
        }
        else if (request.MarketWatch is not null)
            throw new InvalidOperationException("Retained rating/factor watch requires tenant market-store authority.");
        var active = request.Obligations.Where(row => row.IsCurrentHolding).ToArray();
        var previous = (await store.GetHistoryAsync(request.Scope, ct).ConfigureAwait(false)).LastOrDefault();
        foreach (var obligation in active)
        {
            var models = obligation.Inputs.Where(input => input.Kind == "CalculationModel").ToArray();
            if (obligation.ModelVersion is not (BondWorkflowModelVersions.Legacy or BondWorkflowModelVersions.Current)
                || models.Length != 1 || models[0].Key != obligation.ModelVersion)
                throw new InvalidOperationException("Monitoring obligations require one exact supported retained workflow model and matching CalculationModel input; refresh ambiguous or unsupported source evidence explicitly.");
        }
        var authoritative = new Dictionary<string, (Guid Security, string Kind, DateOnly Due, decimal Amount, string Currency, string Model, bool IsForecast)>();
        var heldSecurities = new HashSet<Guid>();
        foreach (var group in snapshot.Positions.Where(row => row.Bond is not null && row.SecurityId is { } id && id != Guid.Empty)
            .GroupBy(row => row.SecurityId!.Value))
        {
            var rows = group.ToArray();
            var face = rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.OriginalFace) : row.Bond!.OriginalFace);
            if (rows.Any(row => row.Bond!.OriginalFace != 0m))
                heldSecurities.Add(group.Key);
            if (face == 0m)
            {
                if (heldSecurities.Contains(group.Key))
                    throw new InvalidOperationException(AccountBondIncomePlanningService.OffsettingHolderEntitlementBlocker(group.Key));
                continue;
            }
            var versions = active.Where(row => row.SecurityId == group.Key).Select(row => row.ModelVersion).Distinct(StringComparer.Ordinal).ToArray();
            if (versions.Length != 1)
                throw new InvalidOperationException("Each held security requires one unambiguous retained workflow model for monitoring revalidation; missing or mixed versions require an explicit refreshed plan.");
            var result = await bonds.CalculateAsync(new(group.Key, context, Math.Abs(face)) { ModelVersion = versions[0] }, ct).ConfigureAwait(false);
            if (result is null || result.IsBlocked || result.ModelVersion != versions[0]
                || rows.Select(row => row.Currency).Distinct().Count() != 1
                || result.Currency != rows[0].Currency || result.CurrentFace != Math.Abs(rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace)))
                throw new InvalidOperationException("Retained bond economics do not reproduce accepted account holdings for monitoring capture.");
            if (AccountBondIncomePlanningService.HolderEntitlementBlocker(result) is { } entitlementBlocker)
                throw new InvalidOperationException(entitlementBlocker);
            foreach (var obligation in AccountBondIncomePlanningService.ProjectObligations(request.Scope, group.Key, result,
                Math.Sign(face), result.Inputs, previous))
                authoritative.Add(obligation.ObligationId, (group.Key, obligation.FlowType, obligation.DueDate,
                    obligation.Amount, result.Currency, result.ModelVersion, obligation.IsForecast));
        }
        if (active.Length != authoritative.Count || active.Any(row => !authoritative.TryGetValue(row.ObligationId, out var value)
            || row.SecurityId != value.Security || row.FlowType != value.Kind || row.DueDate != value.Due || row.Amount != value.Amount
            || row.Currency != value.Currency || row.ModelVersion != value.Model || row.IsForecast != value.IsForecast
            || !row.Inputs.Any(input => input.Kind == "HoldingsSnapshot" && input.Key == snapshot.BatchId.ToString("D")
                && input.ContentSha256 == snapshot.SourceContentHash)))
            throw new InvalidOperationException("Captured monitoring obligations differ from canonical accepted holdings and retained calculation inputs.");
        if (AccountBondIncomePlanningService.MissingHeldCouponBlockers(previous, active, request.Allocations).FirstOrDefault() is { } missingCoupon)
            throw new InvalidOperationException(missingCoupon);
        foreach (var historical in request.Obligations.Where(row => !row.IsCurrentHolding))
        {
            var prior = previous?.Inputs.Obligations.FirstOrDefault(row => row.ObligationId == historical.ObligationId);
            if (prior is null || JsonSerializer.Serialize(prior with { IsCurrentHolding = false }, BondMonitoringJsonContext.Default.BondMonitoringObligationDto)
                != JsonSerializer.Serialize(historical, BondMonitoringJsonContext.Default.BondMonitoringObligationDto))
                throw new InvalidOperationException("Inactive monitoring obligations must retain a previously captured obligation exactly.");
        }
    }
}
