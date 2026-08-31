using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReportPackValidationServiceTests
{
    [Fact]
    public void Validate_WithSecurityMasterValidationResult_AddsReportPackEvidenceGateIssue()
    {
        var asOf = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        var row = new EnrichedLedgerRow(
            AccountName: "Assets:Securities:AAPL",
            AccountType: "Asset",
            Symbol: "AAPL",
            Currency: "USD",
            AssetClass: "Equity",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "AAPL",
            SubType: "CommonShare",
            AssetFamily: "Equity",
            IssuerType: "Corporate",
            RiskCountry: "US",
            LookupQuality: "resolved",
            DisplayName: "Apple Inc.",
            NetBalance: 100m);
        var report = new ReportPack(
            Guid.NewGuid(),
            "fund-alpha",
            ReportKind.TrialBalance,
            asOf,
            asOf,
            [row],
            [new AssetClassSection("Equity", [row], 100m)],
            100m);
        var validationIssue = new SecurityValidationIssueDto(
            SecurityValidationSeverityDto.Error,
            "SM_ACCOUNTING_CLASSIFICATION_MISSING",
            "Accounting classification is missing",
            "The record does not expose an accounting classification for ledger posting and report grouping.",
            ["commonTerms.accountingClassification"],
            "Attach the ledger/reporting accounting classification.",
            []);
        var securityReport = new SecurityValidationReportDto(
            SecurityId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Scope: "Security",
            EvaluatedAtUtc: asOf,
            HasBlockingIssues: true,
            CriticalIssueCount: 0,
            ErrorIssueCount: 1,
            Issues: [validationIssue]);
        var context = new ReportPackValidationContext(
            ReportId: report.ReportId,
            AsOf: asOf,
            Report: report,
            Ledger: new FundLedgerSummary(
                "fund-alpha",
                "Fund Alpha",
                FundLedgerScope.Consolidated,
                null,
                asOf,
                JournalEntryCount: 1,
                LedgerEntryCount: 1,
                AssetBalance: 100m,
                LiabilityBalance: 0m,
                EquityBalance: 0m,
                RevenueBalance: 0m,
                ExpenseBalance: 0m,
                TrialBalance: [],
                Journal: [],
                EntityCount: 0,
                SleeveCount: 0,
                VehicleCount: 0),
            Reconciliation: new ReconciliationSummary(1, 0, 0m, []),
            RunCount: 1,
            SecurityMissingCount: 0,
            Formats: [GovernanceReportArtifactFormatDto.Json],
            SecurityValidationResults:
            [
                new SecurityValidationGateResultDto(
                    SecurityValidationWorkflowDto.ReportPackEvidence,
                    "AAPL",
                    securityReport.SecurityId,
                    IsResolved: true,
                    IsBlocked: true,
                    Report: securityReport,
                    Snapshot: null)
            ]);
        var service = new ReportPackValidationService();

        var issues = service.Validate(context);

        issues.Should().Contain(issue =>
            issue.Code == "report-pack.security-master.sm_accounting_classification_missing"
            && issue.Severity == GovernanceReportValidationSeverityDto.Critical
            && issue.AffectedSecurity == "AAPL"
            && issue.AffectedSection == "security-master");
    }

    [Theory]
    [InlineData("seeded", "SEEDED")]
    [InlineData("simulated", "SIMULATED")]
    [InlineData("mystery-token", "SIMULATED")]
    public void Validate_WithNonRealProvenanceToken_AddsCriticalBlockAndForcesReviewRequired(
        string token,
        string expectedLabel)
    {
        var service = new ReportPackValidationService();

        var issues = service.Validate(CreateMinimalContext(token));
        var status = service.ResolveStatus(issues);

        issues.Should().ContainSingle(issue =>
            issue.Code == "report-pack.provenance.simulated-source"
            && issue.Severity == GovernanceReportValidationSeverityDto.Critical
            && issue.AffectedSection == "provenance"
            && issue.Title.Contains(expectedLabel));
        status.Should().Be(GovernanceReportPackStatusDto.ReviewRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("real")]
    public void Validate_WithRealOrAbsentProvenanceToken_AddsNoProvenanceIssue(string? token)
    {
        var service = new ReportPackValidationService();

        var issues = service.Validate(CreateMinimalContext(token));

        issues.Should().NotContain(issue => issue.Code == "report-pack.provenance.simulated-source");
    }

    private static ReportPackValidationContext CreateMinimalContext(string? dataProvenanceToken)
    {
        var asOf = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        var row = new EnrichedLedgerRow(
            AccountName: "Assets:Cash",
            AccountType: "Asset",
            Symbol: null,
            Currency: "USD",
            AssetClass: "Cash",
            PrimaryIdentifierKind: null,
            PrimaryIdentifierValue: null,
            SubType: null,
            AssetFamily: "Cash",
            IssuerType: null,
            RiskCountry: null,
            LookupQuality: "resolved",
            DisplayName: "Cash",
            NetBalance: 100m);
        var report = new ReportPack(
            Guid.NewGuid(),
            "fund-alpha",
            ReportKind.TrialBalance,
            asOf,
            asOf,
            [row],
            [new AssetClassSection("Cash", [row], 100m)],
            100m);

        return new ReportPackValidationContext(
            ReportId: report.ReportId,
            AsOf: asOf,
            Report: report,
            Ledger: new FundLedgerSummary(
                "fund-alpha",
                "Fund Alpha",
                FundLedgerScope.Consolidated,
                null,
                asOf,
                JournalEntryCount: 1,
                LedgerEntryCount: 1,
                AssetBalance: 100m,
                LiabilityBalance: 0m,
                EquityBalance: 0m,
                RevenueBalance: 0m,
                ExpenseBalance: 0m,
                TrialBalance: [],
                Journal: [],
                EntityCount: 0,
                SleeveCount: 0,
                VehicleCount: 0),
            Reconciliation: new ReconciliationSummary(1, 0, 0m, []),
            RunCount: 1,
            SecurityMissingCount: 0,
            Formats: [GovernanceReportArtifactFormatDto.Json],
            DataProvenanceToken: dataProvenanceToken);
    }
}
