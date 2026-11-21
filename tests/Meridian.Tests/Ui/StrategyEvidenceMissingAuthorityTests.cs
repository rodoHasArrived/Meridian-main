using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class StrategyEvidenceMissingAuthorityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("no-repository", false)]
    [InlineData("missing-run", false)]
    [InlineData("known-legacy", true)]
    public async Task RetainedStrategyManifestDoesNotLoseAccountAuthorizationWhenRunAuthorityIsUnavailable(string mode, bool visible)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-strategy-evidence-scope-" + Guid.NewGuid().ToString("N"));
        const string runId = "retained-strategy-run";
        Directory.CreateDirectory(root);
        try
        {
            var artifacts = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
            var retained = await artifacts.WriteManifestAsync(new EvidencePacketDto(
                new(runId, EvidenceSubjectResolver.StrategyRunKind, "Retained strategy", "Strategy", null, "Strategy"),
                DateTimeOffset.UtcNow, [], [], new(100, EvidenceStatusDto.Ready, [], [], [], [], []), [], []),
                new("synthetic-author", "Retained scoped strategy evidence")
                {
                    TenantId = "tenant-evidence", Scope = "company-evidence",
                    Linkage = new($"{EvidenceSubjectResolver.StrategyRunKind}/{runId}", runId, null, null, null)
                });
            var lookup = new EvidenceVaultLookupRequestDto(null, runId, null, null, null)
            { TenantId = "tenant-evidence", Scope = "company-evidence" };
            (await artifacts.FindByLinkageAsync(lookup)).Should().ContainSingle("the retained object exists before authorization filtering");
            var config = Path.Combine(root, "appsettings.json");
            await File.WriteAllTextAsync(config, "{\"DataRoot\":\".\"}");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(new Meridian.Application.UI.ConfigStore(config));
            builder.Services.AddWorkflowLibrary();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IEvidenceArtifactStore>(artifacts);
            if (mode != "no-repository")
            {
                var runs = new StrategyRunStore();
                if (mode == "known-legacy")
                    await runs.RecordRunAsync(StrategyRunEntry.Start("synthetic-legacy", "Synthetic legacy", RunType.Backtest) with { RunId = runId });
                builder.Services.AddSingleton<IStrategyRepository>(runs);
            }
            builder.Services.AddEvidenceWorkflowFabric();
            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "synthetic-reporter";
                context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Controller;
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewReporting | UserPermission.ViewStrategies;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-evidence";
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-evidence";
                await next();
            });
            app.MapEvidenceEndpoints(Json);
            await app.StartAsync();
            var client = app.GetTestClient();
            var expected = visible ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            (await client.GetAsync(retained.ManifestRoute)).StatusCode.Should().Be(expected);
            var vaultId = retained.VaultIdentity!.VaultId;
            (await client.GetAsync($"/workstation/evidence/vault/{vaultId}")).StatusCode.Should().Be(expected);
            var response = await client.PostAsJsonAsync(UiApiRoutes.WorkstationEvidenceVaultSearch, lookup, Json);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var found = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            found.RootElement.GetArrayLength().Should().Be(visible ? 1 : 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SupplementaryStrategyGuardLeavesOtherSubjectKindsToTheirExistingPolicy()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        (await EvidenceEndpoints.CanReadAccountBoundStrategyEvidenceAsync("report-pack", "retained-report", context)).Should().BeTrue();
    }
}
