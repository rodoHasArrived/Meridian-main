using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Instruments.FixedIncome;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondWorkflowEndpointsTests
{
    internal static readonly JsonSerializerOptions Transport = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task WorkflowRoutePreservesEveryDecimalDigitAndBindsRouteSecurity()
    {
        const decimal exact = 999999999999999999.123456789m;
        var security = Guid.NewGuid();
        var service = Substitute.For<IBondWorkflowService>();
        service.CalculateAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<BondWorkflowResult?>(Echo(call.ArgAt<BondWorkflowRequest>(0))));
        await using var app = await AppAsync(service);
        var request = new BondWorkflowRequest(Guid.Empty, Context(), exact, 100m, Fees: 0.123456789m);
        using var response = await app.GetTestClient().PostAsJsonAsync($"/api/reference-data/bonds/{security:D}/workflow", request, Transport);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("originalFace").ValueKind.Should().Be(JsonValueKind.String);
        json.RootElement.GetProperty("originalFace").GetString().Should().Be("999999999999999999.123456789");
        json.RootElement.GetProperty("fees").GetString().Should().Be("0.123456789");
        await service.Received(1).CalculateAsync(Arg.Is<BondWorkflowRequest>(row => row.SecurityId == security
            && row.OriginalFace == exact && row.Fees == 0.123456789m), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("different-security", HttpStatusCode.BadRequest)]
    [InlineData("account-pricing", HttpStatusCode.Forbidden)]
    [InlineData("missing-actor", HttpStatusCode.Unauthorized)]
    [InlineData("missing-tenant", HttpStatusCode.Forbidden)]
    [InlineData("permission", HttpStatusCode.Forbidden)]
    public async Task GlobalWorkflowRejectsConflictingIdentityOrUnauthorizedScopeBeforeCalculation(string problem, HttpStatusCode expected)
    {
        var service = Substitute.For<IBondWorkflowService>();
        var security = Guid.NewGuid();
        await using var app = await AppAsync(service, actor: problem == "missing-actor" ? null : "operator",
            tenant: problem == "missing-tenant" ? null : "tenant",
            permissions: problem == "permission" ? UserPermission.ViewMarketData : UserPermission.ViewSecurityMaster);
        var request = new BondWorkflowRequest(problem == "different-security" ? Guid.NewGuid() : security, Context())
        { AccountId = problem == "account-pricing" ? "foreign-account" : null };
        using var response = await app.GetTestClient().PostAsJsonAsync($"/api/reference-data/bonds/{security:D}/workflow", request, Transport);
        response.StatusCode.Should().Be(expected);
        await service.DidNotReceiveWithAnyArgs().CalculateAsync(default!, default);
    }

    [Theory]
    [InlineData("Fixed")]
    [InlineData("ZeroCoupon")]
    public async Task ActualWorkflowServiceRunsThroughHttpAndReturnsExactCashStrings(string coupon)
    {
        var security = Guid.NewGuid();
        var context = Context();
        await using var app = await AppAsync(RealService(security, context.AsOf, coupon));
        using var response = await app.GetTestClient().PostAsJsonAsync($"/api/reference-data/bonds/{security:D}/workflow",
            new BondWorkflowRequest(security, context, 1234.56m, 100m), Transport);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BondWorkflowResult>(Transport);
        result!.IsBlocked.Should().BeFalse();
        result.CashFlows.Sum(row => row.Principal).Should().Be(1234.56m);
        result.CashFlows.Sum(row => row.Interest).Should().Be(coupon == "ZeroCoupon" ? 0m : 123.456m);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("cashFlows")[0].GetProperty("principal").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Theory]
    [InlineData("Fixed", 50)]
    [InlineData("ZeroCoupon", 0)]
    [InlineData("SimpleFloater", 35)]
    [InlineData("Inflation", 55)]
    [InlineData("Amortizer", 80)]
    [InlineData("SignedCoupon", -20)]
    [InlineData("Overnight", null)]
    public async Task AllRequestedFamiliesUseRealWorkflowMathThroughHttpWithRetainedTraces(string family, int? interest)
    {
        var security = Guid.NewGuid();
        var context = Context();
        await using var app = await AppAsync(BondFamilyAcceptance.Service(security, context.AsOf, family));
        using var response = await app.GetTestClient().PostAsJsonAsync($"/api/reference-data/bonds/{security:D}/workflow",
            new BondWorkflowRequest(security, context, 1000m, 100m), Transport);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<BondWorkflowResult>(Transport))!;
        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(row => row.Reason)));
        result.CashFlows.Sum(row => row.Principal).Should().Be(family == "Inflation" ? 1100m : 1000m);
        if (interest is { } expected)
            result.CashFlows.Sum(row => row.Interest).Should().Be(expected);
        result.Inputs.Should().Contain(row => row.Kind == "SecurityTerms" && row.Version == 9 && row.ContentSha256 != null);
        result.PresentValue.Should().BeNull();
        result.Issues.Should().Contain(row => row.Code == "DiscountCurveMissing" && !row.BlocksCalculation);
        if (family == "Amortizer")
            result.CashFlows.Where(row => row.Principal != 0m).Select(row => row.Principal).Should().Equal(400m, 600m);
        if (family == "SimpleFloater")
        {
            result.CashFlows.SelectMany(row => row.Observations).Should().Contain(row => row.IsObserved);
            result.CashFlows.SelectMany(row => row.Observations).Should().Contain(row => !row.IsObserved);
            result.Inputs.Should().Contain(row => row.Kind == "Curve:Forward:SOFR");
        }
        if (family == "Overnight")
        {
            var observations = result.CashFlows.SelectMany(row => row.Observations).ToArray();
            observations.Select(row => row.Days).Should().Equal(new[] { 4, 1, 1, 1 }, "the synthetic Friday holiday extends Thursday's observed fixing across the weekend");
            var overnightExpected = 1000m * ((1m + .03m * 4m / 360m) * (1m + .02m / 360m)
                * (1m + .02m / 360m) * (1m + .02m / 360m) - 1m + .01m * 7m / 360m);
            result.CashFlows.Sum(row => row.Interest).Should().BeApproximately(overnightExpected, .000000001m);
            result.Inputs.Should().Contain(row => row.Kind == "FixingCalendar" && row.ContentSha256 != null);
        }
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("cashFlows")[0].GetProperty("interest").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task RealFloaterHttpRefusesMissingForwardPopulationInsteadOfInventingFutureCoupons()
    {
        var security = Guid.NewGuid();
        var context = Context();
        await using var app = await AppAsync(BondFamilyAcceptance.Service(security, context.AsOf, "SimpleFloater", missingForward: true));
        using var response = await app.GetTestClient().PostAsJsonAsync($"/api/reference-data/bonds/{security:D}/workflow",
            new BondWorkflowRequest(security, context, 1000m), Transport);
        var result = (await response.Content.ReadFromJsonAsync<BondWorkflowResult>(Transport))!;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsBlocked.Should().BeTrue();
        result.CashFlows.Should().BeEmpty();
        result.Issues.Should().Contain(row => row.BlocksCalculation && row.Reason.Contains("ForwardCurveMissing"));
    }

    internal static BondCalculationContext Context() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 1, 1), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    internal static BondWorkflowResult Echo(BondWorkflowRequest request) => new(request.SecurityId, "USD", "synthetic-verified",
        request.Context, request.OriginalFace, request.OriginalFace, 0m, null, null, request.Fees, null, null, null, null,
        null, null, null, [], [], [], []);

    internal static BondWorkflowService RealService(Guid id, DateTimeOffset asOf, string coupon = "Fixed")
    {
        var date = DateOnly.FromDateTime(asOf.UtcDateTime);
        var security = new SecurityDetailDto(id, "Bond", SecurityStatusDto.Active, "Synthetic endpoint bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2,
                issueDate = date.ToString("yyyy-MM-dd"),
                maturity = date.AddYears(2).ToString("yyyy-MM-dd"),
                couponType = coupon,
                couponRate = 5m,
                dayCount = "30/360",
                par = 100m,
                paymentFrequency = "Annual",
                isCallable = false,
                scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
            }), [], [], 9, asOf, null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(security);
        query.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(security);
        return new(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
    }

    private static async Task<WebApplication> AppAsync(IBondWorkflowService service,
        string? actor = "operator", string? tenant = "tenant", UserPermission permissions = UserPermission.ViewSecurityMaster)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (actor is not null)
                context.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (tenant is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenant;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = tenant;
            }
            await next();
        });
        app.MapBondWorkflowEndpoints();
        await app.StartAsync();
        return app;
    }
}
