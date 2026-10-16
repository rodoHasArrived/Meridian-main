using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>What one unit of a position lot measures.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityLotUnitKind>))]
public enum SecurityLotUnitKind
{
    /// <summary>Equity shares.</summary>
    Shares,

    /// <summary>Listed derivative contracts, scaled by the class's contract multiplier.</summary>
    Contracts,

    /// <summary>Par (face) amount of a debt instrument.</summary>
    FaceValue,

    /// <summary>Notional amount of an OTC derivative.</summary>
    Notional,

    /// <summary>Fund, holding or other units with no par or notional meaning.</summary>
    Units,

    /// <summary>An amount of currency (FX spot, cash sweep balances).</summary>
    CurrencyAmount,

    /// <summary>A committed amount (fund commitments, guarantees).</summary>
    Commitment
}

/// <summary>How an asset class produces cash flows.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityCashFlowBehavior>))]
public enum SecurityCashFlowBehavior
{
    /// <summary>The class produces no scheduled cash flows.</summary>
    None,

    /// <summary>Coupon, interest and principal flows calculated from contractual terms.</summary>
    ContractualSchedule,

    /// <summary>Contractual flows whose principal amortizes by a dated pool factor.</summary>
    FactorAmortizing,

    /// <summary>Exchanges calculated per swap leg.</summary>
    SwapLegs,

    /// <summary>Financing interest calculated from rate and term (repo).</summary>
    FinancingInterest,

    /// <summary>Declared dividends, recorded as corporate actions rather than calculated.</summary>
    Dividends,

    /// <summary>Fund distributions, recorded from the administrator rather than calculated.</summary>
    FundDistributions,

    /// <summary>Flows supplied by a provider or administrator (capital calls, rents, draws).</summary>
    ProviderSupplied
}

/// <summary>How an asset class's terms reach a relational projection.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityTermsProjectionKind>))]
public enum SecurityTermsProjectionKind
{
    /// <summary>The class has no typed projection table; its terms live only in the terms JSON.</summary>
    TermsJsonOnly,

    /// <summary>A declarative <c>SecurityTermsProjectionRegistry</c> descriptor writes the table.</summary>
    Declarative,

    /// <summary>A specialized writer projects derived columns the declarative registry cannot express.</summary>
    Specialized
}

/// <summary>Behaviour derived from <see cref="SecurityCashFlowBehavior"/>.</summary>
public static class SecurityCashFlowBehaviorExtensions
{
    /// <summary>Whether flows of this kind are calculated from the security's terms.</summary>
    public static bool IsCalculatedFromTerms(this SecurityCashFlowBehavior behavior)
        => behavior is SecurityCashFlowBehavior.ContractualSchedule
            or SecurityCashFlowBehavior.FactorAmortizing
            or SecurityCashFlowBehavior.SwapLegs
            or SecurityCashFlowBehavior.FinancingInterest;
}

/// <summary>The editor a form should render for one term field.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityAssetTermEditorKind>))]
public enum SecurityAssetTermEditorKind
{
    Text,
    Decimal,
    Integer,
    Toggle,
    Date,
    Identifier,
    Choice,

    /// <summary>
    /// A typed convention (day count, settlement method): free text the convention's parser must
    /// recognize, with <see cref="SecurityAssetTermUiHint.Choices"/> offering the canonical spellings.
    /// </summary>
    Convention,
    Json
}

/// <summary>
/// Presentation hints for one asset-specific term, derived from its declared field so both
/// workstations build edit and create forms from the term schema instead of free-text paths.
/// </summary>
/// <param name="Key">The term key (the edit path is <c>assetSpecificTerms.{Key}</c>).</param>
/// <param name="FieldPath">The governed field-edit path for this term.</param>
/// <param name="Label">A human label derived from the key.</param>
/// <param name="Editor">The editor to render.</param>
/// <param name="Group">The form section the field belongs in.</param>
/// <param name="Order">Display order within the class (declaration order).</param>
/// <param name="Required">Whether the term must be supplied on create.</param>
/// <param name="Choices">The closed vocabulary for <see cref="SecurityAssetTermEditorKind.Choice"/> editors.</param>
/// <param name="ScalarEditable">
/// Whether the field can be edited as one scalar value through the governed field-edit route.
/// Arrays and nested objects are edited through a full terms amendment instead.
/// </param>
public sealed record SecurityAssetTermUiHint(
    string Key,
    string FieldPath,
    string Label,
    SecurityAssetTermEditorKind Editor,
    string Group,
    int Order,
    bool Required,
    IReadOnlyList<string> Choices,
    bool ScalarEditable);

