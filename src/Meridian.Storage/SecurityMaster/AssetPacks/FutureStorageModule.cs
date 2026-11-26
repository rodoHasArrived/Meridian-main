using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class FutureStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
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
            ChildTables: []));

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
}
