using System.Diagnostics.CodeAnalysis;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Where a provider asset-class code appears on the wire.</summary>
[Flags]
public enum ProviderAssetClassCodeRoles
{
    None = 0,

    /// <summary>The code is sent to (or read back from) the provider on an order.</summary>
    OrderPayload = 1,

    /// <summary>The code is stamped on positions, fills, or execution reports.</summary>
    PositionReport = 2,

    /// <summary>The code is a symbol-search asset type.</summary>
    Search = 4,
}

/// <summary>Behaviour that execution and booking read from a provider asset-class code.</summary>
[Flags]
public enum ProviderAssetClassCodeTraits
{
    None = 0,

    /// <summary>
    /// Orders in this class are sized in face value and priced as a percentage of par
    /// (Alpaca treasury and corporate).
    /// </summary>
    FaceValueFixedIncome = 1,

    /// <summary>
    /// A fill in this class can be booked from the execution report alone: unit quantity at the
    /// reported price, no contract multiplier and no percentage-of-par scaling.
    /// </summary>
    BookableFromReportAlone = 2,
}

/// <summary>
/// One provider spelling of an asset class: the code the provider uses, the Security Master
/// catalog class it names, the extra input spellings accepted for it, and its roles and traits.
/// </summary>
public sealed record ProviderAssetClassCode(
    string Provider,
    string Code,
    string CatalogAssetClass,
    IReadOnlyList<string> InputAliases,
    ProviderAssetClassCodeRoles Roles,
    ProviderAssetClassCodeTraits Traits = ProviderAssetClassCodeTraits.None)
{
    public bool HasTrait(ProviderAssetClassCodeTraits trait) => (Traits & trait) == trait;
}

/// <summary>Provider keys used by <see cref="ProviderAssetClassCodeCatalog"/>.</summary>
public static class ProviderAssetClassCodeProviders
{
    public const string Alpaca = "alpaca";
    public const string Robinhood = "robinhood";
    public const string InteractiveBrokers = "ib";

    /// <summary>
    /// Meridian's own lowercase execution vocabulary ("equity", "option", ...): what the IB
    /// mapper reports and what execution metadata carries for brokers without a vendor spelling.
    /// </summary>
    public const string Meridian = "meridian";

    public static IReadOnlyList<string> All { get; } = [Alpaca, Robinhood, InteractiveBrokers, Meridian];
}

/// <summary>Alpaca's wire codes for <c>asset_class</c>.</summary>
public static class AlpacaAssetClassCodes
{
    public const string UsEquity = "us_equity";
    public const string UsOption = "us_option";
    public const string Treasury = "treasury";
    public const string Corporate = "corporate";
    public const string Crypto = "crypto";
}

/// <summary>
/// The one table of brokerage asset-class codes. Each provider's codes are matched exactly
/// (no token or substring heuristics), which is why this table exists instead of
/// <see cref="SecurityAssetClassCatalog.ResolveLabel"/>: that resolver reads none of "crypto",
/// "treasury" or "corporate". Every helper here reproduces the comparison the call site used
/// before it moved onto the table (ordinal, ordinal-ignore-case, or trimmed lower-invariant),
/// so routing a site through the table does not change what it accepts.
/// <para>
/// The table is checked when the type loads: every provider is known, every catalog class is a
/// canonical <see cref="SecurityAssetClassCatalog"/> name, no code or alias is declared twice
/// for one provider, and every IB code has a Meridian report code to translate to.
/// </para>
/// </summary>
public static class ProviderAssetClassCodeCatalog
{
    private const ProviderAssetClassCodeRoles OrderAndReport =
        ProviderAssetClassCodeRoles.OrderPayload | ProviderAssetClassCodeRoles.PositionReport;

