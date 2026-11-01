namespace Meridian.DataIntegration.Credentials;

/// <summary>Shared DI keys for credential stores with an explicit runtime ownership policy.</summary>
public static class ProviderCredentialStoreServiceKeys
{
    /// <summary>A guarded provider-wide store that refuses every retained owned connection.</summary>
    public const string LegacyRuntime = "legacy-provider-runtime";
}
