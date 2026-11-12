using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Instruments.FixedIncome;

public sealed class BondComparisonService(IBondWorkflowService bonds) : IBondComparisonService
{
    public async Task<BondComparisonResult> CompareAsync(BondComparisonRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RunId) || request.Bonds is null || request.Bonds.Count is < 1 or > 100
            || request.Context is null || request.Context.SettlementDate is null || request.Context.KnownAt is null
            || request.Context.AsOf == default || request.Context.AsOf.Offset != TimeSpan.Zero
            || request.Context.KnownAt.Value.Offset != TimeSpan.Zero || request.Context.KnownAt > DateTimeOffset.UtcNow
            || request.Context.AsOf > DateTimeOffset.UtcNow)
            throw new ArgumentException("A comparison identity, explicit settlement/knowledge cutoffs and one to 100 rows are required.");
        var results = new List<BondWorkflowResult>();
        var issues = new List<string>();
        foreach (var row in request.Bonds)
        {
            if (row is null)
                throw new ArgumentException("Comparison rows cannot be null.");
            var context = request.Context with
            { SecurityVersion = row.Context?.SecurityVersion, Curves = row.Context?.Curves ?? request.Context.Curves };
            var result = await bonds.CalculateAsync(row with { Context = context }, ct).ConfigureAwait(false);
            if (result is null)
                issues.Add($"Security {row.SecurityId} is unavailable in the current scope.");
            else
                results.Add(result);
        }
        return new(request.RunId, "bond-comparison-v1", request.Context, results, issues,
            issues.Count == 0 && results.All(row => !row.IsBlocked && row.Metrics.All(metric => metric.Available)));
    }
}

