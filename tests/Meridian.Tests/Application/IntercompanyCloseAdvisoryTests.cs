using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Moq;
using Xunit;

namespace Meridian.Tests.Application;

public sealed class IntercompanyCloseAdvisoryTests
{
    [Fact]
    public async Task ObligationWarningsDoNotChangeAuthoritativeCloseDecision()
    {
        var workflows = new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict);
        var baseline = await new FinancialOperationsCommandCenterReadService(workflows.Object).GetCommandCenterAsync();
        var observation = new IntercompanyObligationMonitorDto(new(2026, 11, 13), 30, DateTimeOffset.UtcNow,
            "Current", true, "synthetic-source-version", [],
            [new("intercompany.agreement.expiring", "Warning", "Agreement expiry is approaching.")], null, []);
        var source = new Mock<IIntercompanyCloseAdvisorySource>();
        source.Setup(x => x.GetAsync(It.IsAny<CloseReadinessScopeDto>(), "tenant-a", "company-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(observation);
        var actual = await new FinancialOperationsCommandCenterReadService(workflows.Object,
            intercompanyAdvisories: source.Object).GetCommandCenterAsync(tenantId: "tenant-a", companyId: "company-a");

        AssertSameDecision(baseline, actual);
        Assert.Same(observation, actual.CloseReadiness!.IntercompanyAdvisories);
        workflows.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdvisoryFailureRemainsUnavailableWithoutChangingCloseEligibility()
    {
        var workflows = new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict);
        var baseline = await new FinancialOperationsCommandCenterReadService(workflows.Object).GetCommandCenterAsync();
        var source = new Mock<IIntercompanyCloseAdvisorySource>();
        source.Setup(x => x.GetAsync(It.IsAny<CloseReadinessScopeDto>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Private provider failure details"));
        var actual = await new FinancialOperationsCommandCenterReadService(workflows.Object,
            intercompanyAdvisories: source.Object).GetCommandCenterAsync();

        AssertSameDecision(baseline, actual);
        Assert.Equal("Unavailable", actual.CloseReadiness!.IntercompanyAdvisories!.Status);
        Assert.False(actual.CloseReadiness.IntercompanyAdvisories.IsComplete);
        Assert.DoesNotContain("Private provider", actual.CloseReadiness.IntercompanyAdvisories.Findings.Single().Message);
        workflows.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdvisoryCancellationPropagatesRatherThanReturningSuccessfulCloseResponse()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Mock<IIntercompanyCloseAdvisorySource>();
        source.Setup(x => x.GetAsync(It.IsAny<CloseReadinessScopeDto>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).Returns((CloseReadinessScopeDto _, string? _, string? _, CancellationToken ct) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<IntercompanyObligationMonitorDto>(ct);
            });
        var service = new FinancialOperationsCommandCenterReadService(new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict).Object,
            intercompanyAdvisories: source.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetCommandCenterAsync(ct: cancellation.Token));
    }

    private static void AssertSameDecision(FinancialOperationsCommandCenterDto baseline, FinancialOperationsCommandCenterDto actual)
    {
        Assert.Equal(baseline.IsReadyToComplete, actual.IsReadyToComplete);
        Assert.Equal(baseline.CloseReadiness!.Status, actual.CloseReadiness!.Status);
        Assert.Equal(baseline.CloseReadiness.IsComplete, actual.CloseReadiness.IsComplete);
        Assert.Equal(baseline.CloseReadiness.IsReadyToClose, actual.CloseReadiness.IsReadyToClose);
        Assert.Equal(baseline.CloseReadiness.Blockers.Select(x => (x.Code, x.ContributorId, x.Message)),
            actual.CloseReadiness.Blockers.Select(x => (x.Code, x.ContributorId, x.Message)));
        Assert.Equal(baseline.QueueRows, actual.QueueRows);
        Assert.False(actual.CloseReadiness.IsReadyToClose);
    }
}
