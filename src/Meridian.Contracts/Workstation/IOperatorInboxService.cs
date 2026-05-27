namespace Meridian.Contracts.Workstation;

public interface IOperatorInboxService
{
    Task<IReadOnlyList<OperatorWorkItemDto>> GetItemsAsync(CancellationToken ct = default);

    Task UpsertItemAsync(OperatorWorkItemDto item, CancellationToken ct = default);

    Task RemoveItemAsync(string workItemId, CancellationToken ct = default);
}
