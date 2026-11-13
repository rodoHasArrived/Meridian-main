using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.FundStructure;

/// <summary>Shared authoritative entity inspection; clients render it without rebuilding business state.</summary>
public sealed record EntityTimelineDto(Guid OrganizationId, LegalEntitySummaryDto Entity, DateTimeOffset AsOf,
    IReadOnlyList<EntityLifecycleRevisionDto> LifecycleRevisions,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks, IReadOnlyList<IntercompanyAgreementDetailDto> Agreements,
    IReadOnlyList<ResolvedIntercompanyAccountingArrangementDto> AccountingArrangements,
    IReadOnlyList<LedgerBookDto> Books, IReadOnlyList<string> Warnings);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EntityTimelineDto))]
[JsonSerializable(typeof(EntityLifecycleRevisionDto))]
[JsonSerializable(typeof(EntityLifecycleRevisionDto[]))]
public partial class EntityTimelineJsonContext : JsonSerializerContext;
