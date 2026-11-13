using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.PrivateCapital;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.PrivateCapital;
using Meridian.Storage.FundAccounts;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalRecordEndpointTests
{
    [Fact]
    public async Task DedicatedReadPermissionDoesNotGrantWrite_AndMissingScopeIsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), "capital-endpoints-" + Guid.NewGuid());
        try
        {
            var service = new PrivateCapitalRecordService(new FilePrivateCapitalRecordStore(path), new Authority());
            await using var app = await AppAsync(service, UserPermission.ViewPrivateCapitalRecords);
            var client = app.GetTestClient();
            using var records = JsonDocument.Parse(await client.GetStringAsync("/api/private-capital/records"));
            Assert.False(records.RootElement.GetProperty("canManage").GetBoolean());
            var request = new CreatePrivateCapitalCommitment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "USD", "source",
                new("ev-012345678901234567890123", "doc", new('a', 64)));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/commitments", request)).StatusCode);
            client.DefaultRequestHeaders.Add("X-Test-Missing-Company", "true");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/private-capital/records")).StatusCode);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task NewRecordAndImportIdentitiesCannotCrossCompanyPartitions()
    {
        var path = Path.Combine(Path.GetTempPath(), "capital-endpoints-" + Guid.NewGuid());
        try
        {
            var service = new PrivateCapitalRecordService(new FilePrivateCapitalRecordStore(path), new Authority());
            await using var app = await AppAsync(service, UserPermission.ManagePrivateCapitalRecords);
            var client = app.GetTestClient();
            var evidence = new PrivateCapitalEvidence("ev-012345678901234567890123", "doc", new('a', 64));
            var create = await client.PostAsJsonAsync("/api/private-capital/commitments",
                new CreatePrivateCapitalCommitment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "USD", "agreement", evidence));
            create.EnsureSuccessStatusCode();
            var commitment = (await create.Content.ReadFromJsonAsync<PrivateCapitalCommitment>())!;
            client.DefaultRequestHeaders.Add("X-Test-Company", "company-b");
            using var records = JsonDocument.Parse(await client.GetStringAsync("/api/private-capital/records"));
            Assert.Empty(records.RootElement.GetProperty("commitments").EnumerateArray());
            var row = new PrivateCapitalSnapshotInput(commitment.CommitmentId, "2026-09-30", null, "1000", "0", null, "0", null, "statement");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/preview",
                new PreviewPrivateCapitalImport([row], null, evidence, "Cross-company identity refused"))).StatusCode);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task EntityGrantsFilterRecordsChoicesAndOpaqueSources_AndGateEveryMutation()
    {
        var path = Path.Combine(Path.GetTempPath(), "capital-entity-grants-" + Guid.NewGuid());
        try
        {
            var allowedHolder = Guid.NewGuid();
            var deniedHolder = Guid.NewGuid();
            var source = new Authority([allowedHolder, deniedHolder]);
            var service = new PrivateCapitalRecordService(new FilePrivateCapitalRecordStore(path), source);
            var scope = new PrivateCapitalScope("tenant-a", "company-a");
            var evidence = new PrivateCapitalEvidence("ev-012345678901234567890123", "doc", new('a', 64));
            var owned = await service.CreateAsync(scope, "operator", new(Guid.NewGuid(), allowedHolder, Guid.NewGuid(), "USD", "owned source", evidence));
            var hidden = await service.CreateAsync(scope, "operator", new(Guid.NewGuid(), deniedHolder, Guid.NewGuid(), "USD", "hidden source", evidence));
            PrivateCapitalSnapshotInput Row(Guid id, string date = "2026-09-30") => new(id, date, null, "1000", "0", "1000", "0", null, "statement");
            var ownedPreview = await service.PreviewAsync(scope, "operator", new([Row(owned.CommitmentId)], null, evidence, "Owned source review"));
            await service.AcceptAsync(scope, "operator", new(ownedPreview.BatchId, ownedPreview.Fingerprint, Guid.NewGuid()));
            var hiddenPreview = await service.PreviewAsync(scope, "operator", new([Row(hidden.CommitmentId)], null, evidence, "Hidden source review"));
            await service.AcceptAsync(scope, "operator", new(hiddenPreview.BatchId, hiddenPreview.Fingerprint, Guid.NewGuid()));
            var pendingOwned = await service.PreviewAsync(scope, "operator", new([Row(owned.CommitmentId, "2026-10-31")], null, evidence, "Owned pending review"));
            var pendingHidden = await service.PreviewAsync(scope, "operator", new([Row(hidden.CommitmentId, "2026-10-31")], null, evidence, "Hidden pending review"));
            var grants = new ScopedAuthorization([allowedHolder], [allowedHolder]);
            await using var app = await AppAsync(service, UserPermission.ViewPrivateCapitalRecords | UserPermission.ManagePrivateCapitalRecords, grants);
            var client = app.GetTestClient();

            using (var records = JsonDocument.Parse(await client.GetStringAsync("/api/private-capital/records")))
            {
                Assert.Equal(owned.CommitmentId, Assert.Single(records.RootElement.GetProperty("commitments").EnumerateArray()).GetProperty("commitmentId").GetGuid());
                Assert.Equal(owned.CommitmentId, Assert.Single(records.RootElement.GetProperty("snapshots").EnumerateArray()).GetProperty("commitmentId").GetGuid());
            }
            var choices = (await client.GetFromJsonAsync<PrivateCapitalChoices>("/api/private-capital/choices"))!;
            Assert.Equal(allowedHolder, Assert.Single(choices.Entities).Id);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/private-capital/imports/{ownedPreview.BatchId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/private-capital/imports/{hiddenPreview.BatchId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/commitments", new CreatePrivateCapitalCommitment(Guid.NewGuid(), deniedHolder, Guid.NewGuid(), "USD", "denied", evidence))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/preview", new PreviewPrivateCapitalImport([Row(owned.CommitmentId, "2026-11-01"), Row(hidden.CommitmentId, "2026-11-01")], null, evidence, "Mixed-holder batch refused"))).StatusCode);
            var csv = PrivateCapitalCsv.Header + $"\n{hidden.CommitmentId},2026-11-01,,1000,0,1000,0,,statement,";
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/preview", new PreviewPrivateCapitalImport(null, csv, evidence, "Hidden CSV refused"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/accept", new AcceptPrivateCapitalImport(pendingHidden.BatchId, pendingHidden.Fingerprint, Guid.NewGuid()))).StatusCode);

            // A retained opaque preview does not preserve write access after an entity grant is revoked.
            grants.WriteHolders.Clear();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/accept", new AcceptPrivateCapitalImport(pendingOwned.BatchId, pendingOwned.Fingerprint, Guid.NewGuid()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/preview", new PreviewPrivateCapitalImport([Row(owned.CommitmentId, "2026-11-01")], null, evidence, "Read grant cannot write"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/private-capital/imports/{ownedPreview.BatchId}")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<PrivateCapitalChoices>("/api/private-capital/choices"))!.Entities);
            Assert.Equal(2, (await service.ReadAsync(scope)).Snapshots.Count);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task MissingScopedAuthorizationServiceFailsClosedDespiteGlobalWritePermission()
    {
        var path = Path.Combine(Path.GetTempPath(), "capital-no-grants-" + Guid.NewGuid());
        try
        {
            var service = new PrivateCapitalRecordService(new FilePrivateCapitalRecordStore(path), new Authority());
            var evidence = new PrivateCapitalEvidence("ev-012345678901234567890123", "doc", new('a', 64));
            var create = new CreatePrivateCapitalCommitment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "USD", "source", evidence);
            await service.CreateAsync(new("tenant-a", "company-a"), "operator", create);
            await using var app = await AppAsync(service, UserPermission.ManagePrivateCapitalRecords, registerAuthorization: false);
            var client = app.GetTestClient();
            using var records = JsonDocument.Parse(await client.GetStringAsync("/api/private-capital/records"));
            Assert.Empty(records.RootElement.GetProperty("commitments").EnumerateArray());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/private-capital/commitments", create with { CommandId = Guid.NewGuid() })).StatusCode);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    private static async Task<WebApplication> AppAsync(PrivateCapitalRecordService service, UserPermission permissions,
        IScopedAuthorizationService? authorization = null, bool registerAuthorization = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMutationRateLimiter();
        builder.Services.AddSingleton(service);
        if (registerAuthorization)
            builder.Services.AddSingleton(authorization ?? new ScopedAuthorization());
        var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            if (!context.Request.Headers.ContainsKey("X-Test-Missing-Company"))
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = context.Request.Headers["X-Test-Company"].FirstOrDefault() ?? "company-a";
            await next(context);
        });
        app.MapPrivateCapitalRecordEndpoints(new(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }

    private sealed class Authority(IReadOnlyList<Guid>? holders = null) : IPrivateCapitalRecordAuthority
    {
        public Task<PrivateCapitalChoices> ChoicesAsync(PrivateCapitalScope scope, CancellationToken ct)
            => Task.FromResult(new PrivateCapitalChoices((holders ?? []).Select(id => new PrivateCapitalChoice(id, "Holder")).ToArray(), [new(Guid.NewGuid(), "Vehicle")]));
        public Task<PrivateCapitalVehicle> ValidateRegisterAsync(PrivateCapitalScope scope, Guid entityId, Guid securityId, string currency, CancellationToken ct)
            => Task.FromResult(new PrivateCapitalVehicle("Vehicle", "Buyout", 2020, 1));
        public Task ValidateEvidenceAsync(PrivateCapitalScope scope, PrivateCapitalEvidence evidence, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ScopedAuthorization(IReadOnlyList<Guid>? readHolders = null, IReadOnlyList<Guid>? writeHolders = null) : IScopedAuthorizationService
    {
        public HashSet<Guid> WriteHolders { get; } = (writeHolders ?? []).ToHashSet();
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(string actor, UserPermission requiredPermission,
            AccessScopeKindDto scopeKind, Guid? scopeId, UserPermission globalPermissions, CancellationToken ct = default)
        {
            var allowed = scopeKind == AccessScopeKindDto.LegalEntity && scopeId.HasValue &&
                (requiredPermission == UserPermission.ManagePrivateCapitalRecords
                    ? writeHolders is null || WriteHolders.Contains(scopeId.Value)
                    : readHolders is null || readHolders.Contains(scopeId.Value));
            return Task.FromResult(new ScopedAuthorizationDecisionDto(allowed, actor, requiredPermission, scopeKind, scopeId, "Explicit synthetic entity grant."));
        }
    }
}
