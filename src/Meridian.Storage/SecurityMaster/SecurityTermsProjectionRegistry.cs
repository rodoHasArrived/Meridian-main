using System.Text.RegularExpressions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// How a projected <see cref="SecurityAssetTermFieldType.String"/> term is normalized before it is
/// bound. <see cref="TrimBlankAsAbsent"/> is the registry's original behaviour; the other modes
/// exist so the writers that predate the registry could move onto it without changing a single
/// persisted value.
/// </summary>
internal enum SecurityTermsProjectionTextNormalization
{
    /// <summary>
    /// Trimmed, with a blank value read as absent (NULL), via
    /// <c>TextPrimitives.NormalizeOptional</c>: a padded vendor value and a clean one land on the
    /// same indexed column value.
    /// </summary>
    TrimBlankAsAbsent = 0,

    /// <summary>Bound exactly as carried: surrounding whitespace and empty strings are kept.</summary>
    Verbatim,

    /// <summary>Trimmed, but a blank value is bound as the empty string rather than NULL.</summary>
    Trim,

    /// <summary>
    /// Trimmed and upper-cased with the invariant culture, with a blank value read as absent. For
    /// code-shaped terms (currency codes) whose read side matches on the canonical upper-case form.
    /// </summary>
    TrimUpperInvariant
}

/// <summary>
/// One projected scalar column of an asset class's relational terms projection: the SQL column it
/// lands in, the asset-specific-terms JSON key it reads, and the value type that key carries.
/// </summary>
/// <param name="ColumnName">Target SQL column (snake_case).</param>
/// <param name="TermKey">
/// The asset-specific-terms JSON key. It must be declared for the owning asset class in
/// <see cref="SecurityAssetTermsSchema"/> with the same <paramref name="Type"/>; the registry
/// validation refuses a column that reads a key the terms contract does not declare, which is the
/// drift that once had the bond projection decoding a nested <c>coupon</c> object the serializer
/// never wrote.
/// </param>
/// <param name="Type">The declared JSON value type, used to pick the reader and to check the schema.</param>
/// <param name="Gates">
/// Whether the column is NOT NULL and gates the whole projection: a record whose payload does not
/// carry this term — or carries it as a blank or whitespace-only string, whatever the column's
/// <paramref name="Normalization"/> — gets no projection row at all (and any stale row is deleted).
/// A gating column must be declared <c>Required</c> in <see cref="SecurityAssetTermsSchema"/> —
/// gating on a term the serializer may legitimately omit would drop projections for valid records.
/// </param>
/// <param name="Normalization">
/// How a string term is normalized before binding. Only meaningful for
/// <see cref="SecurityAssetTermFieldType.String"/>; validation refuses a non-default value on any
/// other type.
/// </param>
/// <param name="DefaultWhenAbsent">
/// The value bound instead of NULL when the term is absent, JSON null, or carries the wrong JSON
/// kind. Only a <see cref="bool"/> on a <see cref="SecurityAssetTermFieldType.Boolean"/> column is
/// accepted — it models the <c>boolean not null default false</c> flag columns — and a gating
/// column cannot carry one, because a default would make its gate unreachable.
/// </param>
internal sealed record SecurityTermsProjectionColumn(
    string ColumnName,
    string TermKey,
    SecurityAssetTermFieldType Type,
    bool Gates = false,
    SecurityTermsProjectionTextNormalization Normalization = SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent,
    object? DefaultWhenAbsent = null)
{
    /// <summary>A nullable projected column; a missing or malformed term writes NULL.</summary>
    internal static SecurityTermsProjectionColumn Optional(
        string columnName,
        string termKey,
        SecurityAssetTermFieldType type,
        SecurityTermsProjectionTextNormalization normalization = SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent)
        => new(columnName, termKey, type, Normalization: normalization);

    /// <summary>A NOT NULL projected column whose absence suppresses the whole projection row.</summary>
    internal static SecurityTermsProjectionColumn Gate(
        string columnName,
        string termKey,
        SecurityAssetTermFieldType type,
        SecurityTermsProjectionTextNormalization normalization = SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent)
        => new(columnName, termKey, type, Gates: true, Normalization: normalization);

    /// <summary>
    /// A NOT NULL boolean flag column that binds <paramref name="defaultWhenAbsent"/> when the term
    /// is absent or malformed, rather than gating the projection on it.
    /// </summary>
    internal static SecurityTermsProjectionColumn Flag(string columnName, string termKey, bool defaultWhenAbsent)
        => new(columnName, termKey, SecurityAssetTermFieldType.Boolean, DefaultWhenAbsent: defaultWhenAbsent);

    /// <summary>
    /// A column whose value is computed from the whole record rather than read from one declared
    /// term: a value derived from several terms (a lifecycle state, a pair code, a swap type
    /// scanned out of legs), a common-terms column, or a fallback chain. The compute function returns
    /// the bound value, or null for SQL NULL. A gating derived column suppresses the projection when
    /// it returns null or blank text, exactly like a gating term column.
    /// </summary>
    internal static SecurityTermsProjectionColumn Derived(
        string columnName,
        Func<SecurityProjectionRecord, object?> compute,
        bool gates = false)
        => new(columnName, TermKey: string.Empty, SecurityAssetTermFieldType.String, Gates: gates) { Compute = compute };

    /// <summary>
    /// The value function of a <see cref="Derived"/> column, or <see langword="null"/> for a column
    /// that reads <see cref="TermKey"/>. A derived column is exempt from the term-schema checks: it
    /// may read common terms or legacy keys, and its declared type is not a decode instruction.
    /// </summary>
    internal Func<SecurityProjectionRecord, object?>? Compute { get; init; }

