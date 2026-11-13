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
        => history.GroupBy(x => new { x.LedgerBookId, x.JournalEntryId, x.LineId }).Select(x => x.MaxBy(y => y.Revision)!)
            .Where(x => !x.Revoked)
            .OrderBy(x => x.JournalEntryId).ThenBy(x => x.LineId).ToArray();

    public static void ValidateAppend(LegacyIntercompanyAttributionDto fact, Guid? expectedPrevious,
        IReadOnlyList<LegacyIntercompanyAttributionDto> history)
    {
        if (fact.AttributionId == Guid.Empty || fact.LedgerBookId == Guid.Empty || fact.JournalEntryId == Guid.Empty ||
            fact.LineId == Guid.Empty || fact.AccountingArrangementId == Guid.Empty || fact.AccountingArrangementRevisionId == Guid.Empty ||
            fact.AccountBindingRevisionId == Guid.Empty || fact.ApprovedAt == default
            || fact.IntercompanyAgreementId == Guid.Empty || fact.IntercompanyAgreementVersion <= 0
            || fact.IntercompanyAgreementPayloadHash.Length != 64 || fact.IntercompanyAgreementPayloadHash.Any(x => !Uri.IsHexDigit(x)))
            throw new InvalidOperationException("Legacy attribution requires exact source, agreement, account and approval identities.");
        foreach (var text in new[] { fact.ChartAccountNodeId, fact.IntercompanyTransactionId, fact.PreparedBy,
            fact.ApprovedBy, fact.EvidenceReference, fact.TenantId, fact.SourceLineDigest })
            if (string.IsNullOrWhiteSpace(text) || text != text.Trim())
                throw new InvalidOperationException("Legacy attribution requires trimmed source, scope and review evidence.");
        if (fact.SourceLineDigest.Length != 64 || fact.SourceLineDigest.Any(x => !Uri.IsHexDigit(x)) ||
            fact.TenantId.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            !Enum.IsDefined(fact.ReviewMode) ||
            fact.ReviewMode == LegacyIntercompanyReviewModeDto.Independent && fact.PreparedBy.Equals(fact.ApprovedBy, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legacy attribution requires a source digest, owning tenant and independent approval.");
        if (fact.ReviewMode == LegacyIntercompanyReviewModeDto.OperatorReviewed
            && (string.IsNullOrWhiteSpace(fact.ReviewRationale) || fact.ReviewRationale != fact.ReviewRationale.Trim()
                || !fact.PreparedBy.Equals(fact.ApprovedBy, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Operator-reviewed attribution requires explicit rationale and the same retained preparing and reviewing operator.");
        if (history.Any(x => x.LedgerBookId != fact.LedgerBookId || x.TenantId != fact.TenantId || x.CompanyId != fact.CompanyId))
            throw new InvalidOperationException("Legacy attribution history must have one authoritative book owner.");
        var current = history.Where(x => x.JournalEntryId == fact.JournalEntryId && x.LineId == fact.LineId).MaxBy(x => x.Revision);
        if (current?.AttributionId != expectedPrevious || fact.PreviousAttributionId != expectedPrevious ||
            fact.Revision != (current?.Revision ?? 0) + 1)
            throw new InvalidOperationException("Legacy attribution changed; reload before appending a reviewed successor.");
        if (fact.Revoked)
        {
            if (current is null || current.Revoked)
                throw new InvalidOperationException("Only the latest active attribution can be revoked.");
            if (string.IsNullOrWhiteSpace(fact.ReviewRationale) || fact.ReviewRationale != fact.ReviewRationale.Trim())
                throw new InvalidOperationException("Attribution revocation requires an explicit reviewed rationale.");
            // Only decision metadata and successor identity may change. This also protects future source/legal pin fields.
            var priorPins = fact with
            {
                AttributionId = current.AttributionId,
                Revision = current.Revision,
                PreviousAttributionId = current.PreviousAttributionId,
                PreparedBy = current.PreparedBy,
                ApprovedBy = current.ApprovedBy,
                ApprovedAt = current.ApprovedAt,
                EvidenceReference = current.EvidenceReference,
                ReviewMode = current.ReviewMode,
                ReviewRationale = current.ReviewRationale,
                Revoked = current.Revoked
            };
            if (priorPins != current)
                throw new InvalidOperationException("Revocation must retain the exact prior source, legal, configuration and transaction pins.");
        }
    }
}

/// <summary>Checks reviewer assertions against immutable journal facts and retained approved terms.</summary>
public sealed class LegacyIntercompanyAttributionValidator(ILedgerJournalStore journals,
    IIntercompanyAccountBindingStore bindings, IIntercompanyAccountingArrangementStore agreements,
    IIntercompanyAccountingArrangementLegalAuthority legalAuthority)
{
    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(LegacyIntercompanyAttributionDto fact, CancellationToken ct = default)
    {
        var source = await journals.QueryAsync(new(LedgerBookId: fact.LedgerBookId, JournalEntryId: fact.JournalEntryId), ct).ConfigureAwait(false);
        if (source.Count != 1)
            throw new InvalidOperationException("The exact immutable legacy source is unavailable.");
        // Revocation retracts an assertion. Its immutable predecessor pins are checked atomically by
        // the store; an amended or withdrawn legal/configuration record must not prevent retraction.
        if (fact.Revoked)
            return new AuthorityLease([]);
        var date = source[0].Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(source[0].Entry.Timestamp.UtcDateTime);
        var reference = new IntercompanyCanonicalAgreementReferenceDto(fact.IntercompanyAgreementId,
            fact.IntercompanyAgreementVersion, fact.IntercompanyAgreementPayloadHash);
        var legal = await legalAuthority.AcquireLeaseAsync(fact.TenantId, [reference], ct: ct).ConfigureAwait(false);
        var leases = new List<IAsyncDisposable> { legal };
        try
        {
            _ = legal.ResolveEffective(reference, date);
            leases.Add(await bindings.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            leases.Add(await agreements.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            return new AuthorityLease(leases);
        }
        catch { await new AuthorityLease(leases).DisposeAsync().ConfigureAwait(false); throw; }
    }

    private sealed class AuthorityLease(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var i = leases.Count - 1; i >= 0; i--)
                await leases[i].DisposeAsync().ConfigureAwait(false);
        }
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
        if (fact.Revoked)
            return; // The store still requires exact book ownership, active predecessor and unchanged immutable pins.
        if (line.Dimensions?.AccountingArrangementId is not null || line.Account.Symbol is not null || line.Account.FinancialAccountId is not null)
            throw new InvalidOperationException("Only unscoped legacy lines lacking agreement attribution may receive sidecar evidence.");
        if (line.Dimensions?.IntercompanyAgreementId is { } retainedLegalId
            && (retainedLegalId != fact.IntercompanyAgreementId
                || line.Dimensions.IntercompanyAgreementVersion != fact.IntercompanyAgreementVersion))
            throw new InvalidOperationException("Legacy attribution cannot replace canonical legal provenance already retained on the source line.");
        var binding = await bindings.GetRevisionAsync(fact.AccountBindingRevisionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The approved attribution account revision is unavailable.");
        if (binding.LedgerBookId != fact.LedgerBookId || binding.ChartAccountNodeId != fact.ChartAccountNodeId ||
            binding.TenantId != fact.TenantId || binding.CompanyId != fact.CompanyId ||
            binding.AccountType != line.Account.AccountType.ToString() ||
            !(binding.AccountName == line.Account.Name || binding.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Legacy source identity differs from its approved account binding.");
        var history = await agreements.GetAsync(fact.TenantId, fact.AccountingArrangementId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The attribution agreement is unavailable.");
        var date = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
        var instant = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var arrangement = IntercompanyAccountingArrangementRevisionResolver.ResolveApprovedRevision(history, fact.AccountingArrangementRevisionId, instant);
        if (fact.IntercompanyAgreementId != arrangement.CanonicalAgreementId
            || fact.IntercompanyAgreementVersion != arrangement.CanonicalAgreementVersion
            || fact.IntercompanyAgreementPayloadHash != arrangement.CanonicalPayloadHash)
            throw new InvalidOperationException("Legacy attribution must pin the exact canonical legal revision of its accounting arrangement.");
        var legalRevision = await legalAuthority.GetRevisionAsync(fact.TenantId, arrangement.CanonicalReference, ct).ConfigureAwait(false);
        var agreement = new ResolvedIntercompanyAccountingArrangementDto(arrangement, legalRevision);
        if (legalRevision.Action == IntercompanyAgreementActionDto.Terminate
            || date < legalRevision.Terms.EffectiveFrom || legalRevision.Terms.EffectiveTo <= date)
            throw new InvalidOperationException("Legacy source date is outside the approved canonical legal revision.");
        if (agreement.EffectiveFrom > instant || agreement.EffectiveTo is { } end && instant >= end ||
            agreement.TemplateKind == IntercompanyAccountingTemplateKindDto.Guarantee ||
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
