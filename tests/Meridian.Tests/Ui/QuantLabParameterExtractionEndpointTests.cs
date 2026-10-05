using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.QuantScript.Compilation;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class QuantLabParameterExtractionEndpointTests
{
    [Fact]
    public async Task Parameters_InferredCall_ReturnsCompleteMetadata()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/api/quant/parameters",
            new QuantParametersRequest("var lookback = Param(\"lookback\", 20);"));
        var body = await response.Content.ReadFromJsonAsync<QuantParametersResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Parameters.Should().ContainSingle().Which.Should().Be(
            new QuantParameterDto("lookback", "lookback", "int", "20", double.MinValue, double.MaxValue, null));
    }

    [Theory]
    [InlineData("var name = \"unknown\"; var unsupported = Param(name, 20);")]
    [InlineData("var fallback = 20; var unsupported = Param(\"unknown\", fallback);")]
    [InlineData("var unsupported = Param<DateTime>(\"unknown\", default);")]
    public async Task Parameters_MixedSupportedAndUnsupportedCalls_Returns400WithoutPartialDescriptors(string unsupported)
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        var source = "var supported = Param<int>(\"supported\", 10);\n" + unsupported;

        using var response = await client.PostAsJsonAsync("/api/quant/parameters", new QuantParametersRequest(source));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.RootElement.GetProperty("error").GetString().Should().Contain("extraction is incomplete");
        body.RootElement.TryGetProperty("parameters", out _).Should().BeFalse();
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IQuantScriptCompiler>(
            new RoslynScriptCompiler(NullLogger<RoslynScriptCompiler>.Instance));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "strategy-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageStrategies;
            await next(context);
        });
        app.MapQuantLabEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
