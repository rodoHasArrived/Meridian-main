using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

// Bond coupon payload round-trip guards: refuses coupon structure the selected arm does not read, and
// the missing-key/default helpers that decide whether a patch repairs or orphans a coupon value.
public sealed partial class SecurityMasterService
{
    /// <summary>
    /// The vocabulary walk inspects the FLAT discriminant keys the canonical serializer writes. A
    /// bond carrying the legacy nested <c>coupon</c> object instead has no flat <c>couponType</c>
    /// for that walk to see, yet it is lossy in exactly the same way: <c>ToBondCouponStructure</c> has no
    /// nested-coupon fallback, so the record reads as <c>Fixed(couponRate ?? 0)</c> and
    /// re-serializing flattens it to <c>couponType: "Fixed"</c>, dropping the nested index, spread,
    /// and day count. Nothing in this repo writes that shape, but the projection store reads it
    /// deliberately — the Bond descriptor in <c>SecurityTermsProjectionRegistry</c> falls back to
    /// <c>coupon.kind</c>/<c>rate</c>/<c>index</c> "for externally-authored payloads that still use
    /// it", with a regression test pinning the behaviour — so such rows are expected to exist.
    /// <para>The same hole is open one step wider, and a present <c>couponType</c> does not close
    /// it: the codec reads the flat companions ONE ARM AT A TIME, so a document with no
    /// <c>couponType</c> and a populated <c>floatingIndex</c> orphans it on the Fixed default, and
    /// a document declaring <c>couponType: "Fixed"</c> beside that same <c>floatingIndex</c> orphans
    /// it just as completely — the Fixed arm never reads it and the serializer writes it back null.
    /// The projection store reads those columns independently of the discriminant, so the value is
    /// visible to operators right up until the amendment that deletes it. All of these are one
    /// defect — coupon structure the selected arm does not read — so all of them are refused here.</para>
    /// <para>What keeps this free of false positives is the serializer's shape rather than any
    /// gate: <c>Interop.SecurityMaster.assetSpecificTermsJson</c> emits the fields of the arm it is
    /// writing and <see langword="null"/>/<c>[]</c> for every other arm's, so a canonically written
    /// row only ever populates keys its own arm reads and none of them can trip this.</para>
    /// </summary>
    private static void EnsureBondCouponPayloadRoundTripsSafely(
        SecurityProjectionRecord projection,
        JsonElement? assetSpecificTermsPatch)
    {
        var terms = projection.AssetSpecificTerms;
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(projection.AssetClass);
        if (!descriptor.CarriesBondCouponStructure
            || terms.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var couponType = SecurityAssetTermsSchema.Field("Bond", "couponType");
        var declared = string.Join(", ", couponType?.AllowedValues ?? Array.Empty<string>());

        // The SUBMITTED document is held to the same standard as the stored one, and INDEPENDENTLY
        // of it. A patch replaces the terms wholesale, so coupon structure its own discriminant does
        // not name is dropped on the way in — the operator's spreadBps never reaches the record.
        // Checking this only while the stored terms were themselves orphaned left the hole open on
        // every other path, the repair route this guard advertises included: a record refused for an
        // undeclared couponType would be "repaired" by a patch that quietly lost its nested spread.
        // The loss is not self-correcting either — by the time the next amendment refuses the new
        // record the value is already gone.
        if (assetSpecificTermsPatch is JsonElement patch && patch.ValueKind == JsonValueKind.Object)
        {
            var submitted = OrphanedCouponStructure(patch);
            if (submitted.Length > 0)
            {
                throw new SecurityMasterWriteRefusedException(
                    $"The amendment for security '{projection.SecurityId:D}' submits a bond document whose " +
                    $"{string.Join(" and ", submitted)} the canonical codec does not read for the coupon type the " +
                    "document declares, so the amendment would persist the record without them. The write is " +
                    $"refused. Re-submit it with a declared couponType ({declared}) naming the structure and its " +
                    "values in the flat keys the codec reads.");
            }
        }

        var orphaned = OrphanedCouponStructure(terms);
        if (orphaned.Length == 0)
        {
            return;
        }

        // The governed exit, with the repair document already validated above: naming a declared
        // couponType is not enough on its own, because the submitted document can carry an orphan of
        // its own and the amendment would persist it having dropped that value first.
        if (couponType is not null && PatchRepairsValueFor(assetSpecificTermsPatch, couponType))
        {
            return;
        }

        throw new SecurityMasterWriteRefusedException(
            $"Security '{projection.SecurityId:D}' is a bond carrying {string.Join(" and ", orphaned)} that the " +
            "canonical codec does not read for the coupon structure the record resolves to, so re-serializing it " +
            "here would drop that structure. The write is refused. To repair the record here, amend it with a " +
            $"COMPLETE asset-terms document naming a declared couponType ({declared}) and carrying those values in " +
            "the flat keys — the amendment replaces the terms wholesale, so any field the patch omits is dropped " +
            "with them — and note that a deactivation cannot carry a patch, so repair it first.");
    }

    /// <summary>
    /// Every flat key that belongs to a coupon STRUCTURE rather than to the bond, in the order a
    /// refusal names them. Which of them survives a round trip is not a property of the key but of
    /// the arm <c>ToBondCouponStructure</c> selects — see <see cref="CouponArmReads"/> — so the whole set is
    /// checked and the arm decides.
    /// </summary>
    private static readonly string[] FlatCouponStructureKeys =
    [
        "couponRate", "floatingIndex", "spreadBps", "capRate", "floorRate",
        "stepSchedule", "inflationIndex", "inflationBaseIndexValue", "inflationIndexRatio", "dayCount"
    ];

    /// <summary>
    /// The arm <c>ToBondCouponStructure</c> selects for this document. Anything the codec cannot read as a
    /// discriminant — absent, null, a case variant, a non-string token — lands on the same Fixed
    /// default the reader applies (<c>GetOptionalString(json, "couponType") ?? "Fixed"</c>), and so
    /// does an undeclared string, which the read-tolerant arm also decodes as Fixed.
    /// </summary>
    private static string SelectedCouponArm(JsonElement terms)
        => CodecCanRead(terms, "couponType") && terms.TryGetProperty("couponType", out var couponType)
            ? couponType.GetString() ?? "Fixed"
            : "Fixed";

    /// <summary>
    /// True when <paramref name="arm"/> reads <paramref name="key"/>, mirroring the arms of
    /// <c>ToBondCouponStructure</c> exactly. A key the selected arm does not read is not merely ignored: the
    /// F# serializer emits its OWN arm's fields and nulls the rest
    /// (<c>Interop.SecurityMaster.assetSpecificTermsJson</c>), so re-serializing deletes it — a
    /// <c>floatingIndex</c> beside <c>couponType: "Fixed"</c> is as gone as one with no discriminant
    /// at all, even though the projection store reads and shows it.
    /// <para>That same emit-per-arm shape is what keeps this free of false positives: a canonically
    /// written row only ever populates the keys its own arm reads, so no canonical bond can trip it.</para>
    /// </summary>
    private static bool CouponArmReads(string arm, string key) => arm switch
    {
        "Floating" => key is "floatingIndex" or "spreadBps" or "capRate" or "floorRate" or "dayCount",
        "ZeroCoupon" => false,
        "Step" => key is "stepSchedule" or "dayCount",
        "InflationLinked" =>
            key is "couponRate" or "inflationIndex" or "inflationBaseIndexValue" or "inflationIndexRatio" or "dayCount",
        // "Fixed", and the read-tolerant fallback every unreadable or undeclared discriminant lands on.
        _ => key is "couponRate" or "dayCount",
    };

    /// <summary>
    /// The legacy nested <c>coupon</c> members and the flat key each corresponds to, mirroring the
    /// per-field fallback in the Bond descriptor in <c>SecurityTermsProjectionRegistry</c>. That
    /// per-field shape is why the nested object cannot be judged present-or-absent: a row can carry
    /// its discriminant flat and one value only nested.
    /// </summary>
    private static readonly (string Nested, string Flat)[] NestedCouponMembers =
    [
        ("kind", "couponType"), ("rate", "couponRate"), ("index", "floatingIndex"),
        ("spreadBps", "spreadBps"), ("dayCountConvention", "dayCount")
    ];

    /// <summary>
    /// The coupon structure this record carries that the canonical codec would drop on
    /// re-serialization, named for the refusal message. Two independent sources:
    /// <list type="bullet">
    /// <item>flat companions the arm the codec selects does not read, or reads and cannot decode —
    /// see <see cref="OrphansFlatCompanion"/>. The arm is the whole question: the same
    /// <c>spreadBps</c> is economics under <c>couponType: "Floating"</c> and a deleted value under
    /// <c>"Fixed"</c>.</item>
    /// <item>nested <c>coupon</c> members whose flat counterpart is missing — the codec reads only
    /// the flat key, so the nested value is invisible to it and gone on the next write. Judged per
    /// member rather than by the object's presence, so a vestigial duplicate of the flat payload
    /// (and an empty <c>coupon: {}</c>) is correctly ignored, while a mixed row that keeps one value
    /// only nested is correctly refused.</item>
    /// </list>
    /// Only a POPULATED value counts in either case: the canonical serializer emits the companions
    /// its arm does not own as <see langword="null"/>/<c>[]</c>, so a presence-only test would
    /// refuse every ordinary bond.
    /// </summary>
    private static string[] OrphanedCouponStructure(JsonElement terms)
    {
        var orphaned = new List<string>();
        var arm = SelectedCouponArm(terms);

        orphaned.AddRange(FlatCouponStructureKeys.Where(key => OrphansFlatCompanion(terms, arm, key)));

        if (!CodecCanRead(terms, "couponType"))
        {
            // A case-variant spelling is not the discriminant, so the record still loads as a fixed
            // coupon and re-serializing writes a fresh document without the stray key. The same
            // helper the schema-driven walk uses, so the two cannot drift; the exact key, present
            // but undecodable, is that walk's business and it refuses it there.
            orphaned.AddRange(CaseVariantKeys(terms, "couponType"));
        }

        if (terms.TryGetProperty("coupon", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            // The flat counterpart only REPRESENTS the nested value if the codec can decode it.
            // CarriesValue is the wrong question here: the projection store's readers are
            // type-aware and fall through to the nested value when the flat one does not parse
            // (spreadBps: "N/A"), so treating any populated flat value as a representation would
            // discard exactly the nested value the store is reading.
            orphaned.AddRange(NestedCouponMembers
                .Where(member => CarriesValue(nested, member.Nested)
                    && !CodecCanRead(terms, member.Flat)
                    && !MatchesTheMissingKeyDefault(nested, member, arm))
                .Select(member => $"coupon.{member.Nested}"));
        }

        return orphaned.ToArray();
    }

    /// <summary>
    /// True when <paramref name="key"/> holds coupon structure this document would lose. Both halves
    /// of the question matter: <see cref="CouponArmReads"/> decides whether the selected arm reads
    /// the key at all, and <see cref="CodecCanRead"/> decides whether reading it yields anything —
    /// <c>spreadBps: "N/A"</c> IS read by the Floating arm and still lands as null.
    /// <para>A key the arm does not read is lost whatever it holds, zero included. The
    /// substitution that makes an absent rate read as zero
    /// (<c>GetOptionalDecimal(json, "couponRate") ?? 0m</c>) belongs to the Fixed and
    /// InflationLinked arms, and those arms READ the key, so they are judged by
    /// <see cref="CodecCanRead"/> and never reach this branch at all. The arms that do reach it
    /// leave <c>BondTerms.couponRate</c> as <c>None</c>, so re-serializing writes
    /// <c>couponRate: null</c> while the Bond projection reads the flat key without
    /// consulting the arm — a stored <c>fixed_coupon_rate</c> of 0 becomes NULL. That is a stated
    /// value turning into an absence, refused like any other.</para>
    /// </summary>
    private static bool OrphansFlatCompanion(JsonElement terms, string arm, string key)
    {
        if (!CarriesValue(terms, key))
        {
            return false;
        }

        return !CouponArmReads(arm, key) || !CodecCanRead(terms, key);
    }

    /// <summary>True when <paramref name="key"/> holds a JSON number the codec would decode as zero.</summary>
    private static bool IsNumericZero(JsonElement document, string key)
        => document.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
            && number == 0m;

    /// <summary>
    /// True when the nested member says exactly what the codec's documented default for the absent
    /// flat key already produces, so re-serializing canonicalizes the JSON shape and loses nothing.
    /// <para>Two members qualify, each against its own type of default. <c>coupon.kind = "Fixed"</c>
    /// qualifies through <see cref="MissingKeyDefaultFor"/>: <c>ToBondCouponStructure</c> defaults an absent
    /// <c>couponType</c> to <c>Fixed</c> and the projection store reads the nested kind as
    /// <c>Fixed</c>, so both codecs already agree. <c>coupon.rate = 0</c> qualifies because an
    /// absent flat rate reads as <c>0m</c> — but only under an arm that actually performs that
    /// substitution, which is why <paramref name="arm"/> is part of the question; a nested
    /// <c>4.25</c> is not the same number and stays refused under every arm. The remaining members
    /// (index, spread, day count) have no substituted counterpart at all: an absent flat key means
    /// the value is gone, not written another way.</para>
    /// </summary>
    private static bool MatchesTheMissingKeyDefault(
        JsonElement nested,
        (string Nested, string Flat) member,
        string arm)
    {
        if (SecurityAssetTermsSchema.Field("Bond", member.Flat) is not { } field)
        {
            return false;
        }

        // The discriminant's substituted default is a declared vocabulary value.
        if (MissingKeyDefaultFor(field) is { } substituted)
        {
            return nested.TryGetProperty(member.Nested, out var value)
                && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), substituted, StringComparison.Ordinal);
        }

        // The scalar rate's is a NUMBER: GetOptionalDecimal(json, "couponRate") ?? 0m means an
        // absent flat rate reads as exactly the zero a nested rate of 0 states, so both codecs
        // already agree and re-serializing only canonicalizes the shape. That substitution is the
        // Fixed and InflationLinked arms', though, and the arm has to be asked. Under an arm that
        // reads no rate the round trip writes couponRate: null, so the 0 the projection store was
        // reading out of coupon.rate becomes NULL — the nested spelling of the same loss a flat
        // zero suffers there. Any other nested rate IS a different number and stays refused.
        return string.Equals(member.Flat, "couponRate", StringComparison.Ordinal)
            && CouponArmReads(arm, member.Flat)
            && IsNumericZero(nested, member.Nested);
    }

