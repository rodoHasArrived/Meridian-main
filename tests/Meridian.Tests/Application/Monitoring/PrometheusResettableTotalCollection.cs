using Xunit;

namespace Meridian.Tests.Application.Monitoring;

/// <summary>
/// Serializes the test classes that mutate the process-wide <c>Meridian.Platform.Tracing.Metrics</c>
/// snapshot or the prometheus-net default registry.
///
/// <para>Both are static and shared across the whole assembly. <c>PlatformMetrics.Reset()</c> and
/// <c>PrometheusMetrics.UpdateFromSnapshot()</c> are only meaningful when read back in the same
/// sequence they were written, so a class asserting counter deltas cannot run concurrently with
/// another that publishes events or resets the snapshot underneath it.</para>
/// </summary>
[CollectionDefinition("PrometheusResettableTotals", DisableParallelization = true)]
public sealed class PrometheusResettableTotalCollection
{
}
