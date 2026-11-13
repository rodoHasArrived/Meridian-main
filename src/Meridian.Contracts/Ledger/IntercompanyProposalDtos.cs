using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyProposalStatusDto>))]
public enum IntercompanyProposalStatusDto { Pending, NeedsFix, Drafted, Linked }

public sealed record IntercompanyProposalSourceLineDto(Guid LineId, string AccountName, string AccountType,
    string? ChartAccountNodeId, decimal Debit, decimal Credit, string Currency,
    decimal TransactionDebit, decimal TransactionCredit);

/// <summary>Immutable source facts retained atomically with the source journal.</summary>
public sealed record IntercompanyProposalSourceDto(Guid ProposalId, string TenantId, string? CompanyId,
    Guid SourceJournalId, Guid SourceBookId, Guid SourceEntityId, Guid CounterpartyEntityId,
    Guid AccountingArrangementId, Guid AccountingArrangementRevisionId, string TransactionId, DateOnly EffectiveDate,
    string SourceFunctionalCurrency, IReadOnlyList<IntercompanyProposalSourceLineDto> Lines,
    Guid? ReversesJournalId = null, string RuleVersion = "intercompany-mirror-v1")
{
    /// <summary>Unattributed contra legs cannot be allocated between multiple retained event groups.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresSourceAllocation { get; init; }
    public Guid IntercompanyAgreementId { get; init; }
    public int IntercompanyAgreementVersion { get; init; }
    public string IntercompanyAgreementPayloadHash { get; init; } = string.Empty;
}

public sealed record IntercompanyProposalDto(IntercompanyProposalSourceDto Source, string SourceDigest,
    IntercompanyProposalStatusDto Status, int Attempts, DateTimeOffset RecordedAt,
    Guid? TargetBookId = null, Guid? TargetJournalId = null, string? LastError = null,
    string? ClaimToken = null, DateTimeOffset? LeaseUntil = null, DateOnly? DueDate = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IntercompanyCounterpartLinkDto? LatestLink { get; init; }
    public Guid? ResolvedTargetBookId => LatestLink?.TargetBookId ?? TargetBookId;
    public Guid? ResolvedTargetJournalId => LatestLink?.TargetJournalId ?? TargetJournalId;
}

public sealed record IntercompanyCounterpartLinkDto(string TenantId, Guid RequestId, Guid ProposalId,
    Guid TargetBookId, Guid TargetJournalId, string Actor, string Reason, DateTimeOffset RecordedAt,
    Guid? RejectedDraftId = null, int? RejectedDraftVersion = null);

/// <summary>Server-captured rejection checked while holding the journal mutation authority lease.</summary>
public sealed record RejectedIntercompanyDraftEvidenceDto(Guid LedgerBookId, Guid JournalEntryId,
    int Version, Guid ProposalId, string SourceDigest);

public sealed record IntercompanyBreakDto(Guid ProposalId, Guid AccountingArrangementId, string TransactionId,
    string Classification, DateOnly AgeFrom, int AgeDays, string AgingBasis, string Detail,
    Guid SourceJournalId, Guid? TargetJournalId)
{
    public Guid IntercompanyAgreementId { get; init; }
    public int IntercompanyAgreementVersion { get; init; }
    public string IntercompanyAgreementPayloadHash { get; init; } = string.Empty;
}

public sealed record LinkIntercompanyCounterpartRequest(Guid RequestId, Guid TargetBookId,
    Guid TargetJournalId, string Reason);

public sealed record RetryIntercompanyProposalRequest(Guid RequestId, string Reason);
public sealed record IntercompanyRefreshPreviewDto(ManualJournalEntryDraftDto Draft, string Fingerprint,
    ManualJournalEntryDraftDto? CurrentDraft = null);
public sealed record RefreshIntercompanyProposalRequest(Guid RequestId, int ExpectedDraftVersion,
    string PreviewFingerprint, string Reason);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IntercompanyProposalDto))]
[JsonSerializable(typeof(IntercompanyProposalDto[]))]
[JsonSerializable(typeof(IntercompanyProposalSourceDto))]
[JsonSerializable(typeof(IntercompanyBreakDto[]))]
[JsonSerializable(typeof(LinkIntercompanyCounterpartRequest))]
[JsonSerializable(typeof(RetryIntercompanyProposalRequest))]
[JsonSerializable(typeof(IntercompanyRefreshPreviewDto))]
[JsonSerializable(typeof(RefreshIntercompanyProposalRequest))]
[JsonSerializable(typeof(IntercompanyCounterpartLinkDto))]
public partial class IntercompanyProposalJsonContext : JsonSerializerContext;
