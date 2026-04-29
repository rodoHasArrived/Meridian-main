using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
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
[JsonSerializable(typeof(List<CreateSecurityRequest>))]
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
[JsonSerializable(typeof(SecurityMasterIngestStatusResponse))]
[JsonSerializable(typeof(SecurityMasterActiveImportStatusResponse))]
[JsonSerializable(typeof(SecurityMasterCompletedImportStatusResponse))]
[JsonSerializable(typeof(EdgarIngestRequest))]
[JsonSerializable(typeof(EdgarIngestResult))]
[JsonSerializable(typeof(EdgarFilerRecord))]
[JsonSerializable(typeof(EdgarFilerRecord[]))]
[JsonSerializable(typeof(List<EdgarFilerRecord>))]
[JsonSerializable(typeof(EdgarAddress))]
[JsonSerializable(typeof(EdgarFormerName))]
[JsonSerializable(typeof(EdgarTickerAssociation))]
[JsonSerializable(typeof(EdgarTickerAssociation[]))]
[JsonSerializable(typeof(List<EdgarTickerAssociation>))]
[JsonSerializable(typeof(EdgarFilingSummary))]
[JsonSerializable(typeof(EdgarSecurityDataRecord))]
[JsonSerializable(typeof(EdgarDebtOfferingTerms))]
[JsonSerializable(typeof(EdgarDebtOfferingTerms[]))]
[JsonSerializable(typeof(List<EdgarDebtOfferingTerms>))]
[JsonSerializable(typeof(EdgarFundHolding))]
[JsonSerializable(typeof(EdgarFundHolding[]))]
[JsonSerializable(typeof(List<EdgarFundHolding>))]
[JsonSerializable(typeof(EdgarXbrlFact))]
[JsonSerializable(typeof(EdgarXbrlFact[]))]
[JsonSerializable(typeof(List<EdgarXbrlFact>))]
[JsonSerializable(typeof(IssuerFactSnapshot))]
[JsonSerializable(typeof(EdgarFactsRecord))]
[JsonSerializable(typeof(EdgarReferenceDataManifest))]
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
