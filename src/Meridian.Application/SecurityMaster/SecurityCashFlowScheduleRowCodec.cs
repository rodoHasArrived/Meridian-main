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
    public const int CurrentVersion = 1;

    public static StructuredCashFlowProjectionDto Current(StructuredCashFlowProjectionDto projection)
        => MapRows(projection, static row => row with { RowVersion = CurrentVersion });

    public static StructuredCashFlowProjectionDto Legacy(StructuredCashFlowProjectionDto projection)
        => MapRows(projection, static row =>
        {
            // This is a frozen v0 codec, deliberately separate from the current record. Future
            // additions belong to a new row contract; the legacy codec must never grow with it.
            var legacy = ToLegacyRow(row);
            return JsonSerializer.Deserialize(
                JsonSerializer.Serialize(legacy, SecurityCashFlowReceiptJsonContext.Default.LegacyStructuredCashFlowScheduleEntry),
                SecurityCashFlowReceiptJsonContext.Default.StructuredCashFlowScheduleEntry)!;
        });

    internal static LegacyStructuredCashFlowScheduleEntry ToLegacyRow(StructuredCashFlowScheduleEntry row)
        => new(row.PeriodDate, row.PrincipalAmount, row.InterestAmount, row.Factor,
            row.UnadjustedStart, row.UnadjustedEnd, row.AccrualStart, row.AccrualEnd,
            row.RateEvidence is { } rate ? new LegacyBondFloatingRateEvidence(rate.ResetDate, rate.IsObserved,
                rate.IndexName, rate.IndexRate, rate.Spread, rate.Cap, rate.Floor, rate.Principal,
                rate.AccrualFraction, rate.OriginalRateUnit, rate.Source) : null);

    public static bool HasSupportedRows(StructuredCashFlowProjectionDto projection, bool current)
        => Rows(projection).All(row => current
            ? row.RowVersion == CurrentVersion
            : row.RowVersion == 0 && row.InterestMeasuredFrom is null && row.InterestMeasuredTo is null);

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
