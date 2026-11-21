using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondObservedFixingEndpointsTests
{
    private static readonly Guid SecurityId = Guid.Parse("5d1e7c3a-9b2f-4e6a-8c1d-3f4a5b6c7d8e");
    private static readonly DateOnly ResetDate = new(2026, 1, 15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SecurityRateObservationDto Observation = new("fred", "SOFR", 3m, "3", SecurityRateUnit.Percent,
        "Percent", ResetDate, null, new DateOnly(2026, 1, 16), new DateOnly(9999, 12, 31),
        new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero), "lin", SecurityRateObservationKind.ObservedFixing, "USD");

    [Fact]
    public async Task Retain_VerifiesExactSourceAndBindsSecurityVersionRateAndSessionActor()
    {
        var store = Substitute.For<IBondObservedFixingStore>();
        BondObservedFixingCandidate? captured = null;
        store.RetainAsync(Arg.Do<BondObservedFixingCandidate>(candidate => captured = candidate), Arg.Any<CancellationToken>())
            .Returns(call => new BondObservedFixingReceipt(Guid.NewGuid(), call.Arg<BondObservedFixingCandidate>(), DateTimeOffset.UtcNow, new string('a', 64)));
        await using var app = await CreateAppAsync(store, Observation);
        var request = new RetainBondObservedFixingRequest(7, ResetDate, Observation, "Reviewed contractual reset and official source");
        using var response = await app.GetTestClient().PostAsJsonAsync(Route, request, Json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.SecurityId.Should().Be(SecurityId);
        captured.SecurityVersion.Should().Be(7);
        captured.RateFraction.Should().Be(0.03m);
        captured.RecordedBy.Should().Be("session.operator");
        captured.Observation.Should().Be(Observation);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("fixing").GetProperty("rateFraction").GetString().Should().Be("0.03");
    }

    [Theory]
    [InlineData(false, UserPermission.ModifySecurityMaster)]
    [InlineData(true, UserPermission.ViewSecurityMaster)]
    public async Task Retain_RequiresTenantAndModifyAuthority(bool tenant, UserPermission permission)
    {
        var store = Substitute.For<IBondObservedFixingStore>();
        await using var app = await CreateAppAsync(store, Observation, tenant, permission);
        using var response = await app.GetTestClient().PostAsJsonAsync(Route,
            new RetainBondObservedFixingRequest(7, ResetDate, Observation, "Reviewed"), Json);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await store.DidNotReceive().RetainAsync(Arg.Any<BondObservedFixingCandidate>(), Arg.Any<CancellationToken>());
        if (!tenant)
            (await app.GetTestClient().GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Retain_RefusesStaleTermsEditedQuoteAndOldDateWithoutWriting()
    {
        var store = Substitute.For<IBondObservedFixingStore>();
        await using var app = await CreateAppAsync(store, Observation);
        foreach (var request in new[]
        {
            new RetainBondObservedFixingRequest(6, ResetDate, Observation, "Reviewed"),
            new RetainBondObservedFixingRequest(7, ResetDate, Observation with { Value = 4m, OriginalValue = "4" }, "Reviewed")
        })
            (await app.GetTestClient().PostAsJsonAsync(Route, request, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var old = Observation with { ObservationDate = ResetDate.AddDays(-1) };
        await using var oldSource = await CreateAppAsync(store, old);
        (await oldSource.GetTestClient().PostAsJsonAsync(Route, new RetainBondObservedFixingRequest(7, ResetDate, old, "Reviewed"), Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await store.DidNotReceive().RetainAsync(Arg.Any<BondObservedFixingCandidate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorruptReceipt_ListAndRetentionReturnActionableConflict()
    {
        var store = Substitute.For<IBondObservedFixingStore>();
        store.ListAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<BondObservedFixingReceipt>>(new InvalidDataException("Receipt hash mismatch")));
        store.RetainAsync(Arg.Any<BondObservedFixingCandidate>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BondObservedFixingReceipt>(new InvalidDataException("Receipt identity conflicts with retained evidence")));
        await using var app = await CreateAppAsync(store, Observation);
        using var listed = await app.GetTestClient().GetAsync(Route);
        listed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await listed.Content.ReadAsStringAsync()).Should().Contain("Restore the retained receipts");
        using var retained = await app.GetTestClient().PostAsJsonAsync(Route,
            new RetainBondObservedFixingRequest(7, ResetDate, Observation, "Reviewed source evidence"), Json);
        retained.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await retained.Content.ReadAsStringAsync()).Should().Contain("Reload retained receipts before retrying");
    }

    private static string Route => $"/api/security-master/{SecurityId}/observed-bond-fixings";
    private static async Task<WebApplication> CreateAppAsync(IBondObservedFixingStore store, SecurityRateObservationDto observation,
        bool tenant = true, UserPermission permission = UserPermission.ModifySecurityMaster)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        var terms = JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-15",
            maturity = "2028-01-15",
            couponType = "Floating",
            floatingIndex = "SOFR",
            spreadBps = 100,
            dayCount = "30/360",
            par = 100,
            paymentFrequency = "SemiAnnual",
            scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Fraction", couponCashBasis = "Level" }
        });
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(SecurityId, "Bond", SecurityStatusDto.Active,
            "Floater", "USD", JsonSerializer.SerializeToElement(new { }), terms, [], [], 7, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null));
        var provider = Substitute.For<ISecurityRateObservationProvider>();
        provider.Name.Returns("fred");
        provider.GetRateObservationsAsync("SOFR", Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(new[] { observation });
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(query);
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(provider);
        builder.Services.AddSingleton<IBusinessDayCalendarSource>(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "session.operator";
            if (tenant)
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "fixing-test";
            await next();
        });
        app.MapSecurityMasterEndpoints(Json);
        await app.StartAsync();
        return app;
    }
}
