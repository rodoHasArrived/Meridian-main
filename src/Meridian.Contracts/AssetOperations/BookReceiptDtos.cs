using System.Text.Json.Serialization;

namespace Meridian.Contracts.AssetOperations;

/// <summary>Server-resolved ownership; tenant and company are never supplied as mutation authority by a client.</summary>
public sealed record BookReceiptScopeDto(string TenantId, string CompanyId, Guid LedgerBookId);

public sealed record BookReceiptQueryDto(BookReceiptScopeDto Scope, DateOnly AsOfDate);

/// <summary>An accepted holding-sized expectation. ObligationKey is independent of terms and run revisions.</summary>
public sealed record BookReceiptExpectationDto(
    string ObligationKey,
    Guid PositionId,
    Guid SecurityId,
    string DisplayName,
    string FlowType,
    DateOnly DueDate,
    string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal ExpectedAmount,
    Guid TermsVersionId,
    string TermsHash,
    Guid ProjectionRunId,
    Guid ProjectedCashFlowId,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence);

public sealed record BookReceiptAllocationDto(string ObligationKey,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal Amount);

/// <summary>Canonical retained cash evidence, distinct from posted accounting evidence.</summary>
public sealed record BookReceiptDto(
    string ReceiptId,
    Guid SecurityId,
    DateOnly EffectiveDate,
    string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal Amount,
    string SourceSystem,
    string SourceReference,
    IReadOnlyList<BookReceiptAllocationDto> Allocations,
    string? ReversesReceiptId,
    IReadOnlyList<string> EvidenceLinks,
    string? FlowType = null);

/// <summary>
/// Posted journal evidence linked to a receipt allocation. Drafts are never postings.
/// An inferred observation may leave ObligationKey empty when current projections no longer
/// resolve a split; only a retained journal/receipt allocation can supply those historical legs.
/// </summary>
public sealed record BookReceiptPostingDto(
    string PostingId,
    string ReceiptId,
    string ObligationKey,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal Amount,
    string Currency,
    string JournalEntryId,
    DateOnly EffectiveDate,
    bool IsReversed,
    IReadOnlyList<string> EvidenceLinks,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal? JournalCashAmount = null,
    bool IsInferredAllocation = false);

public sealed record BookReceiptCoverageDto(string Area, bool IsComplete, string? Reason = null);

public sealed record BookReceiptSourceSnapshotDto(
    IReadOnlyList<BookReceiptExpectationDto> Expectations,
    IReadOnlyList<BookReceiptDto> Receipts,
    IReadOnlyList<BookReceiptPostingDto> Postings,
    IReadOnlyList<BookReceiptCoverageDto> Coverage);

public interface IBookReceiptSource
{
    Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query, CancellationToken ct = default);
}

public sealed record BookReceiptExpectationVersionDto(
    string VersionId,
    DateOnly FirstObservedDate,
    BookReceiptExpectationDto Expectation);

public sealed record BookReceiptQueueItemDto(
    string ObligationKey,
    Guid PositionId,
    Guid SecurityId,
    string DisplayName,
    string FlowType,
    DateOnly DueDate,
    string Currency,
    string Status,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal ExpectedAmount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal ReceivedAmount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal PostedAmount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal RemainingAmount,
    int AgingDays,
    string? Owner,
    string? ExceptionId,
    bool EvidenceComplete,
    IReadOnlyList<string> EvidenceIssues,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<BookReceiptExpectationVersionDto> ExpectationHistory,
    IReadOnlyList<BookReceiptDto> Receipts,
    IReadOnlyList<BookReceiptPostingDto> Postings);

/// <summary>Immutable daily observation. Currency amounts never imply an FX conversion.</summary>
public sealed record BookReceiptRunDto(
    string RunId,
    string EngineVersion,
    BookReceiptScopeDto Scope,
    DateOnly AsOfDate,
    string InputHash,
    IReadOnlyList<BookReceiptQueueItemDto> Items,
    IReadOnlyList<BookReceiptCoverageDto> Coverage,
    BookReceiptSourceSnapshotDto SourceSnapshot,
    long Sequence = 0);

public interface IBookReceiptQueueService
{
    Task<BookReceiptRunDto> RunAsync(BookReceiptQueryDto query, CancellationToken ct = default);
    Task<IReadOnlyList<BookReceiptRunDto>> GetRunsAsync(BookReceiptScopeDto scope, CancellationToken ct = default);
    Task<BookReceiptRunDto?> GetRunAsync(BookReceiptScopeDto scope, string runId, CancellationToken ct = default);
}
