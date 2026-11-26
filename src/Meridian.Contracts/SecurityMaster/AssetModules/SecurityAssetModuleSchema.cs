namespace Meridian.Contracts.SecurityMaster;

/// <summary>Shared, frozen term fragments used by the class modules without registry dependencies.</summary>
internal static class SecurityAssetModuleSchema
{
    internal static SecurityAssetTermField Req(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => SecurityAssetTermField.Req(key, type, aliases);

    internal static SecurityAssetTermField Opt(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => SecurityAssetTermField.Opt(key, type, aliases);

    internal static SecurityAssetTermField ReqOneOf(string key, params string[] allowedValues)
        => SecurityAssetTermField.ReqOneOf(key, allowedValues);

    internal static SecurityAssetTermField OptOneOf(string key, params string[] allowedValues)
        => SecurityAssetTermField.OptOneOf(key, allowedValues);

    // The scheduleConventions block: how the contractual schedule kernel (ADR-025) builds an
    // instrument's periods. Every member is optional and Since 1 within the block, which carries the
    // class's terms version itself; a member added later needs a new readable version. It must be
    // declared above ElementFieldsByPath, which reads it while the type initializes.
    internal static readonly IReadOnlyList<SecurityAssetTermField> ScheduleConventionFields =
    [
        Opt("businessDayConvention", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.BusinessDayConvention),
        Opt("accrualAdjusted", SecurityAssetTermFieldType.Boolean),
        Opt("paymentCalendars", SecurityAssetTermFieldType.Array),
        Opt("paymentLagBusinessDays", SecurityAssetTermFieldType.Integer),
        Opt("rollConvention", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.RollConvention),
        Opt("endOfMonthRule", SecurityAssetTermFieldType.Boolean),
        Opt("firstRegularDate", SecurityAssetTermFieldType.Date),
        Opt("lastRegularDate", SecurityAssetTermFieldType.Date),
        OptOneOf("direction", "Forward", "Backward").WithVerbatimUndeclaredValue(),
        Opt("stubPolicy", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.StubPolicy),
        OptOneOf("couponRateUnit", "Percent", "Fraction").WithVerbatimUndeclaredValue(),
        OptOneOf("couponCashBasis", "Level", "DayCount").WithVerbatimUndeclaredValue()
    ];

    // Notice terms shared by call and put entries (ADR-025 Applications, increment 4b): how many days
    // before the exercise date notice (or a holder's put election) is due, at the latest and the
    // earliest, and whether the counts are business or calendar days.
    internal static readonly IReadOnlyList<SecurityAssetTermField> NoticeFields =
    [
        Opt("minimumNoticeDays", SecurityAssetTermFieldType.Integer),
        Opt("maximumNoticeDays", SecurityAssetTermFieldType.Integer),
        OptOneOf("noticeDayType", "Business", "Calendar").WithVerbatimUndeclaredValue()
    ];

    // One entry of a declared call schedule (asset-class terms version 3 on Bond and
    // CertificateOfDeposit, 4 on StructuredCredit; readable before writable, ADR-024). Meanings are
    // frozen in ADR-025 Applications. The date is required in meaning but optional here, because a
    // versioned container's members are untagged and optional: the reader drops an undated entry and
    // the contract step refuses it on write. The shape is frozen once readable
    // (terms-element-shapes/versioned-containers.json): a member added later needs a new container key
    // at a new readable version, so a later class-wide term belongs at the top level instead. Both
    // entry lists are declared above ElementFieldsByPath, which reads them while the type initializes.
    internal static readonly IReadOnlyList<SecurityAssetTermField> CallScheduleEntryFields =
    [
        Opt("callDate", SecurityAssetTermFieldType.Date),
        Opt("callEndDate", SecurityAssetTermFieldType.Date),
        OptOneOf("exerciseStyle", "European", "American", "Bermudan").WithVerbatimUndeclaredValue(),
        Opt("exerciseFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
        Opt("callPrice", SecurityAssetTermFieldType.Decimal),
        OptOneOf("priceBasis", "PercentOfPar", "PercentOfAccretedValue").WithVerbatimUndeclaredValue(),
        OptOneOf("priceType", "Fixed", "MakeWhole").WithVerbatimUndeclaredValue(),
        Opt("makeWholeSpreadBps", SecurityAssetTermFieldType.Decimal),
        OptOneOf("partialRedemption", "WholeOnly", "ProRata", "ByLot").WithVerbatimUndeclaredValue(),
        .. NoticeFields
    ];

    // One entry of a declared put schedule: a holder-elective put (a mandatory tender stays
    // mandatoryPutDate). The same version and freezing rules as the call entry.
    internal static readonly IReadOnlyList<SecurityAssetTermField> PutScheduleEntryFields =
    [
        Opt("putDate", SecurityAssetTermFieldType.Date),
        Opt("putEndDate", SecurityAssetTermFieldType.Date),
        OptOneOf("exerciseStyle", "European", "American", "Bermudan").WithVerbatimUndeclaredValue(),
        Opt("exerciseFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
        Opt("putPrice", SecurityAssetTermFieldType.Decimal),
        OptOneOf("priceBasis", "PercentOfPar", "PercentOfAccretedValue").WithVerbatimUndeclaredValue(),
        .. NoticeFields
    ];

}
