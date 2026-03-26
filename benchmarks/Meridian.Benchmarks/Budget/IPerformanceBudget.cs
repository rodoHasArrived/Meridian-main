namespace Meridian.Benchmarks;

/// <summary>
/// Exposes the allocation budget for a single hot-path stage, expressed as
/// bytes allocated per event on the GC-tracked heap.
/// </summary>
public interface IPerformanceBudget
{
    /// <summary>Human-readable name of the stage under test.</summary>
    string StageName { get; }

    /// <summary>
    /// Maximum allowed heap allocation per event in bytes.
    /// Includes only GC-tracked (managed) allocations; stack and ArrayPool
    /// buffers do not count.
    /// </summary>
    long MaxAllocatedBytesPerEvent { get; }

    /// <summary>
    /// Maximum allowed mean execution time per event in nanoseconds.
    /// Use <see cref="long.MaxValue"/> to disable the latency gate.
    /// </summary>
    long MaxMeanNanosPerEvent { get; }

    /// <summary>
    /// SIMD path — excluded from CI gate; requires AVX2 at runtime.
    /// When <c>true</c>, <c>validate_budget.py</c> skips this entry.
    /// </summary>
    bool RequiresSimd { get; }
}
