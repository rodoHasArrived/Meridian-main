using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels;

internal sealed class SecurityMasterSearchWorkspaceService
{
    public IReadOnlyList<string> BuildAssetClassOptions(IEnumerable<SecurityMasterWorkstationDto> results, string allAssetClassesLabel)
    {
        var options = results
            .Select(result => result.Classification.AssetClass)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        options.Insert(0, allAssetClassesLabel);
        return options;
    }

    public IReadOnlyList<string> BuildProviderOptions(
        IEnumerable<SecurityMasterWorkstationDto> results,
        string allProvidersLabel,
        Func<SecurityMasterWorkstationDto, string> getMatchedProvider)
    {
        var options = results
            .Select(getMatchedProvider)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        options.Insert(0, allProvidersLabel);
        return options;
    }

    public SecurityMasterWorkstationDto[] ApplyFilters(
        IEnumerable<SecurityMasterWorkstationDto> results,
        string selectedAssetClassFilter,
        string selectedProviderFilter,
        bool showMappingGapsOnly,
        string allAssetClassesLabel,
        string allProvidersLabel,
        Func<SecurityMasterWorkstationDto, string> getMatchedProvider,
        Func<SecurityMasterWorkstationDto, bool> hasProviderMapping)
    {
        IEnumerable<SecurityMasterWorkstationDto> filteredResults = results;

        if (!string.Equals(selectedAssetClassFilter, allAssetClassesLabel, StringComparison.Ordinal))
        {
            filteredResults = filteredResults.Where(result =>
                string.Equals(result.Classification.AssetClass, selectedAssetClassFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(selectedProviderFilter, allProvidersLabel, StringComparison.Ordinal))
        {
            filteredResults = filteredResults.Where(result =>
                string.Equals(getMatchedProvider(result), selectedProviderFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (showMappingGapsOnly)
        {
            filteredResults = filteredResults.Where(result => !hasProviderMapping(result));
        }

        return filteredResults.ToArray();
    }
}

internal sealed class SecurityMasterPrintProjectionService
{
    public SecurityMasterPrintProjection BuildProjection(
        SecurityMasterTrustSnapshotDto snapshot,
        string goldenCopySourceText,
        string latestHistoryEventText,
        string printDistributionText)
    {
        return new SecurityMasterPrintProjection(
            Sections:
            [
                new SecurityMasterPrintSectionItem("Overview deck", "Search outcome, trust posture, and downstream scope stay on the first page.", "01"),
                new SecurityMasterPrintSectionItem("Company context", "Issuer profile, country risk, listing, and settlement cues support review.", "02"),
                new SecurityMasterPrintSectionItem("Corporate actions", "Upcoming events and readiness notes follow the company brief.", "03"),
                new SecurityMasterPrintSectionItem("Evidence trail", "Winning source, audit history, and delivery routing close the packet.", "04")
            ],
            ChecklistItems:
            [
                new SecurityMasterChecklistItem("Canonical identifiers attested", "Data operations", snapshot.TrustPosture.HasOpenConflicts ? "Review" : "Ready"),
                new SecurityMasterChecklistItem("Validation blockers cleared", "Security master", snapshot.ValidationReport?.HasBlockingIssues == true ? "Review" : "Ready"),
                new SecurityMasterChecklistItem("Trading parameters complete", "Trading operations", snapshot.TrustPosture.TradingParametersComplete ? "Ready" : "Review"),
                new SecurityMasterChecklistItem("Corporate actions reviewed", "Fund operations", snapshot.TrustPosture.CorporateActionsTrusted ? "Ready" : "Review"),
                new SecurityMasterChecklistItem("Distribution lane confirmed", "Reporting", snapshot.DownstreamImpact.Severity is SecurityMasterImpactSeverity.None or SecurityMasterImpactSeverity.Low ? "Ready" : "Draft")
            ],
            EvidenceItems:
            [
                new SecurityMasterEvidenceItem("Winning source", goldenCopySourceText, FirstNonEmpty(snapshot.EconomicDefinition.WinningSourceReason, "Golden copy rationale")),
                new SecurityMasterEvidenceItem("Validation summary", BuildValidationSummaryText(snapshot), "Validation report"),
                new SecurityMasterEvidenceItem("Identifier coverage", snapshot.IdentifierSummary?.Summary ?? "Identifier summary unavailable.", "Identifier resolution"),
                new SecurityMasterEvidenceItem("Schedule model", snapshot.ScheduleBook?.Summary ?? snapshot.ScheduleSummary?.Summary ?? "Schedule summary unavailable.", FormatScheduleSourceLabel(snapshot)),
                new SecurityMasterEvidenceItem("Lot model", snapshot.OpenLotReadModel?.Summary ?? snapshot.LotModel?.Summary ?? "Lot model summary unavailable.", "Lot/open-position guidance"),
                new SecurityMasterEvidenceItem("Schema compatibility", snapshot.SchemaCompatibility?.Summary ?? "Schema compatibility unavailable.", "Snapshot projection"),
                new SecurityMasterEvidenceItem("Latest audit event", latestHistoryEventText, "History stream"),
                new SecurityMasterEvidenceItem("Downstream scope", snapshot.DownstreamImpact.Summary, printDistributionText)
            ]);
    }

    private static string BuildValidationSummaryText(SecurityMasterTrustSnapshotDto snapshot)
    {
        var report = snapshot.ValidationReport;
        if (report is null)
        {
            return "Validation report unavailable.";
        }

        if (report.Issues.Count == 0)
        {
            return "No validation issues detected.";
        }

        var blockingCount = report.CriticalIssueCount + report.ErrorIssueCount;
        var advisoryCount = Math.Max(0, report.Issues.Count - blockingCount);
        if (blockingCount > 0 && advisoryCount > 0)
        {
            return $"{blockingCount} blocking issue(s) • {advisoryCount} advisory issue(s)";
        }

        return blockingCount > 0
            ? $"{blockingCount} blocking issue(s)"
            : $"{advisoryCount} advisory issue(s)";
    }

    private static string FormatScheduleSourceLabel(SecurityMasterTrustSnapshotDto snapshot)
        => string.IsNullOrWhiteSpace(snapshot.ScheduleBook?.SourceSummary ?? snapshot.ScheduleSummary?.SourceSummary)
            ? "Schedule source"
            : $"Schedule source: {snapshot.ScheduleBook?.SourceSummary ?? snapshot.ScheduleSummary?.SourceSummary}";

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

internal sealed record SecurityMasterPrintProjection(
    IReadOnlyList<SecurityMasterPrintSectionItem> Sections,
    IReadOnlyList<SecurityMasterChecklistItem> ChecklistItems,
    IReadOnlyList<SecurityMasterEvidenceItem> EvidenceItems);