    /// <summary>Whether the column is computed rather than read from one declared term.</summary>
    internal bool IsDerived => Compute is not null;
}

/// <summary>
/// One column of a child (one-to-many) projection table, read from an element of the parent's
/// declared array term rather than from the terms document root.
/// </summary>
/// <param name="ColumnName">Target SQL column on the child table.</param>
/// <param name="ElementKey">
/// The JSON key on each array element. Unlike a scalar column's term key this is NOT checked against
/// <see cref="SecurityAssetTermsSchema"/>, which declares array fields as
/// <see cref="SecurityAssetTermFieldType.Array"/> without enumerating their inner shape. What pins
/// these keys instead is the canonical-payload decode test per registered class: a mistyped required
/// element key makes the whole projection unbuildable, which those tests assert against. Enforcing
/// it in the registry would mean declaring element contracts in the terms schema first.
/// </param>
/// <param name="Type">The element value type.</param>
/// <param name="Required">
/// Whether the column is NOT NULL. A malformed element — one missing a required key, or carrying it
/// with the wrong JSON kind — suppresses the entire projection rather than writing a partial
/// schedule: a half-projected principal or factor schedule reads as a complete one and would
/// misstate amortization, whereas an absent projection reads as "not projected".
/// </param>
/// <param name="MustBePositive">
/// Whether a row whose value here is zero or negative is skipped rather than projected. Mirrors
/// <c>StructuredCashFlowTermsResolver.ReadPrincipalSchedule</c>, which discards instalments with a
/// non-positive amount: projecting one would have the relational read model report a contractual
/// payment the canonical cash-flow path does not recognise. Unlike a malformed element, this is a
/// value the domain defines as "not a payment", so it is dropped rather than suppressing the
/// projection.
/// </param>
internal sealed record SecurityTermsProjectionChildColumn(
    string ColumnName,
    string ElementKey,
    SecurityAssetTermFieldType Type,
    bool Required = false,
    bool MustBePositive = false);

/// <summary>
/// A child projection table fanned out from one declared array term (a covenant list, a principal
/// instalment schedule, a dated factor schedule). Rows are keyed by
/// <c>(security_id, ordinal)</c> so the persisted order matches the array order in the terms
/// document, and are replaced wholesale on every write.
/// </summary>
/// <param name="TableName">Target SQL table (unqualified).</param>
/// <param name="TermKey">
/// The asset-specific-terms key holding the array. It must be declared for the owning asset class in
/// <see cref="SecurityAssetTermsSchema"/> as <see cref="SecurityAssetTermFieldType.Array"/>.
/// </param>
/// <param name="Columns">The element columns, in insert order after the <c>(security_id, ordinal)</c> key.</param>
/// <param name="CascadesFromParent">
/// Whether the table's <c>security_id</c> foreign key declares <c>on delete cascade</c> from the
/// parent projection. The writer relies on this to clear a projection with a single parent delete
/// instead of one delete per child table, which matters because every registered writer runs for
/// every persisted record. The migration-DDL guard checks the claim against the shipped SQL, so it
/// cannot drift into a silent orphan-row leak.
/// </param>
internal sealed record SecurityTermsProjectionChildTable(
    string TableName,
    string TermKey,
    IReadOnlyList<SecurityTermsProjectionChildColumn> Columns,
    bool CascadesFromParent = true);

/// <summary>
/// The declarative relational projection for one asset class: a <c>security_id</c>-keyed parent
/// table of scalar terms plus any child tables fanned out from its declared array terms.
/// </summary>
/// <param name="AssetClass">Canonical Security Master asset class (must be a catalog class).</param>
/// <param name="TableName">The parent projection table (unqualified).</param>
/// <param name="Columns">Class-specific scalar columns, written between the identity spine columns.</param>
/// <param name="ChildTables">Child tables fanned out from declared array terms; empty for a flat class.</param>
/// <param name="ProjectsCurrency">
/// Whether the parent table carries the record's <c>currency</c> in its leading identity spine. A
/// pair-quoted class (CryptoCurrency) states both of its currencies as terms instead, and its table
/// has no <c>currency</c> column, so it opts out.
/// </param>
internal sealed record SecurityTermsProjectionDescriptor(
    string AssetClass,
    string TableName,
    IReadOnlyList<SecurityTermsProjectionColumn> Columns,
    IReadOnlyList<SecurityTermsProjectionChildTable> ChildTables,
    bool ProjectsCurrency = true);

