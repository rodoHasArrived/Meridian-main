using Meridian.Ledger;
using Npgsql;
using static Meridian.Contracts.Text.TextPrimitives;
using static Meridian.Storage.Ledger.LedgerRetainedValueComparison;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Durable posting seam used after a governed workflow has approved a journal write.
/// The workflow remains responsible for validation and approval; this target owns the
/// idempotent handoff to the authoritative journal store.
/// </summary>
public interface IGovernedLedgerPostingTarget
{
    Task<GovernedLedgerPostingResult> PostAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default);
}

public sealed record GovernedLedgerPostingResult(
    Guid JournalEntryId,
    bool WasAppended);

/// <summary>
/// Serializes the check-and-append handoff for one process and treats an equivalent
/// retained posting identity as a successful retry. Global journal/command collisions
/// and aggregate-scoped source/idempotency collisions with different accounting content
/// fail closed.
/// </summary>
public sealed class DurableLedgerPostingTarget : IGovernedLedgerPostingTarget, IDisposable
{
    private readonly ILedgerJournalStore _store;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public DurableLedgerPostingTarget(ILedgerJournalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<GovernedLedgerPostingResult> PostAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Entry);
        write = NormalizeWrite(AccountingPostingCommandValidator.NormalizeAndValidate(write));

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var identity = LedgerPostingIdentity.FromWrite(write);
            var collisions = await _store
                .FindPostingIdentityCollisionsAsync(identity, ct)
                .ConfigureAwait(false);
            if (collisions.Count > 0)
            {
                return ResolveRetainedCollision(collisions, write);
            }

            try
            {
                await _store.AppendAsync(write, ct).ConfigureAwait(false);
            }
            catch (PostgresException exception)
                when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                collisions = await _store
                    .FindPostingIdentityCollisionsAsync(identity, ct)
                    .ConfigureAwait(false);
                if (collisions.Count == 0)
                    throw;

