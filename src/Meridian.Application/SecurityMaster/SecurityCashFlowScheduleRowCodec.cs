using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// The row contract is independent of the calculation model. Historical context receipts retain
/// their original row shape; current receipts serialize and compare every stored record member.
/// </summary>
internal static class SecurityCashFlowScheduleRowCodec
{
    public const int CurrentVersion = 2;

    public static StructuredCashFlowProjectionDto Current(StructuredCashFlowProjectionDto projection)
        => MapRows(projection, static row => row with { RowVersion = CurrentVersion });

    public static StructuredCashFlowProjectionDto Legacy(StructuredCashFlowProjectionDto projection)
        => ForVersion(projection, 0);

    public static StructuredCashFlowProjectionDto ForVersion(StructuredCashFlowProjectionDto projection, int rowVersion)
        => rowVersion switch
        {
            CurrentVersion => Current(projection),
            0 => MapRows(projection, static row => ReadFrozen(ToLegacyRow(row),
                SecurityCashFlowReceiptJsonContext.Default.LegacyStructuredCashFlowScheduleEntry)),
            1 => MapRows(projection, static row => ReadFrozen(ToVersionOneRow(row),
                SecurityCashFlowReceiptJsonContext.Default.VersionOneStructuredCashFlowScheduleEntry)),
            _ => throw new ArgumentOutOfRangeException(nameof(rowVersion))
        };

    private static StructuredCashFlowScheduleEntry ReadFrozen<T>(T row,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(JsonSerializer.Serialize(row, typeInfo),
            CurrentSecurityCashFlowRowJsonContext.Default.StructuredCashFlowScheduleEntry)!;

    internal static VersionOneStructuredCashFlowScheduleEntry ToVersionOneRow(StructuredCashFlowScheduleEntry row)
        => new(row.PeriodDate, row.PrincipalAmount, row.InterestAmount, row.Factor,
            row.UnadjustedStart, row.UnadjustedEnd, row.AccrualStart, row.AccrualEnd,
            ToLegacyRate(row), 1, row.InterestMeasuredFrom, row.InterestMeasuredTo);

    private static LegacyBondFloatingRateEvidence? ToLegacyRate(StructuredCashFlowScheduleEntry row)
        => row.RateEvidence is { } rate ? new(rate.ResetDate, rate.IsObserved,
            rate.IndexName, rate.IndexRate, rate.Spread, rate.Cap, rate.Floor, rate.Principal,
            rate.AccrualFraction, rate.OriginalRateUnit, rate.Source) : null;

    internal static LegacyStructuredCashFlowScheduleEntry ToLegacyRow(StructuredCashFlowScheduleEntry row)
        => new(row.PeriodDate, row.PrincipalAmount, row.InterestAmount, row.Factor,
            row.UnadjustedStart, row.UnadjustedEnd, row.AccrualStart, row.AccrualEnd, ToLegacyRate(row));

    public static bool HasSupportedRows(StructuredCashFlowProjectionDto projection, int rowVersion)
        => rowVersion is >= 0 and <= CurrentVersion && Rows(projection).All(row =>
            row.RowVersion == rowVersion && (rowVersion switch
            {
                0 => row.InterestMeasuredFrom is null && row.InterestMeasuredTo is null && row.InterestSegments is null,
                1 => row.InterestSegments is null,
                _ => TryValidateInterestSegments(row, out _)
            }));

    internal static bool TryValidateInterestSegments(StructuredCashFlowScheduleEntry row, out string? reason)
    {
        reason = null;
        if (row.InterestSegments is not { } segments)
            return true;
        if (segments.Count == 0)
        {
            if (row.InterestAmount == 0m)
                return true;
            reason = "Positive interest requires complete interest segment evidence.";
            return false;
        }
        if (row.InterestAccrualStart is not { } periodStart || row.InterestAccrualEnd is not { } periodEnd
            || periodEnd <= periodStart)
        {
            reason = "Interest segment evidence requires the original coupon interval.";
            return false;
        }
        var cursor = periodStart;
        var total = 0m;
        decimal? precedingFactorAfter = null;
        foreach (var segment in segments)
        {
            if (segment is null || segment.Start != cursor || segment.End <= segment.Start
                || segment.End > periodEnd || segment.PeriodStart != periodStart || segment.PeriodEnd != periodEnd
                || segment.ReferencePrincipal < 0m || segment.UnroundedInterestAmount < 0m
                || segment.Factor is < 0m or > 1m
                || segment.FactorAfter is not { } factorAfter || factorAfter < 0m || factorAfter > segment.Factor
                || precedingFactorAfter is { } preceding && segment.Factor != preceding
                || (segment.ReferencePrincipal == 0m) != (segment.Factor == 0m)
                || segment.ReferencePrincipal == 0m && segment.UnroundedInterestAmount != 0m)
            {
                reason = "Interest segments require contiguous original-period coverage, nonnegative economics and consistent opening/closing factors; zero principal and factor must occur together without interest.";
                return false;
            }
            try
            { total = checked(total + segment.UnroundedInterestAmount); }
            catch (OverflowException)
            {
                reason = "Interest segment totals exceed the supported monetary range.";
                return false;
            }
            cursor = segment.End;
            precedingFactorAfter = factorAfter;
        }
        if (cursor != periodEnd || decimal.Round(total, 4, MidpointRounding.AwayFromZero) != row.InterestAmount)
        {
            reason = "Interest segments must cover the whole coupon and reconcile to its rounded interest.";
            return false;
        }
        return true;
    }

    private static IEnumerable<StructuredCashFlowScheduleEntry> Rows(StructuredCashFlowProjectionDto projection)
        => projection.Schedule.Concat((projection.LegSchedules ?? []).SelectMany(static leg => leg.Schedule));

    private static StructuredCashFlowProjectionDto MapRows(StructuredCashFlowProjectionDto projection,
        Func<StructuredCashFlowScheduleEntry, StructuredCashFlowScheduleEntry> map)
        => projection with
        {
            Schedule = projection.Schedule.Select(map).ToArray(),
            LegSchedules = projection.LegSchedules?.Select(leg => leg with { Schedule = leg.Schedule.Select(map).ToArray() }).ToArray()
        };
}

/// <summary>
/// Receipt hashing and comparison dispatch through the declared row version. The legacy writer
/// never serializes the evolving current row, including when future members have nonnull defaults.
/// </summary>
internal sealed class SecurityCashFlowScheduleRowJsonConverter : JsonConverter<StructuredCashFlowScheduleEntry>
{
    public SecurityCashFlowScheduleRowJsonConverter() { }

