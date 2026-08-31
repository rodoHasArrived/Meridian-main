using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ArchiveMaintenanceEndpointsTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-archive-endpoint-tests",
        Guid.NewGuid().ToString("N"));

    public ArchiveMaintenanceEndpointsTests()
    {
        Directory.CreateDirectory(_dataRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task Update_InvalidCron_ReturnsBadRequestWithoutMutatingRetainedSchedule()
    {
        var manager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            _dataRoot);
        var created = await manager.CreateScheduleAsync(new ArchiveMaintenanceSchedule
        {
            Name = "Original archive schedule",
            Description = "Retained description",
            CronExpression = "0 3 * * *"
        });
        await using var app = await CreateAppAsync(manager);

        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/maintenance/schedules/{created.ScheduleId}",
            new UpdateMaintenanceScheduleRequest(
                Name: "Partially applied name",
                Description: "Partially applied description",
                CronExpression: "not-a-cron-expression"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var retained = manager.GetSchedule(created.ScheduleId)!;
        retained.Name.Should().Be("Original archive schedule");
        retained.Description.Should().Be("Retained description");
        retained.CronExpression.Should().Be("0 3 * * *");
    }

    [Fact]
    public async Task Presets_MonthlyCompression_UsesExplicitFirstSundayCron()
    {
        var manager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            _dataRoot);
        await using var app = await CreateAppAsync(manager);

        using var response = await app.GetTestClient().GetAsync("/api/maintenance/presets");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var monthly = document.RootElement
            .EnumerateArray()
            .Single(element => element.GetProperty("name").GetString() == "monthly-compression");
        monthly.GetProperty("cronExpression").GetString().Should().Be("0 1 * * 0#1");
    }

    [Fact]
    public async Task Trigger_WhenScheduleAlreadyQueued_ReturnsConflict()
    {
        var manager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            _dataRoot);
        var schedule = await manager.CreateScheduleAsync(new ArchiveMaintenanceSchedule
        {
            Name = "Archive schedule",
            CronExpression = "0 3 * * *"
        });
        var service = new ScheduledArchiveMaintenanceService(
            NullLogger<ScheduledArchiveMaintenanceService>.Instance,
            manager,
            Mock.Of<IFileMaintenanceService>(),
            Mock.Of<ITierMigrationService>(),
            new StorageOptions { RootPath = _dataRoot });
        await using var app = await CreateAppAsync(manager, service);
        var client = app.GetTestClient();

        using var first = await client.PostAsync(
            $"/api/maintenance/schedules/{schedule.ScheduleId}/trigger",
            content: null);
        using var duplicate = await client.PostAsync(
            $"/api/maintenance/schedules/{schedule.ScheduleId}/trigger",
            content: null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CompatibilityRun_WhenScheduleAlreadyQueued_ReturnsConflict()
    {
        var manager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            _dataRoot);
        var schedule = await manager.CreateScheduleAsync(new ArchiveMaintenanceSchedule
        {
            Name = "Compatibility archive schedule",
            CronExpression = "0 3 * * *"
        });
        await using var app = await CreateAppAsync(manager);
        var client = app.GetTestClient();

        using var first = await client.PostAsync(
            $"/api/maintenance/schedules/{schedule.ScheduleId}/run",
            content: null);
        using var duplicate = await client.PostAsync(
            $"/api/maintenance/schedules/{schedule.ScheduleId}/run",
            content: null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<WebApplication> CreateAppAsync(
        ArchiveMaintenanceScheduleManager manager,
        ScheduledArchiveMaintenanceService? service = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("archive-maintenance-test"));
        });
        builder.Services.AddSingleton(manager);
        service ??= new ScheduledArchiveMaintenanceService(
            NullLogger<ScheduledArchiveMaintenanceService>.Instance,
            manager,
            Mock.Of<IFileMaintenanceService>(),
            Mock.Of<ITierMigrationService>(),
            new StorageOptions { RootPath = _dataRoot });
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.UseRateLimiter();
        // The maintenance routes require AdminMaintenance (W9-GOV-008). This host composes the
        // endpoints directly without the session middleware, so stamp the permissions snapshot
        // the authorization filter reads, exactly as the shared endpoint fixture does.
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.AdminMaintenance;
            await next();
        });
        app.MapArchiveMaintenanceEndpoints();
        app.MapMaintenanceScheduleEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await app.StartAsync();
        return app;
    }
}
