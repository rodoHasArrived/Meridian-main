using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Composes the selected-security trust snapshot used by the Security Master workstation.
/// The endpoint is selection-scoped and additive, so the heavier downstream checks only run
/// when the operator explicitly loads or refreshes a security drill-in.
/// </summary>
public sealed class SecurityMasterWorkbenchQueryService : ISecurityMasterWorkbenchQueryService
{
    private readonly Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService _queryService;
    private readonly ISecurityValidationService _validationService;
    private readonly ISecurityMasterConflictService _conflictService;
    private readonly ISecurityMasterIngestStatusService _ingestStatusService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly LedgerReadService _ledgerReadService;
    private readonly IReconciliationRunService? _reconciliationRunService;
    private readonly ReportGenerationService _reportGenerationService;
    private readonly ISecurityMasterPricingService? _pricingService;
    private readonly ISecurityMasterCashFlowService? _cashFlowService;
    private readonly IDataVendorEntitlementService? _entitlementService;
    private readonly ISecurityMasterDataQualityService? _dataQualityService;
    private readonly IFundProfileTenancyRegistry? _tenancyRegistry;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public SecurityMasterWorkbenchQueryService(
        Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService queryService,
        ISecurityValidationService validationService,
        ISecurityMasterConflictService conflictService,
        ISecurityMasterIngestStatusService ingestStatusService,
        IStrategyRepository strategyRepository,
        PortfolioReadService portfolioReadService,
        LedgerReadService ledgerReadService,
        ReportGenerationService reportGenerationService,
        IReconciliationRunService? reconciliationRunService = null,
        ISecurityMasterPricingService? pricingService = null,
        ISecurityMasterCashFlowService? cashFlowService = null,
        IDataVendorEntitlementService? entitlementService = null,
        ISecurityMasterDataQualityService? dataQualityService = null,
        IFundProfileTenancyRegistry? tenancyRegistry = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _conflictService = conflictService ?? throw new ArgumentNullException(nameof(conflictService));
        _ingestStatusService = ingestStatusService ?? throw new ArgumentNullException(nameof(ingestStatusService));
        _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _ledgerReadService = ledgerReadService ?? throw new ArgumentNullException(nameof(ledgerReadService));
        _reportGenerationService = reportGenerationService ?? throw new ArgumentNullException(nameof(reportGenerationService));
        _reconciliationRunService = reconciliationRunService;
        _pricingService = pricingService;
        _cashFlowService = cashFlowService;
        _entitlementService = entitlementService;
        _dataQualityService = dataQualityService;
        _tenancyRegistry = tenancyRegistry;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var nowUtc = DateTimeOffset.UtcNow;

        // Tenant isolation (SEC-005): sanitize the operator-supplied fund scope ONCE here so every
        // fund-scoped evidence path the snapshot and its instrument passport fan out to — downstream
        // impact, open lots, Clearwater pricing (golden-copy/hierarchy), and entitlement applicability —
        // is unscoped for a fund owned by another tenant, not just runs/lots. Own/blank funds and registry
        // uncertainty pass through unchanged (fail open to the single-company-per-deployment boundary).
        fundProfileId = await SanitizeFundScopeAsync(fundProfileId, ct).ConfigureAwait(false);

        var detailTask = _queryService.GetByIdAsync(securityId, ct);
        var historyTask = _queryService.GetHistoryAsync(new SecurityHistoryRequest(securityId, 50), ct);
        var economicTask = _queryService.GetEconomicDefinitionByIdAsync(securityId, ct);
        var tradingTask = _queryService.GetTradingParametersAsync(securityId, nowUtc, ct);
        var corporateActionsTask = _queryService.GetCorporateActionsAsync(securityId, ct);
        var conflictsTask = _conflictService.GetOpenConflictsAsync(ct);
        var validationTask = _validationService.ValidateSecurityAsync(securityId, ct);

        await Task.WhenAll(
            detailTask,
            historyTask,
            economicTask,
            tradingTask,
            corporateActionsTask,
            conflictsTask,
            validationTask).ConfigureAwait(false);

        var detail = await detailTask.ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var history = (await historyTask.ConfigureAwait(false))
            .OrderByDescending(static item => item.EventTimestamp)
            .ToArray();
        var economic = await economicTask.ConfigureAwait(false);
        var trading = await tradingTask.ConfigureAwait(false);
        var corporateActions = (await corporateActionsTask.ConfigureAwait(false))
            .OrderByDescending(static action => action.ExDate)
            .ToArray();
        var selectedConflicts = (await conflictsTask.ConfigureAwait(false))
            .Where(conflict =>
                conflict.SecurityId == securityId &&
                string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static conflict => conflict.DetectedAt)
            .ToArray();
        var validationReport = await validationTask.ConfigureAwait(false);

        var winningSource = ParseWinningSource(economic?.Provenance);
        var downstreamImpact = await BuildDownstreamImpactAsync(detail, fundProfileId, ct).ConfigureAwait(false);
        var assessments = selectedConflicts
            .Select(conflict => AssessConflict(conflict, detail, economic, trading, winningSource, downstreamImpact))
            .ToArray();
        var trustPosture = BuildTrustPosture(economic, trading, corporateActions, assessments, winningSource, validationReport);
        var provenanceCandidates = BuildProvenanceCandidates(detail, winningSource, assessments);
        var recommendedActions = BuildRecommendedActions(detail, trustPosture, assessments, downstreamImpact, validationReport);
        var identifierSummary = BuildIdentifierSummary(detail);
        var schemaCompatibility = BuildSchemaCompatibility(detail, economic);
        var changeHistory = BuildChangeHistory(history);
        var scheduleSummary = BuildScheduleSummary(detail, economic, corporateActions, winningSource);
        var lotModel = BuildLotModel(detail, economic, trading);
        var scheduleBook = BuildScheduleBook(detail, economic, corporateActions, history, winningSource, scheduleSummary);
        var openLotReadModel = await BuildOpenLotReadModelAsync(detail, economic, trading, fundProfileId, nowUtc, ct).ConfigureAwait(false);
        var corporateActionDescriptors = BuildCorporateActionDescriptors(corporateActions, nowUtc);

        var snapshot = new SecurityMasterTrustSnapshotDto(
            SecurityId: detail.SecurityId,
            Security: MapToWorkstationSecurity(detail, economic),
            Identity: new SecurityIdentityDrillInDto(
                SecurityId: detail.SecurityId,
                DisplayName: detail.DisplayName,
                AssetClass: detail.AssetClass,
                Status: detail.Status,
                Version: detail.Version,
                EffectiveFrom: detail.EffectiveFrom,
                EffectiveTo: detail.EffectiveTo,
                Identifiers: detail.Identifiers,
                Aliases: detail.Aliases,
                IssuerName: TryGetJsonString(detail.CommonTerms, "issuerName"),
                CountryOfRisk: TryGetJsonString(detail.CommonTerms, "countryOfRisk"),
                PrimaryListingMic: TryGetJsonString(detail.CommonTerms, "primaryListingMic"),
                SettlementCycleDays: TryGetJsonInt(detail.CommonTerms, "settlementCycleDays")),
            EconomicDefinition: MapToEconomicDefinition(detail, economic, winningSource),
            TrustPosture: trustPosture,
            ProvenanceCandidates: provenanceCandidates,
            ConflictAssessments: assessments,
            DownstreamImpact: downstreamImpact,
            RecommendedActions: recommendedActions,
            History: history,
            CorporateActions: corporateActions,
            RetrievedAtUtc: nowUtc)
        {
            ValidationReport = validationReport,
            IdentifierSummary = identifierSummary,
            SchemaCompatibility = schemaCompatibility,
            ChangeHistory = changeHistory,
            ScheduleSummary = scheduleSummary,
            LotModel = lotModel,
            ScheduleBook = scheduleBook,
            OpenLotReadModel = openLotReadModel,
            CorporateActionDescriptors = corporateActionDescriptors
        };

        return snapshot with
        {
            InstrumentPassport = await BuildInstrumentPassportAsync(snapshot, trading, fundProfileId, ct).ConfigureAwait(false)
        };
    }

