using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface ISecurityMasterOperatorWorkflowClient
{
    Task<SecurityMasterIngestStatusResponse?> GetIngestStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct = default);

    Task<SecurityMasterConflict?> ResolveConflictAsync(
        Guid conflictId,
        string resolution,
        string resolvedBy,
        string? reason,
        CancellationToken ct = default);
}

public sealed class SecurityMasterOperatorWorkflowClient : ISecurityMasterOperatorWorkflowClient
{
    public async Task<SecurityMasterIngestStatusResponse?> GetIngestStatusAsync(CancellationToken ct = default)
        => await ApiClientService.Instance
            .GetAsync<SecurityMasterIngestStatusResponse>("/api/security-master/ingest/status", ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct = default)
        => await ApiClientService.Instance
            .GetAsync<SecurityMasterConflict[]>("/api/security-master/conflicts", ct)
            .ConfigureAwait(false)
            ?? [];

    public async Task<SecurityMasterConflict?> ResolveConflictAsync(
        Guid conflictId,
        string resolution,
        string resolvedBy,
        string? reason,
        CancellationToken ct = default)
    {
        var request = new ResolveConflictRequest(
            ConflictId: conflictId,
            Resolution: resolution,
            ResolvedBy: resolvedBy,
            Reason: reason);

        return await ApiClientService.Instance
            .PostAsync<SecurityMasterConflict>(
                $"/api/security-master/conflicts/{conflictId}/resolve",
                request,
                ct)
            .ConfigureAwait(false);
    }
}
