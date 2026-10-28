using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.AssetOperations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task FactorPaydownPreparation_UsesTrustedScopeAndExactDecimalStringsWithoutRetainingProjection()
    {
        var fixture = FactorPreparationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var preview = json.RootElement;
        preview.GetProperty("components").GetProperty("lot.principal").GetString().Should().Be("50000.00");
        preview.GetProperty("instruction").GetProperty("scope").GetProperty("tenantId").GetString().Should().Be("company-alpha");
        preview.GetProperty("instruction").GetProperty("scope").GetProperty("companyId").GetString().Should().Be("company-alpha");
        preview.GetProperty("projectRequest").GetProperty("actor").GetString().Should().Be("ops-user");
        preview.GetProperty("preparationFingerprint").GetString().Should().NotBeNullOrWhiteSpace();
        spine.ReceivedCalls().Should().BeEmpty("preparation cannot retain, approve or post an event");
    }

    [Fact]
    public async Task FactorPaydownPreparation_NullEvidenceElementReturnsBadRequestBeforeReadingAuthority()
    {
        var fixture = FactorPreparationFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(FactorPreparationRoute(fixture.Request),
            fixture.Request with { FactorEvidence = [null!] }, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        fixture.Books.ReceivedCalls().Should().BeEmpty();
        fixture.Securities.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("fund", HttpStatusCode.Forbidden)]
    [InlineData("book", HttpStatusCode.BadRequest)]
    [InlineData("version", HttpStatusCode.Conflict)]
    [InlineData("date", HttpStatusCode.Conflict)]
    [InlineData("basis", HttpStatusCode.Conflict)]
    [InlineData("closed", HttpStatusCode.Conflict)]
    public async Task FactorPaydownPreparation_RejectsScopeAndPeriodDriftBeforeReadingSecurity(string scenario, HttpStatusCode expected)
    {
        var fixture = FactorPreparationFixture();
        var request = scenario switch
        {
            "fund" => fixture.Request with { FundProfileId = "foreign-fund" },
            "book" => fixture.Request with { LedgerBookId = Guid.NewGuid() },
            "version" => fixture.Request with { ExpectedPeriodVersion = 2 },
            "date" => fixture.Request with { EffectiveDate = new DateOnly(2026, 7, 1) },
            "basis" => fixture.Request with { AccountingBasis = AccountingBasisKindDto.Tax },
            _ => fixture.Request
        };
        if (scenario == "closed")
            fixture.Periods.GetPeriodAsync(request.PeriodId, Arg.Any<CancellationToken>()).Returns(
                new LedgerAccountingPeriod(request.PeriodId, request.LedgerBookId, 2026, 6, "June", new(2026, 6, 1), new(2026, 6, 30), "HardClosed", DateTimeOffset.UnixEpoch, null, 1));
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(FactorPreparationRoute(fixture.Request), request, ServerJsonOptions);

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        fixture.Securities.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task FactorPaydownPreparation_ReadPermissionCannotPrepare()
    {
        var fixture = FactorPreparationFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Books.ReceivedCalls().Should().BeEmpty();
        fixture.Securities.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task FactorPaydownDraft_ChangedReviewedAccountRefusesBeforeProjection()
    {
        var fixture = FactorPreparationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var fingerprint = preview.RootElement.GetProperty("preparationFingerprint").GetString()!;
        var draft = new CreateFactorPaydownDraftRequestDto(fixture.Request with { CashAccountPath = "Assets:DifferentCash" }, fingerprint, "Reviewed factor paydown");

        using var response = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), draft, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("FACTOR_PAYDOWN_PREVIEW_STALE");
        spine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task FactorPaydownDraft_RetainsProjectionAndPassesOnlyServerDerivedInstructionToCandidate()
    {
        var fixture = FactorPreparationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        spine.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call => new AssetAccountingEventSpineAppendResultDto(FactorProjectedSpine(call.Arg<ProjectAssetAccountingEventRequestDto>()), false));
        spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingPostingCandidateDto(null!, null!));
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var fingerprint = preview.RootElement.GetProperty("preparationFingerprint").GetString()!;

        using var response = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request),
            new CreateFactorPaydownDraftRequestDto(fixture.Request, fingerprint, "Reviewed factor paydown"), ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await spine.Received(1).ProjectAsync(Arg.Is<ProjectAssetAccountingEventRequestDto>(r => r.Actor == "ops-user" && r.EventAmount == 50_000m), Arg.Any<CancellationToken>());
        await spine.Received(1).BuildPostingCandidateAsync(Arg.Is<AssetAccountingPostingCandidateRequestDto>(r =>
            r.Actor == "ops-user" && r.Scope.TenantId == "company-alpha" && r.ExpectedSpineVersion == 2 &&
            r.LotMutation!.FactorPaydown!.PrincipalPaydown == 50_000m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FactorPaydownDraft_ResumesAnInterruptedRetainedProjectionWithoutReprojecting()
    {
        var fixture = FactorPreparationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        AssetAccountingEventSpineDto? retained = null;
        var attempts = 0;
        spine.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => retained);
        spine.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            retained = FactorProjectedSpine(call.Arg<ProjectAssetAccountingEventRequestDto>());
            return new AssetAccountingEventSpineAppendResultDto(retained, false);
        });
        spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>()).Returns(_ =>
            ++attempts == 1
                ? Task.FromException<AssetAccountingPostingCandidateDto>(new InvalidOperationException("Interrupted candidate creation"))
                : Task.FromResult(new AssetAccountingPostingCandidateDto(retained!, null!)));
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var draft = new CreateFactorPaydownDraftRequestDto(fixture.Request,
            preview.RootElement.GetProperty("preparationFingerprint").GetString()!, "Reviewed paydown");

        using var interrupted = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), draft, ServerJsonOptions);
        using var retry = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), draft, ServerJsonOptions);

        interrupted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        await spine.Received(1).ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>());
        await spine.Received(2).BuildPostingCandidateAsync(Arg.Is<AssetAccountingPostingCandidateRequestDto>(r =>
            r.ExpectedSpineVersion == 2 && r.ProjectionLineage == retained!.ProjectionLineage), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FactorPaydownDraft_LotVersionDriftInvalidatesTheReviewedPreview()
    {
        var fixture = FactorPreparationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var currentLots = await fixture.Periods.ListOpenTaxLotsByAssetScopeAsync(fixture.Request.LedgerBookId,
            fixture.Request.SecurityId, fixture.Request.BookPositionId, fixture.Request.EffectiveDate);
        fixture.Periods.ListOpenTaxLotsByAssetScopeAsync(fixture.Request.LedgerBookId, fixture.Request.SecurityId,
            fixture.Request.BookPositionId, fixture.Request.EffectiveDate, Arg.Any<CancellationToken>())
            .Returns([currentLots[0] with { Version = currentLots[0].Version + 1 }]);

        using var response = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request),
            new CreateFactorPaydownDraftRequestDto(fixture.Request,
                preview.RootElement.GetProperty("preparationFingerprint").GetString()!, "Reviewed paydown"), ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("FACTOR_PAYDOWN_PREVIEW_STALE");
        spine.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("same", HttpStatusCode.OK)]
    [InlineData("description", HttpStatusCode.Conflict)]
    [InlineData("actor", HttpStatusCode.Conflict)]
    public async Task FactorPaydownDraft_ExactRetryReturnsRetainedDraftAndRejectsChangedCommand(string scenario, HttpStatusCode expected)
    {
        var fixture = FactorPreparationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        var preparation = await fixture.Service.PrepareAsync(new FactorPaydownPreparationRequest(
            fixture.Request.LedgerBookId, fixture.Request.SecurityId, fixture.Request.BookPositionId, fixture.Request.PeriodId,
            1, fixture.Request.AccountingBasis, fixture.Request.FundProfileId, fixture.Request.EffectiveDate,
            fixture.Request.PriorFactor, fixture.Request.NewFactor, fixture.Request.SourceDomain, fixture.Request.SourceEntityId,
            fixture.Request.SourceContentHash, fixture.Request.OccurredAtUtc, fixture.Request.FactorEvidence,
            fixture.Request.CashAccountPath, fixture.Request.CarryingAccountPath, fixture.Request.RealizedGainAccountPath,
            fixture.Request.RealizedLossAccountPath, "company-alpha", "company-alpha"), "ops-user", DateTimeOffset.UtcNow);
        var candidate = new PostingRuleJournalCandidateResultDto(null!, null, null, [], null, null,
            50_750m, 50_750m, 0m, true, false, true, false, [], []);
        var retained = FactorProjectedSpine(preparation.ProjectRequest) with
        {
            SpineVersion = 3,
            DraftedCandidate = new PostingRuleJournalCandidateRequestDto(fixture.Request.FundProfileId,
                preparation.ProjectRequest.EconomicEvent.EventType, 50_000m, "USD", fixture.Request.EffectiveDate,
                scenario == "actor" ? "another-user" : "ops-user", fixture.Request.LedgerBookId, fixture.Request.PeriodId,
                DateTimeOffset.UtcNow, scenario == "description" ? "Different draft" : "Reviewed paydown"),
            DraftedCandidateResult = candidate
        };
        spine.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(retained);
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());

        using var response = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request),
            new CreateFactorPaydownDraftRequestDto(fixture.Request,
                preview.RootElement.GetProperty("preparationFingerprint").GetString()!, "Reviewed paydown"), ServerJsonOptions);

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        await spine.DidNotReceive().ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>());
        await spine.DidNotReceive().BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>());
    }

    private static AssetAccountingEventSpineDto FactorProjectedSpine(ProjectAssetAccountingEventRequestDto request)
        => new(request.EconomicEvent.EventId, request.EventKind, request.EconomicEvent.EventVersion, 2,
            request.EconomicEvent.EffectiveDate, request.EventAmount, request.Currency, request.Scope,
            request.EconomicEvent, request.ProjectionLineage, request.RetainedEvidence,
            ProjectedEffect: request.ProjectedEffect, FactorPaydown: request.FactorPaydown);

    private static string FactorPreparationRoute(PrepareFactorPaydownRequestDto request)
        => UiApiRoutes.WithParam(UiApiRoutes.LedgerFactorPaydownPreparation, "ledgerBookId", request.LedgerBookId.ToString("D"));

    private static string FactorDraftRoute(PrepareFactorPaydownRequestDto request)
        => UiApiRoutes.WithParam(UiApiRoutes.LedgerFactorPaydownDrafts, "ledgerBookId", request.LedgerBookId.ToString("D"));

    private static FactorPreparationTestFixture FactorPreparationFixture()
    {
        var lot = FactorPaydownTestData.PremiumLot();
        var instruction = FactorPaydownTestData.Build([lot], 0.95m, 0.90m);
        var request = new PrepareFactorPaydownRequestDto(lot.LedgerBookId, lot.SecurityId, lot.BookPositionId,
            instruction.Scope.PeriodId, 1, AccountingBasisKindDto.Gaap, "fund-alpha", FactorPaydownTestData.EffectiveDate,
            0.95m, 0.90m, "SecurityMaster", instruction.EconomicEvent.SourceEntityId!, instruction.EconomicEvent.SourceContentHash!,
            instruction.EconomicEvent.OccurredAtUtc, [instruction.EconomicEvent.RetainedEvidence[0]], "Assets:Cash",
            "Assets:Investment", "Income:Realized Gain", "Expense:Realized Loss");
        var books = Substitute.For<ILedgerBookService>();
        var periods = Substitute.For<ILedgerJournalStore>();
        var securities = Substitute.For<ISecurityMasterStore>();
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        books.GetBookAsync(lot.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(lot.LedgerBookId,
            "fund-alpha", ownerId, FundStructureNodeKindDto.Fund, "GAAP", "USD", now, now, AccountingBasis: AccountingBasisKindDto.Gaap));
        periods.GetPeriodAsync(request.PeriodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(
            request.PeriodId, request.LedgerBookId, 2026, 6, "June", new(2026, 6, 1), new(2026, 6, 30), "Open", now, null, 1));
        var empty = JsonSerializer.SerializeToElement(new { });
        securities.GetProjectionAsync(lot.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityProjectionRecord(
            lot.SecurityId, "Bond", SecurityStatusDto.Active, "MBS pool", "USD", "ISIN", "TESTMBS", empty, empty, empty, 1, now, null, [], []));
        positions.GetBookPositionAsync(lot.BookPositionId, Arg.Any<CancellationToken>()).Returns(new BookPositionDto(lot.BookPositionId,
            lot.SecurityId, Guid.NewGuid(), new AccountingBookContextDto(lot.LedgerBookId, "fund-alpha", ownerId,
                FundStructureNodeKindDto.Fund, "GAAP", "USD", AccountingBasisKindDto.Gaap, "policy", "1", request.PeriodId),
            BookPositionSides.Long, "Active", new(2026, 1, 1)));
        periods.ListOpenTaxLotsByAssetScopeAsync(lot.LedgerBookId, lot.SecurityId, lot.BookPositionId, request.EffectiveDate,
            Arg.Any<CancellationToken>()).Returns([FactorPaydownTestData.LotRecord(lot, new Meridian.Ledger.LedgerAccount("Assets:Investment", Meridian.Ledger.LedgerAccountType.Asset), now)]);
        return new(request, books, periods, securities, new FactorPaydownLotInstructionService(periods, securities, positions, new FactorPaydownProjectionService()));
    }

    private sealed record FactorPreparationTestFixture(PrepareFactorPaydownRequestDto Request, ILedgerBookService Books,
        ILedgerJournalStore Periods, ISecurityMasterStore Securities, FactorPaydownLotInstructionService Service)
    {
        public void Register(IServiceCollection services)
        {
            services.AddSingleton(Books);
            services.AddSingleton(Periods);
            services.AddSingleton(Service);
        }
    }
}
