using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Core.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(SecurityAliasDto))]
[JsonSerializable(typeof(SecurityAliasDto[]))]
[JsonSerializable(typeof(List<SecurityAliasDto>))]
[JsonSerializable(typeof(SecurityDetailDto))]
[JsonSerializable(typeof(SecurityEconomicDefinitionRecord))]
[JsonSerializable(typeof(SecurityIdentifierDto))]
[JsonSerializable(typeof(SecurityIdentifierDto[]))]
[JsonSerializable(typeof(List<SecurityIdentifierDto>))]
[JsonSerializable(typeof(PreferredEquityTermsDto))]
[JsonSerializable(typeof(ConvertibleEquityTermsDto))]
[JsonSerializable(typeof(TradingParametersDto))]
[JsonSerializable(typeof(CorporateActionDto))]
[JsonSerializable(typeof(CorporateActionDto[]))]
[JsonSerializable(typeof(List<CorporateActionDto>))]
[JsonSerializable(typeof(SecurityMasterEventEnvelope))]
[JsonSerializable(typeof(SecurityMasterEventEnvelope[]))]
[JsonSerializable(typeof(List<SecurityMasterEventEnvelope>))]
[JsonSerializable(typeof(SecurityProjectionRecord))]
[JsonSerializable(typeof(SecuritySearchRequest))]
[JsonSerializable(typeof(SecuritySnapshotRecord))]
[JsonSerializable(typeof(SecuritySummaryDto))]
[JsonSerializable(typeof(SecuritySummaryDto[]))]
[JsonSerializable(typeof(List<SecuritySummaryDto>))]
[JsonSerializable(typeof(CreateSecurityRequest))]
[JsonSerializable(typeof(AmendSecurityTermsRequest))]
[JsonSerializable(typeof(AmendPreferredEquityTermsRequest))]
[JsonSerializable(typeof(AmendConvertibleEquityTermsRequest))]
[JsonSerializable(typeof(DeactivateSecurityRequest))]
[JsonSerializable(typeof(ResolveSecurityRequest))]
[JsonSerializable(typeof(UpsertSecurityAliasRequest))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(SecurityClassificationSummaryDto))]
[JsonSerializable(typeof(SecurityEconomicDefinitionSummaryDto))]
[JsonSerializable(typeof(SecurityIdentityDrillInDto))]
[JsonSerializable(typeof(SecurityMasterWorkstationDto))]
[JsonSerializable(typeof(SecurityMasterWorkstationDto[]))]
[JsonSerializable(typeof(List<SecurityMasterWorkstationDto>))]
public partial class SecurityMasterJsonContext : JsonSerializerContext
{
    public static readonly JsonSerializerOptions HighPerformanceOptions = new()
    {
        TypeInfoResolver = Default,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