/// <summary>
/// The declarative registry of schema-driven relational terms projections.
/// <para>
/// Every projected asset class used to cost a hand-written <c>Upsert&lt;Class&gt;ProjectionAsync</c>
/// method whose body was ~85% mechanical: an asset-class gate, a delete branch, a
/// <c>GetOptional*</c> read per term, an <c>insert … on conflict (security_id) do update set …</c>
/// statement whose update clause is fully derivable from its column list, and one
/// <c>AddWithValue</c> per column. This registry names the parts that actually differ — the table,
/// the columns, the terms they read — so a flat asset class is a data declaration instead of another
/// copy of that method, and the projected columns can be checked against
/// <see cref="SecurityAssetTermsSchema"/> instead of against each other.
/// </para>
/// <para>
/// The registry deliberately models only what a class can express declaratively: scalar terms read
/// from the asset-specific-terms document, plus child tables fanned out from its declared array
/// terms. A class whose projection needs derived columns (a computed lifecycle state, a swap type
/// scanned out of legs, a concatenated FX pair code), columns sourced from common terms, or a
/// legacy nested-shape fallback keeps its hand-written writer — those are genuine economics, not
/// boilerplate, and folding them in would trade the duplication for a configuration language.
/// </para>
/// <para>
/// Of the eleven writers that predate this registry, nine now live here. MoneyMarketFund,
/// CertificateOfDeposit, Deposit and CryptoCurrency moved behind a database-backed parity guard
/// (<c>RegistryMigratedProjectionGoldenTests</c>). Equity, FxSpot, Future, Swap and Commodity moved
/// once <see cref="SecurityTermsProjectionColumn.Derived"/> columns could express what made them
/// special — common-terms columns, the futures root-symbol fallback and lifecycle state, the FX pair
/// code, the swap type scanned out of legs, and Commodity's undeclared legacy keys — by calling the
/// same readers the writers used; <c>SpecializedProjectionGoldenTests</c> captured the writers' rows
/// across a gate/kind/class-change matrix before the move and holds the descriptors to them. Bond
/// and Option remain specialized: each fans out to several projection tables (lifecycle, accrual,
/// issuer; series, lifecycle, aliases) with a legacy nested-shape fallback.
/// </para>
/// <para>
/// Migration 043 then projected the twelve classes that had never had a relational projection
/// (CommercialPaper, TreasuryBill, Repo, CashSweep, OtherSecurity, Cfd, Warrant, InvestmentFund,
/// PrivateFundInterest, PrivateCompanyEquity, RealEstateHolding, CommitmentGuarantee) as pure
/// declarations over their terms contract, so every catalog class except the profile-governed
/// CustomAsset now has a projection — 25 of 26 — and the catalog's <c>ProjectionKind</c> says which
/// path writes each one.
/// </para>
/// </summary>
internal static partial class SecurityTermsProjectionRegistry
{
    private static SecurityTermsProjectionColumn Optional(
        string columnName,
        string termKey,
        SecurityAssetTermFieldType type,
        SecurityTermsProjectionTextNormalization normalization = SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent)
        => SecurityTermsProjectionColumn.Optional(columnName, termKey, type, normalization);

    private static SecurityTermsProjectionColumn Gate(
        string columnName,
        string termKey,
        SecurityAssetTermFieldType type,
        SecurityTermsProjectionTextNormalization normalization = SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent)
        => SecurityTermsProjectionColumn.Gate(columnName, termKey, type, normalization);

    private static SecurityTermsProjectionColumn Flag(string columnName, string termKey, bool defaultWhenAbsent)
        => SecurityTermsProjectionColumn.Flag(columnName, termKey, defaultWhenAbsent);

    private const SecurityTermsProjectionTextNormalization Verbatim = SecurityTermsProjectionTextNormalization.Verbatim;

    /// <summary>
    /// Columns every projection carries from the record itself rather than from its terms, written
    /// before the class-specific columns. Matches the spine the hand-written projections already
    /// share (<c>security_id, display_name, currency, …, primary_identifier_value, version</c>). A
    /// descriptor that opts out of <see cref="SecurityTermsProjectionDescriptor.ProjectsCurrency"/>
    /// drops <c>currency</c>; use <see cref="LeadingIdentityColumnsFor"/> for a specific descriptor.
    /// </summary>
    internal static readonly IReadOnlyList<string> LeadingIdentityColumns =
        ["security_id", "display_name", "currency"];

    private static readonly IReadOnlyList<string> LeadingIdentityColumnsWithoutCurrency =
        LeadingIdentityColumns.Where(static column => column != "currency").ToArray();

    /// <summary>The leading identity columns <paramref name="descriptor"/>'s parent table carries.</summary>
    internal static IReadOnlyList<string> LeadingIdentityColumnsFor(SecurityTermsProjectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.ProjectsCurrency ? LeadingIdentityColumns : LeadingIdentityColumnsWithoutCurrency;
    }

    /// <summary>Identity columns written after the class-specific columns.</summary>
    internal static readonly IReadOnlyList<string> TrailingIdentityColumns =
        ["primary_identifier_value", "version"];

    /// <summary>The key columns of every child projection table, written before its element columns.</summary>
    internal static readonly IReadOnlyList<string> ChildKeyColumns = ["security_id", "ordinal"];

