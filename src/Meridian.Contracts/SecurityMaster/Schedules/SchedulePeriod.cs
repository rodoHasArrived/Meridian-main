using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>The shape of a schedule period relative to the regular grid.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SchedulePeriodKind>))]
public enum SchedulePeriodKind
{
    /// <summary>A full regular period on the grid.</summary>
    Regular,

    /// <summary>The single period of a term tenor.</summary>
    Term,

    /// <summary>An initial period shorter than a regular period.</summary>
    ShortInitialStub,

    /// <summary>An initial period longer than a regular period.</summary>
    LongInitialStub,

    /// <summary>A final period shorter than a regular period.</summary>
    ShortFinalStub,

    /// <summary>A final period longer than a regular period.</summary>
    LongFinalStub,
}

/// <summary>
/// One period of a contractual schedule: its unadjusted dates (its identity and its ICMA reference
/// frame), its adjusted accrual dates, its payment date, and its notional reference periods.
/// </summary>
/// <param name="Sequence">The 1-based position of the period in the schedule.</param>
/// <param name="UnadjustedStart">The unadjusted period start, from the generation grid.</param>
/// <param name="UnadjustedEnd">The unadjusted period end, from the generation grid.</param>
/// <param name="AccrualStart">The business-day adjusted accrual start.</param>
/// <param name="AccrualEnd">The business-day adjusted accrual end.</param>
/// <param name="PaymentDate">The payment date: the adjusted accrual end under the payment adjustment, or the payment lag from it.</param>
/// <param name="Kind">Whether the period is regular, a term period, or a stub.</param>
/// <param name="ReferencePeriods">
/// The unadjusted notional periods the period is measured against, always taken from the generation
/// grid. A regular or term period references itself; a stub references the regular period(s) it
/// would otherwise have been. A notional date beyond the supported date range is omitted, so such a
/// stub's references do not cover it and Actual/Actual (ICMA) refuses it.
/// </param>
/// <param name="IsFinal">Whether this is the last period of the schedule.</param>
public sealed record SchedulePeriod(
    int Sequence,
    DateOnly UnadjustedStart,
    DateOnly UnadjustedEnd,
    DateOnly AccrualStart,
    DateOnly AccrualEnd,
    DateOnly PaymentDate,
    SchedulePeriodKind Kind,
    IReadOnlyList<DayCountReferencePeriod> ReferencePeriods,
    bool IsFinal)
{
    /// <summary>Whether the period is an initial or final stub, short or long.</summary>
    public bool IsStub => Kind is SchedulePeriodKind.ShortInitialStub
        or SchedulePeriodKind.LongInitialStub
        or SchedulePeriodKind.ShortFinalStub
        or SchedulePeriodKind.LongFinalStub;

    /// <summary>
    /// Value equality over every member, comparing <see cref="ReferencePeriods"/> element by element.
    /// The generated record equality would compare the list by reference, so the same period from two
    /// generations of the same spec would be unequal; schedules are regenerated rather than stored, so
    /// a period must equal its regenerated twin.
    /// </summary>
    /// <param name="other">The period to compare with.</param>
    /// <returns><see langword="true"/> when every member, including each reference period, is equal.</returns>
    public bool Equals(SchedulePeriod? other)
        => other is not null
            && Sequence == other.Sequence
            && UnadjustedStart == other.UnadjustedStart
            && UnadjustedEnd == other.UnadjustedEnd
            && AccrualStart == other.AccrualStart
            && AccrualEnd == other.AccrualEnd
            && PaymentDate == other.PaymentDate
            && Kind == other.Kind
            && IsFinal == other.IsFinal
            && ReferencePeriods.SequenceEqual(other.ReferencePeriods);

    /// <summary>A hash consistent with <see cref="Equals(SchedulePeriod?)"/>.</summary>
    /// <returns>The hash of every member, folding in each reference period.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Sequence);
        hash.Add(UnadjustedStart);
        hash.Add(UnadjustedEnd);
        hash.Add(AccrualStart);
        hash.Add(AccrualEnd);
        hash.Add(PaymentDate);
        hash.Add(Kind);
        hash.Add(IsFinal);
        foreach (var reference in ReferencePeriods)
        {
            hash.Add(reference);
        }

        return hash.ToHashCode();
    }
}
