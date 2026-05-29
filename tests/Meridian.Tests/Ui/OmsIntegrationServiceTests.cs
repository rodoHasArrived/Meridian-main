using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Auth;
using Meridian.Ui.Services.Services.Integrations;
using Meridian.Ui.Shared.Contracts.Integrations;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class OmsIntegrationServiceTests
{
    [Fact]
    public void Ingest_IsIdempotent_WithStableDeduplicationKey()
    {
        var service = new OmsIntegrationApiHandler();
        var message = new OmsInboundMessage("oms-a", "ord-1", "fill", DateTimeOffset.UtcNow, "hash-1", null, "corr-1");

        var first = service.Ingest(message);
        var second = service.Ingest(message);

        first.ReplayDetected.Should().BeFalse();
        second.ReplayDetected.Should().BeTrue();
        service.Snapshot().Should().HaveCount(1);
    }

    [Fact]
    public void Ingest_Replay_DoesNotOverwriteExistingMessage()
    {
        var service = new OmsIntegrationApiHandler();
        var first = new OmsInboundMessage("oms-a", "ord-1", "fill", DateTimeOffset.UtcNow, "hash-1", "stable-key", "corr-1");
        var replay = new OmsInboundMessage("oms-b", "ord-99", "cancel", DateTimeOffset.UtcNow.AddMinutes(1), "hash-2", "stable-key", "corr-2");

        service.Ingest(first);
        var result = service.Ingest(replay);

        result.ReplayDetected.Should().BeTrue();
        var snapshot = service.Snapshot().Should().ContainSingle().Subject;
        snapshot.SourceSystem.Should().Be("oms-a");
        snapshot.ExternalOrderId.Should().Be("ord-1");
        snapshot.EventType.Should().Be("fill");
        snapshot.PayloadHash.Should().Be("hash-1");
    }

    [Fact]
    public void AdapterDiagnostics_DeclareFixAndFileTransferBoundaries_WithRetryPolicy()
    {
        var service = new OmsIntegrationApiHandler();

        var diagnostics = service.AdapterDiagnostics();

        diagnostics.Should().Contain(d => d.Boundary.Kind == OmsAdapterKind.Fix && d.Boundary.RequiresRequestSigning);
        diagnostics.Should().Contain(d => d.Boundary.Kind == OmsAdapterKind.Sftp && d.Boundary.RetryPolicy.MaxAttempts == 7);
        diagnostics.Should().Contain(d => d.Boundary.Kind == OmsAdapterKind.FileDrop && !d.Boundary.RequiresRequestSigning);
    }

    [Fact]
    public void ResolveSyncConflict_UsesTimestampPrecedence_ForExcelPush()
    {
        var service = new OmsIntegrationApiHandler();
        var older = new OmsSyncRecord("acct-1", DateTimeOffset.UtcNow.AddMinutes(-5), new Dictionary<string, string>{{"qty","10"}});
        var newer = new OmsSyncRecord("acct-1", DateTimeOffset.UtcNow, new Dictionary<string, string>{{"qty","12"}});

        var result = service.ResolveSyncConflict(new OmsSyncRequest("push", "acct-1", "corr-2", older, newer));

        result.Policy.Should().Be("timestamp-precedence");
        result.Mode.Should().Be("push");
        result.Winner.Fields["qty"].Should().Be("12");
        service.AuditTrail().Should().Contain(a => a.Action == "excel.sync-conflict-resolved");
    }

    [Fact]
    public void RequestSigning_RejectsInvalidSignature_AndAuditLogsFailure()
    {
        var service = new OmsIntegrationApiHandler();
        var signature = new OmsRequestSignatureInput("default", DateTimeOffset.UtcNow.ToString("O"), "bad", "corr-3");
        var message = new OmsInboundMessage("ems-a", "ord-2", "new", DateTimeOffset.UtcNow, "hash-3", null, "corr-3");

        var act = () => service.Ingest(message, signature);

        act.Should().Throw<InvalidOperationException>().WithMessage("Signature mismatch.");
        service.AuditTrail().Should().Contain(a => a.Action == "auth.signature-rejected");
    }

    [Fact]
    public async Task MapOmsIntegrationEndpoints_Ingest_MapsInboundContractThroughHandler()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageOrders);
        var client = app.GetTestClient();
        var message = new OmsInboundMessage("oms-a", "ord-42", "fill", DateTimeOffset.UtcNow, "hash-42", "map-key", "corr-map");

        using var response = await client.PostAsJsonAsync("/api/oms/ingest", message);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OmsIngestionResult>(JsonOptions());
        result!.DeduplicationKey.Should().Be("map-key");
        result.ReplayDetected.Should().BeFalse();
    }

    [Fact]
    public async Task MapOmsIntegrationEndpoints_ExcelSync_RequiresManageOrdersPermission()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades);
        var client = app.GetTestClient();
        var older = new OmsSyncRecord("acct-1", DateTimeOffset.UtcNow.AddMinutes(-5), new Dictionary<string, string>{{"qty","10"}});
        var newer = new OmsSyncRecord("acct-1", DateTimeOffset.UtcNow, new Dictionary<string, string>{{"qty","12"}});

        using var response = await client.PostAsJsonAsync("/api/oms/excel/sync", new OmsSyncRequest("push", "acct-1", "corr-auth", older, newer));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void ServiceCollectionExtension_RegistersServicesApiHandlerForSharedContract()
    {
        var services = new ServiceCollection();

        services.AddOmsIntegrationApiHandlers();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOmsIntegrationApiHandler) &&
            descriptor.ImplementationType == typeof(OmsIntegrationApiHandler));
    }

    [Fact]
    public void KeyRotation_CreatesAuditedSigningKeyHook()
    {
        var service = new OmsIntegrationApiHandler();
        var request = new OmsKeyRotationRequest("oms-key-2026-05", "security-admin", "corr-4", DateTimeOffset.UtcNow);

        var result = service.RotateSigningKey(request);

        result.Status.Should().Be("rotated");
        service.AuditTrail().Should().Contain(a => a.Action == "auth.signing-key-rotated" && a.Scope == OmsIntegrationScope.Admin);
    }

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddOmsIntegrationApiHandlers();
        var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapOmsIntegrationEndpoints(JsonOptions());
        await app.StartAsync();
        return app;
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
}
