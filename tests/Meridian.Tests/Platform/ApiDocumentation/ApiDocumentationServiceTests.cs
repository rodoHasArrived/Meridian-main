using FluentAssertions;
using Meridian.Platform.ApiDocumentation;

namespace Meridian.Tests.Platform.ApiDocumentation;

public sealed class ApiDocumentationServiceTests
{
    [Fact]
    public void GenerateOpenApiSpec_IncludesCoreMetadataAndHistoricalPath()
    {
        var service = new ApiDocumentationService();

        var spec = service.GenerateOpenApiSpec();

        spec.OpenApi.Should().Be("3.0.3");
        spec.Info.Should().NotBeNull();
        spec.Info!.Title.Should().Be("Meridian API");
        spec.Paths.Should().ContainKey("/api/historical/query");
    }
}
