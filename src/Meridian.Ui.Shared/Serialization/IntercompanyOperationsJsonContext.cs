using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Serialization;

/// <summary>Generated transport metadata for lifecycle and intercompany control operations.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LedgerBookControlProfileDto))]
[JsonSerializable(typeof(LedgerBookControlPreflightDto))]
[JsonSerializable(typeof(LedgerControlDecisionDto[]))]
[JsonSerializable(typeof(ActivateLedgerBookControlsRequestDto))]
[JsonSerializable(typeof(ControlledCloseAuthorizationRequestDto))]
[JsonSerializable(typeof(ControlledCloseAuthorizationDto))]
[JsonSerializable(typeof(EntityLifecycleRevisionDto))]
[JsonSerializable(typeof(EntityLifecycleRevisionDto[]))]
[JsonSerializable(typeof(IReadOnlyList<EntityLifecycleRevisionDto>))]
[JsonSerializable(typeof(EntityLifecycleEligibilityDto))]
[JsonSerializable(typeof(AppendEntityLifecycleRevisionRequestDto))]
public partial class IntercompanyOperationsJsonContext : JsonSerializerContext;
