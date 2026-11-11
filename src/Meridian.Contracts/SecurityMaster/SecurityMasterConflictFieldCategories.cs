namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// What a Security Master conflict's field touches, used to route the conflict for review.
/// A conflict can touch more than one category (a currency is both <see cref="Currency"/> and
/// <see cref="Classification"/>).
/// </summary>
[Flags]
public enum SecurityMasterConflictFieldCategory
{
    None = 0,
    Identifier = 1 << 0,
    Lifecycle = 1 << 1,
    TradingParameter = 1 << 2,
    CorporateAction = 1 << 3,
    Classification = 1 << 4,
    Currency = 1 << 5,
    Display = 1 << 6
}

/// <summary>
/// Classifies a <see cref="SecurityMasterConflict"/> by the declared names in its field path,
/// not by substrings of the path text. Each dot-separated path segment is compared whole
/// (case-insensitive, underscores ignored) with the names below, so a field such as
/// <c>ProfileFields.transferRestrictions</c> no longer reads as an identifier because it
/// contains "ric", and <c>ProfileFields.slotCount</c> no longer reads as a trading parameter
/// because it contains "lot".
/// <para>
/// Identifier names come from <see cref="SecurityIdentifierKind"/> (the kinds conflict detection
/// writes as <c>Identifiers.{Kind}</c>) and trading-parameter names from
/// <see cref="TradingParametersDto"/> (the fields the workbench reads under
/// <c>TradingParameters.</c>). A field outside these declared names has no category and takes the
/// default review route.
/// </para>
/// </summary>
public static class SecurityMasterConflictFieldCategories
{
    private static readonly IReadOnlyDictionary<string, SecurityMasterConflictFieldCategory> CategoryByName =
        BuildCategoryByName();

    /// <summary>The declared segment names and the category each one carries.</summary>
    public static IReadOnlyDictionary<string, SecurityMasterConflictFieldCategory> DeclaredNames => CategoryByName;

    public static SecurityMasterConflictFieldCategory For(SecurityMasterConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return For(conflict.FieldPath, conflict.ConflictKind);
    }

    public static SecurityMasterConflictFieldCategory For(string? fieldPath, string? conflictKind)
    {
        var category = string.Equals(conflictKind, SecurityMasterConflictKinds.IdentifierAmbiguity, StringComparison.OrdinalIgnoreCase)
            ? SecurityMasterConflictFieldCategory.Identifier
            : SecurityMasterConflictFieldCategory.None;

        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return category;
        }

        foreach (var segment in fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (CategoryByName.TryGetValue(Normalize(segment), out var segmentCategory))
            {
                category |= segmentCategory;
            }
        }

        return category;
    }

    private static string Normalize(string name)
        => name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static Dictionary<string, SecurityMasterConflictFieldCategory> BuildCategoryByName()
    {
        var names = new Dictionary<string, SecurityMasterConflictFieldCategory>(StringComparer.Ordinal);

        void Add(SecurityMasterConflictFieldCategory category, params string[] declared)
        {
            foreach (var name in declared)
            {
                var key = Normalize(name);
                names[key] = names.TryGetValue(key, out var existing) ? existing | category : category;
            }
        }

        Add(SecurityMasterConflictFieldCategory.Identifier, "Identifiers", "Identifier", "PrimaryIdentifier");
        Add(SecurityMasterConflictFieldCategory.Identifier, Enum.GetNames<SecurityIdentifierKind>());

        Add(SecurityMasterConflictFieldCategory.Lifecycle,
            "Status", "LifecycleStatus", "Lifecycle", "EffectiveFrom", "EffectiveTo", "EffectiveDate");

        Add(SecurityMasterConflictFieldCategory.TradingParameter,
            "TradingParameters",
            nameof(TradingParametersDto.LotSize),
            nameof(TradingParametersDto.TickSize),
            nameof(TradingParametersDto.ContractMultiplier),
            nameof(TradingParametersDto.MarginRequirementPct),
            nameof(TradingParametersDto.TradingHoursUtc),
            nameof(TradingParametersDto.CircuitBreakerThresholdPct),
            nameof(TradingParametersDto.IsMarginable),
            nameof(TradingParametersDto.MinimumOrderSize),
            nameof(TradingParametersDto.MinimumTradeIncrement),
            nameof(TradingParametersDto.PriceIncrement),
            "Multiplier", "MarginRequirement", "TradingHours");

        Add(SecurityMasterConflictFieldCategory.CorporateAction,
            "CorporateActions", "CorporateAction", "Dividend", "DividendAmount", "DividendPerShare",
            "Split", "SplitRatio", "Rights", "RightsRatio", "Distribution", "ExDate", "PayDate");

        Add(SecurityMasterConflictFieldCategory.Classification,
            "Classification", "AssetClass", "AssetFamily", "SubType", "AssetSubType", "InstrumentType",
            "Issuer", "IssuerName", "IssuerId", "IssuerType", "CollateralType", "PropertyType", "Currency");

        Add(SecurityMasterConflictFieldCategory.Currency, "Currency");

        Add(SecurityMasterConflictFieldCategory.Display, "DisplayName", "Name", "Alias", "Aliases");

        return names;
    }
}
