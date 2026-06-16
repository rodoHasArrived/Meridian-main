using System.Collections.Concurrent;
using Meridian.Application.Composition;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class InMemoryOperatorInboxService : INonProductionOnlyService, IOperatorInboxService
{
    private readonly ConcurrentDictionary<string, OperatorWorkItemDto> _items =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<OperatorWorkItemDto>> GetItemsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<OperatorWorkItemDto> items = _items.Values
            .OrderByDescending(static item => item.Tone)
            .ThenByDescending(static item => item.CreatedAt)
            .ThenBy(static item => item.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(items);
    }

    public Task UpsertItemAsync(OperatorWorkItemDto item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.WorkItemId);

        _items[item.WorkItemId] = item;
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string workItemId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        _items.TryRemove(workItemId, out _);
        return Task.CompletedTask;
    }
}
