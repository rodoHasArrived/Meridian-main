using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// The entities, funds and fund accounts the calling operator may choose as an operating scope
/// (<c>GET /api/workstation/scope-options</c>), with the account → fund → entity relationships
/// between them. The host fills it only with nodes the caller is authorized to see, and drops any
/// relationship that would name a fund or entity outside that set.
/// <para>
/// Field names deliberately match the entity, fund and account summaries in
/// <see cref="Meridian.Contracts.FundStructure.OrganizationStructureGraphDto"/>, so a client can read
/// either payload with one parser. Every list may be empty; an empty list means "none visible to
/// this caller", never "not loaded".
/// </para>
/// </summary>
public sealed record WorkstationScopeOptionsDto(
    IReadOnlyList<WorkstationScopeEntityOptionDto> Entities,
    IReadOnlyList<WorkstationScopeFundOptionDto> Funds,
    IReadOnlyList<WorkstationScopeAccountOptionDto> Accounts);

/// <summary>A legal entity the caller may scope to.</summary>
public sealed record WorkstationScopeEntityOptionDto(
    Guid EntityId,
    string Name);

/// <summary>
/// A fund the caller may scope to, with the visible legal entities it holds. Entities the caller
/// may not see are left out of <see cref="EntityIds"/>.
/// </summary>
public sealed record WorkstationScopeFundOptionDto(
    Guid FundId,
    string Name,
    IReadOnlyList<Guid> EntityIds);

/// <summary>
/// A fund account the caller may scope to. <see cref="FundId"/> and <see cref="EntityId"/> are
/// null when the account has no such relationship or when it points at a fund or entity the
/// caller may not see.
/// </summary>
public sealed record WorkstationScopeAccountOptionDto(
    Guid AccountId,
    string DisplayName,
    Guid? FundId,
    Guid? EntityId);

/// <summary>
/// Provides source-generated JSON metadata for the workstation scope-option contracts. Null
/// relationships are written explicitly so clients can tell "no fund" from a missing field.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WorkstationScopeOptionsDto))]
[JsonSerializable(typeof(WorkstationScopeEntityOptionDto))]
[JsonSerializable(typeof(WorkstationScopeFundOptionDto))]
[JsonSerializable(typeof(WorkstationScopeAccountOptionDto))]
public sealed partial class WorkstationScopeOptionsJsonContext : JsonSerializerContext;
