using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;

namespace Meridian.Storage.Ledger;

public static class LegacyIntercompanyAttributionRules
{
    public static string SourceDigest(LedgerBookRecord book, LedgerJournalEntryRecord record, LedgerEntry line)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        { book.LedgerBookId, record.Entry.JournalEntryId, record.GlobalSequence, record.Entry.Metadata.EffectiveDate, Line = line }));

    public static IReadOnlyList<LegacyIntercompanyAttributionDto> Resolve(IEnumerable<LegacyIntercompanyAttributionDto> history)
        => history.GroupBy(x => new { x.JournalEntryId, x.LineId }).Select(x => x.MaxBy(y => y.Revision)!)
            .OrderBy(x => x.JournalEntryId).ThenBy(x => x.LineId).ToArray();

    public static void ValidateAppend(LegacyIntercompanyAttributionDto fact, Guid? expectedPrevious,
        IReadOnlyList<LegacyIntercompanyAttributionDto> history)
    {
        if (fact.AttributionId == Guid.Empty || fact.LedgerBookId == Guid.Empty || fact.JournalEntryId == Guid.Empty ||
            fact.LineId == Guid.Empty || fact.AgreementId == Guid.Empty || fact.AgreementRevisionId == Guid.Empty ||
            fact.AccountBindingRevisionId == Guid.Empty || fact.ApprovedAt == default)
            throw new InvalidOperationException("Legacy attribution requires exact source, agreement, account and approval identities.");
        foreach (var text in new[] { fact.ChartAccountNodeId, fact.IntercompanyTransactionId, fact.PreparedBy,
            fact.ApprovedBy, fact.EvidenceReference, fact.TenantId, fact.SourceLineDigest })
            if (string.IsNullOrWhiteSpace(text) || text != text.Trim())
                throw new InvalidOperationException("Legacy attribution requires trimmed source, scope and review evidence.");
        if (fact.SourceLineDigest.Length != 64 || fact.SourceLineDigest.Any(x => !Uri.IsHexDigit(x)) ||
            fact.TenantId.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            fact.PreparedBy.Equals(fact.ApprovedBy, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legacy attribution requires a source digest, owning tenant and independent approval.");
        if (history.Any(x => x.LedgerBookId != fact.LedgerBookId || x.TenantId != fact.TenantId || x.CompanyId != fact.CompanyId))
            throw new InvalidOperationException("Legacy attribution history must have one authoritative book owner.");
        var current = history.Where(x => x.JournalEntryId == fact.JournalEntryId && x.LineId == fact.LineId).MaxBy(x => x.Revision);
        if (current?.AttributionId != expectedPrevious || fact.PreviousAttributionId != expectedPrevious ||
            fact.Revision != (current?.Revision ?? 0) + 1)
            throw new InvalidOperationException("Legacy attribution changed; reload before appending a reviewed successor.");
    }
}

/// <summary>Checks reviewer assertions against immutable journal facts and retained approved terms.</summary>
public sealed class LegacyIntercompanyAttributionValidator(ILedgerJournalStore journals,
    IIntercompanyAccountBindingStore bindings, IIntercompanyAgreementStore agreements)
{
    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        var bindingLease = await bindings.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        try { return new AuthorityLease(bindingLease, await agreements.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false)); }
        catch { await bindingLease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private sealed class AuthorityLease(IAsyncDisposable binding, IAsyncDisposable agreement) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        { try { await agreement.DisposeAsync().ConfigureAwait(false); } finally { await binding.DisposeAsync().ConfigureAwait(false); } }
    }

    public async Task ValidateAsync(LegacyIntercompanyAttributionDto fact, CancellationToken ct = default)
    {
        var book = await journals.GetLedgerBookAsync(fact.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The attribution source book is unavailable.");
        var population = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book.LedgerBookId), ct).ConfigureAwait(false);
        var record = population.SingleOrDefault(x => x.Entry.JournalEntryId == fact.JournalEntryId)
            ?? throw new InvalidOperationException("The attribution source journal is unavailable in its exact book.");
        var line = record.Entry.Lines.SingleOrDefault(x => x.EntryId == fact.LineId)
            ?? throw new InvalidOperationException("The attribution source line is unavailable.");
        if (!Sha256Digest.FixedEquals(fact.SourceLineDigest, LegacyIntercompanyAttributionRules.SourceDigest(book, record, line)))
            throw new InvalidOperationException("Legacy attribution does not match the exact immutable source digest.");
        if (line.Dimensions?.AgreementId is not null || line.Account.Symbol is not null || line.Account.FinancialAccountId is not null)
            throw new InvalidOperationException("Only unscoped legacy lines lacking agreement attribution may receive sidecar evidence.");
        var binding = await bindings.GetRevisionAsync(fact.AccountBindingRevisionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The approved attribution account revision is unavailable.");
        if (binding.LedgerBookId != fact.LedgerBookId || binding.ChartAccountNodeId != fact.ChartAccountNodeId ||
            binding.TenantId != fact.TenantId || binding.CompanyId != fact.CompanyId ||
            binding.AccountType != line.Account.AccountType.ToString() ||
            !(binding.AccountName == line.Account.Name || binding.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Legacy source identity differs from its approved account binding.");
        var history = await agreements.GetAsync(fact.TenantId, fact.AgreementId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The attribution agreement is unavailable.");
        var date = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
        var instant = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var agreement = IntercompanyAgreementRevisionResolver.ResolveApprovedRevision(history, fact.AgreementRevisionId, instant);
        if (agreement.EffectiveFrom > instant || agreement.EffectiveTo is { } end && instant >= end ||
            agreement.Kind == IntercompanyAgreementKindDto.Guarantee ||
            (line.Currency?.TransactionCurrency ?? book.BaseCurrency) != agreement.Currency)
            throw new InvalidOperationException("Legacy source date, currency or treatment falls outside the approved agreement.");
        if (!Guid.TryParse(line.Dimensions?.EntityId, out var entity) || entity != book.FundStructureNodeId ||
            !Guid.TryParse(line.Dimensions?.CounterpartyId, out var cp) || entity == cp ||
            !((agreement.PartyOneEntityId == entity && agreement.PartyTwoEntityId == cp) ||
              (agreement.PartyTwoEntityId == entity && agreement.PartyOneEntityId == cp)))
            throw new InvalidOperationException("Legacy source parties disagree with the authoritative book and approved agreement.");
        var mapping = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == entity && x.LedgerBookId == book.LedgerBookId);
        var expected = binding.Role switch
        {
            IntercompanyAccountRoleDto.Receivable => mapping?.ReceivableAccountNodeId,
            IntercompanyAccountRoleDto.Payable => mapping?.PayableAccountNodeId,
            IntercompanyAccountRoleDto.Revenue => mapping?.RevenueAccountNodeId,
            IntercompanyAccountRoleDto.Expense => mapping?.ExpenseAccountNodeId,
            _ => null
        };
        if (expected != binding.ChartAccountNodeId)
            throw new InvalidOperationException("The agreement does not authorize the legacy account role in this book.");
    }
}
