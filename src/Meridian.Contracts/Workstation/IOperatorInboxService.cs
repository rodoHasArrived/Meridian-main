namespace Meridian.Contracts.Workstation;

/// <summary>
/// Queries and maintains the operator's actionable work item inbox.
/// </summary>
public interface IOperatorInboxService
{
    Task<IReadOnlyList<OperatorWorkItemDto>> GetItemsAsync(CancellationToken ct = default);

    Task UpsertItemAsync(OperatorWorkItemDto item, CancellationToken ct = default);

    Task RemoveItemAsync(string workItemId, CancellationToken ct = default);
}
