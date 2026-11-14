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

// Reference-data control evidence and the passport sections built from it.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private async Task<ReferenceDataControlEvidence> BuildReferenceDataEvidenceAsync(
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

        return new ReferenceDataControlEvidence(
            securityId,
            goldenCopy,
            hierarchy,
            cashFlowSource,
            entitlements,
            qualityReport);
    }

    private static IReadOnlyList<InstrumentPassportReferenceDataWorkbenchSectionDto> BuildReferenceDataControlSections(
        ReferenceDataControlEvidence evidence,
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

    private static InstrumentPassportReferenceDataWorkbenchSectionDto BuildPricingHierarchySection(
        ReferenceDataControlEvidence evidence)
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
                    : "No pricing hierarchy or market-price golden copy is available on this passport.",
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
            Summary: $"Golden-copy price {evidence.GoldenCopyPrice.GoldenCopyPrice.ToString("0.####", CultureInfo.InvariantCulture)} {PriceUnitLabel(evidence.GoldenCopyPrice.Unit)} from {evidence.GoldenCopyPrice.SelectedSource}; {staleLabel}. {hierarchyCount} pricing source(s) configured.",
            EvidenceCount: hierarchyCount + evidence.GoldenCopyPrice.ComparisonPrices.Count + 1,
            BlockingIssueCount: evidence.GoldenCopyPrice.IsStaleFallback ? 1 : 0);
    }

    private static InstrumentPassportReferenceDataWorkbenchSectionDto BuildCashFlowSourceSection(
        ReferenceDataControlEvidence evidence)
    {
        if (evidence.CashFlowSource is null)
        {
            return new InstrumentPassportReferenceDataWorkbenchSectionDto(
                SectionId: "cash-flow-source-governance",
                Title: "Cash-flow source governance",
                Status: "Review",
                Summary: "No cash-flow source assignment is available for this security.",
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
        ReferenceDataControlEvidence evidence)
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
        ReferenceDataControlEvidence evidence)
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
                : $"{violations.Length} quality violation(s) retained for this security from the {evidence.QualityReport.RunAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC run.",
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
}
