using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Theory]
    [InlineData("FILL", "qty", "1,25")]
    [InlineData("FILL", "qty", "")]
    [InlineData("FILL", "qty", null)]
    [InlineData("FILL", "price", "invalid")]
    [InlineData("FILL", "price", "79228162514264337593543950336")]
    [InlineData("FILL", "price", null)]
    [InlineData("FILL", "commission", "invalid")]
    [InlineData("FILL", "net_amount", "1,25")]
    [InlineData("DIV", "net_amount", "invalid")]
    [InlineData("DIV", "net_amount", "")]
    [InlineData("DIV", "net_amount", null)]
    [InlineData("CSD", "net_amount", "1,25")]
    public async Task StatementActivity_MalformedAmountsRefuseBeforeTypedSnapshot(
        string type, string field, string? value)
    {
        var activity = new JsonObject
        {
            ["id"] = "amount-1",
            ["activity_type"] = type,
            ["symbol"] = "AAPL",
            ["qty"] = "10",
            ["price"] = "187.25",
            ["net_amount"] = "25",
            ["side"] = "buy",
            ["currency"] = "USD",
            ["commission"] = "0.55",
            ["transaction_time"] = "2026-06-02T14:30:00Z",
            ["date"] = "2026-06-02"
        };
        activity[field] = value;
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request => request.RequestUri!.AbsolutePath switch
        {
            "/v2/account" => BuildAccountResponse(),
            "/v2/account/activities" => BuildJson(new JsonArray(activity.DeepClone())),
            _ => BuildJson(Array.Empty<object>())
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ((IBrokerageActivitySync)sut).GetActivitySnapshotAsync("TEST123"));
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("1,25")]
    [InlineData("invalid")]
    public async Task StatementPortfolio_InvalidPositionAmountCannotClaimCompleteness(string quantity)
    {
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request =>
            request.RequestUri!.AbsolutePath == "/v2/account" ? BuildAccountResponse()
                : BuildPositionsResponse([new { symbol = "AAPL", qty = quantity, avg_entry_price = "180",
                    current_price = "185", market_value = "1850", unrealized_pl = "50", asset_class = "us_equity" }])));

        var snapshot = await ((IBrokeragePortfolioSync)sut).GetPortfolioSnapshotAsync("TEST123");

        snapshot.IsComplete.Should().BeFalse();
        snapshot.CompletenessIssues.Should().Contain(issue => issue.Contains("position quantity"));
    }

    [Theory]
    [InlineData("maintenance_margin")]
    [InlineData("initial_margin")]
    [InlineData("long_market_value")]
    public async Task StatementPortfolio_MalformedOptionalAccountAmountCannotDisappear(string field)
    {
        using var validAccount = BuildAccountResponse();
        var account = JsonNode.Parse(await validAccount.ReadAsStringAsync())!;
        account[field] = "1,25";
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request =>
            request.RequestUri!.AbsolutePath == "/v2/account" ? BuildJson(account)
                : BuildPositionsResponse([])));

        var snapshot = await ((IBrokeragePortfolioSync)sut).GetPortfolioSnapshotAsync("TEST123");

        snapshot.IsComplete.Should().BeFalse();
        snapshot.CompletenessIssues.Should().ContainSingle();
    }

    [Theory]
    [InlineData("unrealized_pl")]
    [InlineData("accrued_interest")]
    public async Task StatementPortfolio_MalformedOptionalPositionAmountCannotDisappear(string field)
    {
        var position = new JsonObject
        {
            ["symbol"] = "AAPL",
            ["qty"] = "10",
            ["avg_entry_price"] = "180",
            ["current_price"] = "185",
            ["market_value"] = "1850",
            ["asset_class"] = "us_equity"
        };
        position[field] = "invalid";
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request =>
            request.RequestUri!.AbsolutePath == "/v2/account" ? BuildAccountResponse()
                : BuildJson(new JsonArray(position.DeepClone()))));

        var snapshot = await ((IBrokeragePortfolioSync)sut).GetPortfolioSnapshotAsync("TEST123");

        snapshot.IsComplete.Should().BeFalse();
        snapshot.CompletenessIssues.Should().ContainSingle();
    }
}
