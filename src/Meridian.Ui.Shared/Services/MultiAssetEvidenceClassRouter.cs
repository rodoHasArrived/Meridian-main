namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Declared routing from provider-evidence vocabulary to a canonical asset class, for evidence
/// that names no resolved security. Replaces the substring inference that classified a feed by
/// what its concatenated shape happened to contain (any text containing "factor" read as
/// <c>Bond</c>): each route below names one exact vocabulary value that
/// <c>ProviderLedgerReconciliationService.CorporateActions</c> produces, and evidence outside the
/// declared vocabulary stays unresolved (<c>null</c>) rather than being guessed. Route targets are
/// pinned to the asset-class catalog, and the full vocabulary's routes are pinned, by
/// <c>MultiAssetEvidenceClassRouterTests</c>.
/// <para>
/// The routes preserve the substring chain's outcomes on the produced vocabulary: factor,
/// amortization and principal-return evidence reads as <c>Bond</c>, loan schedules as
/// <c>DirectLoan</c>, fund distributions as <c>PrivateFundInterest</c>, and equity
/// corporate-action, dividend, interest and plain provider evidence stays unresolved. Securitized
/// evidence has no producing vocabulary value today; when one lands it routes to
/// <c>StructuredCredit</c>, the canonical securitized home under ADR-022, by adding its declared
/// token here — never by substring.
/// </para>
/// </summary>
internal static class MultiAssetEvidenceClassRouter
{
    private static readonly IReadOnlyDictionary<string, string> CandidateTypeRoutes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Factor, amortization and principal-return evidence is fixed-income schedule evidence.
            ["FactorScheduleCandidate"] = "Bond",
            ["FactorScheduleEvent"] = "Bond",
            ["AmortizationScheduleEvent"] = "Bond",
            ["PrincipalCashActivity"] = "Bond",
            // Loan schedules belong to private credit.
            ["LoanScheduleEvent"] = "DirectLoan",
            // Distributions are private-fund lifecycle evidence.
            ["DistributionCashActivity"] = "PrivateFundInterest",
        };

    private static readonly IReadOnlyDictionary<string, string> LedgerEffectKindRoutes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FactorScheduleValuationInput"] = "Bond",
            ["FactorScheduleCoverageCandidate"] = "Bond",
            ["AmortizationScheduleValuationInput"] = "Bond",
            ["PrincipalReturnRecognition"] = "Bond",
            ["LoanScheduleValuationInput"] = "DirectLoan",
            ["DistributionIncomeRecognition"] = "PrivateFundInterest",
        };

    /// <summary>The canonical class a candidate type declares, or null when it declares none.</summary>
    public static string? RouteCandidateType(string? candidateType)
        => candidateType is not null && CandidateTypeRoutes.TryGetValue(candidateType, out var assetClass)
            ? assetClass
            : null;

    /// <summary>The canonical class a ledger-effect kind declares, or null when it declares none.</summary>
    public static string? RouteLedgerEffectKind(string? ledgerEffectKind)
        => ledgerEffectKind is not null && LedgerEffectKindRoutes.TryGetValue(ledgerEffectKind, out var assetClass)
            ? assetClass
            : null;

    /// <summary>Every route target, for the catalog-membership test.</summary>
    internal static IEnumerable<string> AllRouteTargets()
        => CandidateTypeRoutes.Values.Concat(LedgerEffectKindRoutes.Values).Distinct(StringComparer.Ordinal);
}
