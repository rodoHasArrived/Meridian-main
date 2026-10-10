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
/// <b>The bridge is lossy, and callers must treat it as such.</b> The flat v1 family is keyed per
/// asset class (a callable bond's <c>callDate</c>, an MBS's <c>currentFactor</c>, a sweep vehicle's
/// <c>programName</c>). When the caller knows the class it should use
/// <see cref="Convert(JsonElement, string?)"/>, which writes every value the economic document
/// carries at the class's declared flat key and loses only what that document never held
/// (<see cref="UnrecoverableKeys"/>). The class-independent <see cref="Convert(JsonElement)"/>
/// described next is the fallback for an unknown class: it can only carry the
/// modules whose fields have one class-independent flat spelling. It reads the
/// <see cref="BridgedModules"/> — <c>maturity</c>, <c>coupon</c>, <c>payment</c>, <c>accrual</c>,
/// <c>discount</c> — and drops every other module the economic serializer emits
/// (<see cref="DroppedModules"/>: redemption, call, auction, sweep, financing, issuer, equity
/// behaviour, fund, structured product), plus the accrual and payment fields that have no flat
/// counterpart. A callable bond comes out as a bullet bond with the same coupon; an MBS loses its
/// pool factor; every class loses its issuer identity. The authoritative economic record remains
/// stored separately — this conversion only feeds read compatibility — and the output is stamped
/// with <see cref="FlattenedFromMarkerProperty"/> so a projection rebuilt through this route is
/// distinguishable from one that always carried the flat terms.
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

    /// <summary>The economic-terms modules this conversion reads. Order is the flattening order.</summary>
    public static IReadOnlyList<string> BridgedModules { get; } =
        ["maturity", "coupon", "payment", "accrual", "discount"];

    /// <summary>
    /// The economic-terms modules the serializer emits that this conversion does not read. Listed
    /// here, rather than left implicit, so the loss is enumerated in code and in the coverage test
    /// (<c>SecurityEconomicTermsV2BridgeCoverageTests</c>) instead of discovered in a restatement.
    /// </summary>
    public static IReadOnlyList<string> DroppedModules { get; } =
        ["redemption", "call", "auction", "sweep", "financing", "issuer", "equityBehavior", "fund", "structuredProduct"];

    /// <summary>
    /// Every module key an economic-terms document can carry: the union of the bridged and dropped
    /// sets. A module added to the economic serializer must be added to one of the two lists, and
    /// the coverage test fails until it is.
    /// </summary>
    public static IReadOnlyList<string> EconomicTermsModules { get; } =
        [.. BridgedModules, .. DroppedModules];

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
    /// Whether the payload has the economic-terms family's shape: an object stamped
    /// <see cref="EconomicTermsSchema.Current"/> that carries at least one of the
    /// <see cref="EconomicTermsModules"/> keys. The two payload families share one
    /// <c>schemaVersion</c> key, so the integer alone cannot tell a v2 economic-terms document from
    /// a (reserved, never declared) flat v2; the module keys can, because the economic serializer
    /// always writes them and a flat document never does.
    /// </summary>
    public static bool IsEconomicTermsDocument(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(payload) != EconomicTermsSchema.Current)
        {
            return false;
        }

        foreach (var module in EconomicTermsModules)
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
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", AssetSpecificTermsSchema.Legacy);
            writer.WriteNumber(FlattenedFromMarkerProperty, EconomicTermsSchema.Current);

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
    /// What remains lost is what the economic-terms document never carried: an option's strike and
    /// put/call, a swap's legs, a bond's subclass and schedules. Those keys are listed by
    /// <see cref="UnrecoverableKeys"/> and the result keeps the <see cref="FlattenedFromMarkerProperty"/>
    /// marker. When <paramref name="assetClass"/> is not a declared class (or is <see langword="null"/>)
    /// this falls back to the class-independent <see cref="Convert(JsonElement)"/>.
    /// </para>
    /// </summary>
    public static SecurityAssetSpecificTerms Convert(JsonElement economicTerms, string? assetClass)
    {
        if (!TryGetClassFlattening(assetClass, out var mappings))
        {
            return Convert(economicTerms);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", AssetSpecificTermsSchema.Legacy);
            writer.WriteNumber(FlattenedFromMarkerProperty, EconomicTermsSchema.Current);

            foreach (var mapping in mappings)
            {
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

            writer.WriteEndObject();
        }

        using var flattened = JsonDocument.Parse(buffer.ToArray());
        return new SecurityAssetSpecificTerms(flattened.RootElement.Clone(), AssetSpecificTermsSchema.Legacy);
    }

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
    /// </summary>
    private sealed record FlatTermMapping(string FlatKey, (string Module, string Field)[] Sources, string? EqualsLabel = null);

    private static FlatTermMapping Map(string flatKey, params (string Module, string Field)[] sources) => new(flatKey, sources);

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
        ("Option", [Map("expiry", MaturityDate)]),
        ("Future", [Map("expiry", MaturityDate)]),
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
            Map("factorSchedule", ("coupon", "paymentFrequency")),
            Map("maturity", MaturityDate)
        ]),
        ("PrivateFundInterest", [Map("gpSponsor", ("issuer", "issuerName"), ("fund", "fundFamily")), Map("strategy", ("issuer", "issuerProgram"))]),
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
            Map("expiryDate", MaturityDate)
        ]),
        ("Warrant", [Map("expiry", MaturityDate)]),
        ("InvestmentFund", [Map("fundFamily", ("fund", "fundFamily"))])
    ];

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
