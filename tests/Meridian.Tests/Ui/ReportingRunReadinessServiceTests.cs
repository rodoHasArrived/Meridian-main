using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunReadinessServiceTests
{
    [Fact]
    public async Task AssessAsync_DraftIgnoresFinalOnlyBlockerButPreservesFinalCapabilityFlag()
    {
        var service = new ReportingRunReadinessService(
            new DefaultReportingTemplateCatalog(),
            dependencyEvaluator: new FinalOnlyBlockingEvaluator(),
            utcNow: () => new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto("fund-a"),
            "2026-06",
            new DateOnly(2026, 6, 30),
            new ReportingLedgerBookSelectionDto(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: false);

        var result = await service.AssessAsync(new ReportingRunRequestDto(
            "investor-monthly-statement",
            AsOfDate: parameters.AsOfDate,
            Parameters: parameters));

        result.Status.Should().Be(ReportingRunReadinessStatusDto.Ready);
        result.CanGenerateDraft.Should().BeTrue();
        result.CanGenerateFinal.Should().BeFalse();
        result.BlockingReasons.Should().BeEmpty();
        result.Checks.Single(check => check.CheckId == "final-only").Status
            .Should().Be(ReportingRunReadinessStatusDto.Blocked);
    }

    [Fact]
    public async Task AssessAsync_MalformedFinalityEnumFailsPreflightForDraftAndFinalCapabilities()
    {
        var service = new ReportingRunReadinessService(
            new DefaultReportingTemplateCatalog(),
            dependencyEvaluator: new FinalOnlyBlockingEvaluator());
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto("fund-a"),
            "2026-06",
            new DateOnly(2026, 6, 30),
            new ReportingLedgerBookSelectionDto(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            (ReportingFinalityDto)999,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);

        var result = await service.AssessAsync(new ReportingRunRequestDto(
            "investor-monthly-statement",
            AsOfDate: parameters.AsOfDate,
            Parameters: parameters));

        result.Status.Should().Be(ReportingRunReadinessStatusDto.Blocked);
        result.CanGenerateDraft.Should().BeFalse();
        result.CanGenerateFinal.Should().BeFalse();
        result.Checks.Single(check => check.CheckId == "parameter-enums").Summary
            .Should().Contain(nameof(ReportingRunParametersDto.Finality));
    }

    private sealed class FinalOnlyBlockingEvaluator : IReportingRunReadinessDependencyEvaluator
    {
        public Task<IReadOnlyList<ReportingRunReadinessCheckDto>> EvaluateAsync(
            ReportingRunRequestDto request,
            ReportingTemplateMetadata template,
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext? accessContext,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingRunReadinessCheckDto>>(
            [
                new ReportingRunReadinessCheckDto(
                    "final-only",
                    "Final-only gate",
                    ReportingRunReadinessStatusDto.Blocked,
                    "Final output is awaiting close evidence.",
                    1,
                    BlocksDraft: false,
                    BlocksFinal: true)
            ]);
    }
}
