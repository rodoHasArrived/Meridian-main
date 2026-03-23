using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class DirectLendingEndpointsTests
{
    [Fact]
    public async Task DirectLendingEndpoints_ShouldCreateAndFetchLoanLifecycle()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        });

        var client = app.GetTestClient();
        var createRequest = BuildCreateRequest();

        var createResponse = await client.PostAsJsonAsync("/api/loans", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<LoanContractDetailDto>();
        created.Should().NotBeNull();
        created!.Status.Should().Be(LoanStatus.Draft);

        var activateResponse = await client.PostAsJsonAsync($"/api/loans/{created.LoanId}/activate", new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var drawdownResponse = await client.PostAsJsonAsync(
            $"/api/loans/{created.LoanId}/drawdowns",
            new BookDrawdownRequest(200_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-9"));
        drawdownResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var drawdownState = await drawdownResponse.Content.ReadFromJsonAsync<LoanServicingStateDto>();
        drawdownState.Should().NotBeNull();
        drawdownState!.Balances.PrincipalOutstanding.Should().Be(200_000m);

        var accrualResponse = await client.PostAsJsonAsync(
            $"/api/loans/{created.LoanId}/accruals/daily",
            new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)));
        accrualResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var accrual = await accrualResponse.Content.ReadFromJsonAsync<DailyAccrualEntryDto>();
        accrual.Should().NotBeNull();
        accrual!.InterestAmount.Should().BeApproximately(44.444444444m, 0.000000001m);

        var getLoan = await client.GetFromJsonAsync<LoanContractDetailDto>($"/api/loans/{created.LoanId}");
        getLoan.Should().NotBeNull();
        getLoan!.Status.Should().Be(LoanStatus.Active);

        var contractProjection = await client.GetFromJsonAsync<LoanContractDetailDto>($"/api/loans/{created.LoanId}/projections/contract");
        contractProjection.Should().NotBeNull();
        contractProjection!.CurrentTermsVersion.Should().Be(1);

        var servicing = await client.GetFromJsonAsync<LoanServicingStateDto>($"/api/loans/{created.LoanId}/servicing-state");
        servicing.Should().NotBeNull();
        servicing!.AccrualEntries.Should().ContainSingle();

        var servicingProjection = await client.GetFromJsonAsync<LoanServicingStateDto>($"/api/loans/{created.LoanId}/projections/servicing");
        servicingProjection.Should().NotBeNull();
        servicingProjection!.Balances.PrincipalOutstanding.Should().Be(200_000m);
        servicingProjection.AccrualEntries.Should().ContainSingle();

        var projectedLots = await client.GetFromJsonAsync<List<DrawdownLotDto>>($"/api/loans/{created.LoanId}/projections/drawdown-lots");
        projectedLots.Should().NotBeNull();
        projectedLots!.Should().ContainSingle();
        projectedLots[0].OriginalPrincipal.Should().Be(200_000m);

        var projectedRevisions = await client.GetFromJsonAsync<List<ServicingRevisionDto>>($"/api/loans/{created.LoanId}/projections/revisions");
        projectedRevisions.Should().NotBeNull();
        projectedRevisions!.Should().HaveCount(2);

        var projectedAccruals = await client.GetFromJsonAsync<List<DailyAccrualEntryDto>>($"/api/loans/{created.LoanId}/projections/accruals");
        projectedAccruals.Should().NotBeNull();
        projectedAccruals!.Should().ContainSingle();

        var history = await client.GetFromJsonAsync<List<LoanEventLineageDto>>($"/api/loans/{created.LoanId}/history");
        history.Should().NotBeNull();
        history!.Should().HaveCount(4);
        history.Select(static item => item.EventType).Should().ContainInOrder(
            "loan.created",
            "loan.activated",
            "loan.drawdown-booked",
            "loan.daily-accrual-posted");
        history.All(static item => item.CommandId.HasValue).Should().BeTrue();
        history.All(static item => item.CorrelationId.HasValue).Should().BeTrue();
        history.All(static item => item.ReplayFlag is false).Should().BeTrue();

        var rebuildResponse = await client.PostAsync($"/api/loans/{created.LoanId}/rebuild-state", content: null);
        rebuildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rebuilt = await rebuildResponse.Content.ReadFromJsonAsync<LoanAggregateSnapshotDto>();
        rebuilt.Should().NotBeNull();
        rebuilt!.AggregateVersion.Should().Be(4);
        rebuilt.Servicing.AccrualEntries.Should().ContainSingle();

        var termsVersions = await client.GetFromJsonAsync<List<LoanTermsVersionDto>>($"/api/loans/{created.LoanId}/terms-versions");
        termsVersions.Should().NotBeNull();
        termsVersions!.Should().ContainSingle();

        var termsProjection = await client.GetFromJsonAsync<List<LoanTermsVersionDto>>($"/api/loans/{created.LoanId}/projections/terms-versions");
        termsProjection.Should().NotBeNull();
        termsProjection!.Should().ContainSingle();
    }

    [Fact]
    public async Task DirectLendingEndpoints_ShouldReturnNotFoundForUnknownLoan()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        });

        var client = app.GetTestClient();
        var missingId = Guid.NewGuid();

        var getResponse = await client.GetAsync($"/api/loans/{missingId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var servicingResponse = await client.GetAsync($"/api/loans/{missingId}/servicing-state");
        servicingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DirectLendingEndpoints_ShouldPreserveIngressEnvelopeMetadataInHistory()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        });

        var client = app.GetTestClient();
        var commandId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var createEnvelope = new DirectLendingCommandEnvelope<CreateLoanRequest>(
            BuildCreateRequest(),
            new DirectLendingCommandMetadataDto(
                CommandId: commandId,
                CorrelationId: correlationId,
                CausationId: causationId,
                SourceSystem: "ops-audit-test",
                ReplayFlag: false));

        var createResponse = await client.PostAsJsonAsync("/api/loans", createEnvelope);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<LoanContractDetailDto>();
        created.Should().NotBeNull();

        var history = await client.GetFromJsonAsync<List<LoanEventLineageDto>>($"/api/loans/{created!.LoanId}/history");
        history.Should().NotBeNull();
        history!.Should().ContainSingle();
        history[0].CommandId.Should().Be(commandId);
        history[0].CorrelationId.Should().Be(correlationId);
        history[0].CausationId.Should().Be(causationId);
        history[0].SourceSystem.Should().Be("ops-audit-test");
    }

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection> configureServices)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices(builder.Services);

        var app = builder.Build();
        app.MapDirectLendingEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static CreateLoanRequest BuildCreateRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Contoso Unitranche",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Contoso Borrower LLC", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 3, 22),
                MaturityDate: new DateOnly(2028, 3, 22),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.08m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.03m,
                DefaultRateSpreadBps: 200m,
                PrepaymentAllowed: true,
                CovenantsJson: "{\"interestCoverage\": \">= 2.0x\"}"));
}
