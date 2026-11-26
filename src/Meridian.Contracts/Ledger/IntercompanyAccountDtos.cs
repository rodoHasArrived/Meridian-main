using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccountRoleDto>))]
public enum IntercompanyAccountRoleDto
{
    Receivable = 0,
    Payable = 1,
    Revenue = 2,
    Expense = 3,
    TranslationReserve = 4
}

/// <summary>
/// An immutable, effective-dated assignment of a stable chart node to an intercompany role.
/// Historical names identify old immutable journal legs; they must never be reused for another node.
/// </summary>
public sealed record IntercompanyAccountBindingDto(
    Guid BindingId,
    Guid RevisionId,
    long RevisionNumber,
    Guid LedgerBookId,
    string ChartAccountNodeId,
    IntercompanyAccountRoleDto Role,
    string AccountPath,
    string AccountName,
    string AccountType,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<string> HistoricalNames,
    string TenantId,
    string? CompanyId,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    string Reason,
    Guid? PreviousRevisionId = null,
    bool IsArchived = false);

/// <summary>The server resolves account facts from the chart and stamps authenticated audit scope.</summary>
public sealed record AppendIntercompanyAccountBindingRequest(
    Guid BindingId,
    Guid RevisionId,
    Guid LedgerBookId,
    string ChartAccountNodeId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<string> HistoricalNames,
    string Reason,
    Guid? ExpectedPreviousRevisionId = null,
    bool IsArchived = false);

/// <summary>Retained account authority shared by posting, consolidation and both workstations.</summary>
public interface IIntercompanyAccountBindingStore
{
    Task<IReadOnlyList<IntercompanyAccountBindingDto>> ListAsync(Guid ledgerBookId, DateOnly asOf,
        CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyAccountBindingDto>> HistoryAsync(Guid ledgerBookId,
        CancellationToken ct = default);
    Task<IntercompanyAccountBindingDto?> GetRevisionAsync(Guid revisionId, CancellationToken ct = default);
    Task<IntercompanyAccountBindingDto> AppendAsync(IntercompanyAccountBindingDto revision,
        Guid? expectedPreviousRevisionId, CancellationToken ct = default);

    /// <summary>Holds mapping mutations until the consumer's financial transaction commits.</summary>
    Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntercompanyAccountBindingDto))]
[JsonSerializable(typeof(IntercompanyAccountBindingDto[]))]
[JsonSerializable(typeof(IReadOnlyList<IntercompanyAccountBindingDto>))]
[JsonSerializable(typeof(AppendIntercompanyAccountBindingRequest))]
public partial class IntercompanyAccountContractsJsonContext : JsonSerializerContext;
