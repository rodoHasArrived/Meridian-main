using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

/// <summary>HTTP surface for index fixings, scheduled resets, PIK capitalization and the schedule preview.</summary>
[Collection("Sequential")]
public sealed class DirectLendingServicingScheduleEndpointsTests
{
    private static readonly DateOnly Origination = new(2026, 1, 15);

    [Fact]
    public async Task ScheduledResetAndCapitalization_RoundTripOverHttp()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewDirectLending | UserPermission.ManageDirectLending);
        var client = app.GetTestClient();

        var created = await (await client.PostAsJsonAsync("/api/loans", BuildCreateRequest())).Content.ReadFromJsonAsync<LoanContractDetailDto>();
        var loanId = created!.LoanId;
        created.CurrentTerms.RateResetSchedule.Should().NotBeNull();
        (await client.PostAsJsonAsync($"/api/loans/{loanId}/activate", new ActivateLoanRequest(Origination))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/loans/{loanId}/drawdowns", new BookDrawdownRequest(500_000m, Origination, Origination, "wire")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // No fixing yet: the scheduled reset fails closed with 422.
        var missing = await client.PostAsJsonAsync($"/api/loans/{loanId}/rate-resets/scheduled", new ApplyScheduledRateResetRequest(Origination));
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var fixingResponse = await client.PostAsJsonAsync(
            "/api/loans/index-fixings",
            new RecordIndexFixingRequest("SOFR", new DateOnly(2026, 1, 13), DeclaredRate.Percent(4.3m), "feed"));
        fixingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixingResponse.Content.ReadFromJsonAsync<DirectLendingIndexFixingDto>())!.RateFraction.Should().Be(0.043m);

        var fixings = await client.GetFromJsonAsync<List<DirectLendingIndexFixingDto>>("/api/loans/index-fixings?index=sofr&from=2026-01-01&to=2026-01-31");
        fixings.Should().ContainSingle();

        var reset = await client.PostAsJsonAsync($"/api/loans/{loanId}/rate-resets/scheduled", new ApplyScheduledRateResetRequest(Origination));
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var resetState = await reset.Content.ReadFromJsonAsync<LoanServicingStateDto>();
        resetState!.CurrentRateReset!.AllInRate.Should().Be(0.068m);
        resetState.CurrentRateReset.ResetKind.Should().Be(RateResetKind.Scheduled);

        (await client.PostAsJsonAsync($"/api/loans/{loanId}/accruals/daily", new PostDailyAccrualRequest(Origination.AddDays(1))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // 2026-01-16 is not a capitalization date.
        (await client.PostAsJsonAsync($"/api/loans/{loanId}/pik/capitalizations", new CapitalizePikRequest(Origination.AddDays(1))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var preview = await client.GetFromJsonAsync<LoanServicingSchedulePreviewDto>($"/api/loans/{loanId}/schedules/servicing?from=2026-01-01&to=2026-06-30");
        preview!.RateResets.Should().HaveCount(2);
        preview.RateResets[0].Applied.Should().BeTrue();
        preview.RateResets[1].FixingAvailable.Should().BeFalse();
        preview.PikCapitalizationDates.Should().Equal(new DateOnly(2026, 4, 15));
        preview.PikInterestAccruedUnpaid.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task RecordIndexFixing_RequiresManagePermission()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewDirectLending);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/loans/index-fixings",
            new RecordIndexFixingRequest("SOFR", new DateOnly(2026, 1, 13), DeclaredRate.Percent(4.3m), null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RecordIndexFixing_RejectsAValueOutsideItsDeclaredUnitRange()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewDirectLending | UserPermission.ManageDirectLending);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/loans/index-fixings",
            new RecordIndexFixingRequest("SOFR", new DateOnly(2026, 1, 13), DeclaredRate.Percent(4_300m), null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        builder.Services.AddMutationRateLimiter(forceEnable: false);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "integration-user";
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "integration-user")], "test-session"));
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Admin;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.UseRateLimiter();
        app.MapDirectLendingEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }

    private static CreateLoanRequest BuildCreateRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Contoso Unitranche",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Contoso Borrower LLC", null),
            EffectiveDate: Origination,
            Terms: new DirectLendingTermsDto(
                OriginationDate: Origination,
                MaturityDate: new DateOnly(2029, 1, 15),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Floating,
                FixedAnnualRate: null,
                InterestIndexName: "SOFR",
                SpreadBps: 250m,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.Bullet,
                CommitmentFeeRate: null,
                DefaultRateSpreadBps: null,
                PrepaymentAllowed: true,
                CovenantsJson: null)
            {
                RateResetSchedule = new FloatingRateResetSchedule(
                    "SOFR", RateResetFrequency.Quarterly, Origination, 2, LookbackDayKind.Business, DeclaredRate.BasisPoints(250m)),
                PikCapitalization = new PikCapitalizationPolicy(
                    PikElection.Always, DeclaredRate.Percent(50m), PikCapitalizationFrequency.Quarterly, new DateOnly(2026, 4, 15))
            });
}
