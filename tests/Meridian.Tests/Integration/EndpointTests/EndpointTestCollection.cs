using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Serialization boundary for endpoint tests that mutate process-wide environment variables.
/// Each test class owns its own <see cref="EndpointTestFixture"/> so authentication, configuration,
/// and in-memory application state cannot leak between classes.
/// </summary>
[CollectionDefinition("Endpoint", DisableParallelization = true)]
public sealed class EndpointTestCollection
{
}
