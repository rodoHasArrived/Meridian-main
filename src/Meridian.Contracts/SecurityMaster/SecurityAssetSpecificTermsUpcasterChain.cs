using System.IO;
using System.Text.Json;
using Meridian.Contracts.Schema;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Converts a structured economic-terms payload (<see cref="EconomicTermsSchema.Current"/>, the
/// nested per-module shape the economic definition adapter writes) into the flat legacy
/// asset-specific-terms shape (<see cref="AssetSpecificTermsSchema.Legacy"/>) that the mapping,
/// resolver, and projection paths read. This is the bridge that closes the cross-family trap where
/// an economic-terms document landing in the asset-specific-terms slot failed every read with
/// "Unsupported schemaVersion '2'".
/// <para>
/// <b>The flat keys are lossy; the payload is not.</b> The flat v1 family is keyed per asset class
/// (a callable bond's <c>callDate</c>, an MBS's <c>currentFactor</c>, a sweep vehicle's
/// <c>programName</c>) and cannot express every economic module. When the caller knows the class it
/// should use <see cref="Convert(JsonElement, string?)"/>, which writes every value the economic
/// document carries at the class's declared flat key. The class-independent
/// <see cref="Convert(JsonElement)"/> is the fallback for an unknown class and can only place the
/// <see cref="BridgedModules"/>; it places nothing from the <see cref="DroppedModules"/>
/// (redemption, call, auction, sweep, financing, issuer, equity behaviour, fund, structured product).
/// Both conversions therefore also carry the complete source document verbatim under
/// <see cref="SecurityTermsEnvelope.RetainedEconomicTermsKey"/>, stamp the result with the explicit
/// flat <see cref="SecurityTermsEnvelope.FormatKey"/>, and mark it with
/// <see cref="FlattenedFromMarkerProperty"/>, so a projection rebuilt through this route keeps every
/// economic value and stays distinguishable from one that always carried the flat terms. The
/// economic definition adapter folds the retained document back into the economic record on the
/// next write, so data the flat keys cannot hold survives a replay-then-amend cycle.
/// </para>
/// </summary>
public sealed class SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster : ISchemaUpcaster<SecurityAssetSpecificTerms>
{
    /// <summary>Shared stateless instance for callers that resolve the upcaster outside DI.</summary>
    public static SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster Instance { get; } = new();

    /// <summary>
    /// The marker written onto every flattened payload, carrying the economic-terms schema version it
    /// was flattened from. Its presence means the flat terms were reconstructed by this lossy route
    /// rather than retained from the original v1 write. The flat readers ignore undeclared keys, so
    /// the marker is inert on every read path and visible to audit.
    /// </summary>
    public const string FlattenedFromMarkerProperty = "flattenedFromEconomicTermsSchemaVersion";

    /// <summary>
    /// The flat dated factor-schedule array StructuredCredit declares
    /// (<see cref="SecurityAssetTermsSchema"/>), and the economic serializer's key for the same
    /// list inside its <c>structuredProduct</c> module. ADR-027 stage 1 increment 4 recovers the
    /// economic list into the flat home.
    /// </summary>
    private const string FactorScheduleEntriesKey = "factorScheduleEntries";

    /// <summary>The economic module that carries <see cref="FactorScheduleEntriesKey"/>.</summary>
    private const string StructuredProductModule = "structuredProduct";

    /// <summary>
    /// The economic module that carries a profile-backed custom asset's envelope and its governed
    /// terms document (ADR-027 stage 3, S3-E4).
    /// </summary>
    private const string ProfileEnvelopeModule = "profileEnvelope";

    /// <summary>The economic-terms modules this conversion reads. Order is the flattening order.</summary>
    public static IReadOnlyList<string> BridgedModules { get; } =
        ["maturity", "coupon", "payment", "accrual", "discount"];

    /// <summary>
    /// The module keys the economic serializer gained after the identity gate (ADR-027 stage 1
    /// increment 6) shipped: the S3-E module homes, in emission order. An image document written
    /// before a key existed lacks it, while the current serializer emits it — null for a class the
    /// module does not describe. <c>SecurityEconomicDefinitionAdapter</c>'s gate treats exactly
    /// that absent-versus-null difference as none, so serializer growth does not re-age every
    /// stored image into carrying its retained document again; every other difference still
    /// fails closed. A module added to the serializer from here on joins this list.
    /// </summary>
    public static IReadOnlyList<string> ModulesAddedAfterIdentityGate { get; } =
        ["exercise", "currencyPair", "notional", ProfileEnvelopeModule];

    /// <summary>
    /// The economic-terms modules the serializer emits that this conversion does not read. Listed
    /// here, rather than left implicit, so the loss is enumerated in code and in the coverage test
    /// (<c>SecurityEconomicTermsV2BridgeCoverageTests</c>) instead of discovered in a restatement.
    /// Eleven joined when ADR-027 stage 1 increment 5 taught the serializer to emit them, and the
    /// S3-E module homes (<c>exercise</c> onward) follow them; the class-aware flattening recovers
    /// parts of some dropped modules into declared flat homes (call schedules, accrual
    /// conventions, factor entries, the S3-E scalar homes), and the rest ride in
    /// <c>retainedEconomicTerms</c> until their own recovery step.
    /// </summary>
    public static IReadOnlyList<string> DroppedModules { get; } =
        ["redemption", "call", "auction", "sweep", "financing", "issuer", "equityBehavior", "fund", "structuredProduct",
         "floatingRate", "tradingParameters", "creditRating", "depositaryReceipt", "dividendSchedule", "covenants",
         "esg", "venue", "multiCalendar", "inflationLinked", "sinkingFund",
         .. ModulesAddedAfterIdentityGate];

    /// <summary>
    /// Every module key an economic-terms document can carry: the union of the bridged and dropped
    /// sets. A module added to the economic serializer must be added to one of the two lists, and
    /// the coverage test fails until it is.
    /// </summary>
    public static IReadOnlyList<string> EconomicTermsModules { get; } =
        [.. BridgedModules, .. DroppedModules];

    /// <summary>
    /// The module keys the historical shape rule consults: exactly the modules the economic
    /// serializer wrote before <see cref="SecurityTermsEnvelope.FormatKey"/> existed. Untagged
    /// payloads predate the tag, so only these keys can mark one as economic; modules serialized
    /// later (ADR-027 stage 1 increment 5) never appear untagged and are deliberately not
    /// consulted — the rule stays frozen with the population it classifies, as does migration
    /// 042's SQL mirror of it. This list never grows; it retires with the shape rule itself
    /// (ADR-024 decision 5).
    /// </summary>
    public static IReadOnlyList<string> ShapeRuleModules { get; } =
        ["maturity", "coupon", "payment", "accrual", "discount",
         "redemption", "call", "auction", "sweep", "financing", "issuer", "equityBehavior", "fund", "structuredProduct"];

    public int FromSchemaVersion => EconomicTermsSchema.Current;

    public int ToSchemaVersion => AssetSpecificTermsSchema.Legacy;

    public SecurityAssetSpecificTerms? Upcast(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Convert(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the payload is an economic-terms document. A payload that declares
    /// <see cref="SecurityTermsEnvelope.FormatKey"/> is classified by that declaration alone, so a
    /// tagged document never depends on its <c>schemaVersion</c> integer; an untagged payload falls
    /// back to <see cref="IsEconomicTermsShape"/>.
    /// </summary>
    public static bool IsEconomicTermsDocument(JsonElement payload)
        => SecurityTermsEnvelope.GetDeclaredFormat(payload) is { } declared
            ? string.Equals(declared, SecurityTermsFormats.Economic, StringComparison.Ordinal)
            : IsEconomicTermsShape(payload);

    /// <summary>
    /// The historical shape rule for untagged payloads: an object stamped
    /// <see cref="EconomicTermsSchema.Current"/> that carries at least one of the
    /// <see cref="ShapeRuleModules"/> keys. The two payload families share one
    /// <c>schemaVersion</c> key, so the integer alone cannot tell a v2 economic-terms document from
    /// a (reserved, never declared) flat v2; the module keys can, because the economic serializer
    /// always writes them and a flat document never does. Payloads written after
    /// <see cref="SecurityTermsEnvelope.FormatKey"/> existed declare their family instead.
    /// </summary>
    public static bool IsEconomicTermsShape(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(payload) != EconomicTermsSchema.Current)
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
    /// Whether a flat asset-specific-terms payload was produced by <see cref="Convert"/> — that is,
    /// reconstructed from an economic-terms document by the lossy route — rather than retained from
    /// an original v1 write.
    /// </summary>
    public static bool WasFlattenedFromEconomicTerms(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty(FlattenedFromMarkerProperty, out var marker)
           && marker.ValueKind == JsonValueKind.Number;

    /// <summary>
    /// Flattens a v2 economic-terms element into a v1 asset-specific-terms element. Only the
    /// <see cref="BridgedModules"/> are read and only fields present in the source are written; the
    /// coupon block's payment frequency and day count win over the payment/accrual blocks' when both
    /// are present (they describe the same economics at different granularities). Every other module
    /// is dropped — see the type summary — and the result carries
    /// <see cref="FlattenedFromMarkerProperty"/> so the loss is visible on the rebuilt record.
    /// </summary>
    public static SecurityAssetSpecificTerms Convert(JsonElement economicTerms)
    {
        EnsureScheduleModulesRecoverable(economicTerms, callSchedulesRecoverable: false, accrualConventionsRecoverable: false);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteFlatEnvelope(writer, SecurityTermsEnvelope.DefaultAssetClassTermsVersion);

            var maturity = GetObject(economicTerms, "maturity");
            WriteIfPresent(writer, "maturityDate", maturity, "maturityDate");
            WriteIfPresent(writer, "issueDate", maturity, "issueDate");
            WriteIfPresent(writer, "effectiveDate", maturity, "effectiveDate");

            var coupon = GetObject(economicTerms, "coupon");
            WriteIfPresent(writer, "couponType", coupon, "couponType");
            WriteIfPresent(writer, "couponRate", coupon, "couponRate");

            var payment = GetObject(economicTerms, "payment");
            if (!WriteIfPresent(writer, "paymentFrequency", coupon, "paymentFrequency"))
            {
                WriteIfPresent(writer, "paymentFrequency", payment, "paymentFrequency");
            }

            var accrual = GetObject(economicTerms, "accrual");
            if (!WriteIfPresent(writer, "dayCount", coupon, "dayCount"))
            {
                WriteIfPresent(writer, "dayCount", accrual, "dayCount");
            }

            WriteIfPresent(writer, "accrualStartDate", accrual, "accrualStartDate");

            var discount = GetObject(economicTerms, "discount");
            WriteIfPresent(writer, "discountRate", discount, "discountRate");
            WriteIfPresent(writer, "yieldRate", discount, "yieldRate");

            WriteRetainedEconomicTerms(writer, economicTerms);
            writer.WriteEndObject();
        }

        using var flattened = JsonDocument.Parse(buffer.ToArray());
        return new SecurityAssetSpecificTerms(flattened.RootElement.Clone(), AssetSpecificTermsSchema.Legacy);
    }

    /// <summary>
    /// Asset-class-aware flattening: inverts the per-class module assignment the economic
    /// definition makes (<c>SecurityMasterLegacyUpgrade.termsFromKind</c>) so every value the
    /// economic-terms document carries lands at the flat key <see cref="SecurityAssetTermsSchema"/>
    /// declares for <paramref name="assetClass"/> — a callable CD's first call date at
    /// <c>callableDate</c>, a bond's at <c>callDate</c>, an MBS tranche's factor at
    /// <c>currentFactor</c>, a deposit's institution at <c>institutionName</c>. Only declared keys are
    /// written (<c>SecurityEconomicTermsV2BridgeCoverageTests</c> asserts it), so the result reads like
    /// an original v1 write for that class.
    /// <para>
    /// What remains lost is what the economic-terms document never carried: a swap's legs, a
    /// bond's subclass, an option's listing detail (the S3-E module homes carry strikes, currency
    /// pairs, notionals and the custom-asset profile document). Since ADR-027 stage 1 increments 1 and 2,
    /// the Call module's call and put lists land in the flat callSchedule and putSchedule arrays,
    /// and the Accrual module's business-day convention and joint payment calendar land in the
    /// scheduleConventions block, for a class that declares those homes — stamped at the held
    /// terms' versions, with the members the economic family never carried left null under
    /// ADR-025's declared read defaults. Economic-only documents that declare schedule content a
    /// class has no home for, that the flat readers could not decode, or that use non-canonical
    /// spellings are refused instead of presenting their flattened projection as an undeclared,
    /// unadjusted bullet.
    /// Those keys are listed by
    /// <see cref="UnrecoverableKeys"/> and the result keeps the <see cref="FlattenedFromMarkerProperty"/>
    /// marker. When <paramref name="assetClass"/> is not a declared class (or is <see langword="null"/>)
    /// this falls back to the class-independent <see cref="Convert(JsonElement)"/>.
    /// </para>
    /// </summary>
    public static SecurityAssetSpecificTerms Convert(JsonElement economicTerms, string? assetClass)
    {
        // ADR-027 stage 3 (S3-E4): a class that must pin a custom profile carries its whole
        // governed terms document on the profile-envelope module. That document IS the row's
        // asset-specific terms, so it is returned as stored — no flattening, no marker and no
        // retained carry, because nothing was reconstructed and nothing is left behind. A
        // document written before the module existed has no such module and takes the routes
        // below unchanged.
        if (TryRecoverProfileDocument(economicTerms, assetClass, out var profileDocument))
        {
            return SecurityAssetSpecificTermsV0ToCurrentUpcaster.Normalize(profileDocument);
        }

        if (!TryGetClassFlattening(assetClass, out var mappings))
        {
            return Convert(economicTerms);
        }

        if (SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsEquityClassification)
        {
            // Structural CommonEquity/CommonShare metadata also labels legacy preferred equity.
            // Absence of preferred modules therefore cannot establish Common classification.
            // The old economic family lacks a complete independent equity discriminant and loses
            // preferred/convertible economics; all economic-only Equity replay must refuse.
            throw new InvalidOperationException(
                "Equity economic replay requires retained legacy asset-specific terms; " +
                "the economic payload cannot reconstruct the complete classification and preferred/convertible terms.");
        }

        // ADR-027 stage 1, increments 1 and 2: a class that declares the flat
        // callSchedule/putSchedule arrays recovers the Call module's lists, and a class that
        // declares the scheduleConventions block recovers the Accrual module's business-day
        // convention and joint payment calendar, instead of refusing them. The entries and block
        // carry what the economic family carried; everything it never carried stays null and
        // reads under ADR-025's declared defaults, so the flattened record declares its calls and
        // conventions rather than silently running an unadjusted bullet.
        var callScheduleSince = SecurityAssetTermsSchema.Field(assetClass!, SecurityVersionedTerms.CallScheduleKey)?.SinceTermsVersion;
        var conventionsSince = SecurityAssetTermsSchema.Field(assetClass!, SecurityVersionedTerms.ScheduleConventionsKey)?.SinceTermsVersion;
        EnsureScheduleModulesRecoverable(
            economicTerms,
            callSchedulesRecoverable: callScheduleSince is not null,
            accrualConventionsRecoverable: conventionsSince is not null);

        var callModule = GetObject(economicTerms, "call");
        IReadOnlyList<JsonElement> callEntries = [];
        IReadOnlyList<JsonElement> putEntries = [];
        if (callScheduleSince is not null)
        {
            (callEntries, putEntries) = CollectScheduleEntries(callModule);
        }

        var writeSchedules = callEntries.Count > 0 || putEntries.Count > 0;

        (string? BusinessDayConvention, IReadOnlyList<string> PaymentCalendars) conventions = (null, []);
        if (conventionsSince is not null)
        {
            conventions = CollectAccrualConventions(GetObject(economicTerms, "accrual"));
        }

        var writeConventions = conventions.BusinessDayConvention is not null || conventions.PaymentCalendars.Count > 0;

        // ADR-027 stage 1, increment 4: a class that declares the typed factorScheduleEntries
        // array (StructuredCredit, Since 1) recovers the economic structured-product module's
        // dated factor observations instead of dropping them into the retained document while the
        // flat row — and every projection built from it — silently loses the schedule.
        IReadOnlyList<(string AsOfDate, decimal Factor)> factorEntries = [];
        if (SecurityAssetTermsSchema.Field(assetClass!, FactorScheduleEntriesKey) is not null)
        {
            factorEntries = CollectFactorScheduleEntries(economicTerms);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            var stamp = SecurityAssetClassCatalog.GetStampFloor(assetClass);
            if (writeSchedules)
            {
                stamp = Math.Max(stamp, callScheduleSince!.Value);
            }

            if (writeConventions)
            {
                stamp = Math.Max(stamp, conventionsSince!.Value);
            }

            WriteFlatEnvelope(writer, stamp);

            // The frozen fill: at or above its Since the block key is present, null when nothing
            // is declared; below it the key is absent (ADR-024 decision 3).
            if (conventionsSince is not null && stamp >= conventionsSince.Value)
            {
                WriteScheduleConventions(writer, conventions);
            }

            if (writeSchedules)
            {
                WriteCallSchedule(writer, callEntries, callModule);
                WritePutSchedule(writer, putEntries);
            }

            if (factorEntries.Count > 0)
            {
                writer.WritePropertyName(FactorScheduleEntriesKey);
                writer.WriteStartArray();
                foreach (var (asOfDate, factor) in factorEntries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("asOfDate", asOfDate);
                    writer.WriteNumber("factor", factor);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            foreach (var mapping in mappings)
            {
                if (mapping.LabelFromFlags is { } flags)
                {
                    foreach (var (module, field, label) in flags)
                    {
                        if (GetObject(economicTerms, module) is JsonElement flagSource
                            && flagSource.TryGetProperty(field, out var flag)
                            && flag.ValueKind == JsonValueKind.True)
                        {
                            writer.WriteString(mapping.FlatKey, label);
                            break;
                        }
                    }

                    continue;
                }

                var written = false;
                foreach (var (module, field) in mapping.Sources)
                {
                    var source = GetObject(economicTerms, module);
                    if (source is not JsonElement element
                        || !element.TryGetProperty(field, out var value)
                        || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    {
                        continue;
                    }

                    writer.WritePropertyName(mapping.FlatKey);
                    if (mapping.EqualsLabel is { } label)
                    {
                        writer.WriteBooleanValue(value.ValueKind == JsonValueKind.String
                                                 && string.Equals(value.GetString(), label, StringComparison.Ordinal));
                    }
                    else if (mapping.Scale is { } scale && value.ValueKind == JsonValueKind.Number)
                    {
                        writer.WriteNumberValue(value.GetDecimal() * scale);
                    }
                    else
                    {
                        value.WriteTo(writer);
                    }

                    written = true;
                    break;
                }

                // A label flag is false when its label is absent: the economic definition writes a
                // null issuerProgram for commercial paper that is not asset-backed, and the flat
                // schema declares isAssetBacked required.
                if (!written && mapping.EqualsLabel is not null)
                {
                    writer.WriteBoolean(mapping.FlatKey, false);
                }
            }

            WriteRetainedEconomicTerms(writer, economicTerms);
            writer.WriteEndObject();
        }

        using var flattened = JsonDocument.Parse(buffer.ToArray());
        return new SecurityAssetSpecificTerms(flattened.RootElement.Clone(), AssetSpecificTermsSchema.Legacy);
    }

    private static bool TryRecoverProfileDocument(JsonElement economicTerms, string? assetClass, out JsonElement document)
    {
        document = default;
        if (string.IsNullOrWhiteSpace(assetClass)
            || !SecurityAssetClassCatalog.GetOrDefault(assetClass).RequiresProfileReference
            || GetObject(economicTerms, ProfileEnvelopeModule) is not JsonElement envelope
            || !envelope.TryGetProperty("terms", out var terms)
            || terms.ValueKind != JsonValueKind.Object
            || !terms.TryGetProperty("customProfileId", out var profileId)
            || profileId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(profileId.GetString()))
        {
            return false;
        }

        document = terms;
        return true;
    }

    // An economic-only event cannot recover the complete flat schedule declaration: Accrual
    // lacks roll, unit, lag and accrual-adjustment terms; Call lists lack windows and notice terms.
    // Retaining those modules under an ignored key would let projection/amortization run a bullet.
    // Only conversion uses this guard. Retained flat rows keep their established replay path.
    // Since ADR-027 stage 1 increments 1 and 2, a class whose schema declares the flat
    // callSchedule and putSchedule arrays passes callSchedulesRecoverable, and one that declares
    // the scheduleConventions block passes accrualConventionsRecoverable: the Call module's lists
    // and the Accrual module's conventions land in those declared homes instead of being refused.
    // Only the serializer's canonical module spellings are recoverable; case variants keep the old
    // refusal, and the class-independent route keeps refusing everything: it has no declared keys
    // to map onto.
    private static void EnsureScheduleModulesRecoverable(
        JsonElement economicTerms,
        bool callSchedulesRecoverable,
        bool accrualConventionsRecoverable)
    {
        if (economicTerms.ValueKind != JsonValueKind.Object)
            return;
        foreach (var module in economicTerms.EnumerateObject())
        {
            if (module.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (module.Name.Equals("accrual", StringComparison.OrdinalIgnoreCase)
                && (!accrualConventionsRecoverable || !module.Name.Equals("accrual", StringComparison.Ordinal)))
            {
                foreach (var member in module.Value.EnumerateObject())
                {
                    if ((member.Name.Equals("businessDayConvention", StringComparison.OrdinalIgnoreCase)
                         || member.Name.Equals("holidayCalendar", StringComparison.OrdinalIgnoreCase))
                        && member.Value.ValueKind != JsonValueKind.Null)
                        throw UnrecoverableSchedule();
                }
            }
            else if (module.Name.Equals("call", StringComparison.OrdinalIgnoreCase)
                     && (!callSchedulesRecoverable || !module.Name.Equals("call", StringComparison.Ordinal)))
            {
                // Only the serializer's canonical `call` module is recoverable; a case-variant
                // module is not the economic writer's shape, the conversion will not read it, and
                // letting it pass would silently drop a declared schedule.
                foreach (var member in module.Value.EnumerateObject())
                {
                    if ((member.Name.Equals(SecurityVersionedTerms.CallScheduleKey, StringComparison.OrdinalIgnoreCase)
                         || member.Name.Equals(SecurityVersionedTerms.PutScheduleKey, StringComparison.OrdinalIgnoreCase))
                        && member.Value.ValueKind != JsonValueKind.Null
                        && (member.Value.ValueKind != JsonValueKind.Array || member.Value.GetArrayLength() > 0))
                        throw UnrecoverableSchedule();
                }
            }
        }
    }

    /// <summary>
    /// The dated call and put entries of the economic Call module, validated under the frozen
    /// written form's rules: a schedule member that is not an array (or null) is malformed and
    /// refuses like any other unrecoverable schedule; a JSON-null or all-null entry is unset and
    /// skipped ("a re-serialization drops it"); an entry with content but no date is populated and
    /// refuses, exactly as a flat write would. Member names match the old guard's tolerance
    /// (ordinal-ignore-case), so a case-variant key cannot smuggle a schedule past validation.
    /// </summary>
    private static (IReadOnlyList<JsonElement> Calls, IReadOnlyList<JsonElement> Puts) CollectScheduleEntries(JsonElement? callModule)
    {
        if (callModule is not JsonElement module)
        {
            return ([], []);
        }

        var calls = new List<JsonElement>();
        var puts = new List<JsonElement>();
        foreach (var member in module.EnumerateObject())
        {
            if (member.Name.Equals(SecurityVersionedTerms.CallScheduleKey, StringComparison.OrdinalIgnoreCase))
            {
                RefuseUnlessCanonicalSpelling(member, SecurityVersionedTerms.CallScheduleKey);
                CollectDatedEntries(member.Value, "callDt", calls);
            }
            else if (member.Name.Equals(SecurityVersionedTerms.PutScheduleKey, StringComparison.OrdinalIgnoreCase))
            {
                RefuseUnlessCanonicalSpelling(member, SecurityVersionedTerms.PutScheduleKey);
                CollectDatedEntries(member.Value, "putDt", puts);
            }
        }

        return (calls, puts);
    }

    /// <summary>
    /// The Accrual module's recoverable conventions (ADR-027 stage 1, increment 2): the
    /// business-day convention, which must decode under the flat reader's own parser
    /// (<see cref="SecurityTermConventions.TryParseBusinessDayConvention"/>) and is kept in its
    /// verbatim spelling, and the joint calendar id, which inverts
    /// <c>BusinessDayCalendars.JointCalendarId</c> exactly: the <c>+</c>-joined parts become the
    /// block's payment calendars (the forward rule already canonicalized trim, case-dedup and
    /// order, so the inversion is lossless for the canonical form). A declared value the flat
    /// side could not decode — a non-string, a blank calendar part, an unrecognized convention
    /// spelling, or a case-variant member that declares content — refuses, exactly as the old
    /// guard did, instead of being recovered as text the schedule kernel would later reject or a
    /// variant the conversion would ignore.
    /// </summary>
    private static (string? BusinessDayConvention, IReadOnlyList<string> PaymentCalendars) CollectAccrualConventions(JsonElement? accrualModule)
    {
        if (accrualModule is not JsonElement module)
        {
            return (null, []);
        }

        string? businessDayConvention = null;
        IReadOnlyList<string> paymentCalendars = [];
        foreach (var member in module.EnumerateObject())
        {
            if (member.Name.Equals("businessDayConvention", StringComparison.OrdinalIgnoreCase))
            {
                RefuseUnlessCanonicalScalarSpelling(member, "businessDayConvention");
                if (member.Value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (member.Value.ValueKind != JsonValueKind.String
                    || !SecurityTermConventions.TryParseBusinessDayConvention(member.Value.GetString(), out _))
                {
                    throw UnrecoverableSchedule();
                }

                businessDayConvention = member.Value.GetString();
            }
            else if (member.Name.Equals("holidayCalendar", StringComparison.OrdinalIgnoreCase))
            {
                RefuseUnlessCanonicalScalarSpelling(member, "holidayCalendar");
                if (member.Value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (member.Value.ValueKind != JsonValueKind.String)
                {
                    throw UnrecoverableSchedule();
                }

                var parts = member.Value.GetString()!.Split('+');
                if (parts.Length == 0 || parts.Any(static part => string.IsNullOrWhiteSpace(part)))
                {
                    throw UnrecoverableSchedule();
                }

                paymentCalendars = parts.Select(static part => part.Trim()).ToArray();
            }
        }

        return (businessDayConvention, paymentCalendars);
    }

    private static void RefuseUnlessCanonicalScalarSpelling(JsonProperty member, string canonicalKey)
    {
        if (!member.Name.Equals(canonicalKey, StringComparison.Ordinal)
            && member.Value.ValueKind is not JsonValueKind.Null)
        {
            throw UnrecoverableSchedule();
        }
    }

    /// <summary>
    /// The structured-product module's dated factor observations (ADR-027 stage 1, increment 4),
    /// validated under the flat schema's own rules before they land in the declared
    /// <c>factorScheduleEntries</c> home: each entry carries the canonical <c>asOfDate</c>
    /// (decodable ISO <c>yyyy-MM-dd</c>) and <c>factor</c> (a number in [0, 1]); dates are
    /// distinct and, ordered by date, factors never increase — the same invariants the domain
    /// validation (<c>structured_credit_factor_schedule_invalid</c>) enforces on a flat write.
    /// A JSON-null or all-null entry is unset and skipped. Anything else that declares content —
    /// a non-array list, a non-object entry, an undecodable date, an out-of-range or missing
    /// factor, a member the economic serializer never writes, or a case-variant module or key
    /// spelling — refuses instead of being recovered as a schedule the flat readers would reject
    /// or silently drop.
    /// </summary>
    private static IReadOnlyList<(string AsOfDate, decimal Factor)> CollectFactorScheduleEntries(JsonElement economicTerms)
    {
        if (economicTerms.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var entries = new List<(DateOnly Date, string AsOfDate, decimal Factor)>();
        foreach (var module in economicTerms.EnumerateObject())
        {
            if (!module.Name.Equals(StructuredProductModule, StringComparison.OrdinalIgnoreCase)
                || module.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var canonicalModule = module.Name.Equals(StructuredProductModule, StringComparison.Ordinal);
            foreach (var member in module.Value.EnumerateObject())
            {
                if (!member.Name.Equals(FactorScheduleEntriesKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var declaresContent = member.Value.ValueKind != JsonValueKind.Null
                    && (member.Value.ValueKind != JsonValueKind.Array || member.Value.GetArrayLength() > 0);
                if (!canonicalModule || !member.Name.Equals(FactorScheduleEntriesKey, StringComparison.Ordinal))
                {
                    // A case-variant module or key is not the economic serializer's shape: the
                    // conversion only reads the canonical spelling, so a variant that declares
                    // content refuses instead of passing while its schedule is silently ignored.
                    if (declaresContent)
                    {
                        throw UnrecoverableFactorSchedule();
                    }

                    continue;
                }

                CollectFactorEntries(member.Value, entries);
            }
        }

        if (entries.Count > 1)
        {
            var ordered = entries.OrderBy(static entry => entry.Date).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i].Date == ordered[i - 1].Date || ordered[i].Factor > ordered[i - 1].Factor)
                {
                    throw UnrecoverableFactorSchedule();
                }
            }
        }

        return entries.Select(static entry => (entry.AsOfDate, entry.Factor)).ToArray();
    }

    private static void CollectFactorEntries(JsonElement list, List<(DateOnly Date, string AsOfDate, decimal Factor)> entries)
    {
        if (list.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            throw UnrecoverableFactorSchedule();
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw UnrecoverableFactorSchedule();
            }

            DateOnly? date = null;
            string? asOfDate = null;
            decimal? factor = null;
            var populated = false;
            foreach (var member in entry.EnumerateObject())
            {
                if (member.Value.ValueKind is JsonValueKind.Null)
                {
                    continue;
                }

                populated = true;
                if (member.Name.Equals("asOfDate", StringComparison.Ordinal)
                    && member.Value.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(member.Value.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    date = parsed;
                    asOfDate = member.Value.GetString();
                }
                else if (member.Name.Equals("factor", StringComparison.Ordinal)
                         && member.Value.ValueKind == JsonValueKind.Number
                         && member.Value.TryGetDecimal(out var value)
                         && value is >= 0m and <= 1m)
                {
                    factor = value;
                }
                else
                {
                    // A populated member the frozen written form cannot carry — a variant or
                    // foreign key, an undecodable date, an out-of-range or non-numeric factor —
                    // would be silently dropped by the flat write, so it refuses instead.
                    throw UnrecoverableFactorSchedule();
                }
            }

            if (!populated)
            {
                continue;
            }

            if (date is not { } entryDate || asOfDate is null || factor is not { } entryFactor)
            {
                throw UnrecoverableFactorSchedule();
            }

            entries.Add((entryDate, asOfDate, entryFactor));
        }
    }

    /// <summary>
    /// Writes the flat <c>scheduleConventions</c> block in the frozen ADR-024 form: null when
    /// nothing is declared, otherwise all twelve members in alphabetical order with null or []
    /// for an unset one. The economic family carried only the business-day convention and the
    /// joint calendar; the schedule-build members it never carried stay null and read under
    /// ADR-025's declared defaults.
    /// </summary>
    private static void WriteScheduleConventions(Utf8JsonWriter writer, (string? BusinessDayConvention, IReadOnlyList<string> PaymentCalendars) conventions)
    {
        writer.WritePropertyName(SecurityVersionedTerms.ScheduleConventionsKey);
        if (conventions.BusinessDayConvention is null && conventions.PaymentCalendars.Count == 0)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNull("accrualAdjusted");
        WriteStringOrNull(writer, "businessDayConvention", conventions.BusinessDayConvention);
        writer.WriteNull("couponCashBasis");
        writer.WriteNull("couponRateUnit");
        writer.WriteNull("direction");
        writer.WriteNull("endOfMonthRule");
        writer.WriteNull("firstRegularDate");
        writer.WriteNull("lastRegularDate");
        writer.WritePropertyName("paymentCalendars");
        writer.WriteStartArray();
        foreach (var calendar in conventions.PaymentCalendars)
        {
            writer.WriteStringValue(calendar);
        }

        writer.WriteEndArray();
        writer.WriteNull("paymentLagBusinessDays");
        writer.WriteNull("rollConvention");
        writer.WriteNull("stubPolicy");
        writer.WriteEndObject();
    }

    /// <summary>
    /// A case-variant schedule key is not the economic serializer's shape: the conversion only
    /// reads the canonical spelling, so a variant that declares content refuses — exactly the old
    /// guard's rule — instead of passing while its schedule is silently ignored.
    /// </summary>
    private static void RefuseUnlessCanonicalSpelling(JsonProperty member, string canonicalKey)
    {
        if (member.Name.Equals(canonicalKey, StringComparison.Ordinal))
        {
            return;
        }

        if (member.Value.ValueKind != JsonValueKind.Null
            && (member.Value.ValueKind != JsonValueKind.Array || member.Value.GetArrayLength() > 0))
        {
            throw UnrecoverableSchedule();
        }
    }

    private static void CollectDatedEntries(JsonElement list, string dateField, List<JsonElement> entries)
    {
        if (list.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            throw UnrecoverableSchedule();
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw UnrecoverableSchedule();
            }

            // The date must decode under the flat reader's frozen semantics (ISO yyyy-MM-dd): an
            // entry whose date string the flat side would drop is a populated schedule term that
            // cannot be recovered, so it refuses below instead of being emitted and silently lost.
            var hasDate = entry.TryGetProperty(dateField, out var date)
                && date.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
            if (hasDate)
            {
                entries.Add(entry);
                continue;
            }

            foreach (var member in entry.EnumerateObject())
            {
                if (member.Value.ValueKind is not JsonValueKind.Null)
                {
                    // Populated but dateless: a flat write refuses it, so the conversion does too.
                    throw UnrecoverableSchedule();
                }
            }
        }
    }

    /// <summary>
    /// Writes the flat <c>callSchedule</c> array in the frozen ADR-024 form (every member, in
    /// alphabetical order, null when unset) from the economic Call module's entries
    /// (<c>callDt</c>, <c>callPx</c>, <c>isParCall</c>, <c>callType</c>). The conversion rules are
    /// the increment's declared semantics: <c>callPx</c> is documented as a percentage of par, so a
    /// written price carries <c>priceBasis = "PercentOfPar"</c>; a par call without a price writes
    /// 100 at that basis, which is what a par call means; <c>callType</c> "MakeWhole" is a price
    /// type (with the module-level <c>makeWholeSpreadBps</c>), while every other label is an
    /// exercise style carried back verbatim — the flat field preserves undeclared style strings
    /// (<c>WithVerbatimUndeclaredValue</c>) and the economic upgrade copies them into
    /// <c>callType</c> unchanged, so the recovery keeps them rather than dropping them. Members
    /// the economic family never carried (windows, notice terms, partial redemption) stay null
    /// and read under ADR-025's declared defaults.
    /// </summary>
    private static void WriteCallSchedule(Utf8JsonWriter writer, IReadOnlyList<JsonElement> entries, JsonElement? callModule)
    {
        var makeWholeSpreadBps = callModule is JsonElement module
            && module.TryGetProperty("makeWholeSpreadBps", out var spread)
            && spread.ValueKind == JsonValueKind.Number
                ? spread.GetDecimal()
                : (decimal?)null;

        writer.WritePropertyName(SecurityVersionedTerms.CallScheduleKey);
        writer.WriteStartArray();
        foreach (var entry in entries)
        {
            var callType = entry.TryGetProperty("callType", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
            // The economic writer's own inverse (SecurityMasterLegacyUpgrade.economicCalls):
            // callType carries "MakeWhole" when the flat entry priced that way, else the flat
            // entry's exerciseStyle verbatim — including labels outside the declared vocabulary,
            // which the flat field preserves (WithVerbatimUndeclaredValue). Dropping an
            // undeclared label here would lose it on the recovery round-trip, so every
            // non-MakeWhole label goes back to exerciseStyle unchanged.
            var isMakeWhole = string.Equals(callType, "MakeWhole", StringComparison.Ordinal);
            var isExerciseStyle = callType is not null && !isMakeWhole;
            var callPrice = entry.TryGetProperty("callPx", out var price) && price.ValueKind == JsonValueKind.Number
                ? price.GetDecimal()
                : entry.TryGetProperty("isParCall", out var par) && par.ValueKind == JsonValueKind.True
                    ? 100m
                    : (decimal?)null;

            writer.WriteStartObject();
            writer.WritePropertyName("callDate");
            entry.GetProperty("callDt").WriteTo(writer);
            writer.WriteNull("callEndDate");
            WriteNumberOrNull(writer, "callPrice", callPrice);
            writer.WriteNull("exerciseFrequency");
            WriteStringOrNull(writer, "exerciseStyle", isExerciseStyle ? callType : null);
            WriteNumberOrNull(writer, "makeWholeSpreadBps", isMakeWhole ? makeWholeSpreadBps : null);
            writer.WriteNull("maximumNoticeDays");
            writer.WriteNull("minimumNoticeDays");
            writer.WriteNull("noticeDayType");
            writer.WriteNull("partialRedemption");
            WriteStringOrNull(writer, "priceBasis", callPrice is null ? null : "PercentOfPar");
            WriteStringOrNull(writer, "priceType", isMakeWhole ? "MakeWhole" : callPrice is null ? null : "Fixed");
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the flat <c>putSchedule</c> array in the frozen ADR-024 form from the economic Call
    /// module's put entries (<c>putDt</c>, <c>putPx</c>); <c>putPx</c> follows <c>callPx</c>'s
    /// percentage-of-par documentation. The flat writer emits both arrays together, so this is
    /// written (as <c>[]</c> when no puts are declared) whenever the call schedule is.
    /// </summary>
    private static void WritePutSchedule(Utf8JsonWriter writer, IReadOnlyList<JsonElement> entries)
    {
        writer.WritePropertyName(SecurityVersionedTerms.PutScheduleKey);
        writer.WriteStartArray();
        foreach (var entry in entries)
        {
            var putPrice = entry.TryGetProperty("putPx", out var price) && price.ValueKind == JsonValueKind.Number
                ? price.GetDecimal()
                : (decimal?)null;

            writer.WriteStartObject();
            writer.WriteNull("exerciseFrequency");
            writer.WriteNull("exerciseStyle");
            writer.WriteNull("maximumNoticeDays");
            writer.WriteNull("minimumNoticeDays");
            writer.WriteNull("noticeDayType");
            WriteStringOrNull(writer, "priceBasis", putPrice is null ? null : "PercentOfPar");
            writer.WritePropertyName("putDate");
            entry.GetProperty("putDt").WriteTo(writer);
            writer.WriteNull("putEndDate");
            WriteNumberOrNull(writer, "putPrice", putPrice);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteNumberOrNull(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is { } text)
        {
            writer.WriteString(name, text);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static InvalidOperationException UnrecoverableSchedule()
        => new("Declared schedule economic replay requires retained legacy asset-specific terms; "
            + "the economic payload cannot reconstruct complete schedule conventions or exercise windows and notice terms.");

    private static InvalidOperationException UnrecoverableFactorSchedule()
        => new("Declared factor-schedule economic replay requires retained legacy asset-specific terms; "
            + "the economic payload's factor entries cannot be recovered into the flat schedule the amortization readers trust.");

    /// <summary>
    /// The flat keys <see cref="Convert(JsonElement, string?)"/> can reconstruct for
    /// <paramref name="assetClass"/>; empty for a class with no class-aware flattening.
    /// </summary>
    public static IReadOnlyList<string> RecoverableKeys(string assetClass)
        => TryGetClassFlattening(assetClass, out var mappings)
            ? mappings.Select(static mapping => mapping.FlatKey).ToArray()
            : [];

    /// <summary>
    /// The declared flat keys for <paramref name="assetClass"/> that no economic-terms module
    /// carries, so a projection rebuilt through this route cannot recover them. Required keys in
    /// this list are why the rebuilt record still carries the <see cref="FlattenedFromMarkerProperty"/>.
    /// </summary>
    public static IReadOnlyList<string> UnrecoverableKeys(string assetClass)
    {
        if (!SecurityAssetTermsSchema.TryGetFields(assetClass, out var fields))
        {
            return [];
        }

        var recoverable = RecoverableKeys(assetClass).ToHashSet(StringComparer.Ordinal);
        return fields.Select(static field => field.Key).Where(key => !recoverable.Contains(key)).ToArray();
    }

    private static bool TryGetClassFlattening(string? assetClass, out IReadOnlyList<FlatTermMapping> mappings)
    {
        mappings = [];
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return false;
        }

        foreach (var (name, candidate) in ClassFlattenings)
        {
            if (string.Equals(name, assetClass, StringComparison.OrdinalIgnoreCase))
            {
                mappings = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One flat key and the economic-terms <c>(module, field)</c> sources that feed it, in priority
    /// order. <see cref="EqualsLabel"/> turns a label into a flag (commercial paper's
    /// <c>isAssetBacked</c> is carried as <c>issuer.issuerProgram = "AssetBacked"</c>).
    /// <see cref="Scale"/> multiplies a numeric source on the way back, inverting a unit change the
    /// upgrade made (a CDS coupon is quoted in basis points but carried as a rate).
    /// </summary>
    private sealed record FlatTermMapping(
        string FlatKey,
        (string Module, string Field)[] Sources,
        string? EqualsLabel = null,
        (string Module, string Field, string Label)[]? LabelFromFlags = null,
        decimal? Scale = null);

    private static FlatTermMapping Map(string flatKey, params (string Module, string Field)[] sources) => new(flatKey, sources);

    /// <summary>
    /// The inverse of <see cref="EqualsLabel"/>: the first flag that is <c>true</c> writes its label
    /// as the flat value; when no flag is set the key is left absent, because the economic document
    /// does not carry the label itself (a CLO's kind is not recoverable from its IO/PO flags).
    /// </summary>
    private static FlatTermMapping LabelFromFlags(string flatKey, params (string Module, string Field, string Label)[] flags)
        => new(flatKey, [], LabelFromFlags: flags);

    private static readonly (string Module, string Field) MaturityDate = ("maturity", "maturityDate");

    /// <summary>
    /// Per-class inverse of <c>SecurityMasterLegacyUpgrade.termsFromKind</c>. Kept beside the
    /// class-independent bridge so the two are reviewed together; the coverage test asserts every
    /// target key is declared for its class and that a fully populated v1 record survives
    /// v1 → economic → v1 at every key listed here.
    /// </summary>
    private static readonly IReadOnlyList<(string AssetClass, IReadOnlyList<FlatTermMapping> Mappings)> ClassFlattenings =
    [
        ("Equity", [Map("shareClass", ("equityBehavior", "shareClass")), Map("votingRightsCat", ("equityBehavior", "votingRights"))]),
        // ADR-027 stage 3 (S3-E): the exercise module (and the contract multiplier on
        // tradingParameters) recovers the strike fields termsFromKind now feeds it from.
        ("Option",
        [
            Map("underlyingId", ("exercise", "underlyingSecurityId")),
            Map("putCall", ("exercise", "optionRight")),
            Map("strike", ("exercise", "strikePrice")),
            Map("expiry", MaturityDate),
            Map("multiplier", ("tradingParameters", "contractMultiplier")),
            Map("exerciseStyle", ("exercise", "exerciseStyle"))
        ]),
        ("Future", [Map("expiry", MaturityDate), Map("multiplier", ("tradingParameters", "contractMultiplier"))]),
        ("FxSpot", [Map("baseCurrency", ("currencyPair", "baseCurrency")), Map("quoteCurrency", ("currencyPair", "quoteCurrency"))]),
        ("CryptoCurrency", [Map("baseCurrency", ("currencyPair", "baseCurrency")), Map("quoteCurrency", ("currencyPair", "quoteCurrency"))]),
        ("Bond",
        [
            Map("maturity", MaturityDate),
            Map("issueDate", ("maturity", "issueDate")),
            Map("couponType", ("coupon", "couponType")),
            Map("couponRate", ("coupon", "couponRate")),
            Map("dayCount", ("coupon", "dayCount"), ("accrual", "dayCount")),
            Map("paymentFrequency", ("coupon", "paymentFrequency"), ("payment", "paymentFrequency")),
            Map("isCallable", ("call", "isCallable")),
            Map("callDate", ("call", "firstCallDate")),
            Map("issuerName", ("issuer", "issuerName"))
        ]),
        ("Deposit",
        [
            Map("depositType", ("issuer", "issuerProgram")),
            Map("institutionName", ("issuer", "institutionName")),
            Map("maturity", MaturityDate),
            Map("interestRate", ("coupon", "couponRate")),
            Map("dayCount", ("coupon", "dayCount")),
            Map("isCallable", ("call", "isCallable"))
        ]),
        ("MoneyMarketFund",
        [
            Map("fundFamily", ("fund", "fundFamily")),
            Map("sweepEligible", ("fund", "sweepEligible")),
            Map("weightedAverageMaturityDays", ("fund", "weightedAverageMaturityDays")),
            Map("liquidityFeeEligible", ("fund", "liquidityFeeEligible"))
        ]),
        ("CertificateOfDeposit",
        [
            Map("issuerName", ("issuer", "issuerName")),
            Map("maturity", MaturityDate),
            Map("couponRate", ("coupon", "couponRate")),
            Map("callableDate", ("call", "firstCallDate")),
            Map("dayCount", ("coupon", "dayCount"))
        ]),
        ("CommercialPaper",
        [
            Map("issuerName", ("issuer", "issuerName")),
            Map("maturity", MaturityDate),
            Map("discountRate", ("discount", "discountRate")),
            Map("dayCount", ("coupon", "dayCount")),
            new FlatTermMapping("isAssetBacked", [("issuer", "issuerProgram")], EqualsLabel: "AssetBacked")
        ]),
        ("TreasuryBill",
        [
            Map("maturity", MaturityDate),
            Map("auctionDate", ("auction", "auctionDate")),
            Map("cusip", ("issuer", "issuerProgram")),
            Map("discountRate", ("discount", "discountRate"))
        ]),
        ("Repo",
        [
            Map("counterparty", ("financing", "counterparty")),
            Map("startDate", ("financing", "openDate"), ("maturity", "effectiveDate")),
            Map("endDate", ("financing", "closeDate"), MaturityDate),
            Map("repoRate", ("discount", "yieldRate")),
            Map("collateralType", ("financing", "collateralType")),
            Map("haircut", ("financing", "haircut"))
        ]),
        ("CashSweep",
        [
            Map("programName", ("sweep", "programName")),
            Map("sweepVehicleType", ("sweep", "sweepVehicleType")),
            Map("sweepFrequency", ("sweep", "sweepFrequency")),
            Map("targetAccountType", ("sweep", "targetAccountType")),
            Map("yieldRate", ("discount", "yieldRate"))
        ]),
        ("OtherSecurity",
        [
            Map("maturity", MaturityDate),
            Map("issuerName", ("issuer", "issuerName")),
            Map("settlementType", ("issuer", "issuerProgram"))
        ]),
        ("Swap", [Map("effectiveDate", ("maturity", "effectiveDate")), Map("maturityDate", MaturityDate)]),
        ("DirectLoan", [Map("borrower", ("issuer", "issuerName")), Map("maturity", MaturityDate)]),
        ("StructuredCredit",
        [
            Map("tranche", ("structuredProduct", "trancheClass")),
            Map("poolId", ("structuredProduct", "poolIdentifier"), ("issuer", "issuerName")),
            Map("collateralType", ("structuredProduct", "collateralType"), ("issuer", "issuerProgram")),
            Map("originalFace", ("structuredProduct", "notionalBalance")),
            Map("currentFactor", ("structuredProduct", "factor")),
            Map("couponOrIndex", ("coupon", "couponType")),
            // The trustee-report pointer is evidence lineage on the structured-product module, not a
            // coupon frequency.
            Map("factorSchedule", ("structuredProduct", "factorScheduleReference")),
            Map("maturity", MaturityDate),
            Map("weightedAverageCoupon", ("structuredProduct", "weightedAvgCoupon")),
            Map("weightedAverageMaturityMonths", ("structuredProduct", "weightedAvgMaturityMonths")),
            Map("weightedAverageLoanAgeMonths", ("structuredProduct", "weightedAvgLoanAgeMos")),
            LabelFromFlags(
                "structuredCreditKind",
                ("structuredProduct", "isInterestOnly", "InterestOnly"),
                ("structuredProduct", "isPrincipalOnly", "PrincipalOnly"))
        ]),
        ("PrivateFundInterest",
        [
            Map("gpSponsor", ("issuer", "issuerName"), ("fund", "fundFamily")),
            Map("strategy", ("issuer", "issuerProgram")),
            Map("commitment", ("notional", "committedAmount")),
            Map("fundedAmount", ("notional", "fundedAmount")),
            Map("unfundedAmount", ("notional", "unfundedAmount"))
        ]),
        ("PrivateCompanyEquity",
        [
            Map("issuer", ("issuer", "issuerName")),
            Map("shareClass", ("equityBehavior", "shareClass")),
            Map("round", ("issuer", "issuerProgram"))
        ]),
        ("RealEstateHolding", [Map("propertyType", ("issuer", "issuerProgram")), Map("sponsor", ("issuer", "issuerName"))]),
        ("CommitmentGuarantee",
        [
            Map("counterparty", ("issuer", "issuerName")),
            Map("beneficiary", ("issuer", "issuerProgram")),
            Map("effectiveDate", ("maturity", "effectiveDate")),
            Map("expiryDate", MaturityDate),
            Map("committedAmount", ("notional", "committedAmount")),
            Map("unfundedAmount", ("notional", "unfundedAmount"))
        ]),
        ("Warrant",
        [
            Map("underlyingId", ("exercise", "underlyingSecurityId")),
            Map("warrantType", ("exercise", "optionRight")),
            Map("strike", ("exercise", "strikePrice")),
            Map("expiry", MaturityDate),
            Map("multiplier", ("tradingParameters", "contractMultiplier"))
        ]),
        ("FxForward",
        [
            Map("baseCurrency", ("currencyPair", "baseCurrency")),
            Map("quoteCurrency", ("currencyPair", "quoteCurrency")),
            Map("forwardRate", ("currencyPair", "forwardRate")),
            Map("notional", ("notional", "notional")),
            Map("tradeDate", ("maturity", "effectiveDate")),
            Map("valueDate", MaturityDate)
        ]),
        ("NonDeliverableForward",
        [
            Map("baseCurrency", ("currencyPair", "baseCurrency")),
            Map("quoteCurrency", ("currencyPair", "quoteCurrency")),
            Map("settlementCurrency", ("currencyPair", "settlementCurrency")),
            Map("forwardRate", ("currencyPair", "forwardRate")),
            Map("notional", ("notional", "notional")),
            Map("fixingDate", ("maturity", "effectiveDate")),
            Map("valueDate", MaturityDate)
        ]),
        ("CreditDefaultSwap",
        [
            Map("effectiveDate", ("maturity", "effectiveDate")),
            Map("maturityDate", MaturityDate),
            Map("paymentFrequency", ("coupon", "paymentFrequency")),
            Map("dayCount", ("coupon", "dayCount")),
            // The upgrade stores FixedCouponBps / 10000 as coupon.couponRate.
            new FlatTermMapping("fixedCouponBps", [("coupon", "couponRate")], Scale: 10000m),
            Map("notional", ("notional", "notional"))
        ]),
        ("Swaption",
        [
            Map("payerReceiver", ("exercise", "optionRight")),
            Map("exerciseStyle", ("exercise", "exerciseStyle")),
            Map("strikeRate", ("exercise", "strikeRate")),
            Map("expiry", MaturityDate),
            Map("underlyingEffectiveDate", ("maturity", "effectiveDate")),
            Map("notional", ("notional", "notional"))
        ]),
        ("CapFloor",
        [
            Map("effectiveDate", ("maturity", "effectiveDate")),
            Map("maturityDate", MaturityDate),
            // ADR-027 stage 1 increment 6: the floatingRate module (serialized since increment 5)
            // recovers the flat keys termsFromKind feeds it from.
            Map("referenceIndex", ("floatingRate", "referenceIndex")),
            Map("capStrike", ("floatingRate", "capRate")),
            Map("floorStrike", ("floatingRate", "floorRate")),
            Map("resetFrequency", ("floatingRate", "resetFrequency")),
            Map("capFloorType", ("exercise", "optionRight")),
            Map("premiumAmount", ("exercise", "premiumAmount")),
            Map("notional", ("notional", "notional"))
        ]),
        ("StructuredNote",
        [
            Map("maturity", MaturityDate),
            Map("issueDate", ("maturity", "issueDate")),
            Map("issuerName", ("issuer", "issuerName")),
            Map("couponRate", ("coupon", "couponRate")),
            Map("paymentFrequency", ("coupon", "paymentFrequency")),
            Map("denomination", ("notional", "denomination"))
        ]),
        ("InvestmentFund", [Map("fundFamily", ("fund", "fundFamily"))])
    ];

    /// <summary>
    /// Opens a flattened payload with its envelope: the flat codec version, the explicit flat format
    /// and asset-class terms version, and the marker naming the economic version it came from.
    /// </summary>
    private static void WriteFlatEnvelope(Utf8JsonWriter writer, int assetClassTermsVersion)
    {
        writer.WriteStartObject();
        writer.WriteNumber(SecurityTermsEnvelope.SchemaVersionKey, AssetSpecificTermsSchema.Legacy);
        writer.WriteString(SecurityTermsEnvelope.FormatKey, SecurityTermsFormats.Flat);
        writer.WriteNumber(SecurityTermsEnvelope.AssetClassTermsVersionKey, assetClassTermsVersion);
        writer.WriteNumber(FlattenedFromMarkerProperty, EconomicTermsSchema.Current);
    }

    /// <summary>
    /// Carries the source economic document verbatim under
    /// <see cref="SecurityTermsEnvelope.RetainedEconomicTermsKey"/>. The flat family cannot express
    /// every module (redemption, call and put schedules, issuer identity, fund and structured-product
    /// detail), so instead of dropping what it cannot place, the bridge keeps the whole document
    /// beside the flat keys; <c>SecurityEconomicDefinitionAdapter</c> folds it back into the economic
    /// record on the next write, so a replay-then-amend cycle never erases it.
    /// </summary>
    private static void WriteRetainedEconomicTerms(Utf8JsonWriter writer, JsonElement economicTerms)
    {
        if (economicTerms.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        writer.WritePropertyName(SecurityTermsEnvelope.RetainedEconomicTermsKey);
        economicTerms.WriteTo(writer);
    }

    private static JsonElement? GetObject(JsonElement source, string propertyName)
        => source.ValueKind == JsonValueKind.Object
           && source.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static bool WriteIfPresent(Utf8JsonWriter writer, string targetName, JsonElement? source, string sourceName)
    {
        if (source is not JsonElement element
            || !element.TryGetProperty(sourceName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        writer.WritePropertyName(targetName);
        value.WriteTo(writer);
        return true;
    }
}

/// <summary>
/// The composed migrate-on-read chain for asset-specific-terms payloads — the single entry point
/// that turns "whatever version is stored" into a payload current readers accept:
/// <list type="bullet">
/// <item><description>unstamped (version 0) → stamped <see cref="AssetSpecificTermsSchema.Legacy"/>
/// via <see cref="SecurityAssetSpecificTermsV0ToCurrentUpcaster"/>;</description></item>
/// <item><description>a cross-family economic-terms document (stamped
/// <see cref="EconomicTermsSchema.Current"/> and carrying economic-terms module keys) → flattened v1
/// via the lossy <see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster"/>;</description></item>
/// <item><description>accepted versions pass through unchanged; unknown future versions — including a
/// payload stamped with the reserved <see cref="AssetSpecificTermsSchema.ReservedForEconomicTerms"/>
/// that does not have the economic-terms shape — pass through with their version preserved, so
/// acceptance stays the guard's decision and newer or misrouted payloads degrade with a precise
/// diagnostic instead of being silently emptied in the chain.</description></item>
/// </list>
/// </summary>
public static class SecurityAssetSpecificTermsUpcasterChain
{
    /// <summary>Normalizes a stored asset-specific-terms payload to the newest readable form.</summary>
    public static SecurityAssetSpecificTerms Normalize(JsonElement payload)
        => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(payload)
            ? SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(payload)
            : SecurityAssetSpecificTermsV0ToCurrentUpcaster.Normalize(payload);

    /// <summary>
    /// Class-aware variant: a cross-family economic-terms document is flattened onto the flat keys
    /// <paramref name="assetClass"/> declares (see
    /// <see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonElement, string?)"/>).
    /// Every other payload normalizes exactly as <see cref="Normalize(JsonElement)"/>.
    /// </summary>
    public static SecurityAssetSpecificTerms Normalize(JsonElement payload, string? assetClass)
        => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(payload)
            ? SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(payload, assetClass)
            : SecurityAssetSpecificTermsV0ToCurrentUpcaster.Normalize(payload);

    /// <summary>Text-input variant; returns <see langword="null"/> when the text is not JSON.</summary>
    public static SecurityAssetSpecificTerms? Normalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Normalize(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The registered <see cref="ISchemaUpcaster{T}"/> form of <see cref="SecurityAssetSpecificTermsUpcasterChain"/>:
/// a single injectable choke point that composes the full asset-specific-terms migrate-on-read chain
/// (v0 stamping plus cross-family economic-terms v2 → v1 flattening) for callers that resolve
/// upcasters through the <see cref="ISchemaUpcaster{T}"/> seam. Unknown future versions pass through
/// with their version preserved, keeping acceptance the guard's decision.
/// <para>
/// <see cref="ToSchemaVersion"/> names the version accepted <i>flat</i> payloads normalize to. It is a
/// property of the pipeline, not of any one result: a profile-backed payload passes through as
/// <see cref="AssetSpecificTermsSchema.CustomAssetProfile"/>, and callers must read the version off the
/// returned <see cref="SecurityAssetSpecificTerms.SchemaVersion"/>, never off this declaration.
/// </para>
/// </summary>
public sealed class SecurityAssetSpecificTermsUpcasterPipeline : ISchemaUpcaster<SecurityAssetSpecificTerms>
{
    /// <summary>Shared stateless instance for callers that resolve the pipeline outside DI.</summary>
    public static SecurityAssetSpecificTermsUpcasterPipeline Instance { get; } = new();

    /// <summary>The oldest (unstamped legacy) payload family this pipeline reads from.</summary>
    public int FromSchemaVersion => 0;

    /// <summary>The normalized asset-specific-terms version the pipeline resolves accepted flat payloads to.</summary>
    public int ToSchemaVersion => AssetSpecificTermsSchema.Legacy;

    /// <summary>Normalizes a stored asset-specific-terms payload through the full chain.</summary>
    public SecurityAssetSpecificTerms? Upcast(string json)
        => SecurityAssetSpecificTermsUpcasterChain.Normalize(json);
}