    public override StructuredCashFlowScheduleEntry? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
        => JsonSerializer.Deserialize(ref reader, CurrentSecurityCashFlowRowJsonContext.Default.StructuredCashFlowScheduleEntry);

    public override void Write(Utf8JsonWriter writer, StructuredCashFlowScheduleEntry value,
        JsonSerializerOptions options)
    {
        if (value.RowVersion == 0)
            JsonSerializer.Serialize(writer, SecurityCashFlowScheduleRowCodec.ToLegacyRow(value),
                SecurityCashFlowReceiptJsonContext.Default.LegacyStructuredCashFlowScheduleEntry);
        else if (value.RowVersion == 1)
            JsonSerializer.Serialize(writer, SecurityCashFlowScheduleRowCodec.ToVersionOneRow(value),
                SecurityCashFlowReceiptJsonContext.Default.VersionOneStructuredCashFlowScheduleEntry);
        else
            JsonSerializer.Serialize(writer, value, CurrentSecurityCashFlowRowJsonContext.Default.StructuredCashFlowScheduleEntry);
    }
}

/// <summary>The immutable wire contract used by context-model receipts v1 through v3.</summary>
internal sealed record LegacyStructuredCashFlowScheduleEntry(
    DateTimeOffset PeriodDate,
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal Factor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? UnadjustedStart,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? UnadjustedEnd,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AccrualStart,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AccrualEnd,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LegacyBondFloatingRateEvidence? RateEvidence);

/// <summary>The immutable row wire contract used by context-model receipts v4.</summary>
internal sealed record VersionOneStructuredCashFlowScheduleEntry(
    DateTimeOffset PeriodDate,
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal Factor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? UnadjustedStart,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? UnadjustedEnd,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AccrualStart,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AccrualEnd,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LegacyBondFloatingRateEvidence? RateEvidence,
    int RowVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? InterestMeasuredFrom,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? InterestMeasuredTo);

/// <summary>The frozen nested rate-evidence wire shape in legacy schedule rows.</summary>
internal sealed record LegacyBondFloatingRateEvidence(
    DateOnly ResetDate,
    bool IsObserved,
    string IndexName,
    decimal IndexRate,
    decimal Spread,
    decimal? Cap,
    decimal? Floor,
    decimal Principal,
    decimal AccrualFraction,
    SecurityCouponRateUnit OriginalRateUnit,
    string Source);
