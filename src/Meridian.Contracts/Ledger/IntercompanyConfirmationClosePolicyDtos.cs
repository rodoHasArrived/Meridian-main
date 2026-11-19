using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyConfirmationClosePolicyStatusDto>))]
public enum IntercompanyConfirmationClosePolicyStatusDto { Proposed, Active, Rejected, Disabled }

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyConfirmationClosePolicyActionDto>))]
public enum IntercompanyConfirmationClosePolicyActionDto { Propose, Approve, Reject, Disable }

public sealed record IntercompanyConfirmationClosePolicyEventDto(Guid EventId, Guid RequestId, IntercompanyConfirmationClosePolicyActionDto Action,
    string Actor, DateTimeOffset RecordedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

/// <summary>
/// An opt-in, reviewed close policy for one ledger book (ADR-032). While active, the listed reciprocal balance
/// confirmation findings at the close period end block close readiness. Without an active policy, confirmation stays
/// advisory. Retained as append-only versions; disabling restores advisory-only behaviour.
/// </summary>
public sealed record IntercompanyConfirmationClosePolicyDto(Guid PolicyId, string TenantId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompanyId,
    Guid LedgerBookId, IReadOnlyList<string> BlockingCodes, long Version, IntercompanyConfirmationClosePolicyStatusDto Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? ActiveFrom,
    IReadOnlyList<IntercompanyConfirmationClosePolicyEventDto> Events);

/// <summary>
/// Proposes a policy. <paramref name="BlockingCodes"/> are confirmation finding codes without the
/// <c>intercompany.confirmation.</c> prefix, for example <c>disputed</c>.
/// </summary>
public sealed record ProposeIntercompanyConfirmationClosePolicyRequest(Guid RequestId, Guid LedgerBookId,
    IReadOnlyList<string> BlockingCodes, string Reason);

/// <summary>Approves, rejects or disables exactly the version the reviewer saw.</summary>
public sealed record DecideIntercompanyConfirmationClosePolicyRequest(Guid RequestId, IntercompanyConfirmationClosePolicyActionDto Action,
    long ExpectedVersion, string? Reason = null);

/// <summary>Append-only, tenant-scoped close-policy versions.</summary>
public interface IIntercompanyConfirmationClosePolicyStore
{
    Task<IntercompanyConfirmationClosePolicyDto?> GetAsync(string tenantId, Guid policyId, CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyConfirmationClosePolicyDto>> ListAsync(string tenantId, Guid? ledgerBookId = null, CancellationToken ct = default);
    Task<IntercompanyConfirmationClosePolicyDto> AppendAsync(IntercompanyConfirmationClosePolicyDto next, long expectedVersion, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IntercompanyConfirmationClosePolicyDto))]
[JsonSerializable(typeof(IntercompanyConfirmationClosePolicyDto[]))]
[JsonSerializable(typeof(IReadOnlyList<IntercompanyConfirmationClosePolicyDto>))]
[JsonSerializable(typeof(ProposeIntercompanyConfirmationClosePolicyRequest))]
[JsonSerializable(typeof(DecideIntercompanyConfirmationClosePolicyRequest))]
public sealed partial class IntercompanyConfirmationClosePolicyJsonContext : JsonSerializerContext;
