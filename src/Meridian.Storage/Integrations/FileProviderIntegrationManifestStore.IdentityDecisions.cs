using System.Text.Json;
using Meridian.Contracts.Integrations;

namespace Meridian.Storage.Integrations;

public sealed partial class FileProviderIntegrationManifestStore
{
    public async Task SaveIdentityDecisionAsync(ProviderIntegrationIdentityDecisionDto decision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(decision.SyncRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decision.DecisionId);
        var typeInfo = ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationIdentityDecisionDto;
        var snapshot = JsonSerializer.SerializeToElement(decision, typeInfo);
        await using var entityLock = await AcquireEntityLockAsync(
            "evidence-locks", $"identity:{decision.SyncRunId}:{decision.DecisionId}", ct).ConfigureAwait(false);
        var path = GetSyncRunScopedPath("identity-decisions", decision.SyncRunId, decision.DecisionId);
        var existing = await ReadAsync(path, typeInfo, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(existing, typeInfo), snapshot))
            {
                throw new InvalidOperationException("A retained identity decision cannot be changed.");
            }
            return;
        }
        await WriteAsync(path, decision, typeInfo, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ProviderIntegrationIdentityDecisionDto>> ListIdentityDecisionsAsync(string syncRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRunId);
        return ListAsync(GetSyncRunScopedDirectory("identity-decisions", syncRunId),
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationIdentityDecisionDto, ct);
    }
}
