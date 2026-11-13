using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

/// <summary>Prevents workstation configuration writes from persisting provider secrets outside the owned vault.</summary>
internal static class ProviderConfigurationCredentialGuard
{
    private static readonly HashSet<string> CredentialFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "KeyId", "SecretKey", "ApiKey", "ApiSecret", "ApiToken", "AccessToken", "RefreshToken",
        "ClientId", "ClientSecret", "Secret", "Token", "Password"
    };

    private static readonly HashSet<string> CredentialEnvironmentKeys = ProviderCredentialCatalog.All
        .SelectMany(provider => provider.RequiredFields)
        .SelectMany(field => field.EnvironmentNames)
        .Concat(["NYSE_API_KEY", "NYSE_API_SECRET", "NYSE_CLIENT_ID"])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static AppConfig Prepare(AppConfig candidate, ProviderCredentialOwnershipPolicy policy)
    {
        if (!policy.RequireExplicitOwnership)
            return candidate;

        // Inspect the final snapshot inside the configuration update lease. A secret-free partial
        // request must not re-publish credentials retained in another section or extension object.
        using var document = JsonSerializer.SerializeToDocument(candidate, AppConfigJsonOptions.Write);
        if (ContainsCredentials(document.RootElement))
            throw new ProviderConfigurationCredentialException();
        // Extension objects and provider settings use mutable dictionaries. Retain an owned copy
        // of exactly the validated snapshot, including when SaveAsync waits for another writer.
        return document.RootElement.Deserialize<AppConfig>(AppConfigJsonOptions.Read)!;
    }

    private static bool ContainsCredentials(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in root.EnumerateObject())
        {
            var path = property.Name.Split(':');
            if (path.Length > 1 && IsCredentialPath(path) && HasValue(property.Value))
                return true;
            if (CredentialEnvironmentKeys.Contains(property.Name) && HasValue(property.Value))
                return true;
            if (IsProvider(property.Name) && ProviderHasCredentials(property.Value))
                return true;
            if (Same(property.Name, "Backfill") && TryProperty(property.Value, "Providers", out var backfill)
                && ProviderMapHasCredentials(backfill))
                return true;
            if (Same(property.Name, "Providers") && ProviderMapHasCredentials(property.Value))
                return true;
            if (Same(property.Name, "DataSources") && TryProperty(property.Value, "Sources", out var sources)
                && sources.ValueKind == JsonValueKind.Array && sources.EnumerateArray().Any(ContainsCredentials))
                return true;
            if (Same(property.Name, "ProviderModules") && TryProperty(property.Value, "Modules", out var modules)
                && modules.ValueKind == JsonValueKind.Object)
            {
                foreach (var module in modules.EnumerateObject())
                {
                    // Credentials in this model are environment variable NAMES, not secret values.
                    // Provider Settings, in contrast, may contain legacy literal credential fields.
                    if (IsProvider(module.Name) && TryProperty(module.Value, "Settings", out var settings)
                        && ProviderHasCredentials(settings))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool ProviderMapHasCredentials(JsonElement providers)
        => providers.ValueKind == JsonValueKind.Object && providers.EnumerateObject()
            .Any(provider => IsProvider(provider.Name) && ProviderHasCredentials(provider.Value));

    private static bool ProviderHasCredentials(JsonElement provider)
        => provider.ValueKind == JsonValueKind.Object && provider.EnumerateObject()
            .Any(field => CredentialFields.Contains(field.Name) && HasValue(field.Value));

    private static bool IsCredentialPath(string[] path)
        => CredentialFields.Contains(path[^1]) &&
            ((path.Length == 2 && IsProvider(path[0])) ||
             (path.Length == 3 && Same(path[0], "Providers") && IsProvider(path[1])) ||
             (path.Length == 4 && Same(path[0], "Backfill") && Same(path[1], "Providers") && IsProvider(path[2])));

    private static bool IsProvider(string name)
        => Same(name, "NYSE") || ProviderCredentialCatalog.Find(name)?.RequiresCredentials == true;

    private static bool HasValue(JsonElement value)
        => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));

    private static bool TryProperty(JsonElement value, string name, out JsonElement found)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (Same(property.Name, name))
                {
                    found = property.Value;
                    return true;
                }
            }
        }
        found = default;
        return false;
    }

    private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Safe refusal without echoing submitted credential values or configuration content.</summary>
internal sealed class ProviderConfigurationCredentialException : InvalidOperationException
{
    public ProviderConfigurationCredentialException() : base(
        "Provider credentials cannot be saved in application configuration. Use Settings > Manage owned credentials for the retained tenant and account. Remove legacy plaintext provider credentials through the reviewed migration or purge workflow before saving other settings.")
    { }
}
