using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

public interface IControlledCloseAuthorizationStore
{
    Task<ControlledCloseAuthorizationDto> SaveCloseAuthorizationAsync(ControlledCloseAuthorizationDto authorization, CancellationToken ct = default);
    Task<ControlledCloseAuthorizationDto?> GetCloseAuthorizationAsync(Guid authorizationId, CancellationToken ct = default);
    Task<ControlledCloseAuthorizationDto?> FindCloseAuthorizationAsync(Guid bookId, Guid journalId, int draftVersion, CancellationToken ct = default);
}

/// <summary>Binds reviewed economics, lineage and evidence while allowing a new posting timestamp and period CAS version.</summary>
public static class ControlledCloseFingerprint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Compute(LedgerJournalEntryWrite write)
    {
        write = AccountingPostingCommandValidator.NormalizeAndValidate(write);
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            write.Entry.JournalEntryId,
            write.Entry.Description,
            write.Entry.Lines,
            write.PeriodId,
            write.LedgerBookId,
            write.AccountingBasis,
            write.AccountingPolicyId,
            write.AccountingPolicyVersion,
            write.SourceJournalEntryId,
            write.PostingCommand?.EffectiveDate,
            write.PostingCommand?.Intent,
            write.PostingCommand?.ApprovalId,
            write.PostingCommand?.TreasuryContext,
            Evidence = write.PostingCommand?.Evidence.Select(item => new
            {
                item.EvidenceId,
                item.Uri,
                item.Kind,
                item.SourceSystem,
                item.SubjectId,
                item.ContentHash
            }).OrderBy(item => item.EvidenceId, StringComparer.Ordinal).ToArray()
        }, Json));
    }
}
