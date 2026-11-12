using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task AppendIntercompanyProposalsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerJournalEntryWrite write, CancellationToken ct)
    {
        var groups = write.Entry.Lines.Where(line => line.Dimensions?.AgreementId is not null)
            .GroupBy(line => new { line.Dimensions!.AgreementId, line.Dimensions.AgreementRevisionId,
                line.Dimensions.IntercompanyTransactionId, line.Dimensions.EntityId, line.Dimensions.CounterpartyId })
            .OrderBy(group => group.Key.AgreementId, StringComparer.Ordinal).ToArray();
        if (groups.Length == 0 || ConsolidationPostingEvidenceGuard.Validate(write) is not null)
            return;
        var unattributed = write.Entry.Lines.Where(line => line.Dimensions?.AgreementId is null).ToArray();
        var bookId = write.LedgerBookId ?? write.PostingCommand?.LedgerBookId
            ?? throw new LedgerValidationException("An intercompany proposal requires a source book.");
        var book = await LoadLedgerBookAsync(connection, transaction, bookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Intercompany source book is unavailable.");
        string? tenant;
        await using (var scope = connection.CreateCommand())
        {
            scope.Transaction = transaction;
            scope.CommandText = $"select tenant_id from {Qualified("ledger_books")} where ledger_book_id=@id";
            scope.Parameters.AddWithValue("id", bookId);
            tenant = await scope.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        }
        // Legacy unattributed books retain their existing warning posture, never a fabricated tenant.
        if (string.IsNullOrWhiteSpace(tenant)) return;
        if (write.Entry.Metadata.Tags?.GetValueOrDefault("intercompany.proposalId") is { } originId)
        {
            await ValidateIntercompanyCounterpartOriginAsync(connection, transaction, write, book, tenant, originId, ct)
                .ConfigureAwait(false);
            return;
        }
        foreach (var group in groups)
        {
            if (!Guid.TryParse(group.Key.AgreementId, out var agreementId)
                || !Guid.TryParse(group.Key.AgreementRevisionId, out var revisionId)
                || !Guid.TryParse(group.Key.EntityId, out var entityId)
                || !Guid.TryParse(group.Key.CounterpartyId, out var counterpartyId)
                || string.IsNullOrWhiteSpace(group.Key.IntercompanyTransactionId))
                continue; // The authority participant retains the legacy warning or blocks enforced writes.
            var key = $"intercompany-mirror-v1:{write.Entry.JournalEntryId:D}:{agreementId:D}:{revisionId:D}:" +
                $"{group.Key.IntercompanyTransactionId}:{entityId:D}:{counterpartyId:D}";
            var proposalId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
            var source = new IntercompanyProposalSourceDto(proposalId, tenant,
                write.Entry.Metadata.Tags?.GetValueOrDefault("companyId"), write.Entry.JournalEntryId,
                bookId, entityId, counterpartyId, agreementId, revisionId, group.Key.IntercompanyTransactionId,
                write.PostingCommand?.EffectiveDate ?? write.Entry.Metadata.EffectiveDate
                    ?? DateOnly.FromDateTime(write.Entry.Timestamp.UtcDateTime), book.BaseCurrency,
                group.Concat(unattributed).OrderBy(line => line.EntryId).Select(line => new IntercompanyProposalSourceLineDto(
                    line.EntryId, line.Account.Name, line.Account.AccountType.ToString(), line.Dimensions?.ChartAccountNodeId,
                    line.Debit, line.Credit, line.Currency?.TransactionCurrency ?? book.BaseCurrency,
                    line.Currency?.TransactionDebit ?? line.Debit, line.Currency?.TransactionCredit ?? line.Credit)).ToArray(),
                write.PostingCommand?.Intent == AccountingPostingIntentDto.Reversal ? write.SourceJournalEntryId : null)
            {
                RequiresSourceAllocation = groups.Length > 1 && unattributed.Length > 0
            };
            var payload = JsonSerializer.Serialize(source, IntercompanyProposalJsonContext.Default.IntercompanyProposalSourceDto);
            var digest = Sha256Digest.ComputeUtf8(payload);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                insert into {Qualified("intercompany_proposals")}
                    (proposal_id,tenant_id,source_book_id,source_journal_id,source_payload,source_digest)
                values (@id,@tenant,@book,@journal,@payload,@digest)
                on conflict (proposal_id) do nothing;
                select source_digest from {Qualified("intercompany_proposals")} where proposal_id=@id;
                """;
            insert.Parameters.AddWithValue("id", proposalId);
            insert.Parameters.AddWithValue("tenant", tenant);
            insert.Parameters.AddWithValue("book", bookId);
            insert.Parameters.AddWithValue("journal", write.Entry.JournalEntryId);
            insert.Parameters.AddWithValue("payload", payload);
            insert.Parameters.AddWithValue("digest", digest);
            var retained = await insert.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (!Sha256Digest.FixedEquals(retained, digest))
                throw new LedgerValidationException("Intercompany proposal identity already retains different source facts.");
        }
    }

    private async Task ValidateIntercompanyCounterpartOriginAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerJournalEntryWrite write, LedgerBookRecord book, string tenant, string originId, CancellationToken ct)
    {
        if (!Guid.TryParse(originId, out var proposalId) || proposalId == Guid.Empty)
            throw new LedgerValidationException("Counterpart source authority identity is invalid.");
        IntercompanyProposalSourceDto retained;
        string digest;
        await using (var origin = connection.CreateCommand())
        {
            origin.Transaction = transaction;
            origin.CommandText = $"select p.source_payload,p.source_digest,p.target_book_id,p.target_journal_id, "
                + $"exists(select 1 from {Qualified("intercompany_proposal_links")} l where l.tenant_id=p.tenant_id and l.proposal_id=p.proposal_id and l.target_journal_id<>@id) "
                + $"from {Qualified("intercompany_proposals")} p where p.proposal_id=@id and p.tenant_id=@tenant for update of p";
            origin.Parameters.AddWithValue("id", proposalId);
            origin.Parameters.AddWithValue("tenant", tenant);
            await using var reader = await origin.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new LedgerValidationException("Counterpart source authority is unavailable.");
            retained = JsonSerializer.Deserialize(reader.GetString(0), IntercompanyProposalJsonContext.Default.IntercompanyProposalSourceDto)
                ?? throw new LedgerValidationException("Counterpart source authority is invalid.");
            digest = reader.GetString(1);
            if (reader.GetBoolean(4))
                throw new LedgerValidationException("This generated counterpart was replaced by a reviewed manual resolution; its rejected draft cannot be posted.");
            if (retained.ProposalId != proposalId || retained.TenantId != tenant
                || !Sha256Digest.FixedEquals(digest, write.Entry.Metadata.Tags?.GetValueOrDefault("intercompany.sourceDigest"))
                || retained.CounterpartyEntityId != book.FundStructureNodeId || retained.SourceBookId == book.LedgerBookId
                || (!reader.IsDBNull(2) && reader.GetGuid(2) != book.LedgerBookId)
                || (!reader.IsDBNull(3) && reader.GetGuid(3) != proposalId)
                || !MatchesIntercompanyCounterpartGroups(write.Entry, retained))
                throw new LedgerValidationException("Counterpart journal differs from its scoped retained source authority.");
        }

        if (write.Entry.JournalEntryId == proposalId)
        {
            if (retained.ReversesJournalId is not null)
            {
                var sourceId = RequireReviewedIntercompanyCorrection(write, requireReversal: true);
                await ValidateIntercompanySourceReversalDeliveryAsync(connection, transaction, write, book,
                    retained, sourceId, ct).ConfigureAwait(false);
            }
            else if (write.PostingCommand?.Intent is AccountingPostingIntentDto.Reversal or AccountingPostingIntentDto.Rebook
                || write.SourceJournalEntryId.HasValue || write.PostingCommand?.SourceJournalEntryId is not null)
                throw new LedgerValidationException("An original generated counterpart cannot claim correction lineage.");
            return;
        }

        // A correction keeps the original generated marker as provenance. Its new journal id is
        // never itself the proposal authority: prove a reviewed, same-book retained predecessor.
        var correctionSourceId = RequireReviewedIntercompanyCorrection(write);
        var sources = await QueryAsync(connection, transaction,
            new(LedgerBookId: book.LedgerBookId, JournalEntryId: correctionSourceId), null, ct).ConfigureAwait(false);
        if (sources.Count != 1 || sources[0].Entry.JournalEntryId != correctionSourceId
            || !Guid.TryParse(sources[0].Entry.Metadata.Tags?.GetValueOrDefault("intercompany.proposalId"), out var sourceOrigin)
            || sourceOrigin != proposalId
            || !Sha256Digest.FixedEquals(digest, sources[0].Entry.Metadata.Tags?.GetValueOrDefault("intercompany.sourceDigest"))
            || !MatchesIntercompanyCounterpartGroups(sources[0].Entry, retained)
            || (write.PostingCommand!.Intent == AccountingPostingIntentDto.Reversal
                && !IntercompanyJournalCorrectionEvidence.IsExactInverse(write.Entry, sources[0].Entry)))
            throw new LedgerValidationException("A generated counterpart correction must retain its actual same-book source and exact reversal economics.");
    }

    private static Guid RequireReviewedIntercompanyCorrection(LedgerJournalEntryWrite write, bool requireReversal = false)
    {
        var command = write.PostingCommand;
        var sourceId = write.SourceJournalEntryId ?? command?.SourceJournalEntryId;
        if (command?.Intent is not (AccountingPostingIntentDto.Reversal or AccountingPostingIntentDto.Rebook)
            || (requireReversal && command.Intent != AccountingPostingIntentDto.Reversal)
            || sourceId is null || sourceId == write.Entry.JournalEntryId
            || (write.SourceJournalEntryId.HasValue && command.SourceJournalEntryId.HasValue
                && write.SourceJournalEntryId != command.SourceJournalEntryId)
            || command.ApprovalState != AccountingPostingApprovalStateDto.Approved
            || write.PostingKind != LedgerPostingKindDto.Adjustment
            || write.AdjustmentApproval is not { Status: LedgerAdjustmentApprovalStatusDto.Approved } approval
            || string.IsNullOrWhiteSpace(command.ApprovalId) || command.ApprovalId != approval.ApprovalId
            || approval.GovernanceCaseId != $"journal-correction:{sourceId:D}")
            throw new LedgerValidationException("A generated counterpart correction requires reviewed, consistent source-journal lineage.");
        return sourceId.Value;
    }

    private async Task ValidateIntercompanySourceReversalDeliveryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerJournalEntryWrite write, LedgerBookRecord book, IntercompanyProposalSourceDto retained, Guid targetOriginalId,
        CancellationToken ct)
    {
        // A new source-reversal proposal has its own marker, unlike a later workbench correction.
        // Prove both actual source economics and the authoritative prior counterpart resolution.
        var sourceReversal = await QueryAsync(connection, transaction,
            new(LedgerBookId: retained.SourceBookId, JournalEntryId: retained.SourceJournalId), null, ct).ConfigureAwait(false);
        var sourceOriginal = await QueryAsync(connection, transaction,
            new(LedgerBookId: retained.SourceBookId, JournalEntryId: retained.ReversesJournalId), null, ct).ConfigureAwait(false);
        if (sourceReversal.Count != 1 || sourceOriginal.Count != 1
            || sourceReversal[0].SourceJournalEntryId != retained.ReversesJournalId
            || (sourceReversal[0].Entry.Metadata.EffectiveDate
                ?? DateOnly.FromDateTime(sourceReversal[0].Entry.Timestamp.UtcDateTime)) != retained.EffectiveDate
            || !IntercompanyJournalCorrectionEvidence.IsExactInverse(sourceReversal[0].Entry, sourceOriginal[0].Entry))
            throw new LedgerValidationException("A counterpart reversal delivery requires its exact retained source reversal.");
        var targets = new List<(Guid? BookId, Guid? JournalId)>();
        await using (var prior = connection.CreateCommand())
        {
            prior.Transaction = transaction;
            prior.CommandText = $"""
                select p.source_payload,coalesce(l.target_book_id,p.target_book_id),coalesce(l.target_journal_id,p.target_journal_id)
                from {Qualified("intercompany_proposals")} p
                left join lateral (select target_book_id,target_journal_id from {Qualified("intercompany_proposal_links")} l
                    where l.tenant_id=p.tenant_id and l.proposal_id=p.proposal_id
                    order by l.recorded_at desc,l.request_id desc limit 1) l on true
                where p.tenant_id=@tenant and p.source_book_id=@book and p.source_journal_id=@journal
                for update of p
                """;
            prior.Parameters.AddWithValue("tenant", retained.TenantId);
            prior.Parameters.AddWithValue("book", retained.SourceBookId);
            prior.Parameters.AddWithValue("journal", retained.ReversesJournalId!.Value);
            await using var reader = await prior.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var candidate = JsonSerializer.Deserialize(reader.GetString(0), IntercompanyProposalJsonContext.Default.IntercompanyProposalSourceDto);
                if (candidate is not null && candidate.SourceEntityId == retained.SourceEntityId
                    && candidate.CounterpartyEntityId == retained.CounterpartyEntityId
                    && candidate.AgreementId == retained.AgreementId && candidate.AgreementRevisionId == retained.AgreementRevisionId
                    && candidate.TransactionId == retained.TransactionId)
                    targets.Add((reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2)));
            }
        }
        if (targets.Count != 1 || targets[0].BookId != book.LedgerBookId || targets[0].JournalId != targetOriginalId)
            throw new LedgerValidationException("A counterpart reversal must reference the authoritative prior counterpart resolution.");
        var targetOriginal = await QueryAsync(connection, transaction,
            new(LedgerBookId: book.LedgerBookId, JournalEntryId: targetOriginalId), null, ct).ConfigureAwait(false);
        if (targetOriginal.Count != 1 || !MatchesIntercompanyCounterpartGroups(targetOriginal[0].Entry, retained)
            || !IntercompanyJournalCorrectionEvidence.IsExactInverse(write.Entry, targetOriginal[0].Entry))
            throw new LedgerValidationException("A counterpart reversal delivery must exactly reverse its actual retained counterpart.");
        // AppendCore holds the ledger audit fence through commit. Recheck after approval because
        // a manual correction may have posted since this generated inverse was prepared.
        await using var corrections = connection.CreateCommand();
        corrections.Transaction = transaction;
        corrections.CommandText = $"""
            with recursive correction_family(journal_id) as (
                select @original::uuid
                union
                select je.journal_entry_id
                from {Qualified("journal_entries")} je
                join {Qualified("accounting_periods")} ap on ap.period_id=je.period_id
                join correction_family parent on coalesce(je.source_journal_entry_id::text,
                    je.metadata->'tags'->>'{LedgerJournalReversal.ReversalOfTag}',
                    je.metadata->'tags'->>'reversalOfJournalEntryId',
                    je.metadata->'tags'->>'rebookedFromJournalEntryId',
                    je.metadata->'tags'->>'sourceJournalEntryId')=parent.journal_id::text
                where ap.ledger_book_id=@book and ap.tenant_id=@tenant and je.journal_entry_id<>@current
            )
            select exists(select 1 from correction_family where journal_id<>@original)
            """;
        corrections.Parameters.AddWithValue("original", targetOriginalId);
        corrections.Parameters.AddWithValue("book", book.LedgerBookId);
        corrections.Parameters.AddWithValue("tenant", retained.TenantId);
        corrections.Parameters.AddWithValue("current", write.Entry.JournalEntryId);
        if (await corrections.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
            throw new LedgerValidationException("The prior counterpart already has a posted correction; review and manually resolve the source-reversal proposal.");
    }

    private static bool MatchesIntercompanyCounterpartGroups(JournalEntry entry, IntercompanyProposalSourceDto retained)
    {
        var lines = entry.Lines.Where(line => line.Dimensions?.AgreementId is not null).ToArray();
        return lines.Length > 0 && lines.All(line => line.Dimensions!.AgreementId == retained.AgreementId.ToString("D")
            && line.Dimensions.AgreementRevisionId == retained.AgreementRevisionId.ToString("D")
            && line.Dimensions.IntercompanyTransactionId == retained.TransactionId
            && line.Dimensions.EntityId == retained.CounterpartyEntityId.ToString("D")
            && line.Dimensions.CounterpartyId == retained.SourceEntityId.ToString("D"));
    }

}