    /// <summary>
    /// True when the canonical codec can actually DECODE <paramref name="key"/> — present,
    /// populated, AND of the JSON kind its declared schema type requires. Presence is not the same
    /// question as readability, and conflating them loses data twice over: <c>couponType: null</c>
    /// is a present property that <c>ToBondCouponStructure</c> treats as the missing-value default, and
    /// <c>spreadBps: "N/A"</c> is a populated value that <c>GetOptionalDecimal</c> cannot parse.
    /// Both are invisible to the codec, so neither can stand in for the value it would drop.
    /// </summary>
    private static bool CodecCanRead(JsonElement document, string key)
    {
        if (!CarriesValue(document, key) || !document.TryGetProperty(key, out var value))
        {
            return false;
        }

        // Each arm mirrors the corresponding reader in SecurityMasterMapping, including its PARSE
        // and not merely its JSON kind. The kind alone is a proxy, and the proxy is wrong in
        // exactly the cases that matter here: 1e100 is a JSON number that TryGetDecimal rejects,
        // and "not-a-date" is a JSON string that DateOnly.TryParse rejects. The projection store's
        // readers parse too, so it falls through to the nested value while a kind-only check would
        // call the flat one a representation and let the nested one be discarded.
        return SecurityAssetTermsSchema.Field("Bond", key)?.Type switch
        {
            SecurityAssetTermFieldType.Decimal =>
                value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetTermFieldType.Integer =>
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SecurityAssetTermFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetTermFieldType.Date =>
                value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out _),
            SecurityAssetTermFieldType.Guid =>
                value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
            SecurityAssetTermFieldType.Array => value.ValueKind == JsonValueKind.Array,
            SecurityAssetTermFieldType.Object => value.ValueKind == JsonValueKind.Object,
            // String, and any undeclared key, decode straight from a non-blank JSON string.
            _ => value.ValueKind == JsonValueKind.String,
        };
    }

    /// <summary>True when <paramref name="key"/> holds a value, not null, blank, or an empty array.</summary>
    private static bool CarriesValue(JsonElement document, string key)
        => document.TryGetProperty(key, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 0)
            && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));

    /// <summary>
    /// True when the amendment's patch settles <paramref name="field"/> itself, so the stored value
    /// is replaced rather than re-serialized. Two forms qualify, and both are POSITIVE instructions:
    /// <list type="bullet">
    /// <item>naming a declared value — the ordinary repair;</item>
    /// <item>an explicit <see langword="null"/>, where <see cref="ClearingIsARepairFor"/> says the
    /// codec decodes an absent key as a genuine absence. Clearing is the honest repair for a value
    /// this node has no declared equivalent for (an <c>exerciseStyle</c> of <c>"Asian"</c>): the
    /// write mapper reads null as None and the serializer emits null, so the amendment persists
    /// exactly what the operator asked for. Refusing it would force them to assert a style the
    /// option does not have, which corrupts the record more than the value it replaces.</item>
    /// </list>
    /// A patch that OMITS the key, blanks it, or repeats an undeclared value is deliberately not an
    /// exit: the write-mode mapping would decode it to the same fallback and complete the very
    /// rewrite the refusal exists to stop.
    /// </summary>
    private static bool PatchRepairsValueFor(JsonElement? assetSpecificTermsPatch, SecurityAssetTermField field)
    {
        if (assetSpecificTermsPatch is not JsonElement patch
            || patch.ValueKind != JsonValueKind.Object
            || !patch.TryGetProperty(field.Key, out var patched))
        {
            return false;
        }

        // Naming a declared value is necessary but not sufficient: the value has to be able to HOLD
        // what the document carries. A repair reading classification "Common" beside a populated
        // preferredTerms decodes to EquityClassification.Common, which owns no preferred block, and
        // the serializer emits it null — so the exit would let a repair silently delete terms the
        // operator explicitly submitted. Same rule for a clear, where the cleared field holds none
        // of them.
        // The document must also not say the same field twice under two spellings. The codec reads
        // the canonical key ordinally and never sees the variant, so a patch carrying both
        // exerciseStyle "American" and ExerciseStyle "European" persists the first and drops the
        // second without trace — the stored-record rule, applied to the document that replaces it.
        if (CaseVariantKeys(patch, field.Key).Length > 0)
        {
            return false;
        }

        if (patched.ValueKind == JsonValueKind.String)
        {
            var named = patched.GetString();
            return field.Allows(named) && !CarriesDependentBlocksTheValueCannotHold(patch, field, named);
        }

        return patched.ValueKind == JsonValueKind.Null
            && ClearingIsARepairFor(field)
            && !CarriesDependentBlocksTheValueCannotHold(patch, field, null);
    }

    /// <summary>
    /// True when an absent or null <paramref name="field"/> round-trips as a genuine absence, so
    /// clearing it is lossless rather than a silent re-typing.
    /// <para>The distinction is the codec's, not the schema's: an optional discriminant whose
    /// missing-key read is <c>None</c> (<c>ParseExerciseStyle</c>, <c>ToEquityClassificationOption</c>)
    /// serializes back as null, while one the codec SUBSTITUTES a declared value for is not cleared
    /// at all — <c>ToBondCouponStructure</c> reads a null <c>couponType</c> as <c>"Fixed"</c>, which is
    /// precisely the rewrite these guards exist to stop. A required field is never clearable: its
    /// reader throws.</para>
    /// </summary>
    private static bool ClearingIsARepairFor(SecurityAssetTermField field)
        => !field.Required && MissingKeyDefaultFor(field) is null;

    /// <summary>
    /// The stored value as the DECODER matches it, which is not always as the record spells it.
    /// <c>ParseExerciseStyle</c> switches on <c>value?.Trim()</c>, so a stored <c>" American "</c>
    /// decodes to <c>American</c> and re-serializes as the canonical spelling: a lossless
    /// canonicalization, and refusing it would freeze a valid record over whitespace. Every other
    /// discriminant switches on the raw string (<c>ToBondCouponStructure</c>,
    /// <c>ToEquityClassificationOption</c>), where padding really does fall through to the
    /// missing-value default and really is lost.
    /// <para>This is a READ question and deliberately not a write one. <c>EnsureDeclaredVocabularies
    /// OnWrite</c> compares the untrimmed value, so a write ASSERTING <c>" American "</c> is still
    /// refused — read tolerance for a value already in a row is not licence to accept a padded one
    /// on the way in. Same reason <see cref="PatchRepairsValueFor"/> stays strict: a repair document
    /// is a write.</para>
    /// </summary>
    private static string? AsTheDecoderMatchesIt(SecurityAssetTermField field, string? value)
        => string.Equals(field.Key, "exerciseStyle", StringComparison.Ordinal) ? value?.Trim() : value;

    /// <summary>
    /// The declared value the canonical codec SUBSTITUTES when a discriminant key is absent or null,
    /// for the one field that substitutes anything: <c>ToBondCouponStructure</c> reads
    /// <c>GetOptionalString(json, "couponType") ?? "Fixed"</c>. This mirrors the codec rather than
    /// the schema — the schema declares what a value may BE, not what the reader invents when the
    /// key is gone — so it lives beside the guards that have to reason about the missing-key read.
    /// </summary>
    private static string? MissingKeyDefaultFor(SecurityAssetTermField field)
        => string.Equals(field.Key, "couponType", StringComparison.Ordinal) ? "Fixed" : null;

    /// <summary>
    /// True when <paramref name="document"/> carries a block that hangs off <paramref name="field"/>'s
    /// vocabulary but that <paramref name="value"/> does not own, so the decode has nowhere to
    /// reattach it and the write would drop it. <see langword="null"/> as the value asks the same
    /// question of a cleared field, which owns none of them.
    /// <para>"Carries" is <see cref="CarriesValue"/>, the same populated test the rest of these
    /// guards use, and deliberately not "is an object or an array". A block of the wrong JSON kind
    /// is not carried more safely for being malformed — <c>preferredTerms: "…"</c> beside a
    /// classification that does not own it is dropped exactly as an object would be, and under one
    /// that DOES own it <c>GetRequiredObject</c> throws rather than passing it through. Recognizing
    /// only the well-formed shapes would have made the malformed ones the way around this check.</para>
    /// </summary>
    private static bool CarriesDependentBlocksTheValueCannotHold(
        JsonElement document,
        SecurityAssetTermField field,
        string? value)
    {
        if (field.Escape is not { } escape)
        {
            return false;
        }

        // The escape's LABEL key is owned by the escape value and nothing else: ToEquityClassification
        // Option reads otherClassification only on the "Other" arms, and the serializer emits it as
        // null for every declared classification. A repair naming "Common" while still carrying
        // otherClassification therefore drops the label — the same silent deletion as a dependent
        // block, one key over. Checking DependentKeys alone missed it because the label is not one
        // of them; it is the escape's own third field.
        if (CarriesValue(document, escape.LabelKey)
            && !string.Equals(value, escape.Value, StringComparison.Ordinal))
        {
            return true;
        }

        return escape.DependentKeys.Any(key => CarriesValue(document, key)
            && !DependentBlockBelongsTo(key, value));
    }

    /// <summary>
    /// Which vocabulary members own each dependent block, mirroring
    /// <c>ToEquityClassificationOption</c> — the only decode with dependent blocks.
    /// <c>preferredTerms</c> is read (via <c>GetRequiredObject</c>) by <c>Preferred</c> and
    /// <c>ConvertiblePreferred</c>, <c>convertibleTerms</c> by <c>Convertible</c> and
    /// <c>ConvertiblePreferred</c>; <c>Common</c> and the <c>Other</c> escape read neither, and the
    /// serializer emits both as <see langword="null"/> for them.
    /// </summary>
    private static bool DependentBlockBelongsTo(string dependentKey, string? value) => dependentKey switch
    {
        "preferredTerms" => value is "Preferred" or "ConvertiblePreferred",
        "convertibleTerms" => value is "Convertible" or "ConvertiblePreferred",
        _ => false,
    };
}
