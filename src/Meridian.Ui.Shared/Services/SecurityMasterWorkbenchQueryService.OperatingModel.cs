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

// The Security Master operating model: stages, entitlement applicability, operator metadata and approval posture.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static SecurityMasterOperatingModelDto BuildOperatingModel(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        string? fundProfileId,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence,
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents,
        ReferenceDataControlEvidence referenceDataEvidence)
    {
        var context = new SecurityMasterOperatingScope(
            ClientId: null,
            AccountId: ResolveDominantAccountId(snapshot),
            FundProfileId: NormalizeOptional(fundProfileId),
            SecurityId: snapshot.SecurityId);
        var controls = BuildReferenceDataControlSections(referenceDataEvidence, lifecycleEvents);
        var entitlementApplicability = BuildEntitlementApplicability(referenceDataEvidence.VendorEntitlements, context);
        var operatorMetadata = BuildOperatorMetadata(referenceDataEvidence, entitlementApplicability);
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
        ReferenceDataControlEvidence evidence,
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
        var configured = NormalizeOptional(configuredScope);
        if (configured is null)
            return true;

        var selected = NormalizeOptional(selectedScope);
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
            .Select(static lot => NormalizeOptional(lot.AccountScopeId))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Key)
            .ToArray();

        return accountIds is { Length: > 0 } ? accountIds[0] : null;
    }

    private static IReadOnlyList<string> DistinctProfileValues(params IEnumerable<string>[] values) =>
        values
            .SelectMany(static value => value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
