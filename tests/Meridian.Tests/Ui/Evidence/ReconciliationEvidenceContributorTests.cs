using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui.Evidence;

/// <summary>
/// Guards the reconciliation evidence contributor's run-service path. The failure mode
/// under guard: open reconciliation breaks or timing drift projected as Ready evidence,
/// or break case ids dropped from the work-item linkage — either would hide unresolved
/// money breaks from the evidence review surface.
/// </summary>
public sealed class ReconciliationEvidenceContributorTests
{
    [Fact]
    public async Task ContributeAsync_RunServiceNotRegistered_ReturnsWarningOnly()
    {
        var contributor = CreateContributor(new ServiceCollection());

        var contribution = await contributor.ContributeAsync(Context(StrategyRunSubject("run-1")));

        contribution.Nodes.Should().BeEmpty();
        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Be("Reconciliation run service is not registered.");
    }

    [Fact]
    public async Task ContributeAsync_StrategyRunSubject_UsesLatestRunLookupAndBuildsReadyNode()
    {
        var service = new Mock<IReconciliationRunService>();
        service
            .Setup(s => s.GetLatestForRunAsync("run-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDetail(matchCount: 12, openBreakCount: 0, hasTimingDrift: false));
        var contributor = CreateContributor(Register(service.Object));

        var contribution = await contributor.ContributeAsync(Context(StrategyRunSubject("run-1")));

        var node = contribution.Nodes.Should().ContainSingle().Which;
        node.Kind.Should().Be("reconciliation-run");
        node.Status.Should().Be(EvidenceStatusDto.Ready);
        node.Summary.Should().Contain("12 match(es)").And.Contain("0 open break(s)");
        contribution.RequiredEvidenceIds.Should().ContainSingle().Which.Should().Be(node.EvidenceId);
        service.Verify(s => s.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "strategy-run subjects resolve via the latest run lookup");
    }

    [Fact]
    public async Task ContributeAsync_ReconciliationReviewSubject_UsesGetByIdAndFlagsOpenBreaks()
    {
        var service = new Mock<IReconciliationRunService>();
        service
            .Setup(s => s.GetByIdAsync("recon-9", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDetail(
                matchCount: 5,
                openBreakCount: 2,
                hasTimingDrift: false,
                breakCheckIds: new[] { "check-cash", "check-position" }));
        var contributor = CreateContributor(Register(service.Object));
        var subject = new EvidenceSubjectDto(
            "recon-9", "reconciliation-review", "Review", "portfolio", null, "evidence");

        var contribution = await contributor.ContributeAsync(Context(subject));

        var node = contribution.Nodes.Should().ContainSingle().Which;
        node.Status.Should().Be(EvidenceStatusDto.ReviewRequired,
            "open breaks must force the evidence into review");
        node.RelatedWorkItemIds.Should().BeEquivalentTo("check-cash", "check-position");
        service.Verify(s => s.GetLatestForRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContributeAsync_TimingDriftWithoutOpenBreaks_YieldsReviewRequired()
    {
        var service = new Mock<IReconciliationRunService>();
        service
            .Setup(s => s.GetLatestForRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDetail(matchCount: 8, openBreakCount: 0, hasTimingDrift: true));
        var contributor = CreateContributor(Register(service.Object));

        var contribution = await contributor.ContributeAsync(Context(StrategyRunSubject("run-1")));

        contribution.Nodes.Should().ContainSingle()
            .Which.Status.Should().Be(EvidenceStatusDto.ReviewRequired,
                "timing drift alone is a review condition even with zero open breaks");
    }

    [Fact]
    public async Task ContributeAsync_DetailNotFound_ReturnsWarning()
    {
        var service = new Mock<IReconciliationRunService>();
        service
            .Setup(s => s.GetLatestForRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReconciliationRunDetail?)null);
        var contributor = CreateContributor(Register(service.Object));

        var contribution = await contributor.ContributeAsync(Context(StrategyRunSubject("run-404")));

        contribution.Nodes.Should().BeEmpty();
        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Be("No reconciliation evidence is available for 'run-404'.");
    }

    [Fact]
    public async Task ContributeAsync_StatementRunSubjectWithoutApiService_ReturnsWarning()
    {
        var contributor = CreateContributor(new ServiceCollection());
        var subject = new EvidenceSubjectDto(
            "stmt-1", "statement-run", "Statement run", "accounting", null, "evidence");

        var contribution = await contributor.ContributeAsync(Context(subject));

        contribution.Nodes.Should().BeEmpty();
        contribution.Warnings.Should().ContainSingle()
            .Which.Should().Be("Statement reconciliation API service is not registered.");
    }

    [Fact]
    public void Supports_MatchesOnlyReconciliationSubjectKinds()
    {
        var contributor = CreateContributor(new ServiceCollection());

        contributor.Supports(StrategyRunSubject("run-1")).Should().BeTrue();
        contributor.Supports(new EvidenceSubjectDto("s", "STATEMENT-RUN", "l", "w", null, "p"))
            .Should().BeTrue("subject-kind matching is case-insensitive");
        contributor.Supports(new EvidenceSubjectDto("s", "reconciliation-review", "l", "w", null, "p"))
            .Should().BeTrue();
        contributor.Supports(new EvidenceSubjectDto("s", "report-pack", "l", "w", null, "p"))
            .Should().BeFalse();
    }

    private static ReconciliationEvidenceContributor CreateContributor(ServiceCollection services) =>
        new(services.BuildServiceProvider());

    private static ServiceCollection Register(IReconciliationRunService service)
    {
        var services = new ServiceCollection();
        services.AddSingleton(service);
        return services;
    }

    private static EvidenceSubjectDto StrategyRunSubject(string runId) =>
        new(runId, "strategy-run", "Strategy run", "strategy", null, "evidence");

    private static EvidenceContributionContext Context(EvidenceSubjectDto subject) =>
        new(subject, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

    private static ReconciliationRunDetail MakeDetail(
        int matchCount,
        int openBreakCount,
        bool hasTimingDrift,
        string[]? breakCheckIds = null)
    {
        var summary = new ReconciliationRunSummary(
            ReconciliationRunId: "recon-9",
            RunId: "run-1",
            CreatedAt: DateTimeOffset.UtcNow,
            PortfolioAsOf: null,
            LedgerAsOf: null,
            MatchCount: matchCount,
            BreakCount: breakCheckIds?.Length ?? 0,
            OpenBreakCount: openBreakCount,
            HasTimingDrift: hasTimingDrift,
            AmountTolerance: 0.01m,
            MaxAsOfDriftMinutes: 5);
        var breaks = (breakCheckIds ?? Array.Empty<string>())
            .Select(checkId => new ReconciliationBreakDto(
                CheckId: checkId,
                Label: $"Break {checkId}",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakStatus.Open,
                MissingSource: "ledger",
                ExpectedAmount: 100m,
                ActualAmount: 90m,
                Variance: 10m,
                Severity: ReconciliationBreakSeverity.High,
                Reason: "Amounts diverge",
                ExpectedAsOf: null,
                ActualAsOf: null))
            .ToArray();
        return new ReconciliationRunDetail(
            summary,
            Matches: Array.Empty<ReconciliationMatchDto>(),
            Breaks: breaks);
    }
}