    public async Task<InstrumentPassportDto?> GetInstrumentPassportAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        var snapshot = await GetTrustSnapshotAsync(securityId, fundProfileId, ct).ConfigureAwait(false);
        return snapshot?.InstrumentPassport;
    }

    public async Task<SecurityMasterOperatingModelDto?> GetOperatingModelAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        var passport = await GetInstrumentPassportAsync(securityId, fundProfileId, ct).ConfigureAwait(false);
        return passport?.OperatingModel;
    }

    /// <summary>
    /// Projects the raw corporate-action rows into canonical-taxonomy descriptors: supersede
    /// chains collapse to their tips via <see cref="CorporateActionEffectiveStateProjector"/>,
    /// catalog metadata supplies the display identity, and lifecycle states resolve at the
    /// snapshot's as-of time. Event types outside the catalog fail open to their raw string
    /// with no CAEV alignment so unknown provider vocab never drops rows from the workbench.
    /// </summary>
    private static IReadOnlyList<CorporateActionDescriptorDto> BuildCorporateActionDescriptors(
        IReadOnlyList<CorporateActionDto> corporateActions,
        DateTimeOffset asOf)
    {
        return CorporateActionEffectiveStateProjector.Project(corporateActions, asOf)
            .Select(static state =>
            {
                var descriptor = CorporateActionTypeDescriptorCatalog.Find(state.Effective.EventType);
                return new CorporateActionDescriptorDto(
                    CorpActId: state.Effective.CorpActId,
                    CanonicalName: state.Effective.EventType,
                    CaevCode: descriptor?.CaevCode,
                    DisplayName: descriptor?.DisplayName ?? state.Effective.EventType,
                    LifecycleState: state.LifecycleState,
                    IsCancelled: state.IsCancelled,
                    Timeline: state.Timeline
                        .Select(static entry => new CorporateActionTimelineEntryDto(
                            CorpActId: entry.CorpActId,
                            LifecycleState: entry.LifecycleState ?? CorporateActionLifecycleStates.Confirmed,
                            ExDate: entry.ExDate,
                            PayDate: entry.PayDate,
                            IsAmendment: entry.SupersedesCorpActId.HasValue))
                        .ToArray());
            })
            .ToArray();
    }

    private async Task<InstrumentPassportDto> BuildInstrumentPassportAsync(
        SecurityMasterTrustSnapshotDto snapshot,
        TradingParametersDto? tradingParameters,
        string? fundProfileId,
        CancellationToken ct)
    {
        var identifierSummary = snapshot.IdentifierSummary ?? BuildFallbackIdentifierSummary(snapshot.Identity);
        var lifecycleEvents = snapshot.ChangeHistory
            ?? snapshot.History
                .OrderByDescending(static item => item.EventTimestamp)
                .Select(MapHistoryToLifecycleEvent)
                .ToArray();
        var pricing = BuildPassportPricing(snapshot.TrustPosture, tradingParameters);
        var providerConfidence = BuildProviderConfidence(snapshot, identifierSummary, lifecycleEvents);
        var clearwaterEvidence = await BuildClearwaterEvidenceAsync(
            snapshot.SecurityId,
            fundProfileId,
            ct).ConfigureAwait(false);
        var operatingModel = BuildOperatingModel(
            snapshot,
            identifierSummary,
            fundProfileId,
            providerConfidence,
            lifecycleEvents,
            clearwaterEvidence);
        var referenceDataWorkbench = BuildReferenceDataWorkbench(
            snapshot,
            identifierSummary,
            providerConfidence,
            pricing,
            operatingModel);
        var classificationProfile = BuildClassificationProfile(snapshot);

        return new InstrumentPassportDto(
            SecurityId: snapshot.SecurityId,
            Identity: snapshot.Identity,
            EconomicDefinition: snapshot.EconomicDefinition,
            IdentifierSummary: identifierSummary,
            ProviderMappings: identifierSummary.ProviderMappings,
            LifecycleEvents: lifecycleEvents,
            CorporateActions: snapshot.CorporateActions,
            Pricing: pricing,
            Usage: snapshot.DownstreamImpact,
            TrustPosture: snapshot.TrustPosture,
            RetrievedAtUtc: snapshot.RetrievedAtUtc)
        {
            ProviderConfidence = providerConfidence,
            OperatingModel = operatingModel,
            ReferenceDataWorkbench = referenceDataWorkbench,
            OperationsWorkbench = BuildOperationsWorkbench(
                snapshot,
                identifierSummary,
                providerConfidence,
                pricing,
                referenceDataWorkbench,
                classificationProfile),
            ClassificationProfile = classificationProfile
        };
    }

    private static InstrumentPassportReferenceDataWorkbenchDto BuildReferenceDataWorkbench(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence,
        InstrumentPassportPricingDto pricing,
        SecurityMasterOperatingModelDto operatingModel)
    {
        var openIdentifierConflictCount = providerConfidence.Sum(static row => row.IdentifierConflictIds.Count);
        var activeProviderEvidenceCount = providerConfidence.Count(static row => row.IsActive);
        var hasUsableIdentifierConfidence =
            identifierSummary.HasPrimaryIdentifier &&
            identifierSummary.HasProviderMappings &&
            activeProviderEvidenceCount > 0 &&
            openIdentifierConflictCount == 0;
        var hasObligationEvidence =
            snapshot.CorporateActions.Count > 0 ||
            snapshot.ScheduleBook?.Events.Count > 0 ||
            snapshot.ScheduleSummary?.HasEconomicScheduleTerms == true;
        var hasCashFlowReadiness =
            snapshot.ScheduleSummary?.SupportsCashflowSchedule == true ||
            snapshot.ScheduleBook?.SupportsCashflowSchedule == true ||
            snapshot.CorporateActions.Count > 0;
        var hasLedgerClassification =
            !string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.AssetClass) &&
            !string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.Currency);
        var operationsHandoffEvidenceCount = Math.Max(
            1,
            snapshot.RecommendedActions.Count + snapshot.DownstreamImpact.Links.Count);

        var sections = new List<InstrumentPassportReferenceDataWorkbenchSectionDto>
        {
            new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "provider-evidence",
                Title: "Provider evidence",
                Status: activeProviderEvidenceCount > 0 ? "Ready" : "Review",
                Summary: activeProviderEvidenceCount > 0
                    ? $"{activeProviderEvidenceCount} active provider evidence row(s) retained on the passport."
                    : "No active provider evidence rows are retained on the passport.",
                EvidenceCount: providerConfidence.Count,
                BlockingIssueCount: openIdentifierConflictCount),
            new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "identifier-confidence",
                Title: "Identifier confidence",
                Status: hasUsableIdentifierConfidence ? "Ready" : "Review",
                Summary: identifierSummary.Summary,
                EvidenceCount: identifierSummary.ActiveIdentifierCount + identifierSummary.ActiveAliasCount + identifierSummary.ProviderMappingCount,
                BlockingIssueCount: openIdentifierConflictCount),
            new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "terms-obligations",
                Title: "Terms and obligations",
                Status: hasObligationEvidence ? "Ready" : "Review",
                Summary: BuildTermsAndObligationsSummary(snapshot),
                EvidenceCount: snapshot.CorporateActions.Count + (snapshot.ScheduleBook?.Events.Count ?? 0),
                BlockingIssueCount: snapshot.TrustPosture.HasOpenConflicts ? snapshot.TrustPosture.OpenConflictCount : 0),
            new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "cash-flow-readiness",
                Title: "Projected cash-flow readiness",
                Status: hasCashFlowReadiness ? "Ready" : "Review",
                Summary: snapshot.ScheduleSummary?.Summary ?? pricing.Summary,
                EvidenceCount: (snapshot.ScheduleBook?.Events.Count ?? 0) + snapshot.CorporateActions.Count,
                BlockingIssueCount: hasCashFlowReadiness ? 0 : 1),
            new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "ledger-classification",
                Title: "Ledger classification",
                Status: hasLedgerClassification ? "Ready" : "Review",
                Summary: BuildLedgerClassificationSummary(snapshot),
                EvidenceCount: snapshot.DownstreamImpact.LedgerExposureCount + (hasLedgerClassification ? 1 : 0),
                BlockingIssueCount: hasLedgerClassification ? 0 : 1),
            new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "operations-handoff",
                Title: "Operations handoff",
                Status: "Ready",
                Summary: snapshot.DownstreamImpact.Summary,
                EvidenceCount: operationsHandoffEvidenceCount,
                BlockingIssueCount: snapshot.RecommendedActions.Count(static action => !action.IsEnabled))
        };

        sections.AddRange(operatingModel.Controls);

        var status = sections.Any(static section => section.BlockingIssueCount > 0 || section.Status.Equals("Review", StringComparison.OrdinalIgnoreCase))
            ? "Review"
            : "Ready";
        var handoffs = BuildOperationsHandoffs(snapshot);

        return new InstrumentPassportReferenceDataWorkbenchDto(
            Status: status,
            Summary: status.Equals("Ready", StringComparison.OrdinalIgnoreCase)
                ? "Multi-asset reference-data workbench is ready for downstream FINOPS use."
                : "Multi-asset reference-data workbench needs review before downstream FINOPS use.",
            Sections: sections,
            OperationsHandoffs: handoffs);
    }

    private static InstrumentPassportOperationsWorkbenchDto BuildOperationsWorkbench(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence,
        InstrumentPassportPricingDto pricing,
        InstrumentPassportReferenceDataWorkbenchDto referenceDataWorkbench,
        InstrumentPassportClassificationProfileDto? classificationProfile)
    {
        var handoffs = referenceDataWorkbench.OperationsHandoffs;
        var readiness = BuildOperationsReadiness(snapshot, identifierSummary, providerConfidence, pricing, handoffs);
        var panels = new List<InstrumentPassportOperationsWorkbenchPanelDto>
        {
            BuildIdentityPanel(snapshot, identifierSummary, providerConfidence),
            BuildProviderEvidencePanel(snapshot, providerConfidence),
            BuildTermsPanel(snapshot, pricing, classificationProfile),
            BuildReadinessPanel(readiness),
            BuildHandoffPanel(snapshot, handoffs)
        };
        var status = panels.Any(PanelNeedsReview) || readiness.Any(static item => !item.IsReady)
            ? "Review"
            : "Ready";

        return new InstrumentPassportOperationsWorkbenchDto(
            Status: status,
            Summary: status.Equals("Ready", StringComparison.OrdinalIgnoreCase)
                ? "Security Master operations workbench is ready for downstream portfolio, accounting, reconciliation, close, and reporting use."
                : "Security Master operations workbench needs review before downstream portfolio, accounting, reconciliation, close, or reporting use.",
            Panels: panels,
            Readiness: readiness,
            Handoffs: handoffs);
    }

    private static InstrumentPassportOperationsWorkbenchPanelDto BuildIdentityPanel(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence)
    {
        var conflictCount = providerConfidence.Sum(static row => row.IdentifierConflictIds.Count);
        var items = new List<InstrumentPassportOperationsWorkbenchItemDto>
        {
            new(
                ItemId: "canonical-id",
                Label: "Canonical ID",
                Value: snapshot.SecurityId.ToString("D"),
                Status: "Ready",
                Detail: SecurityMasterText(snapshot.Identity.DisplayName, "Security Master identity is retained."),
                EvidenceCount: 1,
                BlockingIssueCount: 0),
            new(
                ItemId: "primary-identifier",
                Label: SecurityMasterText(identifierSummary.PrimaryIdentifierKind, "Primary identifier"),
                Value: SecurityMasterText(identifierSummary.PrimaryIdentifierValue, "Unavailable"),
                Status: identifierSummary.HasPrimaryIdentifier ? "Ready" : "Review",
                Detail: identifierSummary.Summary,
                EvidenceCount: identifierSummary.ActiveIdentifierCount,
                BlockingIssueCount: identifierSummary.HasPrimaryIdentifier ? 0 : 1),
            new(
                ItemId: "aliases-provider-ids",
                Label: "Aliases and provider IDs",
                Value: $"{identifierSummary.ActiveAliasCount} alias(es); {identifierSummary.ProviderMappingCount} provider mapping(s)",
                Status: identifierSummary.HasProviderMappings ? "Ready" : "Review",
                Detail: $"{identifierSummary.DistinctProviderCount} distinct provider(s) contribute identity evidence.",
                EvidenceCount: identifierSummary.ActiveAliasCount + identifierSummary.ProviderMappingCount,
                BlockingIssueCount: identifierSummary.HasProviderMappings ? 0 : 1),
            new(
                ItemId: "duplicate-candidates",
                Label: "Duplicate candidates",
                Value: conflictCount == 0 ? "No open identifier conflicts" : $"{conflictCount} identifier conflict(s)",
                Status: conflictCount == 0 ? "Ready" : "Review",
                Detail: conflictCount == 0
                    ? "No provider identifier conflicts are blocking canonical identity confidence."
                    : "Provider identifier conflicts require steward review before downstream consumers trust the asset.",
                EvidenceCount: providerConfidence.Count,
                BlockingIssueCount: conflictCount)
        };

        return BuildOperationsPanel("identity", "Identity", items);
    }

    private static InstrumentPassportOperationsWorkbenchPanelDto BuildProviderEvidencePanel(
        SecurityMasterTrustSnapshotDto snapshot,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence)
    {
        var activeCount = providerConfidence.Count(static row => row.IsActive);
        var items = providerConfidence
            .Select((row, index) => new InstrumentPassportOperationsWorkbenchItemDto(
                ItemId: $"provider-{index + 1}",
                Label: row.Provider,
                Value: $"{row.MappingKind}: {row.Symbol}",
                Status: row.IdentifierConflictIds.Count == 0 && row.IsActive ? "Ready" : "Review",
                Detail: BuildProviderEvidenceDetail(row),
                EvidenceCount: 1 + row.OverrideHistory.Count,
                BlockingIssueCount: row.IdentifierConflictIds.Count))
            .ToList();

        if (items.Count == 0)
        {
            items.Add(new InstrumentPassportOperationsWorkbenchItemDto(
                ItemId: "provider-evidence-missing",
                Label: "Provider evidence",
                Value: "Unavailable",
                Status: "Review",
                Detail: "No provider, administrator, custodian, or retained source evidence is attached to this passport.",
                EvidenceCount: 0,
                BlockingIssueCount: 1));
        }

        items.Insert(0, new InstrumentPassportOperationsWorkbenchItemDto(
            ItemId: "active-source-count",
            Label: "Active sources",
            Value: $"{activeCount} active / {providerConfidence.Count} total",
            Status: activeCount > 0 ? "Ready" : "Review",
            Detail: "Provider, administrator, custodian, and retained source rows are reviewed before downstream handoff.",
            EvidenceCount: providerConfidence.Count,
            BlockingIssueCount: activeCount > 0 ? 0 : 1));
        items.AddRange(BuildSourceEvidenceItems(snapshot));
        items.AddRange(BuildConflictItems(snapshot, "provider-conflict", IsProviderOrIdentifierConflict));

        return BuildOperationsPanel("provider-evidence", "Provider evidence", items);
    }

    private static string BuildProviderEvidenceDetail(InstrumentPassportProviderConfidenceDto row)
    {
        var freshness = row.FreshnessAsOf.HasValue
            ? $"Fresh as of {row.FreshnessAsOf.Value.UtcDateTime:yyyy-MM-dd HH:mm 'UTC'} ({row.FreshnessMinutes.GetValueOrDefault()} minute(s) old)."
            : "Freshness timestamp unavailable.";
        var overrides = row.OverrideHistory.Count == 0
            ? "No retained override history."
            : $"{row.OverrideHistory.Count} retained override event(s).";

        return $"{row.ProviderSource}; {row.ConfidenceReason} {freshness} {overrides}";
    }

    private static IReadOnlyList<InstrumentPassportOperationsWorkbenchItemDto> BuildSourceEvidenceItems(
        SecurityMasterTrustSnapshotDto snapshot)
    {
        if (snapshot.ProvenanceCandidates.Count == 0)
        {
            return
            [
                new InstrumentPassportOperationsWorkbenchItemDto(
                    ItemId: "source-record-missing",
                    Label: "Retained source evidence",
                    Value: "Unavailable",
                    Status: "Review",
                    Detail: "Source record unavailable; as of timestamp unavailable; updated by unknown steward. Link provider, administrator, or custodian evidence before downstream handoff.",
                    EvidenceCount: 0,
                    BlockingIssueCount: 1,
                    Route: "/workstation/accounting/security-master#source-missing")
            ];
        }

        return snapshot.ProvenanceCandidates
            .OrderByDescending(static candidate => candidate.IsWinningSource)
            .ThenByDescending(static candidate => candidate.AsOf)
            .ThenBy(static candidate => candidate.SourceSystem, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select((candidate, index) =>
            {
                var isBlockingConflict = candidate.ConflictId.HasValue &&
                    candidate.ImpactSeverity is not SecurityMasterImpactSeverity.None and not SecurityMasterImpactSeverity.Low;
                var sourceRecord = SecurityMasterText(candidate.SourceRecordId, "no retained source record id");
                var updatedBy = SecurityMasterText(candidate.UpdatedBy, "unknown steward");
                var reason = SecurityMasterText(candidate.Reason, candidate.IsWinningSource ? "winning source" : "source candidate");
                var asOf = candidate.AsOf.HasValue
                    ? candidate.AsOf.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                    : "timestamp unavailable";
                var route = candidate.ConflictId.HasValue
                    ? $"/workstation/accounting/security-master#conflict-{candidate.ConflictId.Value:D}"
                    : $"/workstation/accounting/security-master#source-{index + 1}";

                return new InstrumentPassportOperationsWorkbenchItemDto(
                    ItemId: $"source-record-{index + 1}",
                    Label: candidate.SourceSystem,
                    Value: $"{candidate.FieldPath}: {candidate.DisplayValue}",
                    Status: isBlockingConflict ? "Review" : "Ready",
                    Detail: $"Source record {sourceRecord}; as of {asOf}; updated by {updatedBy}; {reason}.",
                    EvidenceCount: 1,
                    BlockingIssueCount: isBlockingConflict ? 1 : 0,
                    Route: route);
            })
            .ToArray();
    }

    private static InstrumentPassportOperationsWorkbenchPanelDto BuildTermsPanel(
        SecurityMasterTrustSnapshotDto snapshot,
        InstrumentPassportPricingDto pricing,
        InstrumentPassportClassificationProfileDto? classificationProfile)
    {
        var scheduleEventCount = snapshot.ScheduleBook?.Events.Count ?? 0;
        var factorCount = snapshot.ScheduleBook?.FactorHistory.Count ?? 0;
        var hasClassification = !string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.AssetClass)
            && !string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.Currency);
        var hasEconomicTerms = snapshot.ScheduleSummary?.HasEconomicScheduleTerms == true
            || scheduleEventCount > 0
            || snapshot.CorporateActions.Count > 0
            || pricing.TradingParameters is not null;
        var items = new List<InstrumentPassportOperationsWorkbenchItemDto>
        {
            new(
                ItemId: "classification",
                Label: "Classification",
                Value: BuildClassificationValue(snapshot),
                Status: hasClassification ? "Ready" : "Review",
                Detail: BuildLedgerClassificationSummary(snapshot),
                EvidenceCount: hasClassification ? 1 : 0,
                BlockingIssueCount: hasClassification ? 0 : 1),
            new(
                ItemId: "instrument-type-profile",
                Label: "Instrument type profile",
                Value: classificationProfile is null
                    ? "Unavailable"
                    : $"{classificationProfile.DisplayName} ({classificationProfile.InstrumentType})",
                Status: classificationProfile is null ? "Review" : "Ready",
                Detail: classificationProfile?.Summary
                    ?? "No InstrumentType compatibility profile is available for this Security Master asset class.",
                EvidenceCount: classificationProfile is null
                    ? 0
                    : Math.Max(
                        1,
                        classificationProfile.ProviderCapabilities.Count +
                        classificationProfile.LifecycleEvents.Count +
                        classificationProfile.LedgerBehaviorHints.Count),
                BlockingIssueCount: classificationProfile is null ? 1 : 0),
            new(
                ItemId: "economics",
                Label: "Economics",
                Value: pricing.Summary,
                Status: IsReadyStatus(pricing.Status) || hasEconomicTerms ? "Ready" : "Review",
                Detail: BuildTermsAndObligationsSummary(snapshot),
                EvidenceCount: (pricing.TradingParameters is null ? 0 : 1) + snapshot.CorporateActions.Count + scheduleEventCount,
                BlockingIssueCount: IsReadyStatus(pricing.Status) || hasEconomicTerms ? 0 : 1),
            new(
                ItemId: "payment-schedule",
                Label: "Payment schedule",
                Value: scheduleEventCount > 0 ? $"{scheduleEventCount} projected event(s)" : "No projected events",
                Status: scheduleEventCount > 0 || snapshot.ScheduleSummary?.SupportsCashflowSchedule == true ? "Ready" : "Review",
                Detail: snapshot.ScheduleSummary?.Summary ?? "Payment, amortization, PIK, covenant, and paydown schedule evidence is not retained.",
                EvidenceCount: scheduleEventCount,
                BlockingIssueCount: scheduleEventCount > 0 || snapshot.ScheduleSummary?.SupportsCashflowSchedule == true ? 0 : 1),
            new(
                ItemId: "factors-collateral-covenants",
                Label: "Factors, collateral, covenants",
                Value: factorCount > 0 ? $"{factorCount} factor row(s)" : "Review required",
                Status: factorCount > 0 || snapshot.ScheduleSummary?.SupportsFactorHistory == true ? "Ready" : "Review",
                Detail: "Structured-asset factor, collateral, covenant, and paydown evidence must be linked when applicable.",
                EvidenceCount: factorCount,
                BlockingIssueCount: factorCount > 0 || snapshot.ScheduleSummary?.SupportsFactorHistory == true ? 0 : 1)
        };
        items.AddRange(BuildConflictItems(snapshot, "terms-conflict", IsTermsConflict));

        return BuildOperationsPanel("terms", "Terms", items);
    }

    private static IReadOnlyList<InstrumentPassportOperationsWorkbenchItemDto> BuildConflictItems(
        SecurityMasterTrustSnapshotDto snapshot,
        string itemPrefix,
        Func<SecurityMasterConflictAssessmentDto, bool> predicate)
    {
        return snapshot.ConflictAssessments
            .Where(predicate)
            .OrderByDescending(static assessment => assessment.ImpactSeverity)
            .ThenBy(static assessment => assessment.Conflict.FieldPath, StringComparer.OrdinalIgnoreCase)
            .Select((assessment, index) =>
            {
                var conflict = assessment.Conflict;
                return new InstrumentPassportOperationsWorkbenchItemDto(
                    ItemId: $"{itemPrefix}-{index + 1}",
                    Label: conflict.FieldPath,
                    Value: $"{conflict.ProviderA}: {conflict.ValueA} / {conflict.ProviderB}: {conflict.ValueB}",
                    Status: "Review",
                    Detail: $"{assessment.ImpactSummary} Recommended resolution: {assessment.RecommendedResolution}",
                    EvidenceCount: 2,
                    BlockingIssueCount: assessment.ImpactSeverity is SecurityMasterImpactSeverity.None or SecurityMasterImpactSeverity.Low ? 0 : 1,
                    Route: $"/workstation/accounting/security-master#conflict-{conflict.ConflictId:D}");
            })
            .ToArray();
    }

    private static bool IsProviderOrIdentifierConflict(SecurityMasterConflictAssessmentDto assessment) =>
        ContainsAnyControlToken(
            [
                assessment.Conflict.ConflictKind,
                assessment.Conflict.FieldPath
            ],
            "identifier",
            "ticker",
            "cusip",
            "isin",
            "figi",
            "provider",
            "mapping",
            "alias");

    private static bool IsTermsConflict(SecurityMasterConflictAssessmentDto assessment) =>
        ContainsAnyControlToken(
            [
                assessment.Conflict.ConflictKind,
                assessment.Conflict.FieldPath
            ],
            "maturity",
            "coupon",
            "assettype",
            "asset type",
            "classification",
            "currency",
            "factor",
            "collateral",
            "covenant",
            "paydown",
            "amortization",
            "strike",
            "expiry",
            "expiration",
            "pik");

    private static IReadOnlyList<InstrumentPassportOperationsReadinessDto> BuildOperationsReadiness(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence,
        InstrumentPassportPricingDto pricing,
        IReadOnlyList<InstrumentPassportOperationsHandoffDto> handoffs)
    {
        var identifierConflictCount = providerConfidence.Sum(static row => row.IdentifierConflictIds.Count);
        var activeProviderCount = providerConfidence.Count(static row => row.IsActive);
        var identityReady = identifierSummary.HasPrimaryIdentifier
            && identifierSummary.HasProviderMappings
            && activeProviderCount > 0
            && identifierConflictCount == 0;
        var classificationReady = !string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.AssetClass)
            && !string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.Currency);
        var termsReady = snapshot.ScheduleBook?.Events.Count > 0
            || snapshot.ScheduleSummary?.HasEconomicScheduleTerms == true
            || snapshot.CorporateActions.Count > 0
            || pricing.TradingParameters is not null;
        var valuationReady = identityReady && IsReadyStatus(pricing.Status);
        var reconciliationReady = identityReady && classificationReady && !snapshot.TrustPosture.HasOpenConflicts;
        var ledgerReady = identityReady && classificationReady && snapshot.DownstreamImpact.LedgerExposureCount > 0;
        var closeReady = reconciliationReady && ledgerReady && termsReady;
        var reportReady = identityReady && snapshot.DownstreamImpact.ReportPackExposureCount > 0 && !snapshot.TrustPosture.HasOpenConflicts;
        var nextAction = handoffs.FirstOrDefault(static handoff => handoff.IsEnabled);

        return
        [
            BuildReadiness("valuation", "Valuation-ready", valuationReady, pricing.Summary, activeProviderCount + (pricing.TradingParameters is null ? 0 : 1), identifierConflictCount, "Review pricing source, identifier confidence, and active provider evidence.", nextAction?.Target),
            BuildReadiness("reconciliation", "Reconciliation-ready", reconciliationReady, snapshot.DownstreamImpact.ReconciliationExposureSummary, snapshot.DownstreamImpact.ReconciliationExposureCount + activeProviderCount, snapshot.TrustPosture.OpenConflictCount, "Resolve identifier, classification, maturity, coupon, currency, or factor conflicts.", nextAction?.Target),
            BuildReadiness("ledger", "Ledger-ready", ledgerReady, snapshot.DownstreamImpact.LedgerExposureSummary, snapshot.DownstreamImpact.LedgerExposureCount + (classificationReady ? 1 : 0), ledgerReady ? 0 : 1, "Confirm ledger classification and retained Security Master provenance.", nextAction?.Target),
            BuildReadiness("close", "Close-ready", closeReady, snapshot.DownstreamImpact.Summary, snapshot.DownstreamImpact.Links.Count + (termsReady ? 1 : 0), closeReady ? 0 : 1, "Clear unresolved Security Master blockers before close package use.", nextAction?.Target),
            BuildReadiness("report", "Report-ready", reportReady, snapshot.DownstreamImpact.ReportPackExposureSummary, snapshot.DownstreamImpact.ReportPackExposureCount, reportReady ? 0 : 1, "Link report-line provenance and resolve open definition conflicts.", nextAction?.Target)
        ];
    }

    private static InstrumentPassportOperationsWorkbenchPanelDto BuildReadinessPanel(
        IReadOnlyList<InstrumentPassportOperationsReadinessDto> readiness)
    {
        var items = readiness
            .Select(item => new InstrumentPassportOperationsWorkbenchItemDto(
                ItemId: item.ReadinessId,
                Label: item.Label,
                Value: item.Status,
                Status: item.Status,
                Detail: item.Summary,
                EvidenceCount: item.EvidenceCount,
                BlockingIssueCount: item.BlockingIssueCount,
                Route: item.Route))
            .ToArray();
        return BuildOperationsPanel("operations-readiness", "Operations readiness", items);
    }

    private static InstrumentPassportOperationsWorkbenchPanelDto BuildHandoffPanel(
        SecurityMasterTrustSnapshotDto snapshot,
        IReadOnlyList<InstrumentPassportOperationsHandoffDto> handoffs)
    {
        var items = handoffs
            .Select(handoff => new InstrumentPassportOperationsWorkbenchItemDto(
                ItemId: handoff.HandoffId,
                Label: handoff.Title,
                Value: handoff.Target,
                Status: handoff.Status,
                Detail: BuildHandoffDetail(handoff),
                EvidenceCount: 1,
                BlockingIssueCount: handoff.IsEnabled ? 0 : 1,
                Route: handoff.Route ?? handoff.Target))
            .ToList();

        if (items.Count == 0)
        {
            items.Add(new InstrumentPassportOperationsWorkbenchItemDto(
                ItemId: "handoff-review",
                Label: "Review Security Master handoff",
                Value: "Security Master detail",
                Status: "Review",
                Detail: snapshot.DownstreamImpact.Summary,
                EvidenceCount: snapshot.DownstreamImpact.Links.Count,
                BlockingIssueCount: 1));
        }

        return BuildOperationsPanel("handoff", "Handoff", items);
    }

    private static string BuildHandoffDetail(InstrumentPassportOperationsHandoffDto handoff)
    {
        var outputs = handoff.ImpactedOutputs.Count == 0
            ? "Security Master"
            : string.Join(", ", handoff.ImpactedOutputs);
        var linkedCases = handoff.LinkedCases.Count == 0
            ? "none"
            : string.Join(", ", handoff.LinkedCases);
        var owner = SecurityMasterText(handoff.Owner, "Security Master steward");
        var blocker = SecurityMasterText(handoff.BlockerReason, handoff.Detail);

        return $"{handoff.Detail} Owner: {owner}. Blocker: {blocker}. Impacted outputs: {outputs}. Linked cases: {linkedCases}.";
    }

    private static InstrumentPassportOperationsReadinessDto BuildReadiness(
        string readinessId,
        string label,
        bool isReady,
        string summary,
        int evidenceCount,
        int blockingIssueCount,
        string blockedAction,
        string? route)
    {
        return new InstrumentPassportOperationsReadinessDto(
            ReadinessId: readinessId,
            Label: label,
            Status: isReady ? "Ready" : "Review",
            IsReady: isReady,
            Summary: SecurityMasterText(summary, isReady ? "Readiness evidence is retained." : blockedAction),
            EvidenceCount: evidenceCount,
            BlockingIssueCount: isReady ? 0 : Math.Max(1, blockingIssueCount),
            NextAction: isReady ? "No blocker." : blockedAction,
            Route: route);
    }

    private static InstrumentPassportOperationsWorkbenchPanelDto BuildOperationsPanel(
        string panelId,
        string title,
        IReadOnlyList<InstrumentPassportOperationsWorkbenchItemDto> items)
    {
        var blockingCount = items.Sum(static item => item.BlockingIssueCount);
        var reviewCount = items.Count(static item => !IsReadyStatus(item.Status));
        var status = blockingCount == 0 && reviewCount == 0 ? "Ready" : "Review";
        var evidenceCount = items.Sum(static item => item.EvidenceCount);

        return new InstrumentPassportOperationsWorkbenchPanelDto(
            PanelId: panelId,
            Title: title,
            Status: status,
            Summary: status.Equals("Ready", StringComparison.OrdinalIgnoreCase)
                ? $"{title} panel has {evidenceCount} retained evidence item(s) and no blocking issue."
                : $"{title} panel has {blockingCount} blocking issue(s) requiring operator review.",
            Items: items);
    }

    private static bool PanelNeedsReview(InstrumentPassportOperationsWorkbenchPanelDto panel) =>
        !IsReadyStatus(panel.Status) || panel.Items.Any(static item => item.BlockingIssueCount > 0);

    private static bool IsReadyStatus(string? status) =>
        status?.Equals("Ready", StringComparison.OrdinalIgnoreCase) == true
        || status?.Equals("Trusted", StringComparison.OrdinalIgnoreCase) == true
        || status?.Equals("Complete", StringComparison.OrdinalIgnoreCase) == true;

    private static string BuildClassificationValue(SecurityMasterTrustSnapshotDto snapshot)
    {
        var parts = new[]
        {
            snapshot.EconomicDefinition.AssetClass,
            snapshot.EconomicDefinition.AssetFamily,
            snapshot.EconomicDefinition.SubType,
            snapshot.EconomicDefinition.Currency
        }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? "Unavailable" : string.Join(" / ", parts);
    }

    private static InstrumentPassportClassificationProfileDto? BuildClassificationProfile(SecurityMasterTrustSnapshotDto snapshot)
    {
        var assetClass = NormalizeSecurityMasterText(snapshot.EconomicDefinition.AssetClass);
        if (assetClass is null)
        {
            return null;
        }

        var compatibility = SecurityKindMapping.GetCompatibilityProfile(assetClass);
        var descriptors = SecurityKindMapping.ToInstrumentTypeDescriptors(assetClass);
        var hasMultipleDirectInstrumentTypes = compatibility?.InstrumentTypes.Count > 1;
        var primaryDescriptor = compatibility?.PrimaryInstrumentType is { } primaryInstrumentType
            ? InstrumentTypeDescriptorCatalog.Find(primaryInstrumentType)
            : hasMultipleDirectInstrumentTypes == true
                ? null
                : descriptors.FirstOrDefault();

        if (compatibility is null && primaryDescriptor is null)
        {
            return null;
        }

        var descriptorNames = descriptors.Select(static descriptor => descriptor.DisplayName).ToArray();
        var displayName = primaryDescriptor?.DisplayName
            ?? (descriptorNames.Length > 1 ? $"{assetClass} family" : assetClass);
        var instrumentType = primaryDescriptor?.InstrumentType.ToString()
            ?? (compatibility?.InstrumentTypes.Count > 0
                ? string.Join(", ", compatibility.InstrumentTypes)
                : "Unmapped");
        var providerSecurityTypes = DistinctProfileValues(descriptors.Select(static descriptor => descriptor.DefaultProviderSecurityType));
        var preferredIdentifierKinds = DistinctProfileValues(
            descriptors.SelectMany(static descriptor => descriptor.PreferredIdentifierKinds),
            SecurityAssetClassCatalog.GetPreferredIdentifierKinds(assetClass).Select(static kind => kind.ToString()));
        var requiredEconomicTerms = compatibility?.RequiredEconomicTerms
            ?? DistinctProfileValues(descriptors.SelectMany(static descriptor => descriptor.RequiredEconomicTerms));
        var providerCapabilities = compatibility?.ProviderCapabilities
            ?? DistinctProfileValues(descriptors.SelectMany(static descriptor => descriptor.ProviderCapabilities));
        var lifecycleEvents = compatibility?.LifecycleEvents
            ?? DistinctProfileValues(descriptors.SelectMany(static descriptor => descriptor.LifecycleEvents));
        var validationRules = compatibility?.ValidationRules
            ?? DistinctProfileValues(descriptors.SelectMany(static descriptor => descriptor.ValidationRules));
        var ledgerBehaviorHints = compatibility?.LedgerBehaviorHints
            ?? DistinctProfileValues(descriptors.SelectMany(static descriptor => descriptor.LedgerBehaviorHints));
        var riskModelHints = compatibility?.RiskModelHints
            ?? DistinctProfileValues(descriptors.SelectMany(static descriptor => descriptor.RiskModelHints));
        var compatibleAssetClasses = compatibility?.CompatibleSecurityMasterAssetClasses
            ?? DistinctProfileValues([assetClass], descriptors.SelectMany(static descriptor => descriptor.CompatibleSecurityMasterAssetClasses));
        var summary = compatibility?.Summary
            ?? $"{displayName} retains an InstrumentType descriptor for provider routing and downstream operations.";

        if (providerCapabilities.Count > 0 || lifecycleEvents.Count > 0)
        {
            summary = $"{summary} Provider route {SecurityMasterText(string.Join(", ", providerSecurityTypes), "n/a")}; {requiredEconomicTerms.Count} required term(s); {lifecycleEvents.Count} lifecycle event(s).";
        }

        return new InstrumentPassportClassificationProfileDto(
            InstrumentType: instrumentType,
            DisplayName: displayName,
            SecurityMasterAssetClass: assetClass,
            AssetFamily: NormalizeSecurityMasterText(snapshot.EconomicDefinition.AssetFamily) ?? primaryDescriptor?.SecurityMasterAssetFamily,
            SubType: NormalizeSecurityMasterText(snapshot.EconomicDefinition.SubType) ?? primaryDescriptor?.SecurityMasterSubType,
            DefaultProviderSecurityType: SecurityMasterText(string.Join(", ", providerSecurityTypes), "n/a"),
            IsTradeable: primaryDescriptor?.IsTradeable ?? descriptors.Any(static descriptor => descriptor.IsTradeable),
            IsReferenceOnly: primaryDescriptor?.IsReferenceOnly ?? (descriptors.Count > 0 && descriptors.All(static descriptor => descriptor.IsReferenceOnly)),
            IsDerivative: primaryDescriptor?.IsDerivative ?? descriptors.Any(static descriptor => descriptor.IsDerivative),
            RequiresUnderlying: primaryDescriptor?.RequiresUnderlying ?? descriptors.Any(static descriptor => descriptor.RequiresUnderlying),
            ProducesCashFlows: primaryDescriptor?.ProducesCashFlows ?? descriptors.Any(static descriptor => descriptor.ProducesCashFlows),
            RequiresLotTracking: primaryDescriptor?.RequiresLotTracking ?? descriptors.Any(static descriptor => descriptor.RequiresLotTracking),
            SettlementModel: primaryDescriptor?.SettlementModel ?? "Security Master reference-data settlement model is not mapped to a direct InstrumentType.",
            CompatibleSecurityMasterAssetClasses: compatibleAssetClasses,
            PreferredIdentifierKinds: preferredIdentifierKinds,
            RequiredEconomicTerms: requiredEconomicTerms,
            ProviderCapabilities: providerCapabilities,
            LifecycleEvents: lifecycleEvents,
            ValidationRules: validationRules,
            LedgerBehaviorHints: ledgerBehaviorHints,
            RiskModelHints: riskModelHints,
            Summary: summary);
    }

    private async Task<ClearwaterReferenceDataEvidence> BuildClearwaterEvidenceAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct)
    {
        var goldenCopy = _pricingService is null
            ? null
            : await _pricingService.GetGoldenCopyPriceAsync(securityId, fundProfileId, ct).ConfigureAwait(false);
        var hierarchy = _pricingService is null
            ? null
            : await _pricingService.GetPricingHierarchyAsync(securityId, fundProfileId, ct).ConfigureAwait(false);
        var cashFlowSource = _cashFlowService is null
            ? null
            : await _cashFlowService.GetCashFlowSourceAsync(securityId, ct).ConfigureAwait(false);
        var entitlements = _entitlementService is null
            ? []
            : await _entitlementService.GetAllAsync(ct).ConfigureAwait(false);
        var qualityReport = _dataQualityService is null
            ? null
            : await _dataQualityService.GetLatestReportAsync(ct).ConfigureAwait(false);

        return new ClearwaterReferenceDataEvidence(
            securityId,
            goldenCopy,
            hierarchy,
            cashFlowSource,
            entitlements,
            qualityReport);
    }

    private static IReadOnlyList<InstrumentPassportReferenceDataWorkbenchSectionDto> BuildClearwaterControlSections(
        ClearwaterReferenceDataEvidence evidence,
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents)
    {
        return
        [
            BuildPricingHierarchySection(evidence),
            BuildCashFlowSourceSection(evidence),
            BuildVendorEntitlementSection(evidence),
            BuildDataQualityControlSection(evidence),
            BuildManualChangeReviewSection(lifecycleEvents)
        ];
    }

    private static SecurityMasterOperatingModelDto BuildOperatingModel(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        string? fundProfileId,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence,
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents,
        ClearwaterReferenceDataEvidence clearwaterEvidence)
    {
        var context = new SecurityMasterOperatingScope(
            ClientId: null,
            AccountId: ResolveDominantAccountId(snapshot),
            FundProfileId: NormalizeSecurityMasterText(fundProfileId),
            SecurityId: snapshot.SecurityId);
        var controls = BuildClearwaterControlSections(clearwaterEvidence, lifecycleEvents);
        var entitlementApplicability = BuildEntitlementApplicability(clearwaterEvidence.VendorEntitlements, context);
        var operatorMetadata = BuildOperatorMetadata(clearwaterEvidence, entitlementApplicability);
        var manualChangeApproval = BuildManualChangeApprovalPosture(lifecycleEvents);
        var stages = BuildOperatingModelStages(snapshot, identifierSummary, providerConfidence, controls, entitlementApplicability, manualChangeApproval);
        var status = stages.Any(static stage => stage.BlockingIssueCount > 0 || stage.Status.Equals("Review", StringComparison.OrdinalIgnoreCase))
            || manualChangeApproval.UnapprovedManualChangeCount > 0
            ? "Review"
            : "Ready";

        return new SecurityMasterOperatingModelDto(
            SecurityId: snapshot.SecurityId,
            ClientId: context.ClientId,
            AccountId: context.AccountId,
            FundProfileId: context.FundProfileId,
            Status: status,
            Summary: status.Equals("Ready", StringComparison.OrdinalIgnoreCase)
                ? "Security Master operating model has applicable entitlement, source, control, and approval evidence for the selected scope."
                : "Security Master operating model needs entitlement, source, control, or approval review for the selected scope.",
            Stages: stages,
            EntitlementApplicability: entitlementApplicability,
            OperatorMetadata: operatorMetadata,
            ManualChangeApproval: manualChangeApproval,
            Controls: controls,
            RetrievedAtUtc: snapshot.RetrievedAtUtc);
    }

    private static IReadOnlyList<SecurityMasterOperatingModelStageDto> BuildOperatingModelStages(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence,
        IReadOnlyList<InstrumentPassportReferenceDataWorkbenchSectionDto> controls,
        IReadOnlyList<SecurityMasterEntitlementApplicabilityDto> entitlementApplicability,
        SecurityMasterManualChangeApprovalPostureDto manualChangeApproval)
    {
        var pricingEvidence = controls.FirstOrDefault(static section => section.SectionId == "pricing-hierarchy");
        var cashFlowEvidence = controls.FirstOrDefault(static section => section.SectionId == "cash-flow-source-governance");
        var qualityEvidence = controls.FirstOrDefault(static section => section.SectionId == "data-quality-controls");
        var entitlementBlockers = entitlementApplicability.Count(static item =>
            item.IsApplicable
            && item.IsMostSpecific
            && item.Status is DataVendorEntitlementStatus.Expired or DataVendorEntitlementStatus.ExpiringSoon or DataVendorEntitlementStatus.PendingRenewal);
        var mostSpecificEntitlements = entitlementApplicability.Count(static item => item.IsApplicable && item.IsMostSpecific);
        var activeProviderEvidenceCount = providerConfidence.Count(static row => row.IsActive);
        var providerBlockingCount = providerConfidence.Sum(static row => row.IdentifierConflictIds.Count);
        var matchBlockers = identifierSummary.HasPrimaryIdentifier && identifierSummary.HasProviderMappings && providerBlockingCount == 0 ? 0 : 1;

        return
        [
            new SecurityMasterOperatingModelStageDto(
                StageId: "receive",
                Title: "Receive",
                Status: activeProviderEvidenceCount > 0 && providerBlockingCount == 0 ? "Ready" : "Review",
                Summary: activeProviderEvidenceCount > 0
                    ? $"{activeProviderEvidenceCount} active provider evidence row(s) retained on the passport."
                    : "No active provider evidence rows are retained on the passport.",
                EvidenceCount: providerConfidence.Count,
                BlockingIssueCount: activeProviderEvidenceCount == 0 ? 1 : providerBlockingCount),
            new SecurityMasterOperatingModelStageDto(
                StageId: "validate",
                Title: "Validate",
                Status: qualityEvidence?.Status ?? "Review",
                Summary: qualityEvidence?.Summary ?? "Quality validation evidence is unavailable.",
                EvidenceCount: qualityEvidence?.EvidenceCount ?? 0,
                BlockingIssueCount: qualityEvidence?.BlockingIssueCount ?? 1),
            new SecurityMasterOperatingModelStageDto(
                StageId: "match",
                Title: "Match",
                Status: matchBlockers == 0 ? "Ready" : "Review",
                Summary: identifierSummary.Summary,
                EvidenceCount: identifierSummary.ActiveIdentifierCount + identifierSummary.ActiveAliasCount + identifierSummary.ProviderMappingCount,
                BlockingIssueCount: matchBlockers),
            new SecurityMasterOperatingModelStageDto(
                StageId: "create-enrich",
                Title: "Create and enrich",
                Status: manualChangeApproval.UnapprovedManualChangeCount == 0 ? "Ready" : "Review",
                Summary: manualChangeApproval.Summary,
                EvidenceCount: manualChangeApproval.ManualChangeCount,
                BlockingIssueCount: manualChangeApproval.UnapprovedManualChangeCount),
            new SecurityMasterOperatingModelStageDto(
                StageId: "build-golden-copy",
                Title: "Build golden copy",
                Status: pricingEvidence?.Status ?? "Review",
                Summary: pricingEvidence?.Summary ?? "Golden-copy pricing evidence is unavailable.",
                EvidenceCount: pricingEvidence?.EvidenceCount ?? 0,
                BlockingIssueCount: pricingEvidence?.BlockingIssueCount ?? 1),
            new SecurityMasterOperatingModelStageDto(
                StageId: "reconcile",
                Title: "Reconcile",
                Status: entitlementBlockers == 0 && mostSpecificEntitlements > 0 ? "Ready" : "Review",
                Summary: mostSpecificEntitlements > 0
                    ? $"{mostSpecificEntitlements} most-specific entitlement record(s) apply to the selected Security Master scope."
                    : "No applicable vendor entitlement evidence is retained for the selected Security Master scope.",
                EvidenceCount: mostSpecificEntitlements,
                BlockingIssueCount: mostSpecificEntitlements == 0 ? 1 : entitlementBlockers),
            new SecurityMasterOperatingModelStageDto(
                StageId: "calculate-report",
                Title: "Calculate and report",
                Status: cashFlowEvidence?.Status ?? (snapshot.DownstreamImpact.Links.Count > 0 ? "Ready" : "Review"),
                Summary: cashFlowEvidence?.Summary ?? snapshot.DownstreamImpact.Summary,
                EvidenceCount: (cashFlowEvidence?.EvidenceCount ?? 0) + snapshot.DownstreamImpact.Links.Count,
                BlockingIssueCount: cashFlowEvidence?.BlockingIssueCount ?? 0)
        ];
    }

    private static IReadOnlyList<SecurityMasterEntitlementApplicabilityDto> BuildEntitlementApplicability(
        IReadOnlyList<DataVendorEntitlementDto> entitlements,
        SecurityMasterOperatingScope context)
    {
        var rows = entitlements
            .Select(entitlement => BuildEntitlementApplicabilityRow(entitlement, context))
            .OrderByDescending(static row => row.IsApplicable)
            .ThenBy(static row => row.VendorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.DataType)
            .ThenByDescending(GetApplicabilityRank)
            .ToArray();
        var bestRanks = rows
            .Where(static row => row.IsApplicable)
            .GroupBy(static row => (row.VendorName, row.DataType), new VendorDataTypeComparer())
            .ToDictionary(static group => group.Key, static group => group.Max(GetApplicabilityRank), new VendorDataTypeComparer());

        return rows
            .Select(row => row with
            {
                IsMostSpecific = row.IsApplicable
                    && bestRanks.TryGetValue((row.VendorName, row.DataType), out var rank)
                    && GetApplicabilityRank(row) == rank
            })
            .ToArray();
    }

    private static SecurityMasterEntitlementApplicabilityDto BuildEntitlementApplicabilityRow(
        DataVendorEntitlementDto entitlement,
        SecurityMasterOperatingScope context)
    {
        var applies =
            ScopeMatches(entitlement.ClientId, context.ClientId)
            && ScopeMatches(entitlement.AccountId, context.AccountId)
            && ScopeMatches(entitlement.FundProfileId, context.FundProfileId)
            && (!entitlement.SecurityId.HasValue || entitlement.SecurityId == context.SecurityId);
        var scope = ResolveEntitlementScope(entitlement);
        return new SecurityMasterEntitlementApplicabilityDto(
            EntitlementId: entitlement.EntitlementId,
            VendorName: entitlement.VendorName,
            DataType: entitlement.DataType,
            Scope: scope,
            ClientId: entitlement.ClientId,
            AccountId: entitlement.AccountId,
            FundProfileId: entitlement.FundProfileId,
            SecurityId: entitlement.SecurityId,
            IsApplicable: applies,
            IsMostSpecific: false,
            Status: entitlement.Status,
            RequiresDirectClientContract: entitlement.RequiresDirectClientContract,
            ContractReference: entitlement.ContractReference,
            Summary: applies
                ? $"{entitlement.VendorName} {entitlement.DataType} entitlement applies at {scope} scope with {entitlement.Status} status."
                : $"{entitlement.VendorName} {entitlement.DataType} entitlement is configured for {scope} scope and does not match the selected scope.");
    }

    private static IReadOnlyList<SecurityMasterOperatorMetadataDto> BuildOperatorMetadata(
        ClearwaterReferenceDataEvidence evidence,
        IReadOnlyList<SecurityMasterEntitlementApplicabilityDto> entitlementApplicability)
    {
        var entitlementMetadata = evidence.VendorEntitlements
            .Where(entitlement => entitlementApplicability.Any(applicable =>
                applicable.EntitlementId == entitlement.EntitlementId
                && applicable.IsApplicable
                && applicable.IsMostSpecific))
            .Select(entitlement => new SecurityMasterOperatorMetadataDto(
                MetadataId: $"entitlement-{entitlement.EntitlementId:N}",
                VendorName: entitlement.VendorName,
                DataType: entitlement.DataType,
                SourceCategory: SecurityMasterText(entitlement.SourceCategory, "Vendor entitlement"),
                ExpectedRefreshCadence: SecurityMasterText(entitlement.ExpectedRefreshCadence, "Operator configured"),
                DefaultMaxDaysStale: entitlement.DefaultMaxDaysStale,
                RequiresDirectClientContract: entitlement.RequiresDirectClientContract,
                OperatorMetadata: entitlement.OperatorMetadata,
                Summary: SecurityMasterText(
                    entitlement.OperatorMetadata,
                    $"{entitlement.VendorName} {entitlement.DataType} source metadata is configurable by operations.")))
            .ToList();

        if (evidence.GoldenCopyPrice is not null && entitlementMetadata.All(static item => item.DataType != DataVendorDataType.Pricing))
        {
            entitlementMetadata.Add(new SecurityMasterOperatorMetadataDto(
                MetadataId: "golden-copy-pricing",
                VendorName: evidence.GoldenCopyPrice.SelectedSource,
                DataType: DataVendorDataType.Pricing,
                SourceCategory: "Pricing hierarchy",
                ExpectedRefreshCadence: evidence.GoldenCopyPrice.IsStaleFallback ? "Stale fallback review" : "Fresh hierarchy selection",
                DefaultMaxDaysStale: evidence.GoldenCopyPrice.DaysStale,
                RequiresDirectClientContract: false,
                OperatorMetadata: null,
                Summary: $"Golden-copy pricing selected {evidence.GoldenCopyPrice.SelectedSource}."));
        }

        if (evidence.CashFlowSource is not null && entitlementMetadata.All(static item => item.DataType != DataVendorDataType.CashFlows))
        {
            entitlementMetadata.Add(new SecurityMasterOperatorMetadataDto(
                MetadataId: "cash-flow-source",
                VendorName: evidence.CashFlowSource.SourceKind.ToString(),
                DataType: DataVendorDataType.CashFlows,
                SourceCategory: "Cash-flow source governance",
                ExpectedRefreshCadence: evidence.CashFlowSource.IsClientOverride ? "Client override confirmation" : "Vendor source refresh",
                DefaultMaxDaysStale: null,
                RequiresDirectClientContract: evidence.CashFlowSource.IsClientOverride,
                OperatorMetadata: evidence.CashFlowSource.ClientConfirmedBy,
                Summary: $"{evidence.CashFlowSource.SourceKind} cash-flow source metadata is retained for operators."));
        }

        return entitlementMetadata
            .OrderBy(static item => item.VendorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.DataType)
            .ToArray();
    }

    private static SecurityMasterManualChangeApprovalPostureDto BuildManualChangeApprovalPosture(
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents)
    {
        var manualEvents = lifecycleEvents.Where(IsManualSecurityChange).ToArray();
        var unapprovedCount = manualEvents.Count(static item => !IsApprovedManualSecurityChange(item));

        return new SecurityMasterManualChangeApprovalPostureDto(
            PolicyKey: "operations-continuity.security-master-override",
            Gate: OperationsGateKeyDto.SecurityMaster,
            Route: UiApiRoutes.OperationsContinuitySecurityMasterOverrideApprove,
            RequiredPermission: "AdminMaintenance or ModifySecurityMaster",
            RequiredDistinctApprovals: 1,
            RequiresIndependentReviewer: true,
            EvidenceRequirement: "Override id, policy reference, rationale, expiration date, and linked evidence.",
            Status: unapprovedCount == 0 ? "Ready" : "Review",
            ManualChangeCount: manualEvents.Length,
            UnapprovedManualChangeCount: unapprovedCount,
            Summary: manualEvents.Length == 0
                ? "No manual creation, remapping, override, or critical-attribute change is retained on this passport."
                : $"{manualEvents.Length} manual change event(s) reuse the operations-continuity.security-master-override approval policy; {unapprovedCount} require independent reviewer evidence.");
    }

    private static int GetApplicabilityRank(SecurityMasterEntitlementApplicabilityDto row) =>
        row.SecurityId.HasValue ? 4 :
        !string.IsNullOrWhiteSpace(row.AccountId) ? 3 :
        !string.IsNullOrWhiteSpace(row.FundProfileId) ? 2 :
        !string.IsNullOrWhiteSpace(row.ClientId) ? 1 : 0;

    private static bool ScopeMatches(string? configuredScope, string? selectedScope)
    {
        var configured = NormalizeSecurityMasterText(configuredScope);
        if (configured is null)
            return true;

        var selected = NormalizeSecurityMasterText(selectedScope);
        return selected is not null && configured.Equals(selected, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveEntitlementScope(DataVendorEntitlementDto entitlement)
    {
        if (entitlement.SecurityId.HasValue)
            return "Security";
        if (!string.IsNullOrWhiteSpace(entitlement.AccountId))
            return "Account";
        if (!string.IsNullOrWhiteSpace(entitlement.FundProfileId))
            return "FundProfile";
        if (!string.IsNullOrWhiteSpace(entitlement.ClientId))
            return "Client";

        return "Global";
    }

    private static string? ResolveDominantAccountId(SecurityMasterTrustSnapshotDto snapshot)
    {
        var accountIds = snapshot.OpenLotReadModel?.Lots
            .Select(static lot => NormalizeSecurityMasterText(lot.AccountScopeId))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Key)
            .ToArray();

        return accountIds is { Length: > 0 } ? accountIds[0] : null;
    }

    private static string? NormalizeSecurityMasterText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> DistinctProfileValues(params IEnumerable<string>[] values) =>
        values
            .SelectMany(static value => value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static InstrumentPassportReferenceDataWorkbenchSectionDto BuildPricingHierarchySection(
        ClearwaterReferenceDataEvidence evidence)
    {
        var hierarchyCount = evidence.PricingHierarchy?.Entries.Count ?? 0;
        if (evidence.GoldenCopyPrice is null)
        {
            return new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "pricing-hierarchy",
                Title: "Pricing hierarchy and stale fallback",
                Status: "Review",
                Summary: hierarchyCount > 0
                    ? $"{hierarchyCount} pricing source(s) configured, but no market-price golden copy is available."
                    : "No Clearwater pricing hierarchy or market-price golden copy is available on this passport.",
                EvidenceCount: hierarchyCount,
                BlockingIssueCount: 1);
        }

        var staleLabel = evidence.GoldenCopyPrice.IsStaleFallback
            ? $"stale fallback, {evidence.GoldenCopyPrice.DaysStale?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} day(s) old"
            : "fresh hierarchy selection";
        return new InstrumentPassportReferenceDataWorkbenchSectionDto(
            SectionId: "pricing-hierarchy",
            Title: "Pricing hierarchy and stale fallback",
            Status: evidence.GoldenCopyPrice.IsStaleFallback ? "Review" : "Ready",
            Summary: $"Golden-copy price {evidence.GoldenCopyPrice.GoldenCopyPrice.ToString("0.####", CultureInfo.InvariantCulture)} from {evidence.GoldenCopyPrice.SelectedSource}; {staleLabel}. {hierarchyCount} pricing source(s) configured.",
            EvidenceCount: hierarchyCount + evidence.GoldenCopyPrice.ComparisonPrices.Count + 1,
            BlockingIssueCount: evidence.GoldenCopyPrice.IsStaleFallback ? 1 : 0);
    }

    private static InstrumentPassportReferenceDataWorkbenchSectionDto BuildCashFlowSourceSection(
        ClearwaterReferenceDataEvidence evidence)
    {
        if (evidence.CashFlowSource is null)
        {
            return new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "cash-flow-source-governance",
                Title: "Cash-flow source governance",
                Status: "Review",
                Summary: "No Clearwater cash-flow source assignment is available for this security.",
                EvidenceCount: 0,
                BlockingIssueCount: 1);
        }

        var ageDays = evidence.CashFlowSource.LastUpdatedUtc.HasValue
            ? (int)Math.Max(0, (DateTimeOffset.UtcNow - evidence.CashFlowSource.LastUpdatedUtc.Value).TotalDays)
            : (int?)null;
        var isStaleClientOverride = evidence.CashFlowSource.IsClientOverride
            && (!evidence.CashFlowSource.ClientConfirmedAt.HasValue || ageDays is null or > 7);
        var confirmation = evidence.CashFlowSource.IsClientOverride
            ? $" Client confirmation: {SecurityMasterText(evidence.CashFlowSource.ClientConfirmedBy, "not retained")}."
            : string.Empty;

        return new InstrumentPassportReferenceDataWorkbenchSectionDto(
            SectionId: "cash-flow-source-governance",
            Title: "Cash-flow source governance",
            Status: isStaleClientOverride ? "Review" : "Ready",
            Summary: $"{evidence.CashFlowSource.SourceKind} cash-flow source assigned; last update {FormatAgeDays(ageDays)}.{confirmation}",
            EvidenceCount: 1,
            BlockingIssueCount: isStaleClientOverride ? 1 : 0);
    }

    private static InstrumentPassportReferenceDataWorkbenchSectionDto BuildVendorEntitlementSection(
        ClearwaterReferenceDataEvidence evidence)
    {
        var entitlements = evidence.VendorEntitlements;
        var directContractCount = entitlements.Count(static item => item.RequiresDirectClientContract);
        var atRiskCount = entitlements.Count(static item =>
            item.Status is DataVendorEntitlementStatus.Expired or DataVendorEntitlementStatus.ExpiringSoon or DataVendorEntitlementStatus.PendingRenewal);

        return new InstrumentPassportReferenceDataWorkbenchSectionDto(
            SectionId: "vendor-entitlements",
            Title: "Vendor licensing and entitlements",
            Status: entitlements.Count > 0 && atRiskCount == 0 ? "Ready" : "Review",
            Summary: entitlements.Count > 0
                ? $"{entitlements.Count} vendor entitlement record(s) retained; {directContractCount} direct-client contract requirement(s); {atRiskCount} renewal or expiry issue(s)."
                : "No vendor entitlement evidence is retained for CUSIP, SEDOL, pricing, ratings, MSCI, WSO, or cash-flow sources.",
            EvidenceCount: entitlements.Count,
            BlockingIssueCount: entitlements.Count == 0 ? 1 : atRiskCount);
    }

    private static InstrumentPassportReferenceDataWorkbenchSectionDto BuildDataQualityControlSection(
        ClearwaterReferenceDataEvidence evidence)
    {
        var violations = evidence.QualityReport?.Violations
            .Where(item => item.SecurityId == evidence.SecurityId)
            .ToArray() ?? [];
        var blockingCount = violations.Count(static item =>
            item.Severity is DataQualityRuleSeverity.Error or DataQualityRuleSeverity.HardBlock);

        return new InstrumentPassportReferenceDataWorkbenchSectionDto(
            SectionId: "data-quality-controls",
            Title: "Data-quality and golden-copy controls",
            Status: evidence.QualityReport is not null && blockingCount == 0 ? "Ready" : "Review",
            Summary: evidence.QualityReport is null
                ? "No retained Security Master quality report is available for completeness, taxonomy, consistency, or staleness checks."
                : $"{violations.Length} quality violation(s) retained for this security from the {evidence.QualityReport.RunAt:yyyy-MM-dd HH:mm} UTC run.",
            EvidenceCount: violations.Length,
            BlockingIssueCount: evidence.QualityReport is null ? 1 : blockingCount);
    }

    private static InstrumentPassportReferenceDataWorkbenchSectionDto BuildManualChangeReviewSection(
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents)
    {
        var manualEvents = lifecycleEvents
            .Where(IsManualSecurityChange)
            .ToArray();
        var unapprovedCount = manualEvents.Count(static item => !IsApprovedManualSecurityChange(item));

        return new InstrumentPassportReferenceDataWorkbenchSectionDto(
            SectionId: "manual-change-review",
            Title: "Manual creation and change review",
            Status: unapprovedCount == 0 ? "Ready" : "Review",
            Summary: manualEvents.Length == 0
                ? "No manual creation, remapping, override, or critical-attribute change is retained on this passport."
                : $"{manualEvents.Length} manual change event(s) retained; {unapprovedCount} require independent review evidence.",
            EvidenceCount: manualEvents.Length,
            BlockingIssueCount: unapprovedCount);
    }

    private static string FormatAgeDays(int? ageDays) =>
        ageDays.HasValue
            ? $"{ageDays.Value.ToString(CultureInfo.InvariantCulture)} day(s) ago"
            : "unknown";

    private static string SecurityMasterText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

    private static bool IsManualSecurityChange(SecurityMasterChangeHistoryItemDto item) =>
        ContainsAnyControlToken(
            [
                item.EventType,
                item.SourceSystem,
                item.Actor,
                item.Origin,
                item.Reason,
                .. item.ChangedFields
            ],
            "manual",
            "override",
            "operator",
            "wpf-ui",
            "desktop-user",
            "user",
            "remap",
            "amend");

    private static bool IsApprovedManualSecurityChange(SecurityMasterChangeHistoryItemDto item) =>
        ContainsAnyControlToken(
            [
                item.EventType,
                item.Reason,
                item.Summary
            ],
            "approved",
            "reviewed",
            "review",
            "signoff",
            "sign-off");

    private static bool ContainsAnyControlToken(IEnumerable<string?> values, params string[] tokens) =>
        values.Any(value => tokens.Any(token => Contains(value, token)));

    private static string BuildTermsAndObligationsSummary(SecurityMasterTrustSnapshotDto snapshot)
    {
        var scheduleEventCount = snapshot.ScheduleBook?.Events.Count ?? 0;
        if (scheduleEventCount > 0)
        {
            return $"{scheduleEventCount} projected obligation event(s) retained with source provenance.";
        }

        if (snapshot.CorporateActions.Count > 0)
        {
            return $"{snapshot.CorporateActions.Count} corporate action obligation event(s) retained on the passport.";
        }

        return $"{snapshot.EconomicDefinition.AssetClass} terms retained for {snapshot.EconomicDefinition.Currency} reference-data review.";
    }

    private static string BuildLedgerClassificationSummary(SecurityMasterTrustSnapshotDto snapshot)
    {
        var classification = new[]
        {
            snapshot.EconomicDefinition.AssetClass,
            snapshot.EconomicDefinition.AssetFamily,
            snapshot.EconomicDefinition.SubType,
            snapshot.EconomicDefinition.Currency
        }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var prefix = classification.Length == 0
            ? "Ledger classification is unavailable"
            : string.Join(" / ", classification);
        var compatibility = SecurityKindMapping.GetCompatibilityProfile(snapshot.EconomicDefinition.AssetClass);
        var compatibilitySummary = compatibility is null ? string.Empty : $" {compatibility.Summary}";
        return $"{prefix}. {snapshot.DownstreamImpact.LedgerExposureSummary}{compatibilitySummary}";
    }

    private static IReadOnlyList<InstrumentPassportOperationsHandoffDto> BuildOperationsHandoffs(
        SecurityMasterTrustSnapshotDto snapshot)
    {
        var handoffs = new List<InstrumentPassportOperationsHandoffDto>();

        handoffs.AddRange(snapshot.DownstreamImpact.Links.Select((link, index) =>
            new InstrumentPassportOperationsHandoffDto(
                HandoffId: $"impact-{index + 1}",
                Target: link.Target,
                Title: link.Label,
                Detail: link.Summary,
                Status: link.Severity.ToString(),
                IsEnabled: link.IsActive)
            {
                Owner = ResolveHandoffOwner(link.Target),
                BlockerReason = link.IsActive
                    ? link.Summary
                    : "Downstream impact is not active for the selected Security Master scope.",
                ImpactedOutputs = ResolveImpactedOutputs(link.Target),
                LinkedCases = [],
                Route = ResolveHandoffRoute(link.Target)
            }));

        handoffs.AddRange(snapshot.RecommendedActions.Select((action, index) =>
            new InstrumentPassportOperationsHandoffDto(
                HandoffId: $"action-{index + 1}",
                Target: action.Target ?? action.Kind.ToString(),
                Title: action.Title,
                Detail: action.Detail,
                Status: action.IsPrimary ? "Primary" : "Available",
                IsEnabled: action.IsEnabled)
            {
                Owner = ResolveActionOwner(action.Kind),
                BlockerReason = action.Detail,
                ImpactedOutputs = ResolveImpactedOutputs(action.Kind),
                LinkedCases = action.ConflictId.HasValue ? [action.ConflictId.Value.ToString("D")] : [],
                Route = ResolveActionRoute(action)
            }));

        if (handoffs.Count == 0)
        {
            handoffs.Add(new InstrumentPassportOperationsHandoffDto(
                HandoffId: "downstream-impact",
                Target: "SecurityMasterDetail",
                Title: "Retain Security Master context",
                Detail: snapshot.DownstreamImpact.Summary,
                Status: snapshot.DownstreamImpact.Severity.ToString(),
                IsEnabled: true)
            {
                Owner = "Security Master steward",
                BlockerReason = snapshot.DownstreamImpact.Summary,
                ImpactedOutputs = ResolveImpactedOutputs(snapshot.DownstreamImpact),
                LinkedCases = [],
                Route = "/workstation/accounting/security-master"
            });
        }

        return handoffs
            .OrderByDescending(static handoff => handoff.IsEnabled)
            .ThenBy(static handoff => handoff.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveHandoffOwner(string? target)
    {
        return NormalizeSecurityMasterText(target)?.ToLowerInvariant() switch
        {
            "portfolio" => "Portfolio operations",
            "ledger" => "Accounting operations",
            "reconciliation" => "Reconciliation owner",
            "reportpack" => "Reporting controller",
            "securitymasterdetail" => "Security Master steward",
            _ => "Security Master steward"
        };
    }

    private static string ResolveActionOwner(SecurityMasterRecommendedActionKind kind) =>
        kind switch
        {
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => "Portfolio operations",
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => "Accounting operations",
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => "Reconciliation owner",
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => "Reporting controller",
            SecurityMasterRecommendedActionKind.ResolveSelectedConflict or
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts or
            SecurityMasterRecommendedActionKind.EditSelectedSecurity or
            SecurityMasterRecommendedActionKind.RefreshTrustSnapshot => "Security Master steward",
            SecurityMasterRecommendedActionKind.BackfillTradingParameters or
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => "Reference-data steward",
            _ => "Security Master steward"
        };

    private static IReadOnlyList<string> ResolveImpactedOutputs(SecurityMasterRecommendedActionKind kind) =>
        kind switch
        {
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => ["Portfolio"],
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => ["Ledger", "Accounting"],
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => ["Reconciliation", "Close"],
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => ["Reporting", "Close"],
            SecurityMasterRecommendedActionKind.BackfillTradingParameters => ["Valuation", "Portfolio"],
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => ["Valuation", "Ledger", "Close"],
            SecurityMasterRecommendedActionKind.ResolveSelectedConflict or
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts => ["Portfolio", "Accounting", "Reconciliation", "Close", "Reporting"],
            _ => ["Security Master"]
        };

    private static IReadOnlyList<string> ResolveImpactedOutputs(string? target) =>
        NormalizeSecurityMasterText(target)?.ToLowerInvariant() switch
        {
            "portfolio" => ["Portfolio"],
            "ledger" => ["Ledger", "Accounting"],
            "reconciliation" => ["Reconciliation", "Close"],
            "reportpack" => ["Reporting", "Close"],
            _ => ["Security Master"]
        };

    private static IReadOnlyList<string> ResolveImpactedOutputs(SecurityMasterDownstreamImpactDto impact)
    {
        var outputs = new List<string>();
        if (impact.PortfolioExposureCount > 0)
        {
            outputs.Add("Portfolio");
        }

        if (impact.LedgerExposureCount > 0)
        {
            outputs.Add("Ledger");
            outputs.Add("Accounting");
        }

        if (impact.ReconciliationExposureCount > 0)
        {
            outputs.Add("Reconciliation");
            outputs.Add("Close");
        }

        if (impact.ReportPackExposureCount > 0)
        {
            outputs.Add("Reporting");
        }

        return outputs.Count == 0 ? ["Security Master"] : outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveActionRoute(SecurityMasterRecommendedActionDto action)
    {
        if (!string.IsNullOrWhiteSpace(action.Target) && action.Target.StartsWith('/'))
        {
            return action.Target;
        }

        return action.Kind switch
        {
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => "/workstation/portfolio",
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => "/workstation/accounting/ledger",
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => "/workstation/accounting/reconciliation",
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => "/workstation/reporting/evidence",
            SecurityMasterRecommendedActionKind.BackfillTradingParameters => "/workstation/data/backfills",
            SecurityMasterRecommendedActionKind.ResolveSelectedConflict when action.ConflictId.HasValue =>
                $"/workstation/accounting/security-master#conflict-{action.ConflictId.Value:D}",
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts => "/workstation/accounting/security-master#conflicts",
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => "/workstation/accounting/security-master#corporate-actions",
            SecurityMasterRecommendedActionKind.EditSelectedSecurity => "/workstation/accounting/security-master#passport",
            _ => "/workstation/accounting/security-master"
        };
    }

    private static string ResolveHandoffRoute(string? target) =>
        NormalizeSecurityMasterText(target)?.ToLowerInvariant() switch
        {
            "portfolio" => "/workstation/portfolio",
            "ledger" => "/workstation/accounting/ledger",
            "reconciliation" => "/workstation/accounting/reconciliation",
            "reportpack" => "/workstation/reporting/evidence",
            _ => "/workstation/accounting/security-master"
        };

    private static IReadOnlyList<InstrumentPassportProviderConfidenceDto> BuildProviderConfidence(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents)
    {
        var overrideHistory = lifecycleEvents
            .Where(IsOverrideHistory)
            .OrderByDescending(static item => item.ChangedAtUtc)
            .ToArray();

        return DeduplicateProviderConfidenceMappings(identifierSummary.ProviderMappings)
            .Select(mapping =>
            {
                var relatedConflicts = snapshot.ConflictAssessments
                    .Where(assessment => IsRelatedProviderConflict(mapping, assessment))
                    .ToArray();
                var freshnessAsOf = ResolveProviderMappingFreshness(snapshot, mapping, lifecycleEvents);
                var confidenceScore = ScoreProviderMapping(mapping, snapshot.TrustPosture, relatedConflicts);
                return new InstrumentPassportProviderConfidenceDto(
                    Provider: string.IsNullOrWhiteSpace(mapping.Provider) ? "unknown-provider" : mapping.Provider,
                    ProviderSource: mapping.MappingSource,
                    MappingKind: mapping.MappingKind,
                    Symbol: mapping.Value,
                    NormalizedSymbol: mapping.NormalizedValue,
                    IsPrimary: mapping.IsPrimary,
                    IsActive: mapping.IsActive,
                    FreshnessAsOf: freshnessAsOf,
                    FreshnessMinutes: freshnessAsOf.HasValue
                        ? Math.Max(0, (int)(snapshot.RetrievedAtUtc - freshnessAsOf.Value).TotalMinutes)
                        : null,
                    ConfidenceScore: confidenceScore,
                    ConfidenceReason: BuildProviderMappingConfidenceReason(mapping, snapshot.TrustPosture, relatedConflicts),
                    IdentifierConflictIds: relatedConflicts.Select(static assessment => assessment.Conflict.ConflictId).ToArray(),
                    IdentifierConflictSummaries: relatedConflicts.Select(BuildProviderIdentifierConflictSummary).ToArray(),
                    OverrideHistory: overrideHistory);
            })
            .OrderByDescending(static item => item.IsPrimary)
            .ThenByDescending(static item => item.ConfidenceScore)
            .ThenBy(static item => item.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildProviderIdentifierConflictSummary(SecurityMasterConflictAssessmentDto assessment)
    {
        var conflict = assessment.Conflict;
        return $"Identifier conflict {conflict.FieldPath}: {conflict.ProviderA} '{conflict.ValueA}' versus {conflict.ProviderB} '{conflict.ValueB}'. {assessment.ImpactSummary}";
    }

    private static IReadOnlyList<SecurityMasterProviderSymbolMappingDto> DeduplicateProviderConfidenceMappings(
        IReadOnlyList<SecurityMasterProviderSymbolMappingDto> providerMappings)
    {
        return providerMappings
            .GroupBy(
                static mapping => string.Join(
                    "|",
                    string.IsNullOrWhiteSpace(mapping.NormalizedProvider)
                        ? SecurityIdentifierNormalizer.NormalizeProvider(mapping.Provider)
                        : mapping.NormalizedProvider,
                    string.IsNullOrWhiteSpace(mapping.NormalizedValue)
                        ? mapping.Value.Trim().ToUpperInvariant()
                        : mapping.NormalizedValue),
                StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static mapping => mapping.IsActive && mapping.IsEnabled)
                .ThenByDescending(static mapping => mapping.IsPrimary)
                .ThenBy(static mapping => mapping.MappingSource.Equals("Identifier", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(static mapping => mapping.ValidFrom)
                .First())
            .ToArray();
    }

    private static DateTimeOffset? ResolveProviderMappingFreshness(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterProviderSymbolMappingDto mapping,
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents)
    {
        var sourceCandidateAsOf = snapshot.ProvenanceCandidates
            .Where(candidate =>
                ProviderMatches(mapping.Provider, candidate.SourceSystem) ||
                ProviderMatches(mapping.NormalizedProvider, candidate.SourceSystem))
            .Select(static candidate => candidate.AsOf)
            .Where(static asOf => asOf.HasValue)
            .OrderByDescending(static asOf => asOf)
            .FirstOrDefault();
        if (sourceCandidateAsOf.HasValue)
        {
            return sourceCandidateAsOf;
        }

        var lifecycleAsOf = lifecycleEvents
            .Where(item =>
                Contains(item.SourceSystem, mapping.Provider) ||
                Contains(item.SourceSystem, mapping.NormalizedProvider) ||
                item.ChangedFields.Any(field => Contains(field, mapping.MappingKind) || Contains(field, mapping.Value)))
            .Select(static item => item.ChangedAtUtc)
            .OrderByDescending(static item => item)
            .FirstOrDefault();

        return lifecycleAsOf == default ? mapping.ValidFrom : lifecycleAsOf;
    }

    private static decimal ScoreProviderMapping(
        SecurityMasterProviderSymbolMappingDto mapping,
        SecurityMasterTrustPostureDto trustPosture,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> relatedConflicts)
    {
        if (!mapping.IsActive || !mapping.IsEnabled)
        {
            return 0m;
        }

        if (relatedConflicts.Count > 0)
        {
            return Math.Min(55m, Math.Max(25m, trustPosture.TrustScore * 0.5m));
        }

        if (trustPosture.HasOpenConflicts)
        {
            return Math.Min(80m, Math.Max(60m, trustPosture.TrustScore));
        }

        return mapping.IsPrimary
            ? Math.Min(100m, Math.Max(85m, trustPosture.TrustScore))
            : Math.Min(90m, Math.Max(70m, trustPosture.TrustScore - 5m));
    }

    private static string BuildProviderMappingConfidenceReason(
        SecurityMasterProviderSymbolMappingDto mapping,
        SecurityMasterTrustPostureDto trustPosture,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> relatedConflicts)
    {
        if (!mapping.IsActive || !mapping.IsEnabled)
        {
            return "Provider mapping is inactive or disabled and cannot be trusted for workflow routing.";
        }

        if (relatedConflicts.Count > 0)
        {
            return $"{relatedConflicts.Count} open identifier conflict(s) involve this provider mapping.";
        }

        if (trustPosture.HasOpenConflicts)
        {
            return "Mapping is active, but the instrument still has unrelated open conflicts.";
        }

        return mapping.IsPrimary
            ? "Primary active provider mapping with no related open identifier conflicts."
            : "Active provider mapping with no related open identifier conflicts.";
    }

    private static bool IsRelatedProviderConflict(
        SecurityMasterProviderSymbolMappingDto mapping,
        SecurityMasterConflictAssessmentDto assessment)
    {
        var conflict = assessment.Conflict;
        return ProviderMatches(mapping.Provider, conflict.ProviderA) ||
               ProviderMatches(mapping.Provider, conflict.ProviderB) ||
               ProviderMatches(mapping.NormalizedProvider, conflict.ProviderA) ||
               ProviderMatches(mapping.NormalizedProvider, conflict.ProviderB) ||
               Contains(conflict.FieldPath, mapping.MappingKind) ||
               Contains(conflict.FieldPath, mapping.Value);
    }

    private static bool IsOverrideHistory(SecurityMasterChangeHistoryItemDto item) =>
        Contains(item.EventType, "Override") ||
        item.ChangedFields.Any(static field => Contains(field, "override")) ||
        Contains(item.Reason, "override");

    private static bool ProviderMatches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static InstrumentPassportPricingDto BuildPassportPricing(
        SecurityMasterTrustPostureDto trustPosture,
        TradingParametersDto? tradingParameters)
    {
        if (tradingParameters is null)
        {
            return new InstrumentPassportPricingDto(
                Status: "Missing",
                Summary: trustPosture.TradingParametersStatus,
                TradingParameters: null,
                LotSize: null,
                TickSize: null,
                ContractMultiplier: null,
                TradingHoursUtc: null,
                CircuitBreakerThresholdPct: null);
        }

        var hasUsableIncrement = tradingParameters.LotSize.HasValue || tradingParameters.TickSize.HasValue;
        var status = trustPosture.TradingParametersComplete || hasUsableIncrement ? "Ready" : "Review";
        var summary = trustPosture.TradingParametersComplete
            ? "Trading parameters are complete for pricing and execution workflows."
            : trustPosture.TradingParametersStatus;

        return new InstrumentPassportPricingDto(
            Status: status,
            Summary: summary,
            TradingParameters: tradingParameters,
            LotSize: tradingParameters.LotSize,
            TickSize: tradingParameters.TickSize,
            ContractMultiplier: tradingParameters.ContractMultiplier,
            TradingHoursUtc: tradingParameters.TradingHoursUtc,
            CircuitBreakerThresholdPct: tradingParameters.CircuitBreakerThresholdPct);
    }

    private static SecurityMasterIdentifierSummaryDto BuildFallbackIdentifierSummary(SecurityIdentityDrillInDto identity)
    {
        var activeIdentifiers = identity.Identifiers
            .Where(static identifier => !identifier.ValidTo.HasValue || identifier.ValidTo.Value > DateTimeOffset.UtcNow)
            .ToArray();
        var activeAliases = identity.Aliases
            .Where(static alias => alias.IsEnabled && (!alias.ValidTo.HasValue || alias.ValidTo.Value > DateTimeOffset.UtcNow))
            .ToArray();
        var providerMappings = activeIdentifiers
            .Where(static identifier => !string.IsNullOrWhiteSpace(identifier.Provider))
            .Select(static identifier => new SecurityMasterProviderSymbolMappingDto(
                MappingSource: "Identifier",
                MappingKind: identifier.Kind.ToString(),
                Value: identifier.Value,
                NormalizedValue: identifier.NormalizedValue ?? identifier.Value,
                Provider: identifier.Provider,
                NormalizedProvider: identifier.NormalizedProvider ?? identifier.Provider,
                IsPrimary: identifier.IsPrimary,
                IsEnabled: true,
                ValidFrom: identifier.ValidFrom,
                ValidTo: identifier.ValidTo,
                IsActive: true))
            .Concat(activeAliases.Select(static alias => new SecurityMasterProviderSymbolMappingDto(
                MappingSource: "Alias",
                MappingKind: alias.AliasKind,
                Value: alias.AliasValue,
                NormalizedValue: alias.AliasValue,
                Provider: alias.Provider,
                NormalizedProvider: alias.Provider,
                IsPrimary: false,
                IsEnabled: alias.IsEnabled,
                ValidFrom: alias.ValidFrom,
                ValidTo: alias.ValidTo,
                IsActive: true)))
            .OrderByDescending(static mapping => mapping.IsPrimary)
            .ThenBy(static mapping => mapping.MappingSource, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static mapping => mapping.MappingKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static mapping => mapping.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var primaryIdentifier = activeIdentifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? activeIdentifiers.FirstOrDefault();
        var distinctProviderCount = providerMappings
            .Select(static mapping => mapping.NormalizedProvider ?? mapping.Provider)
            .Where(static provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new SecurityMasterIdentifierSummaryDto(
            PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
            PrimaryIdentifierValue: primaryIdentifier?.Value,
            ActiveIdentifierCount: activeIdentifiers.Length,
            ActiveAliasCount: activeAliases.Length,
            ProviderMappingCount: providerMappings.Length,
            DistinctProviderCount: distinctProviderCount,
            HasPrimaryIdentifier: primaryIdentifier is not null,
            HasProviderMappings: providerMappings.Length > 0,
            Summary: providerMappings.Length > 0
                ? $"{providerMappings.Length} active provider mapping(s) across {distinctProviderCount} provider(s)."
                : $"{activeIdentifiers.Length} active identifier(s) and {activeAliases.Length} active alias(es).",
            ProviderMappings: providerMappings);
    }

    private static SecurityMasterChangeHistoryItemDto MapHistoryToLifecycleEvent(SecurityMasterEventEnvelope item)
    {
        var changedFields = ExtractChangedFields(item.Payload);
        return new SecurityMasterChangeHistoryItemDto(
            ChangeId: $"history-{item.StreamVersion}",
            StreamVersion: item.StreamVersion,
            EventType: item.EventType,
            ChangedAtUtc: item.EventTimestamp,
            EffectiveAtUtc: TryGetJsonDateTimeOffset(item.Payload, "effectiveAtUtc")
                ?? TryGetJsonDateTimeOffset(item.Payload, "effectiveFrom"),
            Actor: item.Actor,
            Origin: InferChangeOrigin(
                TryGetJsonString(item.Metadata, "sourceSystem") ?? string.Empty,
                item.Actor),
            SourceSystem: TryGetJsonString(item.Metadata, "sourceSystem")
                ?? TryGetJsonString(item.Metadata, "source")
                ?? "security-history",
            SourceRecordId: TryGetJsonString(item.Metadata, "sourceRecordId"),
            Reason: TryGetJsonString(item.Metadata, "reason"),
            Summary: $"{HumanizeEventType(item.EventType)} recorded for the instrument.",
            ChangedFields: changedFields,
            ChangedFieldsSummary: SummarizeChangedFields(changedFields));
    }

    public async Task<BulkResolveSecurityMasterConflictsResult> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var requestedConflictIds = request.ConflictIds?
            .Where(static conflictId => conflictId != Guid.Empty)
            .Distinct()
            .ToArray()
            ?? [];

        if (requestedConflictIds.Length == 0)
        {
            return new BulkResolveSecurityMasterConflictsResult(
                Requested: 0,
                Eligible: 0,
                Resolved: 0,
                Skipped: 0,
                ResolvedConflictIds: [],
                SkippedReasons: new Dictionary<Guid, string>());
        }

        var openConflicts = await _conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
        var openConflictMap = openConflicts
            .Where(conflict => string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static conflict => conflict.ConflictId, static conflict => conflict);

        var resolvedConflictIds = new List<Guid>(requestedConflictIds.Length);
        var skippedReasons = new Dictionary<Guid, string>();
        var contextCache = new Dictionary<Guid, SecurityWorkbenchContext>();
        var eligibleCount = 0;

        foreach (var conflictId in requestedConflictIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!openConflictMap.TryGetValue(conflictId, out var conflict))
            {
                skippedReasons[conflictId] = "Conflict is not open or no longer exists.";
                continue;
            }

            if (!contextCache.TryGetValue(conflict.SecurityId, out var context))
            {
                var loadedContext = await LoadContextAsync(conflict.SecurityId, request.FundProfileId, ct).ConfigureAwait(false);
                if (loadedContext is null)
                {
                    skippedReasons[conflictId] = "Security snapshot could not be loaded for bulk review.";
                    continue;
                }

                context = loadedContext;
                contextCache[conflict.SecurityId] = context;
            }

            var assessment = AssessConflict(
                conflict,
                context.Detail,
                context.EconomicDefinition,
                context.TradingParameters,
                context.WinningSource,
                context.DownstreamImpact);

            if (!assessment.IsBulkEligible)
            {
                skippedReasons[conflictId] = assessment.BulkIneligibilityReason ?? "Conflict does not meet the low-risk bulk policy.";
                continue;
            }

            eligibleCount++;
            var updated = await _conflictService
                .ResolveAsync(
                    new ResolveConflictRequest(
                        ConflictId: conflictId,
                        Resolution: assessment.RecommendedResolution,
                        ResolvedBy: request.ResolvedBy,
                        Reason: request.Reason),
                    ct)
                .ConfigureAwait(false);

            if (updated is null)
            {
                skippedReasons[conflictId] = "Conflict could not be resolved by the server.";
                continue;
            }

            resolvedConflictIds.Add(conflictId);
        }

        return new BulkResolveSecurityMasterConflictsResult(
            Requested: requestedConflictIds.Length,
            Eligible: eligibleCount,
            Resolved: resolvedConflictIds.Count,
            Skipped: requestedConflictIds.Length - resolvedConflictIds.Count,
            ResolvedConflictIds: resolvedConflictIds,
            SkippedReasons: skippedReasons);
    }

    private async Task<SecurityWorkbenchContext?> LoadContextAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct)
    {
        var detailTask = _queryService.GetByIdAsync(securityId, ct);
        var economicTask = _queryService.GetEconomicDefinitionByIdAsync(securityId, ct);
        var tradingTask = _queryService.GetTradingParametersAsync(securityId, DateTimeOffset.UtcNow, ct);

        await Task.WhenAll(detailTask, economicTask, tradingTask).ConfigureAwait(false);

        var detail = await detailTask.ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var economic = await economicTask.ConfigureAwait(false);
        var trading = await tradingTask.ConfigureAwait(false);
        var winningSource = ParseWinningSource(economic?.Provenance);
        var downstreamImpact = await BuildDownstreamImpactAsync(detail, fundProfileId, ct).ConfigureAwait(false);

        return new SecurityWorkbenchContext(detail, economic, trading, winningSource, downstreamImpact);
    }

    private async Task<SecurityMasterDownstreamImpactDto> BuildDownstreamImpactAsync(
        SecurityDetailDto detail,
        string? fundProfileId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return new SecurityMasterDownstreamImpactDto(
                FundProfileId: null,
                IsScoped: false,
                Severity: SecurityMasterImpactSeverity.Unknown,
                Summary: "Not scoped to a fund profile. Downstream impact is unknown.",
                PortfolioExposureSummary: "Portfolio impact is not scoped.",
                LedgerExposureSummary: "Ledger impact is not scoped.",
                ReconciliationExposureSummary: "Reconciliation impact is not scoped.",
                ReportPackExposureSummary: "Report-pack impact is not scoped.",
                MatchedRunCount: 0,
                PortfolioExposureCount: 0,
                LedgerExposureCount: 0,
                ReconciliationExposureCount: 0,
                ReportPackExposureCount: 0,
                Links: []);
        }

        var normalizedFundProfileId = fundProfileId.Trim();

        // Tenant isolation (SEC-005): a fund the registry reports as owned by another tenant must surface as
        // withheld/unknown — NOT as an empty "no runs" (Severity.None) impact. Reporting None would let
        // downstream low-risk gates (e.g. bulk conflict resolution, which only proceeds on None/Low) treat
        // a foreign scope as safe instead of forbidden; Unknown keeps it non-eligible while still disclosing
        // zero runs/exposures. Unbound/legacy/own funds and uncertainty all pass through and resolve below.
        if (!await IsFundAccessibleToCurrentTenantAsync(normalizedFundProfileId, ct).ConfigureAwait(false))
        {
            return BuildWithheldFundImpact(normalizedFundProfileId);
        }

        var relatedRuns = await LoadFundRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        if (relatedRuns.Count == 0)
        {
            return new SecurityMasterDownstreamImpactDto(
                FundProfileId: normalizedFundProfileId,
                IsScoped: true,
                Severity: SecurityMasterImpactSeverity.None,
                Summary: $"Fund profile {normalizedFundProfileId} has no recorded runs in the workstation store.",
                PortfolioExposureSummary: "No scoped portfolio exposure detected.",
                LedgerExposureSummary: "No scoped ledger exposure detected.",
                ReconciliationExposureSummary: "No scoped reconciliation exposure detected.",
                ReportPackExposureSummary: "No scoped report-pack exposure detected.",
                MatchedRunCount: 0,
                PortfolioExposureCount: 0,
                LedgerExposureCount: 0,
                ReconciliationExposureCount: 0,
                ReportPackExposureCount: 0,
                Links: []);
        }

        var portfolioTasks = relatedRuns
            .Select(run => _portfolioReadService.BuildSummaryAsync(run, ct))
            .ToArray();
        var ledgerTasks = relatedRuns
            .Select(run => _ledgerReadService.BuildSummaryAsync(run, ct))
            .ToArray();

        var combinedTasks = portfolioTasks
            .Cast<Task>()
            .Concat(ledgerTasks)
            .ToArray();

        await Task.WhenAll(combinedTasks).ConfigureAwait(false);

        var portfolios = new List<PortfolioSummary?>(portfolioTasks.Length);
        foreach (var task in portfolioTasks)
        {
            portfolios.Add(await task.ConfigureAwait(false));
        }

        var ledgers = new List<LedgerSummary?>(ledgerTasks.Length);
        foreach (var task in ledgerTasks)
        {
            ledgers.Add(await task.ConfigureAwait(false));
        }

        var normalizedIdentifiers = BuildNormalizedIdentifierSet(detail);
        var portfolioRunCount = 0;
        var portfolioExposureCount = 0;

        foreach (var portfolio in portfolios.Where(static portfolio => portfolio is not null))
        {
            var matches = portfolio!.Positions.Count(position => MatchesSecurity(position.Symbol, position.Security?.SecurityId, detail, normalizedIdentifiers));
            if (matches == 0)
            {
                continue;
            }

            portfolioRunCount++;
            portfolioExposureCount += matches;
        }

        var ledgerRunCount = 0;
        var ledgerExposureCount = 0;
        foreach (var ledger in ledgers.Where(static ledger => ledger is not null))
        {
            var matches = ledger!.TrialBalance.Count(line => MatchesSecurity(line.Symbol, line.Security?.SecurityId, detail, normalizedIdentifiers));
            if (matches == 0)
            {
                continue;
            }

            ledgerRunCount++;
            ledgerExposureCount += matches;
        }

        var reconciliationRunCount = 0;
        var reconciliationExposureCount = 0;
        var reconciliationUnavailableRunCount = 0;
        if (_reconciliationRunService is not null)
        {
            foreach (var run in relatedRuns)
            {
                ct.ThrowIfCancellationRequested();

                var detailResult = await _reconciliationRunService
                    .GetLatestForRunAsync(run.RunId, ct)
                    .ConfigureAwait(false);
                if (detailResult is null)
                {
                    reconciliationUnavailableRunCount++;
                    continue;
                }

                if (detailResult?.SecurityCoverageIssues is null)
                {
                    continue;
                }

                var issueMatches = detailResult.SecurityCoverageIssues.Count(issue =>
                    MatchesSecurity(issue.Symbol, securityId: null, detail, normalizedIdentifiers));
                if (issueMatches == 0)
                {
                    continue;
                }

                reconciliationRunCount++;
                reconciliationExposureCount += issueMatches;
            }
        }

        var reportPackExposureCount = 0;
        if (relatedRuns.Any(run => run.Metrics?.Ledger is not null))
        {
            var report = await _reportGenerationService
                .GenerateAsync(
                    new ReportRequest(
                        FundId: normalizedFundProfileId,
                        AsOf: DateTimeOffset.UtcNow,
                        FundLedger: BuildFundLedgerBook(normalizedFundProfileId, relatedRuns)),
                    ct)
                .ConfigureAwait(false);

            reportPackExposureCount = report.TrialBalance.Count(row =>
                MatchesSecurity(row.Symbol, securityId: null, detail, normalizedIdentifiers));
        }

        var severity = DetermineImpactSeverity(
            portfolioExposureCount,
            ledgerExposureCount,
            reconciliationExposureCount,
            reportPackExposureCount,
            reconciliationUnavailableRunCount);

        var overallSummary = BuildImpactSummary(
            relatedRuns.Count,
            portfolioExposureCount,
            ledgerExposureCount,
            reconciliationExposureCount,
            reportPackExposureCount,
            reconciliationUnavailableRunCount);
        var reconciliationExposureSummary = reconciliationExposureCount == 0
            ? reconciliationUnavailableRunCount == 0
                ? "No scoped reconciliation exposure detected."
                : $"Reconciliation impact has not been materialized for {reconciliationUnavailableRunCount} scoped run(s)."
            : reconciliationUnavailableRunCount == 0
                ? $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security."
                : $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security. Reconciliation impact is still unavailable for {reconciliationUnavailableRunCount} scoped run(s).";

        var links = new List<SecurityMasterImpactLinkDto>(4);
        if (portfolioExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "portfolio",
                Label: "Open Portfolio Impact",
                Summary: $"{portfolioExposureCount} position(s) across {portfolioRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.Low,
                IsActive: true));
        }

        if (ledgerExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "ledger",
                Label: "Open Ledger Impact",
                Summary: $"{ledgerExposureCount} ledger line(s) across {ledgerRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.Medium,
                IsActive: true));
        }

        if (reconciliationExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "reconciliation",
                Label: "Open Reconciliation Impact",
                Summary: $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.High,
                IsActive: true));
        }

        if (reportPackExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "reportPack",
                Label: "Open Report Pack Impact",
                Summary: $"{reportPackExposureCount} report-pack row(s) reference this security in the current fund scope.",
                Severity: SecurityMasterImpactSeverity.High,
                IsActive: true));
        }

        return new SecurityMasterDownstreamImpactDto(
            FundProfileId: normalizedFundProfileId,
            IsScoped: true,
            Severity: severity,
            Summary: overallSummary,
            PortfolioExposureSummary: portfolioExposureCount == 0
                ? "No scoped portfolio exposure detected."
                : $"{portfolioExposureCount} position(s) across {portfolioRunCount} run(s) reference this security.",
            LedgerExposureSummary: ledgerExposureCount == 0
                ? "No scoped ledger exposure detected."
                : $"{ledgerExposureCount} ledger line(s) across {ledgerRunCount} run(s) reference this security.",
            ReconciliationExposureSummary: reconciliationExposureSummary,
            ReportPackExposureSummary: reportPackExposureCount == 0
                ? "No scoped report-pack exposure detected."
                : $"{reportPackExposureCount} report-pack row(s) reference this security.",
            MatchedRunCount: relatedRuns.Count,
            PortfolioExposureCount: portfolioExposureCount,
            LedgerExposureCount: ledgerExposureCount,
            ReconciliationExposureCount: reconciliationExposureCount,
            ReportPackExposureCount: reportPackExposureCount,
            Links: links);
    }

    /// <summary>
    /// Enumerates the process-wide run store for a single fund. Callers MUST first gate access with
    /// <see cref="IsFundAccessibleToCurrentTenantAsync"/> (tenant isolation, SEC-005): the run store carries
    /// no tenant key, so a foreign fund's runs are withheld at the impact/open-lot boundaries — a foreign
    /// scope yields a withheld/Unknown impact and empty lots, never a misleading empty "no runs" result.
    /// </summary>
    private async Task<IReadOnlyList<StrategyRunEntry>> LoadFundRunsAsync(string fundProfileId, CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                runs.Add(run);
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedAt)
            .ThenBy(static run => run.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Whether the fund profile may be resolved for the current request's tenant. Returns true (pass
    /// through) when there is no tenancy registry, a blank fund, no HTTP request context (background/test),
    /// no caller tenant scope, or the registry is unavailable — only a positive "owned by a different
    /// tenant" verdict withholds the fund's data.
    /// </summary>
    private async Task<bool> IsFundAccessibleToCurrentTenantAsync(string fundProfileId, CancellationToken ct)
    {
        if (_tenancyRegistry is null || string.IsNullOrWhiteSpace(fundProfileId))
        {
            return true;
        }

        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext is null)
        {
            return true;
        }

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
        if (!tenant.HasTenantScope)
        {
            return true;
        }

        try
        {
            return await _tenancyRegistry
                .IsAccessibleAsync(fundProfileId, tenant.TenantId!, tenant.CompanyId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail open to the deployment boundary rather than dropping restatement impact on uncertainty.
            return true;
        }
    }

    /// <summary>
    /// Sanitizes an operator-supplied fund scope for tenant isolation (SEC-005): returns the fund unchanged
    /// when it is blank, owned by the caller's tenant, unbound/legacy, or accessibility cannot be decided
    /// (fail open), and returns <c>null</c> — i.e. treat the request as unscoped — when the registry
    /// positively attributes the fund to a different tenant. Used at the snapshot/passport entry so a
    /// foreign fund never reaches any fund-scoped evidence path.
    /// </summary>
    private async Task<string?> SanitizeFundScopeAsync(string? fundProfileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return fundProfileId;
        }

        return await IsFundAccessibleToCurrentTenantAsync(fundProfileId, ct).ConfigureAwait(false)
            ? fundProfileId
            : null;
    }

    /// <summary>
    /// Downstream-impact result for a fund the registry positively attributes to another tenant: scoped but
    /// withheld with <see cref="SecurityMasterImpactSeverity.Unknown"/> (SEC-005). Unknown — not None —
    /// keeps the foreign scope out of low-risk gates such as bulk conflict resolution while disclosing no
    /// runs or exposures.
    /// </summary>
    private static SecurityMasterDownstreamImpactDto BuildWithheldFundImpact(string fundProfileId)
        => new(
            FundProfileId: fundProfileId,
            IsScoped: true,
            Severity: SecurityMasterImpactSeverity.Unknown,
            Summary: $"Fund profile {fundProfileId} is owned by another tenant; downstream impact is withheld.",
            PortfolioExposureSummary: "Portfolio impact is withheld for a fund owned by another tenant.",
            LedgerExposureSummary: "Ledger impact is withheld for a fund owned by another tenant.",
            ReconciliationExposureSummary: "Reconciliation impact is withheld for a fund owned by another tenant.",
            ReportPackExposureSummary: "Report-pack impact is withheld for a fund owned by another tenant.",
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 0,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);

    private static FundLedgerBook BuildFundLedgerBook(string fundProfileId, IReadOnlyList<StrategyRunEntry> runs)
    {
        var ledgerBook = new FundLedgerBook(fundProfileId);
        foreach (var run in runs)
        {
            foreach (var journalEntry in run.Metrics?.Ledger?.Journal ?? [])
            {
                ledgerBook.FundLedger.Post(journalEntry);
            }
        }

        return ledgerBook;
    }

    private static SecurityMasterConflictAssessmentDto AssessConflict(
        SecurityMasterConflict conflict,
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        WinningSourceInfo? winningSource,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        var currentValue = ExtractCurrentFieldValue(conflict.FieldPath, detail, economic, trading);
        var winningSide = ResolveWinningSide(conflict, currentValue, winningSource);

        var currentWinningValue = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ValueB,
            _ => conflict.ValueA
        };
        var challengerValue = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ValueA,
            _ => conflict.ValueB
        };
        var currentWinningSource = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ProviderB,
            _ => conflict.ProviderA
        };
        var challengerSource = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ProviderA,
            _ => conflict.ProviderB
        };
        var recommendation = BuildRecommendation(currentWinningValue, challengerValue);
        var recommendedResolution = recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent => "Dismiss",
            SecurityMasterConflictRecommendationKind.Challenger => winningSide == ConflictSide.ProviderB ? "AcceptA" : "AcceptB",
            SecurityMasterConflictRecommendationKind.PreserveWinner => winningSide == ConflictSide.ProviderB ? "AcceptB" : "AcceptA",
            _ => string.Empty
        };

        var impactSeverity = DetermineConflictImpactSeverity(conflict, downstreamImpact);
        var impactSummary = BuildConflictImpactSummary(conflict.FieldPath, impactSeverity, downstreamImpact);
        var impactDetail = BuildConflictImpactDetail(conflict.FieldPath, impactSeverity, downstreamImpact);
        var winnerBlank = string.IsNullOrWhiteSpace(currentWinningValue);
        var challengerPresent = !string.IsNullOrWhiteSpace(challengerValue);
        var isOpen = string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase);
        var isBulkEligible = isOpen
            && impactSeverity is SecurityMasterImpactSeverity.None or SecurityMasterImpactSeverity.Low
            && (recommendation == SecurityMasterConflictRecommendationKind.DismissAsEquivalent
                || (winnerBlank && challengerPresent));

        var bulkIneligibilityReason = isBulkEligible
            ? null
            : !isOpen
                ? "Conflict is not open."
                : recommendation == SecurityMasterConflictRecommendationKind.ManualReview
                    ? "Conflict requires manual review."
                    : impactSeverity is SecurityMasterImpactSeverity.Unknown
                        ? "Downstream impact is not scoped."
                        : $"Impact severity is {impactSeverity}.";

        var recommendedWinner = recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent =>
                $"{currentWinningSource} and {challengerSource} normalize to the same value.",
            SecurityMasterConflictRecommendationKind.Challenger =>
                $"Accept {challengerSource} because the current winning value is blank.",
            SecurityMasterConflictRecommendationKind.PreserveWinner =>
                $"Preserve {currentWinningSource} as the current winner.",
            _ =>
                $"Manual review required between {currentWinningSource} and {challengerSource}."
        };

        return new SecurityMasterConflictAssessmentDto(
            Conflict: conflict,
            CurrentWinningValue: currentWinningValue,
            ChallengerValue: challengerValue,
            CurrentWinningSource: currentWinningSource,
            ChallengerSource: challengerSource,
            Recommendation: recommendation,
            RecommendedResolution: recommendedResolution,
            RecommendedWinner: recommendedWinner,
            ImpactSeverity: impactSeverity,
            ImpactSummary: impactSummary,
            ImpactDetail: impactDetail,
            IsBulkEligible: isBulkEligible,
            BulkIneligibilityReason: bulkIneligibilityReason);
    }

    private static SecurityMasterConflictRecommendationKind BuildRecommendation(string? currentWinningValue, string? challengerValue)
    {
        if (AreEquivalent(currentWinningValue, challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.DismissAsEquivalent;
        }

        if (string.IsNullOrWhiteSpace(currentWinningValue) && !string.IsNullOrWhiteSpace(challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.Challenger;
        }

        if (!string.IsNullOrWhiteSpace(currentWinningValue) && !string.IsNullOrWhiteSpace(challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.PreserveWinner;
        }

        return SecurityMasterConflictRecommendationKind.ManualReview;
    }

    private static ConflictSide ResolveWinningSide(
        SecurityMasterConflict conflict,
        string? currentValue,
        WinningSourceInfo? winningSource)
    {
        var normalizedCurrentValue = NormalizeComparableString(currentValue);
        if (!string.IsNullOrWhiteSpace(normalizedCurrentValue))
        {
            var matchesA = string.Equals(NormalizeComparableString(conflict.ValueA), normalizedCurrentValue, StringComparison.Ordinal);
            var matchesB = string.Equals(NormalizeComparableString(conflict.ValueB), normalizedCurrentValue, StringComparison.Ordinal);

            if (matchesA && !matchesB)
            {
                return ConflictSide.ProviderA;
            }

            if (matchesB && !matchesA)
            {
                return ConflictSide.ProviderB;
            }
        }

        if (!string.IsNullOrWhiteSpace(winningSource?.SourceSystem))
        {
            var sourceMatchesA = string.Equals(winningSource.SourceSystem, conflict.ProviderA, StringComparison.OrdinalIgnoreCase);
            var sourceMatchesB = string.Equals(winningSource.SourceSystem, conflict.ProviderB, StringComparison.OrdinalIgnoreCase);
            if (sourceMatchesA && !sourceMatchesB)
            {
                return ConflictSide.ProviderA;
            }

            if (sourceMatchesB && !sourceMatchesA)
            {
                return ConflictSide.ProviderB;
            }
        }

        return ConflictSide.ProviderA;
    }

    private static SecurityMasterTrustPostureDto BuildTrustPosture(
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        IReadOnlyList<CorporateActionDto> corporateActions,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments,
        WinningSourceInfo? winningSource,
        SecurityValidationReportDto validationReport)
    {
        var missingTradingFields = GetMissingTradingParameterFields(economic?.AssetClass, trading);
        var openConflictCount = assessments.Count;
        var blockingValidationCount = validationReport.CriticalIssueCount + validationReport.ErrorIssueCount;
        var advisoryValidationCount = Math.Max(0, validationReport.Issues.Count - blockingValidationCount);
        // Effective view only: amendments fold to their latest terms and cancelled actions
        // stop counting against trust, so the posture reflects what will actually happen.
        var upcomingCorporateActions = CorporateActionEffectiveStateProjector
            .ProjectEffectiveActions(corporateActions, DateTimeOffset.UtcNow)
            .Where(action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderBy(static action => action.ExDate)
            .Take(5)
            .ToArray();
        var corporateActionsTrusted = upcomingCorporateActions.Length == 0;

        var tone = openConflictCount > 0 || blockingValidationCount > 0
            ? SecurityMasterTrustTone.Blocked
            : missingTradingFields.Count > 0 || !corporateActionsTrusted || advisoryValidationCount > 0
                ? SecurityMasterTrustTone.Review
                : SecurityMasterTrustTone.Trusted;

        var trustScore = Math.Clamp(
            100
            - (openConflictCount * 25)
            - (blockingValidationCount * 12)
            - (advisoryValidationCount * 3)
            - (missingTradingFields.Count * 10)
            - (upcomingCorporateActions.Length > 0 ? 10 : 0),
            0,
            100);

        var summaryParts = new List<string>(3);
        if (openConflictCount > 0)
        {
            summaryParts.Add($"{openConflictCount} open conflict{(openConflictCount == 1 ? string.Empty : "s")}");
        }

        if (blockingValidationCount > 0)
        {
            summaryParts.Add($"{blockingValidationCount} blocking validation issue{(blockingValidationCount == 1 ? string.Empty : "s")}");
        }

        if (advisoryValidationCount > 0)
        {
            summaryParts.Add($"{advisoryValidationCount} advisory validation issue{(advisoryValidationCount == 1 ? string.Empty : "s")}");
        }

        var summary = tone switch
        {
            SecurityMasterTrustTone.Blocked when summaryParts.Count > 0 =>
                $"Trust is blocked by {string.Join(" and ", summaryParts)}.",
            SecurityMasterTrustTone.Review when missingTradingFields.Count > 0 =>
                $"Golden copy is stable, but trading readiness is incomplete: {string.Join(", ", missingTradingFields)}.",
            SecurityMasterTrustTone.Review when advisoryValidationCount > 0 =>
                $"Golden copy is stable, but validation still reports {advisoryValidationCount} advisory issue{(advisoryValidationCount == 1 ? string.Empty : "s")}.",
            SecurityMasterTrustTone.Review =>
                "Golden copy is stable, but upcoming corporate actions still require operator review.",
            SecurityMasterTrustTone.Trusted =>
                "Golden copy is trusted for downstream Accounting and Reporting workflows.",
            _ =>
                "Trust posture is unavailable."
        };

        var corporateActionReadiness = upcomingCorporateActions.Length == 0
            ? "No upcoming corporate actions are scheduled in the current review window."
            : upcomingCorporateActions.Length == 1
                ? $"Upcoming {CorporateActionTypeDescriptorCatalog.Find(upcomingCorporateActions[0].EventType)?.DisplayName ?? upcomingCorporateActions[0].EventType} on {upcomingCorporateActions[0].ExDate:yyyy-MM-dd} should be reviewed before downstream close."
                : $"{upcomingCorporateActions.Length} upcoming corporate actions should be reviewed before downstream close.";

        return new SecurityMasterTrustPostureDto(
            Tone: tone,
            TrustScore: trustScore,
            Summary: summary,
            GoldenCopySource: winningSource?.SourceSystem ?? "Unknown source",
            GoldenCopyRule: "Preserve winner unless the current winner is blank or the values are equivalent.",
            TradingParametersStatus: missingTradingFields.Count == 0
                ? trading is null
                    ? "Trading parameters could not be confirmed from the query surface."
                    : $"Trading parameters complete as of {trading.AsOf.LocalDateTime:g}."
                : $"Trading parameters incomplete: missing {string.Join(", ", missingTradingFields)}.",
            CorporateActionReadiness: corporateActionReadiness,
            HasOpenConflicts: openConflictCount > 0,
            OpenConflictCount: openConflictCount,
            TradingParametersComplete: missingTradingFields.Count == 0,
            HasUpcomingCorporateActions: upcomingCorporateActions.Length > 0,
            CorporateActionsTrusted: corporateActionsTrusted);
    }

    private static IReadOnlyList<SecurityMasterSourceCandidateDto> BuildProvenanceCandidates(
        SecurityDetailDto detail,
        WinningSourceInfo? winningSource,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments)
    {
        var candidates = new List<SecurityMasterSourceCandidateDto>(assessments.Count + 1);
        if (winningSource is not null)
        {
            candidates.Add(new SecurityMasterSourceCandidateDto(
                ConflictId: null,
                FieldPath: "EconomicDefinition",
                SourceSystem: winningSource.SourceSystem,
                DisplayValue: detail.DisplayName,
                IsWinningSource: true,
                AsOf: winningSource.AsOf,
                UpdatedBy: winningSource.UpdatedBy,
                Reason: winningSource.Reason,
                SourceRecordId: winningSource.SourceRecordId,
                ImpactSeverity: SecurityMasterImpactSeverity.None));
        }

        foreach (var assessment in assessments)
        {
            candidates.Add(new SecurityMasterSourceCandidateDto(
                ConflictId: assessment.Conflict.ConflictId,
                FieldPath: assessment.Conflict.FieldPath,
                SourceSystem: assessment.ChallengerSource,
                DisplayValue: assessment.ChallengerValue ?? string.Empty,
                IsWinningSource: false,
                AsOf: assessment.Conflict.DetectedAt,
                UpdatedBy: null,
                Reason: assessment.ImpactSummary,
                SourceRecordId: null,
                ImpactSeverity: assessment.ImpactSeverity));
        }

        return candidates;
    }

    private static IReadOnlyList<SecurityMasterRecommendedActionDto> BuildRecommendedActions(
        SecurityDetailDto detail,
        SecurityMasterTrustPostureDto trustPosture,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments,
        SecurityMasterDownstreamImpactDto downstreamImpact,
        SecurityValidationReportDto validationReport)
    {
        var actions = new List<SecurityMasterRecommendedActionDto>();
        var selectedConflict = assessments.FirstOrDefault(static assessment =>
            string.Equals(assessment.Conflict.Status, "Open", StringComparison.OrdinalIgnoreCase));

        if (selectedConflict is not null)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ResolveSelectedConflict,
                Title: $"Resolve {FormatFieldLabel(selectedConflict.Conflict.FieldPath)}",
                Detail: $"{selectedConflict.RecommendedWinner} {selectedConflict.ImpactSummary}",
                IsPrimary: true,
                IsEnabled: selectedConflict.Recommendation != SecurityMasterConflictRecommendationKind.ManualReview,
                ConflictId: selectedConflict.Conflict.ConflictId));
        }

        var bulkEligibleCount = assessments.Count(static assessment => assessment.IsBulkEligible);
        if (bulkEligibleCount > 0)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts,
                Title: "Apply low-risk bulk resolutions",
                Detail: $"{bulkEligibleCount} conflict(s) qualify for low-risk bulk assist.",
                IsPrimary: selectedConflict is null,
                IsEnabled: true));
        }

        var missingTradingFields = GetMissingTradingParameterFields(detail.AssetClass, null);
        if (!trustPosture.TradingParametersComplete)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BackfillTradingParameters,
                Title: "Backfill trading parameters",
                Detail: trustPosture.TradingParametersStatus,
                IsPrimary: false,
                IsEnabled: true));
        }

        if (!trustPosture.CorporateActionsTrusted)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ReviewCorporateActions,
                Title: "Review corporate actions",
                Detail: trustPosture.CorporateActionReadiness,
                IsPrimary: false,
                IsEnabled: true));
        }

        foreach (var link in downstreamImpact.Links.OrderBy(GetImpactLinkOrder))
        {
            var kind = link.Target switch
            {
                "reconciliation" => SecurityMasterRecommendedActionKind.OpenReconciliationImpact,
                "ledger" => SecurityMasterRecommendedActionKind.OpenLedgerImpact,
                "reportPack" => SecurityMasterRecommendedActionKind.OpenReportPackImpact,
                "portfolio" => SecurityMasterRecommendedActionKind.OpenPortfolioImpact,
                _ => SecurityMasterRecommendedActionKind.RefreshTrustSnapshot
            };

            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: kind,
                Title: link.Label,
                Detail: link.Summary,
                IsPrimary: false,
                IsEnabled: link.IsActive,
                Target: link.Target));
        }

        actions.Add(new SecurityMasterRecommendedActionDto(
            Kind: SecurityMasterRecommendedActionKind.EditSelectedSecurity,
            Title: "Edit selected security",
            Detail: validationReport.HasBlockingIssues
                ? $"Resolve {validationReport.CriticalIssueCount + validationReport.ErrorIssueCount} blocking validation issue(s) before downstream workflows consume the golden copy."
                : validationReport.Issues.Count > 0
                    ? $"Review {validationReport.Issues.Count} advisory validation issue(s) before promoting the selected security."
                    : "Make a governed amendment to the golden copy after completing triage.",
            IsPrimary: false,
            IsEnabled: true));

        return actions;
    }

    private static SecurityMasterIdentifierSummaryDto BuildIdentifierSummary(SecurityDetailDto detail)
    {
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var activeIdentifiers = detail.Identifiers
            .Where(identifier => IsIdentifierActive(identifier.ValidFrom, identifier.ValidTo, evaluatedAtUtc))
            .ToArray();
        var activeAliases = detail.Aliases
            .Where(alias => alias.IsEnabled && IsIdentifierActive(alias.ValidFrom, alias.ValidTo, evaluatedAtUtc))
            .ToArray();
        var primaryIdentifier = activeIdentifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? activeIdentifiers.FirstOrDefault();

        var providerMappings = new List<SecurityMasterProviderSymbolMappingDto>();
        providerMappings.AddRange(activeIdentifiers
            .Where(static identifier =>
                identifier.Kind == SecurityIdentifierKind.ProviderSymbol ||
                !string.IsNullOrWhiteSpace(identifier.Provider))
            .Select(identifier => new SecurityMasterProviderSymbolMappingDto(
                MappingSource: "Identifier",
                MappingKind: identifier.Kind.ToString(),
                Value: identifier.Value,
                NormalizedValue: SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier),
                Provider: identifier.Provider,
                NormalizedProvider: SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier),
                IsPrimary: identifier.IsPrimary,
                IsEnabled: true,
                ValidFrom: identifier.ValidFrom,
                ValidTo: identifier.ValidTo,
                IsActive: true)));
        providerMappings.AddRange(activeAliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias.Provider))
            .Select(alias => new SecurityMasterProviderSymbolMappingDto(
                MappingSource: "Alias",
                MappingKind: alias.AliasKind,
                Value: alias.AliasValue,
                NormalizedValue: NormalizeAliasValue(alias),
                Provider: alias.Provider,
                NormalizedProvider: SecurityIdentifierNormalizer.NormalizeProvider(alias.Provider),
                IsPrimary: false,
                IsEnabled: alias.IsEnabled,
                ValidFrom: alias.ValidFrom,
                ValidTo: alias.ValidTo,
                IsActive: true)));

        var distinctProviderCount = providerMappings
            .Select(mapping => mapping.NormalizedProvider)
            .Where(static provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var summary = $"{activeIdentifiers.Length} active identifier(s), {activeAliases.Length} active alias(es), {providerMappings.Count} provider mapping(s).";

        return new SecurityMasterIdentifierSummaryDto(
            PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
            PrimaryIdentifierValue: primaryIdentifier?.Value,
            ActiveIdentifierCount: activeIdentifiers.Length,
            ActiveAliasCount: activeAliases.Length,
            ProviderMappingCount: providerMappings.Count,
            DistinctProviderCount: distinctProviderCount,
            HasPrimaryIdentifier: primaryIdentifier is not null,
            HasProviderMappings: providerMappings.Count > 0,
            Summary: summary,
            ProviderMappings: providerMappings
                .OrderBy(mapping => mapping.MappingSource, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.MappingKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mapping => mapping.NormalizedProvider, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.NormalizedValue, StringComparer.Ordinal)
                .ToArray());
    }

    private static SecurityMasterSchemaCompatibilityDto BuildSchemaCompatibility(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic)
    {
        var legacySchemaVersion = TryGetSchemaVersion(detail.AssetSpecificTerms) ?? SecurityMasterSchemaVersions.LegacyAssetSpecificTerms;
        var economicSchemaVersion = economic is null ? 0 : TryGetSchemaVersion(economic.EconomicTerms) ?? SecurityMasterSchemaVersions.EconomicTerms;
        var hasLegacyTerms = HasJsonContent(detail.AssetSpecificTerms);
        var hasEconomicTerms = economic is not null && HasJsonContent(economic.EconomicTerms);
        var hasClassificationPayload = economic is not null && HasJsonContent(economic.Classification);
        var hasSupportedLegacySchema = !hasLegacyTerms || legacySchemaVersion == SecurityMasterSchemaVersions.LegacyAssetSpecificTerms;
        var hasSupportedEconomicSchema = !hasEconomicTerms || economicSchemaVersion == SecurityMasterSchemaVersions.EconomicTerms;

        string summary;
        if (!hasSupportedLegacySchema && !hasSupportedEconomicSchema)
        {
            summary = $"Legacy schema v{legacySchemaVersion} and economic schema v{economicSchemaVersion} differ from the supported workstation versions v{SecurityMasterSchemaVersions.LegacyAssetSpecificTerms}/v{SecurityMasterSchemaVersions.EconomicTerms}; compatibility review is required.";
        }
        else if (!hasSupportedLegacySchema)
        {
            summary = $"Legacy schema v{legacySchemaVersion} differs from supported workstation schema v{SecurityMasterSchemaVersions.LegacyAssetSpecificTerms}; compatibility review is required.";
        }
        else if (!hasSupportedEconomicSchema)
        {
            summary = $"Economic schema v{economicSchemaVersion} differs from supported workstation schema v{SecurityMasterSchemaVersions.EconomicTerms}; compatibility review is required.";
        }
        else if (hasLegacyTerms && hasEconomicTerms)
        {
            summary = $"Legacy schema v{legacySchemaVersion} and economic schema v{economicSchemaVersion} are both available for compatibility-safe projections.";
        }
        else if (hasLegacyTerms)
        {
            summary = $"Legacy schema v{legacySchemaVersion} remains the authoritative workstation projection payload.";
        }
        else if (hasEconomicTerms)
        {
            summary = $"Economic schema v{economicSchemaVersion} is available without a legacy asset-specific payload.";
        }
        else
        {
            summary = "No structured schema payloads were rebuilt for the selected security.";
        }

        return new SecurityMasterSchemaCompatibilityDto(
            AssetClass: detail.AssetClass,
            LegacyAssetSpecificTermsSchemaVersion: legacySchemaVersion,
            EconomicTermsSchemaVersion: economicSchemaVersion,
            HasLegacyAssetSpecificTerms: hasLegacyTerms,
            HasEconomicTerms: hasEconomicTerms,
            HasClassificationPayload: hasClassificationPayload,
            Summary: summary);
    }

    private static IReadOnlyList<SecurityMasterChangeHistoryItemDto> BuildChangeHistory(
        IReadOnlyList<SecurityMasterEventEnvelope> history)
    {
        if (history.Count == 0)
        {
            return [];
        }

        var ordered = history
            .OrderBy(static item => item.StreamVersion)
            .ThenBy(static item => item.EventTimestamp)
            .ToArray();
        var items = new List<SecurityMasterChangeHistoryItemDto>(ordered.Length);
        SecurityEconomicDefinitionRecord? previousRecord = null;

        foreach (var envelope in ordered)
        {
            var currentRecord = TryParseHistoryRecord(envelope.Payload);
            var payloadProvenance = currentRecord?.Provenance;
            var sourceSystem =
                CoalesceNonEmpty(
                    TryGetJsonString(envelope.Metadata, "sourceSystem"),
                    TryGetJsonString(envelope.Metadata, "source"),
                    payloadProvenance.HasValue ? TryGetJsonString(payloadProvenance.Value, "sourceSystem") : null)
                ?? "Unknown";
            var sourceRecordId =
                CoalesceNonEmpty(
                    TryGetJsonString(envelope.Metadata, "sourceRecordId"),
                    payloadProvenance.HasValue ? TryGetJsonString(payloadProvenance.Value, "sourceRecordId") : null);
            var reason =
                CoalesceNonEmpty(
                    TryGetJsonString(envelope.Metadata, "reason"),
                    payloadProvenance.HasValue ? TryGetJsonString(payloadProvenance.Value, "reason") : null);
            var effectiveAtUtc = payloadProvenance.HasValue
                ? TryGetJsonDateTimeOffset(payloadProvenance.Value, "asOf")
                : null;
            var changedFields = BuildChangedFields(previousRecord, currentRecord, envelope.EventType);
            var changedFieldsSummary = changedFields.Count == 0
                ? "No structured field diff available."
                : string.Join(", ", changedFields);

            items.Add(new SecurityMasterChangeHistoryItemDto(
                ChangeId: $"{envelope.StreamVersion}:{envelope.EventType}",
                StreamVersion: envelope.StreamVersion,
                EventType: envelope.EventType,
                ChangedAtUtc: envelope.EventTimestamp,
                EffectiveAtUtc: effectiveAtUtc,
                Actor: envelope.Actor,
                Origin: InferChangeOrigin(sourceSystem, envelope.Actor),
                SourceSystem: sourceSystem,
                SourceRecordId: sourceRecordId,
                Reason: reason,
                Summary: BuildChangeSummary(envelope, currentRecord, changedFields, sourceSystem),
                ChangedFields: changedFields,
                ChangedFieldsSummary: changedFieldsSummary));

            if (currentRecord is not null)
            {
                previousRecord = currentRecord;
            }
        }

        return items
            .OrderByDescending(static item => item.ChangedAtUtc)
            .ThenByDescending(static item => item.StreamVersion)
            .ToArray();
    }

    private static SecurityMasterScheduleSummaryDto BuildScheduleSummary(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        IReadOnlyList<CorporateActionDto> corporateActions,
        WinningSourceInfo? winningSource)
    {
        var assetCapability = GetAssetCapability(detail.AssetClass);
        var economicTerms = economic?.EconomicTerms;
        var supportsFactorHistory = economicTerms.HasValue && TryGetPropertyCaseInsensitive(economicTerms.Value, "structuredProduct", out _);
        var supportsCashflowSchedule =
            supportsFactorHistory
            || economicTerms.HasValue && (
                TryGetPropertyCaseInsensitive(economicTerms.Value, "coupon", out _)
                || TryGetPropertyCaseInsensitive(economicTerms.Value, "payment", out _)
                || TryGetPropertyCaseInsensitive(economicTerms.Value, "redemption", out _)
                || TryGetPropertyCaseInsensitive(economicTerms.Value, "call", out _))
            || assetCapability.SupportsCashflowScheduleByDefault;

        var currentFactor = TryGetNestedJsonDecimal(economicTerms, "structuredProduct", "factor");
        var currentFactorDate = TryGetNestedJsonDateOnly(economicTerms, "structuredProduct", "factorDate");
        var maturityDate = TryGetNestedJsonDateOnly(economicTerms, "maturity", "maturityDate");
        var firstCallDate = TryGetNestedJsonDateOnly(economicTerms, "call", "firstCallDate");
        var nextCorporateAction = corporateActions
            .Where(static action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderBy(static action => action.ExDate)
            .Select(static action => (DateOnly?)action.ExDate)
            .FirstOrDefault();
        var nextLifecycleDate = new[] { nextCorporateAction, firstCallDate, maturityDate }
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .OrderBy(static value => value)
            .Cast<DateOnly?>()
            .FirstOrDefault();

        var sourceSummary = winningSource is null
            ? "Schedule source metadata unavailable."
            : string.IsNullOrWhiteSpace(winningSource.SourceRecordId)
                ? $"Schedules follow {winningSource.SourceSystem} as of {winningSource.AsOf?.UtcDateTime:yyyy-MM-dd HH:mm 'UTC'}."
                : $"Schedules follow {winningSource.SourceSystem} record {winningSource.SourceRecordId} as of {winningSource.AsOf?.UtcDateTime:yyyy-MM-dd HH:mm 'UTC'}.";

        string summary;
        if (!supportsCashflowSchedule && !supportsFactorHistory)
        {
            summary = "Selected security does not expose cash-flow or factor schedule terms in the current projection.";
        }
        else if (supportsFactorHistory && currentFactor.HasValue)
        {
            summary = currentFactorDate.HasValue
                ? $"Factor-aware cash-flow support is available. Current factor {currentFactor.Value:0.########} as of {currentFactorDate.Value:yyyy-MM-dd}."
                : $"Factor-aware cash-flow support is available. Current factor {currentFactor.Value:0.########}.";
        }
        else if (supportsCashflowSchedule)
        {
            summary = nextLifecycleDate.HasValue
                ? $"Cash-flow schedule support is available. Next lifecycle date {nextLifecycleDate.Value:yyyy-MM-dd}."
                : "Cash-flow schedule support is available from the current economic terms.";
        }
        else
        {
            summary = "Factor history is available without a projected cash-flow schedule.";
        }

        return new SecurityMasterScheduleSummaryDto(
            SupportsCashflowSchedule: supportsCashflowSchedule,
            SupportsFactorHistory: supportsFactorHistory,
            HasEconomicScheduleTerms: economicTerms.HasValue && HasJsonContent(economicTerms.Value),
            CurrentFactor: currentFactor,
            CurrentFactorDate: currentFactorDate,
            NextLifecycleDate: nextLifecycleDate,
            SourceSummary: sourceSummary,
            Summary: summary);
    }

    private static SecurityMasterLotModelDto BuildLotModel(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading)
    {
        var assetCapability = GetAssetCapability(detail.AssetClass);
        var economicTerms = economic?.EconomicTerms;
        var hasStructuredFactor = TryGetNestedJsonDecimal(economicTerms, "structuredProduct", "factor").HasValue;
        var multiplier =
            trading?.ContractMultiplier
            ?? TryGetJsonDecimal(detail.AssetSpecificTerms, "multiplier")
            ?? TryGetNestedJsonDecimal(economicTerms, "contract", "multiplier");
        var lotSize = trading?.LotSize ?? TryGetJsonDecimal(detail.CommonTerms, "lotSize");

        var quantityModel =
            hasStructuredFactor ? "FactorAdjustedFace" :
            assetCapability.UsesFaceValueLots ? "FaceValue" :
            multiplier is > 0m ? "ContractUnits" :
            "Units";

        var summary = quantityModel switch
        {
            "FactorAdjustedFace" => $"Lots should reconcile by current face using factor-adjusted exposure{FormatLotSizeSuffix(lotSize)}.",
            "FaceValue" => $"Lots should reconcile by face/par exposure{FormatLotSizeSuffix(lotSize)}.",
            "ContractUnits" => multiplier is > 0m
                ? $"Lots should reconcile in contract units with multiplier {multiplier:0.########}{FormatLotSizeSuffix(lotSize)}."
                : $"Lots should reconcile in contract units{FormatLotSizeSuffix(lotSize)}.",
            _ => $"Lots should reconcile in whole or fractional units{FormatLotSizeSuffix(lotSize)}."
        };

        return new SecurityMasterLotModelDto(
            QuantityModel: quantityModel,
            LotSize: lotSize,
            ContractMultiplier: multiplier,
            UsesFaceValue: assetCapability.UsesFaceValueLots,
            SupportsFactorAdjustedExposure: hasStructuredFactor,
            RequiresResolvedSecurityId: true,
            Summary: summary);
    }

    private static SecurityMasterScheduleBookDto BuildScheduleBook(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        IReadOnlyList<CorporateActionDto> corporateActions,
        IReadOnlyList<SecurityMasterEventEnvelope> history,
        WinningSourceInfo? winningSource,
        SecurityMasterScheduleSummaryDto summary)
    {
        var economicTerms = economic?.EconomicTerms;
        var currency = economic?.Currency ?? detail.Currency;
        var events = new List<SecurityMasterScheduleEventDto>();

        AppendScheduleEventsFromArray(events, economicTerms, "cashflowSchedule", currency, winningSource);
        AppendScheduleEventsFromArray(events, economicTerms, "cashflows", currency, winningSource);
        AppendScheduleEventsFromArray(events, economicTerms, "paymentSchedule", currency, winningSource);

        var factorHistory = BuildFactorHistory(economicTerms, winningSource, summary.CurrentFactor, summary.CurrentFactorDate);
        foreach (var point in factorHistory)
        {
            if (events.Any(existing => string.Equals(existing.EventType, "FactorUpdate", StringComparison.OrdinalIgnoreCase)
                && existing.EffectiveDate == point.EffectiveDate
                && existing.FactorEnd == point.Factor))
            {
                continue;
            }

            events.Add(new SecurityMasterScheduleEventDto(
                EventId: $"factor-{point.EffectiveDate:yyyyMMdd}",
                EventType: "FactorUpdate",
                EffectiveDate: point.EffectiveDate,
                PayDate: null,
                AccrualStartDate: null,
                AccrualEndDate: null,
                ExpectedAmount: null,
                ActualAmount: null,
                VarianceAmount: null,
                FactorStart: point.PreviousFactor,
                FactorEnd: point.Factor,
                Currency: currency,
                PostingStatus: point.IsCurrentFactor ? "Posted" : "Reference",
                SourceSystem: point.SourceSystem,
                SourceRecordId: point.SourceRecordId,
                SourceAsOfUtc: point.SourceAsOfUtc,
                SourceUpdatedBy: point.SourceUpdatedBy,
                SourceReason: point.SourceReason,
                IsDerivedFromEconomicTerms: true,
                IsCurrentProjection: point.IsCurrentFactor));
        }

        AppendLifecycleEvent(events, "Call", TryGetNestedJsonDateOnly(economicTerms, "call", "firstCallDate"), currency, winningSource);
        AppendLifecycleEvent(events, "Maturity", TryGetNestedJsonDateOnly(economicTerms, "maturity", "maturityDate"), currency, winningSource);

        foreach (var action in corporateActions)
        {
            var postingStatus = action.PayDate.HasValue && action.PayDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
                ? "Posted"
                : action.ExDate < DateOnly.FromDateTime(DateTime.UtcNow)
                    ? "Pending"
                    : "Forecast";

            events.Add(new SecurityMasterScheduleEventDto(
                EventId: $"corp-{action.CorpActId:N}",
                EventType: string.IsNullOrWhiteSpace(action.EventType) ? "CorporateAction" : action.EventType.Trim(),
                EffectiveDate: action.ExDate,
                PayDate: action.PayDate,
                AccrualStartDate: null,
                AccrualEndDate: null,
                ExpectedAmount: action.DividendPerShare,
                ActualAmount: null,
                VarianceAmount: null,
                FactorStart: null,
                FactorEnd: action.SplitRatio ?? action.DistributionRatio ?? action.ExchangeRatio,
                Currency: string.IsNullOrWhiteSpace(action.Currency) ? currency : action.Currency.Trim(),
                PostingStatus: postingStatus,
                SourceSystem: "corporate-action-stream",
                SourceRecordId: action.CorpActId.ToString("N"),
                SourceAsOfUtc: null,
                SourceUpdatedBy: null,
                SourceReason: "Corporate action event attached to the security history.",
                IsDerivedFromEconomicTerms: false,
                IsCurrentProjection: action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow)));
        }

        var orderedEvents = events
            .OrderBy(static item => item.EffectiveDate)
            .ThenBy(static item => item.EventType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var provenanceHistory = BuildScheduleProvenanceHistory(orderedEvents, factorHistory, history, winningSource);

        return new SecurityMasterScheduleBookDto(
            SupportsCashflowSchedule: summary.SupportsCashflowSchedule,
            SupportsFactorHistory: summary.SupportsFactorHistory,
            HasEconomicScheduleTerms: summary.HasEconomicScheduleTerms,
            Currency: currency,
            CurrentFactor: summary.CurrentFactor,
            CurrentFactorDate: summary.CurrentFactorDate,
            NextLifecycleDate: summary.NextLifecycleDate,
            SourceSummary: summary.SourceSummary,
            Summary: summary.Summary,
            Events: orderedEvents,
            FactorHistory: factorHistory,
            ProvenanceHistory: provenanceHistory);
    }

    private async Task<SecurityMasterOpenLotReadModelDto> BuildOpenLotReadModelAsync(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        string? fundProfileId,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        var lotModel = BuildLotModel(detail, economic, trading);
        var currentFactor = TryGetNestedJsonDecimal(economic?.EconomicTerms, "structuredProduct", "factor");
        var currentFactorDate = TryGetNestedJsonDateOnly(economic?.EconomicTerms, "structuredProduct", "factorDate");

        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return new SecurityMasterOpenLotReadModelDto(
                QuantityModel: lotModel.QuantityModel,
                LotSize: lotModel.LotSize,
                ContractMultiplier: lotModel.ContractMultiplier,
                UsesFaceValue: lotModel.UsesFaceValue,
                SupportsFactorAdjustedExposure: lotModel.SupportsFactorAdjustedExposure,
                RequiresResolvedSecurityId: lotModel.RequiresResolvedSecurityId,
                CurrentFactor: currentFactor,
                CurrentFactorDate: currentFactorDate,
                AsOfUtc: asOfUtc,
                Summary: "Open-lot read model is unscoped. Select a fund profile to materialize account and portfolio lots.",
                Lots: [],
                ProvenanceHistory: []);
        }

        var scopedFundProfileId = fundProfileId.Trim();
        // Tenant isolation (SEC-005): withhold a foreign fund's lots — treat as no scoped runs so the
        // unscoped/empty read model is returned and no cross-tenant lots are materialized.
        IReadOnlyList<StrategyRunEntry> relatedRuns =
            await IsFundAccessibleToCurrentTenantAsync(scopedFundProfileId, ct).ConfigureAwait(false)
                ? await LoadFundRunsAsync(scopedFundProfileId, ct).ConfigureAwait(false)
                : [];
        if (relatedRuns.Count == 0)
        {
            return new SecurityMasterOpenLotReadModelDto(
                QuantityModel: lotModel.QuantityModel,
                LotSize: lotModel.LotSize,
                ContractMultiplier: lotModel.ContractMultiplier,
                UsesFaceValue: lotModel.UsesFaceValue,
                SupportsFactorAdjustedExposure: lotModel.SupportsFactorAdjustedExposure,
                RequiresResolvedSecurityId: lotModel.RequiresResolvedSecurityId,
                CurrentFactor: currentFactor,
                CurrentFactorDate: currentFactorDate,
                AsOfUtc: asOfUtc,
                Summary: $"Fund profile {scopedFundProfileId} has no recorded runs with open-lot context.",
                Lots: [],
                ProvenanceHistory: []);
        }

        var normalizedIdentifiers = BuildNormalizedIdentifierSet(detail);
        var settlementCycleDays = TryGetJsonInt(detail.CommonTerms, "settlementCycleDays") ?? 0;
        var lots = new List<SecurityMasterOpenLotDto>();
        var provenance = new List<SecurityMasterOpenLotProvenanceDto>();

        foreach (var run in relatedRuns)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = run.Metrics?.Snapshots.LastOrDefault();
            if (latestSnapshot is null)
            {
                continue;
            }

            var portfolio = await _portfolioReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
            var matchingPositions = portfolio?.Positions
                .Where(position => MatchesSecurity(position.Symbol, position.Security?.SecurityId, detail, normalizedIdentifiers))
                .ToArray()
                ?? [];

            var snapshotLots = ExtractSnapshotOpenLots(latestSnapshot);
            var matchingLots = snapshotLots
                .Where(item => MatchesSecurity(item.Lot.Symbol, securityId: null, detail, normalizedIdentifiers))
                .ToArray();

            if (matchingLots.Length == 0)
            {
                continue;
            }

            var portfolioId = portfolio?.PortfolioId ?? run.PortfolioId ?? run.RunId;
            provenance.Add(new SecurityMasterOpenLotProvenanceDto(
                ProvenanceId: $"run-{run.RunId}",
                RunId: run.RunId,
                PortfolioId: portfolioId,
                AccountScopeId: matchingPositions.FirstOrDefault()?.AccountScopeId,
                AccountScopeDisplayName: matchingPositions.FirstOrDefault()?.AccountScopeDisplayName,
                SourceSystem: "strategy-run-snapshot",
                SourceRecordId: $"{run.RunId}:{latestSnapshot.Timestamp:O}",
                AsOfUtc: latestSnapshot.Timestamp,
                Summary: $"{matchingLots.Length} open lot(s) matched from run {run.RunId} portfolio snapshot."));

            foreach (var snapshotLot in matchingLots)
            {
                var quantity = snapshotLot.Lot.Quantity;
                var price = snapshotLot.Lot.EntryPrice;
                var position = latestSnapshot.Positions.TryGetValue(snapshotLot.Lot.Symbol, out var matchedPosition)
                    ? matchedPosition
                    : null;
                var impliedMarketPrice = TryResolveImpliedMarketPrice(position);
                var costBasis = quantity * price;
                var accountScopeId = matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.AccountScopeId))?.AccountScopeId
                    ?? snapshotLot.AccountId
                    ?? snapshotLot.Lot.AccountId;
                var accountScopeDisplayName = matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.AccountScopeDisplayName))?.AccountScopeDisplayName
                    ?? snapshotLot.AccountDisplayName;
                var vehicleScopeId = matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.VehicleScopeId))?.VehicleScopeId;
                var vehicleScopeDisplayName = matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.VehicleScopeDisplayName))?.VehicleScopeDisplayName;
                var (originalFace, currentFace, factorAdjustedQuantity, factorAdjustedFace) = ProjectLotFaces(quantity, lotModel, currentFactor);

                lots.Add(new SecurityMasterOpenLotDto(
                    SecurityId: detail.SecurityId,
                    PortfolioId: portfolioId,
                    RunId: run.RunId,
                    AccountScopeId: accountScopeId,
                    AccountScopeDisplayName: accountScopeDisplayName,
                    VehicleScopeId: vehicleScopeId,
                    VehicleScopeDisplayName: vehicleScopeDisplayName,
                    LotId: snapshotLot.Lot.LotId.ToString("N"),
                    Symbol: snapshotLot.Lot.Symbol,
                    TradeDate: snapshotLot.Lot.OpenedAt,
                    SettleDate: settlementCycleDays > 0 ? snapshotLot.Lot.OpenedAt.AddDays(settlementCycleDays) : null,
                    OriginalQuantity: quantity,
                    CurrentQuantity: quantity,
                    OriginalFace: originalFace,
                    CurrentFace: currentFace,
                    FactorAdjustedQuantity: factorAdjustedQuantity,
                    FactorAdjustedFace: factorAdjustedFace,
                    CostBasis: costBasis,
                    EntryPrice: price,
                    UnrealizedPnl: impliedMarketPrice.HasValue ? snapshotLot.Lot.UnrealizedPnl(impliedMarketPrice.Value) : null,
                    Currency: detail.Currency,
                    LotStatus: "Open",
                    SourceSystem: "strategy-run-snapshot",
                    SourceRecordId: $"{run.RunId}:{portfolioId}:{snapshotLot.Lot.LotId:N}",
                    AsOfUtc: latestSnapshot.Timestamp,
                    SourceUpdatedBy: null,
                    SourceReason: "Latest scoped run snapshot",
                    IsLongTerm: snapshotLot.Lot.IsLongTerm(latestSnapshot.Timestamp),
                    Notes: snapshotLot.Lot.Notes));
            }
        }

        var orderedLots = lots
            .OrderBy(static lot => lot.TradeDate)
            .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = orderedLots.Length == 0
            ? $"No scoped open lots currently reference security {detail.SecurityId} in fund profile {scopedFundProfileId}."
            : lotModel.SupportsFactorAdjustedExposure && currentFactor.HasValue
                ? $"{orderedLots.Length} scoped open lot(s) reconcile with factor-adjusted face support at factor {currentFactor.Value:0.########}."
                : $"{orderedLots.Length} scoped open lot(s) matched across {orderedLots.Select(static lot => lot.PortfolioId).Distinct(StringComparer.OrdinalIgnoreCase).Count()} portfolio(s).";

        return new SecurityMasterOpenLotReadModelDto(
            QuantityModel: lotModel.QuantityModel,
            LotSize: lotModel.LotSize,
            ContractMultiplier: lotModel.ContractMultiplier,
            UsesFaceValue: lotModel.UsesFaceValue,
            SupportsFactorAdjustedExposure: lotModel.SupportsFactorAdjustedExposure,
            RequiresResolvedSecurityId: lotModel.RequiresResolvedSecurityId,
            CurrentFactor: currentFactor,
            CurrentFactorDate: currentFactorDate,
            AsOfUtc: orderedLots.LastOrDefault()?.AsOfUtc ?? asOfUtc,
            Summary: summary,
            Lots: orderedLots,
            ProvenanceHistory: provenance
                .OrderByDescending(static item => item.AsOfUtc)
                .ThenBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static int GetImpactLinkOrder(SecurityMasterImpactLinkDto link)
        => link.Target switch
        {
            "reconciliation" => 0,
            "ledger" => 1,
            "reportPack" => 2,
            "portfolio" => 3,
            _ => 4
        };

    private static SecurityMasterImpactSeverity DetermineImpactSeverity(
        int portfolioExposureCount,
        int ledgerExposureCount,
        int reconciliationExposureCount,
        int reportPackExposureCount,
        int reconciliationUnavailableRunCount)
    {
        if (reconciliationExposureCount > 0 || reportPackExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (ledgerExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Medium;
        }

        if (portfolioExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Low;
        }

        if (reconciliationUnavailableRunCount > 0)
        {
            return SecurityMasterImpactSeverity.Unknown;
        }

        return SecurityMasterImpactSeverity.None;
    }

    private static SecurityMasterImpactSeverity DetermineConflictImpactSeverity(
        SecurityMasterConflict conflict,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (!downstreamImpact.IsScoped)
        {
            return SecurityMasterImpactSeverity.Unknown;
        }

        var normalizedFieldPath = conflict.FieldPath.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalizedFieldPath.Contains("CorporateAction", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.ReportPackExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (normalizedFieldPath.Contains("Identifier", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.ReconciliationExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (normalizedFieldPath.Contains("TradingParameters", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.LedgerExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Medium;
        }

        return downstreamImpact.Severity;
    }

    private static string BuildImpactSummary(
        int matchedRunCount,
        int portfolioExposureCount,
        int ledgerExposureCount,
        int reconciliationExposureCount,
        int reportPackExposureCount,
        int reconciliationUnavailableRunCount)
    {
        if (portfolioExposureCount == 0 &&
            ledgerExposureCount == 0 &&
            reconciliationExposureCount == 0 &&
            reportPackExposureCount == 0)
        {
            if (reconciliationUnavailableRunCount > 0)
            {
                return $"Reconciliation impact has not been materialized for {reconciliationUnavailableRunCount} of {matchedRunCount} scoped run(s).";
            }

            return $"No downstream exposure detected across {matchedRunCount} scoped run(s).";
        }

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{matchedRunCount} scoped run(s) checked • " +
            $"portfolio {portfolioExposureCount}, ledger {ledgerExposureCount}, reconciliation {reconciliationExposureCount}, report pack {reportPackExposureCount}.");

        if (reconciliationUnavailableRunCount > 0)
        {
            summary += string.Create(
                CultureInfo.InvariantCulture,
                $" Reconciliation impact still needs refresh for {reconciliationUnavailableRunCount} scoped run(s).");
        }

        return summary;
    }

    private static string BuildConflictImpactSummary(
        string fieldPath,
        SecurityMasterImpactSeverity severity,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (severity == SecurityMasterImpactSeverity.Unknown)
        {
            return "Downstream impact is not scoped.";
        }

        return severity switch
        {
            SecurityMasterImpactSeverity.High =>
                $"{FormatFieldLabel(fieldPath)} is high impact because downstream Accounting and Reporting workflows already reference this security.",
            SecurityMasterImpactSeverity.Medium =>
                $"{FormatFieldLabel(fieldPath)} already feeds ledger-facing workflows.",
            SecurityMasterImpactSeverity.Low =>
                $"{FormatFieldLabel(fieldPath)} only affects low-risk scoped portfolio exposure today.",
            _ =>
                $"{FormatFieldLabel(fieldPath)} has no detected scoped downstream exposure."
        };
    }

    private static string BuildConflictImpactDetail(
        string fieldPath,
        SecurityMasterImpactSeverity severity,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (severity == SecurityMasterImpactSeverity.Unknown)
        {
            return $"{FormatFieldLabel(fieldPath)} cannot be bulk-resolved because no fund scope is active.";
        }

        return severity switch
        {
            SecurityMasterImpactSeverity.High =>
                $"{FormatFieldLabel(fieldPath)} should be reviewed manually before reconciliation or report-pack consumers ingest the change.",
            SecurityMasterImpactSeverity.Medium =>
                $"{FormatFieldLabel(fieldPath)} reaches ledger-facing workflows. Keep the resolution explicit and operator-reviewed.",
            SecurityMasterImpactSeverity.Low =>
                $"{FormatFieldLabel(fieldPath)} is limited to scoped portfolio posture and can participate in low-risk bulk assist when the recommendation is deterministic.",
            _ =>
                $"{FormatFieldLabel(fieldPath)} has no detected scoped downstream exposure."
        };
    }

    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic)
    {
        var primaryIdentifier = detail.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? detail.Identifiers.FirstOrDefault();

        var riskCountry = economic?.RiskCountry
            ?? TryGetJsonString(detail.CommonTerms, "countryOfRisk");

        return new SecurityMasterWorkstationDto(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            Status: detail.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: detail.AssetClass,
                SubType: economic?.SubType,
                PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
                PrimaryIdentifierValue: primaryIdentifier?.Value,
                RiskCountry: riskCountry,
                IssuerType: economic?.IssuerType,
                TypeName: economic?.TypeName),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: detail.Currency,
                Version: detail.Version,
                EffectiveFrom: detail.EffectiveFrom,
                EffectiveTo: detail.EffectiveTo,
                SubType: economic?.SubType,
                AssetFamily: economic?.AssetFamily,
                IssuerType: economic?.IssuerType,
                RiskCountry: riskCountry,
                TypeName: economic?.TypeName));
    }

    private static SecurityMasterEconomicDefinitionDrillInDto MapToEconomicDefinition(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        WinningSourceInfo? winningSource)
    {
        return new SecurityMasterEconomicDefinitionDrillInDto(
            SecurityId: detail.SecurityId,
            AssetClass: detail.AssetClass,
            Currency: detail.Currency,
            Version: detail.Version,
            EffectiveFrom: detail.EffectiveFrom,
            EffectiveTo: detail.EffectiveTo,
            AssetFamily: economic?.AssetFamily,
            SubType: economic?.SubType,
            IssuerType: economic?.IssuerType,
            RiskCountry: economic?.RiskCountry,
            WinningSourceSystem: winningSource?.SourceSystem,
            WinningSourceRecordId: winningSource?.SourceRecordId,
            WinningSourceAsOf: winningSource?.AsOf,
            WinningSourceUpdatedBy: winningSource?.UpdatedBy,
            WinningSourceReason: winningSource?.Reason);
    }

    private static WinningSourceInfo? ParseWinningSource(JsonElement? provenance)
    {
        if (!provenance.HasValue || provenance.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var sourceSystem = TryGetJsonString(provenance.Value, "sourceSystem");
        if (string.IsNullOrWhiteSpace(sourceSystem))
        {
            return null;
        }

        return new WinningSourceInfo(
            SourceSystem: sourceSystem,
            SourceRecordId: TryGetJsonString(provenance.Value, "sourceRecordId"),
            AsOf: TryGetJsonDateTimeOffset(provenance.Value, "asOf"),
            UpdatedBy: TryGetJsonString(provenance.Value, "updatedBy"),
            Reason: TryGetJsonString(provenance.Value, "reason"));
    }

    private static string? ExtractCurrentFieldValue(
        string fieldPath,
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return null;
        }

        var segments = fieldPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments[0].Equals("Identifiers", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 1 || segments[1].Equals("Primary", StringComparison.OrdinalIgnoreCase))
            {
                return detail.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)?.Value
                    ?? detail.Identifiers.FirstOrDefault()?.Value;
            }

            var kindSegment = segments[1];
            var identifier = detail.Identifiers.FirstOrDefault(identifier =>
                identifier.Kind.ToString().Equals(kindSegment, StringComparison.OrdinalIgnoreCase));
            if (identifier is not null)
            {
                return identifier.Value;
            }

            var alias = detail.Aliases.FirstOrDefault(alias =>
                alias.AliasKind.Equals(kindSegment, StringComparison.OrdinalIgnoreCase));
            return alias?.AliasValue;
        }

        if (segments[0].Equals("TradingParameters", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Last().ToLowerInvariant() switch
            {
                "lotsize" => FormatNullableDecimal(trading?.LotSize),
                "ticksize" => FormatNullableDecimal(trading?.TickSize),
                "contractmultiplier" => FormatNullableDecimal(trading?.ContractMultiplier),
                "marginrequirementpct" => FormatNullableDecimal(trading?.MarginRequirementPct),
                "tradinghoursutc" => trading?.TradingHoursUtc,
                "circuitbreakerthresholdpct" => FormatNullableDecimal(trading?.CircuitBreakerThresholdPct),
                _ => null
            };
        }

        if (segments[0].Equals("EconomicDefinition", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Last().ToLowerInvariant() switch
            {
                "displayname" => detail.DisplayName,
                "currency" => detail.Currency,
                "assetclass" => detail.AssetClass,
                "assetfamily" => economic?.AssetFamily,
                "subtype" => economic?.SubType,
                "issuertype" => economic?.IssuerType,
                _ => null
            };
        }

        var roots = new List<JsonElement>(4);
        if (economic is not null)
        {
            roots.Add(economic.Classification);
            roots.Add(economic.CommonTerms);
            roots.Add(economic.EconomicTerms);
        }

        roots.Add(detail.CommonTerms);
        roots.Add(detail.AssetSpecificTerms);

        foreach (var root in roots)
        {
            if (TryReadJsonPath(root, segments, out var value) ||
                TryReadJsonPath(root, segments.Skip(1), out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadJsonPath(
        JsonElement root,
        IEnumerable<string> segments,
        out string? value)
    {
        value = null;
        var current = root;
        var hasAny = false;

        foreach (var segment in segments)
        {
            hasAny = true;
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetPropertyCaseInsensitive(current, segment, out current))
            {
                return false;
            }
        }

        if (!hasAny)
        {
            return false;
        }

        value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => current.GetRawText()
        };

        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeComparableString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is ' ' or '-' or '/' or '.' or '_')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private static bool AreEquivalent(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(
               NormalizeComparableString(left),
               NormalizeComparableString(right),
               StringComparison.Ordinal);

    private static HashSet<string> BuildNormalizedIdentifierSet(SecurityDetailDto detail)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in detail.Identifiers)
        {
            identifiers.Add(NormalizeComparableString(SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier)));
        }

        foreach (var alias in detail.Aliases.Where(static alias => alias.IsEnabled))
        {
            identifiers.Add(NormalizeComparableString(SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue)));
        }

        identifiers.Add(NormalizeComparableString(detail.DisplayName));
        return identifiers;
    }

    private static bool MatchesSecurity(
        string? symbol,
        Guid? securityId,
        SecurityDetailDto detail,
        ISet<string> normalizedIdentifiers)
    {
        if (securityId.HasValue && securityId.Value == detail.SecurityId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        return normalizedIdentifiers.Contains(NormalizeComparableString(symbol));
    }

    private static IReadOnlyList<string> GetMissingTradingParameterFields(string? assetClass, TradingParametersDto? trading)
    {
        var missingFields = new List<string>(4);

        if (trading?.LotSize is null or <= 0)
        {
            missingFields.Add("lot size");
        }

        if (trading?.TickSize is null or <= 0)
        {
            missingFields.Add("tick size");
        }

        if (string.IsNullOrWhiteSpace(trading?.TradingHoursUtc))
        {
            missingFields.Add("trading hours");
        }

        if (RequiresContractMultiplier(assetClass) && trading?.ContractMultiplier is null or <= 0)
        {
            missingFields.Add("contract multiplier");
        }

        return missingFields;
    }

    private static bool RequiresContractMultiplier(string? assetClass)
        => assetClass is not null &&
           (assetClass.Equals("Option", StringComparison.OrdinalIgnoreCase)
            || assetClass.Equals("Future", StringComparison.OrdinalIgnoreCase)
            || assetClass.Equals("Swap", StringComparison.OrdinalIgnoreCase)
            || assetClass.Equals("Warrant", StringComparison.OrdinalIgnoreCase));

    private static string FormatFieldLabel(string? fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "Field";
        }

        var raw = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
        var builder = new StringBuilder(raw.Length + 8);
        for (var index = 0; index < raw.Length; index++)
        {
            var character = raw[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(raw[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(builder.ToString().Replace('_', ' ').ToLowerInvariant());
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
        => TryGetPropertyCaseInsensitive(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? TryGetJsonDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static int? TryGetJsonInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        // CommonTerms may encode numeric fields as strings in some older records.
        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryGetJsonDecimal(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryGetNestedJsonDecimal(JsonElement? element, string objectName, string propertyName)
    {
        if (!element.HasValue
            || !TryGetPropertyCaseInsensitive(element.Value, objectName, out var nested)
            || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetJsonDecimal(nested, propertyName);
    }

    private static DateOnly? TryGetNestedJsonDateOnly(JsonElement? element, string objectName, string propertyName)
    {
        if (!element.HasValue
            || !TryGetPropertyCaseInsensitive(element.Value, objectName, out var nested)
            || nested.ValueKind != JsonValueKind.Object
            || !TryGetPropertyCaseInsensitive(nested, propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateOnly.TryParse(property.GetString(), out var parsed) ? parsed : null;
    }

    private static JsonElement? TryGetNestedJsonElement(JsonElement? element, string objectName, string propertyName)
    {
        if (!element.HasValue
            || !TryGetPropertyCaseInsensitive(element.Value, objectName, out var nested)
            || nested.ValueKind != JsonValueKind.Object
            || !TryGetPropertyCaseInsensitive(nested, propertyName, out var property))
        {
            return null;
        }

        return property;
    }

    private static DateOnly? TryGetJsonDateOnly(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateOnly.TryParse(property.GetString(), out var parsed) ? parsed : null;
    }

    private static string? FormatNullableDecimal(decimal? value)
        => value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : null;

    private static string FormatLotSizeSuffix(decimal? lotSize)
        => lotSize is > 0m
            ? $" (lot size {lotSize:0.########})"
            : string.Empty;

    private static SecurityAssetClassDescriptor GetAssetCapability(string assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass);

    private static bool IsIdentifierActive(DateTimeOffset validFrom, DateTimeOffset? validTo, DateTimeOffset asOf)
        => validFrom <= asOf && (!validTo.HasValue || validTo.Value > asOf);

    private static string NormalizeAliasValue(SecurityAliasDto alias)
        => SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue);

    private static int? TryGetSchemaVersion(JsonElement element)
    {
        if (!TryGetPropertyCaseInsensitive(element, "schemaVersion", out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericVersion))
        {
            return numericVersion;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsedVersion)
            ? parsedVersion
            : null;
    }

    private static bool HasJsonContent(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            _ => true
        };

    private static SecurityEconomicDefinitionRecord? TryParseHistoryRecord(JsonElement payload)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return SecurityMasterMapping.FromEconomicPayload(payload);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> BuildChangedFields(
        SecurityEconomicDefinitionRecord? previousRecord,
        SecurityEconomicDefinitionRecord? currentRecord,
        string eventType)
    {
        if (currentRecord is null)
        {
            return [];
        }

        if (previousRecord is null)
        {
            return ["Identity", "Common terms", "Identifiers"];
        }

        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(previousRecord.DisplayName, currentRecord.DisplayName, StringComparison.Ordinal))
        {
            fields.Add("Display name");
        }

        if (!string.Equals(previousRecord.Currency, currentRecord.Currency, StringComparison.OrdinalIgnoreCase))
        {
            fields.Add("Currency");
        }

        if (previousRecord.Status != currentRecord.Status)
        {
            fields.Add("Status");
        }

        if (previousRecord.EffectiveFrom != currentRecord.EffectiveFrom
            || previousRecord.EffectiveTo != currentRecord.EffectiveTo)
        {
            fields.Add("Effective window");
        }

        if (!string.Equals(previousRecord.AssetClass, currentRecord.AssetClass, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousRecord.AssetFamily, currentRecord.AssetFamily, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousRecord.SubType, currentRecord.SubType, StringComparison.OrdinalIgnoreCase))
        {
            fields.Add("Classification");
        }

        if (!JsonElementsEquivalent(previousRecord.CommonTerms, currentRecord.CommonTerms))
        {
            fields.Add("Common terms");
        }

        if (!JsonElementsEquivalent(previousRecord.EconomicTerms, currentRecord.EconomicTerms))
        {
            fields.Add("Economic terms");
        }

        if (!IdentifiersEquivalent(previousRecord.Identifiers, currentRecord.Identifiers))
        {
            fields.Add("Identifiers");
        }

        if (eventType.Contains("Deactivate", StringComparison.OrdinalIgnoreCase))
        {
            fields.Add("Lifecycle status");
        }

        return fields
            .OrderBy(static field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractChangedFields(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var fields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in payload.EnumerateObject())
        {
            var field = property.Name;
            if (field.EndsWith("Terms", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Economic terms");
            }
            else if (field.Contains("identifier", StringComparison.OrdinalIgnoreCase)
                || field.Contains("alias", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Identifiers");
            }
            else if (field.Contains("status", StringComparison.OrdinalIgnoreCase)
                || field.Contains("deactivat", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Lifecycle status");
            }
            else if (field.Contains("effective", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Effective window");
            }
            else if (field.Contains("classification", StringComparison.OrdinalIgnoreCase)
                || field.Contains("assetClass", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Classification");
            }
            else
            {
                fields.Add(HumanizeEventType(field));
            }
        }

        return fields.ToArray();
    }

    private static string BuildChangeSummary(
        SecurityMasterEventEnvelope envelope,
        SecurityEconomicDefinitionRecord? currentRecord,
        IReadOnlyList<string> changedFields,
        string sourceSystem)
    {
        if (currentRecord is null)
        {
            return $"{HumanizeEventType(envelope.EventType)} recorded from {sourceSystem}.";
        }

        var primaryIdentifier = currentRecord.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary);
        if (string.Equals(envelope.EventType, "SecurityCreated", StringComparison.OrdinalIgnoreCase))
        {
            var identifierText = primaryIdentifier is null
                ? "without a primary identifier"
                : $"with primary {primaryIdentifier.Kind}:{primaryIdentifier.Value}";
            return $"Created {currentRecord.AssetClass} '{currentRecord.DisplayName}' {identifierText}.";
        }

        if (envelope.EventType.Contains("Deactivate", StringComparison.OrdinalIgnoreCase))
        {
            var effectiveText = currentRecord.EffectiveTo?.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'")
                ?? envelope.EventTimestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'");
            return $"Deactivated {currentRecord.AssetClass} '{currentRecord.DisplayName}' effective {effectiveText}.";
        }

        if (changedFields.Count == 0)
        {
            return $"{HumanizeEventType(envelope.EventType)} recorded for '{currentRecord.DisplayName}'.";
        }

        return $"{HumanizeEventType(envelope.EventType)} updated {SummarizeChangedFields(changedFields)} for '{currentRecord.DisplayName}'.";
    }

    private static string SummarizeChangedFields(IReadOnlyList<string> changedFields)
    {
        if (changedFields.Count == 0)
        {
            return "the selected security";
        }

        if (changedFields.Count <= 3)
        {
            return string.Join(", ", changedFields).ToLowerInvariant();
        }

        var leading = string.Join(", ", changedFields.Take(3)).ToLowerInvariant();
        return $"{leading}, and {changedFields.Count - 3} more area(s)";
    }

    private static string HumanizeEventType(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "Security change";
        }

        var builder = new System.Text.StringBuilder(eventType.Length + 8);
        for (var index = 0; index < eventType.Length; index++)
        {
            var character = eventType[index];
            if (index > 0
                && char.IsUpper(character)
                && !char.IsUpper(eventType[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string InferChangeOrigin(string sourceSystem, string actor)
    {
        if (ContainsAny(sourceSystem, actor, "wpf-ui", "manual", "override", "desktop-user", "operator", "user"))
        {
            return "User";
        }

        if (ContainsAny(sourceSystem, actor, "polygon", "edgar", "bloomberg", "refinitiv", "trustee", "custodian", "vendor", "golden-edm"))
        {
            return "Vendor";
        }

        if (ContainsAny(sourceSystem, actor, "workflow", "system", "bot", "snapshot"))
        {
            return "System";
        }

        return "Unknown";
    }

    private static bool ContainsAny(string sourceSystem, string actor, params string[] tokens)
        => tokens.Any(token =>
            sourceSystem.Contains(token, StringComparison.OrdinalIgnoreCase)
            || actor.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string? value, string? token)
        => !string.IsNullOrWhiteSpace(value) &&
           !string.IsNullOrWhiteSpace(token) &&
           value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool JsonElementsEquivalent(JsonElement left, JsonElement right)
        => left.ValueKind == right.ValueKind
            && string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);

    private static bool IdentifiersEquivalent(
        IReadOnlyList<SecurityIdentifierDto> left,
        IReadOnlyList<SecurityIdentifierDto> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        static string Key(SecurityIdentifierDto identifier) =>
            $"{identifier.Kind}|{identifier.Value}|{identifier.Provider}|{identifier.ValidFrom:O}|{identifier.ValidTo:O}|{identifier.IsPrimary}";

        var leftKeys = left.Select(Key).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        var rightKeys = right.Select(Key).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
    }

    private static string? CoalesceNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static void AppendScheduleEventsFromArray(
        ICollection<SecurityMasterScheduleEventDto> destination,
        JsonElement? economicTerms,
        string propertyName,
        string defaultCurrency,
        WinningSourceInfo? winningSource)
    {
        if (!economicTerms.HasValue
            || !TryGetPropertyCaseInsensitive(economicTerms.Value, propertyName, out var scheduleArray)
            || scheduleArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var entry in scheduleArray.EnumerateArray())
        {
            index++;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var effectiveDate =
                TryGetJsonDateOnly(entry, "effectiveDate")
                ?? TryGetJsonDateOnly(entry, "paymentDate")
                ?? TryGetJsonDateOnly(entry, "payDate")
                ?? TryGetJsonDateOnly(entry, "date");

            if (!effectiveDate.HasValue)
            {
                continue;
            }

            var expectedAmount = TryGetJsonDecimal(entry, "expectedAmount")
                ?? TryGetJsonDecimal(entry, "amount")
                ?? TryGetJsonDecimal(entry, "principalAmount")
                ?? TryGetJsonDecimal(entry, "interestAmount");
            var actualAmount = TryGetJsonDecimal(entry, "actualAmount");
            decimal? varianceAmount = expectedAmount.HasValue && actualAmount.HasValue
                ? actualAmount.Value - expectedAmount.Value
                : (decimal?)null;

            destination.Add(new SecurityMasterScheduleEventDto(
                EventId: TryGetJsonString(entry, "eventId") ?? $"{propertyName}-{effectiveDate.Value:yyyyMMdd}-{index}",
                EventType: TryGetJsonString(entry, "eventType") ?? InferScheduleEventType(propertyName),
                EffectiveDate: effectiveDate.Value,
                PayDate: TryGetJsonDateOnly(entry, "paymentDate") ?? TryGetJsonDateOnly(entry, "payDate"),
                AccrualStartDate: TryGetJsonDateOnly(entry, "accrualStartDate"),
                AccrualEndDate: TryGetJsonDateOnly(entry, "accrualEndDate"),
                ExpectedAmount: expectedAmount,
                ActualAmount: actualAmount,
                VarianceAmount: varianceAmount,
                FactorStart: TryGetJsonDecimal(entry, "factorStart"),
                FactorEnd: TryGetJsonDecimal(entry, "factorEnd") ?? TryGetJsonDecimal(entry, "factor"),
                Currency: TryGetJsonString(entry, "currency") ?? defaultCurrency,
                PostingStatus: TryGetJsonString(entry, "postingStatus") ?? "Projected",
                SourceSystem: TryGetJsonString(entry, "sourceSystem") ?? winningSource?.SourceSystem ?? "economic-terms",
                SourceRecordId: TryGetJsonString(entry, "sourceRecordId") ?? winningSource?.SourceRecordId,
                SourceAsOfUtc: TryGetJsonDateTimeOffset(entry, "asOf") ?? winningSource?.AsOf,
                SourceUpdatedBy: TryGetJsonString(entry, "updatedBy") ?? winningSource?.UpdatedBy,
                SourceReason: TryGetJsonString(entry, "reason") ?? winningSource?.Reason,
                IsDerivedFromEconomicTerms: true,
                IsCurrentProjection: true));
        }
    }

    private static IReadOnlyList<SecurityMasterFactorPointDto> BuildFactorHistory(
        JsonElement? economicTerms,
        WinningSourceInfo? winningSource,
        decimal? currentFactor,
        DateOnly? currentFactorDate)
    {
        var points = new List<SecurityMasterFactorPointDto>();
        var factorHistory = TryGetNestedJsonElement(economicTerms, "structuredProduct", "factorHistory");

        if (factorHistory.HasValue && factorHistory.Value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var entry in factorHistory.Value.EnumerateArray())
            {
                index++;
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var effectiveDate =
                    TryGetJsonDateOnly(entry, "effectiveDate")
                    ?? TryGetJsonDateOnly(entry, "factorDate")
                    ?? TryGetJsonDateOnly(entry, "date");
                var factor = TryGetJsonDecimal(entry, "factor");
                if (!effectiveDate.HasValue || !factor.HasValue)
                {
                    continue;
                }

                points.Add(new SecurityMasterFactorPointDto(
                    PointId: TryGetJsonString(entry, "pointId") ?? $"factor-{effectiveDate.Value:yyyyMMdd}-{index}",
                    EffectiveDate: effectiveDate.Value,
                    Factor: factor.Value,
                    PreviousFactor: TryGetJsonDecimal(entry, "previousFactor"),
                    SourceSystem: TryGetJsonString(entry, "sourceSystem") ?? winningSource?.SourceSystem ?? "economic-terms",
                    SourceRecordId: TryGetJsonString(entry, "sourceRecordId") ?? winningSource?.SourceRecordId,
                    SourceAsOfUtc: TryGetJsonDateTimeOffset(entry, "asOf") ?? winningSource?.AsOf,
                    SourceUpdatedBy: TryGetJsonString(entry, "updatedBy") ?? winningSource?.UpdatedBy,
                    SourceReason: TryGetJsonString(entry, "reason") ?? winningSource?.Reason,
                    IsCurrentFactor: false));
            }
        }

        if (points.Count == 0 && currentFactor.HasValue && currentFactorDate.HasValue)
        {
            points.Add(new SecurityMasterFactorPointDto(
                PointId: $"factor-{currentFactorDate.Value:yyyyMMdd}",
                EffectiveDate: currentFactorDate.Value,
                Factor: currentFactor.Value,
                PreviousFactor: null,
                SourceSystem: winningSource?.SourceSystem ?? "economic-terms",
                SourceRecordId: winningSource?.SourceRecordId,
                SourceAsOfUtc: winningSource?.AsOf,
                SourceUpdatedBy: winningSource?.UpdatedBy,
                SourceReason: winningSource?.Reason,
                IsCurrentFactor: true));
        }

        var ordered = points
            .OrderBy(static point => point.EffectiveDate)
            .ThenBy(static point => point.PointId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var previousFactor = ordered[index].PreviousFactor ?? (index > 0 ? ordered[index - 1].Factor : null);
            var isCurrentFactor = currentFactorDate.HasValue
                ? ordered[index].EffectiveDate == currentFactorDate.Value
                : index == ordered.Length - 1;

            ordered[index] = ordered[index] with
            {
                PreviousFactor = previousFactor,
                IsCurrentFactor = isCurrentFactor
            };
        }

        return ordered;
    }

    private static IReadOnlyList<SecurityMasterScheduleProvenanceDto> BuildScheduleProvenanceHistory(
        IReadOnlyList<SecurityMasterScheduleEventDto> events,
        IReadOnlyList<SecurityMasterFactorPointDto> factorHistory,
        IReadOnlyList<SecurityMasterEventEnvelope> history,
        WinningSourceInfo? winningSource)
    {
        var provenance = new List<SecurityMasterScheduleProvenanceDto>();

        if (winningSource is not null)
        {
            provenance.Add(new SecurityMasterScheduleProvenanceDto(
                ProvenanceId: "winning-source",
                Category: "WinningSource",
                Summary: $"Golden-copy schedule source {winningSource.SourceSystem}.",
                EffectiveDate: null,
                SourceSystem: winningSource.SourceSystem,
                SourceRecordId: winningSource.SourceRecordId,
                SourceAsOfUtc: winningSource.AsOf,
                SourceUpdatedBy: winningSource.UpdatedBy,
                SourceReason: winningSource.Reason,
                StreamVersion: null,
                EventType: null));
        }

        provenance.AddRange(events.Select(eventItem => new SecurityMasterScheduleProvenanceDto(
            ProvenanceId: $"event-{eventItem.EventId}",
            Category: "ScheduleEvent",
            Summary: $"{eventItem.EventType} effective {eventItem.EffectiveDate:yyyy-MM-dd}.",
            EffectiveDate: eventItem.EffectiveDate,
            SourceSystem: eventItem.SourceSystem,
            SourceRecordId: eventItem.SourceRecordId,
            SourceAsOfUtc: eventItem.SourceAsOfUtc,
            SourceUpdatedBy: eventItem.SourceUpdatedBy,
            SourceReason: eventItem.SourceReason,
            StreamVersion: null,
            EventType: eventItem.EventType)));

        provenance.AddRange(factorHistory.Select(point => new SecurityMasterScheduleProvenanceDto(
            ProvenanceId: $"factor-{point.PointId}",
            Category: "FactorHistory",
            Summary: $"Factor {point.Factor:0.########} effective {point.EffectiveDate:yyyy-MM-dd}.",
            EffectiveDate: point.EffectiveDate,
            SourceSystem: point.SourceSystem,
            SourceRecordId: point.SourceRecordId,
            SourceAsOfUtc: point.SourceAsOfUtc,
            SourceUpdatedBy: point.SourceUpdatedBy,
            SourceReason: point.SourceReason,
            StreamVersion: null,
            EventType: "FactorUpdate")));

        foreach (var envelope in history.Where(IsScheduleHistoryEvent))
        {
            provenance.Add(new SecurityMasterScheduleProvenanceDto(
                ProvenanceId: $"history-{envelope.StreamVersion}",
                Category: "History",
                Summary: $"{envelope.EventType} recorded at {envelope.EventTimestamp:yyyy-MM-dd HH:mm} UTC.",
                EffectiveDate: null,
                SourceSystem: TryGetJsonString(envelope.Metadata, "sourceSystem")
                    ?? TryGetJsonString(envelope.Metadata, "source")
                    ?? winningSource?.SourceSystem
                    ?? "security-history",
                SourceRecordId: TryGetJsonString(envelope.Metadata, "sourceRecordId"),
                SourceAsOfUtc: envelope.EventTimestamp,
                SourceUpdatedBy: envelope.Actor,
                SourceReason: TryGetJsonString(envelope.Metadata, "reason"),
                StreamVersion: envelope.StreamVersion,
                EventType: envelope.EventType));
        }

        return provenance
            .OrderByDescending(static item => item.SourceAsOfUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(static item => item.EffectiveDate ?? DateOnly.MinValue)
            .ThenBy(static item => item.ProvenanceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AppendLifecycleEvent(
        ICollection<SecurityMasterScheduleEventDto> destination,
        string eventType,
        DateOnly? effectiveDate,
        string currency,
        WinningSourceInfo? winningSource)
    {
        if (!effectiveDate.HasValue
            || destination.Any(existing => string.Equals(existing.EventType, eventType, StringComparison.OrdinalIgnoreCase)
                && existing.EffectiveDate == effectiveDate.Value))
        {
            return;
        }

        destination.Add(new SecurityMasterScheduleEventDto(
            EventId: $"{eventType.ToLowerInvariant()}-{effectiveDate.Value:yyyyMMdd}",
            EventType: eventType,
            EffectiveDate: effectiveDate.Value,
            PayDate: effectiveDate,
            AccrualStartDate: null,
            AccrualEndDate: null,
            ExpectedAmount: null,
            ActualAmount: null,
            VarianceAmount: null,
            FactorStart: null,
            FactorEnd: null,
            Currency: currency,
            PostingStatus: effectiveDate.Value < DateOnly.FromDateTime(DateTime.UtcNow) ? "Reference" : "Projected",
            SourceSystem: winningSource?.SourceSystem ?? "economic-terms",
            SourceRecordId: winningSource?.SourceRecordId,
            SourceAsOfUtc: winningSource?.AsOf,
            SourceUpdatedBy: winningSource?.UpdatedBy,
            SourceReason: winningSource?.Reason,
            IsDerivedFromEconomicTerms: true,
            IsCurrentProjection: true));
    }

    private static string InferScheduleEventType(string propertyName)
        => propertyName.Equals("paymentSchedule", StringComparison.OrdinalIgnoreCase)
            ? "Payment"
            : propertyName.Equals("cashflows", StringComparison.OrdinalIgnoreCase)
                ? "CashFlow"
                : "Schedule";

    private static bool IsScheduleHistoryEvent(SecurityMasterEventEnvelope envelope)
    {
        var eventType = envelope.EventType.AsSpan();
        if (eventType.Contains("factor", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("coupon", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("maturity", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("call", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("corp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var payloadText = envelope.Payload.GetRawText();
        return payloadText.Contains("factor", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("coupon", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("cashflow", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("maturity", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SnapshotOpenLotEnvelope> ExtractSnapshotOpenLots(PortfolioSnapshot snapshot)
    {
        var lots = new List<SnapshotOpenLotEnvelope>();
        foreach (var account in snapshot.Accounts.Values)
        {
            foreach (var lot in account.OpenLots ?? [])
            {
                lots.Add(new SnapshotOpenLotEnvelope(account.AccountId, account.DisplayName, lot));
            }
        }

        if (lots.Count > 0)
        {
            return lots;
        }

        foreach (var position in snapshot.Positions.Values)
        {
            foreach (var lot in position.OpenLots ?? [])
            {
                lots.Add(new SnapshotOpenLotEnvelope(lot.AccountId, null, lot));
            }
        }

        return lots;
    }

    private static decimal? TryResolveImpliedMarketPrice(Position? position)
    {
        if (position is null || position.Quantity == 0)
        {
            return null;
        }

        return position.AverageCostBasis + (position.UnrealizedPnl / position.Quantity);
    }

    private static (decimal? OriginalFace, decimal? CurrentFace, decimal? FactorAdjustedQuantity, decimal? FactorAdjustedFace) ProjectLotFaces(
        decimal quantity,
        SecurityMasterLotModelDto lotModel,
        decimal? currentFactor)
    {
        if (!lotModel.UsesFaceValue && !lotModel.SupportsFactorAdjustedExposure)
        {
            return (null, null, null, null);
        }

        if (lotModel.SupportsFactorAdjustedExposure)
        {
            var factor = currentFactor is > 0m ? currentFactor.Value : 1m;
            var currentFace = quantity * factor;
            return (quantity, currentFace, currentFace, currentFace);
        }

        return lotModel.UsesFaceValue
            ? (quantity, quantity, null, null)
            : (null, null, null, null);
    }

    private sealed record SecurityWorkbenchContext(
        SecurityDetailDto Detail,
        SecurityEconomicDefinitionRecord? EconomicDefinition,
        TradingParametersDto? TradingParameters,
        WinningSourceInfo? WinningSource,
        SecurityMasterDownstreamImpactDto DownstreamImpact);

    private sealed record SnapshotOpenLotEnvelope(
        string? AccountId,
        string? AccountDisplayName,
        OpenLot Lot);

    private sealed record WinningSourceInfo(
        string SourceSystem,
        string? SourceRecordId,
        DateTimeOffset? AsOf,
        string? UpdatedBy,
        string? Reason);

    private sealed record ClearwaterReferenceDataEvidence(
        Guid SecurityId,
        SecurityPriceGoldenCopyDto? GoldenCopyPrice,
        SecurityPricingHierarchyDto? PricingHierarchy,
        SecurityCashFlowSourceDto? CashFlowSource,
        IReadOnlyList<DataVendorEntitlementDto> VendorEntitlements,
        SecurityMasterQualityReportDto? QualityReport);

    private sealed record SecurityMasterOperatingScope(
        string? ClientId,
        string? AccountId,
        string? FundProfileId,
        Guid SecurityId);

    private sealed class VendorDataTypeComparer : IEqualityComparer<(string VendorName, DataVendorDataType DataType)>
    {
        public bool Equals(
            (string VendorName, DataVendorDataType DataType) x,
            (string VendorName, DataVendorDataType DataType) y) =>
            x.DataType == y.DataType
            && string.Equals(x.VendorName, y.VendorName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string VendorName, DataVendorDataType DataType) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.VendorName), obj.DataType);
    }

    private enum ConflictSide : byte
    {
        ProviderA = 0,
        ProviderB = 1
    }
}
