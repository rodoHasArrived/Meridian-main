using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Tests.Application;

public sealed class ReconciliationGovernanceServiceTests
{
    [Fact]
    public async Task EvaluateGateAsync_Blocks_WhenThresholdBreachedWithoutWaiver()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-1", "run-1", DateTimeOffset.UtcNow, null, null, 0, 1, 1, false, 0.01m, 5),
            [],
            [new ReconciliationBreakDto("b1", "Mismatch", ReconciliationBreakCategory.AmountMismatch, ReconciliationBreakStatus.Open, "", 1m, 2m, 1m, ReconciliationBreakSeverity.Critical, "x", null, null)]);
        await repo.SaveAsync(detail);

        var sut = new ReconciliationGovernanceService(repo);
        var result = await sut.EvaluateGateAsync("run-1", new ReconciliationPolicyThresholds(MaxOpenBreakCount: 0, MaxCriticalOpenBreakCount: 0, MaxAbsoluteVariance: 0.1m), false, false);

        Assert.Equal(TradingAcceptanceGateStatusDto.Blocked, result.Status);
    }

    [Fact]
    public async Task EvaluateGateAsync_ReviewRequired_WhenBreachedAndSecondaryApprovalSigned()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-2", "run-2", DateTimeOffset.UtcNow, null, null, 0, 1, 1, false, 0.01m, 5),
            [],
            [new ReconciliationBreakDto("b1", "Mismatch", ReconciliationBreakCategory.AmountMismatch, ReconciliationBreakStatus.Open, "", 1m, 2m, 1m, ReconciliationBreakSeverity.High, "x", null, null)]);
        await repo.SaveAsync(detail);

        var sut = new ReconciliationGovernanceService(repo);
        var result = await sut.EvaluateGateAsync("run-2", new ReconciliationPolicyThresholds(MaxOpenBreakCount: 0, MaxAbsoluteVariance: 0.1m), true, true);

        Assert.Equal(TradingAcceptanceGateStatusDto.ReviewRequired, result.Status);
        Assert.True(result.SecondaryApprovalRequired);
    }

    [Fact]
    public async Task EvaluateGateAsync_DoesNotWriteAudit_WhenWriteAuditDisabled()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-3", "run-3", DateTimeOffset.UtcNow, null, null, 0, 1, 0, false, 0.01m, 1),
            [],
            []);
        await repo.SaveAsync(detail);

        var auditStore = new TestReconciliationGovernanceAuditStore();
        var sut = new ReconciliationGovernanceService(repo, auditStore);

        _ = await sut.EvaluateGateAsync("run-3", new ReconciliationPolicyThresholds(), waiverRequested: false, secondaryApprovalSigned: false, writeAudit: false);

        Assert.Equal(0, auditStore.AppendCount);
    }

    [Fact]
    public async Task EvaluateGateAsync_WritesAudit_ByDefault()
    {
        var repo = new InMemoryReconciliationRunRepository();
        var detail = new ReconciliationRunDetail(
            new ReconciliationRunSummary("rec-4", "run-4", DateTimeOffset.UtcNow, null, null, 0, 1, 0, false, 0.01m, 1),
            [],
            []);
        await repo.SaveAsync(detail);

        var auditStore = new TestReconciliationGovernanceAuditStore();
        var sut = new ReconciliationGovernanceService(repo, auditStore);

        _ = await sut.EvaluateGateAsync("run-4", new ReconciliationPolicyThresholds(), waiverRequested: false, secondaryApprovalSigned: false);

        Assert.Equal(1, auditStore.AppendCount);
    }

    private sealed class TestReconciliationGovernanceAuditStore : IReconciliationGovernanceAuditStore
    {
        public int AppendCount { get; private set; }

        public Task AppendAsync(ReconciliationGateEvaluation evaluation, CancellationToken ct = default)
        {
            AppendCount++;
            return Task.CompletedTask;
        }
    }

}
