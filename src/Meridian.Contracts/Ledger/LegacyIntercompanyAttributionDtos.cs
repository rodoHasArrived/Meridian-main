using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<LegacyIntercompanyReviewModeDto>))]
public enum LegacyIntercompanyReviewModeDto { Independent, OperatorReviewed }

/// <summary>Approved sidecar evidence attributes an exact immutable legacy journal leg without rewriting it.</summary>
public sealed record LegacyIntercompanyAttributionDto(
    Guid AttributionId, long Revision, Guid LedgerBookId, Guid JournalEntryId, Guid LineId,
    string SourceLineDigest, Guid AccountingArrangementId, Guid AccountingArrangementRevisionId,
    Guid AccountBindingRevisionId, string ChartAccountNodeId, string IntercompanyTransactionId,
    string PreparedBy, string ApprovedBy, DateTimeOffset ApprovedAt, string EvidenceReference,
    string TenantId, string? CompanyId, Guid? PreviousAttributionId = null)
{
    public Guid IntercompanyAgreementId { get; init; }
    public int IntercompanyAgreementVersion { get; init; }
    public string IntercompanyAgreementPayloadHash { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public LegacyIntercompanyReviewModeDto ReviewMode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewRationale { get; init; }
    /// <summary>A reviewed successor retracts this exact assertion; the posted source and earlier history remain immutable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Revoked { get; init; }
}

public interface ILegacyIntercompanyAttributionStore
{
    Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> ListAsync(Guid ledgerBookId, CancellationToken ct = default);
    Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> HistoryAsync(Guid ledgerBookId, CancellationToken ct = default);
    Task<LegacyIntercompanyAttributionDto> AppendAsync(LegacyIntercompanyAttributionDto attribution,
        Guid? expectedPreviousAttributionId, CancellationToken ct = default);
    Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default);
}

public sealed record AppendLegacyIntercompanyAttributionRequest(
    LegacyIntercompanyAttributionDto Attribution, Guid? ExpectedPreviousAttributionId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LegacyIntercompanyAttributionDto))]
[JsonSerializable(typeof(IReadOnlyList<LegacyIntercompanyAttributionDto>))]
[JsonSerializable(typeof(AppendLegacyIntercompanyAttributionRequest))]
public sealed partial class LegacyIntercompanyAttributionJsonContext : JsonSerializerContext;
