using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Collection definition that disables parallelization for tests that modify
/// process-wide state (e.g., environment variables) and would otherwise interfere
/// with concurrently running tests.
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
public sealed class SequentialCollection { }

/// <summary>
/// Collection for allocation-sensitive performance tests that use
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> to measure per-event heap usage.
/// <para>
/// Disabling parallelization prevents background test threads from allocating on the
/// measured thread via shared object pools or GC cross-thread triggers, which would
/// inflate allocation measurements.
/// </para>
/// <para>
/// Tag individual tests with <c>[Trait("Category", "Performance")]</c> to allow
/// targeted execution: <c>dotnet test --filter "Category=Performance"</c>.
/// </para>
/// </summary>
[CollectionDefinition("PerformanceSolo", DisableParallelization = true)]
public sealed class PerformanceSoloCollection { }
