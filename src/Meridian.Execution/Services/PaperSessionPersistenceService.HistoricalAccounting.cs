using System.Text.Json;
using Meridian.Execution.Events;
using Meridian.Execution.Serialization;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

public sealed partial class PaperSessionPersistenceService
{
    private bool HasHistoricalAccountingScope(PaperSession session) =>
        session.AccountingContext is not null && session.AccountingContext != _financing?.PostingContext;

    private void EnsureCurrentAccountingScope(PaperSession session)
    {
        if (HasHistoricalAccountingScope(session))
            throw new InvalidOperationException("The paper session retains a historical accounting scope and is read-only; create a session in the current scope.");
        if (session.AccountingUnavailableReason is not null)
            throw new InvalidOperationException(session.AccountingUnavailableReason);
    }

    private async Task<(IReadOnlyList<PaperFinancingCharge> Charges, string? UnavailableReason)> LoadSessionFinancingAsync(
        TradeFillLedgerPostingContext? context, string sessionId, CancellationToken ct)
    {
        if (context is null || context == _financing?.PostingContext)
            return (_financing is null ? [] : await _financing.LoadAndRecoverAsync(sessionId, ct).ConfigureAwait(false), null);
        if (_financing is null)
            return ([], "Historical financing coverage is unavailable because its accounting owner is not configured.");
        try
        {
            return (await _financing.ReadHistoricalSourcesAsync(context, sessionId, ct).ConfigureAwait(false), null);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            // Isolate unavailable evidence to this session. Never interpret a missing,
            // conflicting, active, or pending old store as an empty financing history.
            return ([], "Historical financing coverage is unavailable or pending; original-scope recovery is required.");
        }
    }

    private enum FinancingProjectionProof { Exact, Missing, Conflict }

    private static FinancingProjectionProof InspectFinancingProjection(
        PaperTradingPortfolio portfolio, IReadOnlyList<PaperFinancingCharge> charges,
        IReadOnlyList<PersistedJournalEntryDto> retainedJournal, string sessionId)
    {
        var expectedIds = charges.Select(charge => PaperFinancingCharge.Identity(
            "projection-journal", charge.SourceEventId.ToString("D"))).ToHashSet();
        if (portfolio.Ledger is not { } ledger)
            return FinancingProjectionProof.Conflict;
        var expected = SerializeLedgerJournal(ledger, sessionId)
            .Where(entry => expectedIds.Contains(entry.JournalEntryId))
            .ToDictionary(static entry => entry.JournalEntryId);
        if (expected.Count != expectedIds.Count)
            return FinancingProjectionProof.Conflict;
        var found = new HashSet<Guid>();
        var missingLegacyMetadata = false;
        foreach (var retained in retainedJournal)
        {
            if (!IsFinancingProjection(retained, expectedIds))
                continue;
            // Reverse membership prevents a dropped source from disappearing when a
            // later owner refreshes this rebuildable cache. Never erase an extra charge.
            if (!found.Add(retained.JournalEntryId)
                || !expected.TryGetValue(retained.JournalEntryId, out var entry)
                || retained.Lines is null || !retained.Lines.SequenceEqual(entry.Lines)
                || (retained with { Lines = entry.Lines, Metadata = entry.Metadata }) != entry
                || entry.Metadata is null)
                return FinancingProjectionProof.Conflict;
            if (retained.Metadata is null)
            {
                // The pre-metadata serializer retained all scalar/line facts above.
                // Only the current owner, after canonical confirmation, can refresh it.
                missingLegacyMetadata = true;
            }
            else if (!JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(retained.Metadata, ExecutionJsonContext.Default.JournalEntryMetadata),
                JsonSerializer.SerializeToElement(entry.Metadata, ExecutionJsonContext.Default.JournalEntryMetadata)))
                return FinancingProjectionProof.Conflict;
        }
        return found.Count == expected.Count && !missingLegacyMetadata
            ? FinancingProjectionProof.Exact : FinancingProjectionProof.Missing;
    }

    private static bool IsFinancingProjection(PersistedJournalEntryDto entry, HashSet<Guid> expectedIds) =>
        expectedIds.Contains(entry.JournalEntryId)
        || string.Equals(entry.ActivityType, "margin-interest", StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Metadata?.ActivityType, "margin-interest", StringComparison.OrdinalIgnoreCase)
        || entry.Metadata?.IdempotencyKey?.StartsWith("paper-financing:", StringComparison.Ordinal) == true
        || entry.Lines?.Any(line => string.Equals(line?.Account?.Name,
            LedgerAccounts.MarginInterestExpense.Name, StringComparison.OrdinalIgnoreCase)) == true;
}