    private static readonly IReadOnlyList<ProviderAssetClassCode> Entries =
    [
        // Alpaca. The aliases are the input spellings the order builder has always normalized.
        new(ProviderAssetClassCodeProviders.Alpaca, AlpacaAssetClassCodes.UsEquity, SecurityAssetClassNames.Equity,
            ["equity"], OrderAndReport | ProviderAssetClassCodeRoles.Search,
            ProviderAssetClassCodeTraits.BookableFromReportAlone),
        new(ProviderAssetClassCodeProviders.Alpaca, AlpacaAssetClassCodes.UsOption, SecurityAssetClassNames.Option,
            ["option", "options"], OrderAndReport | ProviderAssetClassCodeRoles.Search),
        new(ProviderAssetClassCodeProviders.Alpaca, AlpacaAssetClassCodes.Treasury, SecurityAssetClassNames.Bond,
            ["us_treasury", "fixed_income_treasury"], ProviderAssetClassCodeRoles.OrderPayload,
            ProviderAssetClassCodeTraits.FaceValueFixedIncome),
        new(ProviderAssetClassCodeProviders.Alpaca, AlpacaAssetClassCodes.Corporate, SecurityAssetClassNames.Bond,
            ["bond", "corporate_bond", "us_corporate"], ProviderAssetClassCodeRoles.OrderPayload,
            ProviderAssetClassCodeTraits.FaceValueFixedIncome),
        new(ProviderAssetClassCodeProviders.Alpaca, AlpacaAssetClassCodes.Crypto, SecurityAssetClassNames.CryptoCurrency,
            [], ProviderAssetClassCodeRoles.PositionReport | ProviderAssetClassCodeRoles.Search,
            ProviderAssetClassCodeTraits.BookableFromReportAlone),

        // Robinhood.
        new(ProviderAssetClassCodeProviders.Robinhood, "equity", SecurityAssetClassNames.Equity,
            [], OrderAndReport, ProviderAssetClassCodeTraits.BookableFromReportAlone),
        new(ProviderAssetClassCodeProviders.Robinhood, "option", SecurityAssetClassNames.Option,
            [], OrderAndReport),

        // Interactive Brokers secType values.
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "STK", SecurityAssetClassNames.Equity,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "ETF", SecurityAssetClassNames.Equity,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "OPT", SecurityAssetClassNames.Option,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "FUT", SecurityAssetClassNames.Future,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "FOP", SecurityAssetClassNames.Option,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "CASH", SecurityAssetClassNames.FxSpot,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "BOND", SecurityAssetClassNames.Bond,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.InteractiveBrokers, "GOVT", SecurityAssetClassNames.Bond,
            [], OrderAndReport),

