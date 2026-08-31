using System.Text.Json;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.AssetOperations;

public static class InstrumentRoleKinds
{
    public const string Holder = "Holder";
    public const string Issuer = "Issuer";
    public const string Lender = "Lender";
    public const string Borrower = "Borrower";
    public const string Payer = "Payer";
    public const string Receiver = "Receiver";
}

public static class InstrumentAccountingSides
{
    public const string Debit = "Debit";
    public const string Credit = "Credit";
}

public static class InstrumentEconomicSides
{
    public const string Asset = "Asset";
    public const string Liability = "Liability";
    public const string Inflow = "Inflow";
    public const string Outflow = "Outflow";
}

public static class BookPositionSides
{
    public const string Long = "Long";
    public const string Short = "Short";
    public const string Asset = "Asset";
    public const string Liability = "Liability";
}

public sealed record InstrumentRoleDto(
    Guid RoleId,
    Guid SecurityId,
    string OwnerScopeId,
    string OwnerScopeKind,
    string RoleKind,
    string AccountingSide,
    string EconomicSide,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    string? CounterpartyId = null,
    string? DefaultAccountId = null,
    long Version = 1,
    EconomicEventReferenceDto? OriginEvent = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    JsonElement? ExtensionPayload = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } = [];
}

public sealed record BookPositionDto(
    Guid PositionId,
    Guid SecurityId,
    Guid RoleId,
    AccountingBookContextDto BookContext,
    string PositionSide,
    string Status,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    long Version = 1,
    string? PrimaryAccountId = null,
    PositionEconomicStateDto? CurrentEconomicState = null,
    EconomicEventReferenceDto? OriginEvent = null,
    ProjectionLineageDto? ProjectionLineage = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    JsonElement? ExtensionPayload = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } = [];
}

public sealed record PositionEconomicStateDto(
    Guid EconomicStateId,
    Guid PositionId,
    DateOnly AsOfDate,
    string Currency,
    long Version,
    decimal? Quantity = null,
    decimal? ParAmount = null,
    decimal? NotionalAmount = null,
    decimal? OriginalFaceAmount = null,
    decimal? CurrentFaceAmount = null,
    decimal? UnitCost = null,
    decimal? CarryingAmount = null,
    decimal? PurchasePrice = null,
    DateOnly? TradeDate = null,
    DateOnly? SettlementDate = null,
    decimal? Rate = null,
    decimal? PriorFactor = null,
    decimal? CurrentFactor = null,
    EconomicEventReferenceDto? SourceEvent = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    JsonElement? ExtensionPayload = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public ProjectionLineageDto? ProjectionLineage { get; init; }

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } = [];
}

public sealed record EconomicEventReferenceDto(
    Guid EventId,
    string EventType,
    long EventVersion,
    DateOnly EffectiveDate,
    DateTimeOffset OccurredAtUtc,
    string SourceDomain,
    string? SourceEntityId = null,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    string? SourceContentHash = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public Guid? SecurityId { get; init; }

    public Guid? BookPositionId { get; init; }

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } = [];
}

public sealed record ProjectionLineageDto(
    Guid ProjectionRunId,
    Guid? ProjectionEventId,
    string ModelKey,
    string ModelVersion,
    string EngineVersion,
    string Scenario,
    DateOnly ProjectionAsOfDate,
    DateTimeOffset GeneratedAtUtc,
    string SourceDomain,
    string? SourceEntityId,
    EconomicEventReferenceDto TriggerEvent,
    string? TermsVersion = null,
    string? TermsHash = null,
    Guid? SupersededRunId = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public Guid? BookPositionId { get; init; }

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } = [];
}
