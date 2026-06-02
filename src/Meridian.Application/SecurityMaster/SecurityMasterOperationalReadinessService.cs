using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public sealed record SecurityMasterOperationalReadinessRequest(
    string? FundAccountId = null,
    string? Entity = null,
    string? AssetClass = null);

public interface ISecurityMasterOperationalReadinessService
{
    Task<MultiAssetCoverageSummaryDto> GetReadinessAsync(
        SecurityMasterOperationalReadinessRequest request,
        CancellationToken ct = default);
}

public sealed class SecurityMasterOperationalReadinessService : ISecurityMasterOperationalReadinessService
{
    private static readonly IReadOnlyList<MultiAssetCoverageSpecification> Specifications =
    [
        Listed(
            "Equity",
            "Equities",
            "Listed equity positions require exchange identifiers, quote evidence, tax-lot support, corporate-action coverage, and mapped security-ledger accounts.",
            ["Ticker", "ISIN/CUSIP/FIGI or provider symbol"],
            ["issuer", "share class", "dividend/convertible terms when applicable"],
            ["Quote", "Corporate action", "Position", "Trade", "Cash"],
            "Security position / realized and unrealized P&L / dividend income",
            ["quantity", "market value", "cash", "corporate action", "tax lot"],
            hardBlocker: false),
        Listed(
            "Option",
            "Options",
            "Option readiness depends on OCC identity, underlying linkage, contract economics, exercise/expiry metadata, and derivative ledger classification.",
            ["OCC option symbol", "Underlying SecurityId", "Provider symbol"],
            ["put/call", "strike", "expiry", "multiplier"],
            ["Quote", "Option contract", "Underlying security", "Trade", "Cash"],
            "Derivative asset/liability / premium / realized and unrealized P&L",
            ["quantity", "market value", "cash", "option contract metadata"],
            hardBlocker: true),
        Listed(
            "Future",
            "Futures",
            "Future readiness requires exchange contract metadata, expiry controls, variation margin treatment, and futures-specific reconciliation evidence.",
            ["Root symbol", "Contract month", "Provider symbol"],
            ["expiry", "multiplier", "first notice", "last trade"],
            ["Quote", "Future contract", "Margin", "Trade", "Cash"],
            "Derivative asset/liability / variation margin / realized and unrealized P&L",
            ["quantity", "market value", "cash", "future contract metadata"],
            hardBlocker: true),
        Listed(
            "FxSpot",
            "FX",
            "FX readiness requires currency-pair validation, rate provenance, cash-leg reconciliation, and multi-currency ledger coverage.",
            ["Base currency", "Quote currency", "Provider symbol"],
            ["settlement currency", "trade date", "settlement date"],
            ["FX rate", "Cash", "Trade", "Settlement"],
            "Currency remeasurement / cash / realized and unrealized FX P&L",
            ["FX", "cash", "settlement", "market value"],
            hardBlocker: true),
        Listed(
            "Bond",
            "Fixed income",
            "Fixed income readiness requires debt identifiers, maturity/coupon terms, accruals, amortization policy, and factor/corporate-action evidence when applicable.",
            ["CUSIP/ISIN/FIGI", "Provider symbol"],
            ["maturity", "coupon/spread", "issue date", "call date when applicable"],
            ["Quote", "Accrual", "Cash flow schedule", "Corporate action", "Factor schedule"],
            "Debt security / amortization / interest income / realized and unrealized P&L",
            ["quantity", "market value", "cash", "accrual", "factor schedule"],
            hardBlocker: false),
        Listed(
            "DirectLoan",
            "Loans",
            "Loan readiness requires borrower identity, loan schedule evidence, accrual policy, principal paydown events, and direct-lending ledger classification.",
            ["Internal code", "Borrower", "LEI or provider symbol when available"],
            ["borrower", "commitment", "principal", "rate", "maturity", "loan schedule"],
            ["Loan schedule", "Accrual", "Cash", "Collateral", "Valuation"],
            "Loan receivable / interest income / fees / realized and unrealized P&L",
            ["quantity", "market value", "cash", "accrual", "loan schedule"],
            hardBlocker: true),
        Listed(
            "CustomAsset",
            "MBS / ABS / CLO / CMBS / private assets",
            "Structured and private assets require governed custom profiles, factor or NAV evidence, valuation approval, and profile-aware ledger classification.",
            ["Internal code", "CUSIP/ISIN/FIGI when available", "Provider symbol"],
            ["approved profile", "profile version", "required profile fields", "valuation date"],
            ["Custom profile", "Factor schedule", "Dealer pricing", "NAV", "Cash", "Collateral"],
            "Profile-derived classification / valuation adjustment / income accrual / commitment accounting",
            ["quantity", "market value", "cash", "factor schedule", "custom-profile evidence"],
            hardBlocker: true),
        Listed(
            "OtherSecurity",
            "Governed other securities",
            "OtherSecurity records must carry category and optional governed profile evidence before accounting or close workflows can rely on them.",
            ["Internal code", "Provider symbol", "Ticker/FIGI when available"],
            ["category", "approved profile when used", "ledger classification"],
            ["Custom profile", "Quote", "Cash", "Valuation"],
            "Governed profile-derived classification / valuation / realized and unrealized P&L",
            ["quantity", "market value", "cash", "custom-profile evidence"],
            hardBlocker: true)
    ];

