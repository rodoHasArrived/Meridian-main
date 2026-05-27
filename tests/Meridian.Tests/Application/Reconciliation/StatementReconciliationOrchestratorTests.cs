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
}
