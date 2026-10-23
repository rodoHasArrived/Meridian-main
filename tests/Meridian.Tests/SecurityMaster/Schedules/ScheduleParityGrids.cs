namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// The deterministic case grids the kernel parity and oracle fidelity tests share. Every grid is a
/// plain nested loop over fixed dates and offsets, so a run always visits the same cases in the same
/// order and no clock is read.
/// </summary>
internal static class ScheduleParityGrids
{
    /// <summary>The first issue (or start) date of the forward grids.</summary>
    public static readonly DateOnly ForwardStartFrom = new(2023, 11, 1);

    /// <summary>The last issue (or start) date of the forward grids, inclusive.</summary>
    public static readonly DateOnly ForwardStartTo = new(2025, 3, 31);

    /// <summary>Every period length the Security Master call sites can derive: <c>Math.Max(1, 12 / paymentsPerYear)</c>.</summary>
    public static readonly int[] PeriodMonths = [1, 2, 3, 4, 6, 12];

    /// <summary>Whole months from start to maturity, before <see cref="MaturityDayOffsets"/> is applied.</summary>
    public static readonly int[] MaturityMonthOffsets = [0, 1, 2, 3, 5, 6, 7, 11, 12, 13, 24, 25, 60, 121];

    /// <summary>Days added to the whole-month maturity: before, on and after the grid, and mid-period.</summary>
    public static readonly int[] MaturityDayOffsets = [-1, 0, 1, 15];

    /// <summary>The number of start dates from <see cref="ForwardStartFrom"/> to <see cref="ForwardStartTo"/>.</summary>
    public const int ForwardStartCount = 517;

    /// <summary>The number of forward schedules: 517 start dates × 6 period lengths × 14 × 4 maturities.</summary>
    public const int ForwardScheduleCount = 173_712;

    /// <summary>The first maturity of the FaceValueLot grids.</summary>
    public static readonly DateOnly MaturityFrom = new(2024, 1, 1);

    /// <summary>The last maturity of the FaceValueLot grids, inclusive.</summary>
    public static readonly DateOnly MaturityTo = new(2026, 12, 31);

    /// <summary>The coupon frequencies FaceValueLot's constant-yield schedules accept.</summary>
    public static readonly int[] PaymentsPerYear = [1, 2, 4, 12];

    /// <summary>Whole months from acquisition to maturity, before <see cref="AcquisitionDayOffsets"/> is applied.</summary>
    public static readonly int[] AcquisitionMonthOffsets = [0, 1, 2, 3, 5, 7, 13, 25];

    /// <summary>Days added to the whole-month acquisition date.</summary>
    public static readonly int[] AcquisitionDayOffsets = [-1, 0, 1, 9];

    /// <summary>
    /// The number of FaceValueLot cases: 1,096 maturities × 4 frequencies × 8 × 4 acquisitions, less
    /// the 13,152 acquisitions on or after maturity.
    /// </summary>
    public const int FaceValueLotCaseCount = 127_136;

    /// <summary>The number of cases the fidelity tests draw from each grid.</summary>
    public const int FidelitySampleSize = 5_000;

    /// <summary>
    /// Every forward schedule: each start date from <see cref="ForwardStartFrom"/> to
    /// <see cref="ForwardStartTo"/> × <see cref="PeriodMonths"/> × a maturity of
    /// <c>start.AddMonths(k).AddDays(d)</c> for k in <see cref="MaturityMonthOffsets"/> and d in
    /// <see cref="MaturityDayOffsets"/>.
    /// </summary>
    public static IEnumerable<ForwardCase> ForwardCases()
    {
        for (var start = ForwardStartFrom; start <= ForwardStartTo; start = start.AddDays(1))
        {
            foreach (var periodMonths in PeriodMonths)
            {
                foreach (var maturity in Maturities(start))
                {
                    yield return new ForwardCase(start, maturity, periodMonths);
                }
            }
        }
    }

    /// <summary>The maturities of the forward grid for one start date.</summary>
    public static IEnumerable<DateOnly> Maturities(DateOnly start)
    {
        foreach (var months in MaturityMonthOffsets)
        {
            foreach (var days in MaturityDayOffsets)
            {
                yield return start.AddMonths(months).AddDays(days);
            }
        }
    }

    /// <summary>
    /// Every FaceValueLot case: each maturity from <see cref="MaturityFrom"/> to
    /// <see cref="MaturityTo"/> × <see cref="PaymentsPerYear"/> × an acquisition of
    /// <c>maturity.AddMonths(−j).AddDays(d)</c> for j in <see cref="AcquisitionMonthOffsets"/> and d in
    /// <see cref="AcquisitionDayOffsets"/>, keeping only acquisitions before maturity.
    /// </summary>
    public static IEnumerable<FaceValueLotCase> FaceValueLotCases()
    {
        for (var maturity = MaturityFrom; maturity <= MaturityTo; maturity = maturity.AddDays(1))
        {
            foreach (var paymentsPerYear in PaymentsPerYear)
            {
                foreach (var months in AcquisitionMonthOffsets)
                {
                    foreach (var days in AcquisitionDayOffsets)
                    {
                        var acquired = maturity.AddMonths(-months).AddDays(days);
                        if (acquired < maturity)
                        {
                            yield return new FaceValueLotCase(acquired, maturity, paymentsPerYear);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A deterministic sample of <paramref name="size"/> cases: every <paramref name="stride"/>-th case
    /// from the start of <paramref name="grid"/>. A prime stride walks every combination of the grid's
    /// inner offsets instead of repeating a subset of them.
    /// </summary>
    public static IReadOnlyList<T> Sample<T>(IEnumerable<T> grid, int stride, int size = FidelitySampleSize)
        => grid.Where((_, index) => index % stride == 0).Take(size).ToArray();

    /// <summary>One forward schedule: a start (issue or as-of) date, a maturity and a period length.</summary>
    public readonly record struct ForwardCase(DateOnly Start, DateOnly Maturity, int PeriodMonths);

    /// <summary>One FaceValueLot schedule: an acquisition date, a maturity and a coupon frequency.</summary>
    public readonly record struct FaceValueLotCase(DateOnly AcquiredDate, DateOnly Maturity, int PaymentsPerYear);
}
