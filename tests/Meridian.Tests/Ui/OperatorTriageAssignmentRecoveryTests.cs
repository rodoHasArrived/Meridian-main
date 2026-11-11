using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class OperatorTriageAssignmentRecoveryTests
{
    [Fact]
    public async Task SourceAssignmentCommitted_MetadataCommitFails_ExactHttpRetryRecoversWithoutReassignment()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-triage-recovery-" + Guid.NewGuid().ToString("N"));
        var scope = new ReconciliationBreakQueueScope("tenant-test", "tenant-test");
        var triageScope = new OperatorTriageScope(scope.TenantId, scope.CompanyId!);
        const string requestId = "assignment-recovery-command";
        const string actor = "ops-user";
        try
        {
            var queue = new FileReconciliationBreakQueueRepository(Path.Combine(root, "source"), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
            var now = DateTimeOffset.UtcNow;
            var source = new ReconciliationBreakQueueItem(
                BreakId: "break-triage", RunId: "run-triage", StrategyName: "Retained recovery fixture",
                Category: ReconciliationBreakCategory.ExternalStatementMismatch, Status: ReconciliationBreakQueueStatus.Open,
                Variance: 1m, Reason: "Retained source differs.", AssignedTo: null,
                DetectedAt: now, LastUpdatedAt: now, Severity: ReconciliationBreakSeverity.Medium,
                Version: 0) { TenantId = scope.TenantId, CompanyId = scope.CompanyId };
            Assert.True(await queue.CreateIfMissingAsync(scope, source));
            var initialSource = (await queue.GetByIdAsync(scope, source.BreakId))!;
            var durableMetadata = new FileOperatorTriageStore(Path.Combine(root, "metadata"));
            var failingMetadata = new FailFirstAssignmentCommit(durableMetadata, requestId);
            var handoff = new CountingHandoff(new StatementReconciliationCaseworkHandoffService(queue));

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IReconciliationBreakQueueRepository>(queue);
            builder.Services.AddSingleton<IStatementReconciliationCaseworkHandoffService>(handoff);
            builder.Services.AddSingleton<IOperatorTriageStore>(failingMetadata);
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
                _ => RateLimitPartition.GetNoLimiter("recovery-test")));
            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.AdminMaintenance;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = scope.CompanyId;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = scope.TenantId;
                await next();
            });
            app.UseRouting();
            app.UseRateLimiter();
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
            typeof(WorkstationEndpoints).GetMethod("MapOperatorTriageEndpoints", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [app.MapGroup("/api/workstation"), json]);
            await app.StartAsync();
            using var client = app.GetTestClient();
            var inbox = await client.GetFromJsonAsync<OperatorInboxTriageDto>("/api/workstation/operator/triage", json);
            var item = Assert.Single(inbox!.Items);
            Assert.Equal(initialSource.Version, item.SourceVersion);
            var request = new OperatorTriageCommand(requestId, OperatorTriageAction.Assign, item.Version,
                "Assign the retained case to this authorized operator.", actor, ExpectedSourceVersion: item.SourceVersion);
            var path = $"/api/workstation/operator/triage/{Uri.EscapeDataString(item.WorkItemId)}/actions";

            using var failed = await client.PostAsJsonAsync(path, request, json);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.True(failingMetadata.Failed);
            var committedSource = await queue.GetByIdAsync(scope, source.BreakId);
            Assert.Equal(actor, committedSource!.AssignedTo);
            Assert.Equal(initialSource.Version + 1, committedSource.Version);
            var uncommittedMetadata = await durableMetadata.GetAsync(triageScope, item.WorkItemId);
            Assert.Equal(0, uncommittedMetadata.Version);
            Assert.Empty(uncommittedMetadata.Receipts);

            using var recovered = await client.PostAsJsonAsync(path, request, json);
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            var result = await recovered.Content.ReadFromJsonAsync<OperatorTriageResult>(json);
            Assert.Equal("Applied", result!.Status);
            Assert.Equal(actor, result.Item!.OwnerId);
            Assert.Equal(1, result.Item.Version);
            Assert.Equal(2, handoff.Commands.Count);
            Assert.Equal(handoff.Commands[0], handoff.Commands[1]);
            Assert.Equal(initialSource.Version, handoff.Commands[1].ExpectedVersion);
            Assert.Equal(committedSource.Version, (await queue.GetByIdAsync(scope, source.BreakId))!.Version);

            using var replay = await client.PostAsJsonAsync(path, request, json);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True((await replay.Content.ReadFromJsonAsync<OperatorTriageResult>(json))!.Replayed);
            Assert.Equal(2, handoff.Commands.Count);
            var metadata = await durableMetadata.GetAsync(triageScope, item.WorkItemId);
            Assert.Single(metadata.Activity);
            Assert.Single(metadata.Receipts);
            Assert.Equal(1, metadata.Version);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FailFirstAssignmentCommit(IOperatorTriageStore inner, string commandId) : IOperatorTriageStore
    {
        public bool Failed { get; private set; }
        public Task<OperatorTriageState> GetAsync(OperatorTriageScope scope, string workItemId, CancellationToken ct = default)
            => inner.GetAsync(scope, workItemId, ct);
        public Task<OperatorTriageState> UpdateAsync(OperatorTriageScope scope, string workItemId,
            Func<OperatorTriageState, CancellationToken, Task<OperatorTriageState>> update, CancellationToken ct = default)
            => inner.UpdateAsync(scope, workItemId, async (state, token) =>
            {
                var next = await update(state, token);
                if (!Failed && next.Receipts.Any(receipt => receipt.CommandId == commandId))
                {
                    Failed = true;
                    throw new IOException("Injected metadata commit failure after the source command completed.");
                }
                return next;
            }, ct);
    }

    private sealed class CountingHandoff(IStatementReconciliationCaseworkHandoffService inner) : IStatementReconciliationCaseworkHandoffService
    {
        public List<ReconciliationCaseworkCommand> Commands { get; } = [];
        public Task<ReconciliationBreakQueueTransitionResult> ApplyAsync(ReconciliationBreakQueueScope scope,
            ReconciliationCaseworkCommand command, CancellationToken ct = default)
        {
            Commands.Add(command);
            return inner.ApplyAsync(scope, command, ct);
        }
        public Task<ReconciliationBulkCaseworkResult> ApplyBulkAsync(ReconciliationBreakQueueScope scope,
            ReconciliationBulkCaseworkRequest request, CancellationToken ct = default) => inner.ApplyBulkAsync(scope, request, ct);
    }
}
