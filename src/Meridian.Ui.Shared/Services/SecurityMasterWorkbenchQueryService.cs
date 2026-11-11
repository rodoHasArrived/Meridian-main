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
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Composes the selected-security trust snapshot used by the Security Master workstation.
/// The endpoint is selection-scoped and additive, so the heavier downstream checks only run
/// when the operator explicitly loads or refreshes a security drill-in.
/// </summary>
public sealed partial class SecurityMasterWorkbenchQueryService : ISecurityMasterWorkbenchQueryService
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
        IHttpContextAccessor? httpContextAccessor = null,
        Meridian.Contracts.Calendars.IBusinessDayCalendarSource? calendars = null,
        Meridian.Contracts.FixedIncome.IBondCalculationPolicy? bondCalculationPolicy = null,
        TimeProvider? timeProvider = null,
        Microsoft.Extensions.Logging.ILogger<SecurityMasterWorkbenchQueryService>? logger = null)
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
        _calendars = calendars;
        _bondCalculationPolicy = bondCalculationPolicy;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityMasterWorkbenchQueryService>.Instance;
    }

    public async Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var nowUtc = _timeProvider.GetUtcNow();

        // Tenant isolation (SEC-005): sanitize the operator-supplied fund scope ONCE here so every
        // fund-scoped evidence path the snapshot and its instrument passport fan out to — downstream
        // impact, open lots, pricing (golden-copy/hierarchy), and entitlement applicability —
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
        var declaredExercise = await ResolveDeclaredExerciseAsync(detail, nowUtc, ct).ConfigureAwait(false);
        var scheduleSummary = BuildScheduleSummary(detail, economic, corporateActions, winningSource, declaredExercise);
        var lotModel = BuildLotModel(detail, economic, trading);
        var scheduleBook = BuildScheduleBook(detail, economic, corporateActions, history, winningSource, scheduleSummary, declaredExercise);
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
                SubType: ClassificationLabelReader.Normalize(economic?.SubType),
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
                SubType: ClassificationLabelReader.Normalize(economic?.SubType),
                AssetFamily: ClassificationLabelReader.Normalize(economic?.AssetFamily),
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
            AssetFamily: ClassificationLabelReader.Normalize(economic?.AssetFamily),
            SubType: ClassificationLabelReader.Normalize(economic?.SubType),
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

    private sealed record ReferenceDataControlEvidence(
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