/// <summary>Account-owned exposure; signed held face scales one normalized model result, without cross-currency sums.</summary>
public sealed class BondPortfolioRiskService(IAccountBondHoldingsSource holdings, IBondWorkflowService bonds,
    IBondReferenceService reference) : IBondPortfolioRiskService
{
    public async Task<BondPortfolioRiskResult> CalculateAsync(AccountCashForecastScopeDto scope,
        BondPortfolioRiskRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);
        if (new[] { scope.TenantId, scope.CompanyId, scope.AccountId, scope.BookId, scope.BaseCurrency }.Any(string.IsNullOrWhiteSpace)
            || !Guid.TryParse(scope.AccountId, out var accountId) || accountId == Guid.Empty || request.Context is null
            || request.Context.KnownAt is null || request.Context.SettlementDate is null
            || request.Context.AsOf == default || request.Context.AsOf.Offset != TimeSpan.Zero
            || request.Context.KnownAt.Value.Offset != TimeSpan.Zero || request.Context.KnownAt > DateTimeOffset.UtcNow
            || request.Context.AsOf > DateTimeOffset.UtcNow || request.Context.AsOf.Year > 9989
            || request.ParallelShiftBps is < 0m or > 10000m || request.SpreadWideningBps is < 0m or > 10000m)
            throw new ArgumentException("Exact account scope, retained cutoffs and bounded basis-point shocks are required.");
        var date = DateOnly.FromDateTime(request.Context.AsOf.UtcDateTime);
        var snapshot = await holdings.GetHoldingsAsync(accountId, date, request.Context.KnownAt, ct).ConfigureAwait(false);
        var issues = new List<string>();
        var positions = new List<BondPortfolioRiskPosition>();
        if (snapshot is null)
            issues.Add("No accepted account holdings snapshot is available at the requested cutoffs.");
        else
        {
            if (snapshot.AccountId != accountId || snapshot.AsOfDate > date || !Sha256Digest.IsCanonical(snapshot.SourceContentHash))
                throw new InvalidDataException("Holdings authority returned a different account or invalid retained identity.");
            foreach (var row in snapshot.Positions.Where(row => row.SecurityId is null || row.Bond is null))
                issues.Add($"Holding {row.Identifier} lacks canonical bond face or identity and is excluded.");
            foreach (var group in snapshot.Positions.Where(row => row.SecurityId is not null && row.Bond is not null)
                .GroupBy(row => row.SecurityId!.Value))
            {
                var face = group.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.OriginalFace) : row.Bond!.OriginalFace);
                if (face == 0m)
                {
                    if (group.Select(row => row.Currency).Distinct().Count() != 1
                        || group.Select(row => row.Bond!.PrincipalFactor).Distinct().Count() != 1
                        || group.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace) != 0m)
                        issues.Add($"Security {group.Key} has offsetting original face with contradictory holding currencies or principal factors.");
                    continue;
                }
                var sign = Math.Sign(face);
                var calculation = new BondWorkflowRequest(group.Key, request.Context, Math.Abs(face)) { AccountId = scope.AccountId };
                string? quoteIssue = null;
                BondCalculationInput? quoteAuthority = null;
                decimal observedDirty = 0m;
                try { (calculation, quoteAuthority, observedDirty) = BondPortfolioHoldingQuote.Bind(calculation, snapshot, group.ToArray()); }
                catch (ArgumentException exception) { quoteIssue = exception.Message; }
                var baseline = await bonds.CalculateAsync(calculation, ct).ConfigureAwait(false);
                if (baseline is null)
                { issues.Add($"Security {group.Key} is unavailable."); continue; }
                if (quoteAuthority is not null && !baseline.IsBlocked
                    && (baseline.OriginalDirtyConsideration is not { } modeledDirty
                        || LedgerCurrencyRounding.RoundCurrency(modeledDirty, baseline.Currency)
                            != LedgerCurrencyRounding.RoundCurrency(observedDirty, baseline.Currency)))
                {
                    quoteIssue = "Accepted dirty holding invoice differs from retained contractual accrued interest; quote-calibrated credit risk is unavailable.";
                    calculation = calculation with { CleanPricePer100 = null, QuoteSettlementDate = null, ConfirmationEvidenceReference = null };
                    quoteAuthority = null;
                    baseline = await bonds.CalculateAsync(calculation, ct).ConfigureAwait(false);
                    if (baseline is null) { issues.Add($"Security {group.Key} is unavailable."); continue; }
                }
                var localIssues = baseline.Issues.Select(row => row.Code + ": " + row.Reason).ToList();
                if (quoteIssue is not null) localIssues.Add(quoteIssue);
                var refs = await reference.GetReferenceAsOfAsync(group.Key, date, ct).ConfigureAwait(false);
                if (refs?.Version != baseline.Context.SecurityVersion)
                {
                    localIssues.Add("Issuer/maturity metadata is unavailable at the retained calculation terms version.");
                    refs = null;
                }
                var scenarioValues = new List<BondPortfolioScenarioValue>();
                var pillars = request.SteepeningPillars ??
                    [new(date.AddYears(2), -25m), new(date.AddYears(10), 25m)];
                var scenarios = new (string Name, BondWorkflowRequest Request)[]
                {
                    ("parallel-up", calculation with { RateShiftBps = request.ParallelShiftBps }),
                    ("parallel-down", calculation with { RateShiftBps = -request.ParallelShiftBps }),
                    ("steepening", calculation with { RatePillars = pillars }),
                    ("spread-widening", calculation with { SpreadShiftBps = request.SpreadWideningBps })
                };
                foreach (var scenario in scenarios)
                {
                    var result = await bonds.CalculateAsync(scenario.Request, ct).ConfigureAwait(false);
                    var pv = result?.IsBlocked == false ? result.PresentValue * sign : null;
                    scenarioValues.Add(new(scenario.Name, pv, pv - baseline.PresentValue * sign));
                    if (pv is null)
                        localIssues.Add($"{scenario.Name}: complete retained repricing is unavailable.");
                }
                var holdingConflict = group.Any(row => row.Currency != baseline.Currency);
                if (holdingConflict)
                    localIssues.Add("Holding and model currency differ; exposure is excluded from covered totals.");
                if (group.Select(row => row.Bond!.PrincipalFactor).Distinct().Count() != 1
                    || Math.Abs(group.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace)) != baseline.CurrentFace)
                {
                    localIssues.Add("Observed current face or principal factors disagree with retained contractual economics.");
                    holdingConflict = true;
                }
                var covered = !baseline.IsBlocked && !holdingConflict;
                if (!covered)
                    scenarioValues = scenarioValues.Select(row => row with { PresentValue = null, Change = null }).ToList();
                positions.Add(new(group.Key, refs?.IssuerName, baseline.Currency, refs?.Lifecycle?.MaturityDate,
                    face, covered ? baseline.PresentValue * sign : null, covered ? baseline.Dv01 * sign : null,
                    covered ? baseline.Cs01 * sign : null, scenarioValues, localIssues)
                { CalculationRequest = calculation, QuoteAuthority = quoteAuthority });
            }
        }
        var buckets = new List<BondPortfolioRiskBucket>();
        AddBuckets("security", row => row.SecurityId.ToString("D"));
        AddBuckets("issuer", row => row.Issuer ?? "Unknown issuer");
        AddBuckets("currency", row => row.Currency);
        AddBuckets("maturity", row => row.Maturity is not { } maturity ? "Unknown" : maturity <= date.AddYears(1) ? "0–1 years"
            : maturity <= date.AddYears(3) ? "1–3 years" : maturity <= date.AddYears(5) ? "3–5 years"
            : maturity <= date.AddYears(10) ? "5–10 years" : "10+ years");
        return new(scope, request.Context, snapshot?.BatchId, snapshot?.SourceContentHash, positions, buckets,
            issues, snapshot is not null && issues.Count == 0 && positions.All(row => row.PresentValue is not null
                && row.Dv01 is not null && row.Cs01 is not null && row.Scenarios.All(scenario => scenario.PresentValue is not null)));

        void AddBuckets(string dimension, Func<BondPortfolioRiskPosition, string> key)
        {
            foreach (var group in positions.GroupBy(row => (Key: key(row), row.Currency)))
                buckets.Add(new(dimension, group.Key.Key, group.Key.Currency, group.Count(),
                    group.Count(row => row.PresentValue is not null && row.Dv01 is not null && row.Cs01 is not null),
                    group.Sum(row => row.PresentValue ?? 0m), group.Sum(row => row.Dv01 ?? 0m), group.Sum(row => row.Cs01 ?? 0m)));
        }
    }
}