                return ResolveRetainedCollision(collisions, write);
            }

            return new GovernedLedgerPostingResult(write.Entry.JournalEntryId, WasAppended: true);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose() => _writeGate.Dispose();

    private static GovernedLedgerPostingResult ResolveRetainedCollision(
        IReadOnlyList<LedgerJournalEntryRecord> collisions,
        LedgerJournalEntryWrite requested)
    {
        foreach (var collision in collisions)
        {
            EnsureEquivalent(collision, requested);
        }

        var retained = collisions
            .OrderBy(static record => record.GlobalSequence)
            .ThenBy(static record => record.CreatedAt)
            .ThenBy(static record => record.Entry.JournalEntryId)
            .First();
        return new GovernedLedgerPostingResult(retained.Entry.JournalEntryId, WasAppended: false);
    }

    private static void EnsureEquivalent(
        LedgerJournalEntryRecord existing,
        LedgerJournalEntryWrite requested)
    {
        var retained = existing.Entry;
        var candidate = requested.Entry;
        var equivalent = existing.AggregateId == requested.AggregateId
            && existing.PeriodId == requested.PeriodId
            && existing.CommandId == requested.CommandId
            && existing.CorrelationId == requested.CorrelationId
            && existing.AccountingBasis == requested.AccountingBasis
            && string.Equals(existing.AccountingPolicyId, requested.AccountingPolicyId, StringComparison.Ordinal)
            && string.Equals(existing.AccountingPolicyVersion, requested.AccountingPolicyVersion, StringComparison.Ordinal)
            && string.Equals(existing.RuleId, requested.RuleId, StringComparison.Ordinal)
            && string.Equals(existing.RuleVersion, requested.RuleVersion, StringComparison.Ordinal)
            && existing.SourceEventId == requested.SourceEventId
            && existing.SourceJournalEntryId == requested.SourceJournalEntryId
            && existing.PostingKind == requested.PostingKind
            && existing.AdjustmentApproval == requested.AdjustmentApproval
            && TimestampsMatch(retained.Timestamp, candidate.Timestamp)
            && string.Equals(retained.Description, candidate.Description, StringComparison.Ordinal)
            && MetadataEquivalent(retained.Metadata, candidate.Metadata)
            && JournalLinesEquivalent(retained.Lines, candidate.Lines);

        if (!equivalent)
        {
            throw new LedgerValidationException(
                $"Journal entry '{candidate.JournalEntryId}' is already retained with different accounting content.");
        }
    }

    private static bool JournalLinesEquivalent(
        IReadOnlyList<LedgerEntry> retained,
        IReadOnlyList<LedgerEntry> candidate)
    {
        if (retained.Count != candidate.Count)
            return false;

        var matched = new bool[candidate.Count];
        foreach (var retainedLine in retained)
        {
            var matchIndex = -1;
            for (var index = 0; index < candidate.Count; index++)
            {
                if (!matched[index] && LinesEquivalent(retainedLine, candidate[index]))
                {
                    matchIndex = index;
                    break;
                }
            }

            if (matchIndex < 0)
                return false;

            matched[matchIndex] = true;
        }

        return true;
    }

    private static bool LinesEquivalent(LedgerEntry retained, LedgerEntry candidate)
        => TimestampsMatch(retained.Timestamp, candidate.Timestamp)
           && retained.Account.AccountType == candidate.Account.AccountType
           && string.Equals(retained.Account.Name, candidate.Account.Name, StringComparison.Ordinal)
           && string.Equals(retained.Account.Symbol, candidate.Account.Symbol, StringComparison.OrdinalIgnoreCase)
           && string.Equals(
               retained.Account.FinancialAccountId,
               candidate.Account.FinancialAccountId,
               StringComparison.OrdinalIgnoreCase)
           && AmountsMatch(retained.Debit, candidate.Debit)
           && AmountsMatch(retained.Credit, candidate.Credit)
           && string.Equals(retained.Description, candidate.Description, StringComparison.Ordinal)
           && DimensionsEquivalent(retained.Dimensions, candidate.Dimensions)
           // Durable per leg, and previously left out of this comparison entirely. Debit and
           // credit are the functional amounts, so two legs can agree on every one of them while
           // booking a different transaction currency, amount, or FX rate. The amounts are known
           // equal by the time this runs, so either side's serve as the functional pair.
           && CurrencyMatches(retained.Currency, candidate.Currency, retained.Debit, retained.Credit);

    private static LedgerJournalEntryWrite NormalizeWrite(LedgerJournalEntryWrite write)
    {
        var metadata = write.Entry.Metadata;
        if (write.LedgerBookId is { } ledgerBookId && string.IsNullOrWhiteSpace(metadata.LedgerBook))
        {
            metadata = metadata with { LedgerBook = ledgerBookId.ToString("D") };
        }
        else if (write.LedgerBookId is { } requestedLedgerBookId &&
                 (!Guid.TryParse(metadata.LedgerBook, out var metadataLedgerBookId) ||
                  metadataLedgerBookId != requestedLedgerBookId))
        {
            throw new LedgerValidationException(
                $"Ledger write ledger book '{requestedLedgerBookId:D}' conflicts with journal metadata ledger book '{metadata.LedgerBook}'.");
        }

        var entry = ReferenceEquals(metadata, write.Entry.Metadata)
            ? write.Entry
            : new JournalEntry(
                write.Entry.JournalEntryId,
                write.Entry.Timestamp,
                write.Entry.Description,
                write.Entry.Lines,
                metadata);

        return write with
        {
            Entry = entry,
            AccountingPolicyId = RequireText(write.AccountingPolicyId, nameof(write.AccountingPolicyId)),
            AccountingPolicyVersion = RequireText(write.AccountingPolicyVersion, nameof(write.AccountingPolicyVersion)),
            RuleId = NormalizeOptional(write.RuleId),
            RuleVersion = NormalizeOptional(write.RuleVersion)
        };
    }

    private static bool MetadataEquivalent(JournalEntryMetadata retained, JournalEntryMetadata candidate)
    {
        retained = retained.Normalize();
        candidate = candidate.Normalize();

        return TextEquals(retained.ActivityType, candidate.ActivityType)
            && TextEquals(retained.Symbol, candidate.Symbol)
            && retained.SecurityId == candidate.SecurityId
            && retained.OrderId == candidate.OrderId
            && retained.FillId == candidate.FillId
            && TextEquals(retained.ProjectId, candidate.ProjectId)
            && TextEquals(retained.LedgerBook, candidate.LedgerBook)
            && retained.LedgerView == candidate.LedgerView
            && TextEquals(retained.ScenarioId, candidate.ScenarioId)
            && TextEquals(retained.StrategyId, candidate.StrategyId)
            && TextEquals(retained.FinancialAccountId, candidate.FinancialAccountId)
            && TextEquals(retained.CounterpartyAccountId, candidate.CounterpartyAccountId)
            && TextEquals(retained.Institution, candidate.Institution)
            && retained.EffectiveDate == candidate.EffectiveDate
            && TextEquals(retained.IdempotencyKey, candidate.IdempotencyKey)
            && TextEquals(retained.FundEventId, candidate.FundEventId)
            && TextEquals(retained.FundEventType, candidate.FundEventType)
            && TextEquals(retained.CapitalAccountId, candidate.CapitalAccountId)
            && TextEquals(retained.InvestorId, candidate.InvestorId)
            && TextEquals(retained.PaymentIntentId, candidate.PaymentIntentId)
            && TextEquals(retained.SettlementReference, candidate.SettlementReference)
            && TagsEquivalent(retained.Tags, candidate.Tags)
            && EvidenceEquivalent(retained.EvidenceReferences, candidate.EvidenceReferences);
    }

    private static bool TagsEquivalent(
        IReadOnlyDictionary<string, string>? retained,
        IReadOnlyDictionary<string, string>? candidate)
    {
        var ignoreLineDimensionCompatibilityTags =
            ContainsTag(retained, AccountingPostingCommandValidator.PostingCommandFingerprintTag) &&
            ContainsTag(candidate, AccountingPostingCommandValidator.PostingCommandFingerprintTag);
        var retainedPairs = (retained ?? new Dictionary<string, string>())
            .Where(pair => !ignoreLineDimensionCompatibilityTags || !IsLineDimensionCompatibilityTag(pair.Key))
            .ToArray();
        var candidatePairs = (candidate ?? new Dictionary<string, string>())
            .Where(pair => !ignoreLineDimensionCompatibilityTags || !IsLineDimensionCompatibilityTag(pair.Key))
            .ToArray();
        if (retainedPairs.Length != candidatePairs.Length)
            return false;

        foreach (var (key, retainedValue) in retainedPairs)
        {
            var candidatePair = candidatePairs.FirstOrDefault(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
            if (candidatePair.Key is null ||
                !string.Equals(retainedValue, candidatePair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsTag(
        IReadOnlyDictionary<string, string>? tags,
        string key)
        => tags?.Keys.Any(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsLineDimensionCompatibilityTag(string? key)
        => key?.StartsWith("lineDimensions.", StringComparison.OrdinalIgnoreCase) == true;

    private static bool EvidenceEquivalent(
        IReadOnlyList<JournalEvidenceReference> retained,
        IReadOnlyList<JournalEvidenceReference> candidate)
    {
        if (retained.Count != candidate.Count)
            return false;

        var retainedOrdered = retained
            .Select(static evidence => evidence.Normalize())
            .OrderBy(static evidence => evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Uri, StringComparer.Ordinal)
            .ThenBy(static evidence => evidence.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SourceSystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.RetainedAtUtc)
            .ThenBy(static evidence => evidence.RetainedBy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SubjectId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.ContentHash, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Description, StringComparer.Ordinal)
            .ToArray();
        var candidateOrdered = candidate
            .Select(static evidence => evidence.Normalize())
            .OrderBy(static evidence => evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Uri, StringComparer.Ordinal)
            .ThenBy(static evidence => evidence.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SourceSystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.RetainedAtUtc)
            .ThenBy(static evidence => evidence.RetainedBy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.SubjectId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.ContentHash, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static evidence => evidence.Description, StringComparer.Ordinal)
            .ToArray();
        return retainedOrdered.SequenceEqual(candidateOrdered);
    }

    private static bool DimensionsEquivalent(
        LedgerLineDimensionSet? retained,
        LedgerLineDimensionSet? candidate)
    {
        if (retained is null || candidate is null)
            return retained is null && candidate is null;

        return TextEquals(retained.FundId, candidate.FundId)
            && TextEquals(retained.EntityId, candidate.EntityId)
            && TextEquals(retained.SleeveId, candidate.SleeveId)
            && TextEquals(retained.StrategyId, candidate.StrategyId)
            && TextEquals(retained.InvestorId, candidate.InvestorId)
            && TextEquals(retained.CapitalAccountId, candidate.CapitalAccountId)
            && retained.InstrumentId == candidate.InstrumentId
            && retained.PositionId == candidate.PositionId
            && TextEquals(retained.TaxLotId, candidate.TaxLotId)
            && TextEquals(retained.CostCenterId, candidate.CostCenterId)
            && TextEquals(retained.CounterpartyId, candidate.CounterpartyId)
            && TextEquals(retained.OrganizationId, candidate.OrganizationId)
            && TextEquals(retained.PortfolioId, candidate.PortfolioId)
            && TextEquals(retained.BookId, candidate.BookId)
            && TextEquals(retained.AccountId, candidate.AccountId)
            && TextEquals(retained.CustomerId, candidate.CustomerId)
            && TextEquals(retained.VendorId, candidate.VendorId)
            && TextEquals(retained.ProjectId, candidate.ProjectId)
            // Was a first-match scan whose one-to-one pairing held only because the store
            // canonicalizes these keys on read. Nothing at this seam said so or checked it.
            && ExternalDimensionsMatch(retained.ExternalGlDimensions, candidate.ExternalGlDimensions);
    }

    private static bool TextEquals(string? retained, string? candidate)
        => string.Equals(retained, candidate, StringComparison.OrdinalIgnoreCase);

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LedgerValidationException($"{parameterName} is required for durable ledger posting.");

        return value.Trim();
    }
}