/// <summary>
/// The complete definition of one asset class: its behaviour descriptor, its term fields and the
/// UI hints derived from them. Composed by <see cref="SecurityAssetClassRegistry"/> from the single
/// descriptor table and the single term schema, so no surface keeps its own copy.
/// </summary>
public sealed record SecurityAssetClassDefinition(
    string AssetClass,
    SecurityAssetClassDescriptor Descriptor,
    IReadOnlyList<SecurityAssetTermField> Fields,
    IReadOnlyList<SecurityAssetTermUiHint> UiHints)
{
    public SecurityLotUnitKind LotUnit => Descriptor.LotUnit;

    public SecurityCashFlowBehavior CashFlowBehavior => Descriptor.CashFlowBehavior;

    public SecurityTermsProjectionKind ProjectionKind => Descriptor.ProjectionKind;

    public int TermsVersion => Descriptor.TermsVersion;

    /// <summary>The required term keys a create must supply.</summary>
    public IReadOnlyList<string> RequiredTermKeys
        => Fields.Where(static term => term.Required).Select(static term => term.Key).ToArray();
}

/// <summary>Wire form of one term field with its UI hint, for schema-driven forms.</summary>
public sealed record SecurityAssetTermFieldDefinitionDto(
    string Key,
    string FieldPath,
    string Label,
    string Type,
    SecurityAssetTermEditorKind Editor,
    string Group,
    int Order,
    bool Required,
    bool ScalarEditable,
    IReadOnlyList<string> Choices,
    IReadOnlyList<string> Aliases);

/// <summary>Wire form of <see cref="SecurityAssetClassDefinition"/>.</summary>
public sealed record SecurityAssetClassDefinitionDto(
    string AssetClass,
    int TermsVersion,
    SecurityLotUnitKind LotUnit,
    string? LotMultiplierTermKey,
    SecurityCashFlowBehavior CashFlowBehavior,
    SecurityTermsProjectionKind ProjectionKind,
    bool SupportsBasicCreateWorkflow,
    bool SupportsSchemaDrivenCreate,
    bool SupportsProfileBackedTerms,
    bool RequiresMaturity,
    IReadOnlyList<string> PreferredIdentifierKinds,
    IReadOnlyList<SecurityAssetTermFieldDefinitionDto> Fields);

/// <summary>
/// The single asset-class registry. Every per-class behaviour — term fields and validation shape,
/// relational projection kind, lot unit and multiplier, cash-flow behaviour, identifier preference,
/// accounting class and UI hints — is read from here. It composes the one descriptor table
/// (<see cref="SecurityAssetClassCatalog"/>) with the one term schema
/// (<see cref="SecurityAssetTermsSchema"/>); every derived list (face-value classes, calculated
/// cash-flow classes, identifier-only importable classes, ops-capable classes, form hints) is
/// generated from that pair rather than maintained beside it. <c>SecurityAssetClassRegistryCoverageTests</c>
/// locks the registry to every case of the F# <c>SecurityKind</c> union, the F#
/// <c>AssetClassRegistry</c>, the validator registry and the storage writers.
/// </summary>
public static class SecurityAssetClassRegistry
{
    private static readonly IReadOnlyDictionary<string, SecurityAssetClassDefinition> ByAssetClass = Build();

    private static IReadOnlyDictionary<string, SecurityAssetClassDefinition> Build()
    {
        var definitions = new Dictionary<string, SecurityAssetClassDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in SecurityAssetClassCatalog.All)
        {
            if (!SecurityAssetTermsSchema.TryGetFields(descriptor.AssetClass, out var fields))
            {
                throw new InvalidOperationException(
                    $"Asset class '{descriptor.AssetClass}' has a catalog descriptor but no term schema.");
            }

            definitions.Add(descriptor.AssetClass, new SecurityAssetClassDefinition(
                descriptor.AssetClass,
                descriptor,
                fields,
                fields.Select((field, index) => BuildUiHint(field, index)).ToArray()));
        }

