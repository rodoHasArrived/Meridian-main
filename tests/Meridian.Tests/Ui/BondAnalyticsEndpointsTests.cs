using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Instruments.FixedIncome;
using Meridian.Tests.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondAnalyticsEndpointsTests
{
    private static readonly Guid SyntheticSecurityId = Guid.Parse("5d1e7c3a-9b2f-4e6a-8c1d-3f4a5b6c7d8e");
    private static readonly Guid SyntheticCurveVersionId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public async Task Analytics_BindsExplicitContextAndFractionalYield()
    {
        var service = Substitute.For<IBondAnalyticsService>();
        await using var app = await CreateAppAsync(service);
        var securityId = Guid.NewGuid();
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{securityId}/analytics?asOf=2026-10-29T00:00:00Z&settlementDate=2026-10-30&knownAt=2026-10-28T00:00:00Z&yield=0.05&yieldConvention=NominalPeriodic");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await service.Received(1).CalculateAsync(Arg.Is<BondAnalyticsRequest>(request =>
            request.SecurityId == securityId && request.Yield == 0.05m && request.CleanPrice == null
            && request.YieldConvention == BondYieldConvention.NominalPeriodic
            && request.Context.SettlementDate == new DateOnly(2026, 10, 30)
            && request.Context.KnownAt == new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("cleanPrice=100&yield=0.05")]
    [InlineData("cleanPrice=100&yieldConvention=999")]
    [InlineData("cleanPrice=100&yieldConvention=0")]
    [InlineData("yieldConvention=NominalPeriodic")]
    public async Task Analytics_RejectsAmbiguousOrInvalidInputs(string query)
    {
        var service = Substitute.For<IBondAnalyticsService>();
        await using var app = await CreateAppAsync(service);
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{Guid.NewGuid()}/analytics?asOf=2026-10-29T00:00:00Z&{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await service.DidNotReceiveWithAnyArgs().CalculateAsync(default!, default);
    }

    [Fact]
    public async Task Analytics_RefusesCallerWithoutSecurityMasterPermission()
    {
        var service = Substitute.For<IBondAnalyticsService>();
        await using var app = await CreateAppAsync(service, UserPermission.ViewMarketData);
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{Guid.NewGuid()}/analytics?asOf=2026-10-29T00:00:00Z&cleanPrice=100");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await service.DidNotReceiveWithAnyArgs().CalculateAsync(default!, default);
    }

    [Fact]
    public async Task Analytics_V3PricesSyntheticSofrThroughTheRealService_AndSerializesFixingEvidence()
    {
        await using var app = await CreateAppAsync(SyntheticService());
        var response = await app.GetTestClient().GetAsync(SyntheticUrl("bond-analytics-v3"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BondAnalyticsResult>())!;
        Assert.False(result.IsBlocked, string.Join(" ", result.Blockers.Select(blocker => blocker.Reason)));
        Assert.Equal("bond-analytics-v3", result.ModelVersion);
        Assert.Equal(7, result.Context.SecurityVersion);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(4, result.CashFlows.Count);
        Assert.Equal(0.03m, result.YieldToMaturity);
        Assert.Equal(2m, result.CashFlows[0].Interest);
        Assert.Equal(new BondAnalyticsFloatingCoupon(new DateOnly(2026, 1, 15), true, "SOFR",
            0.03m, 100m, 0.04m, "Retained reset schedule"), result.CashFlows[0].FloatingCoupon);
        Assert.All(result.CashFlows.Skip(1), flow =>
        {
            Assert.Equal(1.5m, flow.Interest);
            Assert.Equal(new BondAnalyticsFloatingCoupon(flow.AccrualStart, false, "SOFR",
                0.02m, 100m, 0.03m, "Synthetic SOFR forward curve"), flow.FloatingCoupon);
        });
        Assert.Equal(100m, result.CashFlows[^1].Principal);
        Assert.InRange(result.AccruedInterest!.Value, 1m / 3m - 0.000000000001m, 1m / 3m + 0.000000000001m);
        Assert.True(result.Dv01 > 0m);
        Assert.True(result.SpreadDv01 > 0m);
        Assert.Contains(result.Inputs, input => input.Kind == "ForwardCurveVersion" && input.VersionId == SyntheticCurveVersionId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("not a discount margin", StringComparison.Ordinal));

        // Independently price the four known coupons and principal at 3% nominal semi-annual yield.
        var expectedDirty = new[] { 2m, 1.5m, 1.5m, 101.5m }
            .Select((cash, index) => cash / (decimal)Math.Pow(1.015, 2d * (5d / 12d + index / 2d))).Sum();
        Assert.InRange(result.DirtyPrice!.Value, expectedDirty - 0.00000001m, expectedDirty + 0.00000001m);
        var expectedSpreadDv01 = Enumerable.Range(0, 4)
            .Sum(index => 0.005m / (decimal)Math.Pow(1.015, 2d * (5d / 12d + index / 2d)));
        Assert.InRange(result.SpreadDv01!.Value, expectedSpreadDv01 - 0.00000001m, expectedSpreadDv01 + 0.00000001m);
    }

    [Theory]
    [InlineData(false, true, "MissingFixing")]
    [InlineData(true, false, "ForwardCurveRequired")]
    public async Task Analytics_V3ReturnsMissingEvidenceRefusals_WithoutMetricsOrFallback(
        bool includeFixing, bool includeCurve, string blocker)
    {
        await using var app = await CreateAppAsync(SyntheticService(includeFixing: includeFixing, includeCurve: includeCurve));
        var response = await app.GetTestClient().GetAsync(SyntheticUrl("bond-analytics-v3"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BondAnalyticsResult>())!;
        Assert.Equal("bond-analytics-v3", result.ModelVersion);
        Assert.Equal(blocker, Assert.Single(result.Blockers).Code);
        Assert.Empty(result.CashFlows);
        Assert.Null(result.CleanPrice);
        Assert.Null(result.DirtyPrice);
        Assert.Null(result.AccruedInterest);
        Assert.Null(result.YieldToMaturity);
        Assert.Null(result.Dv01);
        Assert.Null(result.SpreadDv01);
    }

    [Theory]
    [InlineData(null, "Fixed")]
    [InlineData(null, "ZeroCoupon")]
    [InlineData("bond-analytics-v1", "Fixed")]
    [InlineData("bond-analytics-v1", "ZeroCoupon")]
    [InlineData("bond-analytics-v2", "Fixed")]
    [InlineData("bond-analytics-v2", "ZeroCoupon")]
    public async Task Analytics_FixedAndZeroCouponsKeepTheirSelectedOrDefaultModel_WithoutFloatingFields(
        string? version, string couponType)
    {
        await using var app = await CreateAppAsync(SyntheticService(couponType));
        var response = await app.GetTestClient().GetAsync(SyntheticUrl(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BondAnalyticsResult>())!;
        Assert.False(result.IsBlocked, string.Join(" ", result.Blockers.Select(blocker => blocker.Reason)));
        Assert.Equal(version ?? "bond-analytics-v2", result.ModelVersion);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("spreadDv01", out _));
        Assert.All(json.RootElement.GetProperty("cashFlows").EnumerateArray(), flow =>
            Assert.False(flow.TryGetProperty("floatingCoupon", out _)));
    }

    [Theory]
    [InlineData("bond-analytics-v3", "Fixed", "UnsupportedCouponStructure")]
    [InlineData("bond-analytics-v3", "ZeroCoupon", "UnsupportedCouponStructure")]
    [InlineData("bond-analytics-v1", "Floating", "UnsupportedCouponStructure")]
    [InlineData("bond-analytics-v2", "Floating", "UnsupportedCouponStructure")]
    [InlineData("bond-analytics-v999", "Fixed", "UnsupportedCalculationVersion")]
    public async Task Analytics_RefusesAnIncompatibleOrUnknownExplicitModel(string version, string couponType, string blocker)
    {
        await using var app = await CreateAppAsync(SyntheticService(couponType));
        var response = await app.GetTestClient().GetAsync(SyntheticUrl(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BondAnalyticsResult>())!;
        Assert.Equal(blocker, Assert.Single(result.Blockers).Code);
        Assert.Empty(result.CashFlows);
        Assert.Null(result.YieldToMaturity);
        Assert.Null(result.Dv01);
        Assert.Null(result.SpreadDv01);
    }

    private static string SyntheticUrl(string? version) =>
        $"/api/reference-data/bonds/{SyntheticSecurityId}/analytics?asOf=2026-02-15T00:00:00Z&settlementDate=2026-02-15&yield=0.03&yieldConvention=NominalPeriodic"
        + (version is null ? string.Empty : "&calculationVersion=" + version);

    private static BondAnalyticsService SyntheticService(string couponType = "Floating", bool includeFixing = true, bool includeCurve = true)
    {
        var terms = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 2,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponType"] = couponType,
            ["couponRate"] = 0.04m,
            ["floatingIndex"] = "SOFR",
            ["spreadBps"] = 100m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Fraction", couponCashBasis = "Level" }
        };
        if (includeFixing)
            terms["resetSchedule"] = new[] { new { resetDate = "2026-01-15", fixingRate = 0.03m } };
        var security = new SecurityDetailDto(SyntheticSecurityId, "Bond", SecurityStatusDto.Active,
            "Synthetic SOFR + 100 bp", "USD", JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms),
            [], [], 7, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(SyntheticSecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var curves = new InMemorySecurityForwardCurveProvider();
        if (includeCurve)
            curves.Publish(new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1),
                [new(new DateOnly(2026, 1, 1), 0.02m), new(new DateOnly(2029, 1, 1), 0.02m)], Source: "Synthetic SOFR forward curve")
            {
                RetainedInput = new("ForwardCurveVersion", "SYNTHETIC-SOFR", Version: 1, ContentSha256: new string('a', 64),
                    VersionId: SyntheticCurveVersionId, AsOf: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
            });
        return new BondAnalyticsService(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), forwardCurves: curves);
    }

    private static async Task<WebApplication> CreateAppAsync(IBondAnalyticsService service,
        UserPermission permissions = UserPermission.ViewSecurityMaster)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "bond-test-tenant";
            await next();
        });
        app.MapBondAnalyticsEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }
}
