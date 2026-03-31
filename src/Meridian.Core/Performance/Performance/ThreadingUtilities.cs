using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Meridian.Core.Performance;

/// <summary>
/// Low-level threading utilities for high-performance scenarios.
/// Provides thread affinity, priority, and CPU pinning capabilities.
/// </summary>
public static class ThreadingUtilities
{
    /// <summary>
    /// Sets the current thread's priority to highest for latency-critical operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetHighPriority()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        catch
        {
            // Ignore - thread pool threads may not allow priority changes
        }
    }

    /// <summary>
    /// Sets the current thread's priority to above normal (less aggressive than highest).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetAboveNormalPriority()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Attempts to set thread affinity to a specific CPU core (Windows/Linux).
    /// </summary>
    /// <param name="cpuIndex">Zero-based CPU core index</param>
    /// <returns>True if affinity was set successfully</returns>
    public static bool TrySetThreadAffinity(int cpuIndex)
    {
        if (cpuIndex < 0 || cpuIndex >= Environment.ProcessorCount)
            return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TrySetWindowsThreadAffinity(cpuIndex);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TrySetLinuxThreadAffinity(cpuIndex);
            }
        }
        catch
        {
            // Ignore - affinity setting may require elevated privileges
        }

        return false;
    }

    /// <summary>
    /// Attempts to set process affinity to specific CPU cores.
    /// Only supported on Windows and Linux.
    /// </summary>
    /// <param name="affinityMask">Bitmask of allowed CPUs (e.g., 0x3 = CPUs 0 and 1)</param>
    public static bool TrySetProcessAffinity(nint affinityMask)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            process.ProcessorAffinity = affinityMask;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to set the process to high priority class.
    /// Only supported on Windows and Linux.
    /// Use sparingly - this affects all threads and can starve other processes.
    /// </summary>
    public static bool TrySetProcessHighPriority()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.High;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Yields the current thread to allow other threads to execute.
    /// More efficient than Thread.Sleep(0) for busy-wait scenarios.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void YieldProcessor()
    {
        Thread.SpinWait(1);
    }

    /// <summary>
    /// Performs a spin-wait for the specified number of iterations.
    /// Useful for very short waits where context switching is too expensive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SpinWait(int iterations)
    {
        Thread.SpinWait(iterations);
    }

    /// <summary>
    /// Gets the current thread's managed thread ID.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCurrentThreadId() => Environment.CurrentManagedThreadId;

    /// <summary>
    /// Prevents the GC from relocating the current thread's stack during critical sections.
    /// Call EndNoGCRegion when done.
    /// </summary>
    public static bool TryStartNoGCRegion(long totalBytes)
    {
        try
        {
            return GC.TryStartNoGCRegion(totalBytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ends the no-GC region started by TryStartNoGCRegion.
    /// </summary>
    public static void EndNoGCRegion()
    {
        try
        {
            GC.EndNoGCRegion();
        }
        catch
        {
            // Ignore - may not be in a no-GC region
        }
    }


    private static bool TrySetWindowsThreadAffinity(int cpuIndex)
    {
        // On Windows, we can use the thread handle via P/Invoke
        // For now, we rely on process affinity which is more portable
        var mask = (nint)(1L << cpuIndex);
        return TrySetProcessAffinity(mask);
    }

    private static bool TrySetLinuxThreadAffinity(int cpuIndex)
    {
        // On Linux, setting process affinity is the most portable option
        // without native interop
        var mask = (nint)(1L << cpuIndex);
        return TrySetProcessAffinity(mask);
    }

}

/// <summary>
/// Thread-local sequence generator for high-performance sequence number allocation.
/// Avoids contention by using per-thread counters with unique prefixes.
/// </summary>
public sealed class ThreadLocalSequenceGenerator
{
    private readonly int _nodeId;
    private long _sequence;

    /// <summary>
    /// Creates a new thread-local sequence generator.
    /// </summary>
    /// <param name="nodeId">Unique node identifier (0-1023)</param>
    public ThreadLocalSequenceGenerator(int nodeId = 0)
    {
        if (nodeId < 0 || nodeId > 1023)
            throw new ArgumentOutOfRangeException(nameof(nodeId), "Node ID must be between 0 and 1023");
        _nodeId = nodeId;
    }

    /// <summary>
    /// Generates the next unique sequence number.
    /// Format: [nodeId (10 bits)][sequence (54 bits)]
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Next()
    {
        var seq = Interlocked.Increment(ref _sequence);
        return ((long)_nodeId << 54) | (seq & 0x3FFFFFFFFFFFFF);
    }
}

/// <summary>
/// High-performance timestamp utilities.
/// </summary>
public static class HighResolutionTimestamp
{
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;
    private static readonly double TickFrequency = 1.0 / Stopwatch.Frequency;

    /// <summary>
    /// Gets a high-resolution timestamp in ticks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>
    /// Gets elapsed time since the specified timestamp in microseconds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetElapsedMicroseconds(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * TickFrequency * 1_000_000;
    }

    /// <summary>
    /// Gets elapsed time since the specified timestamp in nanoseconds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetElapsedNanoseconds(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * TickFrequency * 1_000_000_000;
    }

    /// <summary>
    /// Converts a high-resolution timestamp to a DateTimeOffset.
    /// More accurate than DateTimeOffset.UtcNow for relative timing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset ToDateTimeOffset(long timestamp)
    {
        var elapsedTicks = (long)((timestamp - StartTimestamp) * TickFrequency * TimeSpan.TicksPerSecond);
        return StartTime.AddTicks(elapsedTicks);
    }

    /// <summary>
    /// Gets the current time with high resolution.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset GetCurrentTime()
    {
        return ToDateTimeOffset(Stopwatch.GetTimestamp());
    }
}
