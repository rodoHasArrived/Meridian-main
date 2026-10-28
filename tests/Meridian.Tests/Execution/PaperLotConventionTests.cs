using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Execution;

public sealed class PaperLotConventionTests
{
    [Theory]
    [InlineData("InvestmentFund", SecurityLotUnitKind.Units)]
    [InlineData("MoneyMarketFund", SecurityLotUnitKind.Units)]
    [InlineData("Option", SecurityLotUnitKind.Contracts)]
    public async Task KnownSecurity_FillAndReplayPreserveLotDenomination(string assetClass, SecurityLotUnitKind expectedUnit)
    {
        var identity = Substitute.For<ISecurityIdentityQueryService>();
        var now = DateTimeOffset.UtcNow;
        var detail = new SecurityDetailDto(Guid.NewGuid(), assetClass, SecurityStatusDto.Active,
            "Known instrument", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { multiplier = 1m }), [], [], 1, now, null);
        identity.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "KNOWN", null,
            Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>()).Returns(detail);
        await using var gateway = new Meridian.Execution.PaperTradingGateway(
            NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
            securityMaster: (ISecurityTradingParametersQueryService?)null, securityIdentity: identity);
        var report = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "KNOWN",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 2m,
            LimitPrice = 10m
        });

        report.LotUnitKind.Should().Be(expectedUnit);
        report.LotMultiplier.Should().Be(1m);
        // Session replay has no current security definition or OMS sidecar.
        var replayed = JsonSerializer.Deserialize<ExecutionReport>(JsonSerializer.Serialize(report))!;
        var portfolio = new PaperTradingPortfolio(100m);
        portfolio.ApplyFill(replayed);
        var account = (AccountState)portfolio.GetAccount(PaperTradingPortfolio.DefaultAccountId)!;
        var lot = account.Positions["KNOWN"].UnitizedLots.Should().ContainSingle().Subject;
        lot.UnitKind.Should().Be(expectedUnit);
        lot.Multiplier.Should().Be(1m);
    }

    [Fact]
    public void ReportClass_PreservesContractsEvenWhenMultiplierIsOne()
    {
        var portfolio = new PaperTradingPortfolio(100m);
        portfolio.ApplyFill(new ExecutionReport
        {
            OrderId = "OPTION",
            Symbol = "OPTION",
            AssetClass = "us_option",
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            Side = OrderSide.Buy,
            FilledQuantity = 2m,
            FillPrice = 10m
        });
        var account = (AccountState)portfolio.GetAccount(PaperTradingPortfolio.DefaultAccountId)!;
        account.Positions["OPTION"].UnitizedLots.Should().ContainSingle().Which.UnitKind
            .Should().Be(SecurityLotUnitKind.Contracts);
    }
}
