using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed record SecurityMasterOperationalReadinessRequest(
    string? FundAccountId = null,
    string? Entity = null,
    string? AssetClass = null,
    SecurityMasterOperationalEvidenceSnapshot? EvidenceSnapshot = null);

public sealed record SecurityMasterOperationalEvidenceSnapshot(
    string? ProviderId,
    string? ExternalAccountId,
    string? ReconciliationStatus,
    string? ReconciliationDetailPath,
    IReadOnlyList<SecurityMasterOperationalEvidenceItem> EvidenceItems);

public sealed record SecurityMasterOperationalEvidenceItem(
    string EvidenceId,
    string Category,
    string EvidenceKind,
    string Status,
    string Source,
    string? AssetClass = null,
    string? Label = null,
    string? EvidenceRoute = null,
    string? EvidenceLink = null,
    string? Reason = null);

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
            "Private credit / loans",
            "Private-credit readiness requires borrower identity, commitment and obligation schedules, accrual policy, principal paydown events, covenant notices, and direct-lending ledger classification.",
            ["Internal code", "Borrower", "LEI or provider symbol when available"],
            ["borrower", "commitment", "unfunded commitment", "principal", "rate", "maturity", "loan schedule", "covenant", "obligation"],
            ["Loan schedule", "Borrower notice", "Commitment schedule", "Unfunded commitment", "Paydown", "Covenant", "Accrual", "Cash", "Collateral", "Valuation"],
            "Loan receivable / unfunded commitment obligation / interest income / fees / realized and unrealized P&L",
            ["quantity", "market value", "cash", "accrual", "loan schedule", "commitment", "paydown", "obligation"],
            hardBlocker: true),
        Listed(
            "CustomAsset",
            "MBS / ABS / CLO / CMBS / private assets",
            "Structured and private assets require governed custom profiles, servicer/trustee reports, factor or NAV evidence, obligation events, valuation approval, and profile-aware ledger classification.",
            ["Internal code", "CUSIP/ISIN/FIGI when available", "Provider symbol"],
            ["approved profile", "profile version", "required profile fields", "valuation date", "servicer/trustee cut-off date", "obligation event"],
            ["Custom profile", "Servicer report", "Trustee report", "Warehouse tape", "Factor schedule", "Dealer pricing", "NAV", "Capital call", "Distribution notice", "Cash", "Collateral", "Obligation schedule"],
            "Profile-derived classification / valuation adjustment / income accrual / commitment and obligation accounting",
            ["quantity", "market value", "cash", "factor schedule", "NAV", "servicer report", "trustee report", "capital call", "distribution", "obligation", "custom-profile evidence"],
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

        var rows = filtered.Select(spec => BuildCoverageRow(spec, request.EvidenceSnapshot)).ToArray();
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
                ["securityMasterProfiles"] = UiApiRoutes.SecurityMasterAssetProfiles,
                ["providerEvidence"] = UiApiRoutes.WorkstationDataOperations,
                ["reconciliation"] = UiApiRoutes.ReconciliationRuns,
                ["ledgerMapping"] = UiApiRoutes.FundStructureLedgerMappingAssignments,
                ["closeReadiness"] = UiApiRoutes.FundAccountCloseReadiness,
                ["assetOperations"] = UiApiRoutes.WorkstationAssetOperations,
                ["coverage"] = UiApiRoutes.WorkstationPortfolioMultiAssetCoverage
            }));
    }

    private MultiAssetClassCoverageDto BuildCoverageRow(
        MultiAssetCoverageSpecification spec,
        SecurityMasterOperationalEvidenceSnapshot? evidenceSnapshot)
    {
        var hasValidator = _assetClassValidators.TryGetValidator(spec.AssetClass, out _);
        var hasCatalogDescriptor = SecurityAssetClassCatalog.GetOrDefault(spec.AssetClass).AssetClass != "Unknown";
        var hasProfileCoverage = !RequiresGovernedProfile(spec.AssetClass)
            || _assetProfileCatalog.GetProfiles().Any(profile =>
                profile.Status == SecurityAssetProfileStatusDto.Approved &&
                (string.Equals(profile.ProfileId, "structured-credit-io-po", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(profile.Category, "PrivateFunds", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(profile.Category, "PrivateEquity", StringComparison.OrdinalIgnoreCase)));

        var evidence = SelectEvidence(spec, evidenceSnapshot);
        var requirements = BuildRequirements(spec, hasValidator, hasCatalogDescriptor, hasProfileCoverage, evidenceSnapshot, evidence);
        var blockers = BuildBlockers(spec, hasValidator, hasCatalogDescriptor, hasProfileCoverage, evidenceSnapshot, evidence);
        var drillThroughTargets = BuildDrillThroughTargets(spec, requirements, blockers, evidence);
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
            DrillThroughTargets: drillThroughTargets,
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
                ["providerEvidence"] = string.Join(", ", spec.ProviderFeeds),
                ["retainedEvidence"] = evidence.Count == 0
                    ? "No retained account-scoped provider evidence linked"
                    : string.Join(", ", evidence.Select(static item => item.EvidenceKind).Distinct(StringComparer.OrdinalIgnoreCase)),
                ["providerStatus"] = evidenceSnapshot?.ReconciliationStatus ?? "Not evaluated"
            });
    }

    private static IReadOnlyList<MultiAssetEvidenceRequirementDto> BuildRequirements(
        MultiAssetCoverageSpecification spec,
        bool hasValidator,
        bool hasCatalogDescriptor,
        bool hasProfileCoverage,
        SecurityMasterOperationalEvidenceSnapshot? evidenceSnapshot,
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence)
    {
        var evidenceExcludingCloseReadiness = evidence
            .Where(static item => !string.Equals(item.Category, "CloseReadiness", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var providerEvidence = evidenceExcludingCloseReadiness
            .Where(static item =>
                string.Equals(item.Category, "ProviderEvidence", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category, "Reconciliation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category, "ShadowBook", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var providerStatus = EvaluateEvidenceStatus(
            spec.ProviderFeeds,
            providerEvidence,
            evidenceSnapshot,
            defaultWhenNoSnapshot: spec.HardBlocker ? "ReviewRequired" : "Ready");
        var ledgerStatus = EvaluateEvidenceStatus(
            [spec.LedgerClassification, .. spec.ReconciliationSignals],
            evidenceExcludingCloseReadiness.Where(static item =>
                    string.Equals(item.Category, "Ledger", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Category, "ShadowBook", StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            evidenceSnapshot,
            defaultWhenNoSnapshot: "Ready");
        var reconciliationStatus = EvaluateReconciliationStatus(spec, evidenceSnapshot, evidenceExcludingCloseReadiness);

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
                providerStatus,
                BestEvidenceRoute(evidence, "ProviderEvidence") ?? UiApiRoutes.WorkstationDataOperations,
                true),
            new(
                $"{spec.AssetClass}:ledger-classification",
                spec.LedgerClassification,
                "Ledger",
                ledgerStatus,
                BestEvidenceRoute(evidence, "Ledger") ?? UiApiRoutes.WorkstationAccounting,
                true),
            new(
                $"{spec.AssetClass}:reconciliation-close",
                $"Reconciliation signals: {string.Join(", ", spec.ReconciliationSignals)}",
                "Reconciliation",
                reconciliationStatus,
                BestEvidenceRoute(evidence, "Reconciliation") ?? UiApiRoutes.ReconciliationRuns,
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

    private static IReadOnlyList<MultiAssetDrillThroughTargetDto> BuildDrillThroughTargets(
        MultiAssetCoverageSpecification spec,
        IReadOnlyList<MultiAssetEvidenceRequirementDto> requirements,
        IReadOnlyList<MultiAssetReadinessBlockerDto> blockers,
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence)
    {
        var providerRoute = RouteForRequirement(requirements, "ProviderEvidence") ?? UiApiRoutes.WorkstationDataOperations;
        var reconciliationRoute = RouteForRequirement(requirements, "Reconciliation") ?? UiApiRoutes.ReconciliationRuns;
        var ledgerRoute = RouteForRequirement(requirements, "Ledger") ?? UiApiRoutes.WorkstationAccounting;
        var closeRoute = BestEvidenceRoute(evidence, "CloseReadiness")
            ?? blockers.FirstOrDefault(static blocker =>
                string.Equals(blocker.Source, "CloseReadiness", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(blocker.Source, "FundAccountCloseReadinessService", StringComparison.OrdinalIgnoreCase))
                ?.EvidenceRoute
            ?? UiApiRoutes.WorkstationPortfolioMultiAssetCoverage;
        var closeStatus = EvaluateTargetStatus(evidence, "CloseReadiness") ??
                          (blockers.Count == 0 ? "Ready" : "ReviewRequired");
        var operationsStatus = requirements.Any(static requirement =>
                !string.Equals(requirement.Status, "Ready", StringComparison.OrdinalIgnoreCase))
            ? "ReviewRequired"
            : "Ready";

        var targets = new List<MultiAssetDrillThroughTargetDto>
        {
            new(
                $"{spec.AssetClass}:security-master-passport",
                "SecurityMasterPassport",
                "Security Master passport/profile",
                RequiresGovernedProfile(spec.AssetClass) ? UiApiRoutes.SecurityMasterAssetProfiles : UiApiRoutes.WorkstationSecurityMasterSearch,
                BestEvidenceLink(evidence, "SecurityMaster"),
                RequirementStatus(requirements, "SecurityMaster"),
                "SecurityMaster"),
            new(
                $"{spec.AssetClass}:provider-evidence",
                "ProviderEvidence",
                "Provider evidence",
                providerRoute,
                BestEvidenceLink(evidence, "ProviderEvidence"),
                RequirementStatus(requirements, "ProviderEvidence"),
                "ProviderLedgerReconciliation"),
            new(
                $"{spec.AssetClass}:reconciliation-case",
                "ReconciliationCase",
                "Reconciliation break/case",
                reconciliationRoute,
                BestEvidenceLink(evidence, "Reconciliation"),
                RequirementStatus(requirements, "Reconciliation"),
                "ProviderLedgerReconciliation"),
            new(
                $"{spec.AssetClass}:ledger-mapping",
                "LedgerMapping",
                "Ledger mapping/evidence",
                ledgerRoute,
                BestEvidenceLink(evidence, "Ledger"),
                RequirementStatus(requirements, "Ledger"),
                "LedgerPeriodPostingGuard"),
            new(
                $"{spec.AssetClass}:close-readiness",
                "CloseReadiness",
                "Close readiness",
                closeRoute,
                BestEvidenceLink(evidence, "CloseReadiness"),
                closeStatus,
                "FundAccountCloseReadinessService"),
            new(
                $"{spec.AssetClass}:asset-operations",
                "AssetOperations",
                "Asset operations detail",
                UiApiRoutes.WorkstationAssetOperations,
                BestEvidenceLink(evidence, "SecurityMaster") ?? BestEvidenceLink(evidence, "ProviderEvidence"),
                operationsStatus,
                "AssetOperationsReadService")
        };

        AddAssetClassDepthTargets(targets, spec, requirements, evidence, providerRoute, ledgerRoute, closeRoute);

        return targets;
    }

    private static void AddAssetClassDepthTargets(
        List<MultiAssetDrillThroughTargetDto> targets,
        MultiAssetCoverageSpecification spec,
        IReadOnlyList<MultiAssetEvidenceRequirementDto> requirements,
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence,
        string providerRoute,
        string ledgerRoute,
        string closeRoute)
    {
        if (string.Equals(spec.AssetClass, "DirectLoan", StringComparison.OrdinalIgnoreCase))
        {
            var providerStatus = RequirementStatus(requirements, "ProviderEvidence");
            targets.Add(new(
                $"{spec.AssetClass}:loan-schedule-evidence",
                "LoanScheduleEvidence",
                "Loan schedule and borrower notices",
                providerRoute,
                BestEvidenceLink(evidence, "ProviderEvidence"),
                providerStatus,
                "ProviderLedgerReconciliation"));
            targets.Add(new(
                $"{spec.AssetClass}:commitment-covenant-evidence",
                "CommitmentCovenantEvidence",
                "Commitment, unfunded commitment, and covenant evidence",
                providerRoute,
                BestEvidenceLink(evidence, "ProviderEvidence"),
                providerStatus,
                "ProviderLedgerReconciliation"));
            targets.Add(new(
                $"{spec.AssetClass}:paydown-obligation-ledger",
                "PaydownObligationLedger",
                "Paydown and obligation ledger support",
                ledgerRoute,
                BestEvidenceLink(evidence, "Ledger") ?? BestEvidenceLink(evidence, "ProviderEvidence"),
                RequirementStatus(requirements, "Ledger"),
                "LoanAccountingProjector"));
        }

        if (string.Equals(spec.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase))
        {
            var governanceStatus = RequirementStatus(requirements, "Governance");
            var providerStatus = RequirementStatus(requirements, "ProviderEvidence");
            targets.Add(new(
                $"{spec.AssetClass}:profile-lineage",
                "AssetProfileLineage",
                "Approved profile lineage",
                UiApiRoutes.SecurityMasterAssetProfiles,
                BestEvidenceLink(evidence, "SecurityMaster"),
                governanceStatus,
                "SecurityAssetProfileGovernanceService"));
            targets.Add(new(
                $"{spec.AssetClass}:servicer-trustee-evidence",
                "ServicerTrusteeEvidence",
                "Servicer, trustee, warehouse, and factor evidence",
                providerRoute,
                BestEvidenceLink(evidence, "ProviderEvidence"),
                providerStatus,
                "ProviderLedgerReconciliation"));
            targets.Add(new(
                $"{spec.AssetClass}:valuation-nav-evidence",
                "StructuredValuationEvidence",
                "NAV, dealer pricing, capital call, and distribution evidence",
                providerRoute,
                BestEvidenceLink(evidence, "ProviderEvidence"),
                providerStatus,
                "ProviderLedgerReconciliation"));
            targets.Add(new(
                $"{spec.AssetClass}:obligation-close-evidence",
                "ObligationCloseEvidence",
                "Obligation schedule and close-readiness evidence",
                closeRoute,
                BestEvidenceLink(evidence, "CloseReadiness") ?? BestEvidenceLink(evidence, "ProviderEvidence"),
                EvaluateTargetStatus(evidence, "CloseReadiness") ?? providerStatus,
                "FundAccountCloseReadinessService"));
        }
    }

    private static IReadOnlyList<MultiAssetReadinessBlockerDto> BuildBlockers(
        MultiAssetCoverageSpecification spec,
        bool hasValidator,
        bool hasCatalogDescriptor,
        bool hasProfileCoverage,
        SecurityMasterOperationalEvidenceSnapshot? evidenceSnapshot,
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence)
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

        var evidenceGaps = evidenceSnapshot is null
            ? []
            : evidence
                .Where(static item => !string.Equals(EvaluateEvidenceItemStatus(item.Status), "Ready", StringComparison.OrdinalIgnoreCase))
                .GroupBy(static item => item.EvidenceKind, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();
        foreach (var gap in evidenceGaps)
        {
            blockers.Add(new(
                $"{spec.AssetClass}:retained-evidence:{NormalizeToken(gap.EvidenceKind)}",
                string.Equals(EvaluateEvidenceItemStatus(gap.Status), "Blocked", StringComparison.OrdinalIgnoreCase) ? "Blocker" : "Review",
                string.IsNullOrWhiteSpace(gap.Reason)
                    ? $"{spec.DisplayName} retained evidence for {gap.EvidenceKind} is {gap.Status}."
                    : gap.Reason!,
                gap.Category,
                gap.EvidenceRoute));
        }

        if (spec.HardBlocker && (evidenceSnapshot is null || evidence.Count == 0))
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

    private static IReadOnlyList<SecurityMasterOperationalEvidenceItem> SelectEvidence(
        MultiAssetCoverageSpecification spec,
        SecurityMasterOperationalEvidenceSnapshot? evidenceSnapshot)
    {
        if (evidenceSnapshot?.EvidenceItems is null || evidenceSnapshot.EvidenceItems.Count == 0)
        {
            return [];
        }

        return evidenceSnapshot.EvidenceItems
            .Where(item => MatchesAssetClass(spec.AssetClass, item.AssetClass) &&
                           (MatchesAny(item, spec.ProviderFeeds) ||
                            MatchesAny(item, spec.ReconciliationSignals) ||
                            MatchesText(item, spec.LedgerClassification) ||
                            string.Equals(item.Category, "SecurityMaster", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.Category, "Reconciliation", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.Category, "CloseReadiness", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string EvaluateEvidenceStatus(
        IReadOnlyList<string> expected,
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence,
        SecurityMasterOperationalEvidenceSnapshot? evidenceSnapshot,
        string defaultWhenNoSnapshot)
    {
        if (evidenceSnapshot is null)
        {
            return defaultWhenNoSnapshot;
        }

        if (evidence.Count == 0)
        {
            return "ReviewRequired";
        }

        if (evidence.Any(static item => string.Equals(EvaluateEvidenceItemStatus(item.Status), "Blocked", StringComparison.OrdinalIgnoreCase)))
        {
            return "Blocked";
        }

        if (evidence.Any(static item => string.Equals(EvaluateEvidenceItemStatus(item.Status), "ReviewRequired", StringComparison.OrdinalIgnoreCase)))
        {
            return "ReviewRequired";
        }

        var matchedExpectedCount = expected
            .Select(NormalizeToken)
            .Where(static token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(token => evidence.Any(item => MatchesToken(item, token)));

        return matchedExpectedCount == 0 ? "ReviewRequired" : "Ready";
    }

    private static string EvaluateReconciliationStatus(
        MultiAssetCoverageSpecification spec,
        SecurityMasterOperationalEvidenceSnapshot? evidenceSnapshot,
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence)
    {
        if (evidenceSnapshot is null)
        {
            return spec.HardBlocker ? "ReviewRequired" : "Ready";
        }

        if (string.Equals(evidenceSnapshot.ReconciliationStatus, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "Blocked";
        }

        if (string.Equals(evidenceSnapshot.ReconciliationStatus, "Breaks", StringComparison.OrdinalIgnoreCase))
        {
            return "ReviewRequired";
        }

        return EvaluateEvidenceStatus(spec.ReconciliationSignals, evidence, evidenceSnapshot, "ReviewRequired");
    }

    private static string EvaluateEvidenceItemStatus(string status)
        => status switch
        {
            "Matched" or "Ready" or "Resolved" => "Ready",
            "Blocked" or "Unresolved" => "Blocked",
            _ => "ReviewRequired"
        };

    private static string? BestEvidenceRoute(
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence,
        string category)
        => evidence.FirstOrDefault(item =>
                string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.EvidenceRoute))
            ?.EvidenceRoute;

    private static string? BestEvidenceLink(
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence,
        string category)
        => evidence.FirstOrDefault(item =>
                string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.EvidenceLink))
            ?.EvidenceLink;

    private static string? RouteForRequirement(
        IReadOnlyList<MultiAssetEvidenceRequirementDto> requirements,
        string category)
        => requirements.FirstOrDefault(requirement =>
                string.Equals(requirement.Category, category, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(requirement.EvidenceRoute))
            ?.EvidenceRoute;

    private static string RequirementStatus(
        IReadOnlyList<MultiAssetEvidenceRequirementDto> requirements,
        string category)
        => requirements
               .Where(requirement => string.Equals(requirement.Category, category, StringComparison.OrdinalIgnoreCase))
               .Select(static requirement => requirement.Status)
               .OrderBy(static status => status switch
               {
                   "Blocked" => 0,
                   "ReviewRequired" => 1,
                   "Degraded" => 2,
                   "Ready" => 3,
                   _ => 2
               })
               .FirstOrDefault()
           ?? "ReviewRequired";

    private static string? EvaluateTargetStatus(
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence,
        string category)
    {
        var statuses = evidence
            .Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(static item => EvaluateEvidenceItemStatus(item.Status))
            .ToArray();

        if (statuses.Length == 0)
        {
            return null;
        }

        return statuses.Contains("Blocked", StringComparer.OrdinalIgnoreCase)
            ? "Blocked"
            : statuses.Contains("ReviewRequired", StringComparer.OrdinalIgnoreCase)
                ? "ReviewRequired"
                : "Ready";
    }

    private static bool MatchesAny(SecurityMasterOperationalEvidenceItem item, IReadOnlyList<string> values)
        => values.Any(value => MatchesText(item, value));

    private static bool MatchesText(SecurityMasterOperationalEvidenceItem item, string value)
    {
        var token = NormalizeToken(value);
        return token.Length > 0 && MatchesToken(item, token);
    }

    private static bool MatchesToken(SecurityMasterOperationalEvidenceItem item, string token)
        => NormalizeToken(item.EvidenceKind).Contains(token, StringComparison.OrdinalIgnoreCase) ||
           NormalizeToken(item.Category).Contains(token, StringComparison.OrdinalIgnoreCase) ||
           NormalizeToken(item.Label).Contains(token, StringComparison.OrdinalIgnoreCase) ||
           NormalizeToken(item.Source).Contains(token, StringComparison.OrdinalIgnoreCase) ||
           NormalizeToken(item.Reason).Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAssetClass(string expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return true;
        }

        var normalizedExpected = NormalizeToken(expected);
        var normalizedActual = NormalizeToken(actual);
        if (normalizedExpected == normalizedActual)
        {
            return true;
        }

        return normalizedExpected switch
        {
            "bond" => normalizedActual is "fixedincome" or "fixedincomesecurity" or "debt" or "mbs" or "abs" or "clo" or "cmbs",
            "directloan" => normalizedActual is "loan" or "directloan" or "privatecredit",
            "customasset" => normalizedActual is "structuredproduct" or "structuredcredit" or "privateasset" or "privatefund" or "privateequity" or "mbs" or "abs" or "clo" or "cmbs" or "customasset",
            "othersecurity" => normalizedActual is "other" or "othersecurity" or "customasset",
            "fxspot" => normalizedActual is "fx" or "foreignexchange" or "currency",
            _ => false
        };
    }

    private static string NormalizeToken(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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
