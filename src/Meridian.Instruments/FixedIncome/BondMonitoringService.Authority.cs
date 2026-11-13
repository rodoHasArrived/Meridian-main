using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;

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
        foreach (var obligation in active)
        {
            var models = obligation.Inputs.Where(input => input.Kind == "CalculationModel").ToArray();
            if (obligation.ModelVersion is not (BondWorkflowModelVersions.Legacy or BondWorkflowModelVersions.Current)
                || models.Length != 1 || models[0].Key != obligation.ModelVersion)
                throw new InvalidOperationException("Monitoring obligations require one exact supported retained workflow model and matching CalculationModel input; refresh ambiguous or unsupported source evidence explicitly.");
        }
        var authoritative = new Dictionary<string, (Guid Security, string Kind, DateOnly Due, decimal Amount, string Currency, string Model)>();
        foreach (var group in snapshot.Positions.Where(row => row.Bond is not null && row.SecurityId is { } id && id != Guid.Empty)
            .GroupBy(row => row.SecurityId!.Value))
        {
            var rows = group.ToArray();
            var face = rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.OriginalFace) : row.Bond!.OriginalFace);
            if (face == 0m)
                continue;
            var versions = active.Where(row => row.SecurityId == group.Key).Select(row => row.ModelVersion).Distinct(StringComparer.Ordinal).ToArray();
            if (versions.Length != 1)
                throw new InvalidOperationException("Each held security requires one unambiguous retained workflow model for monitoring revalidation; missing or mixed versions require an explicit refreshed plan.");
            var result = await bonds.CalculateAsync(new(group.Key, context, Math.Abs(face)) { ModelVersion = versions[0] }, ct).ConfigureAwait(false);
            if (result is null || result.IsBlocked || result.ModelVersion != versions[0]
                || rows.Select(row => row.Currency).Distinct().Count() != 1
                || result.Currency != rows[0].Currency || result.CurrentFace != Math.Abs(rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace)))
                throw new InvalidOperationException("Retained bond economics do not reproduce accepted account holdings for monitoring capture.");
            foreach (var flow in result.CashFlows)
            {
                if (flow.Interest != 0m)
                    Add("Coupon", flow.Interest);
                if (flow.Principal != 0m)
                    Add("Principal", flow.Principal);
                void Add(string kind, decimal value)
                {
                    var payable = LedgerCurrencyRounding.RoundCurrency(Math.Sign(face) * value, result.Currency);
                    if (payable == 0m)
                        return;
                    var identity = AccountBondIncomePlanningService.ObligationIdentity(request.Scope, group.Key, kind, flow.PaymentDate, flow.AccrualStart);
                    authoritative.Add(identity, (group.Key, kind, flow.PaymentDate,
                        payable, result.Currency, result.ModelVersion));
                }
            }
        }
        if (active.Length != authoritative.Count || active.Any(row => !authoritative.TryGetValue(row.ObligationId, out var value)
            || row.SecurityId != value.Security || row.FlowType != value.Kind || row.DueDate != value.Due || row.Amount != value.Amount
            || row.Currency != value.Currency || row.ModelVersion != value.Model
            || !row.Inputs.Any(input => input.Kind == "HoldingsSnapshot" && input.Key == snapshot.BatchId.ToString("D")
                && input.ContentSha256 == snapshot.SourceContentHash)))
            throw new InvalidOperationException("Captured monitoring obligations differ from canonical accepted holdings and retained calculation inputs.");
        var previous = (await store.GetHistoryAsync(request.Scope, ct).ConfigureAwait(false)).LastOrDefault();
        foreach (var historical in request.Obligations.Where(row => !row.IsCurrentHolding))
        {
            var prior = previous?.Inputs.Obligations.FirstOrDefault(row => row.ObligationId == historical.ObligationId);
            if (prior is null || JsonSerializer.Serialize(prior with { IsCurrentHolding = false }, BondMonitoringJsonContext.Default.BondMonitoringObligationDto)
                != JsonSerializer.Serialize(historical, BondMonitoringJsonContext.Default.BondMonitoringObligationDto))
                throw new InvalidOperationException("Inactive monitoring obligations must retain a previously captured obligation exactly.");
        }
    }
}
