using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

/// <summary>Actual posted due-to/due-from balances. Terms never supply accounting amounts.</summary>
public sealed record IntercompanyBalanceScheduleRowDto(Guid LedgerBookId, Guid EntityId, Guid? CounterpartyEntityId,
    Guid? AccountingArrangementId, Guid? AccountingArrangementRevisionId, string? AccountingTemplateKind, string? ChartAccountNodeId,
    IntercompanyAccountRoleDto? Role, string TransactionCurrency, string FunctionalCurrency,
    decimal TransactionSignedBalance, decimal FunctionalSignedBalance, bool MappingVerified,
    IReadOnlyList<Guid> JournalEntryIds, IReadOnlyList<string> BindingRevisionIds)
{
    public Guid? IntercompanyAgreementId { get; init; }
    public int? IntercompanyAgreementVersion { get; init; }
    public string? IntercompanyAgreementPayloadHash { get; init; }
}

public sealed record IntercompanyBalanceScheduleIssueDto(string Code, string Message, bool BlocksMapping,
    Guid? LedgerBookId = null, Guid? JournalEntryId = null);

public sealed record IntercompanyBalanceScheduleDto(string TenantId, string? CompanyId, string? FundProfileId,
    Guid? LedgerBookId, DateOnly AsOf, bool Available, IReadOnlyList<IntercompanyBalanceScheduleRowDto> Rows,
    IReadOnlyList<IntercompanyBalanceScheduleIssueDto> Issues, string ContentHash)
{
    public IReadOnlyList<Guid> LegacyAttributionIds { get; init; } = [];
    public bool IsMappingComplete => Available && Issues.All(x => !x.BlocksMapping) && Rows.All(x => x.MappingVerified);
    public bool IsBalanced => Issues.All(x => x.Code != "IntercompanyReciprocalBalanceMismatch");
    public bool IsReady => IsMappingComplete && IsBalanced;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntercompanyBalanceScheduleDto))]
public partial class IntercompanyBalanceScheduleJsonContext : JsonSerializerContext;
