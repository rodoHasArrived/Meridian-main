using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrations;
using Meridian.Identity.Auth;
using Meridian.Storage.Integrations;
using Microsoft.AspNetCore.TestHost;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationActorFlags_AppendFlagsForDeskLabelActors_Idempotently()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            var tenantStore = DefaultProviderIntegrationTenantStore(store);
            var manifest = CreateProviderIntegrationActivationManifest() with { ApprovedBy = "Meridian Operator" };
            await tenantStore.SaveManifestAsync(manifest);
            var storedManifest = await tenantStore.GetManifestAsync(manifest.ManifestId);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ManageProviders);
            var client = app.GetTestClient();

            var first = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationActorFlagsScan,
                new ProviderIntegrationActorFlagScanRequestDto(),
                ServerJsonOptions);
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            var scan = await first.Content.ReadFromJsonAsync<ProviderIntegrationActorFlagScanResultDto>(ServerJsonOptions);
            scan!.FlagsAppended.Should().BeGreaterThan(0);
            // The approver lives in the retained, digest-bound manifest version.
            var approver = scan.Flags.Should().ContainSingle(flag => flag.FieldPath == "manifest.approvedBy").Subject;
            approver.RecordKind.Should().Be("manifest-versions");
            approver.RecordedValue.Should().Be("Meridian Operator");
            approver.Reason.Should().Be("session desk label");
            approver.SubjectId.Should().Be(manifest.ManifestId);
            approver.FlaggedBy.Should().Be("ops-user", "the scan records the authenticated operator");

            var second = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationActorFlagsScan,
                new ProviderIntegrationActorFlagScanRequestDto(),
                ServerJsonOptions);
            var rescan = await second.Content.ReadFromJsonAsync<ProviderIntegrationActorFlagScanResultDto>(ServerJsonOptions);
            rescan!.FlagsAppended.Should().Be(0, "flags are append-only and never duplicated");
            rescan.AlreadyFlagged.Should().Be(scan.FlagsAppended);

            var listed = await client.GetFromJsonAsync<IReadOnlyList<ProviderIntegrationActorFlagDto>>(
                UiApiRoutes.WorkstationProviderIntegrationActorFlags, ServerJsonOptions);
            listed.Should().ContainEquivalentOf(approver);

            (await tenantStore.GetManifestAsync(manifest.ManifestId)).Should().BeEquivalentTo(storedManifest,
                "the flagged manifest, and its digest-bound approvedBy, are never rewritten");
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ProviderIntegrationActorFlags_ScanRequiresConfigurePermission_ListAllowsRead()
    {
        var testRoot = CreateProviderIntegrationTestRoot();
        try
        {
            var store = new FileProviderIntegrationManifestStore(testRoot);
            await using var app = await CreateAppAsync(
                services => RegisterProviderIntegrationEndpointServices(services, store),
                currentUserPermissions: UserPermission.ViewConfig);
            var client = app.GetTestClient();

            var scan = await client.PostAsJsonAsync(
                UiApiRoutes.WorkstationProviderIntegrationActorFlagsScan,
                new ProviderIntegrationActorFlagScanRequestDto(),
                ServerJsonOptions);
            scan.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var list = await client.GetAsync(UiApiRoutes.WorkstationProviderIntegrationActorFlags);
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            (await list.Content.ReadFromJsonAsync<IReadOnlyList<ProviderIntegrationActorFlagDto>>(ServerJsonOptions))
                .Should().BeEmpty();
        }
        finally
        {
            DeleteProviderIntegrationTestRoot(testRoot);
        }
    }
}
