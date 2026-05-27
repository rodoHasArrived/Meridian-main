using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Regression tests for endpoint metadata consistency.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class EndpointMetadataTests
{
    private readonly EndpointTestFixture _fixture;

    public EndpointMetadataTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void EndpointNames_AreGloballyUnique()
    {
        var duplicateNames = _fixture.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        duplicateNames.Should().BeEmpty("all endpoint names must be unique, but found duplicates: {0}", string.Join(", ", duplicateNames));
    }
}
