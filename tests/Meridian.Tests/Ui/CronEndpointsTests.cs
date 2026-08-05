using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class CronEndpointsTests
{
    [Fact]
    public async Task NextRuns_EveryMinute_ReturnsAdjacentOccurrences()
    {
        await using var app = await CreateAppAsync();

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.SchedulesCronNextRuns,
            new
            {
                expression = "* * * * *",
                count = 4,
                timeZoneId = "UTC"
            });

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var nextRuns = document.RootElement
            .GetProperty("nextRuns")
            .EnumerateArray()
            .Select(static value => value.GetDateTimeOffset())
            .ToArray();

        nextRuns.Should().HaveCount(4);
        nextRuns.Zip(nextRuns.Skip(1), static (current, next) => next - current)
            .Should()
            .OnlyContain(interval => interval == TimeSpan.FromMinutes(1));
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.MapCronEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
