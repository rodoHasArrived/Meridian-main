using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application;

public sealed class OperationsCloseChecklistHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LegacyEventsKeepTheirExactCanonicalHashBytes()
    {
        var workflow = Workflow();
        var audit = OperationsWorkflowAuditHashing.Create(Draft(workflow), null, Now);
        Assert.Null(audit.CloseChecklistSnapshot);
        var json = JsonSerializer.Serialize(new
        {
            audit.AuditId, audit.OccurredAtUtc, audit.WorkflowId, audit.FundAccountId, audit.PeriodId,
            audit.EventType, audit.FromState, audit.ToState, audit.Gate, audit.FromGateStatus, audit.ToGateStatus,
            audit.Actor, audit.Rationale, audit.CorrelationId, audit.CorrelationKeys, audit.References, audit.PreviousHash
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(), audit.CurrentHash);
        Assert.True(OperationsWorkflowAuditHashing.TryValidateChain([audit], out _, out _));
    }

    [Fact]
    public void NewSnapshotEventsExtendLegacyChainsAndDetectSnapshotMutationOrStripping()
    {
        var workflow = Workflow();
        var legacy = OperationsWorkflowAuditHashing.Create(Draft(workflow), null, Now);
        var captured = OperationsWorkflowAuditHashing.Create(OperationsWorkflowChecklistSnapshots.ForCommit(Draft(workflow), workflow),
            legacy.CurrentHash, Now.AddMinutes(1));
        Assert.True(OperationsWorkflowAuditHashing.TryValidateChain([legacy, captured], out _, out _));
        var snapshot = captured.CloseChecklistSnapshot!;
        var tampered = captured with
        {
            CloseChecklistSnapshot = snapshot with
            {
                Gates = snapshot.Gates.Select((gate, index) => index == 0 ? gate with { Status = OperationsGateStatusDto.Passed } : gate).ToArray()
            }
        };
        Assert.False(OperationsWorkflowAuditHashing.TryValidateChain([legacy, tampered], out _, out _));
        Assert.False(OperationsWorkflowAuditHashing.TryValidateChain([legacy, captured with { CloseChecklistSnapshot = null }], out _, out _));
        Assert.False(OperationsWorkflowAuditHashing.TryValidateChain([legacy, captured with
        {
            CloseChecklistSnapshot = snapshot with { SchemaVersion = 999 }
        }], out _, out _));
        Assert.False(OperationsWorkflowAuditHashing.TryValidateChain([legacy, captured with
        {
            CloseChecklistSnapshot = snapshot with { Gates = [null!, .. snapshot.Gates.Skip(1)] }
        }], out _, out _));
    }

    [Fact]
    public void LegacyMissingSnapshotsStayGapsAndDoNotAcquireInventedHistoricalCounts()
    {
        var workflow = Workflow();
        var first = OperationsWorkflowAuditHashing.Create(OperationsWorkflowChecklistSnapshots.ForCommit(Draft(workflow), workflow), null, Now);
        var gap = OperationsWorkflowAuditHashing.Create(Draft(workflow), first.CurrentHash, Now.AddMinutes(1));
        workflow.Version++;
        workflow.BrokerIngestGate = workflow.BrokerIngestGate with { Status = OperationsGateStatusDto.Passed };
        var last = OperationsWorkflowAuditHashing.Create(OperationsWorkflowChecklistSnapshots.ForCommit(Draft(workflow), workflow), gap.CurrentHash, Now.AddMinutes(2));
        var history = OperationsCloseChecklistHistory.Build(workflow.WorkflowId, [Timeline(last), Timeline(first), Timeline(gap)], Now.AddHours(1));
        Assert.Equal("Ready", history.Status);
        Assert.True(history.IsPartial);
        Assert.Equal(gap.AuditId, Assert.Single(history.MissingAuditIds));
        Assert.Equal(2, history.Observations.Count);
        Assert.Equal(5, history.Observations[0].RemainingTasks);
        Assert.Equal(4, history.Observations[1].RemainingTasks);
        Assert.NotEqual(history.Observations[0].Segment, history.Observations[1].Segment);
        Assert.Equal(Now, history.CoverageStart);
    }

    [Fact]
    public void LegacyOnlyHistoryRemainsUnavailableAndCannotDrawABurndown()
    {
        var workflow = Workflow();
        var legacy = OperationsWorkflowAuditHashing.Create(Draft(workflow), null, Now);
        var history = OperationsCloseChecklistHistory.Build(workflow.WorkflowId, [Timeline(legacy)], Now.AddHours(1));
        Assert.Equal("Unavailable", history.Status);
        Assert.Empty(history.Observations);
        Assert.Null(history.CoverageStart);
    }

    [Fact]
    public void MissingPersistedWorkflowVersionsStartANewCoverageSegment()
    {
        var workflow = Workflow();
        var first = OperationsWorkflowAuditHashing.Create(OperationsWorkflowChecklistSnapshots.ForCommit(Draft(workflow), workflow), null, Now);
        workflow.Version = 3;
        var last = OperationsWorkflowAuditHashing.Create(OperationsWorkflowChecklistSnapshots.ForCommit(Draft(workflow), workflow), first.CurrentHash, Now.AddMinutes(1));
        var history = OperationsCloseChecklistHistory.Build(workflow.WorkflowId, [Timeline(first), Timeline(last)], Now.AddHours(1));
        Assert.True(history.IsPartial);
        Assert.Empty(history.MissingAuditIds);
        var gap = Assert.Single(history.VersionGaps);
        Assert.Equal(1, gap.AfterVersion);
        Assert.Equal(4, gap.BeforeVersion);
        Assert.NotEqual(history.Observations[0].Segment, history.Observations[1].Segment);
    }

    [Fact]
    public async Task FileCommitRetainsSnapshotWithTheExactPersistedWorkflowVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-close-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workflow = Workflow();
            var repository = new FileOperationsContinuityRepository(root, new OperationsStatusDerivationService(),
                NullLogger<FileOperationsContinuityRepository>.Instance);
            var committed = await repository.CommitWorkflowStartAsync(workflow, Draft(workflow));
            var persisted = await repository.GetAsync(workflow.WorkflowId);
            var auditStore = new FileOperationsWorkflowAuditStore(root, NullLogger<FileOperationsWorkflowAuditStore>.Instance);
            var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
            var captured = Assert.Single(timeline).CloseChecklistSnapshot;
            Assert.NotNull(captured);
            Assert.Equal(persisted!.Version, captured.WorkflowVersion);
            Assert.Equal(committed.Workflow.Version, captured.WorkflowVersion);
            Assert.Equal(5, captured.Gates.Count);
            Assert.True(OperationsWorkflowAuditHashing.TryValidateChain(timeline, out _, out _));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ForeignOrTamperedHistoryIsRejectedBeforeProjectingCounts()
    {
        var workflow = Workflow();
        var audit = OperationsWorkflowAuditHashing.Create(OperationsWorkflowChecklistSnapshots.ForCommit(Draft(workflow), workflow), null, Now);
        Assert.Throws<InvalidDataException>(() => OperationsCloseChecklistHistory.Build(Guid.NewGuid(), [Timeline(audit)], Now.AddHours(1)));
        Assert.Throws<InvalidDataException>(() => OperationsCloseChecklistHistory.Build(workflow.WorkflowId,
            [Timeline(audit) with { Rationale = "Changed without updating canonical hash" }], Now.AddHours(1)));
    }

    private static OperationsContinuityWorkflow Workflow()
        => OperationsContinuityWorkflow.Start(Guid.NewGuid(), Guid.NewGuid(), "2026-11", null, "synthetic", Now);
    private static OperationsWorkflowAuditDraft Draft(OperationsContinuityWorkflow workflow)
        => new(workflow.WorkflowId, workflow.FundAccountId, workflow.PeriodId, "workflow-started",
            OperationsWorkflowStatusDto.NotStarted, OperationsWorkflowStatusDto.NotStarted, null, null, null,
            "test-operator", "Retain actual committed posture", "synthetic-close-history", []);
    private static OperationsTimelineEntryDto Timeline(OperationsWorkflowAuditDto audit)
        => new(audit.AuditId, audit.OccurredAtUtc, audit.WorkflowId, audit.FundAccountId, audit.PeriodId,
            audit.EventType, audit.FromState, audit.ToState, audit.Gate, audit.FromGateStatus, audit.ToGateStatus,
            audit.Actor, audit.Rationale, audit.CorrelationId, audit.CorrelationKeys, audit.References,
            audit.PreviousHash, audit.CurrentHash, audit.Outcome) { CloseChecklistSnapshot = audit.CloseChecklistSnapshot };
}
