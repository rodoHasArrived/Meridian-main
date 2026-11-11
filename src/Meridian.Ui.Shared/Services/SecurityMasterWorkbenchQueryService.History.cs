using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

// Schema compatibility, change history and the event-payload readers behind them.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static SecurityMasterChangeHistoryItemDto MapHistoryToLifecycleEvent(SecurityMasterEventEnvelope item)
    {
        var changedFields = ExtractChangedFields(item.Payload);
        return new SecurityMasterChangeHistoryItemDto(
            ChangeId: $"history-{item.StreamVersion}",
            StreamVersion: item.StreamVersion,
            EventType: item.EventType,
            ChangedAtUtc: item.EventTimestamp,
            EffectiveAtUtc: TryGetJsonDateTimeOffset(item.Payload, "effectiveAtUtc")
                ?? TryGetJsonDateTimeOffset(item.Payload, "effectiveFrom"),
            Actor: item.Actor,
            Origin: InferChangeOrigin(
                TryGetJsonString(item.Metadata, "sourceSystem") ?? string.Empty,
                item.Actor),
            SourceSystem: TryGetJsonString(item.Metadata, "sourceSystem")
                ?? TryGetJsonString(item.Metadata, "source")
                ?? "security-history",
            SourceRecordId: TryGetJsonString(item.Metadata, "sourceRecordId"),
            Reason: TryGetJsonString(item.Metadata, "reason"),
            Summary: $"{HumanizeEventType(item.EventType)} recorded for the instrument.",
            ChangedFields: changedFields,
            ChangedFieldsSummary: SummarizeChangedFields(changedFields));
    }

    private static SecurityMasterSchemaCompatibilityDto BuildSchemaCompatibility(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic)
    {
        var legacySchemaVersion = TryGetSchemaVersion(detail.AssetSpecificTerms) ?? SecurityMasterSchemaVersions.LegacyAssetSpecificTerms;
        var economicSchemaVersion = economic is null ? 0 : TryGetSchemaVersion(economic.EconomicTerms) ?? SecurityMasterSchemaVersions.EconomicTerms;
        var hasLegacyTerms = HasJsonContent(detail.AssetSpecificTerms);
        var hasEconomicTerms = economic is not null && HasJsonContent(economic.EconomicTerms);
        var hasClassificationPayload = economic is not null && HasJsonContent(economic.Classification);
        var hasSupportedLegacySchema = !hasLegacyTerms || legacySchemaVersion == SecurityMasterSchemaVersions.LegacyAssetSpecificTerms;
        var hasSupportedEconomicSchema = !hasEconomicTerms || economicSchemaVersion == SecurityMasterSchemaVersions.EconomicTerms;

        string summary;
        if (!hasSupportedLegacySchema && !hasSupportedEconomicSchema)
        {
            summary = $"Legacy schema v{legacySchemaVersion} and economic schema v{economicSchemaVersion} differ from the supported workstation versions v{SecurityMasterSchemaVersions.LegacyAssetSpecificTerms}/v{SecurityMasterSchemaVersions.EconomicTerms}; compatibility review is required.";
        }
        else if (!hasSupportedLegacySchema)
        {
            summary = $"Legacy schema v{legacySchemaVersion} differs from supported workstation schema v{SecurityMasterSchemaVersions.LegacyAssetSpecificTerms}; compatibility review is required.";
        }
        else if (!hasSupportedEconomicSchema)
        {
            summary = $"Economic schema v{economicSchemaVersion} differs from supported workstation schema v{SecurityMasterSchemaVersions.EconomicTerms}; compatibility review is required.";
        }
        else if (hasLegacyTerms && hasEconomicTerms)
        {
            summary = $"Legacy schema v{legacySchemaVersion} and economic schema v{economicSchemaVersion} are both available for compatibility-safe projections.";
        }
        else if (hasLegacyTerms)
        {
            summary = $"Legacy schema v{legacySchemaVersion} remains the authoritative workstation projection payload.";
        }
        else if (hasEconomicTerms)
        {
            summary = $"Economic schema v{economicSchemaVersion} is available without a legacy asset-specific payload.";
        }
        else
        {
            summary = "No structured schema payloads were rebuilt for the selected security.";
        }

        return new SecurityMasterSchemaCompatibilityDto(
            AssetClass: detail.AssetClass,
            LegacyAssetSpecificTermsSchemaVersion: legacySchemaVersion,
            EconomicTermsSchemaVersion: economicSchemaVersion,
            HasLegacyAssetSpecificTerms: hasLegacyTerms,
            HasEconomicTerms: hasEconomicTerms,
            HasClassificationPayload: hasClassificationPayload,
            Summary: summary);
    }

    private static IReadOnlyList<SecurityMasterChangeHistoryItemDto> BuildChangeHistory(
        IReadOnlyList<SecurityMasterEventEnvelope> history)
    {
        if (history.Count == 0)
        {
            return [];
        }

        var ordered = history
            .OrderBy(static item => item.StreamVersion)
            .ThenBy(static item => item.EventTimestamp)
            .ToArray();
        var items = new List<SecurityMasterChangeHistoryItemDto>(ordered.Length);
        SecurityEconomicDefinitionRecord? previousRecord = null;

        foreach (var envelope in ordered)
        {
            var currentRecord = TryParseHistoryRecord(envelope.Payload);
            var payloadProvenance = currentRecord?.Provenance;
            var sourceSystem =
                CoalesceNonEmpty(
                    TryGetJsonString(envelope.Metadata, "sourceSystem"),
                    TryGetJsonString(envelope.Metadata, "source"),
                    payloadProvenance.HasValue ? TryGetJsonString(payloadProvenance.Value, "sourceSystem") : null)
                ?? "Unknown";
            var sourceRecordId =
                CoalesceNonEmpty(
                    TryGetJsonString(envelope.Metadata, "sourceRecordId"),
                    payloadProvenance.HasValue ? TryGetJsonString(payloadProvenance.Value, "sourceRecordId") : null);
            var reason =
                CoalesceNonEmpty(
                    TryGetJsonString(envelope.Metadata, "reason"),
                    payloadProvenance.HasValue ? TryGetJsonString(payloadProvenance.Value, "reason") : null);
            var effectiveAtUtc = payloadProvenance.HasValue
                ? TryGetJsonDateTimeOffset(payloadProvenance.Value, "asOf")
                : null;
            var changedFields = BuildChangedFields(previousRecord, currentRecord, envelope.EventType);
            var changedFieldsSummary = changedFields.Count == 0
                ? "No structured field diff available."
                : string.Join(", ", changedFields);

            items.Add(new SecurityMasterChangeHistoryItemDto(
                ChangeId: $"{envelope.StreamVersion}:{envelope.EventType}",
                StreamVersion: envelope.StreamVersion,
                EventType: envelope.EventType,
                ChangedAtUtc: envelope.EventTimestamp,
                EffectiveAtUtc: effectiveAtUtc,
                Actor: envelope.Actor,
                Origin: InferChangeOrigin(sourceSystem, envelope.Actor),
                SourceSystem: sourceSystem,
                SourceRecordId: sourceRecordId,
                Reason: reason,
                Summary: BuildChangeSummary(envelope, currentRecord, changedFields, sourceSystem),
                ChangedFields: changedFields,
                ChangedFieldsSummary: changedFieldsSummary));

            if (currentRecord is not null)
            {
                previousRecord = currentRecord;
            }
        }

        return items
            .OrderByDescending(static item => item.ChangedAtUtc)
            .ThenByDescending(static item => item.StreamVersion)
            .ToArray();
    }

    private static int? TryGetSchemaVersion(JsonElement element)
    {
        if (!TryGetPropertyCaseInsensitive(element, "schemaVersion", out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericVersion))
        {
            return numericVersion;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsedVersion)
            ? parsedVersion
            : null;
    }

    private static bool HasJsonContent(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            _ => true
        };

    private static bool ClassificationLabelsEqual(string? previous, string? current)
        => string.Equals(
            ClassificationLabelReader.Normalize(previous),
            ClassificationLabelReader.Normalize(current),
            StringComparison.OrdinalIgnoreCase);

    private static SecurityEconomicDefinitionRecord? TryParseHistoryRecord(JsonElement payload)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return SecurityMasterMapping.FromEconomicPayload(payload);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> BuildChangedFields(
        SecurityEconomicDefinitionRecord? previousRecord,
        SecurityEconomicDefinitionRecord? currentRecord,
        string eventType)
    {
        if (currentRecord is null)
        {
            return [];
        }

        if (previousRecord is null)
        {
            return ["Identity", "Common terms", "Identifiers"];
        }

        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(previousRecord.DisplayName, currentRecord.DisplayName, StringComparison.Ordinal))
        {
            fields.Add("Display name");
        }

        if (!string.Equals(previousRecord.Currency, currentRecord.Currency, StringComparison.OrdinalIgnoreCase))
        {
            fields.Add("Currency");
        }

        if (previousRecord.Status != currentRecord.Status)
        {
            fields.Add("Status");
        }

        if (previousRecord.EffectiveFrom != currentRecord.EffectiveFrom
            || previousRecord.EffectiveTo != currentRecord.EffectiveTo)
        {
            fields.Add("Effective window");
        }

        // A retained event may carry the legacy F# label rendering while its successor carries the
        // canonical name; compare decoded labels so a label-format change alone is not reported.
        if (!ClassificationLabelsEqual(previousRecord.AssetClass, currentRecord.AssetClass)
            || !ClassificationLabelsEqual(previousRecord.AssetFamily, currentRecord.AssetFamily)
            || !ClassificationLabelsEqual(previousRecord.SubType, currentRecord.SubType))
        {
            fields.Add("Classification");
        }

        if (!JsonElementsEquivalent(previousRecord.CommonTerms, currentRecord.CommonTerms))
        {
            fields.Add("Common terms");
        }

        if (!JsonElementsEquivalent(previousRecord.EconomicTerms, currentRecord.EconomicTerms))
        {
            fields.Add("Economic terms");
        }

        if (!IdentifiersEquivalent(previousRecord.Identifiers, currentRecord.Identifiers))
        {
            fields.Add("Identifiers");
        }

        if (eventType.Contains("Deactivate", StringComparison.OrdinalIgnoreCase))
        {
            fields.Add("Lifecycle status");
        }

        return fields
            .OrderBy(static field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractChangedFields(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var fields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in payload.EnumerateObject())
        {
            var field = property.Name;
            if (field.EndsWith("Terms", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Economic terms");
            }
            else if (field.Contains("identifier", StringComparison.OrdinalIgnoreCase)
                || field.Contains("alias", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Identifiers");
            }
            else if (field.Contains("status", StringComparison.OrdinalIgnoreCase)
                || field.Contains("deactivat", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Lifecycle status");
            }
            else if (field.Contains("effective", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Effective window");
            }
            else if (field.Contains("classification", StringComparison.OrdinalIgnoreCase)
                || field.Contains("assetClass", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("Classification");
            }
            else
            {
                fields.Add(HumanizeEventType(field));
            }
        }

        return fields.ToArray();
    }

    private static string BuildChangeSummary(
        SecurityMasterEventEnvelope envelope,
        SecurityEconomicDefinitionRecord? currentRecord,
        IReadOnlyList<string> changedFields,
        string sourceSystem)
    {
        if (currentRecord is null)
        {
            return $"{HumanizeEventType(envelope.EventType)} recorded from {sourceSystem}.";
        }

        var primaryIdentifier = currentRecord.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary);
        if (string.Equals(envelope.EventType, "SecurityCreated", StringComparison.OrdinalIgnoreCase))
        {
            var identifierText = primaryIdentifier is null
                ? "without a primary identifier"
                : $"with primary {primaryIdentifier.Kind}:{primaryIdentifier.Value}";
            return $"Created {currentRecord.AssetClass} '{currentRecord.DisplayName}' {identifierText}.";
        }

        if (envelope.EventType.Contains("Deactivate", StringComparison.OrdinalIgnoreCase))
        {
            var effectiveText = currentRecord.EffectiveTo?.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'")
                ?? envelope.EventTimestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'");
            return $"Deactivated {currentRecord.AssetClass} '{currentRecord.DisplayName}' effective {effectiveText}.";
        }

        if (changedFields.Count == 0)
        {
            return $"{HumanizeEventType(envelope.EventType)} recorded for '{currentRecord.DisplayName}'.";
        }

        return $"{HumanizeEventType(envelope.EventType)} updated {SummarizeChangedFields(changedFields)} for '{currentRecord.DisplayName}'.";
    }

    private static string SummarizeChangedFields(IReadOnlyList<string> changedFields)
    {
        if (changedFields.Count == 0)
        {
            return "the selected security";
        }

        if (changedFields.Count <= 3)
        {
            return string.Join(", ", changedFields).ToLowerInvariant();
        }

        var leading = string.Join(", ", changedFields.Take(3)).ToLowerInvariant();
        return $"{leading}, and {changedFields.Count - 3} more area(s)";
    }

    private static string HumanizeEventType(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "Security change";
        }

        var builder = new System.Text.StringBuilder(eventType.Length + 8);
        for (var index = 0; index < eventType.Length; index++)
        {
            var character = eventType[index];
            if (index > 0
                && char.IsUpper(character)
                && !char.IsUpper(eventType[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string InferChangeOrigin(string sourceSystem, string actor)
    {
        if (ContainsAny(sourceSystem, actor, "wpf-ui", "manual", "override", "desktop-user", "operator", "user"))
        {
            return "User";
        }

        if (ContainsAny(sourceSystem, actor, "polygon", "edgar", "bloomberg", "refinitiv", "trustee", "custodian", "vendor", "golden-edm"))
        {
            return "Vendor";
        }

        if (ContainsAny(sourceSystem, actor, "workflow", "system", "bot", "snapshot"))
        {
            return "System";
        }

        return "Unknown";
    }

    private static bool ContainsAny(string sourceSystem, string actor, params string[] tokens)
        => tokens.Any(token =>
            sourceSystem.Contains(token, StringComparison.OrdinalIgnoreCase)
            || actor.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string? value, string? token)
        => !string.IsNullOrWhiteSpace(value) &&
           !string.IsNullOrWhiteSpace(token) &&
           value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool JsonElementsEquivalent(JsonElement left, JsonElement right)
        => left.ValueKind == right.ValueKind
            && string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);

    private static bool IdentifiersEquivalent(
        IReadOnlyList<SecurityIdentifierDto> left,
        IReadOnlyList<SecurityIdentifierDto> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        static string Key(SecurityIdentifierDto identifier) =>
            $"{identifier.Kind}|{identifier.Value}|{identifier.Provider}|{identifier.ValidFrom:O}|{identifier.ValidTo:O}|{identifier.IsPrimary}";

        var leftKeys = left.Select(Key).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        var rightKeys = right.Select(Key).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
    }

    private static string? CoalesceNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
