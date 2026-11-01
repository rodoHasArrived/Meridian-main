using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.CashFlows;

/// <summary>Readable-before-writable companion. Legacy document residue never activates a contract.</summary>
public static class SecurityContractualCashFlowTerms
{
    public const string Key = "cashFlowContract";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static bool IsDeclared(string assetClass, JsonElement payload)
        => SecurityAssetTermsSchema.Field(assetClass, Key) is { } field
           && SecurityVersionedTerms.Decoding(assetClass, payload, writeMode: false).Decodes(field)
           && payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(Key, out var block)
           && SecurityAssetTermsSchema.IsPopulatedTermValue(block);

    public static ContractualCashFlowContract? Read(SecurityDetailDto security)
    {
        SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(security.AssetClass, security.AssetSpecificTerms);
        if (!IsDeclared(security.AssetClass, security.AssetSpecificTerms))
            return null;
        var block = security.AssetSpecificTerms.GetProperty(Key);
        var contract = block.Deserialize<ContractualCashFlowContract>(Json)
            ?? throw new JsonException("Declared cashFlowContract must contain a versioned contract.");
        if (contract.Version != 1)
            throw new JsonException("Unsupported cashFlowContract version.");
        if (!string.Equals(contract.Currency, security.Currency, StringComparison.OrdinalIgnoreCase))
            throw new JsonException("Contract companion currency differs from the security currency.");
        return contract;
    }
}
