using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Tests.AssetOperations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

/// <summary>
/// The factor-paydown instruction carries its own copy of the event scope, which the spine requires
/// payload-equal to the request scope and the atomic store binds to the caller's tenant. The spine
/// endpoints therefore give it the same server-resolved tenant and company as the request scope.
/// </summary>
public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task AssetAccountingProjectionEndpoint_RewritesTheFactorPaydownInstructionScopeWithTheTrustedTenant()
    {
        var service = new CapturingAssetAccountingEventSpineService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IAssetAccountingEventSpineService>(service),
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserCompanyId: "company-alpha",
            mapLedgerApi: true);
        var client = app.GetTestClient();
        var instruction = FactorPaydownTestData.Build([FactorPaydownTestData.PremiumLot()], 0.95m, 0.90m,
            tenantId: null, companyId: null);
        var request = FactorPaydownTestData.ProjectRequest(instruction, new LedgerDimensionSetDto()) with
        {
            Scope = instruction.Scope with { TenantId = "client-tenant", CompanyId = "client-company" }
        };

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAssetAccountingEventProjections, request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var forwarded = service.ProjectRequests.Should().ContainSingle().Subject;
        forwarded.Scope.TenantId.Should().Be("company-alpha");
        forwarded.Scope.CompanyId.Should().Be("company-alpha");
        var paydown = forwarded.FactorPaydown.Should().NotBeNull().And.Subject.As<OpenLotFactorPaydownInstructionDto>();
        paydown.Scope.Should().BeEquivalentTo(forwarded.Scope, "the spine requires the two scopes payload-equal");
        (paydown with { Scope = instruction.Scope }).Should().BeEquivalentTo(instruction,
            "only the tenant and company of the instruction scope are rewritten");
    }

    [Fact]
    public async Task AssetAccountingCandidateEndpoint_RewritesTheDraftedFactorPaydownInstructionScopeWithTheTrustedTenant()
    {
        var service = new CapturingAssetAccountingEventSpineService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IAssetAccountingEventSpineService>(service),
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserCompanyId: "company-alpha",
            mapLedgerApi: true);
        var client = app.GetTestClient();
        var instruction = FactorPaydownTestData.Build([FactorPaydownTestData.PremiumLot()], 0.95m, 0.90m,
            tenantId: "client-tenant", companyId: "client-company");
        var request = BuildAssetAccountingCandidateRequest() with
        {
            LotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.FactorPaydown,
                AssetAccountId: "Assets:Investment", FactorPaydown: instruction)
        };

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationAssetAccountingCandidates, request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var forwarded = service.CandidateRequests.Should().ContainSingle().Subject;
        var lotMutation = forwarded.LotMutation.Should().NotBeNull().And.Subject.As<AssetLotMutationInstructionDto>();
        lotMutation.Intent.Should().Be(AssetLotMutationIntentDto.FactorPaydown);
        lotMutation.AssetAccountId.Should().Be("Assets:Investment");
        lotMutation.FactorPaydown!.Scope.TenantId.Should().Be("company-alpha");
        lotMutation.FactorPaydown.Scope.CompanyId.Should().Be("company-alpha");
        (lotMutation.FactorPaydown with { Scope = instruction.Scope }).Should().BeEquivalentTo(instruction);
    }
}
