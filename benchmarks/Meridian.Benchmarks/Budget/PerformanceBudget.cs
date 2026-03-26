namespace Meridian.Benchmarks;

/// <summary>
/// Immutable performance budget for a single hot-path stage.
/// </summary>
/// <param name="StageName">Human-readable name used as the lookup key by <c>validate_budget.py</c>.</param>
/// <param name="MaxAllocatedBytesPerEvent">Maximum managed heap bytes per event (0 = zero-alloc required).</param>
/// <param name="MaxMeanNanosPerEvent">Maximum mean execution time in nanoseconds (<see cref="long.MaxValue"/> = no gate).</param>
/// <param name="RequiresSimd">When <c>true</c> the CI script skips this entry because AVX2 is not guaranteed on <c>ubuntu-latest</c>.</param>
public sealed record PerformanceBudget(
    string StageName,
    long MaxAllocatedBytesPerEvent,
    long MaxMeanNanosPerEvent,
    bool RequiresSimd = false) : IPerformanceBudget;
