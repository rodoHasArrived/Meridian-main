using Meridian.Application.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class StatementReconciliationOrchestratorTests
{
    [Fact]
    public async Task RunAsync_Completes_All_Stages_And_Persists_Checkpoint()
    {
        var store = new InMemoryStatementReconciliationCheckpointStore();
        var service = new StatementReconciliationService();
        var orchestrator = new StatementReconciliationOrchestrator(service, store);

        var path = Path.GetTempFileName();
        await File.WriteAllLinesAsync(path, ["a,b", "1,2"]);

        try
        {
            var result = await orchestrator.RunAsync(Guid.NewGuid(), "local", path, resume: false, CancellationToken.None);
            Assert.Equal(StatementReconciliationStage.Completed, result.CurrentStage);
            Assert.Equal("Completed", result.Status);
            Assert.True(result.ImportedRowCount > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RunAsync_Records_Failure_For_Missing_File()
    {
        var store = new InMemoryStatementReconciliationCheckpointStore();
        var orchestrator = new StatementReconciliationOrchestrator(new StatementReconciliationService(), store);

        var result = await orchestrator.RunAsync(Guid.NewGuid(), "local", "./missing.csv", resume: false, CancellationToken.None);

        Assert.Equal(StatementReconciliationStage.Failed, result.CurrentStage);
        Assert.Equal("Failed", result.Status);
        Assert.NotNull(result.LastError);
    }

    [Fact]
    public async Task RunAsync_Creates_Cases_From_Broker_Statement_Intake()
    {
        var store = new InMemoryStatementReconciliationCheckpointStore();
        var service = new StatementReconciliationService();
        var orchestrator = new StatementReconciliationOrchestrator(service, store);

        var path = Path.GetTempFileName();
        await File.WriteAllLinesAsync(path,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "A1,SPY,10,500,0,position,2026-05-29",
            "A1,SPY,0,0,125.25,dividend,2026-05-30"
        ]);

        try
        {
            var result = await orchestrator.RunAsync(Guid.NewGuid(), "broker", path, resume: false, CancellationToken.None);
            var intake = await service.CreateExternalStatementCasesAsync("broker", path);

            Assert.Equal(StatementReconciliationStage.Completed, result.CurrentStage);
            Assert.Equal(2, result.ImportedRowCount);
            Assert.Equal(1, result.MatchCount);
            Assert.Equal(1, result.UnresolvedCount);
            Assert.Single(intake.Cases);
            Assert.Equal("fund-ops", intake.Cases[0].Owner);
            Assert.NotNull(intake.Cases[0].DueAtUtc);
            Assert.Contains(intake.Cases[0].AuditEvents, e => e.EventType == "ExternalStatementCaseCreated");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RunAsync_Broker_Intake_Recovers_After_Blocker_Is_Corrected()
    {
        var accountId = Guid.NewGuid();
        var store = new InMemoryStatementReconciliationCheckpointStore();
        var service = new StatementReconciliationService();
        var orchestrator = new StatementReconciliationOrchestrator(service, store);

        var path = Path.GetTempFileName();
        await File.WriteAllLinesAsync(path, ["foo,bar", "1,2"]);

        try
        {
            var failed = await orchestrator.RunAsync(accountId, "broker", path, resume: false, CancellationToken.None);
            Assert.Equal(StatementReconciliationStage.Failed, failed.CurrentStage);
            Assert.Contains("canonical external statement header", failed.LastError);

            await File.WriteAllLinesAsync(path,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "A1,QQQ,5,400,0,position,2026-05-29"
            ]);

            var recovered = await orchestrator.RunAsync(accountId, "broker", path, resume: false, CancellationToken.None);

            Assert.Equal(StatementReconciliationStage.Completed, recovered.CurrentStage);
            Assert.Equal("Completed", recovered.Status);
            Assert.Equal(1, recovered.ImportedRowCount);
            Assert.Equal(1, recovered.MatchCount);
            Assert.Equal(0, recovered.UnresolvedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
