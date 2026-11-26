using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads direct loan reference projections from PostgreSQL by security and borrower.
/// </summary>
public sealed class PostgresDirectLoanReferenceProjectionStore : IDirectLoanReferenceProjectionStore
{
    internal const string AssetClass = "DirectLoan";
    internal const string CovenantTable = "direct_loan_covenant_projection";
    internal const string PrincipalScheduleTable = "direct_loan_principal_schedule_projection";

    internal static readonly RegistryProjectionQuery ByBorrower = new(nameof(ByBorrower),
        """
        where lower(borrower) = lower(@borrower)
        order by maturity_date nulls last, display_name
        """);

    internal static readonly RegistryProjectionQuery ByReferenceIndex = new(nameof(ByReferenceIndex),
        """
        where lower(reference_index) = lower(@reference_index)
        order by maturity_date nulls last, display_name
        """);

    internal static readonly RegistryProjectionQuery MaturityLadder = new(nameof(MaturityLadder),
        """
        where maturity_date between @from_date and @to_date
        order by maturity_date, display_name
        """);

    /// <summary>One loan's child rows in declared order; shared by both child tables.</summary>
    internal static readonly RegistryProjectionQuery ChildrenBySecurity = new(nameof(ChildrenBySecurity),
        """
        where security_id = @security_id
        order by ordinal
        """);

    internal static readonly RegistryProjectionQuery PrincipalPaymentsDue = new(nameof(PrincipalPaymentsDue),
        """
        where payment_date between @from_date and @to_date
        order by payment_date, security_id, ordinal
        """);

    private readonly RegistryReferenceProjectionStore<DirectLoanProjectionRow> _loans;
    private readonly RegistryReferenceProjectionStore<DirectLoanCovenantRow> _covenants;
    private readonly RegistryReferenceProjectionStore<DirectLoanPrincipalPaymentRow> _principalSchedule;

    public PostgresDirectLoanReferenceProjectionStore(SecurityMasterOptions options)
    {
        _loans = CreateLoanStore(options);
        _covenants = CreateCovenantStore(options);
        _principalSchedule = CreatePrincipalScheduleStore(options);
    }

    internal static RegistryReferenceProjectionStore<DirectLoanProjectionRow> CreateLoanStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapLoanRow, [ByBorrower, ByReferenceIndex, MaturityLadder]);

    internal static RegistryReferenceProjectionStore<DirectLoanCovenantRow> CreateCovenantStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapCovenantRow, [ChildrenBySecurity], CovenantTable);

    internal static RegistryReferenceProjectionStore<DirectLoanPrincipalPaymentRow> CreatePrincipalScheduleStore(SecurityMasterOptions options)
        => new(options, AssetClass, MapPrincipalPaymentRow, [ChildrenBySecurity, PrincipalPaymentsDue], PrincipalScheduleTable);

    public Task<DirectLoanProjectionRow?> GetDirectLoanAsync(Guid securityId, CancellationToken ct = default)
        => _loans.GetBySecurityIdAsync(securityId, ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetByBorrowerAsync(string borrower, CancellationToken ct = default)
        => _loans.ListAsync(ByBorrower, [new NpgsqlParameter("borrower", borrower.Trim())], ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetByReferenceIndexAsync(string referenceIndex, CancellationToken ct = default)
        => _loans.ListAsync(ByReferenceIndex, [new NpgsqlParameter("reference_index", referenceIndex.Trim())], ct);

    public Task<IReadOnlyList<DirectLoanProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => _loans.ListAsync(MaturityLadder, DateWindow(from, to), ct);

    public Task<IReadOnlyList<DirectLoanCovenantRow>> GetCovenantsAsync(Guid securityId, CancellationToken ct = default)
        => _covenants.ListAsync(ChildrenBySecurity, [new NpgsqlParameter("security_id", securityId)], ct);

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalScheduleAsync(Guid securityId, CancellationToken ct = default)
        => _principalSchedule.ListAsync(ChildrenBySecurity, [new NpgsqlParameter("security_id", securityId)], ct);

    public Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalPaymentsDueAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => _principalSchedule.ListAsync(PrincipalPaymentsDue, DateWindow(from, to), ct);

    private static NpgsqlParameter[] DateWindow(DateOnly from, DateOnly to)
        =>
        [
            new NpgsqlParameter("from_date", from.ToDateTime(TimeOnly.MinValue)),
            new NpgsqlParameter("to_date", to.ToDateTime(TimeOnly.MinValue))
        ];

    internal static DirectLoanProjectionRow MapLoanRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("display_name"),
            row.String("currency"),
            row.String("borrower"),
            row.NullableDate("maturity_date"),
            row.NullableString("reference_index"),
            row.NullableDecimal("spread_bps"),
            row.NullableDecimal("current_coupon_rate"),
            row.NullableString("reset_frequency"),
            row.NullableString("pricing_source"),
            row.String("primary_identifier_value"),
            row.Int64("version"));

    internal static DirectLoanCovenantRow MapCovenantRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.Int32("ordinal"),
            row.String("covenant_type"),
            row.String("threshold"),
            row.NullableString("notes"));

    internal static DirectLoanPrincipalPaymentRow MapPrincipalPaymentRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.Int32("ordinal"),
            row.Date("payment_date"),
            row.Decimal("amount"));
}
