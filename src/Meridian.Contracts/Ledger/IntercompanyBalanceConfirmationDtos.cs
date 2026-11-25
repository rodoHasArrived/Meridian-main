using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

/// <summary>
/// Where one reciprocal confirmation stands. Advisory unless the book adopts a reviewed confirmation close policy
/// (ADR-032), which may treat named findings as close blockers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyBalanceConfirmationStatusDto>))]
public enum IntercompanyBalanceConfirmationStatusDto
{
    /// <summary>Both sides' posted balances are pinned and await each side's confirmation.</summary>
    Proposed,
    ConfirmedByPartyOne,
    ConfirmedByPartyTwo,
    /// <summary>Both sides confirmed the same pinned balances.</summary>
    Confirmed,
    /// <summary>A side disputes the pinned balances; only a reasoned resolution or a fresh proposal follows.</summary>
    Disputed,
    /// <summary>
    /// The dispute was settled by review; a fresh proposal confirms the corrected balances. When the two sides belong
    /// to different companies of the tenant, each side records its own resolution before this status is reached.
    /// </summary>
    Resolved
}

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyBalanceConfirmationActionDto>))]
public enum IntercompanyBalanceConfirmationActionDto
{
    Propose,
    Confirm,
    Dispute,
    Resolve
}

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyBalanceConfirmationPartyDto>))]
public enum IntercompanyBalanceConfirmationPartyDto
{
    PartyOne,
    PartyTwo
}

/// <summary>
/// One side's posted due-to/due-from balance for the agreement pair at the as-of date, summed from its own
/// book's balance schedule rows. Amounts come only from posted journals; nothing is entered by an operator.
/// </summary>
public sealed record IntercompanyBalanceConfirmationSideDto(Guid EntityId, Guid LedgerBookId, string TransactionCurrency,
    decimal TransactionSignedBalance, string FunctionalCurrency, decimal FunctionalSignedBalance, string ScheduleContentHash,
    IReadOnlyList<Guid> JournalEntryIds);

/// <summary>
/// One retained step. Each step pins the balance digest it acted on and the request identity that made it, so an
/// identical retry returns the retained result instead of acting twice.
/// </summary>
public sealed record IntercompanyBalanceConfirmationEventDto(Guid EventId, Guid RequestId, IntercompanyBalanceConfirmationActionDto Action,
    IntercompanyBalanceConfirmationPartyDto? Party, string Actor, DateTimeOffset RecordedAt, string BalanceDigest, string? Reason);

