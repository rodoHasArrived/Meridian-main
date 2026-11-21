using System.Net;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage.AssetOperations;
using Meridian.Tests.SecurityMaster;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("scoped-denial")]
    public async Task AssetMarketValuation_RefusesForeignOwnershipAndScopedDenialsBeforePositionOrPriceReads(string refusal)
    {
        var f = new AssetPositionValuationFixture();
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(f.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(f.Book);
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync(f.Book.FundProfileId, Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership(f.Book.FundProfileId,
            refusal == "foreign-tenant" ? "foreign" : "tenant-test", refusal == "foreign-company" ? "foreign" : "tenant-test"));
        var authorization = Substitute.For<IScopedAuthorizationService>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<UserPermission>(), Arg.Any<AccessScopeKindDto>(), Arg.Any<Guid?>(), Arg.Any<UserPermission>(), Arg.Any<CancellationToken>())
            .Returns(new ScopedAuthorizationDecisionDto(false, "ops-user", UserPermission.ViewTrades, AccessScopeKindDto.Account, f.Book.FundStructureNodeId, "Denied"));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books); services.AddSingleton(registry); services.AddSingleton(f.Store);
            services.AddSingleton(f.Query); services.AddSingleton(f.Pricing); services.AddSingleton(authorization);
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityValuationModel>(new Meridian.Application.SecurityMaster.Valuation.QuotedPriceValuationModel(f.Pricing));
        }, currentUserPermissions: UserPermission.ViewTrades);
        var request = f.Request;
        using var response = await app.GetTestClient().GetAsync($"/api/workstation/assets/{request.SecurityId:D}/market-valuation?positionId={request.PositionId:D}&ledgerBookId={request.LedgerBookId:D}&accountId={request.AccountId}&valuationDate={request.ValuationDate:yyyy-MM-dd}&economicStateId={request.EconomicStateId:D}&economicStateVersion={request.EconomicStateVersion}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await f.Store.DidNotReceive().GetAsOfAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await f.Pricing.DidNotReceive().GetGoldenCopyPriceAsOfAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
    }

    [Fact]
    public async Task AssetMarketValuation_ReturnsExactStringAmountsOnlyForTheOwnedAuthorizedScope()
    {
        var f = new AssetPositionValuationFixture();
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(f.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(f.Book);
        var authorization = Substitute.For<IScopedAuthorizationService>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<UserPermission>(), Arg.Any<AccessScopeKindDto>(), Arg.Any<Guid?>(), Arg.Any<UserPermission>(), Arg.Any<CancellationToken>())
            .Returns(new ScopedAuthorizationDecisionDto(true, "ops-user", UserPermission.ViewTrades, AccessScopeKindDto.Account, f.Book.FundStructureNodeId, "Authorized"));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books); services.AddSingleton(f.Store); services.AddSingleton(f.Query);
            services.AddSingleton(f.Pricing); services.AddSingleton(authorization);
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityValuationModel>(new Meridian.Application.SecurityMaster.Valuation.QuotedPriceValuationModel(f.Pricing));
        }, currentUserPermissions: UserPermission.ViewTrades);
        var request = f.Request;
        using var response = await app.GetTestClient().GetAsync($"/api/workstation/assets/{request.SecurityId:D}/market-valuation?positionId={request.PositionId:D}&ledgerBookId={request.LedgerBookId:D}&accountId={request.AccountId}&valuationDate={request.ValuationDate:yyyy-MM-dd}&economicStateId={request.EconomicStateId:D}&economicStateVersion={request.EconomicStateVersion}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().Should().Be("Ready");
        json.RootElement.GetProperty("marketValue").GetString().Should().Be("24.691357802469135780");
        json.RootElement.GetProperty("selectionReceiptId").GetGuid().Should().Be(f.Selection.SelectionReceiptId!.Value);
    }
}