    private readonly AssetClassValidatorRegistry _assetClassValidators;
    private readonly ISecurityAssetProfileCatalog _assetProfileCatalog;

    public SecurityMasterOperationalReadinessService(
        AssetClassValidatorRegistry? assetClassValidators = null,
        ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        _assetProfileCatalog = assetProfileCatalog ?? StaticSecurityAssetProfileCatalog.CreateDefault();
        _assetClassValidators = assetClassValidators ?? AssetClassValidatorRegistry.CreateDefault(_assetProfileCatalog);
    }

    public Task<MultiAssetCoverageSummaryDto> GetReadinessAsync(
        SecurityMasterOperationalReadinessRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var filtered = string.IsNullOrWhiteSpace(request.AssetClass)
            ? Specifications
            : Specifications
                .Where(spec => string.Equals(spec.AssetClass, request.AssetClass, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var rows = filtered.Select(BuildCoverageRow).ToArray();
        var blocked = rows.Count(static row => string.Equals(row.Status, "Blocked", StringComparison.OrdinalIgnoreCase));
        var reviewRequired = rows.Count(static row => string.Equals(row.Status, "ReviewRequired", StringComparison.OrdinalIgnoreCase));
        var ready = rows.Length - blocked - reviewRequired;

        var metrics = new WorkstationMetricCard[]
        {
            new("multi-asset-classes", "Asset classes", rows.Length.ToString(CultureInfo.InvariantCulture), "covered", "default"),
            new("multi-asset-ready", "Ready", ready.ToString(CultureInfo.InvariantCulture), "definition + evidence", ready == rows.Length ? "success" : "default"),
            new("multi-asset-review", "Review required", reviewRequired.ToString(CultureInfo.InvariantCulture), "evidence gaps", reviewRequired > 0 ? "warning" : "success"),
            new("multi-asset-blocked", "Blocked", blocked.ToString(CultureInfo.InvariantCulture), "close gates", blocked > 0 ? "danger" : "success")
        };

        var fundAccountId = string.IsNullOrWhiteSpace(request.FundAccountId) ? "all" : request.FundAccountId!;
        var entity = string.IsNullOrWhiteSpace(request.Entity) ? "portfolio" : request.Entity!;

        return Task.FromResult(new MultiAssetCoverageSummaryDto(
            FundAccountId: fundAccountId,
            Entity: entity,
            AsOfUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Metrics: metrics,
            AssetClasses: rows,
            DrillThroughRoutes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["portfolio"] = UiApiRoutes.WorkstationPortfolio,
                ["accounting"] = UiApiRoutes.WorkstationAccounting,
                ["securityMaster"] = UiApiRoutes.WorkstationSecurityMasterSearch,
                ["reconciliation"] = UiApiRoutes.ReconciliationRuns,
                ["coverage"] = UiApiRoutes.WorkstationPortfolioMultiAssetCoverage
            }));
    }

    private MultiAssetClassCoverageDto BuildCoverageRow(MultiAssetCoverageSpecification spec)
    {
        var hasValidator = _assetClassValidators.TryGetValidator(spec.AssetClass, out _);
        var hasCatalogDescriptor = SecurityAssetClassCatalog.GetOrDefault(spec.AssetClass).AssetClass != "Unknown";
        var hasProfileCoverage = !RequiresGovernedProfile(spec.AssetClass)
            || _assetProfileCatalog.GetProfiles().Any(profile =>
                profile.Status == SecurityAssetProfileStatusDto.Approved &&
                (string.Equals(profile.ProfileId, "structured-credit-io-po", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(profile.Category, "PrivateFunds", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(profile.Category, "PrivateEquity", StringComparison.OrdinalIgnoreCase)));

        var requirements = BuildRequirements(spec, hasValidator, hasCatalogDescriptor, hasProfileCoverage);
        var blockers = BuildBlockers(spec, hasValidator, hasCatalogDescriptor, hasProfileCoverage);
        var status = blockers.Any(static blocker => string.Equals(blocker.Severity, "Blocker", StringComparison.OrdinalIgnoreCase))
            ? "Blocked"
            : requirements.Any(static requirement => string.Equals(requirement.Status, "ReviewRequired", StringComparison.OrdinalIgnoreCase))
                ? "ReviewRequired"
                : "Ready";

        return new MultiAssetClassCoverageDto(
            AssetClass: spec.AssetClass,
            DisplayName: spec.DisplayName,
            Status: status,
            StatusLabel: status == "Ready" ? "Ready" : status == "Blocked" ? "Blocked" : "Review required",
            Summary: spec.Summary,
            EvidenceRequirements: requirements,
            Blockers: blockers,
            LedgerClassification: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["classification"] = spec.LedgerClassification,
                ["postingGate"] = "LedgerPeriodPostingGuard.Validate",
                ["projectors"] = ProjectorsFor(spec.AssetClass)
            },
            ReconciliationSignals: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["breaks"] = string.Join(", ", spec.ReconciliationSignals),
                ["closeReadiness"] = blockers.Count == 0 ? "No hard blocker from definition coverage" : "Close blocked until evidence is retained",
                ["providerEvidence"] = string.Join(", ", spec.ProviderFeeds)
            });
    }

    private static IReadOnlyList<MultiAssetEvidenceRequirementDto> BuildRequirements(
        MultiAssetCoverageSpecification spec,
        bool hasValidator,
        bool hasCatalogDescriptor,
        bool hasProfileCoverage)
    {
        var requirements = new List<MultiAssetEvidenceRequirementDto>
        {
            new(
                $"{spec.AssetClass}:security-master-identifiers",
                $"Security Master identifiers: {string.Join(", ", spec.RequiredIdentifiers)}",
                "SecurityMaster",
                hasCatalogDescriptor ? "Ready" : "Blocked",
                UiApiRoutes.WorkstationSecurityMasterSearch,
                true),
            new(
                $"{spec.AssetClass}:economic-terms",
                $"Economic terms: {string.Join(", ", spec.EconomicTerms)}",
                "SecurityMaster",
                hasValidator ? "Ready" : "Blocked",
                UiApiRoutes.WorkstationSecurityMasterSearch,
                true),
            new(
                $"{spec.AssetClass}:provider-evidence",
                $"Provider evidence feeds: {string.Join(", ", spec.ProviderFeeds)}",
                "ProviderEvidence",
                spec.HardBlocker ? "ReviewRequired" : "Ready",
                UiApiRoutes.WorkstationDataOperations,
                true),
            new(
                $"{spec.AssetClass}:ledger-classification",
                spec.LedgerClassification,
                "Ledger",
                "Ready",
                UiApiRoutes.WorkstationAccounting,
                true),
            new(
                $"{spec.AssetClass}:reconciliation-close",
                $"Reconciliation signals: {string.Join(", ", spec.ReconciliationSignals)}",
                "Reconciliation",
                spec.HardBlocker ? "ReviewRequired" : "Ready",
                UiApiRoutes.ReconciliationRuns,
                true)
        };

        if (RequiresGovernedProfile(spec.AssetClass))
        {
            requirements.Add(new(
                $"{spec.AssetClass}:governed-profile",
                "Approved custom/private asset profile coverage",
                "Governance",
                hasProfileCoverage ? "Ready" : "Blocked",
                UiApiRoutes.SecurityMasterAssetProfiles,
                true));
        }

        return requirements;
    }

    private static IReadOnlyList<MultiAssetReadinessBlockerDto> BuildBlockers(
        MultiAssetCoverageSpecification spec,
        bool hasValidator,
        bool hasCatalogDescriptor,
        bool hasProfileCoverage)
    {
        var blockers = new List<MultiAssetReadinessBlockerDto>();
        if (!hasCatalogDescriptor)
        {
            blockers.Add(new(
                $"{spec.AssetClass}:catalog-missing",
                "Blocker",
                $"{spec.DisplayName} is missing from SecurityAssetClassCatalog.",
                "SecurityMaster",
                UiApiRoutes.WorkstationSecurityMasterSearch));
        }

        if (!hasValidator)
        {
            blockers.Add(new(
                $"{spec.AssetClass}:validator-missing",
                "Blocker",
                $"{spec.DisplayName} is missing asset-class validator rules.",
                "SecurityMaster",
                UiApiRoutes.WorkstationSecurityMasterSearch));
        }

        if (RequiresGovernedProfile(spec.AssetClass) && !hasProfileCoverage)
        {
            blockers.Add(new(
                $"{spec.AssetClass}:profile-missing",
                "Blocker",
                $"{spec.DisplayName} requires an approved governed profile before valuation, ledger, reconciliation, or close can rely on it.",
                "SecurityMaster",
                UiApiRoutes.SecurityMasterAssetProfiles));
        }

        if (spec.HardBlocker)
        {
            blockers.Add(new(
                $"{spec.AssetClass}:provider-evidence-review",
                "Review",
                $"{spec.DisplayName} needs retained provider evidence for {string.Join(", ", spec.ProviderFeeds)} before close readiness can be marked complete.",
                "ProviderEvidence",
                UiApiRoutes.WorkstationPortfolioMultiAssetCoverage));
        }

        return blockers;
    }

    private static string ProjectorsFor(string assetClass)
        => assetClass switch
        {
            "Bond" => "DailyPortfolioPricingProjector, FixedIncomeAmortizationProjector, SecurityMasterAccountingEventService",
            "DirectLoan" => "LoanAccountingProjector, FixedIncomeAmortizationProjector, SecurityMasterAccountingEventService",
            "FxSpot" => "MultiCurrency remeasurement projectors",
            "Option" or "Future" => "DailyPortfolioPricingProjector, LedgerTaxLotReliefProjector",
            "CustomAsset" or "OtherSecurity" => "DailyPortfolioPricingProjector, Security Master accounting-event services",
            _ => "DailyPortfolioPricingProjector, LedgerTaxLotReliefProjector"
        };

    private static bool RequiresGovernedProfile(string assetClass)
        => string.Equals(assetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase);

    private static MultiAssetCoverageSpecification Listed(
        string assetClass,
        string displayName,
        string summary,
        IReadOnlyList<string> requiredIdentifiers,
        IReadOnlyList<string> economicTerms,
        IReadOnlyList<string> providerFeeds,
        string ledgerClassification,
        IReadOnlyList<string> reconciliationSignals,
        bool hardBlocker)
        => new(
            assetClass,
            displayName,
            summary,
            requiredIdentifiers,
            economicTerms,
            providerFeeds,
            ledgerClassification,
            reconciliationSignals,
            hardBlocker);
}

internal sealed record MultiAssetCoverageSpecification(
    string AssetClass,
    string DisplayName,
    string Summary,
    IReadOnlyList<string> RequiredIdentifiers,
    IReadOnlyList<string> EconomicTerms,
    IReadOnlyList<string> ProviderFeeds,
    string LedgerClassification,
    IReadOnlyList<string> ReconciliationSignals,
    bool HardBlocker);
