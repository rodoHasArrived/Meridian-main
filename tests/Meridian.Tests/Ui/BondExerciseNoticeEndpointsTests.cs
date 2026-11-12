using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Tests.FixedIncome;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondExerciseNoticeEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ExerciseNotices_ServesTheOpenCallWindow()
    {
        var security = BondExerciseNoticeWatchTests.Bond([new Dictionary<string, object?>
        {
            ["callDate"] = "2027-01-15", ["callPrice"] = 101m, ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar", ["minimumNoticeDays"] = 30, ["maximumNoticeDays"] = 60,
            ["noticeDayType"] = "Calendar"
        }]);
        await using var app = await CreateAppAsync(security);

        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{security.SecurityId}/exercise-notices?asOf=2026-12-01T00:00:00Z&horizonDays=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var window = Assert.Single(document.RootElement.GetProperty("windows").EnumerateArray().ToArray());
        Assert.Equal("Open", window.GetProperty("status").GetString());
        Assert.Equal("2026-12-16", window.GetProperty("latestNoticeDate").GetString());
        Assert.Equal(15, window.GetProperty("daysToDeadline").GetInt32());
        var result = (await response.Content.ReadFromJsonAsync<BondExerciseNoticeWatchResult>(Json))!;
        Assert.False(result.IsBlocked);
        Assert.Equal(30, result.HorizonDays);
    }

    [Theory]
    [InlineData("asOf=2026-12-01T00:00:00%2B02:00")]
    [InlineData("asOf=2026-12-01T00:00:00Z&knownAt=2026-11-01T00:00:00%2B01:00")]
    [InlineData("asOf=2026-12-01T00:00:00Z&horizonDays=0")]
    [InlineData("asOf=2026-12-01T00:00:00Z&horizonDays=367")]
    [InlineData("horizonDays=30")]
    public async Task ExerciseNotices_RejectsInvalidInputs(string query)
    {
        await using var app = await CreateAppAsync(BondExerciseNoticeWatchTests.Bond());
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{Guid.NewGuid()}/exercise-notices?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExerciseNotices_UnknownSecurity_IsNotFound()
    {
        await using var app = await CreateAppAsync(BondExerciseNoticeWatchTests.Bond());
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{Guid.NewGuid()}/exercise-notices?asOf=2026-12-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExerciseNotices_RefusesCallerWithoutSecurityMasterPermission()
    {
        var security = BondExerciseNoticeWatchTests.Bond();
        await using var app = await CreateAppAsync(security, UserPermission.ViewMarketData);
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{security.SecurityId}/exercise-notices?asOf=2026-12-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<WebApplication> CreateAppAsync(SecurityDetailDto security,
        UserPermission permissions = UserPermission.ViewSecurityMaster)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<IBondAnalyticsService>());
        builder.Services.AddSingleton(new BondExerciseNoticeReadService(query,
            new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly])));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "bond-test-tenant";
            await next();
        });
        app.MapBondAnalyticsEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }
}
