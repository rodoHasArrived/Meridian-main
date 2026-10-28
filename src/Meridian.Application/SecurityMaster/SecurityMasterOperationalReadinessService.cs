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

/// <summary>
/// Builds multi-asset operational readiness evidence from Security Master coverage.
/// </summary>
public interface ISecurityMasterOperationalReadinessService
{
    Task<MultiAssetCoverageSummaryDto> GetReadinessAsync(
        SecurityMasterOperationalReadinessRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Evaluates Security Master coverage for multi-asset operational readiness views.
/// </summary>
public sealed class SecurityMasterOperationalReadinessService : ISecurityMasterOperationalReadinessService
{
    // Compatibility accessors for existing internal guards; the pack registry owns the metadata.
    internal static IReadOnlyList<MultiAssetCoverageSpecification> Specifications
        => SecurityAssetPackRegistry.ReadinessSpecifications;

    private readonly AssetClassValidatorRegistry _assetClassValidators;
    private readonly ISecurityAssetProfileCatalog _assetProfileCatalog;

    internal static IReadOnlyDictionary<string, IReadOnlyList<AssetClassDepthTargetSpec>> AssetClassDepthTargetSpecs
        => SecurityAssetPackRegistry.ReadinessDepthTargets;

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
                .Where(spec => string.Equals(spec.AssetClass, SecurityAssetPackRegistry.ResolveAssetClass(request.AssetClass), StringComparison.Ordinal))
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
            },
            AssetPacks: SecurityAssetPackRegistry.All.Select(ToAssetPackCoverage).ToArray()));
    }

    private MultiAssetClassCoverageDto BuildCoverageRow(
        MultiAssetCoverageSpecification spec,
        SecurityMasterOperationalEvidenceSnapshot? evidenceSnapshot)
    {
        var hasValidator = _assetClassValidators.TryGetValidator(spec.AssetClass, out _);
        var hasCatalogDescriptor = SecurityAssetClassCatalog.TryGet(spec.AssetClass, out _);
        // GetProfiles() already applies write-time governance: Approved OR Superseded versions
        // whose effective window covers today. A Superseded predecessor stays THE selectable
        // version while its future-dated replacement waits to activate, so re-filtering to
        // Approved here would raise a false profile-missing Blocker for every covered asset class
        // during that transition gap.
        var hasProfileCoverage = !RequiresGovernedProfile(spec.AssetClass)
            || _assetProfileCatalog.GetProfiles().Any(profile =>
                string.Equals(profile.ProfileId, "structured-credit-io-po", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(profile.Category, "PrivateFunds", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(profile.Category, "PrivateEquity", StringComparison.OrdinalIgnoreCase));

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
                ["projectors"] = SecurityAssetPackRegistry.ReadinessProjectorsFor(spec.AssetClass)
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

        if (spec.RequiresOperationalProfileReview)
        {
            requirements.Add(new(
                $"{spec.AssetClass}:operational-profile-review",
                "Asset-specific readiness profile requires review in the asset-pack registry",
                "SecurityMaster",
                "ReviewRequired",
                UiApiRoutes.WorkstationPortfolioMultiAssetCoverage,
                true));
        }

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
        if (!AssetClassDepthTargetSpecs.TryGetValue(spec.AssetClass, out var specs))
        {
            return;
        }

        foreach (var targetSpec in specs)
        {
            targets.Add(CreateDepthTarget(
                spec.AssetClass,
                targetSpec,
                requirements,
                evidence,
                providerRoute,
                ledgerRoute,
                closeRoute));
        }
    }

    private static MultiAssetDrillThroughTargetDto CreateDepthTarget(
        string assetClass,
        AssetClassDepthTargetSpec targetSpec,
        IReadOnlyList<MultiAssetEvidenceRequirementDto> requirements,
        IReadOnlyList<SecurityMasterOperationalEvidenceItem> evidence,
        string providerRoute,
        string ledgerRoute,
        string closeRoute)
    {
        var evidenceLink = BestEvidenceLink(evidence, targetSpec.EvidenceCategory);
        if (targetSpec.FallbackEvidenceToProvider)
        {
            evidenceLink ??= BestEvidenceLink(evidence, "ProviderEvidence");
        }

        var status = targetSpec.UseCloseEvidenceStatus
            ? EvaluateTargetStatus(evidence, "CloseReadiness") ?? RequirementStatus(requirements, "ProviderEvidence")
            : RequirementStatus(requirements, targetSpec.RequirementCategory);

        return new(
            $"{assetClass}:{targetSpec.KeySuffix}",
            targetSpec.TargetType,
            targetSpec.Label,
            targetSpec.RouteKind switch
            {
                AssetClassDepthRouteKind.Provider => providerRoute,
                AssetClassDepthRouteKind.Ledger => ledgerRoute,
                AssetClassDepthRouteKind.Close => closeRoute,
                AssetClassDepthRouteKind.AssetProfiles => UiApiRoutes.SecurityMasterAssetProfiles,
                _ => providerRoute
            },
            evidenceLink,
            status,
            targetSpec.Source);
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
        if (spec.RequiresOperationalProfileReview)
        {
            blockers.Add(new(
                $"{spec.AssetClass}:operational-profile-review",
                "Review",
                $"{spec.DisplayName} uses catalog-derived readiness requirements; declare and review its asset-specific operational profile in SecurityAssetPackRegistry before marking it ready.",
                "SecurityMaster",
                UiApiRoutes.WorkstationPortfolioMultiAssetCoverage));
        }
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

        var expectedClass = SecurityAssetPackRegistry.ResolveAssetClass(expected);
        var actualClass = SecurityAssetPackRegistry.ResolveAssetClass(actual);
        return expectedClass is not null && string.Equals(expectedClass, actualClass, StringComparison.Ordinal);
    }

    private static string NormalizeToken(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool RequiresGovernedProfile(string assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).RequiresGovernedProfile;

    private static MultiAssetPackCoverageDto ToAssetPackCoverage(SecurityAssetPackDescriptor pack)
    {
        var validation = SecurityAssetPackRegistry.ValidateDescriptor(pack);
        return new(
            PackId: pack.PackId,
            DisplayName: pack.DisplayName,
            AssetClasses: pack.AssetClasses,
            ContractSchema: pack.ContractSchema,
            LifecycleEvents: pack.LifecycleEvents,
            LifecycleCoverage: pack.LifecycleCoverage,
            ValuationMethods: pack.ValuationMethods,
            AccountingRules: pack.AccountingRules,
            ValidationRules: pack.ValidationRules,
            ReportingTaxonomy: pack.ReportingTaxonomy,
            AutomationDepth: pack.AutomationDepth.ToString(),
            AdmissionPolicy: pack.AdmissionPolicy,
            LedgerExtensionPolicy: pack.LedgerExtensionPolicy,
            RegistryValidationStatus: validation.IsValid ? "Valid" : "Invalid",
            RegistryValidationIssues: validation.Issues,
            PlannedAssetClasses: pack.PlannedAssetClasses);
    }

}
