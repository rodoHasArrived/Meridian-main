using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Serialization boundary for tests that deliberately exercise process-startup configuration or
/// legacy static provider bindings. Ordinary endpoint fixtures own their settings and services
/// and use xUnit's per-class collections so independent hosts can run concurrently.
/// </summary>
[CollectionDefinition("Endpoint", DisableParallelization = true)]
public sealed class EndpointTestCollection
{
}