/// <summary>
/// A per-period reciprocal balance confirmation for one approved accounting profile. Party one and party two
/// follow the profile's legal agreement. <see cref="NetTransactionBalance"/> is the sum of both sides' signed
/// transaction-currency balances; reciprocal books net to zero.
/// </summary>
/// <remarks>
/// <see cref="CompanyId"/> is the company that first proposed the confirmation and never changes. When the two books
/// belong to different companies of the tenant, <see cref="PartyOneCompanyId"/> and <see cref="PartyTwoCompanyId"/>
/// name each side's owning company; the confirmation is visible to both and each side acts only through its own
/// company. Records retained before cross-company support carry neither and belong to <see cref="CompanyId"/> alone.
/// </remarks>
public sealed record IntercompanyBalanceConfirmationDto(Guid ConfirmationId, string TenantId, string? CompanyId,
    Guid AccountingArrangementId, Guid IntercompanyAgreementId, DateOnly AsOf, long Version,
    IntercompanyBalanceConfirmationStatusDto Status, IntercompanyBalanceConfirmationSideDto PartyOne,
    IntercompanyBalanceConfirmationSideDto PartyTwo, decimal NetTransactionBalance, string BalanceDigest,
    IReadOnlyList<IntercompanyBalanceConfirmationEventDto> Events)
{
    public string? PartyOneCompanyId { get; init; }
    public string? PartyTwoCompanyId { get; init; }

    /// <summary>True when the two sides' books belong to different companies of the tenant.</summary>
    [JsonIgnore]
    public bool IsCrossCompany => (PartyOneCompanyId is not null || PartyTwoCompanyId is not null)
        && !string.Equals(PartyOneCompanyId, PartyTwoCompanyId, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="companyId"/> may see this confirmation: the proposer's or either side's company.</summary>
    public bool IsVisibleTo(string? companyId)
        => string.Equals(CompanyId, companyId, StringComparison.OrdinalIgnoreCase)
            || (IsCrossCompany && (string.Equals(PartyOneCompanyId, companyId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(PartyTwoCompanyId, companyId, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>Pins both sides' current posted balances for the profile at the as-of date.</summary>
public sealed record ProposeIntercompanyBalanceConfirmationRequest(Guid RequestId, Guid AccountingArrangementId, DateOnly AsOf);

/// <summary>
/// Confirms, disputes or resolves the exact retained version. <see cref="ExpectedBalanceDigest"/> is the digest the
/// operator saw; a confirmation is refused if the balances moved since.
/// </summary>
public sealed record IntercompanyBalanceConfirmationActionRequest(Guid RequestId, IntercompanyBalanceConfirmationActionDto Action,
    IntercompanyBalanceConfirmationPartyDto? Party, long ExpectedVersion, string ExpectedBalanceDigest, string? Reason);

/// <summary>
/// Pre-fills an ordinary journal-workbench draft that corrects one side after a dispute was resolved. The draft is
/// never submitted, approved or posted automatically. <see cref="AdjustmentAmount"/> is in the agreement currency:
/// a positive amount debits the side's intercompany account named by <see cref="Role"/>, a negative amount credits
/// it, and <see cref="OffsetAccountNodeId"/> takes the opposite side. After the correction posts, propose again.
/// </summary>
public sealed record CreateIntercompanyConfirmationAdjustmentDraftRequest(Guid RequestId, IntercompanyBalanceConfirmationPartyDto Party,
    long ExpectedVersion, string ExpectedBalanceDigest, IntercompanyAccountRoleDto Role, decimal AdjustmentAmount,
    string OffsetAccountNodeId, DateOnly AccountingDate, string? Memo = null);

/// <summary>
/// The confirmation position for a ledger book's approved profiles at an as-of date, for close readiness.
/// <see cref="Findings"/> are advisory unless the book adopts a reviewed confirmation close policy (ADR-032).
/// </summary>
public sealed record IntercompanyBalanceConfirmationSummaryDto(Guid LedgerBookId, DateOnly AsOf,
    IReadOnlyList<IntercompanyBalanceConfirmationDto> Confirmations, IReadOnlyList<IntercompanyBalanceConfirmationFindingDto> Findings)
{
    public bool AllConfirmed => Findings.Count == 0;
}

/// <summary>
/// Codes: NotProposed, AwaitingConfirmation, Disputed, BalancesChanged, ResolvedAwaitingProposal, Unavailable.
/// </summary>
public sealed record IntercompanyBalanceConfirmationFindingDto(string Code, string Message, Guid AccountingArrangementId,
    Guid? ConfirmationId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntercompanyBalanceConfirmationDto))]
[JsonSerializable(typeof(IntercompanyBalanceConfirmationDto[]))]
[JsonSerializable(typeof(IReadOnlyList<IntercompanyBalanceConfirmationDto>))]
[JsonSerializable(typeof(IntercompanyBalanceConfirmationSummaryDto))]
[JsonSerializable(typeof(ProposeIntercompanyBalanceConfirmationRequest))]
[JsonSerializable(typeof(IntercompanyBalanceConfirmationActionRequest))]
[JsonSerializable(typeof(CreateIntercompanyConfirmationAdjustmentDraftRequest))]
[JsonSerializable(typeof(IntercompanyBalanceConfirmationEventDto[]))]
public partial class IntercompanyBalanceConfirmationJsonContext : JsonSerializerContext;

/// <summary>
/// Durable, append-only confirmation history scoped to the caller's tenant and company. A cross-company confirmation
/// is also visible to either side's company. Each append adds the next version of one confirmation; a stale expected
/// version is refused rather than overwritten.
/// </summary>
public interface IIntercompanyBalanceConfirmationStore
{
    Task<IntercompanyBalanceConfirmationDto?> GetAsync(Guid confirmationId, string? companyId, CancellationToken ct = default);
    /// <summary>The latest version of the confirmation for one accounting profile and as-of date, if any.</summary>
    Task<IntercompanyBalanceConfirmationDto?> FindAsync(Guid accountingArrangementId, DateOnly asOf, string? companyId, CancellationToken ct = default);
    /// <summary>
    /// Appends <paramref name="next"/> as version <paramref name="expectedVersion"/> + 1. Version 1 creates the
    /// confirmation and is refused when one already exists for the same profile and date.
    /// </summary>
    Task<IntercompanyBalanceConfirmationDto> AppendAsync(IntercompanyBalanceConfirmationDto next, long expectedVersion, CancellationToken ct = default);
}
