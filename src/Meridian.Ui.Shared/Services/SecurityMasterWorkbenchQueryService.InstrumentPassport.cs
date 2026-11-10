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

// The instrument passport: reference-data and operations workbenches, their panels, readiness and pricing.
public sealed partial class SecurityMasterWorkbenchQueryService
{
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
        var referenceDataEvidence = await BuildReferenceDataEvidenceAsync(
            snapshot.SecurityId,
            fundProfileId,
            ct).ConfigureAwait(false);
        var operatingModel = BuildOperatingModel(
            snapshot,
            identifierSummary,
            fundProfileId,
            providerConfidence,
            lifecycleEvents,
            referenceDataEvidence);
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
        // A declared call or put schedule that is blocked leaves its option dates unknown, so the
        // obligation and cash-flow sections are not ready however many other rows the book holds.
        var declaredExerciseBlockerCount = DeclaredExerciseBlockers(snapshot).Count;
        var hasObligationEvidence =
            declaredExerciseBlockerCount == 0 &&
            (snapshot.CorporateActions.Count > 0 ||
            snapshot.ScheduleBook?.Events.Count > 0 ||
            snapshot.ScheduleSummary?.HasEconomicScheduleTerms == true);
        var hasCashFlowReadiness =
            declaredExerciseBlockerCount == 0 &&
            (snapshot.ScheduleSummary?.SupportsCashflowSchedule == true ||
            snapshot.ScheduleBook?.SupportsCashflowSchedule == true ||
            snapshot.CorporateActions.Count > 0);
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
                BlockingIssueCount: (snapshot.TrustPosture.HasOpenConflicts ? snapshot.TrustPosture.OpenConflictCount : 0)
                    + declaredExerciseBlockerCount),
            new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "cash-flow-readiness",
                Title: "Projected cash-flow readiness",
                Status: hasCashFlowReadiness ? "Ready" : "Review",
                Summary: snapshot.ScheduleSummary?.Summary ?? pricing.Summary,
                EvidenceCount: (snapshot.ScheduleBook?.Events.Count ?? 0) + snapshot.CorporateActions.Count,
                BlockingIssueCount: hasCashFlowReadiness ? 0 : Math.Max(1, declaredExerciseBlockerCount)),
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
        var declaredExerciseBlockerCount = DeclaredExerciseBlockers(snapshot).Count;
        var paymentScheduleReady = declaredExerciseBlockerCount == 0
            && (scheduleEventCount > 0 || snapshot.ScheduleSummary?.SupportsCashflowSchedule == true);
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
                Status: paymentScheduleReady ? "Ready" : "Review",
                Detail: snapshot.ScheduleSummary?.Summary ?? "Payment, amortization, PIK, covenant, and paydown schedule evidence is not retained.",
                EvidenceCount: scheduleEventCount,
                BlockingIssueCount: paymentScheduleReady ? 0 : Math.Max(1, declaredExerciseBlockerCount)),
            new(
                ItemId: "factors-collateral-covenants",
                Label: "Factors, collateral, covenants",
                Value: factorCount > 0 ? $"{factorCount} factor row(s)" : "Review required",
                Status: factorCount > 0 || snapshot.ScheduleSummary?.SupportsFactorHistory == true ? "Ready" : "Review",
                Detail: "Structured-asset factor, collateral, covenant, and paydown evidence must be linked when applicable.",
                EvidenceCount: factorCount,
                BlockingIssueCount: factorCount > 0 || snapshot.ScheduleSummary?.SupportsFactorHistory == true ? 0 : 1)
        };
        items.AddRange(BuildConflictItems(
            snapshot,
            "terms-conflict",
            assessment => IsTermsConflict(assessment, snapshot.EconomicDefinition.AssetClass)));

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

    // Conflict routing reads declared facts (SecurityMasterConflictRouting): identifier names and kinds
    // and provider-evidence segments for the identity panel; for the terms panel the declared mismatch
    // kinds, the terms containers and the term keys the record's own asset class declares.
    private static bool IsProviderOrIdentifierConflict(SecurityMasterConflictAssessmentDto assessment) =>
        SecurityMasterConflictRouting.IsIdentityConflict(assessment.Conflict);

    private static bool IsTermsConflict(SecurityMasterConflictAssessmentDto assessment, string? assetClass) =>
        SecurityMasterConflictRouting.IsTermsConflict(assessment.Conflict, assetClass);

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
        // Close readiness needs the record's terms; a blocked declared call or put schedule means they
        // cannot be relied on for option dates, whatever else is retained.
        var declaredExerciseBlockers = DeclaredExerciseBlockers(snapshot);
        var closeBlockedAction = declaredExerciseBlockers.Count == 0
            ? "Clear unresolved Security Master blockers before close package use."
            : $"Resolve the blocked declared call/put schedule ({string.Join(", ", declaredExerciseBlockers.Select(static blocker => blocker.Code))}) before close package use.";
        var termsReady = declaredExerciseBlockers.Count == 0
            && (snapshot.ScheduleBook?.Events.Count > 0
            || snapshot.ScheduleSummary?.HasEconomicScheduleTerms == true
            || snapshot.CorporateActions.Count > 0
            || pricing.TradingParameters is not null);
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
            BuildReadiness("close", "Close-ready", closeReady, snapshot.DownstreamImpact.Summary, snapshot.DownstreamImpact.Links.Count + (termsReady ? 1 : 0), closeReady ? 0 : Math.Max(1, declaredExerciseBlockers.Count), closeBlockedAction, nextAction?.Target),
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
            // The snapshot's economic definition is already decoded (MapToEconomicDefinition).
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
        var assetClass = NormalizeOptional(snapshot.EconomicDefinition.AssetClass);
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
            AssetFamily: NormalizeOptional(snapshot.EconomicDefinition.AssetFamily) ?? primaryDescriptor?.SecurityMasterAssetFamily,
            SubType: NormalizeOptional(snapshot.EconomicDefinition.SubType) ?? primaryDescriptor?.SecurityMasterSubType,
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

    private static string PriceUnitLabel(SecurityPriceUnit unit) => unit switch
    {
        SecurityPriceUnit.PercentOfPar => "% of par",
        SecurityPriceUnit.CurrencyPerUnit => "currency per unit",
        _ => "(quote unit unspecified)"
    };

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
            // The snapshot's economic definition is already decoded (MapToEconomicDefinition).
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
}