    /// <summary>
    /// The descriptor registered for <paramref name="assetClass"/> (canonical name, ordinal match).
    /// Throws for a class without a registry projection: the specialized Bond and Option writers
    /// and the profile-governed CustomAsset are not here.
    /// </summary>
    internal static SecurityTermsProjectionDescriptor Require(string assetClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetClass);
        return Descriptors.FirstOrDefault(descriptor =>
                string.Equals(descriptor.AssetClass, assetClass, StringComparison.Ordinal))
            ?? throw new ArgumentException(
                $"'{assetClass}' has no SecurityTermsProjectionRegistry descriptor.",
                nameof(assetClass));
    }

    /// <summary>
    /// Every column of <paramref name="descriptor"/>'s parent table in write order: the leading
    /// identity columns, the class-specific columns, then the trailing identity columns. The upsert
    /// binds exactly this list and the reference-projection readers select it.
    /// </summary>
    internal static IReadOnlyList<string> ParentColumnNames(SecurityTermsProjectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return LeadingIdentityColumnsFor(descriptor)
            .Concat(descriptor.Columns.Select(static column => column.ColumnName))
            .Concat(TrailingIdentityColumns)
            .ToArray();
    }

    /// <summary>Every column of <paramref name="child"/> in write order: the key columns, then its element columns.</summary>
    internal static IReadOnlyList<string> ChildColumnNames(SecurityTermsProjectionChildTable child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return ChildKeyColumns
            .Concat(child.Columns.Select(static column => column.ColumnName))
            .ToArray();
    }

    /// <summary>
    /// The registered schema-driven projections, in fan-out order. DirectLoan and StructuredCredit
    /// are the first two Asset Operations classes to leave their economic terms in a JSONB blob:
    /// both declare ProjectedCashFlows, Reconciliation and LedgerProjection in
    /// <see cref="SecurityAssetClassCatalog"/>, so their borrower, spread, instalment schedule,
    /// tranche, original face and dated pool factors drive money movement and need to be queryable
    /// as columns rather than reachable only by parsing the blob one security at a time.
    /// CryptoCurrency, Deposit, MoneyMarketFund and CertificateOfDeposit follow: flat classes whose
    /// hand-written writers were migrated onto the registry without changing their persisted rows.
    /// </summary>
    internal static readonly IReadOnlyList<SecurityTermsProjectionDescriptor> Descriptors =
    [
        new(
            AssetClass: "DirectLoan",
            TableName: "direct_loan_projection",
            Columns:
            [
                Gate("borrower", "borrower", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("reference_index", "referenceIndex", SecurityAssetTermFieldType.String),
                Optional("spread_bps", "spreadBps", SecurityAssetTermFieldType.Decimal),
                Optional("current_coupon_rate", "currentCouponRate", SecurityAssetTermFieldType.Decimal),
                Optional("reset_frequency", "resetFrequency", SecurityAssetTermFieldType.String),
                Optional("pricing_source", "pricingSource", SecurityAssetTermFieldType.String)
            ],
            ChildTables:
            [
                new(
                    TableName: "direct_loan_covenant_projection",
                    TermKey: "covenants",
                    Columns:
                    [
                        new("covenant_type", "covenantType", SecurityAssetTermFieldType.String, Required: true),
                        // The canonical covenant threshold is a STRING ("4.5x", "2.00x fixed charge"),
                        // not a number — projecting it as numeric would lose every ratio covenant.
                        new("threshold", "threshold", SecurityAssetTermFieldType.String, Required: true),
                        new("notes", "notes", SecurityAssetTermFieldType.String)
                    ]),
                new(
                    TableName: "direct_loan_principal_schedule_projection",
                    TermKey: "principalSchedule",
                    Columns:
                    [
                        new("payment_date", "paymentDate", SecurityAssetTermFieldType.Date, Required: true),
                        new("amount", "amount", SecurityAssetTermFieldType.Decimal, Required: true, MustBePositive: true)
                    ])
            ]),
        new(
            AssetClass: "StructuredCredit",
            TableName: "structured_credit_projection",
            Columns:
            [
                Gate("tranche", "tranche", SecurityAssetTermFieldType.String),
                Optional("pool_id", "poolId", SecurityAssetTermFieldType.String),
                Gate("collateral_type", "collateralType", SecurityAssetTermFieldType.String),
                Gate("original_face", "originalFace", SecurityAssetTermFieldType.Decimal),
                Optional("current_factor", "currentFactor", SecurityAssetTermFieldType.Decimal),
                Gate("coupon_or_index", "couponOrIndex", SecurityAssetTermFieldType.String),
                // The free-text trustee-report pointer, kept distinct from the typed dated schedule
                // in the child table so a reader cannot mistake prose for factor data.
                Optional("factor_schedule_reference", "factorSchedule", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date)
            ],
            ChildTables:
            [
                new(
                    TableName: "structured_credit_factor_schedule_projection",
                    TermKey: "factorScheduleEntries",
                    Columns:
                    [
                        new("as_of_date", "asOfDate", SecurityAssetTermFieldType.Date, Required: true),
                        new("factor", "factor", SecurityAssetTermFieldType.Decimal, Required: true)
                    ])
            ]),

        // The four flat classes below were hand-written writers (migrations 012–015) and moved here
        // behaviour-preserving. Their non-default normalizations reproduce exactly what those
        // writers bound: optional strings went in verbatim (whitespace and "" kept), MMF fund family
        // was trimmed but never nulled, crypto pair codes were trimmed and upper-cased, and the
        // NOT NULL DEFAULT FALSE flags bound false for an absent or malformed term.
        new(
            AssetClass: "CryptoCurrency",
            TableName: "crypto_projection",
            Columns:
            [
                Gate("base_currency", "baseCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Gate("quote_currency", "quoteCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("network", "network", SecurityAssetTermFieldType.String, Verbatim)
            ],
            ChildTables: [],
            // A crypto pair states both currencies as terms; migration 012 gave it no currency column.
            ProjectsCurrency: false),
        new(
            AssetClass: "Deposit",
            TableName: "deposit_projection",
            Columns:
            [
                Gate("deposit_type", "depositType", SecurityAssetTermFieldType.String),
                Gate("institution_name", "institutionName", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("interest_rate", "interestRate", SecurityAssetTermFieldType.Decimal),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String, Verbatim),
                Flag("is_callable", "isCallable", defaultWhenAbsent: false)
            ],
            ChildTables: []),
        new(
            AssetClass: "MoneyMarketFund",
            TableName: "money_market_fund_projection",
            Columns:
            [
                Optional("fund_family", "fundFamily", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.Trim),
                Flag("sweep_eligible", "sweepEligible", defaultWhenAbsent: false),
                Optional("weighted_average_maturity_days", "weightedAverageMaturityDays", SecurityAssetTermFieldType.Integer),
                Flag("liquidity_fee_eligible", "liquidityFeeEligible", defaultWhenAbsent: false)
            ],
            ChildTables: []),
        new(
            AssetClass: "CertificateOfDeposit",
            TableName: "certificate_of_deposit_projection",
            Columns:
            [
                Gate("issuer_name", "issuerName", SecurityAssetTermFieldType.String),
                Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("coupon_rate", "couponRate", SecurityAssetTermFieldType.Decimal),
                Optional("callable_date", "callableDate", SecurityAssetTermFieldType.Date),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String, Verbatim)
            ],
            ChildTables: []),
        // The five classes below were specialized writers. Their identity spine, gates, delete
        // branch and upsert are now the registry's; what is genuinely class-specific — common-terms
        // columns, fallback chains and derived values — is a Derived column that calls the same
        // readers the writers used, so every persisted value is unchanged
        // (SpecializedProjectionGoldenTests captured the writers' rows before the move).
        new(
            AssetClass: "Equity",
            TableName: "equity_projection",
            Columns:
            [
                Optional("share_class", "shareClass", SecurityAssetTermFieldType.String, Verbatim),
                Optional("voting_rights_cat", "votingRightsCat", SecurityAssetTermFieldType.String, Verbatim),
                SecurityTermsProjectionColumn.Derived("classification",
                    static record => SecurityTermReader.ReadEquityClassification(record.AssetSpecificTerms)),
                CommonTerm("exchange_code", "exchange"),
                CommonTerm("country_of_risk", "countryOfRisk"),
                CommonTerm("issuer_name", "issuerName")
            ],
            ChildTables: []),
        new(
            AssetClass: "FxSpot",
            TableName: "fxspot_projection",
            Columns:
            [
                Gate("base_currency", "baseCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Gate("quote_currency", "quoteCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                SecurityTermsProjectionColumn.Derived("pair_code", static record =>
                    $"{PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "baseCurrency")?.Trim().ToUpperInvariant()}" +
                    $"{PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "quoteCurrency")?.Trim().ToUpperInvariant()}")
            ],
            ChildTables: [],
            ProjectsCurrency: false),
        new(
            AssetClass: "Future",
            TableName: "future_projection",
            Columns:
            [
                SecurityTermsProjectionColumn.Derived("root_symbol", static record =>
                    (PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "rootSymbol")
                     ?? record.PrimaryIdentifierValue.Trim().ToUpperInvariant()).Trim().ToUpperInvariant()),
                SecurityTermsProjectionColumn.Derived("contract_month", static record =>
                    PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "contractMonth") ?? string.Empty),
                SecurityTermsProjectionColumn.Derived("expiry_date", static record =>
                    FutureExpiry(record)?.ToDateTime(TimeOnly.MinValue), gates: true),
                SecurityTermsProjectionColumn.Derived("multiplier", static record =>
                    PostgresSecurityMasterStore.GetOptionalDecimal(record.AssetSpecificTerms, "multiplier") ?? 1m),
                Optional("settlement_type", "settlementType", SecurityAssetTermFieldType.String, Verbatim),
                Flag("is_roll_target", "isRollTarget", defaultWhenAbsent: false),
                Optional("roll_window_days", "rollWindowDays", SecurityAssetTermFieldType.Integer),
                SecurityTermsProjectionColumn.Derived("last_trading_dt", static record =>
                    (PostgresSecurityMasterStore.GetOptionalDateOnly(record.AssetSpecificTerms, "lastTradingDt") ?? FutureExpiry(record))
                        ?.ToDateTime(TimeOnly.MinValue)),
                Optional("first_notice_dt", "firstNoticeDt", SecurityAssetTermFieldType.Date),
                Optional("delivery_month_dt", "deliveryMonthDt", SecurityAssetTermFieldType.Date),
                Optional("delivery_location_code", "deliveryLocationCode", SecurityAssetTermFieldType.String, Verbatim),
                SecurityTermsProjectionColumn.Derived("lifecycle_stat", static record => FutureLifecycleState(record))
            ],
            ChildTables: []),
        new(
            AssetClass: "Swap",
            TableName: "swap_projection",
            Columns:
            [
                SecurityTermsProjectionColumn.Derived("swap_type", static record =>
                    PostgresSecurityMasterStore.DeriveSwapType(record.AssetSpecificTerms)),
                Gate("effective_date", "effectiveDate", SecurityAssetTermFieldType.Date),
                Gate("maturity_date", "maturityDate", SecurityAssetTermFieldType.Date),
                SecurityTermsProjectionColumn.Derived("lifecycle_stat", static _ => "Active")
            ],
            ChildTables: []),
        new(
            AssetClass: "Commodity",
            TableName: "commodity_projection",
            Columns:
            [
                Gate("commodity_type", "commodityType", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.Trim),
                Optional("denomination", "denomination", SecurityAssetTermFieldType.String, Verbatim),
                Optional("contract_size", "contractSize", SecurityAssetTermFieldType.Decimal),
                // exchangeCode and deliveryCountry are not declared terms — the canonical serializer
                // never writes them — so they are read verbatim for rows written by older importers.
                SecurityTermsProjectionColumn.Derived("exchange_code", static record =>
                    PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "exchangeCode")),
                SecurityTermsProjectionColumn.Derived("delivery_country", static record =>
                    PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, "deliveryCountry"))
            ],
            ChildTables: [])
