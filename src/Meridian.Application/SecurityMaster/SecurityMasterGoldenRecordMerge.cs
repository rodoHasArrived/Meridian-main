using System.Globalization;
using System.IO;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Pure golden-record merge helpers: when an operator resolves a <em>field-value</em> conflict by
/// choosing a winning source, these functions apply the winning value into the stored terms and stamp
/// field-level provenance for it — so resolution rewrites the projection rather than only annotating a
/// winner string on the conflict row. Kept storage-agnostic and side-effect free so both the in-memory
/// and durable conflict stores share one implementation and it is fully unit-testable.
/// </summary>
public static class SecurityMasterGoldenRecordMerge
{
    /// <summary>The conflict kind whose resolution merges a winning field value into the projection.</summary>
    public const string FieldValueConflictKind = "FieldValue";

    private const string CommonScope = "common";
    private const string AssetSpecificScope = "assetSpecific";

    /// <summary>
    /// Resolves the winning field value from a <see cref="FieldValueConflictKind"/> conflict given the
    /// operator-chosen winning source. Returns <see langword="false"/> for non-field-value conflicts or
    /// when the chosen source is not one of the two candidate providers — callers then leave the stored
    /// terms untouched.
    /// </summary>
    public static bool TryResolveWinningValue(SecurityMasterConflict conflict, string? chosenWinnerSource, out string winningValue)
    {
        winningValue = string.Empty;
        if (conflict is null
            || !string.Equals(conflict.ConflictKind, FieldValueConflictKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(chosenWinnerSource, conflict.ProviderA, StringComparison.OrdinalIgnoreCase))
        {
            winningValue = conflict.ValueA;
            return true;
        }

        if (string.Equals(chosenWinnerSource, conflict.ProviderB, StringComparison.OrdinalIgnoreCase))
        {
            winningValue = conflict.ValueB;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Splits a canonical dotted field path into its scope and property name. <c>common.currency</c>
    /// targets the common-terms document; <c>assetSpecific.x</c> and any un-scoped or unknown-scoped
    /// path target the asset-specific-terms document.
    /// </summary>
    public static (bool IsCommon, string Property) ResolveFieldTarget(string fieldPath)
    {
        var trimmed = (fieldPath ?? string.Empty).Trim();
        var dot = trimmed.IndexOf('.');
        if (dot <= 0)
        {
            return (false, trimmed);
        }

        var scope = trimmed[..dot];
        var property = trimmed[(dot + 1)..];
        if (string.Equals(scope, CommonScope, StringComparison.OrdinalIgnoreCase))
        {
            return (true, property);
        }

        // assetSpecific.* and any unrecognized scope resolve to the asset-specific property.
        return (false, property);
    }

    /// <summary>
    /// Returns a copy of <paramref name="terms"/> with <paramref name="property"/> set to
    /// <paramref name="winningValue"/>. Numeric and boolean literals are written as JSON numbers/bools
    /// so typed consumers still read them; everything else is written as a JSON string.
    /// </summary>
    public static JsonElement ApplyFieldValue(JsonElement terms, string property, string winningValue)
        => SetProperty(terms, property, writer => WriteScalar(writer, winningValue));

    /// <summary>Reads the embedded field-provenance set from a provenance JSON blob (empty if absent).</summary>
    public static SecurityFieldProvenanceSet ReadFieldProvenance(JsonElement provenance)
    {
        if (provenance.ValueKind != JsonValueKind.Object
            || !provenance.TryGetProperty(SecurityFieldProvenanceSet.EmbeddedPropertyName, out var fields)
            || fields.ValueKind != JsonValueKind.Object)
        {
            return SecurityFieldProvenanceSet.Empty;
        }

        var entries = new List<SecurityFieldProvenance>();
        foreach (var field in fields.EnumerateObject())
        {
            if (field.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            entries.Add(new SecurityFieldProvenance(
                FieldPath: field.Name,
                Source: GetString(field.Value, "source") ?? string.Empty,
                Authority: GetInt(field.Value, "authority") ?? int.MaxValue,
                Confidence: GetDecimal(field.Value, "confidence") ?? 0m,
                AsOf: GetDate(field.Value, "asOf") ?? default,
                Reason: GetString(field.Value, "reason"),
                UpdatedBy: GetString(field.Value, "updatedBy")));
        }

        return new SecurityFieldProvenanceSet(entries);
    }

    /// <summary>
    /// Returns a copy of <paramref name="provenance"/> carrying <paramref name="fields"/> under the
    /// reserved <c>fields</c> object. An empty set leaves the blob unchanged so provenance stays lean.
    /// </summary>
    public static JsonElement WriteFieldProvenance(JsonElement provenance, SecurityFieldProvenanceSet fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Fields.Count == 0)
        {
            return provenance.Clone();
        }

        return SetProperty(provenance, SecurityFieldProvenanceSet.EmbeddedPropertyName, writer =>
        {
            writer.WriteStartObject();
            foreach (var entry in fields.Fields.OrderBy(f => f.FieldPath, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(entry.FieldPath);
                writer.WriteStartObject();
                writer.WriteString("source", entry.Source);
                writer.WriteNumber("authority", entry.Authority);
                writer.WriteNumber("confidence", entry.Confidence);
                writer.WriteString("asOf", entry.AsOf);
                if (entry.Reason is not null)
                {
                    writer.WriteString("reason", entry.Reason);
                }

                if (entry.UpdatedBy is not null)
                {
                    writer.WriteString("updatedBy", entry.UpdatedBy);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// Applies a resolved field-value conflict's winning value into the affected security's stored
    /// terms and stamps field-level provenance for it. A no-op for non-field-value conflicts, dismissed
    /// resolutions, or when the chosen source is not a candidate — so identifier-ambiguity resolution
    /// keeps its existing annotate-only behavior. Best-effort: the conflict's status transition has
    /// already been committed by the caller; a failure here is surfaced via <paramref name="onError"/>
    /// rather than unwinding a completed resolution.
    /// </summary>
    /// <returns><see langword="true"/> when a winning value was merged into the projection.</returns>
    public static async Task<bool> ApplyResolvedFieldWinnerAsync(
        ISecurityMasterStore store,
        SecurityMasterConflict resolvedConflict,
        ResolveConflictRequest request,
        DateTimeOffset asOf,
        Action<Exception>? onError,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolvedConflict);
        ArgumentNullException.ThrowIfNull(request);

        // Only a genuine "Resolved" (not "Dismiss") outcome with a chosen candidate source merges a value.
        if (string.Equals(request.Resolution, "Dismiss", StringComparison.OrdinalIgnoreCase)
            || !TryResolveWinningValue(resolvedConflict, request.ChosenWinnerSource, out var winningValue))
        {
            return false;
        }

        try
        {
            var projection = await store.GetProjectionAsync(resolvedConflict.SecurityId, ct).ConfigureAwait(false);
            if (projection is null)
            {
                return false;
            }

            var (isCommon, property) = ResolveFieldTarget(resolvedConflict.FieldPath);
            var updatedCommon = isCommon
                ? ApplyFieldValue(projection.CommonTerms, property, winningValue)
                : projection.CommonTerms;
            var updatedAssetSpecific = isCommon
                ? projection.AssetSpecificTerms
                : ApplyFieldValue(projection.AssetSpecificTerms, property, winningValue);

            var fieldProvenance = ReadFieldProvenance(projection.Provenance).Upsert(new SecurityFieldProvenance(
                FieldPath: resolvedConflict.FieldPath,
                Source: request.ChosenWinnerSource ?? resolvedConflict.ProviderA,
                Authority: 0,
                Confidence: 1m,
                AsOf: asOf,
                Reason: request.Reason,
                UpdatedBy: request.ResolvedBy));
            var updatedProvenance = WriteFieldProvenance(projection.Provenance, fieldProvenance);

            // Keep the denormalized Currency column consistent when it is the merged field.
            var updatedCurrency = isCommon && string.Equals(property, "currency", StringComparison.OrdinalIgnoreCase)
                ? winningValue
                : projection.Currency;

            var merged = projection with
            {
                CommonTerms = updatedCommon,
                AssetSpecificTerms = updatedAssetSpecific,
                Provenance = updatedProvenance,
                Currency = updatedCurrency,
            };

            await store.UpsertProjectionAsync(merged, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            // Best-effort by contract: the conflict's status transition is already committed, so a
            // merge failure must never unwind a completed resolution — swallow it (surfacing it via
            // onError when a callback is supplied) rather than propagating even when onError is null.
            onError?.Invoke(ex);
            return false;
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static JsonElement SetProperty(JsonElement source, string propertyName, Action<Utf8JsonWriter> writeValue)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (source.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in source.EnumerateObject())
                {
                    if (property.NameEquals(propertyName))
                    {
                        continue;
                    }

                    property.WriteTo(writer);
                }
            }

            writer.WritePropertyName(propertyName);
            writeValue(writer);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteScalar(Utf8JsonWriter writer, string raw)
    {
        if (bool.TryParse(raw, out var flag))
        {
            writer.WriteBooleanValue(flag);
            return;
        }

        // Only a plain signed decimal is treated as numeric; currency/country codes and other
        // alphanumeric values (e.g. "USD") fall through to a JSON string.
        const NumberStyles numberStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!string.IsNullOrWhiteSpace(raw)
            && decimal.TryParse(raw.Trim(), numberStyles, CultureInfo.InvariantCulture, out var number))
        {
            writer.WriteNumberValue(number);
            return;
        }

        writer.WriteStringValue(raw);
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static decimal? GetDecimal(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    private static DateTimeOffset? GetDate(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && value.TryGetDateTimeOffset(out var date)
            ? date
            : null;
}
