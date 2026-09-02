using System.Text.RegularExpressions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

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
/// carry this term gets no projection row at all (and any stale row is deleted). A gating column
/// must be declared <c>Required</c> in <see cref="SecurityAssetTermsSchema"/> — gating on a term the
/// serializer may legitimately omit would drop projections for valid records.
/// </param>
internal sealed record SecurityTermsProjectionColumn(
    string ColumnName,
    string TermKey,
    SecurityAssetTermFieldType Type,
    bool Gates = false)
{
    /// <summary>A nullable projected column; a missing or malformed term writes NULL.</summary>
    internal static SecurityTermsProjectionColumn Optional(string columnName, string termKey, SecurityAssetTermFieldType type)
        => new(columnName, termKey, type);

    /// <summary>A NOT NULL projected column whose absence suppresses the whole projection row.</summary>
    internal static SecurityTermsProjectionColumn Gate(string columnName, string termKey, SecurityAssetTermFieldType type)
        => new(columnName, termKey, type, Gates: true);
}

/// <summary>
/// One column of a child (one-to-many) projection table, read from an element of the parent's
/// declared array term rather than from the terms document root.
/// </summary>
/// <param name="ColumnName">Target SQL column on the child table.</param>
/// <param name="ElementKey">The JSON key on each array element.</param>
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
internal sealed record SecurityTermsProjectionDescriptor(
    string AssetClass,
    string TableName,
    IReadOnlyList<SecurityTermsProjectionColumn> Columns,
    IReadOnlyList<SecurityTermsProjectionChildTable> ChildTables);

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
/// boilerplate, and folding them in would trade the duplication for a configuration language. The
/// eleven writers that predate this registry are unchanged; they migrate one at a time, each behind
/// its own regression guard, rather than in a single behaviour-preserving sweep.
/// </para>
/// </summary>
internal static partial class SecurityTermsProjectionRegistry
{
    private static SecurityTermsProjectionColumn Optional(string columnName, string termKey, SecurityAssetTermFieldType type)
        => SecurityTermsProjectionColumn.Optional(columnName, termKey, type);

    private static SecurityTermsProjectionColumn Gate(string columnName, string termKey, SecurityAssetTermFieldType type)
        => SecurityTermsProjectionColumn.Gate(columnName, termKey, type);

    /// <summary>
    /// Columns every projection carries from the record itself rather than from its terms, written
    /// before the class-specific columns. Matches the spine the hand-written projections already
    /// share (<c>security_id, display_name, currency, …, primary_identifier_value, version</c>).
    /// </summary>
    internal static readonly IReadOnlyList<string> LeadingIdentityColumns =
        ["security_id", "display_name", "currency"];

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
            ])
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
