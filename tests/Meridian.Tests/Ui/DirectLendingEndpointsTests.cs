using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Identity.Auth;
using Meridian.Contracts.DirectLending;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

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

        var penaltyResponse = await client.PostAsJsonAsync(
            $"/api/loans/{created.LoanId}/prepayment-penalties",
            new ChargePrepaymentPenaltyRequest(200_000m, new DateOnly(2026, 3, 24), "prepay-rebuild"));
        penaltyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

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
        projectedRevisions!.Should().HaveCount(3);

        var projectedAccruals = await client.GetFromJsonAsync<List<DailyAccrualEntryDto>>($"/api/loans/{created.LoanId}/projections/accruals");
        projectedAccruals.Should().NotBeNull();
        projectedAccruals!.Should().ContainSingle();

        var history = await client.GetFromJsonAsync<List<LoanEventLineageDto>>($"/api/loans/{created.LoanId}/history");
        history.Should().NotBeNull();
        history!.Should().HaveCount(5);
        history.Select(static item => item.EventType).Should().ContainInOrder(
            "loan.created",
            "loan.activated",
            "loan.drawdown-booked",
            "loan.daily-accrual-posted",
            "loan.prepayment-penalty-charged");
        history.All(static item => item.CommandId.HasValue).Should().BeTrue();
        history.All(static item => item.CorrelationId.HasValue).Should().BeTrue();
        history.All(static item => item.ReplayFlag is false).Should().BeTrue();

        var rebuildResponse = await client.PostAsync($"/api/loans/{created.LoanId}/rebuild-state", content: null);
        rebuildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rebuilt = await rebuildResponse.Content.ReadFromJsonAsync<LoanAggregateSnapshotDto>();
        rebuilt.Should().NotBeNull();
        rebuilt!.AggregateVersion.Should().Be(5);
        rebuilt.Servicing.Balances.PenaltyAccruedUnpaid.Should().Be(4_000m);
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
    public async Task DirectLendingEndpoints_ShouldIgnoreIngressReplayFlagMetadataInHistory()
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
                ReplayFlag: true));

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
        history[0].ReplayFlag.Should().BeFalse();
    }

    [Fact]
    public async Task DirectLendingEndpoints_ShouldExposeCollateralStatusPikRestructureAndAmortizationParity()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        });

        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync("/api/loans", BuildCreateRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<LoanContractDetailDto>();
        created.Should().NotBeNull();

        var loanId = created!.LoanId;
        (await client.PostAsJsonAsync($"/api/loans/{loanId}/activate", new ActivateLoanRequest(new DateOnly(2026, 3, 22))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/drawdowns",
            new BookDrawdownRequest(400_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-10")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var collateralResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/collateral",
            new AddCollateralRequest(CollateralType.RealEstate, "Phoenix industrial property", 650_000m, CurrencyCode.USD, new DateOnly(2026, 4, 1)));
        collateralResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var collateralState = await collateralResponse.Content.ReadFromJsonAsync<LoanServicingStateDto>();
        collateralState.Should().NotBeNull();
        var collateral = collateralState!.Collateral.Should().ContainSingle().Subject;

        var collateralList = await client.GetFromJsonAsync<List<CollateralDto>>($"/api/loans/{loanId}/collateral");
        collateralList.Should().NotBeNull();
        collateralList!.Should().ContainSingle(item => item.CollateralId == collateral.CollateralId);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/loans/{loanId}/collateral/value",
            new UpdateCollateralValueRequest(collateral.CollateralId, 700_000m, new DateOnly(2026, 5, 1)));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<LoanServicingStateDto>();
        updated!.Collateral!.Single().EstimatedValue.Should().Be(700_000m);

        var statusResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/status-transitions",
            new TransitionLoanStatusRequest(LoanStatus.Workout, "Covenant breach notice", new DateOnly(2026, 5, 2)));
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await statusResponse.Content.ReadFromJsonAsync<LoanServicingStateDto>())!.Status.Should().Be(LoanStatus.Workout);

        var pikResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/pik",
            new TogglePikRequest(true, new DateOnly(2026, 5, 3), "Borrower cash sweep approved"));
        pikResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pikResponse.Content.ReadFromJsonAsync<LoanServicingStateDto>())!.IsPikToggled.Should().BeTrue();

        var restructureResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/restructures",
            new RestructureLoanRequest(
                RestructuringType.MaturityExtension,
                "Approved workout extension",
                new DateOnly(2026, 5, 4),
                created.CurrentTerms with { MaturityDate = new DateOnly(2030, 3, 22) }));
        restructureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await restructureResponse.Content.ReadFromJsonAsync<LoanContractDetailDto>())!
            .CurrentTerms.MaturityDate.Should().Be(new DateOnly(2030, 3, 22));

        var amortizationResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/amortization/discount-premium",
            new AmortizeDiscountPremiumRequest(new DateOnly(2026, 5, 5)));
        amortizationResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var operations = await client.GetFromJsonAsync<DirectLendingOperationsReadModelDto>("/api/loans/operations");
        operations.Should().NotBeNull();
        operations!.CollateralCoverage.Should().ContainSingle(row => row.LoanId == loanId && row.CollateralValue == 700_000m);
        operations.CovenantStatusPosture.Should().ContainSingle(row => row.LoanId == loanId && row.PikEnabled);
    }

    [Fact]
    public async Task DirectLendingOperationsReadService_ShouldScopeJournalEvidenceToSelectedLedgerBook()
    {
        var selectedLedgerBookId = Guid.NewGuid();
        var otherLedgerBookId = Guid.NewGuid();
        var loanId = Guid.NewGuid();
        var borrower = new BorrowerInfoDto(Guid.NewGuid(), "Contoso Borrower LLC", Guid.NewGuid());
        var terms = BuildCreateRequest() with { LoanId = loanId, Borrower = borrower };
        var summary = new LoanSummaryDto(
            loanId,
            terms.FacilityName,
            borrower.BorrowerId,
            borrower.BorrowerName,
            LoanStatus.Active,
            terms.Terms.BaseCurrency,
            terms.Terms.CommitmentAmount,
            100_000m,
            25m,
            0m,
            900_000m,
            terms.Terms.OriginationDate,
            terms.Terms.MaturityDate,
            new DateOnly(2026, 3, 24),
            null);
        var portfolio = new LoanPortfolioSummaryDto(
            1,
            1,
            0,
            0,
            0,
            summary.CommitmentAmount,
            summary.PrincipalOutstanding,
            summary.InterestAccruedUnpaid,
            summary.PenaltyAccruedUnpaid,
            summary.AvailableToDraw,
            0m,
            [summary]);
        var contract = new LoanContractDetailDto(
            loanId,
            terms.FacilityName,
            borrower,
            LoanStatus.Active,
            terms.EffectiveDate,
            terms.EffectiveDate,
            null,
            1,
            terms.Terms,
            [new LoanTermsVersionDto(1, "terms-hash", terms.Terms, "create", null, DateTimeOffset.UtcNow)]);
        var servicing = new LoanServicingStateDto(
            loanId,
            LoanStatus.Active,
            terms.Terms.CommitmentAmount,
            100_000m,
            900_000m,
            new OutstandingBalancesDto(100_000m, 25m, 0m, 0m, 0m),
            [],
            null,
            new DateOnly(2026, 3, 24),
            null,
            1,
            [],
            [],
            []);
        var journals = new[]
        {
            BuildJournal(loanId, selectedLedgerBookId, JournalEntryStatus.Posted),
            BuildJournal(loanId, otherLedgerBookId, JournalEntryStatus.Draft)
        };

        var directLending = Substitute.For<IDirectLendingService>();
        directLending.GetPortfolioSummaryAsync(Arg.Any<CancellationToken>()).Returns(portfolio);
        directLending.GetLoanAsync(loanId, Arg.Any<CancellationToken>()).Returns(contract);
        directLending.GetServicingStateAsync(loanId, Arg.Any<CancellationToken>()).Returns(servicing);
        directLending.GetJournalsAsync(loanId, Arg.Any<CancellationToken>()).Returns(journals);
        directLending.GetReconciliationRunsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        directLending.GetProjectionsAsync(loanId, Arg.Any<CancellationToken>()).Returns([]);
        directLending.GetReconciliationExceptionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var service = new DirectLendingOperationsReadService(directLending);

        var selected = await service.GetOperationsAsync(selectedLedgerBookId);
        var mismatched = await service.GetOperationsAsync(Guid.NewGuid());

        selected.LedgerBookId.Should().Be(selectedLedgerBookId);
        selected.Journals.Should().ContainSingle(row =>
            row.LoanId == loanId &&
            row.PostedCount == 1 &&
            row.DraftCount == 0);
        selected.Evidence.Should().ContainSingle(row =>
            row.LoanId == loanId &&
            row.EvidenceType == "Journals" &&
            row.Label == "1 journal(s)");
        selected.CloseBlockers.Should().NotContain(row => row.BlockerType == "LedgerBookScope");

        mismatched.Journals.Should().ContainSingle(row =>
            row.LoanId == loanId &&
            row.PostedCount == 0 &&
            row.DraftCount == 0);
        mismatched.CloseBlockers.Should().ContainSingle(row =>
            row.LoanId == loanId &&
            row.BlockerType == "LedgerBookScope" &&
            row.Severity == "Critical");
    }

    [Fact]
    public async Task DirectLendingEndpoints_ShouldPreviewImportAndApplyServicerStatementRows()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
            services.AddSingleton<IDirectLendingServicerStatementService, DirectLendingServicerStatementService>();
        });

        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync("/api/loans", BuildCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<LoanContractDetailDto>();
        created.Should().NotBeNull();
        var loanId = created!.LoanId;

        (await client.PostAsJsonAsync($"/api/loans/{loanId}/activate", new ActivateLoanRequest(new DateOnly(2026, 3, 22))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/loans/{loanId}/drawdowns", new BookDrawdownRequest(100_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-servicer")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var request = new ServicerStatementImportRequestDto(
            ServicerStatementKind.Remittance,
            "Northwind Servicer",
            new DateOnly(2026, 4, 30),
            "csv",
            "remittance.csv",
            "rowId,kind,loanId,statementDate,effectiveDate,grossAmount,principalAmount,interestAmount,feeAmount,penaltyAmount,currency,externalRef,applyMode\n" +
            $"REM-1,Remittance,{loanId:D},2026-04-30,2026-04-30,10500.00,10000.00,500.00,0.00,0.00,USD,remit-1,ApplyMixedPayment",
            ImportedBy: "ops-user",
            FundAccountId: "fund-direct-lending",
            CashAccountId: "cash-operating");

        var previewResponse = await client.PostAsJsonAsync("/api/loans/servicer-statements/preview", request);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await previewResponse.Content.ReadFromJsonAsync<ServicerStatementPreviewDto>();
        preview.Should().NotBeNull();
        preview!.ReadyToApplyRowCount.Should().Be(1);
        preview.Issues.Should().NotContain(issue => issue.Severity == "Error");

        var importResponse = await client.PostAsJsonAsync("/api/loans/servicer-statements/import", request);
        importResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var imported = await importResponse.Content.ReadFromJsonAsync<ServicerStatementImportResultDto>();
        imported.Should().NotBeNull();
        imported!.Preview.Rows.Should().ContainSingle(row => row.Status == ServicerStatementRowStatus.ReadyToApply);

        var applyResponse = await client.PostAsJsonAsync(
            $"/api/loans/servicer-statements/{imported.BatchId:D}/apply",
            new ServicerStatementApplyRequestDto(["REM-1"], ServicerStatementApplyMode.ApplyMixedPayment, "ops-user", "Reviewed servicer remittance."));
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = await applyResponse.Content.ReadFromJsonAsync<ServicerStatementApplyResultDto>();
        applied.Should().NotBeNull();
        applied!.AppliedRowCount.Should().Be(1);

        var servicing = await client.GetFromJsonAsync<LoanServicingStateDto>($"/api/loans/{loanId}/servicing-state");
        servicing.Should().NotBeNull();
        servicing!.Balances.PrincipalOutstanding.Should().Be(90_000m);

        var operations = await client.GetFromJsonAsync<DirectLendingOperationsReadModelDto>("/api/loans/operations");
        operations.Should().NotBeNull();
        operations!.ServicerStatements.Should().ContainSingle(row =>
            row.BatchId == imported.BatchId &&
            row.AppliedRowCount == 1 &&
            row.EvidenceRoute == $"/api/loans/servicer-statements/{imported.BatchId:D}");
    }

    [Fact]
    public async Task DirectLendingEndpoints_PortfolioRequiresDirectLendingViewOrManagePermission()
    {
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>(),
            UserPermission.None);

        var response = await app.GetTestClient().GetAsync("/api/loans/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ViewDirectLending | UserPermission.ManageDirectLending)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices(builder.Services);
        builder.Services.AddSingleton<DirectLendingOperationsReadService>();

        var app = builder.Build();

        // SEC-001: direct-lending routes now require ViewDirectLending/ManageDirectLending. This
        // standalone host has no LoginSessionMiddleware, so seed the manage permission directly into
        // the request context (the same context.Items injection used by the production middleware)
        // to keep these lifecycle tests exercising the handlers.
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "integration-user";
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Admin;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                permissions;
            await next();
        });

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
                CovenantsJson: "{\"interestCoverage\": \">= 2.0x\"}",
                PrepaymentPenaltyRate: 0.02m));

    private static JournalEntryDto BuildJournal(
        Guid loanId,
        Guid ledgerBookId,
        JournalEntryStatus status)
    {
        var dimensionsJson = JsonSerializer.Serialize(
            new { ledgerBookId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new JournalEntryDto(
            Guid.NewGuid(),
            loanId,
            new DateOnly(2026, 3, 24),
            new DateOnly(2026, 3, 24),
            Guid.NewGuid(),
            "loan.drawdown-booked",
            "Primary",
            "Drawdown funding",
            DateTimeOffset.UtcNow,
            status == JournalEntryStatus.Posted ? DateTimeOffset.UtcNow : null,
            status,
            [
                new JournalLineDto(Guid.NewGuid(), 1, "LoanPrincipal", 100_000m, 0m, CurrencyCode.USD, dimensionsJson),
                new JournalLineDto(Guid.NewGuid(), 2, "Cash", 0m, 100_000m, CurrencyCode.USD, dimensionsJson)
            ]);
    }
}
