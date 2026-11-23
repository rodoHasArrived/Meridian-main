using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed partial class ReconciliationProjectionService
{
    public IReadOnlyList<StrategyPositionAccountingCheck> BuildPositionChecks(
        ReconciliationNormalizedInputs inputs, ReconciliationRunRequest request)
    {
        var expected = inputs.Portfolio?.AccountingPositions ?? [];
        var actual = inputs.Ledger?.AccountingPositions ?? [];
        var results = new List<StrategyPositionAccountingCheck>();
        foreach (var key in expected.Concat(actual).Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var expectedRows = expected.Where(item => string.Equals(Key(item), key, StringComparison.OrdinalIgnoreCase)).ToArray();
            var actualRows = actual.Where(item => string.Equals(Key(item), key, StringComparison.OrdinalIgnoreCase)).ToArray();
            var e = expectedRows.Length == 1 ? expectedRows[0] : null;
            var a = actualRows.Length == 1 ? actualRows[0] : null;
            if (expectedRows.Length == 0 && a is { Quantity: 0m, MonetaryBasis: 0m, CoverageFailure: null })
                continue;
            // Cardinality failures still need a label; neither unique counterpart may exist.
            var representative = expectedRows.FirstOrDefault() ?? actualRows[0];
            var problem = expectedRows.Length > 1 || actualRows.Length > 1
                ? "Position scope has multiple rows without aggregation authority."
                : e is null || a is null ? "An exact account and position counterpart is unavailable."
                : string.IsNullOrWhiteSpace(e.FinancialAccountId) || string.IsNullOrWhiteSpace(a.FinancialAccountId)
                    ? "Exact financial-account scope is unavailable."
                : e.SecurityId is null || e.SecurityId == Guid.Empty || a.SecurityId is null || a.SecurityId == Guid.Empty || e.SecurityId != a.SecurityId ? "Canonical security identities are missing or disagree."
                : e.Currency is null || a.Currency is null || !string.Equals(e.Currency, a.Currency, StringComparison.OrdinalIgnoreCase)
                    ? "Functional currencies are missing or disagree; no FX translation was requested."
                : inputs.Portfolio is not null && inputs.Ledger is not null
                    && Math.Abs((inputs.Portfolio.AsOf - inputs.Ledger.AsOf).TotalMinutes) > Math.Max(0, request.MaxAsOfDriftMinutes)
                    ? "Position populations have incompatible economic cutoffs." : null;
            var unitProblem = problem ?? (e?.QuantityUnit is null || a?.QuantityUnit is null || e.Multiplier is null || a.Multiplier is null
                ? "Native unit or multiplier evidence is unavailable."
                : e.QuantityUnit != a.QuantityUnit || e.Multiplier != a.Multiplier ? "Native units or multipliers disagree." : null);
            Add(ReconciliationBreakMeasureKindDto.Quantity, e?.Quantity, a?.Quantity, Math.Abs(request.QuantityTolerance),
                e?.QuantityUnit is { } unit ? $"{unit} (multiplier {e.Multiplier})" : a?.QuantityUnit ?? "unknown",
                unitProblem ?? (e?.Quantity is null ? e?.CoverageFailure : null)
                    ?? (a?.Quantity is null ? a?.CoverageFailure : null), unitProblem is not null);
            Add(ReconciliationBreakMeasureKindDto.CostBasis, e?.MonetaryBasis, a?.MonetaryBasis,
                Math.Abs(request.BasisAmountTolerance), e?.Currency ?? a?.Currency ?? "unknown",
                unitProblem ?? (e?.MonetaryBasis is null ? e?.CoverageFailure : null)
                    ?? (a?.MonetaryBasis is null ? a?.CoverageFailure : null), unitProblem is not null);

            void Add(ReconciliationBreakMeasureKindDto kind, decimal? expectedValue, decimal? actualValue,
                decimal tolerance, string unit, string? unavailable, bool incomparable)
            {
                unavailable ??= expectedValue is null || actualValue is null ? "The exact retained measurement is unavailable." : null;
                // The shared measure has one unit. Different scopes, currencies or cutoffs cannot
                // honestly be rendered as two amounts in that unit, even with a warning attached.
                if (incomparable)
                    expectedValue = actualValue = null;
                var variance = expectedValue.HasValue && actualValue.HasValue ? actualValue.Value - expectedValue.Value : (decimal?)null;
                results.Add(new($"position-{kind}:{Uri.EscapeDataString(key)}", $"{representative.Symbol} {kind} [{representative.FinancialAccountId ?? "unscoped"}]",
                    representative.FinancialAccountId, new(kind, expectedValue, actualValue, variance, tolerance, unit, unavailable),
                    unavailable is null && Math.Abs(variance!.Value) <= tolerance));
            }
        }
        return results;
    }

    private static string Key(StrategyPositionAccountingEvidence position) =>
        $"{position.FinancialAccountId?.Trim().ToUpperInvariant() ?? "<unscoped>"}|{position.Symbol.Trim().ToUpperInvariant()}|{(position.IsShort ? "short" : "long")}";
}

public sealed record StrategyPositionAccountingCheck(string CheckId, string Label, string? SourceScope,
    ReconciliationBreakMeasureDto Measure, bool IsMatch);
