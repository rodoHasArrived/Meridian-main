using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Ledger;

public sealed record ControlledCloseAuthorizationRequestDto(Guid JournalEntryId, string FundProfileId,
    int DraftVersion, EntityLifecycleOperationDto Operation, string SourceObligationEvidence,
    string Reason, string? TenantId = null, string? CompanyId = null);

public sealed record ControlledCloseAuthorizationDto(Guid AuthorizationId, Guid JournalEntryId,
    Guid LedgerBookId, string FundProfileId, int DraftVersion, string? TenantId,
    EntityLifecycleOperationDto Operation, string PostingFingerprint, string ApprovalId,
    string SourceObligationEvidence, string Reason, string AuthorizedBy, DateTimeOffset AuthorizedAtUtc,
    Guid? SourceJournalEntryId = null,
    IReadOnlyDictionary<string, string>? EntityLifecycleRevisions = null);
