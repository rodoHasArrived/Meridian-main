using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    [Fact]
    public async Task GetPositionsAsync_ContractIdsSeparateSameSymbolContractsAndAccountsAndReplaceRepeatedCallbacks()
    {
        var client = CreateAcknowledgingClient();
        client.OnRequestPositions = c =>
        {
            c.RaisePosition(Position("DU1", "AAPL", "OPT", "101", "20261120", "100", 1m));
            c.RaisePosition(Position("DU1", "AAPL", "OPT", "102", "20261120", "110", 2m));
            c.RaisePosition(Position("DU1", "AAPL", "OPT", "103", "20261218", "100", 3m));
            c.RaisePosition(Position("DU2", "AAPL", "OPT", "101", "20261120", "100", 4m));
            // The same conId is one position even if the routing exchange changes.
            c.RaisePosition(Position("du1", "AAPL", "OPT", "101", "20261120", "100", 5m) with { Exchange = "CBOE" });
            c.RaisePosition(Position("DU1", "ES", "FUT", "201", "202612", null, 6m));
            c.RaisePosition(Position("DU1", "ES", "FUT", "202", "202703", null, 7m));
            c.RaisePositionsCompleted();
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var positions = await sut.GetPositionsAsync();

        positions.Should().HaveCount(6);
        positions.Select(p => p.PositionId).Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(6);
        positions.Should().ContainSingle(p => p.PositionId == "DU1:con_id:101" && p.Quantity == 5m);
        positions.Should().ContainSingle(p => p.PositionId == "DU2:con_id:101" && p.Quantity == 4m);
        positions.Where(p => p.Symbol == "ES").Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPositionsAsync_DescriptorFallbackSeparatesStrikesExpiriesRightsAndSecurityTypes()
    {
        var client = CreateAcknowledgingClient();
        client.OnRequestPositions = c =>
        {
            c.RaisePosition(Position("DU1", "AAPL", "OPT", null, "20261120", "100", 1m));
            c.RaisePosition(Position("DU1", "AAPL", "OPT", null, "20261120", "110", 2m));
            c.RaisePosition(Position("DU1", "AAPL", "OPT", null, "20261218", "100", 3m));
            c.RaisePosition(Position("DU1", "AAPL", "OPT", null, "20261120", "100", 4m, "P"));
            c.RaisePosition(Position("DU1", "AAPL", "STK", null, null, null, 5m));
            c.RaisePosition(Position("DU1", "ES", "FUT", null, "202612", null, 6m));
            c.RaisePosition(Position("DU1", "ES", "FUT", null, "202703", null, 7m));
            c.RaisePosition(Position("DU1", "AAPL", "OPT", null, "20261120", "100.00", 8m, "CALL"));
            c.RaisePositionsCompleted();
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var positions = await sut.GetPositionsAsync();

        positions.Should().HaveCount(7);
        positions.Select(p => p.PositionId).Distinct().Should().HaveCount(7);
        positions.Should().ContainSingle(p => p.Quantity == 8m);
        positions.Should().NotContain(p => p.Quantity == 1m);
        positions.Where(p => p.AssetClass == "option").Should().HaveCount(4);
        positions.Where(p => p.AssetClass == "futures").Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPortfolioSnapshotAsync_FiltersAccountWithoutCollapsingItsContracts()
    {
        var client = CreateAcknowledgingClient();
        client.OnRequestAccountSummary = (c, id) =>
        {
            c.RaiseAccountSummary(new IBAccountSummaryUpdate(id, "DU1", "NetLiquidation", "10000", "USD", DateTimeOffset.UtcNow));
            c.RaiseAccountSummaryCompleted(id);
        };
        client.OnRequestPositions = c =>
        {
            c.RaisePosition(Position("DU1", "AAPL", "OPT", "101", "20261120", "100", 1m));
            c.RaisePosition(Position("DU1", "AAPL", "OPT", "102", "20261120", "110", 2m));
            c.RaisePosition(Position("DU2", "AAPL", "OPT", "101", "20261120", "100", 3m));
            c.RaisePositionsCompleted();
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var portfolio = await sut.GetPortfolioSnapshotAsync("DU1");

        portfolio.Positions.Should().HaveCount(2);
        portfolio.Positions.Should().OnlyContain(p => p.PositionId!.StartsWith("DU1:", StringComparison.Ordinal));
        portfolio.Positions.Select(p => p.PositionId).Should().OnlyHaveUniqueItems();
    }

    private static IBPositionUpdate Position(string account, string symbol, string securityType,
        string? contractId, string? expiry, string? strike, decimal quantity, string right = "C")
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (contractId is not null)
            metadata["con_id"] = contractId;
        if (expiry is not null)
            metadata["last_trade_date_or_contract_month"] = expiry;
        if (strike is not null)
        {
            metadata["strike"] = strike;
            metadata["right"] = right;
            metadata["multiplier"] = "100";
        }
        return new IBPositionUpdate(account, symbol, securityType, quantity, 100d,
            "USD", "SMART", metadata, DateTimeOffset.UtcNow);
    }
}