,
        // The twelve classes below (migration 043) had no relational projection at all: their terms
        // were reachable only by parsing the JSONB blob one security at a time. Each is a pure data
        // declaration over the terms contract — gates on the terms the schema marks Required, nullable
        // columns for the rest, NOT NULL DEFAULT FALSE for the one required flag — with no derived
        // columns, so the registry's generic writer is the whole implementation. CustomAsset stays
        // unprojected on purpose: its fields are governed per profile and have no fixed column set.
        new(
            AssetClass: "CommercialPaper",
            TableName: "commercial_paper_projection",
            Columns:
            [
                Gate("issuer_name", "issuerName", SecurityAssetTermFieldType.String),
                Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("discount_rate", "discountRate", SecurityAssetTermFieldType.Decimal),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String, Verbatim),
                Flag("is_asset_backed", "isAssetBacked", defaultWhenAbsent: false)
            ],
            ChildTables: []),
        new(
            AssetClass: "TreasuryBill",
            TableName: "treasury_bill_projection",
            Columns:
            [
                Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("auction_date", "auctionDate", SecurityAssetTermFieldType.Date),
                Optional("cusip", "cusip", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("discount_rate", "discountRate", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []),
        new(
            AssetClass: "Repo",
            TableName: "repo_projection",
            Columns:
            [
                Gate("counterparty", "counterparty", SecurityAssetTermFieldType.String),
                Gate("start_date", "startDate", SecurityAssetTermFieldType.Date),
                Gate("end_date", "endDate", SecurityAssetTermFieldType.Date),
                Optional("repo_rate", "repoRate", SecurityAssetTermFieldType.Decimal),
                Optional("collateral_type", "collateralType", SecurityAssetTermFieldType.String),
                Optional("haircut", "haircut", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []),
        new(
            AssetClass: "CashSweep",
            TableName: "cash_sweep_projection",
            Columns:
            [
                Gate("program_name", "programName", SecurityAssetTermFieldType.String),
                Gate("sweep_vehicle_type", "sweepVehicleType", SecurityAssetTermFieldType.String),
                Optional("sweep_frequency", "sweepFrequency", SecurityAssetTermFieldType.String),
                Optional("target_account_type", "targetAccountType", SecurityAssetTermFieldType.String),
                Optional("yield_rate", "yieldRate", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []),
        new(
            AssetClass: "OtherSecurity",
            TableName: "other_security_projection",
            Columns:
            [
                Gate("category", "category", SecurityAssetTermFieldType.String),
                Optional("sub_type", "subType", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("issuer_name", "issuerName", SecurityAssetTermFieldType.String),
                Optional("settlement_type", "settlementType", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []),
        new(
            AssetClass: "Cfd",
            TableName: "cfd_projection",
            Columns:
            [
                Gate("underlying_asset_class", "underlyingAssetClass", SecurityAssetTermFieldType.String),
                Optional("underlying_description", "underlyingDescription", SecurityAssetTermFieldType.String),
                Optional("leverage", "leverage", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []),
        new(
            AssetClass: "Warrant",
            TableName: "warrant_projection",
            Columns:
            [
                // The first Guid-typed projected term: an underlying Security Master identity, so it
                // is a uuid column that joins straight back to securities.security_id.
                Gate("underlying_id", "underlyingId", SecurityAssetTermFieldType.Guid),
                Gate("warrant_type", "warrantType", SecurityAssetTermFieldType.String),
                Optional("strike", "strike", SecurityAssetTermFieldType.Decimal),
                Optional("expiry_date", "expiry", SecurityAssetTermFieldType.Date),
                Optional("multiplier", "multiplier", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []),
        new(
            AssetClass: "InvestmentFund",
            TableName: "investment_fund_projection",
            Columns:
            [
                // Every InvestmentFund term is optional, so the projection has no gate: a fund row
                // exists for every record of the class and reads null where a term is unknown.
                Optional("fund_type", "fundType", SecurityAssetTermFieldType.String),
                Optional("fund_family", "fundFamily", SecurityAssetTermFieldType.String),
                Optional("nav_currency", "navCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("distribution_policy", "distributionPolicy", SecurityAssetTermFieldType.String),
                Optional("is_stable_nav", "isStableNav", SecurityAssetTermFieldType.Boolean),
                Optional("pricing_source", "pricingSource", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []),
        new(
            AssetClass: "PrivateFundInterest",
            TableName: "private_fund_interest_projection",
            Columns:
            [
                Gate("gp_sponsor", "gpSponsor", SecurityAssetTermFieldType.String),
                Gate("strategy", "strategy", SecurityAssetTermFieldType.String),
                Gate("vintage", "vintage", SecurityAssetTermFieldType.Integer),
                Gate("commitment", "commitment", SecurityAssetTermFieldType.Decimal),
                Optional("funded_amount", "fundedAmount", SecurityAssetTermFieldType.Decimal),
                Optional("unfunded_amount", "unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Gate("nav_date", "navDate", SecurityAssetTermFieldType.Date),
                Optional("lockup", "lockup", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []),
        new(
            AssetClass: "PrivateCompanyEquity",
            TableName: "private_company_equity_projection",
            Columns:
            [
                Gate("issuer", "issuer", SecurityAssetTermFieldType.String),
                Gate("share_class", "shareClass", SecurityAssetTermFieldType.String),
                Gate("round", "round", SecurityAssetTermFieldType.String),
                Optional("ownership_percent", "ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Gate("cost_basis", "costBasis", SecurityAssetTermFieldType.Decimal),
                Optional("latest_valuation", "latestValuation", SecurityAssetTermFieldType.Decimal),
                Optional("transfer_restrictions", "transferRestrictions", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []),
        new(
            AssetClass: "RealEstateHolding",
            TableName: "real_estate_holding_projection",
            Columns:
            [
                Gate("property_type", "propertyType", SecurityAssetTermFieldType.String),
                Gate("address_or_market", "addressOrMarket", SecurityAssetTermFieldType.String),
                Gate("ownership_percent", "ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Gate("appraisal_value", "appraisalValue", SecurityAssetTermFieldType.Decimal),
                Gate("valuation_date", "valuationDate", SecurityAssetTermFieldType.Date),
                Optional("debt_stack", "debtStack", SecurityAssetTermFieldType.String),
                Optional("sponsor", "sponsor", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []),
        new(
            AssetClass: "CommitmentGuarantee",
            TableName: "commitment_guarantee_projection",
            Columns:
            [
                Gate("counterparty", "counterparty", SecurityAssetTermFieldType.String),
                Optional("beneficiary", "beneficiary", SecurityAssetTermFieldType.String),
                Gate("committed_amount", "committedAmount", SecurityAssetTermFieldType.Decimal),
                Optional("unfunded_amount", "unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Gate("effective_date", "effectiveDate", SecurityAssetTermFieldType.Date),
                Optional("expiry_date", "expiryDate", SecurityAssetTermFieldType.Date),
                Optional("fee_rate", "feeRate", SecurityAssetTermFieldType.Decimal),
                Optional("collateral", "collateral", SecurityAssetTermFieldType.String)
            ],
            ChildTables:
            [
                // Same element shape as the direct-loan covenant list: a typed covenant with a prose
                // threshold ("$5m", "4.5x") that must never be coerced to a number.
                new(
                    TableName: "commitment_guarantee_covenant_projection",
                    TermKey: "covenants",
                    Columns:
                    [
                        new("covenant_type", "covenantType", SecurityAssetTermFieldType.String, Required: true),
                        new("threshold", "threshold", SecurityAssetTermFieldType.String, Required: true),
                        new("notes", "notes", SecurityAssetTermFieldType.String)
                    ])
            ])
    ];

    /// <summary>A verbatim common-terms string column (exchange, country of risk, issuer).</summary>
    private static SecurityTermsProjectionColumn CommonTerm(string columnName, string commonTermKey)
        => SecurityTermsProjectionColumn.Derived(columnName, record =>
            PostgresSecurityMasterStore.GetOptionalString(record.CommonTerms, commonTermKey));

    private static DateOnly? FutureExpiry(SecurityProjectionRecord record)
        => PostgresSecurityMasterStore.GetOptionalDateOnly(record.AssetSpecificTerms, "expiry");

    /// <summary>Retired once the record has an effective-to date; otherwise expiry, then roll target.</summary>
    private static string? FutureLifecycleState(SecurityProjectionRecord record)
    {
        if (FutureExpiry(record) is not { } expiry)
        {
            return null;
        }

        return record.EffectiveTo.HasValue ? "Retired"
            : expiry < DateOnly.FromDateTime(DateTime.UtcNow) ? "Expired"
            : PostgresSecurityMasterStore.GetOptionalBool(record.AssetSpecificTerms, "isRollTarget") == true ? "RollTarget"
            : "Active";
    }

    /// <summary>The asset classes covered by a schema-driven projection.</summary>
    internal static IReadOnlyList<string> AssetClasses { get; } =
        Descriptors.Select(static descriptor => descriptor.AssetClass).ToArray();

    /// <summary>
    /// Contract violations in <see cref="Descriptors"/>, empty when the registry is sound. Checked by
    /// a commit-time guard rather than thrown from a static constructor: a descriptor that reads a
    /// key the terms contract does not declare is a review-time defect, and failing type
    /// initialization would take the whole Security Master store down for it.
    /// </summary>
    internal static IReadOnlyList<string> ValidationIssues { get; } = Validate(Descriptors);

    /// <summary>Validates a descriptor set against the catalog, the terms schema, and SQL identifier safety.</summary>
    internal static IReadOnlyList<string> Validate(IReadOnlyList<SecurityTermsProjectionDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var issues = new List<string>();
        var seenAssetClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            var assetClass = descriptor.AssetClass;

            if (!seenAssetClasses.Add(assetClass))
            {
                issues.Add($"'{assetClass}' is registered more than once.");
            }

            if (!SecurityAssetClassCatalog.AssetClasses.Contains(assetClass, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add($"'{assetClass}' is not a canonical catalog asset class.");
            }

            if (!SecurityAssetTermsSchema.TryGetFields(assetClass, out var declaredFields))
            {
                issues.Add($"'{assetClass}' has no declared terms schema, so its projected columns cannot be checked.");
                declaredFields = [];
            }

            ValidateTableName(descriptor.TableName, assetClass, seenTables, issues);
            ValidateColumns(descriptor, declaredFields, issues);
            ValidateChildTables(descriptor, declaredFields, seenTables, issues);
        }

        return issues;
    }

    private static void ValidateColumns(
        SecurityTermsProjectionDescriptor descriptor,
        IReadOnlyList<SecurityAssetTermField> declaredFields,
        List<string> issues)
    {
        // The full spine is reserved even when a descriptor opts out of currency: a class column
        // named "currency" would read as the record's own currency to every consumer of the table.
        var reserved = LeadingIdentityColumns.Concat(TrailingIdentityColumns).ToArray();
        var seenColumns = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);

        foreach (var column in descriptor.Columns)
        {
            var target = $"{descriptor.AssetClass}.{descriptor.TableName}.{column.ColumnName}";

            if (!IsSafeIdentifier(column.ColumnName))
            {
                issues.Add($"{target} is not a lower snake_case SQL identifier.");
            }

            if (!seenColumns.Add(column.ColumnName))
            {
                issues.Add($"{target} duplicates another column or an identity-spine column.");
            }

            if (column.IsDerived)
            {
                // A derived column carries its own value function; there is no term to check
                // against the schema, and no decode type or normalization to validate.
                continue;
            }

            if (!IsProjectableScalar(column.Type))
            {
                issues.Add($"{target} declares {column.Type}, which has no scalar projection reader.");
            }

            if (!Enum.IsDefined(column.Normalization))
            {
                issues.Add($"{target} declares an unknown text normalization '{column.Normalization}'.");
            }
            else if (column.Normalization != SecurityTermsProjectionTextNormalization.TrimBlankAsAbsent
                && column.Type != SecurityAssetTermFieldType.String)
            {
                issues.Add($"{target} declares text normalization {column.Normalization} on a {column.Type} column; normalization applies only to String terms.");
            }

            if (column.DefaultWhenAbsent is not null)
            {
                if (column.Type != SecurityAssetTermFieldType.Boolean || column.DefaultWhenAbsent is not bool)
                {
                    issues.Add($"{target} declares a default-when-absent value; only a bool default on a Boolean column is supported.");
                }

                if (column.Gates)
                {
                    issues.Add($"{target} both gates the projection and declares a default-when-absent value, which makes the gate unreachable.");
                }
            }

            // Ordinal, not case-insensitive: the decode side reads the term with
            // JsonElement.TryGetProperty, which is case-SENSITIVE. Accepting "Borrower" here would
            // approve a descriptor whose gating column can never resolve, silently suppressing every
            // projection of the class while ValidationIssues stayed empty.
            var declared = declaredFields.FirstOrDefault(field =>
                string.Equals(field.Key, column.TermKey, StringComparison.Ordinal));

            if (declared is null)
            {
                issues.Add(
                    $"{target} reads term '{column.TermKey}', which SecurityAssetTermsSchema does not declare for {descriptor.AssetClass}.");
                continue;
            }

            if (declared.Type != column.Type)
            {
                issues.Add(
                    $"{target} reads term '{column.TermKey}' as {column.Type}, but the terms schema declares it as {declared.Type}.");
            }

            if (column.Gates && !declared.Required)
            {
                issues.Add(
                    $"{target} gates the projection on optional term '{column.TermKey}'; gating on a term the serializer may omit drops projections for valid records.");
            }
        }
    }

    private static void ValidateChildTables(
        SecurityTermsProjectionDescriptor descriptor,
        IReadOnlyList<SecurityAssetTermField> declaredFields,
        HashSet<string> seenTables,
        List<string> issues)
    {
        foreach (var child in descriptor.ChildTables)
        {
            ValidateTableName(child.TableName, descriptor.AssetClass, seenTables, issues);

            // Ordinal for the same reason as the scalar columns above: an approved descriptor whose
            // array key differs only in case would publish an empty schedule, not a missing one.
            var declared = declaredFields.FirstOrDefault(field =>
                string.Equals(field.Key, child.TermKey, StringComparison.Ordinal));

            if (declared is null)
            {
                issues.Add(
                    $"{descriptor.AssetClass}.{child.TableName} fans out term '{child.TermKey}', which SecurityAssetTermsSchema does not declare.");
            }
            else if (declared.Type != SecurityAssetTermFieldType.Array)
            {
                issues.Add(
                    $"{descriptor.AssetClass}.{child.TableName} fans out term '{child.TermKey}', which the terms schema declares as {declared.Type}, not Array.");
            }

            if (child.Columns.Count == 0)
            {
                issues.Add($"{descriptor.AssetClass}.{child.TableName} declares no element columns.");
            }

            var seenColumns = new HashSet<string>(ChildKeyColumns, StringComparer.OrdinalIgnoreCase);
            foreach (var column in child.Columns)
            {
                var target = $"{descriptor.AssetClass}.{child.TableName}.{column.ColumnName}";

                if (!IsSafeIdentifier(column.ColumnName))
                {
                    issues.Add($"{target} is not a lower snake_case SQL identifier.");
                }

                if (!seenColumns.Add(column.ColumnName))
                {
                    issues.Add($"{target} duplicates another column or a child key column.");
                }

                if (!IsProjectableScalar(column.Type))
                {
                    issues.Add($"{target} declares {column.Type}, which has no scalar projection reader.");
                }
            }
        }
    }

    private static void ValidateTableName(string tableName, string assetClass, HashSet<string> seenTables, List<string> issues)
    {
        if (!IsSafeIdentifier(tableName))
        {
            issues.Add($"{assetClass} table '{tableName}' is not a lower snake_case SQL identifier.");
        }

        if (!seenTables.Add(tableName))
        {
            issues.Add($"{assetClass} table '{tableName}' is already claimed by another projection.");
        }
    }

    /// <summary>
    /// The term types a projected column can carry. Array and Object are structural (Array is fanned
    /// out to a child table instead); every other declared type has a scalar reader on the projection
    /// path, Guid landing in a uuid column.
    /// </summary>
    private static bool IsProjectableScalar(SecurityAssetTermFieldType type)
        => type is SecurityAssetTermFieldType.String
            or SecurityAssetTermFieldType.Decimal
            or SecurityAssetTermFieldType.Integer
            or SecurityAssetTermFieldType.Boolean
            or SecurityAssetTermFieldType.Date
            or SecurityAssetTermFieldType.Guid;

    /// <summary>
    /// Table and column names are interpolated into projection SQL, so they are held to a literal
    /// lower snake_case shape rather than trusted because they happen to be compile-time constants.
    /// </summary>
    private static bool IsSafeIdentifier(string identifier)
        => !string.IsNullOrEmpty(identifier) && SafeIdentifier().IsMatch(identifier);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SafeIdentifier();
}
