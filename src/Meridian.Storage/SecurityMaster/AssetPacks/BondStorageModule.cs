using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class BondStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Bond",
            TableName: "bond_projection",
            Columns:
            [
                SecurityTermsProjectionColumn.Derived("issuer_name", BondIssuerName),
                SecurityTermsProjectionColumn.Derived("seniority", static record => BondTerm(record, "seniority")),
                SecurityTermsProjectionColumn.Derived("subclass", static record => BondTerm(record, "subclass")),
                SecurityTermsProjectionColumn.Derived("issue_date", static record => BondDate(record, "issueDate")),
                SecurityTermsProjectionColumn.Derived("maturity_date", static record => BondDate(record, "maturity"), gates: true),
                SecurityTermsProjectionColumn.Derived("coupon_kind", static record => BondCoupon(record).Kind),
                SecurityTermsProjectionColumn.Derived("fixed_coupon_rate", static record => BondCoupon(record).FixedRate),
                SecurityTermsProjectionColumn.Derived("floating_rate_index", static record => BondCoupon(record).FloatingIndex),
                SecurityTermsProjectionColumn.Derived("floating_spread_bps", static record => BondCoupon(record).SpreadBps)
            ],
            ChildTables: [],
            ProjectsCurrency: false,
            ProjectsDisplayName: false,
            ProjectsPrimaryIdentifier: false,
            SatelliteTables:
            [
                new(
                    TableName: "bond_lifecycle_projection",
                    Columns:
                    [
                        SecurityTermsProjectionColumn.Derived("lifecycle_stat", BondLifecycleState),
                        SecurityTermsProjectionColumn.Derived("issue_date", static record => BondDate(record, "issueDate")),
                        SecurityTermsProjectionColumn.Derived("call_date", static record => BondDate(record, "callDate")),
                        SecurityTermsProjectionColumn.Derived("maturity_date", static record => BondDate(record, "maturity")),
                        SecurityTermsProjectionColumn.Derived("is_callable", static record => BondIsCallable(record)),
                        SecurityTermsProjectionColumn.Derived("subclass", static record => BondTerm(record, "subclass")),
                        SecurityTermsProjectionColumn.Derived("par", static record =>
                            PostgresSecurityMasterStore.GetOptionalDecimal(record.AssetSpecificTerms, "par")),
                        SecurityTermsProjectionColumn.Derived("payment_frequency", static record => BondTerm(record, "paymentFrequency")),
                        SecurityTermsProjectionColumn.Derived("legal_final_maturity", static record => BondDate(record, "legalFinalMaturity")),
                        SecurityTermsProjectionColumn.Derived("pre_refund_date", static record => BondDate(record, "preRefundDate")),
                        SecurityTermsProjectionColumn.Derived("mandatory_put_date", static record => BondDate(record, "mandatoryPutDate"))
                    ]),
                new(
                    TableName: "bond_accrual_convention_projection",
                    Columns:
                    [
                        SecurityTermsProjectionColumn.Derived("day_count_convention", static record => BondCoupon(record).DayCount),
                        SecurityTermsProjectionColumn.Derived("settlement_cycle_days", static record =>
                            PostgresSecurityMasterStore.GetOptionalInt(record.CommonTerms, "settlementCycleDays")),
                        SecurityTermsProjectionColumn.Derived("holiday_calendar_id", static record =>
                            PostgresSecurityMasterStore.GetOptionalString(record.CommonTerms, "holidayCalendarId")),
                        SecurityTermsProjectionColumn.Derived("coupon_kind", static record => BondCoupon(record).Kind),
                        SecurityTermsProjectionColumn.Derived("fixed_coupon_rate", static record => BondCoupon(record).FixedRate),
                        SecurityTermsProjectionColumn.Derived("floating_rate_index", static record => BondCoupon(record).FloatingIndex),
                        SecurityTermsProjectionColumn.Derived("floating_spread_bps", static record => BondCoupon(record).SpreadBps)
                    ]),
                new(
                    TableName: "bond_issuer_projection",
                    Columns:
                    [
                        SecurityTermsProjectionColumn.Derived("issuer_name", BondIssuerName),
                        SecurityTermsProjectionColumn.Derived("maturity_date", static record => BondDate(record, "maturity")),
                        SecurityTermsProjectionColumn.Derived("seniority", static record => BondTerm(record, "seniority"))
                    ],
                    ProjectsDisplayName: true,
                    ProjectsCurrency: true,
                    ProjectsPrimaryIdentifier: true,
                    Condition: static record => !string.IsNullOrWhiteSpace(BondIssuerName(record)))
            ]));

    private static string? BondTerm(SecurityProjectionRecord record, string key)
        => PostgresSecurityMasterStore.GetOptionalString(record.AssetSpecificTerms, key);

    private static DateTime? BondDate(SecurityProjectionRecord record, string key)
        => PostgresSecurityMasterStore.GetOptionalDateOnly(record.AssetSpecificTerms, key)?.ToDateTime(TimeOnly.MinValue);

    private static bool BondIsCallable(SecurityProjectionRecord record)
        => PostgresSecurityMasterStore.GetOptionalBool(record.AssetSpecificTerms, "isCallable") ?? false;

    /// <summary>The issuer named in the terms, else in the common terms (the writer's fallback order).</summary>
    private static string? BondIssuerName(SecurityProjectionRecord record)
        => BondTerm(record, "issuerName") ?? PostgresSecurityMasterStore.GetOptionalString(record.CommonTerms, "issuerName");

    /// <summary>Retired, matured, callable (past call date), when-issued, else active — the writer's order of tests.</summary>
    private static string? BondLifecycleState(SecurityProjectionRecord record)
    {
        if (PostgresSecurityMasterStore.GetOptionalDateOnly(record.AssetSpecificTerms, "maturity") is not { } maturity)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var issueDate = PostgresSecurityMasterStore.GetOptionalDateOnly(record.AssetSpecificTerms, "issueDate");
        var callDate = PostgresSecurityMasterStore.GetOptionalDateOnly(record.AssetSpecificTerms, "callDate");

        return record.EffectiveTo.HasValue ? "Retired"
            : maturity < today ? "Matured"
            : BondIsCallable(record) && callDate.HasValue && callDate.Value <= today ? "Callable"
            : issueDate.HasValue && issueDate.Value > today ? "WhenIssued"
            : "Active";
    }

    private readonly record struct BondCouponTerms(string? Kind, decimal? FixedRate, string? FloatingIndex, decimal? SpreadBps, string? DayCount);

    /// <summary>
    /// The coupon as the serializer writes it — flat couponType/couponRate/floatingIndex/spreadBps/
    /// dayCount per the Bond entry in <see cref="SecurityAssetTermsSchema"/> — falling back per field
    /// to the legacy nested <c>coupon</c> object for externally authored payloads that still use it.
    /// Reading only the nested shape once left every coupon column null for canonically serialized
    /// bonds, so the flat keys are read first.
    /// </summary>
    private static BondCouponTerms BondCoupon(SecurityProjectionRecord record)
    {
        var terms = record.AssetSpecificTerms;
        var coupon = PostgresSecurityMasterStore.GetOptionalObject(terms, "coupon");
        return new(
            BondTerm(record, "couponType") ?? (coupon.HasValue ? PostgresSecurityMasterStore.GetOptionalString(coupon.Value, "kind") : null),
            PostgresSecurityMasterStore.GetOptionalDecimal(terms, "couponRate") ?? (coupon.HasValue ? PostgresSecurityMasterStore.GetOptionalDecimal(coupon.Value, "rate") : null),
            BondTerm(record, "floatingIndex") ?? (coupon.HasValue ? PostgresSecurityMasterStore.GetOptionalString(coupon.Value, "index") : null),
            PostgresSecurityMasterStore.GetOptionalDecimal(terms, "spreadBps") ?? (coupon.HasValue ? PostgresSecurityMasterStore.GetOptionalDecimal(coupon.Value, "spreadBps") : null),
            BondTerm(record, "dayCount") ?? (coupon.HasValue ? PostgresSecurityMasterStore.GetOptionalString(coupon.Value, "dayCountConvention") : null));
    }
}
