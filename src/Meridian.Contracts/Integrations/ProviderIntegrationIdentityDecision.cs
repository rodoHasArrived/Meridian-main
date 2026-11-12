namespace Meridian.Contracts.Integrations;

/// <summary>A retained operator choice bound to the exact identifier matching evidence.</summary>
public sealed record ProviderIntegrationIdentityDecisionDto(
    string DecisionId,
    string ConnectionId,
    string SyncRunId,
    string StagingRecordId,
    Guid SelectedSecurityId,
    string EvidenceHash,
    string ReviewedBy,
    DateTimeOffset ReviewedAt,
    string Reason);

public sealed record ProviderIntegrationIdentityResolutionRequestDto(
    string SyncRunId,
    string StagingRecordId,
    Guid SelectedSecurityId,
    string EvidenceHash,
    string Reason);

/// <summary>Append-only identity decisions; staged source records are never rewritten.</summary>
public interface IProviderIntegrationIdentityDecisionStore
{
    Task SaveIdentityDecisionAsync(ProviderIntegrationIdentityDecisionDto decision, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderIntegrationIdentityDecisionDto>> ListIdentityDecisionsAsync(string syncRunId, CancellationToken ct = default);
}