        // Meridian's execution vocabulary.
        new(ProviderAssetClassCodeProviders.Meridian, "equity", SecurityAssetClassNames.Equity,
            [], OrderAndReport, ProviderAssetClassCodeTraits.BookableFromReportAlone),
        new(ProviderAssetClassCodeProviders.Meridian, "option", SecurityAssetClassNames.Option,
            [], OrderAndReport),
        new(ProviderAssetClassCodeProviders.Meridian, "futures", SecurityAssetClassNames.Future,
            [], ProviderAssetClassCodeRoles.PositionReport),
        new(ProviderAssetClassCodeProviders.Meridian, "forex", SecurityAssetClassNames.FxSpot,
            [], ProviderAssetClassCodeRoles.PositionReport),
        new(ProviderAssetClassCodeProviders.Meridian, "bond", SecurityAssetClassNames.Bond,
            [], ProviderAssetClassCodeRoles.PositionReport),
    ];

    // provider -> lower-invariant code or alias -> entry.
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ProviderAssetClassCode>> InputIndex =
        BuildAndValidate();

    private static readonly IReadOnlyDictionary<string, string> IbToMeridianReportCodes =
        BuildTranslation(ProviderAssetClassCodeProviders.InteractiveBrokers, ProviderAssetClassCodeProviders.Meridian);

    public static IReadOnlyList<ProviderAssetClassCode> All => Entries;

    public static IReadOnlyList<ProviderAssetClassCode> ForProvider(string provider)
        => Entries.Where(entry => string.Equals(entry.Provider, provider, StringComparison.Ordinal)).ToArray();

    /// <summary>
    /// Resolves a provider code compared ordinally and untrimmed — the comparison a site that
    /// wrote <c>value == "us_option"</c> made. Aliases do not resolve here.
    /// </summary>
    public static bool TryResolveExact(string provider, string? code, [NotNullWhen(true)] out ProviderAssetClassCode? entry)
    {
        entry = code is null
            ? null
            : Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Provider, provider, StringComparison.Ordinal)
                && string.Equals(candidate.Code, code, StringComparison.Ordinal));
        return entry is not null;
    }

    /// <summary>
    /// Resolves a provider code compared with <see cref="StringComparison.OrdinalIgnoreCase"/>
    /// and untrimmed. Aliases do not resolve here.
    /// </summary>
    public static bool TryResolveCodeIgnoreCase(string provider, string? code, [NotNullWhen(true)] out ProviderAssetClassCode? entry)
    {
        entry = code is null
            ? null
            : Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Provider, provider, StringComparison.Ordinal)
                && string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));
        return entry is not null;
    }

    /// <summary>
    /// Resolves caller input — a code or one of its input aliases — after
    /// <c>Trim().ToLowerInvariant()</c>, the normalization order builders apply to metadata.
    /// </summary>
    public static bool TryResolveInput(string provider, string? input, [NotNullWhen(true)] out ProviderAssetClassCode? entry)
    {
        entry = null;
        return input is not null
            && InputIndex.TryGetValue(provider, out var index)
            && index.TryGetValue(input.Trim().ToLowerInvariant(), out entry);
    }

    /// <summary>
    /// The provider code for caller input; input that names no code is returned trimmed and
    /// lower-cased so the provider sees (and refuses) what was submitted.
    /// </summary>
    public static string NormalizeInput(string provider, string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return TryResolveInput(provider, input, out var entry) ? entry.Code : input.Trim().ToLowerInvariant();
    }

    /// <summary>Whether <paramref name="code"/> is, ordinally, the provider's code for the catalog class.</summary>
    public static bool IsExactCode(string provider, string? code, string catalogAssetClass)
        => TryResolveExact(provider, code, out var entry)
            && string.Equals(entry.CatalogAssetClass, catalogAssetClass, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="code"/> is, ignoring case, one of the named providers' codes for
    /// the catalog class.
    /// </summary>
    public static bool IsCodeIgnoreCase(string? code, string catalogAssetClass, params string[] providers)
    {
        foreach (var provider in providers)
        {
            if (TryResolveCodeIgnoreCase(provider, code, out var entry)
                && string.Equals(entry.CatalogAssetClass, catalogAssetClass, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether caller input (a code or alias, trimmed and lower-cased) names a face-value
    /// fixed-income class for the provider. The live Alpaca gateway and the paper gateway both
    /// size face-value orders off this answer.
    /// </summary>
    public static bool IsFaceValueFixedIncomeInput(string provider, string? input)
        => TryResolveInput(provider, input, out var entry)
            && entry.HasTrait(ProviderAssetClassCodeTraits.FaceValueFixedIncome);

    /// <summary>
    /// Whether a fill stamped with this asset-class code can be booked from the report alone.
    /// The code is trimmed and lower-cased and must equal a code (not an alias) of any provider
    /// declared <see cref="ProviderAssetClassCodeTraits.BookableFromReportAlone"/>; an absent
    /// class is unknown, not equity.
    /// </summary>
    public static bool IsBookableFromReportAlone(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        return normalized is not null
            && Entries.Any(entry =>
                entry.HasTrait(ProviderAssetClassCodeTraits.BookableFromReportAlone)
                && string.Equals(entry.Code, normalized, StringComparison.Ordinal));
    }

    /// <summary>
    /// IB secType → Meridian report code ("STK" → "equity"), matched ignoring case like the
    /// mapper's other vocabularies.
    /// </summary>
    public static IReadOnlyDictionary<string, string> InteractiveBrokersReportCodes => IbToMeridianReportCodes;

    /// <summary>
    /// Translates every code of <paramref name="fromProvider"/> to the
    /// <paramref name="toProvider"/> position-report code for the same catalog class.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildTranslation(string fromProvider, string toProvider)
    {
        var translation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ForProvider(fromProvider))
        {
            var target = ForProvider(toProvider).FirstOrDefault(candidate =>
                (candidate.Roles & ProviderAssetClassCodeRoles.PositionReport) != 0
                && string.Equals(candidate.CatalogAssetClass, entry.CatalogAssetClass, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Provider '{toProvider}' has no position-report code for catalog class '{entry.CatalogAssetClass}' (from {fromProvider} code '{entry.Code}').");
            translation.Add(entry.Code, target.Code);
        }

        return translation;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ProviderAssetClassCode>> BuildAndValidate()
    {
        var index = new Dictionary<string, IReadOnlyDictionary<string, ProviderAssetClassCode>>(StringComparer.Ordinal);
        foreach (var group in Entries.GroupBy(static entry => entry.Provider, StringComparer.Ordinal))
        {
            if (!ProviderAssetClassCodeProviders.All.Contains(group.Key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Provider asset-class code table names unknown provider '{group.Key}'.");
            }

            var byInput = new Dictionary<string, ProviderAssetClassCode>(StringComparer.Ordinal);
            foreach (var entry in group)
            {
                if (!SecurityAssetClassCatalog.TryGet(entry.CatalogAssetClass, out var descriptor)
                    || !string.Equals(descriptor.AssetClass, entry.CatalogAssetClass, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Provider '{entry.Provider}' code '{entry.Code}' names '{entry.CatalogAssetClass}', which is not a canonical catalog class.");
                }

                if (entry.Roles == ProviderAssetClassCodeRoles.None)
                {
                    throw new InvalidOperationException($"Provider '{entry.Provider}' code '{entry.Code}' declares no role.");
                }

                foreach (var spelling in entry.InputAliases.Prepend(entry.Code))
                {
                    if (string.IsNullOrWhiteSpace(spelling) || spelling != spelling.Trim())
                    {
                        throw new InvalidOperationException(
                            $"Provider '{entry.Provider}' code '{entry.Code}' declares a blank or untrimmed spelling.");
                    }

                    if (!byInput.TryAdd(spelling.ToLowerInvariant(), entry))
                    {
                        throw new InvalidOperationException(
                            $"Provider '{entry.Provider}' declares the spelling '{spelling}' more than once.");
                    }
                }
            }

            index.Add(group.Key, byInput);
        }

        return index;
    }
}
