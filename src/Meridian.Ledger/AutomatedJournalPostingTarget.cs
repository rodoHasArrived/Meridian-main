namespace Meridian.Ledger;

/// <summary>
/// Shared posting target for approved automated journal projections.
/// </summary>
public interface IAutomatedJournalPostingTarget
{
    Task<AutomatedJournalApproval> PostAsync(
        AutomatedJournalApproval approval,
        AutomatedJournalPostingContext context,
        CancellationToken ct = default);
}

public sealed record AutomatedJournalPostingContext
{
    public AutomatedJournalPostingContext(
        Guid periodId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string> evidenceLinks,
        Guid? aggregateId = null)
    {
        if (periodId == Guid.Empty)
        {
            throw new ArgumentException("Period id is required.", nameof(periodId));
        }

        PeriodId = periodId;
        Actor = NormalizeRequired(actor, nameof(actor));
        OccurredAtUtc = occurredAtUtc;
        Reason = NormalizeRequired(reason, nameof(reason));
        EvidenceLinks = NormalizeEvidence(evidenceLinks);
        AggregateId = aggregateId;
    }

    public Guid PeriodId { get; init; }

    public string Actor { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public string Reason { get; init; }

    public IReadOnlyList<string> EvidenceLinks { get; init; }

    public Guid? AggregateId { get; init; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static IReadOnlyList<string> NormalizeEvidence(IReadOnlyList<string> evidenceLinks)
    {
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        var normalized = evidenceLinks
            .Select(static link => link.Trim())
            .Where(static link => link.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Posting evidence is required.", nameof(evidenceLinks));
        }

        return normalized;
    }
}

public sealed class InMemoryAutomatedJournalPostingTarget : IAutomatedJournalPostingTarget
{
    public InMemoryAutomatedJournalPostingTarget(Ledger ledger)
    {
        Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public Ledger Ledger { get; }

    public Task<AutomatedJournalApproval> PostAsync(
        AutomatedJournalApproval approval,
        AutomatedJournalPostingContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(context);

        return Task.FromResult(approval.PostTo(
            Ledger,
            context.Actor,
            context.OccurredAtUtc,
            context.Reason,
            context.EvidenceLinks));
    }
}
