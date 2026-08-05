using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_TradingIntegration_ShouldPreferOwnedScopedCoveredCallPaperTarget()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserCompanyId: "company-a",
            currentUserTenantId: "tenant-a");
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var startedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

        await store.RecordRunAsync(BuildTradingScopedRun(
            "owned-covered-call-paper",
            "covered-call-overwrite:owned",
            startedAt,
            "tenant-a",
            "company-a"));
        await store.RecordRunAsync(BuildTradingScopedRun(
            "foreign-covered-call-paper",
            "covered-call-overwrite:foreign",
            startedAt.AddMinutes(20),
            "tenant-b",
            "company-b"));
        await store.RecordRunAsync(BuildRun(
            "newer-legacy-paper",
            "legacy-paper-strategy",
            "Legacy Paper Strategy",
            RunType.Paper,
            startedAt.AddMinutes(30)));

        var client = app.GetTestClient();
        var tradingResponse = await client.GetAsync(UiApiRoutes.WorkstationTrading);
        tradingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var trading = await tradingResponse.Content
            .ReadFromJsonAsync<WorkstationTradingPayload>(ServerJsonOptions);

        trading.Should().NotBeNull();
        trading!.DrillIn.Should().NotBeNull();
        trading.DrillIn!.EquityCurve.Should().Contain("owned-covered-call-paper");
        trading.DrillIn.EquityCurve.Should().NotContain("foreign-covered-call-paper");
        trading.DrillIn.EquityCurve.Should().NotContain("newer-legacy-paper");
        trading.Readiness.SnapshotVersion.Split('|')[0]
            .Should().Be("owned-covered-call-paper");

        var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        readiness.Should().NotBeNull();
        readiness!.SnapshotVersion.Split('|')[0]
            .Should().Be("owned-covered-call-paper");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingIntegration_ShouldRejectMissingCompanyScope()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserCompanyId: null,
            currentUserTenantId: "tenant-a");
        var client = app.GetTestClient();

        (await client.GetAsync(UiApiRoutes.WorkstationTrading))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(UiApiRoutes.WorkstationTradingReadiness))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(UiApiRoutes.WorkstationOperatorInbox))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static StrategyRunEntry BuildTradingScopedRun(
        string runId,
        string strategyId,
        DateTimeOffset startedAt,
        string tenantId,
        string companyId)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workstationTenantId"] = tenantId,
            ["workstationCompanyId"] = companyId
        };
        var run = BuildRun(
            runId,
            strategyId,
            "Scoped Covered Call",
            RunType.Paper,
            startedAt);
        return run with
        {
            ParameterSet = parameters,
            InputHashSha256 = StrategyRunEntry.ComputeInputHash(
                run.StrategyId,
                run.StrategyName,
                run.RunType,
                run.DatasetReference,
                run.FeedReference,
                run.Engine,
                parameters,
                run.ParentRunId,
                run.PortfolioId,
                run.LedgerReference,
                run.AuditReference,
                run.FundProfileId)
        };
    }
}
