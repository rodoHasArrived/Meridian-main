using System.Text.Json.Serialization;

namespace Meridian.Contracts.Extensibility;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CoreFinancialObjectKindDto))]
[JsonSerializable(typeof(ExtensibilityConfigurationAreaDto))]
[JsonSerializable(typeof(ExtensibilityConfigurationStatusDto))]
[JsonSerializable(typeof(ExtensibilityScopeKindDto))]
[JsonSerializable(typeof(GovernedFoundationKindDto))]
[JsonSerializable(typeof(ExtensibilityValidationSeverityDto))]
[JsonSerializable(typeof(StableCoreObjectContractDto))]
[JsonSerializable(typeof(IReadOnlyList<StableCoreObjectContractDto>))]
[JsonSerializable(typeof(ExtensibilityLayerContractDto))]
[JsonSerializable(typeof(IReadOnlyList<ExtensibilityLayerContractDto>))]
[JsonSerializable(typeof(GovernedFoundationContractDto))]
[JsonSerializable(typeof(IReadOnlyList<GovernedFoundationContractDto>))]
[JsonSerializable(typeof(ExtensibilityScopeDto))]
[JsonSerializable(typeof(ExtensibilityValidationIssueDto))]
[JsonSerializable(typeof(IReadOnlyList<ExtensibilityValidationIssueDto>))]
[JsonSerializable(typeof(ExtensibilityConfigurationEnvelopeDto))]
[JsonSerializable(typeof(IReadOnlyList<ExtensibilityConfigurationEnvelopeDto>))]
[JsonSerializable(typeof(DomainExtensionDescriptorDto))]
[JsonSerializable(typeof(IReadOnlyList<DomainExtensionDescriptorDto>))]
[JsonSerializable(typeof(TenantTemplateConfigurationBundleDto))]
[JsonSerializable(typeof(ExtensibilityActivationReadinessDto))]
[JsonSerializable(typeof(ExtensibilityRegistrationDto))]
[JsonSerializable(typeof(IReadOnlyList<ExtensibilityRegistrationDto>))]
[JsonSerializable(typeof(ExtensibilityCatalogDto))]
public sealed partial class CoreExtensibilityContractsJsonContext : JsonSerializerContext;
