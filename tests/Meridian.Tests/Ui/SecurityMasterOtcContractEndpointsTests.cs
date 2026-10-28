using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterOtcContractEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Trade = new(2026, 3, 2);

    [Fact]
    public async Task GetContracts_ListsTheInstrumentsContracts_OrNotFoundForAnUnknownSecurity()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(Detail(securityId));
        queryService.GetByIdAsync(Arg.Is<Guid>(id => id != securityId), Arg.Any<CancellationToken>()).Returns((SecurityDetailDto?)null);
        var store = Substitute.For<IOtcContractStore>();
        store.ListContractsForSecurityAsync(securityId, Arg.Any<CancellationToken>()).Returns(new[] { Contract(securityId), Contract(securityId) });
        await using var app = await CreateAppAsync(queryService, store);
        var client = app.GetTestClient();

        using var ok = await client.GetAsync($"/api/security-master/{securityId}/contracts");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<List<OtcContractDto>>(Json)).Should().HaveCount(2);

        using var missing = await client.GetAsync($"/api/security-master/{Guid.NewGuid()}/contracts");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpsertContract_StampsTheActor_AndMapsARuleFailureToBadRequest()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<IOtcContractStore>();
        UpsertOtcContractRequest? captured = null;
        store.UpsertContractAsync(Arg.Do<UpsertOtcContractRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Contract(securityId)));
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), store);
        var client = app.GetTestClient();

        var request = new UpsertOtcContractRequest(null, securityId, Guid.NewGuid(), "T-1", OtcContractDirection.Buy, 1_000_000m, "USD", Trade, Trade, null, OtcContractStatus.Pending, null, null, "untrusted.body.actor");
        using var ok = await client.PostAsJsonAsync("/api/security-master/otc/contracts", request, Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.UpdatedBy.Should().Be("casey.doyle", "the actor comes from the session, not the body");

        store.UpsertContractAsync(Arg.Any<UpsertOtcContractRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OtcContractDto>(new ArgumentException("Asset class 'Equity' is not traded as a bilateral contract.")));
        using var refused = await client.PostAsJsonAsync("/api/security-master/otc/contracts", request, Json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("bilateral contract");
    }

    [Fact]
    public async Task Agreements_AndNettingSets_ReadBack_OrNotFound()
    {
        var store = Substitute.For<IOtcContractStore>();
        var master = new OtcMasterAgreementDto(Guid.NewGuid(), "Big Bank", null, "Fund", null, OtcMasterAgreementType.Isda2002, Trade, null, null, 1, "casey", DateTimeOffset.UtcNow);
        var csa = new OtcCollateralAgreementDto(Guid.NewGuid(), master.AgreementId, "USD", 0m, 0m, 0m, null, null, "Daily", [new OtcEligibleCollateralDto("USD cash", null, 0m)], Trade, null, null, 1, "casey", DateTimeOffset.UtcNow);
        var nettingSet = new OtcNettingSetDto(Guid.NewGuid(), master.AgreementId, csa.CollateralAgreementId, "Big Bank ISDA", null, 1, "casey", DateTimeOffset.UtcNow);
        store.GetMasterAgreementAsync(master.AgreementId, Arg.Any<CancellationToken>()).Returns(master);
        store.ListMasterAgreementsAsync("Big Bank", Arg.Any<CancellationToken>()).Returns(new[] { master });
        store.GetCollateralAgreementAsync(csa.CollateralAgreementId, Arg.Any<CancellationToken>()).Returns(csa);
        store.GetNettingSetAsync(nettingSet.NettingSetId, Arg.Any<CancellationToken>()).Returns(new OtcNettingSetViewDto(nettingSet, master, csa, []));
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), store);
        var client = app.GetTestClient();

        (await client.GetFromJsonAsync<OtcMasterAgreementDto>($"/api/security-master/otc/master-agreements/{master.AgreementId}", Json))!.Counterparty.Should().Be("Big Bank");
        (await client.GetFromJsonAsync<List<OtcMasterAgreementDto>>("/api/security-master/otc/master-agreements?counterparty=Big%20Bank", Json)).Should().ContainSingle();
        (await client.GetFromJsonAsync<OtcCollateralAgreementDto>($"/api/security-master/otc/collateral-agreements/{csa.CollateralAgreementId}", Json))!.EligibleCollateral.Should().ContainSingle();
        var view = await client.GetFromJsonAsync<OtcNettingSetViewDto>($"/api/security-master/otc/netting-sets/{nettingSet.NettingSetId}", Json);
        view!.CollateralAgreement!.CollateralAgreementId.Should().Be(csa.CollateralAgreementId);

        (await client.GetAsync($"/api/security-master/otc/master-agreements/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/security-master/otc/collateral-agreements/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/security-master/otc/netting-sets/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/security-master/otc/contracts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AgreementWrites_StampTheActor()
    {
        var store = Substitute.For<IOtcContractStore>();
        UpsertOtcMasterAgreementRequest? master = null;
        UpsertOtcCollateralAgreementRequest? csa = null;
        UpsertOtcNettingSetRequest? nettingSet = null;
        store.UpsertMasterAgreementAsync(Arg.Do<UpsertOtcMasterAgreementRequest>(r => master = r), Arg.Any<CancellationToken>())
            .Returns(new OtcMasterAgreementDto(Guid.NewGuid(), "Big Bank", null, "Fund", null, OtcMasterAgreementType.Isda2002, Trade, null, null, 1, "casey.doyle", DateTimeOffset.UtcNow));
        store.UpsertCollateralAgreementAsync(Arg.Do<UpsertOtcCollateralAgreementRequest>(r => csa = r), Arg.Any<CancellationToken>())
            .Returns(new OtcCollateralAgreementDto(Guid.NewGuid(), Guid.NewGuid(), "USD", 0m, 0m, 0m, null, null, "Daily", [], Trade, null, null, 1, "casey.doyle", DateTimeOffset.UtcNow));
        store.UpsertNettingSetAsync(Arg.Do<UpsertOtcNettingSetRequest>(r => nettingSet = r), Arg.Any<CancellationToken>())
            .Returns(new OtcNettingSetDto(Guid.NewGuid(), Guid.NewGuid(), null, "Set", null, 1, "casey.doyle", DateTimeOffset.UtcNow));
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), store);
        var client = app.GetTestClient();

        (await client.PostAsJsonAsync("/api/security-master/otc/master-agreements",
            new UpsertOtcMasterAgreementRequest(null, "Big Bank", null, "Fund", null, OtcMasterAgreementType.Isda2002, Trade, null, null, "body"), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/security-master/otc/collateral-agreements",
            new UpsertOtcCollateralAgreementRequest(null, Guid.NewGuid(), "USD", 0m, 0m, 0m, null, null, "Daily", [], Trade, null, null, "body"), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/security-master/otc/netting-sets",
            new UpsertOtcNettingSetRequest(null, Guid.NewGuid(), null, "Set", null, "body"), Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        master!.UpdatedBy.Should().Be("casey.doyle");
        csa!.UpdatedBy.Should().Be("casey.doyle");
        nettingSet!.UpdatedBy.Should().Be("casey.doyle");
    }

    private static OtcContractDto Contract(Guid securityId)
        => new(Guid.NewGuid(), securityId, Guid.NewGuid(), null, OtcContractDirection.Buy, 1_000_000m, "USD", Trade, Trade, null, OtcContractStatus.Pending, null, null, 1, "casey.doyle", DateTimeOffset.UtcNow);

    private static SecurityDetailDto Detail(Guid securityId)
        => new(
            securityId,
            "Swap",
            SecurityStatusDto.Active,
            "Test",
            "USD",
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }),
            [],
            [],
            1,
            DateTimeOffset.UtcNow.AddDays(-1),
            null);

    private static async Task<WebApplication> CreateAppAsync(ISecurityMasterQueryService queryService, IOtcContractStore store)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(store);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ModifySecurityMaster;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "casey.doyle";
            await next();
        });
        app.MapSecurityMasterEndpoints(Json);

        await app.StartAsync();
        return app;
    }
}
