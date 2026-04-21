using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ContractsSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterIngestStatusEndpointsTests
{
    [Fact]
    public async Task MapSecurityMasterEndpoints_IngestStatusRoute_ReturnsTypedPayload()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        conflictService.GetOpenConflictsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(
                new[]
                {
                    new SecurityMasterConflict(Guid.NewGuid(), Guid.NewGuid(), "Identifier", "identifiers[0]", "A", "US0001", "B", "US0002", DateTimeOffset.UtcNow.AddMinutes(-5), "Open"),
                    new SecurityMasterConflict(Guid.NewGuid(), Guid.NewGuid(), "Identifier", "identifiers[1]", "A", "US0003", "B", "US0004", DateTimeOffset.UtcNow.AddMinutes(-4), "Open")
                }));
        ingestStatusService.GetSnapshot()
            .Returns(new SecurityMasterIngestStatusSnapshot(
                ActiveImport: new SecurityMasterActiveImportStatus(
                    FileExtension: ".csv",
                    Total: 12,
                    Processed: 5,
                    Imported: 4,
                    Skipped: 1,
                    Failed: 0,
                    StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                    UpdatedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-10)),
                LastCompleted: new SecurityMasterCompletedImportStatus(
                    FileExtension: ".json",
                    Total: 8,
                    Processed: 8,
                    Imported: 6,
                    Skipped: 1,
                    Failed: 1,
                    ConflictsDetected: 3,
                    ErrorCount: 1,
                    StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-15),
                    CompletedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-13))));

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.SecurityMasterIngestStatus);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<SecurityMasterIngestStatusResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload.Should().NotBeNull();
        payload!.OpenConflicts.Should().Be(2);
        payload.IsImportActive.Should().BeTrue();
        payload.ActiveImport.Should().NotBeNull();
        payload.ActiveImport!.Processed.Should().Be(5);
        payload.ActiveImport.Imported.Should().Be(4);
        payload.ActiveImport.Skipped.Should().Be(1);
        payload.LastCompleted.Should().NotBeNull();
        payload.LastCompleted!.FileExtension.Should().Be(".json");
        payload.LastCompleted.ConflictsDetected.Should().Be(3);
        payload.LastCompleted.ErrorCount.Should().Be(1);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ContractsSecurityMasterQueryService queryService,
        ISecurityMasterConflictService conflictService,
        ISecurityMasterIngestStatusService ingestStatusService,
        ISecurityMasterService commandService,
        ISecurityMasterImportService importService,
        ISecurityMasterEventStore eventStore)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(conflictService);
        builder.Services.AddSingleton(ingestStatusService);
        builder.Services.AddSingleton(commandService);
        builder.Services.AddSingleton(importService);
        builder.Services.AddSingleton(eventStore);
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());

        var app = builder.Build();
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }
}
