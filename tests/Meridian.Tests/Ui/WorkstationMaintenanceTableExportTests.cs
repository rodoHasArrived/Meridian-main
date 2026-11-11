using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Maintenance;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class WorkstationMaintenanceTableExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-maintenance-export", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Export_ReadsPastBrowserCapAndAppliesActiveSourceFilter()
    {
        var history = new MaintenanceExecutionHistory(_root);
        for (var index = 0; index < 40; index++)
            await history.RecordExecutionAsync(new MaintenanceExecution
            {
                ExecutionId = $"retained-{index}", TaskType = MaintenanceTaskType.HealthCheck,
                Status = index % 10 == 0 ? MaintenanceExecutionStatus.Failed : MaintenanceExecutionStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-index), FilesProcessed = index,
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-index).AddSeconds(1)
            });
        var manager = new ArchiveMaintenanceScheduleManager(NullLogger<ArchiveMaintenanceScheduleManager>.Instance, _root, history);
        using var services = new ServiceCollection().AddSingleton(manager).BuildServiceProvider();
        var context = Context(services, UserPermission.AdminMaintenance);
        var request = new WorkstationTableExportRequest(["task", "status", "files"]);
        var result = WorkstationEndpoints.BuildWorkstationDataTableExport("data-maintenance-executions", request, context, new(JsonSerializerDefaults.Web));
        var export = result.Should().BeOfType<JsonHttpResult<WorkstationTableExportDto>>().Which.Value!;
        export.RowCount.Should().Be(40, "the browser window of 25 is not the retained population");
        export.Csv.Should().Contain("Context · Execution ID").And.Contain("host retention applies");

        var filtered = WorkstationEndpoints.BuildWorkstationDataTableExport("data-maintenance-executions", request with
        {
            SearchText = "Failed", Filters = new Dictionary<string, string> { ["failedOnly"] = "true" }
        }, context, new(JsonSerializerDefaults.Web));
        filtered.Should().BeOfType<JsonHttpResult<WorkstationTableExportDto>>().Which.Value!.RowCount.Should().Be(4);
    }

    [Fact]
    public void Export_RefusesInsufficientAuthorityAndUnknownFilters()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var request = new WorkstationTableExportRequest(["task"]);
        var denied = WorkstationEndpoints.BuildWorkstationDataTableExport("data-maintenance-executions", request,
            Context(services, UserPermission.ViewDiagnostics), new(JsonSerializerDefaults.Web));
        denied.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
        var invalid = WorkstationEndpoints.BuildWorkstationDataTableExport("data-maintenance-executions", request with
        {
            Filters = new Dictionary<string, string> { ["accountId"] = "foreign" }
        }, Context(services, UserPermission.AdminMaintenance), new(JsonSerializerDefaults.Web));
        invalid.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(400);
    }

    private static DefaultHttpContext Context(IServiceProvider services, UserPermission permission)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
        return context;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
