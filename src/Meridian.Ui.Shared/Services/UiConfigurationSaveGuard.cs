using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Core.Config;

namespace Meridian.Ui.Shared.Services;

/// <summary>Validation failures contain property names and safe messages, never submitted values.</summary>
public sealed class UiConfigurationValidationException(IReadOnlyDictionary<string, string[]> errors)
    : InvalidOperationException("Configuration was not saved. Correct the validation errors and retry.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Guards ordinary UI settings writes. Credential lifecycle and host deployment use their own authority.</summary>
public static class UiConfigurationSaveGuard
{
    private static readonly HashSet<string> CredentialProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "KeyId", "SecretKey", "ApiKey", "ApiToken", "Token", "Password", "Secret", "ConnectionString",
        "AccessKey", "AccessToken", "RefreshToken", "BearerToken", "ClientSecret", "PrivateKey"
    };

    /// <summary>
    /// Validates a desktop whole-document save without recovering corrupt input or ignoring
    /// unmodeled host sections. Call while holding the configuration write lease.
    /// </summary>
    public static void ValidateWholeDocumentJson(string existingJson, string proposedJson)
    {
        AppConfig current;
        AppConfig next;
        JsonNode before;
        JsonNode after;
        try
        {
            current = JsonSerializer.Deserialize<AppConfig>(existingJson, AppConfigJsonOptions.Read)
                ?? throw new JsonException();
            next = JsonSerializer.Deserialize<AppConfig>(proposedJson, AppConfigJsonOptions.Read)
                ?? throw new JsonException();
            var documentOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
            before = JsonNode.Parse(existingJson, documentOptions: documentOptions) as JsonObject ?? throw new JsonException();
            after = JsonNode.Parse(proposedJson, documentOptions: documentOptions) as JsonObject ?? throw new JsonException();
            RefuseAmbiguousProperties(before);
            RefuseAmbiguousProperties(after);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("Configuration was not saved. Existing and proposed documents must be readable, nonempty configuration objects; repair the source explicitly before retrying.");
        }
        Validate(current, next, confirmDataSemantics: false, wholeDocument: true);
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        // AppConfig deliberately does not model every host section. Inspect raw JSON too, so
        // desktop extension data cannot introduce plaintext secrets or turn on a live gateway.
        CheckChanges(before, after, "", confirmed: false, errors);
        foreach (var path in new[] { "ProviderConnections", "ProviderModules" })
            if (!JsonNode.DeepEquals(At(before, path), At(after, path)))
                errors[path] = ["Use the dedicated owned-provider workflow; whole-document settings saves cannot change host provider bindings or modules."];
        if (errors.Count > 0) throw new UiConfigurationValidationException(errors);
    }

    internal static void Validate(AppConfig current, AppConfig next, bool confirmDataSemantics, bool wholeDocument)
    {
        ArgumentNullException.ThrowIfNull(next);
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var failure in new AppConfigValidator().Validate(next).Errors)
        {
            // Credential presence belongs to the vault/environment and connection checks. Never
            // require inline secrets merely to save a provider's operational settings.
            if (failure.PropertyName is "Alpaca" or "IB" or "Alpaca.KeyId" or "Alpaca.SecretKey") continue;
            errors[failure.PropertyName] = [failure.ErrorMessage];
        }
        foreach (var entry in next.Backfill?.ValidationProfiles ?? new Dictionary<string, ProviderValidationProfile>())
        {
            try
            {
                if (entry.Value is null || string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > 80 ||
                    entry.Key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
                    throw new ArgumentException("Invalid validation profile.");
                entry.Value.Validate();
            }
            catch (ArgumentException)
            {
                errors["Backfill.ValidationProfiles"] = ["Every provider validation profile must have a supported provider ID and bounded finite tolerances."];
            }
        }
        // Invalid floating-point values cannot be serialized. Refuse them with safe property-only
        // validation evidence before producing the before/after JSON used by the remaining guards.
        if (errors.ContainsKey("Backfill.ValidationProfiles")) throw new UiConfigurationValidationException(errors);
        if (next.Backfill?.Providers is { } providers)
        {
            foreach (var provider in BackfillProviderCatalog.Providers)
            {
                try
                {
                    BackfillProviderCatalog.Apply(providers, provider.Id,
                        BackfillProviderCatalog.Read(providers, provider.Id));
                }
                catch (ArgumentException)
                {
                    errors[$"Backfill.Providers.{provider.Id}"] =
                        ["Provider options must use a recognized tier, a bounded priority and positive request limits no greater than that tier's ceiling."];
                }
            }
        }
        if (!string.Equals(current.TenantScopeEnforcement, next.TenantScopeEnforcement, StringComparison.Ordinal))
            errors["TenantScopeEnforcement"] = ["Tenant posture is a host deployment setting and cannot be changed by a UI save."];

        var before = JsonSerializer.SerializeToNode(current, AppConfigJsonOptions.Write);
        var after = JsonSerializer.SerializeToNode(next, AppConfigJsonOptions.Write);
        CheckChanges(before, after, "", confirmDataSemantics, errors);
        if (wholeDocument)
        {
            foreach (var path in new[] { "Backfill.ValidationProfiles", "Backfill.ValidationProfileAudit", "DataSources.FailoverRules", "DataSources.FailoverRuleAudit", "ProviderConnections" })
            {
                if (!JsonNode.DeepEquals(At(before, path), At(after, path)))
                    errors[path] = ["Use the dedicated provider editor so validation reasons and audit evidence are retained."];
            }
        }
        if (errors.Count > 0) throw new UiConfigurationValidationException(errors);
    }

    private static void RefuseAmbiguousProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, child) in obj)
            {
                if (!names.Add(name)) throw new JsonException();
                RefuseAmbiguousProperties(child);
            }
        }
        else if (node is JsonArray array)
            foreach (var child in array) RefuseAmbiguousProperties(child);
    }

    private static JsonNode? At(JsonNode? root, string path)
    {
        foreach (var segment in path.Split('.'))
        {
            if (root is not JsonObject obj) return null;
            root = obj.FirstOrDefault(entry => entry.Key.Equals(segment, StringComparison.OrdinalIgnoreCase)).Value;
        }
        return root;
    }

    private static JsonNode? Child(JsonObject? obj, string name)
        => obj?.FirstOrDefault(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static void CheckChanges(JsonNode? before, JsonNode? after, string path, bool confirmed,
        Dictionary<string, string[]> errors)
    {
        if (JsonNode.DeepEquals(before, after)) return;
        var property = path.Split('.').Last();
        if (CredentialProperties.Contains(property) && after is JsonValue value
            && value.TryGetValue<string>(out var credential) && !string.IsNullOrWhiteSpace(credential))
            errors[path] = ["Store credentials through the owned provider credential workflow or environment; settings cannot write plaintext credentials."];
        if (!confirmed && (property.Equals("Adjustment", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(before?.ToString() ?? "all", after?.ToString() ?? "all", StringComparison.OrdinalIgnoreCase)
            || property.Equals("PreferAdjustedPrices", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(before?.ToString() ?? "true", after?.ToString() ?? "true", StringComparison.OrdinalIgnoreCase)
            || (property.Equals("SymbolResolutionMode", StringComparison.OrdinalIgnoreCase)
                || property.Equals("ResolutionMode", StringComparison.OrdinalIgnoreCase)
                || path.Contains("ProviderResolutionModes.", StringComparison.OrdinalIgnoreCase))
                && string.Equals(after?.ToString(), "Canonical", StringComparison.OrdinalIgnoreCase)))
            errors[path] = ["Confirm that existing archives are not rewritten and adjusted and raw series will be kept separate before changing stored-data semantics."];
        if (path.Equals("IB.UsePaperTrading", StringComparison.OrdinalIgnoreCase)
            && after?.ToString().Equals("false", StringComparison.OrdinalIgnoreCase) == true)
            errors[path] = ["Settings cannot enable live brokerage. Use the separately authorized host activation workflow."];
        if (path.Equals("Execution", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Brokerage", StringComparison.OrdinalIgnoreCase)
            || path.Equals("LiveTrading", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Gateways", StringComparison.OrdinalIgnoreCase))
            errors[path] = ["Settings cannot change host execution or live gateway activation."];
        if (before is JsonObject || after is JsonObject)
        {
            var oldObject = before as JsonObject;
            var newObject = after as JsonObject;
            foreach (var key in (oldObject?.Select(entry => entry.Key) ?? [])
                .Union(newObject?.Select(entry => entry.Key) ?? [], StringComparer.OrdinalIgnoreCase))
                CheckChanges(Child(oldObject, key), Child(newObject, key), path.Length == 0 ? key : path + "." + key, confirmed, errors);
        }
        else if (before is JsonArray || after is JsonArray)
        {
            var oldArray = before as JsonArray;
            var newArray = after as JsonArray;
            for (var index = 0; index < Math.Max(oldArray?.Count ?? 0, newArray?.Count ?? 0); index++)
                CheckChanges(index < (oldArray?.Count ?? 0) ? oldArray![index] : null,
                    index < (newArray?.Count ?? 0) ? newArray![index] : null, path + "." + index, confirmed, errors);
        }
    }
}