        return definitions;
    }

    /// <summary>Every definition, in catalog order.</summary>
    public static IReadOnlyList<SecurityAssetClassDefinition> All { get; } =
        SecurityAssetClassCatalog.All.Select(static descriptor => ByAssetClass[descriptor.AssetClass]).ToArray();

    /// <summary>Resolves a canonical name or catalog alias to its definition.</summary>
    public static bool TryGet(string? assetClass, out SecurityAssetClassDefinition definition)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
        return ByAssetClass.TryGetValue(descriptor.AssetClass, out definition!);
    }

    /// <summary>Resolves a canonical name or catalog alias, failing for an unknown class.</summary>
    public static SecurityAssetClassDefinition Get(string assetClass)
        => TryGet(assetClass, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unknown Security Master asset class.");

    /// <summary>The classes whose lots are denominated in the given unit.</summary>
    public static IReadOnlyList<string> AssetClassesWithLotUnit(SecurityLotUnitKind unit)
        => All.Where(definition => definition.LotUnit == unit).Select(static definition => definition.AssetClass).ToArray();

    /// <summary>The classes whose terms are projected the given way.</summary>
    public static IReadOnlyList<string> AssetClassesWithProjection(SecurityTermsProjectionKind kind)
        => All.Where(definition => definition.ProjectionKind == kind).Select(static definition => definition.AssetClass).ToArray();

    /// <summary>
    /// Whether a create form can be generated for the class from its term schema alone. Every
    /// first-class kind with a flat schema qualifies; profile-backed custom assets need their
    /// approved profile's fields instead.
    /// </summary>
    public static bool SupportsSchemaDrivenCreate(SecurityAssetClassDefinition definition)
        => !string.Equals(definition.AssetClass, "CustomAsset", StringComparison.Ordinal);

    /// <summary>The wire form of a definition.</summary>
    public static SecurityAssetClassDefinitionDto ToDto(SecurityAssetClassDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var hints = definition.UiHints.ToDictionary(static hint => hint.Key, StringComparer.Ordinal);
        return new SecurityAssetClassDefinitionDto(
            definition.AssetClass,
            definition.TermsVersion,
            definition.LotUnit,
            definition.Descriptor.LotMultiplierTermKey,
            definition.CashFlowBehavior,
            definition.ProjectionKind,
            definition.Descriptor.SupportsBasicCreateWorkflow,
            SupportsSchemaDrivenCreate(definition),
            definition.Descriptor.SupportsProfileBackedTerms,
            definition.Descriptor.RequiresMaturity,
            definition.Descriptor.PreferredIdentifierKinds.Select(static kind => kind.ToString()).ToArray(),
            definition.Fields
                .Select(field =>
                {
                    var hint = hints[field.Key];
                    return new SecurityAssetTermFieldDefinitionDto(
                        field.Key,
                        hint.FieldPath,
                        hint.Label,
                        field.Type.ToString(),
                        hint.Editor,
                        hint.Group,
                        hint.Order,
                        field.Required,
                        hint.ScalarEditable,
                        hint.Choices,
                        field.Aliases);
                })
                .ToArray());
    }

    /// <summary>The wire form of every definition, in catalog order.</summary>
    public static IReadOnlyList<SecurityAssetClassDefinitionDto> ToDtos()
        => All.Select(ToDto).ToArray();

    private static SecurityAssetTermUiHint BuildUiHint(SecurityAssetTermField field, int order)
    {
        var editor = field.AllowedValues.Count > 0
            ? SecurityAssetTermEditorKind.Choice
            : field.Convention != SecurityTermConventionKind.None
                ? SecurityAssetTermEditorKind.Convention
            : field.Type switch
            {
                SecurityAssetTermFieldType.String => SecurityAssetTermEditorKind.Text,
                SecurityAssetTermFieldType.Decimal => SecurityAssetTermEditorKind.Decimal,
                SecurityAssetTermFieldType.Integer => SecurityAssetTermEditorKind.Integer,
                SecurityAssetTermFieldType.Boolean => SecurityAssetTermEditorKind.Toggle,
                SecurityAssetTermFieldType.Date => SecurityAssetTermEditorKind.Date,
                SecurityAssetTermFieldType.Guid => SecurityAssetTermEditorKind.Identifier,
                _ => SecurityAssetTermEditorKind.Json
            };

        var scalar = field.Type is not (SecurityAssetTermFieldType.Array or SecurityAssetTermFieldType.Object);
        return new SecurityAssetTermUiHint(
            field.Key,
            $"assetSpecificTerms.{field.Key}",
            Humanize(field.Key),
            editor,
            GroupFor(field),
            order,
            field.Required,
            field.AllowedValues.Count > 0 ? field.AllowedValues : SecurityTermConventions.CanonicalValues(field.Convention),
            scalar);
    }

    private static string GroupFor(SecurityAssetTermField field)
    {
        if (field.Type is SecurityAssetTermFieldType.Array or SecurityAssetTermFieldType.Object)
        {
            return "Schedules and structure";
        }

        if (field.Type == SecurityAssetTermFieldType.Date)
        {
            return "Dates";
        }

        var key = field.Key;
        if (key.Contains("rate", StringComparison.OrdinalIgnoreCase)
            || key.Contains("spread", StringComparison.OrdinalIgnoreCase)
            || key.Contains("coupon", StringComparison.OrdinalIgnoreCase)
            || key.Contains("yield", StringComparison.OrdinalIgnoreCase)
            || key.Contains("dayCount", StringComparison.OrdinalIgnoreCase)
            || key.Contains("frequency", StringComparison.OrdinalIgnoreCase))
        {
            return "Rates and accrual";
        }

        return field.Required ? "Core terms" : "Additional terms";
    }

    /// <summary>"couponRate" → "Coupon rate"; "isCallable" → "Is callable"; "cusip" → "Cusip".</summary>
    public static string Humanize(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        var builder = new StringBuilder(key.Length + 8);
        for (var i = 0; i < key.Length; i++)
        {
            var character = key[i];
            var startsWord = i > 0
                && char.IsUpper(character)
                && (char.IsLower(key[i - 1]) || (i + 1 < key.Length && char.IsLower(key[i + 1])));
            if (startsWord)
            {
                builder.Append(' ');
                builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(i == 0 ? char.ToUpper(character, CultureInfo.InvariantCulture) : character);
            }
        }

        return builder.ToString();
    }
}
