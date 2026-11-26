using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.AccountingSystem;

/// <summary>The exact Meridian ownership and accounting scope for an external projection.</summary>
public sealed record ExternalGlPostingScope(
    string TenantId, string CompanyId, string FundProfileId, Guid LedgerBookId, Guid PeriodId);

/// <summary>A named, owned connection pinned to its external ledger and environment.</summary>
public sealed record ExternalGlDestination(string ProviderId, string ConnectionId, string ExternalLedgerId, string Environment);

public sealed record ExternalGlBookPolicy(
    ExternalGlDestination Destination, bool ConfirmationRequired, string Actor, DateTimeOffset RecordedAtUtc);

/// <summary>The retained certified mapping, versioned by content and bound to the intended destination.</summary>
public sealed record ExternalGlMappingVersion(
    string Version, ExternalGlMappingProfileDto Profile, string CertifiedBy, IReadOnlyList<string> Evidence);

public sealed record ExternalGlSourceCheckpoint(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long LastJournalSequence,
    string ContentHash, DateOnly PeriodStart, DateOnly PeriodEnd);

public sealed record ExternalGlPostingLine(
    Guid MeridianLineId, string MeridianAccount, string ExternalAccountId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Debit,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Credit,
    LedgerDimensionSetDto MeridianDimensions, IReadOnlyDictionary<string, string> ExternalDimensions);

/// <summary>One balanced provider document derived from one immutable Meridian journal.</summary>
public sealed record ExternalGlPostingDocument(
    Guid MeridianJournalId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long JournalSequence,
    DateOnly EffectiveDate, string Currency, string IdempotencyKey,
    IReadOnlyList<ExternalGlPostingLine> Lines, string? SupersedesIdempotencyKey = null, Guid? ReversesJournalId = null);

[JsonConverter(typeof(JsonStringEnumConverter<ExternalGlPostingPurpose>))]
public enum ExternalGlPostingPurpose
{
    Export,
    Correction,
    Reversal
}

public sealed record ExternalGlPostingPayload(
    ExternalGlPostingScope Scope, ExternalGlDestination Destination, ExternalGlMappingVersion Mapping,
    ExternalGlSourceCheckpoint Source, ExternalGlPostingPurpose Purpose, string? PredecessorPackageId,
    IReadOnlyList<ExternalGlPostingDocument> Documents, string Rationale)
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal TotalDebit => Documents.Sum(doc => doc.Lines.Sum(line => line.Debit));

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal TotalCredit => Documents.Sum(doc => doc.Lines.Sum(line => line.Credit));
}

public sealed record ExternalGlPostingDecision(string Actor, string Notes, string PayloadHash, DateTimeOffset RecordedAtUtc);

[JsonConverter(typeof(JsonStringEnumConverter<ExternalGlPostingStatus>))]
public enum ExternalGlPostingStatus
{
    Pending,
    FullyAccepted,
    PartiallyAccepted,
    Rejected,
    OutcomeUnknown
}

[JsonConverter(typeof(JsonStringEnumConverter<ExternalGlLineStatus>))]
public enum ExternalGlLineStatus
{
    Accepted,
    Rejected,
    OutcomeUnknown
}

[JsonConverter(typeof(JsonStringEnumConverter<ExternalGlErrorCode>))]
public enum ExternalGlErrorCode
{
    CredentialsUnavailable,
    ProviderUnavailable,
    RateLimited,
    UnsupportedConfiguration,
    ValidationFailed,
    DuplicateReference,
    ReceiptMismatch,
    DocumentNotFound,
    UnrecognizedResponse
}

public sealed record ExternalGlLineReceipt(
    Guid MeridianLineId, ExternalGlLineStatus Status, string? ProviderLineId = null,
    ExternalGlErrorCode? ErrorCode = null, string? ExternalAccountId = null,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal? Debit = null,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal? Credit = null,
    IReadOnlyDictionary<string, string>? ExternalDimensions = null);

[JsonConverter(typeof(JsonStringEnumConverter<ExternalGlReceiptKind>))]
public enum ExternalGlReceiptKind
{
    Pending,
    Completed,
    Rejected,
    OutcomeUnknown,
    NotFound
}

/// <summary>Normalized response to a create or lookup. NotFound represents a successful exact-key lookup.</summary>
public sealed record ExternalGlProviderReceipt(
    ExternalGlReceiptKind Kind, IReadOnlyList<ExternalGlLineReceipt> Lines,
    string? ProviderBatchId = null, string? ProviderDocumentId = null, string? ProviderTraceId = null,
    DateTimeOffset? ProviderRecordedAtUtc = null, ExternalGlErrorCode? ErrorCode = null, string? ProviderErrorCode = null,
    bool SafeToReplay = false);

/// <summary>Append-only observation from a provider operation with the local observation time.</summary>
public sealed record ExternalGlReceiptObservation(
    DateTimeOffset ObservedAtUtc, bool IsLookup, ExternalGlProviderReceipt Receipt);

public sealed record ExternalGlDocumentProgress(
    string IdempotencyKey, ExternalGlPostingStatus Status, IReadOnlyList<ExternalGlReceiptObservation> Observations,
    int PostAttempts = 0, DateTimeOffset? LastPostAttemptAtUtc = null);

public sealed record ExternalGlPostingPackage(
    string PackageId, string PayloadHash, ExternalGlPostingPayload Payload, string PreparedBy,
    DateTimeOffset PreparedAtUtc, ExternalGlPostingDecision? Certification, ExternalGlPostingDecision? Approval,
    ExternalGlPostingStatus Status, IReadOnlyList<ExternalGlDocumentProgress> Progress);

public sealed record ExternalGlPackageListItem(
    string PackageId, ExternalGlPostingPurpose Purpose, ExternalGlPostingStatus Status,
    DateTimeOffset PreparedAtUtc, bool Certified, bool Approved);

/// <summary>Final accounting outputs use the same posting coverage rule as the durable period lock.</summary>
public interface IExternalGlConfirmationGate
{
    Task RequireConfirmationAsync(Guid ledgerBookId, Guid periodId, CancellationToken ct = default);

    Task RequireConfirmationForRangeAsync(Guid ledgerBookId, DateOnly start, DateOnly end, CancellationToken ct = default);
}
