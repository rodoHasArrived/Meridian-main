using System.Text.Json;
using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Regression coverage for the shared endpoint JSON options used by the dashboard host.
/// </summary>
public sealed class UiEndpointsJsonOptionsTests
{
    [Fact]
    public void CreateEndpointJsonOptions_DefaultsToCamelCaseAndConfiguresResolver()
    {
        var options = UiEndpoints.CreateEndpointJsonOptions();

        options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        options.WriteIndented.Should().BeFalse();
        options.TypeInfoResolver.Should().NotBeNull();
    }

    [Fact]
    public void CreateEndpointJsonOptions_IndentedVariant_PreservesResolver()
    {
        var options = UiEndpoints.CreateEndpointJsonOptions(writeIndented: true);

        options.WriteIndented.Should().BeTrue();
        options.TypeInfoResolver.Should().NotBeNull();
    }
}
