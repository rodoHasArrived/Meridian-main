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
/// Of the eleven writers that predate this registry, the four whose columns are pure term reads —
/// MoneyMarketFund, CertificateOfDeposit, Deposit and CryptoCurrency — now live here. They moved
/// behind a database-backed parity guard that ran each hand-written writer and its descriptor into
/// the same table across a record matrix (gate hits and misses, whitespace gates, class changes,
/// padded and mixed-case values, explicit nulls, wrong JSON kinds) and required identical rows;
/// <c>RegistryMigratedProjectionGoldenTests</c> keeps those expectations as golden rows. Matching
/// them byte for byte is what the per-column <see cref="SecurityTermsProjectionTextNormalization"/>,
/// the boolean <see cref="SecurityTermsProjectionColumn.DefaultWhenAbsent"/> and the descriptor's
/// <see cref="SecurityTermsProjectionDescriptor.ProjectsCurrency"/> flag express. The rest stay
/// hand-written: Bond (derived lifecycle state, legacy nested-coupon fallback, common-terms
/// columns), Option (series and alias tables), Swap (type scanned out of legs), FxSpot
/// (concatenated pair code), Equity (common-terms columns), Future (derived lifecycle state and
/// fallback-derived root symbol, multiplier and last trading date), and Commodity, which
/// projects <c>exchangeCode</c> and <c>deliveryCountry</c> — keys the terms schema does not declare
/// and the canonical serializer never writes, so a descriptor for it would fail validation, and
/// declaring them would break the serializer's key-set round-trip guard.
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
            ChildTables: [])
    ];

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
    /// out to a child table instead), and Guid has no scalar reader on the projection path.
    /// </summary>
    private static bool IsProjectableScalar(SecurityAssetTermFieldType type)
        => type is SecurityAssetTermFieldType.String
            or SecurityAssetTermFieldType.Decimal
            or SecurityAssetTermFieldType.Integer
            or SecurityAssetTermFieldType.Boolean
            or SecurityAssetTermFieldType.Date;

    /// <summary>
    /// Table and column names are interpolated into projection SQL, so they are held to a literal
    /// lower snake_case shape rather than trusted because they happen to be compile-time constants.
    /// </summary>
    private static bool IsSafeIdentifier(string identifier)
        => !string.IsNullOrEmpty(identifier) && SafeIdentifier().IsMatch(identifier);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SafeIdentifier();
}
