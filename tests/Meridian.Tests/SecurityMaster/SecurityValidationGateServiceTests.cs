using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// <see cref="SecurityValidationGateService"/> keeps informational issues out of workflow gates: gate
/// consumers treat every issue they receive as reviewable, so an Info notice such as
/// <c>SM_ASSET_CLASS_TERMS_VERSION_BEHIND</c>, which needs no action, is dropped before the gate
/// decides, snapshots or hands the report on. Blocking state and counts are unchanged.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityValidationGateServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("7d0a1c52-0000-4000-8000-0000000000c2");

    private static SecurityValidationIssueDto Issue(SecurityValidationSeverityDto severity, string code)
        => new(severity, code, $"{code} title", $"{code} message", ["assetSpecificTerms"], "Review.", []);

    private static SecurityValidationReportDto Report(params SecurityValidationIssueDto[] issues)
        => new(
            SecurityId: SecurityId,
            Scope: "Security",
            EvaluatedAtUtc: new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero),
            HasBlockingIssues: false,
            CriticalIssueCount: 0,
            ErrorIssueCount: 0,
            Issues: issues);

    private static (SecurityValidationGateService Gate, ISecurityValidationSnapshotStore Snapshots) Gate(SecurityValidationReportDto report)
    {
        var validation = Substitute.For<ISecurityValidationService>();
        validation.ValidateSecurityAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(report));
        var snapshots = Substitute.For<ISecurityValidationSnapshotStore>();
        snapshots.RecordAsync(Arg.Any<SecurityValidationReportDto>(), Arg.Any<SecurityValidationSnapshotRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new SecurityValidationSnapshotDto(
                Guid.NewGuid(),
                SecurityId,
                "Security",
                SecurityValidationWorkflowDto.LedgerPosting,
                null,
                null,
                "test",
                DateTimeOffset.UtcNow,
                "hash",
                call.Arg<SecurityValidationReportDto>(),
                [])));
        return (new SecurityValidationGateService(Substitute.For<ISecurityMasterQueryService>(), validation, snapshots), snapshots);
    }

    [Fact]
    public async Task InformationalIssues_AreDropped_AndBlockingStateIsUnchanged()
    {
        var report = Report(
            Issue(SecurityValidationSeverityDto.Warning, "W1"),
            Issue(SecurityValidationSeverityDto.Info, "SM_ASSET_CLASS_TERMS_VERSION_BEHIND"));
        var (gate, _) = Gate(report);

        var result = await gate.ValidateSecurityAsync(SecurityId, SecurityValidationWorkflowDto.LedgerPosting);

        result.Report.Issues.Select(static issue => issue.Code).Should().Equal("W1");
        result.Report.HasBlockingIssues.Should().Be(report.HasBlockingIssues);
        result.Report.CriticalIssueCount.Should().Be(report.CriticalIssueCount);
        result.Report.ErrorIssueCount.Should().Be(report.ErrorIssueCount);
        result.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task AReportWithoutInformationalIssues_IsHandedOnAsTheSameInstance()
    {
        var report = Report(Issue(SecurityValidationSeverityDto.Warning, "W1"), Issue(SecurityValidationSeverityDto.Error, "E1")) with
        {
            HasBlockingIssues = true,
            ErrorIssueCount = 1
        };
        var (gate, _) = Gate(report);

        var result = await gate.ValidateSecurityAsync(SecurityId, SecurityValidationWorkflowDto.ReconciliationBreakIntake);

        result.Report.Should().BeSameAs(report);
        result.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Snapshots_RecordTheReportTheGateDecidedOn()
    {
        var (gate, snapshots) = Gate(Report(
            Issue(SecurityValidationSeverityDto.Warning, "W1"),
            Issue(SecurityValidationSeverityDto.Info, "SM_ASSET_CLASS_TERMS_VERSION_BEHIND")));

        var result = await gate.ValidateSecurityAsync(
            SecurityId,
            SecurityValidationWorkflowDto.LedgerPosting,
            workflowReference: "posting-1",
            persistSnapshot: true);

        await snapshots.Received(1).RecordAsync(
            Arg.Is<SecurityValidationReportDto>(recorded => recorded.Issues.All(issue => issue.Severity != SecurityValidationSeverityDto.Info)),
            Arg.Any<SecurityValidationSnapshotRequestDto>(),
            Arg.Any<CancellationToken>());
        result.Snapshot!.Report.Should().BeSameAs(result.Report);
    }

    [Fact]
    public async Task TheRealValidationService_ReportsTheNotice_AndTheGateDropsIt()
    {
        var terms = SecurityAssetClassTermsUpgraderTests.Json(SecurityAssetClassTermsUpgraderTests.V1Text);
        var record = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", terms) with
        {
            SecurityId = SecurityId
        };
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SecurityProjectionRecord>>([record]));
        var validation = new SecurityValidationService(store, AssetClassValidatorRegistry.CreateDefault());
        var gate = new SecurityValidationGateService(
            Substitute.For<ISecurityMasterQueryService>(),
            validation,
            Substitute.For<ISecurityValidationSnapshotStore>());

        var full = await validation.ValidateSecurityAsync(SecurityId);
        var gated = await gate.ValidateSecurityAsync(SecurityId, SecurityValidationWorkflowDto.StrategyRunPreflight);

        full.Issues.Should().Contain(static issue => issue.Code == "SM_ASSET_CLASS_TERMS_VERSION_BEHIND");
        gated.Report.Issues.Should().NotContain(static issue => issue.Code == "SM_ASSET_CLASS_TERMS_VERSION_BEHIND");
        gated.Report.Issues.Select(static issue => issue.Code)
            .Should().Equal(full.Issues.Select(static issue => issue.Code).Where(static code => code != "SM_ASSET_CLASS_TERMS_VERSION_BEHIND"));
        gated.Report.HasBlockingIssues.Should().Be(full.HasBlockingIssues);
        gated.IsBlocked.Should().Be(full.HasBlockingIssues);
    }
}
