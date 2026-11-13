using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.AssetOperations;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task CanonicalAmortizationPreparation_UsesRetainedMethodAndExactDecimalScheduleWithoutWriting()
    {
        var fixture = CanonicalAmortizationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services => { fixture.Register(services); services.AddSingleton(spine); },
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var request = fixture.Request with { ScheduleEndDate = fixture.Request.AsOfDate.AddMonths(2) };
        using var response = await app.GetTestClient().PostAsJsonAsync(AmortizationRoute(request), request, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("projection").GetProperty("functionalMovement").ValueKind.Should().Be(JsonValueKind.String);
        json.RootElement.GetProperty("occurrences").GetArrayLength().Should().Be(3);
        json.RootElement.GetProperty("scope").GetProperty("tenantId").GetString().Should().Be("company-alpha");
        json.RootElement.GetProperty("preparationFingerprint").GetString().Should().NotBeNullOrWhiteSpace();
        spine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalAmortizationPreparation_AdoptedIncomeLotRequiresItsPolicyOwnedWorkflow()
    {
        var fixture = CanonicalAmortizationFixture(adoptedIncome: true);
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services => { fixture.Register(services); services.AddSingleton(spine); },
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(AmortizationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("ADOPTED_BOND_INCOME_REQUIRED");
        spine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalAmortizationPreparation_ReadOnlyPermissionIsRefusedBeforeAuthorityRead()
    {
        var fixture = CanonicalAmortizationFixture();
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ViewLedgerReports,
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(AmortizationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Books.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign-fund", HttpStatusCode.Forbidden)]
    [InlineData("period-version", HttpStatusCode.Conflict)]
    [InlineData("evidence-date", HttpStatusCode.BadRequest)]
    public async Task CanonicalAmortizationPreparation_RefusesForeignStaleOrUnreviewedScope(string fault, HttpStatusCode expected)
    {
        var fixture = CanonicalAmortizationFixture();
        var request = fault switch
        {
            "foreign-fund" => fixture.Request with { FundProfileId = "foreign" },
            "period-version" => fixture.Request with { ExpectedPeriodVersion = 999 },
            _ => fixture.Request with { EventEvidence = fixture.Request.EventEvidence with { EffectiveDate = fixture.Request.AsOfDate.AddDays(1) } }
        };
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(AmortizationRoute(fixture.Request), request, ServerJsonOptions);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        fixture.Securities.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalAmortizationDraft_RecomputesAndRejectsChangedReviewedAccountsBeforeWriting()
    {
        var fixture = CanonicalAmortizationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services => { fixture.Register(services); services.AddSingleton(spine); },
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var fingerprint = preview.RootElement.GetProperty("preparationFingerprint").GetString()!;
        using var response = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request, true),
            new CreateCanonicalAmortizationDraftRequestDto(fixture.Request with { IncomeAccountPath = "Income:Changed" }, fingerprint, "Reviewed amortization"), ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("AMORTIZATION_PREVIEW_STALE");
        spine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalAmortizationDraft_RetainsServerInstructionInGovernedJournalReviewQueue()
    {
        var fixture = CanonicalAmortizationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        AssetAccountingEventSpineDto? projected = null;
        spine.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
            new AssetAccountingEventSpineAppendResultDto(projected = FactorProjectedSpine(call.Arg<ProjectAssetAccountingEventRequestDto>()), false));
        spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<AssetAccountingPostingCandidateRequestDto>();
            var factorFixture = FactorDraftedCandidate(projected!, command.Actor, command.Description);
            var source = factorFixture.Spine.DraftedCandidate! with { AssetLotMutation = command.LotMutation };
            return factorFixture with { Spine = factorFixture.Spine with {
                DraftedCandidate = source, DraftedLotMutation = command.LotMutation,
                DraftedCandidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(source),
                DraftedLotMutationFingerprint = AssetLotMutationInstructionValidator.Fingerprint(command.LotMutation)
            } };
        });
        await using var app = await CreateAppAsync(services => { fixture.Register(services); services.AddSingleton(spine); },
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var fingerprint = preview.RootElement.GetProperty("preparationFingerprint").GetString()!;
        using var response = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request, true),
            new CreateCanonicalAmortizationDraftRequestDto(fixture.Request, fingerprint, "Reviewed amortization"), ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await spine.Received(1).BuildPostingCandidateAsync(Arg.Is<AssetAccountingPostingCandidateRequestDto>(request =>
            request.Actor == "ops-user" && request.Scope.TenantId == "company-alpha" && request.LotMutation!.Intent == AssetLotMutationIntentDto.Amortize &&
            request.LotMutation.Amortization!.ExpectedLot.TaxLotRecordId == fixture.Request.TaxLotRecordId), Arg.Any<CancellationToken>());
        var store = app.Services.GetRequiredService<IManualJournalEntryDraftStore>();
        var retained = await store.ListAsync("fund-alpha", fixture.Request.LedgerBookId, default, "company-alpha", "company-alpha");
        retained.Should().ContainSingle().Which.AssetAccountingDraft!.Spine.DraftedLotMutation!.Amortization.Should().NotBeNull();
    }

    private static string AmortizationRoute(PrepareCanonicalAmortizationRequestDto request, bool draft = false)
        => $"/api/ledger/books/{request.LedgerBookId:D}/amortization/{(draft ? "drafts" : "prepare")}";

    private static CanonicalAmortizationTestFixture CanonicalAmortizationFixture(bool adoptedIncome = false)
    {
        var inputs = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);
        var lot = inputs.ExpectedLot;
        if (adoptedIncome)
            lot = lot with { Acquisition = lot.Acquisition with { BondIncome = new(
                DateTimeOffset.UtcNow, lot.Acquisition.TransactionCostBasis, 0m, 1m, [], lot.Acquisition.Evidence) } };
        var now = DateTimeOffset.UtcNow;
        var periodId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var evidence = inputs.SecurityEvidence with { EvidenceId = "amort-occurrence", EvidenceUri = "document://amort/occurrence", ContentHashSha256 = new string('d', 64),
            SubjectType = AssetAccountingEvidenceSubjects.Event, SubjectId = eventId.ToString("D"), EffectiveDate = inputs.AsOfDate };
        var request = new PrepareCanonicalAmortizationRequestDto(lot.LedgerBookId, lot.TaxLotRecordId, periodId, 1, AccountingBasisKindDto.Gaap,
            "fund-alpha", inputs.AsOfDate, inputs.SecurityEvidence, evidence, "Assets:Investment", "Income:Amortization");
        var books = Substitute.For<ILedgerBookService>();
        var periods = Substitute.For<ILedgerJournalStore>();
        var securities = Substitute.For<ISecurityMasterStore>();
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        var ownerId = Guid.NewGuid();
        books.GetBookAsync(lot.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(lot.LedgerBookId,
            "fund-alpha", ownerId, FundStructureNodeKindDto.Fund, "GAAP", "USD", now, now, AccountingBasis: AccountingBasisKindDto.Gaap));
        periods.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(periodId, lot.LedgerBookId,
            inputs.AsOfDate.Year, inputs.AsOfDate.Month, "Amortization", inputs.AsOfDate.AddDays(-1), inputs.AsOfDate.AddMonths(1), "Open", now, null, 1));
        periods.GetTaxLotsByIdsAsync(lot.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([
            FactorPaydownTestData.LotRecord(lot, new LedgerAccount("Assets:Investment", LedgerAccountType.Asset), now)]);
        securities.GetProjectionAsync(lot.SecurityId, Arg.Any<CancellationToken>()).Returns(inputs.Security);
        positions.GetBookPositionAsync(lot.BookPositionId, Arg.Any<CancellationToken>()).Returns(new BookPositionDto(lot.BookPositionId,
            lot.SecurityId, Guid.NewGuid(), new AccountingBookContextDto(lot.LedgerBookId, "fund-alpha", ownerId, FundStructureNodeKindDto.Fund,
                "GAAP", "USD", AccountingBasisKindDto.Gaap, "policy", "1", periodId, new LedgerDimensionSetDto(FundId: "fund-alpha")),
            BookPositionSides.Long, "Active", lot.AcquiredDate));
        return new(request, books, periods, securities, new CanonicalLotAmortizationService(periods, securities, positions));
    }
    private sealed record CanonicalAmortizationTestFixture(PrepareCanonicalAmortizationRequestDto Request, ILedgerBookService Books,
        ILedgerJournalStore Periods, ISecurityMasterStore Securities, CanonicalLotAmortizationService Service)
    {
        public void Register(IServiceCollection services)
        {
            services.AddSingleton(Books); services.AddSingleton(Periods); services.AddSingleton(Service);
            var configuration = Substitute.For<IAccountingConfigurationService>();
            var chart = new[] { "Assets:Investment", "Income:Amortization" }.Select(path => new ChartOfAccountsNodeDto(path, path, path, "Asset")).ToArray();
            configuration.GetWorkspaceAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
                .Returns(new AccountingConfigurationWorkspaceDto(Request.FundProfileId, Request.LedgerBookId,
                    AccountingConfigurationStatusDto.Draft, "test", DateTimeOffset.UtcNow, [], chart, [], [], [], []));
            services.AddSingleton(configuration);
            services.AddSingleton<IManualJournalEntryDraftStore>(new InMemoryManualJournalEntryDraftStore());
            services.AddSingleton<IAccountingActionAuditStore>(new InMemoryAccountingActionAuditStore());
        }
    }
}
