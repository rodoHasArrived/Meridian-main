using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class EndpointHelpersTests
{
    [Fact]
    public async Task HandleSync_WhenUnexpectedExceptionOccurs_ReturnsStructuredEnvelopeWithoutRawMessage()
    {
        const string rawMessage = "provider secret was sk-proj-test-value";
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var result = EndpointHelpers.HandleSync(
            () => throw new Exception(rawMessage),
            jsonOptions);

        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Response.Body = responseBody;

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();

        json.Should().NotContain(rawMessage);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("error").GetString().Should().Be("An error occurred");
        root.GetProperty("message").GetString().Should().Be("An error occurred");
        root.GetProperty("code").GetString().Should().Be("Meridian-GEN-001");
    }
}
