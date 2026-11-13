using System.Text.Json;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Application.ProviderRouting;

/// <summary>Prevents supported configuration persistence from writing provider secrets outside the owned vault.</summary>
public static class ProviderConfigurationCredentialGuard
{
    private static readonly HashSet<string> CredentialFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "KeyId", "SecretKey", "ApiKey", "ApiSecret", "ApiToken", "AccessToken", "RefreshToken",
        "ClientId", "ClientSecret", "Secret", "Token", "Password"
    };

    private static readonly HashSet<string> CredentialEnvironmentKeys = ProviderCredentialCatalog.All
        .SelectMany(provider => provider.RequiredFields)
        .Where(field => CredentialFields.Contains(field.Name))
        .SelectMany(field => field.EnvironmentNames)
        .Concat(["NYSE_API_KEY", "NYSE_API_SECRET", "NYSE_CLIENT_ID"])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static AppConfig Prepare(AppConfig candidate, ProviderCredentialOwnershipPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.RequireExplicitOwnership)
            return candidate;

        // A secret-free partial request must not re-publish credentials retained in another
        // section or extension object. Inspect the complete snapshot, including retained fields.
        using var document = JsonSerializer.SerializeToDocument(candidate, AppConfigJsonOptions.Write);
        Validate(document.RootElement, policy);
        // Extension objects and provider settings use mutable dictionaries. Retain an owned copy
        // of exactly the validated snapshot, including when SaveAsync waits for another writer.
        return document.RootElement.Deserialize<AppConfig>(AppConfigJsonOptions.Read)!;
    }

    public static void Validate(JsonElement candidate, ProviderCredentialOwnershipPolicy policy)
    {
        if (policy.RequireExplicitOwnership && ContainsCredentials(candidate))
            throw new ProviderConfigurationCredentialException();
    }

    /// <summary>Validates an existing serialized snapshot before it is copied or published.</summary>
    public static void ValidateJson(string json, ProviderCredentialOwnershipPolicy policy)
    {
        if (!policy.RequireExplicitOwnership)
            return;
        using var candidate = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        Validate(candidate.RootElement, policy);
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
            if (Same(property.Name, "Backfill") && AnyProperty(property.Value, "Providers", ProviderMapHasCredentials))
                return true;
            if (Same(property.Name, "Providers") && ProviderMapHasCredentials(property.Value))
                return true;
            if (Same(property.Name, "DataSources") && AnyProperty(property.Value, "Sources",
                    sources => sources.ValueKind == JsonValueKind.Array && sources.EnumerateArray().Any(ContainsCredentials)))
                return true;
            if (Same(property.Name, "ProviderModules") && AnyProperty(property.Value, "Modules", ModuleMapHasCredentials))
                return true;
        }
        return false;
    }

    private static bool ProviderMapHasCredentials(JsonElement providers)
        => providers.ValueKind == JsonValueKind.Object && providers.EnumerateObject()
            .Any(provider => IsProvider(provider.Name) && ProviderHasCredentials(provider.Value));

    private static bool ProviderHasCredentials(JsonElement provider)
        => provider.ValueKind == JsonValueKind.Object && provider.EnumerateObject()
            .Any(field => CredentialFields.Contains(field.Name) && HasValue(field.Value));

    private static bool ModuleMapHasCredentials(JsonElement modules)
        // Credentials in this model are environment variable NAMES, not secret values.
        // Provider Settings, in contrast, may contain legacy literal credential fields.
        => modules.ValueKind == JsonValueKind.Object && modules.EnumerateObject()
            .Any(module => IsProvider(module.Name) && AnyProperty(module.Value, "Settings", ProviderHasCredentials));

    private static bool IsCredentialPath(string[] path)
        => CredentialFields.Contains(path[^1]) &&
            ((path.Length == 2 && IsProvider(path[0])) ||
             (path.Length == 3 && Same(path[0], "Providers") && IsProvider(path[1])) ||
             (path.Length == 4 && Same(path[0], "Backfill") && Same(path[1], "Providers") && IsProvider(path[2])));

    private static bool IsProvider(string name)
        => Same(name, "NYSE") || ProviderCredentialCatalog.Find(name)?.RequiresCredentials == true;

    private static bool HasValue(JsonElement value)
        => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && (value.ValueKind != JsonValueKind.String ||
                (!string.IsNullOrWhiteSpace(value.GetString()) && !IsEnvironmentReference(value.GetString()!)));

    // Preserve documented references as references; never accept a literal/default value embedded
    // inside an interpolation expression as an alternative route for storing a credential.
    private static bool IsEnvironmentReference(string value)
        => value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}')
            && CredentialEnvironmentKeys.Contains(value[2..^1]);

    private static bool AnyProperty(JsonElement value, string name, Func<JsonElement, bool> predicate)
        => value.ValueKind == JsonValueKind.Object && value.EnumerateObject()
            .Any(property => Same(property.Name, name) && predicate(property.Value));

    private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Safe refusal without echoing submitted credential values or configuration content.</summary>
public sealed class ProviderConfigurationCredentialException : InvalidOperationException
{
    public ProviderConfigurationCredentialException() : base(
        "Provider credentials cannot be saved in application configuration. Use Settings > Manage owned credentials for the retained tenant and account. For legacy plaintext, complete an explicit migration and purge before production or during reviewed offline maintenance. The original configuration was not changed.")
    { }
}
