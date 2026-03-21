using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Collection definition that shares a single EndpointTestFixture instance
/// across all endpoint test classes. Without this, each class using
/// IClassFixture&lt;EndpointTestFixture&gt; creates its own WebApplication TestServer,
/// which is expensive. Using a shared collection fixture reduces the number of
/// WebApplication instances from 16+ down to 1.
/// </summary>
[CollectionDefinition("Endpoint")]
public sealed class EndpointTestCollection : ICollectionFixture<EndpointTestFixture>
{
}
