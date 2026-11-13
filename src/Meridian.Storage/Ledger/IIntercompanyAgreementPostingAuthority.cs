using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.Ledger;

/// <summary>Checks approved agreement authority and retains exclusion against amendments through ledger commit.</summary>
public interface IIntercompanyAgreementPostingAuthority
{
    Task<IAsyncDisposable> AcquireValidatedLeaseAsync(
        IntercompanyAgreementPostingRequest request,
        CancellationToken ct = default);
}

/// <summary>Validates a transaction's union of agreement IDs under one sorted retained lease.</summary>
public interface IIntercompanyAgreementBatchPostingAuthority : IIntercompanyAgreementPostingAuthority
{
    Task<IAsyncDisposable> AcquireValidatedBatchLeaseAsync(
        IReadOnlyList<IntercompanyAgreementPostingRequest> requests, CancellationToken ct = default);
}

/// <summary>Read-only preparation/review validation using the same authority as durable append.</summary>
public interface IIntercompanyAgreementJournalValidation
{
    Task ValidateIntercompanyAgreementReferencesAsync(LedgerJournalEntryWrite write, CancellationToken ct = default);
}

/// <summary>
/// A request constructed only by the durable ledger boundary after resolving its actual book and,
/// for historical reversals, proving the complete inverse of the immutable source journal.
/// </summary>
public sealed class IntercompanyAgreementPostingRequest
{
    internal IntercompanyAgreementPostingRequest(string tenantId, Guid bookEntityId,
        DateOnly accountingDate, string bookCurrency,
        IReadOnlyList<IntercompanyAgreementPostingLine> lines, bool isVerifiedSourceReversal)
    {
        TenantId = tenantId;
        BookEntityId = bookEntityId;
        AccountingDate = accountingDate;
        BookCurrency = bookCurrency;
        Lines = lines;
        IsVerifiedSourceReversal = isVerifiedSourceReversal;
    }

    public string TenantId { get; }
    public Guid BookEntityId { get; }
    public DateOnly AccountingDate { get; }
    public string BookCurrency { get; }
    public IReadOnlyList<IntercompanyAgreementPostingLine> Lines { get; }
    public bool IsVerifiedSourceReversal { get; }
    public LegalEntityReadAuthorityProof? StructureAuthorityProof { get; internal set; }
}

public sealed record IntercompanyAgreementPostingLine(
    Guid AgreementId, int AgreementVersion, Guid PostingEntityId, Guid CounterpartyId,
    string TransactionCurrency);
