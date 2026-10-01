using System.Collections.ObjectModel;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels.Accounting;

/// <summary>Read-only presentation: claiming, approval and reopening remain server governed.</summary>
public sealed class RecurringJournalQueueViewModel(IRecurringJournalQueueSource? source) : BindableBase
{
    private long _generation;
    private string _statusText = "Select a fund, ledger book and entity to load recurring occurrences.";

    public ObservableCollection<RecurringJournalQueueRow> Rows { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public async Task LoadAsync(string? fundProfileId, Guid? ledgerBookId, string? entityId, CancellationToken ct = default)
    {
        var generation = Interlocked.Increment(ref _generation);
        Publish(generation, () => Rows.Clear());
        if (string.IsNullOrWhiteSpace(fundProfileId) || !ledgerBookId.HasValue || ledgerBookId == Guid.Empty
            || string.IsNullOrWhiteSpace(entityId))
        {
            Publish(generation, () => StatusText = "Select a fund, ledger book and entity to load recurring occurrences.");
            return;
        }
        if (source is null)
        {
            Publish(generation, () => StatusText = "Recurring queue unavailable: the shared queue service is not registered.");
            return;
        }

        Publish(generation, () => StatusText = "Loading retained recurring occurrences…");
        try
        {
            var queue = await source.GetQueueAsync(fundProfileId, ledgerBookId.Value, entityId, ct).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _generation))
                return;
            if (queue.FundProfileId != fundProfileId || queue.LedgerBookId != ledgerBookId || queue.EntityId != entityId
                || queue.Occurrences.Any(row => row.FundProfileId != fundProfileId || row.LedgerBookId != ledgerBookId || row.EntityId != entityId))
                throw new InvalidOperationException("The retained queue does not match the selected scope.");
            Publish(generation, () =>
            {
                Rows.ReplaceWith(queue.Occurrences.Select(row => new RecurringJournalQueueRow(row)));
                StatusText = Rows.Count == 0
                    ? "No retained recurring occurrences for this scope."
                    : $"{Rows.Count} retained occurrence(s). Generated drafts require human approval.";
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (generation != Volatile.Read(ref _generation))
                return;
            Publish(generation, () =>
            {
                Rows.Clear();
                StatusText = $"Recurring queue unavailable: {ex.Message}";
            });
        }
    }

    private void Publish(long generation, Action update)
    {
        void Apply()
        {
            if (generation == Volatile.Read(ref _generation))
                update();
        }
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(Apply);
        else
            Apply();
    }
}

public sealed record RecurringJournalQueueRow(RecurringJournalOccurrenceDto Occurrence)
{
    public string Definition => $"{Occurrence.ScheduleId} · v{Occurrence.ScheduleVersion}\nTemplate {Occurrence.TemplateId} · v{Occurrence.TemplateVersion}";
    public string EffectiveDate => $"{Occurrence.EffectiveDate:yyyy-MM-dd}\n{Occurrence.PeriodId ?? "Period unresolved"}";
    public string State => $"{Occurrence.State}\nApproval: {Occurrence.ApprovalStatus ?? "Not submitted"}";
    public string Draft => Occurrence.JournalEntryId?.ToString("D") ?? "No retained draft";
    public string Blockers => string.Join(Environment.NewLine, Occurrence.Blockers);
    public string PeriodLock => string.Join(Environment.NewLine,
        new[] { Occurrence.PeriodLockOwner is { } owner ? $"Owner: {owner}" : null,
            Occurrence.GovernedReopenPath is { } path ? $"Governed reopen: {path}" : null }.OfType<string>());
    public string Evidence => Occurrence.SourceEvidenceReferences.Count > 0
        ? string.Join(Environment.NewLine, Occurrence.SourceEvidenceReferences)
        : "No retained source references.";
}
