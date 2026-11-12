using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

/// <summary>Approved sidecar evidence attributes an exact immutable legacy journal leg without rewriting it.</summary>
public sealed record LegacyIntercompanyAttributionDto(
    Guid AttributionId, long Revision, Guid LedgerBookId, Guid JournalEntryId, Guid LineId,
    string SourceLineDigest, Guid AgreementId, Guid AgreementRevisionId,
    Guid AccountBindingRevisionId, string ChartAccountNodeId, string IntercompanyTransactionId,
    string PreparedBy, string ApprovedBy, DateTimeOffset ApprovedAt, string EvidenceReference,
    string TenantId, string? CompanyId, Guid? PreviousAttributionId = null);

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
