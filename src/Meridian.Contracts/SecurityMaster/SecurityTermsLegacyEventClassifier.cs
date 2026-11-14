using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The frozen classifier for asset-specific terms carried by Security Master event history written
/// before <see cref="SecurityTermsEnvelope.FormatKey"/> existed (ADR-024 decision 5, amendment
/// 2026-11-13 steps 5 and 6).
/// <para>
/// Live code dispatches on the declared tag alone: <see cref="SecurityTermsEnvelope.ResolveFormat"/>
/// and <see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument"/> refuse
/// an untagged payload with a precise error. Migration 068 declared the family on every current-state
/// row and its <c>ck_securities_terms_format_declared</c> constraint refuses new untagged rows, but
/// <c>security_events</c> is append-only (migration 059) and retained snapshots keep their pre-tag
/// bytes, so event and snapshot decoding still meets untagged retained terms. Only that decoding —
/// <c>SecurityMasterMapping.FromEconomicPayload</c> and the rebuild paths fed by it — classifies them
/// here, and only in memory: nothing is ever written back to an event.
/// </para>
/// <para>
/// The rule is frozen with the population it classifies and must never grow: a profile envelope
/// (a string <c>customProfileId</c> and a <c>profileFields</c> object) is
/// <see cref="SecurityTermsFormats.CustomProfile"/>; an object stamped
/// <see cref="ReservedForEconomicTerms"/> that carries one of the <see cref="ShapeRuleModules"/> keys is
/// <see cref="SecurityTermsFormats.Economic"/>; anything else is <see cref="SecurityTermsFormats.Flat"/>.
/// Migration 042's SQL backfill and migration 068's refusal step mirror it; the parity test in
/// <c>SecurityTermsVersioningTests</c> pins the SQL to <see cref="ShapeRuleModules"/>.
/// </para>
/// </summary>
public static class SecurityTermsLegacyEventClassifier
{
    /// <summary>
    /// The <c>schemaVersion</c> an untagged pre-tag economic-terms document carries, and therefore the
    /// one integer the historical shape rule consults. Before the tag existed, the asset-specific-terms
    /// slot held two payload families behind one <c>schemaVersion</c> key, so flat version 2 was
    /// reserved: declaring it would have routed untagged flat documents into the economic bridge. Live
    /// acceptance no longer reserves it (a tagged flat version-2 payload reads as flat and is judged by
    /// <see cref="AssetSpecificTermsSchema.IsAccepted"/> like any other flat version); the reservation
    /// survives only here, frozen with the untagged history it classifies.
    /// </summary>
    public const int ReservedForEconomicTerms = 2;

    /// <summary>
    /// The module keys the historical shape rule consults: exactly the modules the economic serializer
    /// wrote before <see cref="SecurityTermsEnvelope.FormatKey"/> existed. Untagged payloads predate the
    /// tag, so only these keys can mark one as economic; modules serialized later (ADR-027 stage 1
    /// increment 5) never appear untagged and are deliberately not consulted. This list never grows —
    /// migration 042's SQL mirror of it is pinned to it.
    /// </summary>
    public static IReadOnlyList<string> ShapeRuleModules { get; } =
        ["maturity", "coupon", "payment", "accrual", "discount",
         "redemption", "call", "auction", "sweep", "financing", "issuer", "equityBehavior", "fund", "structuredProduct"];

    /// <summary>
    /// The historical shape rule for an untagged payload: an object stamped
    /// <see cref="ReservedForEconomicTerms"/> that carries at least one of the
    /// <see cref="ShapeRuleModules"/> keys. The economic serializer always wrote them and a flat document
    /// never did, so the module keys, not the integer, told the two families apart.
    /// </summary>
    public static bool IsEconomicTermsShape(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(payload) != ReservedForEconomicTerms)
        {
            return false;
        }

        foreach (var module in ShapeRuleModules)
        {
            if (payload.TryGetProperty(module, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The family of retained terms from event history: the declared
    /// <see cref="SecurityTermsEnvelope.FormatKey"/> when present (a tagged document never depends on
    /// its shape), otherwise the frozen shape rule. A non-object is <see cref="SecurityTermsFormats.Flat"/>.
    /// </summary>
    public static string Classify(JsonElement payload)
    {
        if (SecurityTermsEnvelope.GetDeclaredFormat(payload) is { } declared)
        {
            return declared;
        }

        if (SecurityTermsEnvelope.IsProfileEnvelopeDocument(payload))
        {
            return SecurityTermsFormats.CustomProfile;
        }

        return IsEconomicTermsShape(payload) ? SecurityTermsFormats.Economic : SecurityTermsFormats.Flat;
    }

    /// <summary>
    /// The family replay declares on untagged retained terms: <see cref="Classify"/>'s verdict when it is
    /// <see cref="SecurityTermsFormats.Flat"/> or <see cref="SecurityTermsFormats.CustomProfile"/>.
    /// <see langword="null"/> for a payload that already declares a family, a non-object, or an untagged
    /// economic-shaped document (the asset-specific-terms slot never declares
    /// <see cref="SecurityTermsFormats.Economic"/>).
    /// </summary>
    public static string? GetStampableFormat(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || SecurityTermsEnvelope.GetDeclaredFormat(payload) is not null)
        {
            return null;
        }

        var classified = Classify(payload);
        return classified is SecurityTermsFormats.Flat or SecurityTermsFormats.CustomProfile ? classified : null;
    }

    /// <summary>
    /// Declares <see cref="SecurityTermsEnvelope.FormatKey"/> on untagged retained terms decoded from an
    /// event or snapshot, in memory, as <see cref="GetStampableFormat"/> classifies them. Only the key is
    /// added (after <c>schemaVersion</c>, or last); every other member is copied byte for byte. A payload
    /// that already declares a family, a non-object and an untagged economic-shaped document are returned
    /// unchanged, so the stamp is idempotent.
    /// </summary>
    public static JsonElement StampDeclaredFormat(JsonElement payload)
        => GetStampableFormat(payload) is { } format
            ? SecurityTermsEnvelope.DeclareFormat(payload, format)
            : payload;
}
