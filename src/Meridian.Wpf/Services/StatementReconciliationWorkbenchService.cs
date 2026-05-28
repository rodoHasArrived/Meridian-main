using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public interface IStatementReconciliationWorkbenchService
{
    Task<StatementReconciliationWorkbenchSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public sealed class StatementReconciliationWorkbenchService : IStatementReconciliationWorkbenchService
{
    private readonly IWorkstationReconciliationApiClient _apiClient;

    public StatementReconciliationWorkbenchService(IWorkstationReconciliationApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<StatementReconciliationWorkbenchSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var runsTask = _apiClient.GetStatementRunsAsync(ct);
        var validationTask = _apiClient.GetStatementExceptionsAsync(ct);
        var breaksTask = _apiClient.GetOpenStatementBreaksAsync(ct);
        var casesTask = _apiClient.GetOpenReconciliationCasesAsync(ct);
        var queueTask = _apiClient.GetReconciliationQueueStatusAsync(ct);

        await Task.WhenAll(runsTask, validationTask, breaksTask, casesTask, queueTask).ConfigureAwait(false);

        return new StatementReconciliationWorkbenchSnapshot(
            await runsTask.ConfigureAwait(false),
            await validationTask.ConfigureAwait(false),
            await breaksTask.ConfigureAwait(false),
            await casesTask.ConfigureAwait(false),
            await queueTask.ConfigureAwait(false),
            DateTimeOffset.UtcNow);
    }
}

public sealed class NullStatementReconciliationWorkbenchService : IStatementReconciliationWorkbenchService
{
    public Task<StatementReconciliationWorkbenchSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => Task.FromResult(new StatementReconciliationWorkbenchSnapshot([], [], [], [], [], DateTimeOffset.UtcNow));
}
