using FluentAssertions;
using Meridian.Application.SecurityMaster.Valuation;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The valuation service dispatches on the catalog's <c>DefaultValuationModel</c> fact unless the
/// request names a model, returns null for an unknown security and blocks when no model is registered.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityValuationServiceTests
{
    [Fact]
    public async Task Dispatches_OnTheCatalogFact_BondToDiscountedCashFlow_EquityToQuotedPrice()
    {
        var bond = ValuationTestSecurities.Bond();
        var equity = ValuationTestSecurities.Equity();
        var query = Query(bond, equity);
        var (quoted, dcf) = Models();
        var service = new SecurityValuationService(query, [quoted, dcf], NullLogger<SecurityValuationService>.Instance);

        await service.ValueAsync(Request(bond.SecurityId));
        await service.ValueAsync(Request(equity.SecurityId));

        await dcf.Received(1).ValueAsync(Arg.Is<SecurityValuationRequest>(r => r.SecurityId == bond.SecurityId), bond, Arg.Any<CancellationToken>());
        await quoted.Received(1).ValueAsync(Arg.Is<SecurityValuationRequest>(r => r.SecurityId == equity.SecurityId), equity, Arg.Any<CancellationToken>());
        await dcf.DidNotReceive().ValueAsync(Arg.Any<SecurityValuationRequest>(), equity, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheRequest_OverridesTheCatalogDefault()
    {
        var bond = ValuationTestSecurities.Bond();
        var (quoted, dcf) = Models();
        var service = new SecurityValuationService(Query(bond), [quoted, dcf], NullLogger<SecurityValuationService>.Instance);

        await service.ValueAsync(Request(bond.SecurityId) with { Model = SecurityValuationModelKind.QuotedPrice });

        await quoted.Received(1).ValueAsync(Arg.Any<SecurityValuationRequest>(), bond, Arg.Any<CancellationToken>());
        await dcf.DidNotReceive().ValueAsync(Arg.Any<SecurityValuationRequest>(), Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownSecurity_IsNull_AndAMissingModel_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var (quoted, _) = Models();
        var service = new SecurityValuationService(Query(bond), [quoted], NullLogger<SecurityValuationService>.Instance);

        (await service.ValueAsync(Request(Guid.NewGuid()))).Should().BeNull();

        var blocked = await service.ValueAsync(Request(bond.SecurityId));
        blocked.Should().NotBeNull();
        blocked!.IsBlocked.Should().BeTrue();
        blocked.Model.Should().Be(SecurityValuationModelKind.DiscountedCashFlow);
        blocked.Blockers.Should().ContainSingle().Which.Code.Should().Be("NoModel");
        blocked.Price.Should().BeNull();
        blocked.PresentValue.Should().BeNull();
        blocked.MarketValue.Should().BeNull();
    }

    [Fact]
    public void TwoModelsForOneKind_AreACompositionError()
    {
        var (quoted, _) = Models();
        var (another, _) = Models();

        var act = () => new SecurityValuationService(Substitute.For<ISecurityMasterQueryService>(), [quoted, another], NullLogger<SecurityValuationService>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*QuotedPrice*");
    }

    private static SecurityValuationRequest Request(Guid securityId) => new(securityId, ValuationTestSecurities.AsOf);

    private static ISecurityMasterQueryService Query(params SecurityDetailDto[] securities)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SecurityDetailDto?)null);
        foreach (var security in securities)
        {
            query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        }

        return query;
    }

    private static (ISecurityValuationModel Quoted, ISecurityValuationModel DiscountedCashFlow) Models()
    {
        var quoted = Substitute.For<ISecurityValuationModel>();
        quoted.Kind.Returns(SecurityValuationModelKind.QuotedPrice);
        quoted.ModelKey.Returns("quoted-price");
        var dcf = Substitute.For<ISecurityValuationModel>();
        dcf.Kind.Returns(SecurityValuationModelKind.DiscountedCashFlow);
        dcf.ModelKey.Returns("discounted-cash-flow");
        foreach (var model in new[] { quoted, dcf })
        {
            model.ValueAsync(Arg.Any<SecurityValuationRequest>(), Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(SecurityValuationResult.Blocked(
                    call.Arg<SecurityValuationRequest>(), call.Arg<SecurityDetailDto>(), model.Kind, model.ModelKey, "v1", ValuationTestSecurities.AsOf,
                    [new SecurityValuationBlockerDto("Stub", "stub")])));
        }

        return (quoted, dcf);
    }
}
