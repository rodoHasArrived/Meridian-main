using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunCertificationServiceTests
{
    [Fact]
    public void Certify_SameInputsInDifferentRowOrder_ProducesStableContentAddress()
    {
        var sut = new ReportingRunCertificationService();
        var first = sut.Certify(
            Template(),
            Readiness("evaluation-one", new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero)),
            [Row(("amount", "10"), ("account", "cash")), Row(("account", "payable"), ("amount", "5"))],
            "retained-reporting-rows",
            Access());
        var second = sut.Certify(
            Template(),
            Readiness("evaluation-two", new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero)),
            [Row(("amount", "5"), ("account", "payable")), Row(("account", "cash"), ("amount", "10"))],
            "retained-reporting-rows",
            Access());

        second.Snapshot.SnapshotHash.Should().Be(first.Snapshot.SnapshotHash);
        second.Snapshot.SnapshotId.Should().Be(first.Snapshot.SnapshotId);
        second.Snapshot.ReconciliationCheckpointId.Should().NotBe(first.Snapshot.ReconciliationCheckpointId);
        first.OperationalScope.TenantId.Should().Be("tenant-a");
        first.OperationalScope.CompanyId.Should().Be("company-a");
    }

    [Fact]
    public void Certify_CallerSuppliedSource_FailsClosed()
    {
        var act = () => new ReportingRunCertificationService().Certify(
            Template(),
            Readiness("evaluation-one", DateTimeOffset.UtcNow),
            [Row(("account", "cash"))],
            "custom-request-dataset",
            Access());

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be certified*");
    }

    [Fact]
    public void EvaluateManifest_CrossTenantAdministrator_IsDeniedBeforeOverride()
    {
        var certified = new ReportingRunCertificationService().Certify(
            Template(),
            Readiness("evaluation-one", DateTimeOffset.UtcNow),
            [Row(("account", "cash"))],
            "retained-reporting-rows",
            Access());
        var manifest = new ReportingOutputManifest(
            "run-a",
            "test-report",
            new DateOnly(2026, 6, 30),
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            OperationalScope: certified.OperationalScope,
            ImmutableAccessScope: certified.AccessScope,
            CertifiedSnapshot: certified.Snapshot);

        var result = ReportAccessPolicyEvaluator.Evaluate(
            manifest,
            new ReportAccessQueryContext(
                "admin-b",
                CompanyId: "company-b",
                HasGlobalOverride: true,
                TenantId: "tenant-b",
                RequireBoundScope: true));

        result.IsAccessible.Should().BeFalse();
        result.Reason.Should().Contain("another tenant");
    }

    private static ReportingTemplateMetadata Template() => new(
        "test-report",
        ReportingTemplateFamily.CustomReport,
        "Test report",
        "2.0.0",
        ["detail"],
        ImmutableDictionary<string, string>.Empty,
        ReportWriterGrids:
        [
            new ReportWriterGridDefinitionDto(
                "detail",
                "Detail",
                ReportWriterGridKindDto.Detail,
                RowFields: ["account"],
                Metrics: [new ReportWriterMetricDefinitionDto("amount", "amount", ReportWriterAggregateFunctionDto.Sum)])
        ],
        AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.CompanyWide, CompanyId: "company-a"));

    private static ReportingRunReadinessDto Readiness(string evaluationId, DateTimeOffset evaluatedAt) => new(
        evaluationId,
        evaluatedAt,
        new VersionedReportTemplateIdDto("test-report", 2),
        new ReportingRunParametersDto(
            new ReportingRunScopeDto("fund-a"),
            "2026-06",
            new DateOnly(2026, 6, 30),
            new ReportingLedgerBookSelectionDto(LedgerBookCode: "primary"),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true),
        ReportingRunReadinessStatusDto.Ready,
        CanGenerateDraft: true,
        CanGenerateFinal: true,
        Checks:
        [
            new ReportingRunReadinessCheckDto(
                "source",
                "Source",
                ReportingRunReadinessStatusDto.Ready,
                "Source is ready.",
                0,
                false,
                false,
                EvidenceReferences: ["source:checkpoint-a"])
        ],
        BlockingReasons: [],
        EvidenceHash: new string('a', 64));

    private static ReportAccessQueryContext Access() => new(
        "operator-a",
        ["report-reviewers"],
        "company-a",
        TenantId: "tenant-a",
        RequireBoundScope: true);

    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
}
