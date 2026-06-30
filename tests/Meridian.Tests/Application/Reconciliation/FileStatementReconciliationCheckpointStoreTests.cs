using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class FileStatementReconciliationCheckpointStoreTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-recon-cp-{Guid.NewGuid():N}");

    [Fact]
    public async Task UpsertAsync_PersistsCheckpointAcrossNewInstance()
    {
        var accountId = Guid.NewGuid();
        var checkpoint = BuildCheckpoint(accountId, StatementReconciliationStage.Import, importedRowCount: 42);

        var first = CreateStore();
        await first.UpsertAsync(checkpoint, CancellationToken.None);

        var reopened = CreateStore();
        var loaded = await reopened.GetAsync(accountId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(StatementReconciliationStage.Import, loaded!.LastCompletedStage);
        Assert.Equal(42, loaded.ImportedRowCount);
    }

    [Fact]
    public async Task UpsertAsync_OverwritesCheckpointForSameAccount()
    {
        var accountId = Guid.NewGuid();
        var store = CreateStore();
        await store.UpsertAsync(BuildCheckpoint(accountId, StatementReconciliationStage.Validate, importedRowCount: 0), CancellationToken.None);
        await store.UpsertAsync(BuildCheckpoint(accountId, StatementReconciliationStage.Reconcile, importedRowCount: 100), CancellationToken.None);

        var reopened = CreateStore();
        var loaded = await reopened.GetAsync(accountId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(StatementReconciliationStage.Reconcile, loaded!.LastCompletedStage);
        Assert.Equal(100, loaded.ImportedRowCount);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForUnknownAccount()
    {
        var store = CreateStore();

        var loaded = await store.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(loaded);
    }

    private FileStatementReconciliationCheckpointStore CreateStore() => new(_dataRoot);

    private static StatementReconciliationCheckpoint BuildCheckpoint(
        Guid accountId,
        StatementReconciliationStage lastCompletedStage,
        int importedRowCount)
        => new(
            AccountId: accountId,
            SourceKind: "local",
            SourcePath: "/tmp/statement.csv",
            LastCompletedStage: lastCompletedStage,
            CurrentStage: StatementReconciliationStage.Reconcile,
            Status: "Running",
            LastError: null,
            ImportId: "import-1",
            ImportedRowCount: importedRowCount,
            MatchCount: 0,
            UnresolvedCount: 0,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
