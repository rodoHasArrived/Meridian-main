using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

// Phase 3, C2: the browser's bulk assign, route and resolve go through the governed casework
// path — a dry run first, then an execute of the cases that passed — and every changed case keeps
// its own audit entry, attributed to the session operator rather than to the browser.
public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_BulkAssignToMe_DryRunsThenWritesOneAuditEntryPerBreakAsTheSessionOperator()
    {
        await using var app = await CreateAppAsync(RegisterBulkCaseworkHandoff);
        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        await repository.CreateIfMissingAsync(TestReconciliationQueueScope, BuildBreakQueueItem("break-bulk-a", null));
        await repository.CreateIfMissingAsync(TestReconciliationQueueScope, BuildBreakQueueItem("break-bulk-b", null));
        var client = app.GetTestClient();

        // The browser may send its display name; "assign to me" still names the session operator.
        var dryRunRequest = BulkRequest("bulk-assign:dry-run", ReconciliationCaseworkAction.Assign, ["break-bulk-a", "break-bulk-b"], dryRun: true) with
        {
            Assignee = "browser-display-name",
            AssignToCurrentOperator = true
        };
        var dryRunResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationBreakBulkDryRun, dryRunRequest, ServerJsonOptions);
        dryRunResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dryRun = await dryRunResponse.Content.ReadFromJsonAsync<ReconciliationBulkCaseworkResult>(ServerJsonOptions);
        dryRun!.DryRun.Should().BeTrue();
        dryRun.Results.Should().OnlyContain(result => result.WouldSucceed && !result.Succeeded);
        (await repository.GetByIdAsync(TestReconciliationQueueScope, "break-bulk-a"))!.AssignedTo.Should().BeNull("a dry run changes nothing");

        dryRun.ReviewProtocol.Should().Be(1);
        var executeRequest = ReviewedExecute(dryRunRequest, "bulk-assign:execute") with
        {
            Assignee = null
        };
        var executeResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationBreakBulkExecute, executeRequest, ServerJsonOptions);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var executed = await executeResponse.Content.ReadFromJsonAsync<ReconciliationBulkCaseworkResult>(ServerJsonOptions);
        executed!.SucceededCount.Should().Be(2);
        executed.Results.Should().OnlyContain(result => result.Succeeded && result.Item!.AssignedTo == "ops-user");

        foreach (var breakId in new[] { "break-bulk-a", "break-bulk-b" })
        {
            var audit = await repository.GetAuditHistoryAsync(breakId);
            var succeeded = audit.Where(entry => entry.EventType == "BulkActionCaseSucceeded").ToArray();
            succeeded.Should().ContainSingle($"break {breakId} keeps its own audit entry for the bulk action");
            succeeded[0].Actor.Should().Be("ops-user");
            succeeded[0].Source.Should().Be("workstation-reconciliation-bulk-casework");
            succeeded[0].CommandId.Should().Be($"bulk-assign:execute:{breakId}");
            audit.Should().Contain(entry => entry.EventType == "BulkActionCaseValidated", "the dry run is retained too");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BulkRoute_KeepsTheNamedOwnerAndBulkResolveReportsBlockedBreaksWithoutChangingThem()
    {
        await using var app = await CreateAppAsync(RegisterBulkCaseworkHandoff);
        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        await repository.CreateIfMissingAsync(TestReconciliationQueueScope, BuildBreakQueueItem("break-route-a", null));
        var client = app.GetTestClient();

        var routeRequest = BulkRequest("bulk-route:dry-run", ReconciliationCaseworkAction.Assign, ["break-route-a"], dryRun: true) with
        {
            Assignee = "custody-desk",
            Note = "Custodian owns the position lag."
        };
        var previewResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationBreakBulkDryRun, routeRequest, ServerJsonOptions);
        previewResponse.EnsureSuccessStatusCode();
        var routeResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationBreakBulkExecute,
            ReviewedExecute(routeRequest, "bulk-route:execute"), ServerJsonOptions);
        var routed = await routeResponse.Content.ReadFromJsonAsync<ReconciliationBulkCaseworkResult>(ServerJsonOptions);
        routed!.Results.Should().ContainSingle(result => result.Succeeded && result.Item!.AssignedTo == "custody-desk");

        var resolveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationBreakBulkDryRun,
            BulkRequest("bulk-resolve:dry-run", ReconciliationCaseworkAction.Resolve, ["break-route-a"], dryRun: true) with
            {
                Reason = "Journal posted for the classification fix.",
                RootCauseCode = "LedgerClassification",
                ResolutionCode = "LedgerAdjusted",
                EvidenceLinks = ["journal:je-2026-06-30-01"]
            },
            ServerJsonOptions);
        var resolvePreview = await resolveResponse.Content.ReadFromJsonAsync<ReconciliationBulkCaseworkResult>(ServerJsonOptions);
        resolvePreview!.Results.Should().ContainSingle(result => !result.WouldSucceed && result.Error!.Contains("Cannot resolve case from Open"));
        (await repository.GetByIdAsync(TestReconciliationQueueScope, "break-route-a"))!.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BulkExecute_RequiresAnExactPreviewAndRejectsAChangedCaseAtomically()
    {
        await using var app = await CreateAppAsync(RegisterBulkCaseworkHandoff);
        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        await repository.CreateIfMissingAsync(TestReconciliationQueueScope, BuildBreakQueueItem("stable-case", null));
        await repository.CreateIfMissingAsync(TestReconciliationQueueScope, BuildBreakQueueItem("changed-case", null));
        var client = app.GetTestClient();
        var request = BulkRequest("stale:preview", ReconciliationCaseworkAction.Assign, ["stable-case", "changed-case"], dryRun: true)
            with
        { Assignee = "reviewer" };
        var execute = ReviewedExecute(request, "stale:execute");

        var unreviewed = await PostAsync(UiApiRoutes.ReconciliationBreakBulkExecute, execute with { PreviewBulkActionId = null });
        unreviewed.SucceededCount.Should().Be(0);
        unreviewed.Results.Should().OnlyContain(item => item.Error!.Contains("fresh bulk preview"));
        var retired = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationBreakQueue + "/bulk",
            new { breakIds = new[] { "stable-case" }, action = "assign", assignee = "reviewer" }, ServerJsonOptions);
        retired.IsSuccessStatusCode.Should().BeFalse();

        var preview = await PostAsync(UiApiRoutes.ReconciliationBreakBulkDryRun, request);
        preview.Results.Should().OnlyContain(result => result.WouldSucceed && result.Item!.Version > 0);
        var changed = preview.Results.Single(result => result.BreakId == "changed-case").Item!;
        await repository.ApplyCaseworkCommandAsync(TestReconciliationQueueScope, new ReconciliationCaseworkCommand(
            changed.BreakId, ReconciliationCaseworkAction.Assign, "another-reviewer", "change", "stale:change", "test",
            changed.Version, Assignee: "another-reviewer"));

        var blocked = await PostAsync(UiApiRoutes.ReconciliationBreakBulkExecute, execute);
        blocked.SucceededCount.Should().Be(0);
        blocked.Results.Should().Contain(result => result.BreakId == "changed-case" && result.Error!.Contains("version", StringComparison.OrdinalIgnoreCase));
        (await repository.GetByIdAsync(TestReconciliationQueueScope, "stable-case"))!.AssignedTo.Should().BeNull();
        (await repository.GetByIdAsync(TestReconciliationQueueScope, "changed-case"))!.AssignedTo.Should().Be("another-reviewer");
        var retained = await client.GetFromJsonAsync<ReconciliationBulkCaseworkResult>(
            UiApiRoutes.ReconciliationBreakBulkResult.Replace("{bulkActionId}", Uri.EscapeDataString(execute.CommandId)), ServerJsonOptions);
        retained.Should().BeEquivalentTo(blocked);

        async Task<ReconciliationBulkCaseworkResult> PostAsync(string route, ReconciliationBulkCaseworkRequest input)
        {
            var response = await client.PostAsJsonAsync(route, input, ServerJsonOptions);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ReconciliationBulkCaseworkResult>(ServerJsonOptions))!;
        }
    }

    private static ReconciliationBulkCaseworkRequest ReviewedExecute(ReconciliationBulkCaseworkRequest dryRun, string commandId)
        => dryRun with
        {
            CommandId = commandId,
            IdempotencyKey = commandId,
            PreviewBulkActionId = dryRun.CommandId,
            DryRun = false,
            AllowPartialSuccess = false
        };

    private static void RegisterBulkCaseworkHandoff(IServiceCollection services)
        => services.AddSingleton<IStatementReconciliationCaseworkHandoffService>(sp =>
            new StatementReconciliationCaseworkHandoffService(sp.GetRequiredService<IReconciliationBreakQueueRepository>()));

    private static ReconciliationBulkCaseworkRequest BulkRequest(
        string operationId,
        ReconciliationCaseworkAction action,
        IReadOnlyList<string> breakIds,
        bool dryRun)
        => new(
            BreakIds: breakIds,
            Action: action,
            Actor: "browser-spoof",
            CommandId: operationId,
            CorrelationId: operationId,
            Source: "browser-workstation",
            IdempotencyKey: operationId,
            DryRun: dryRun,
            AllowPartialSuccess: dryRun);
}
