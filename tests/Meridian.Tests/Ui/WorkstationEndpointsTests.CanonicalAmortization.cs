using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
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
        var fixture = await CanonicalAmortizationFixture();
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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CanonicalAmortizationPreparationAndDraft_UnsupportedFxRefusesBeforeCalculationOrRetention(bool crossCurrency, bool nonUnitFx)
    {
        var fixture = await CanonicalAmortizationFixture(crossCurrency: crossCurrency, nonUnitFx: nonUnitFx);
        var before = await fixture.Positions.GetSecurityAsync(fixture.Security.SecurityId);
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services => { fixture.Register(services); services.AddSingleton(spine); },
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var preparation = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        using var draft = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request, true),
            new CreateCanonicalAmortizationDraftRequestDto(fixture.Request, "not-calculated", "Reviewed FX occurrence"), ServerJsonOptions);
        foreach (var response in new[] { preparation, draft })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadAsStringAsync()).Should().Contain("AMORTIZATION_FX_UNSUPPORTED");
        }
        fixture.Securities.ReceivedCalls().Should().BeEmpty("unsupported FX must be refused before the calculation reads reference data");
        spine.ReceivedCalls().Should().BeEmpty();
        (await fixture.Positions.GetSecurityAsync(fixture.Security.SecurityId)).Should().BeEquivalentTo(before,
            "no reviewed occurrence lineage or position version may be retained for unsupported FX");
        var drafts = app.Services.GetRequiredService<IManualJournalEntryDraftStore>();
        (await drafts.ListAsync("fund-alpha", fixture.Request.LedgerBookId, tenantId: "company-alpha", companyId: "company-alpha")).Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalAmortizationPreparation_ForeignLotCannotRevealUnsupportedFxFacts()
    {
        var fixture = await CanonicalAmortizationFixture(crossCurrency: true, nonUnitFx: true);
        var retained = (await fixture.Periods.GetTaxLotsByIdsAsync(fixture.Request.LedgerBookId,
            [fixture.Request.TaxLotRecordId])).Single();
        fixture.Periods.GetTaxLotsByIdsAsync(fixture.Request.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([retained with { LedgerBookId = Guid.NewGuid() }]);
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(AmortizationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("AMORTIZATION_FX_UNSUPPORTED");
        fixture.Securities.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalAmortizationPreparation_AdoptedIncomeLotRequiresItsPolicyOwnedWorkflow()
    {
        var fixture = await CanonicalAmortizationFixture(adoptedIncome: true);
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
        var fixture = await CanonicalAmortizationFixture();
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
        var fixture = await CanonicalAmortizationFixture();
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
        var fixture = await CanonicalAmortizationFixture();
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
        var fixture = await CanonicalAmortizationFixture();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        AssetAccountingEventSpineDto? projected = null;
        spine.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
            new AssetAccountingEventSpineAppendResultDto(projected = FactorProjectedSpine(call.Arg<ProjectAssetAccountingEventRequestDto>()), false));
        spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<AssetAccountingPostingCandidateRequestDto>();
            var factorFixture = FactorDraftedCandidate(projected!, command.Actor, command.Description);
            var source = factorFixture.Spine.DraftedCandidate! with { AssetLotMutation = command.LotMutation };
            return factorFixture with
            {
                Spine = factorFixture.Spine with
                {
                    DraftedCandidate = source,
                    DraftedLotMutation = command.LotMutation,
                    DraftedCandidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(source),
                    DraftedLotMutationFingerprint = AssetLotMutationInstructionValidator.Fingerprint(command.LotMutation)
                }
            };
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

    private static async Task<CanonicalAmortizationTestFixture> CanonicalAmortizationFixture(bool adoptedIncome = false, bool crossCurrency = false, bool nonUnitFx = false)
    {
        var inputs = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);
        var security = inputs.Security with { Currency = crossCurrency ? "EUR" : "USD" };
        var lot = inputs.ExpectedLot with
        {
            OpenFunctionalCostBasis = nonUnitFx ? inputs.ExpectedLot.OpenFunctionalCostBasis : inputs.ExpectedLot.OpenTransactionCostBasis,
            Acquisition = inputs.ExpectedLot.Acquisition with { AcquisitionCurrency = crossCurrency ? "EUR" : "USD", AcquisitionFxRateToFunctional = nonUnitFx ? 1.1m : 1m,
                FunctionalCostBasis = nonUnitFx ? inputs.ExpectedLot.Acquisition.FunctionalCostBasis : inputs.ExpectedLot.Acquisition.TransactionCostBasis }
        };
        inputs = inputs with { Security = security, ExpectedLot = lot,
            SecurityEvidence = inputs.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(security) } };
        if (adoptedIncome)
            lot = lot with
            {
                Acquisition = lot.Acquisition with
                {
                    BondIncome = new(
                DateTimeOffset.UtcNow, lot.Acquisition.TransactionCostBasis, 0m, 1m, [], lot.Acquisition.Evidence)
                }
            };
        var now = DateTimeOffset.UtcNow;
        var periodId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var evidence = inputs.SecurityEvidence with
        {
            EvidenceId = "amort-occurrence",
            EvidenceUri = "document://amort/occurrence",
            ContentHashSha256 = new string('d', 64),
            SubjectType = AssetAccountingEvidenceSubjects.Event,
            SubjectId = eventId.ToString("D"),
            EffectiveDate = inputs.AsOfDate
        };
        var request = new PrepareCanonicalAmortizationRequestDto(lot.LedgerBookId, lot.TaxLotRecordId, periodId, 1, AccountingBasisKindDto.Gaap,
            "fund-alpha", inputs.AsOfDate, inputs.SecurityEvidence, evidence, "Assets:Investment", "Income:Amortization");
        var books = Substitute.For<ILedgerBookService>();
        var periods = Substitute.For<ILedgerJournalStore>();
        var securities = Substitute.For<ISecurityMasterStore>();
        var positions = new InMemoryAssetOperationsProjectionStore();
        var ownerId = Guid.NewGuid();
        books.GetBookAsync(lot.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(lot.LedgerBookId,
            "fund-alpha", ownerId, FundStructureNodeKindDto.Fund, "GAAP", "USD", now, now, AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1"));
        periods.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(periodId, lot.LedgerBookId,
            inputs.AsOfDate.Year, inputs.AsOfDate.Month, "Amortization", inputs.AsOfDate.AddDays(-1), inputs.AsOfDate.AddMonths(1), "Open", now, null, 1));
        periods.GetTaxLotsByIdsAsync(lot.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([
            FactorPaydownTestData.LotRecord(lot, new LedgerAccount("Assets:Investment", LedgerAccountType.Asset), now)]);
        securities.GetProjectionAsync(lot.SecurityId, Arg.Any<CancellationToken>()).Returns(inputs.Security);
        var dimensions = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-1", AccountId: "account-1",
            CostCenterId: "fixed-income", PortfolioId: "portfolio-1", CounterpartyId: "issuer-1", BookId: lot.LedgerBookId.ToString("D"), InstrumentId: lot.SecurityId)
            { PositionId = lot.BookPositionId };
        var origin = new EconomicEventReferenceDto(Guid.NewGuid(), "Trade", 1, lot.AcquiredDate, inputs.SecurityEvidence.RetainedAtUtc,
            "custodian", "original-acquisition")
        { SecurityId = lot.SecurityId, BookPositionId = lot.BookPositionId,
            RetainedEvidence = lot.Acquisition.Evidence, EvidenceLinks = lot.Acquisition.Evidence.Select(item => item.EvidenceUri).ToArray() };
        var originalLineage = new ProjectionLineageDto(Guid.NewGuid(), null, "acquisition", "1", "1", "base", lot.AcquiredDate,
            origin.OccurredAtUtc, origin.SourceDomain, origin.SourceEntityId, origin)
        { BookPositionId = lot.BookPositionId, RetainedEvidence = lot.Acquisition.Evidence, EvidenceLinks = origin.EvidenceLinks };
        var role = new InstrumentRoleDto(Guid.NewGuid(), lot.SecurityId, "fund-alpha", "Fund", InstrumentRoleKinds.Holder,
            InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, lot.AcquiredDate,
            OriginEvent: origin, EvidenceLinks: origin.EvidenceLinks) { RetainedEvidence = lot.Acquisition.Evidence };
        var originalState = new PositionEconomicStateDto(Guid.NewGuid(), lot.BookPositionId, lot.AcquiredDate, "USD", 1,
            Quantity: lot.OpenQuantity, CarryingAmount: lot.OpenFunctionalCostBasis, SourceEvent: origin, EvidenceLinks: origin.EvidenceLinks)
        { ProjectionLineage = originalLineage, RetainedEvidence = lot.Acquisition.Evidence };
        var position = new BookPositionDto(lot.BookPositionId,
            lot.SecurityId, role.RoleId, new AccountingBookContextDto(lot.LedgerBookId, "fund-alpha", ownerId, FundStructureNodeKindDto.Fund,
                "GAAP", "USD", AccountingBasisKindDto.Gaap, "amort-policy", "1", periodId, dimensions),
            BookPositionSides.Long, "Active", lot.AcquiredDate, CurrentEconomicState: originalState, OriginEvent: origin,
            ProjectionLineage: originalLineage, EvidenceLinks: origin.EvidenceLinks) { RetainedEvidence = lot.Acquisition.Evidence };
        await positions.UpsertAsync(role, position, originalState, 0,
            new AssetOperationsWriteApprovalDto("source-reviewer", "evidence://approved-acquisition", "Reviewed acquisition source", now));
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([
            new LedgerPeriodDto(periodId, lot.LedgerBookId, inputs.AsOfDate.Year, inputs.AsOfDate.Month, "Amortization",
                inputs.AsOfDate.AddDays(-1), inputs.AsOfDate.AddMonths(1), LedgerPeriodStatusDto.Open, now, null, 1,
                AccountingBasisKindDto.Gaap, "amort-policy", "1")]);
        return new(request, books, periods, securities, positions, security,
            new CanonicalLotAmortizationService(periods, securities, positions));
    }

    private sealed record CanonicalAmortizationTestFixture(PrepareCanonicalAmortizationRequestDto Request, ILedgerBookService Books,
        ILedgerJournalStore Periods, ISecurityMasterStore Securities, InMemoryAssetOperationsProjectionStore Positions,
        SecurityProjectionRecord Security, CanonicalLotAmortizationService Service)
    {
        public void Register(IServiceCollection services)
        {
            services.AddSingleton(Books);
            services.AddSingleton(Periods);
            services.AddSingleton(Service);
            services.AddSingleton<IInstrumentPositionProjectionStore>(Positions);
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
