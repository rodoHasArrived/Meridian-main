using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>Desktop queue reads the server's retained occurrences with authenticated scope.</summary>
public sealed class WorkstationRecurringJournalQueueClient(ApiClientService apiClient) : IRecurringJournalQueueSource
{
    public async Task<RecurringJournalQueueDto> GetQueueAsync(
        string fundProfileId,
        Guid ledgerBookId,
        string entityId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("A ledger book is required.", nameof(ledgerBookId));

        // Tenant and company are deliberately resolved by the authenticated server session.
        var route = UiApiRoutes.LedgerJournalAutomationRecurringOccurrences
            + $"?fundProfileId={Uri.EscapeDataString(fundProfileId)}&ledgerBookId={ledgerBookId:D}&entityId={Uri.EscapeDataString(entityId)}";
        var response = await apiClient.GetWithResponseAsync<RecurringJournalQueueDto>(route, ct).ConfigureAwait(false);
        if (!response.Success || response.Data is not { } result)
            throw new InvalidOperationException(response.ErrorMessage ?? "Recurring journal queue is unavailable.");

        if (result.FundProfileId != fundProfileId || result.LedgerBookId != ledgerBookId || result.EntityId != entityId
            || result.Occurrences is null || result.Occurrences.Any(row =>
                row.FundProfileId != fundProfileId || row.LedgerBookId != ledgerBookId || row.EntityId != entityId))
            throw new InvalidOperationException("Recurring journal queue does not match the selected fund, book and entity.");

        return result;
    }
}
