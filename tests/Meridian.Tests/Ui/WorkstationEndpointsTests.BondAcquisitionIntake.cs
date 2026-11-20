using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task BondAcquisitionIntake_EnqueuesVerifiedCandidateOnceAndPreservesImmutableReviewAuthority()
    {
        var fixture = FactorPreparationFixture();
        var request = BondAcquisitionIntakeRequest(fixture.Request.LedgerBookId, fixture.Request.PeriodId);
        var store = new InMemoryManualJournalEntryDraftStore();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        AssetAccountingPostingCandidateDto? verified = null;
        spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call => verified ??= BondAcquisitionIntakeCandidate(call.Arg<AssetAccountingPostingCandidateRequestDto>()));
        spine.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => verified?.Spine);
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            RegisterBondAcquisitionReviewConfiguration(services, request, store);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsJsonAsync(UiApiRoutes.LedgerAccountingConfigurationAssetAccountingCandidates,
                request, ServerJsonOptions);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        var draft = (await store.ListAsync("fund-alpha", request.Scope.LedgerBookId,
            tenantId: "company-alpha", companyId: "company-alpha")).Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.PreparedBy.Should().Be("ops-user");
        draft.AssetAccountingDraft.Should().BeEquivalentTo(verified);
        draft.TotalDebits.Should().Be(102_750m);
        draft.TotalCredits.Should().Be(102_750m);
        var acquisition = draft.AssetAccountingDraft!.Spine.DraftedLotMutation!.Acquisition!;
        (acquisition.Quantity * acquisition.UnitCost).Should().Be(101_250m,
            "purchased accrued interest stays outside the clean acquisition lot cost");
        draft.Lines.Should().Contain(line => line.AccountPath == "Assets:PurchasedInterest" && line.Amount == 1_500m);
        draft.AssetAccountingApproval.Should().BeNull();
        draft.AssetAccountingPostingResult.Should().BeNull();
        await spine.Received(2).BuildPostingCandidateAsync(Arg.Is<AssetAccountingPostingCandidateRequestDto>(forwarded =>
            forwarded.Actor == "ops-user" && forwarded.Scope.TenantId == "company-alpha" &&
            forwarded.Scope.CompanyId == "company-alpha"), Arg.Any<CancellationToken>());

        var workbench = new ManualJournalEntryWorkbenchService(store,
            app.Services.GetRequiredService<IAccountingConfigurationService>(), new InMemoryAccountingActionAuditStore(),
            journalStore: fixture.Periods, assetAccountingSource: spine);
        var forgedSave = () => workbench.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            draft with { AssetAccountingDraft = null, Memo = "Altered bond" }, "ops-user",
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        await forgedSave.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
        var submitted = await workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            draft.JournalEntryId, draft.FundProfileId, "ops-user", draft.Version, CorrelationId: "bond-intake-submit",
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        var selfApproval = () => workbench.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId, submitted.FundProfileId, JournalEntryLifecycleActionDto.Approve, "ops-user", submitted.Version,
            Notes: "Review the synthetic clean acquisition cost and purchased interest.",
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        await selfApproval.Should().ThrowAsync<InvalidOperationException>().WithMessage("*independent*");
        var approved = (await workbench.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId, submitted.FundProfileId, JournalEntryLifecycleActionDto.Approve, "independent-controller", submitted.Version,
            Notes: "Review the synthetic clean acquisition cost and purchased interest.",
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId))).JournalEntry;
        approved.AssetAccountingApproval.Should().NotBeNull();
        approved.AssetAccountingPostingResult.Should().BeNull();
        var genericPost = () => workbench.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntryId, approved.FundProfileId, JournalEntryLifecycleActionDto.Post, "independent-controller", approved.Version,
            Notes: "Post the independently reviewed synthetic bond acquisition.",
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        await genericPost.Should().ThrowAsync<InvalidOperationException>().WithMessage("*governed asset posting workflow*");
    }

    [Fact]
    public async Task BondAcquisitionIntake_MissingReviewServiceRefusesBeforeDraftingTheSpine()
    {
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        await using var app = await CreateAppAsync(services => services.AddSingleton(spine),
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerAccountingConfigurationAssetAccountingCandidates,
            BondAcquisitionIntakeRequest(Guid.NewGuid(), Guid.NewGuid()), ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented, await response.Content.ReadAsStringAsync());
        await spine.DidNotReceiveWithAnyArgs().BuildPostingCandidateAsync(default!, default);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("foreign-currency")]
    [InlineData("missing-face")]
    [InlineData("dirty-cash")]
    public async Task BondAcquisitionIntake_UnsupportedOrUnreconciledSettlementCannotCreateAReviewDraft(string fault)
    {
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        var request = BondAcquisitionIntakeRequest(Guid.NewGuid(), Guid.NewGuid());
        var mutation = request.LotMutation!;
        var acquisition = mutation.Acquisition!;
        mutation = fault switch
        {
            "short" => mutation with { Direction = OpenLotDirection.Short },
            "missing-face" => mutation with { Acquisition = acquisition with { OriginalFace = null } },
            "foreign-currency" => mutation with { Acquisition = acquisition with
            { SettlementConsideration = acquisition.SettlementConsideration! with { FunctionalCurrency = "EUR", SettlementFxRateToFunctional = 1m } } },
            _ => mutation with { Acquisition = acquisition with
            { SettlementConsideration = acquisition.SettlementConsideration! with { ObservedDirtyCash = 102_749.99m } } }
        };
        await using var app = await CreateAppAsync(services => services.AddSingleton(spine),
            currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerAccountingConfigurationAssetAccountingCandidates,
            request with { LotMutation = mutation }, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        await spine.DidNotReceiveWithAnyArgs().BuildPostingCandidateAsync(default!, default);
    }

    [Fact]
    public async Task BondAcquisitionIntake_SpineVerificationFailureCannotEnqueueADraft()
    {
        var fixture = FactorPreparationFixture();
        var request = BondAcquisitionIntakeRequest(fixture.Request.LedgerBookId, fixture.Request.PeriodId);
        var store = new InMemoryManualJournalEntryDraftStore();
        var spine = Substitute.For<IAssetAccountingEventSpineService>();
        spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>())
            .Returns<AssetAccountingPostingCandidateDto>(_ => throw new InvalidOperationException("Settlement inputs changed."));
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            RegisterBondAcquisitionReviewConfiguration(services, request, store);
            services.AddSingleton(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerAccountingConfigurationAssetAccountingCandidates,
            request, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await store.ListAsync("fund-alpha", request.Scope.LedgerBookId,
            tenantId: "company-alpha", companyId: "company-alpha")).Should().BeEmpty();
    }

    [Fact]
    public async Task BondAcquisitionIntake_OrdinaryAcquisitionWithoutVerifiedSettlementRemainsOutsideReviewIntake()
    {
        var request = BondAcquisitionIntakeRequest(Guid.NewGuid(), Guid.NewGuid());
        request = request with { LotMutation = request.LotMutation! with
        { Acquisition = request.LotMutation.Acquisition! with { SettlementConsideration = null } } };
        var workbench = new ManualJournalEntryWorkbenchService(new InMemoryManualJournalEntryDraftStore(),
            Substitute.For<IAccountingConfigurationService>(), new InMemoryAccountingActionAuditStore());
        var save = () => workbench.SaveAssetAccountingDraftAsync(BondAcquisitionIntakeCandidate(request), request.Actor, default);
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*supported asset candidate*");
    }

    private static AssetAccountingPostingCandidateRequestDto BondAcquisitionIntakeRequest(Guid bookId, Guid periodId)
    {
        var request = BuildAssetAccountingCandidateRequest();
        var economicEvent = request.EconomicEvent with { EventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Acquisition) };
        var receipt = new BondSettlementConsiderationDto(request.Scope.SecurityId, "USD", "bond-analytics-v1",
            new BondCalculationContext(request.AccountingTimestamp, economicEvent.EffectiveDate), BondYieldConvention.NominalPeriodic,
            100_000m, 1m, 101.25m, 1.5m, 101_250m, 1_500m, 0m, 102_750m, 102_750m,
            request.RetainedEvidence[0].EvidenceId, "Assets:Cash", "Assets:PurchasedInterest", null,
            [new("SecurityTerms", request.Scope.SecurityId.ToString("D"), request.Scope.ExpectedSecurityVersion, new string('a', 64))]);
        var acquisition = new AssetAcquisitionLotDto(Guid.NewGuid(), "paper-bond-lot", economicEvent.EffectiveDate,
            1_000m, 101.25m, "Assets:Investment", 100_000m, 1m, 100m, BondAmortizationMethod.StraightLine,
            SettlementConsideration: receipt);
        return request with
        {
            EventKind = AssetAccountingEventKindDto.Acquisition,
            Scope = request.Scope with
            {
                LedgerBookId = bookId, PeriodId = periodId,
                Dimensions = new LedgerDimensionSetDto(FundId: request.Scope.FundProfileId, EntityId: "company-alpha",
                    InstrumentId: request.Scope.SecurityId, BookId: bookId.ToString("D"))
                { PositionId = request.Scope.BookPositionId }
            },
            EconomicEvent = economicEvent,
            ProjectionLineage = request.ProjectionLineage with { TriggerEvent = economicEvent },
            EventAmount = 101_250m,
            ExpectedPeriodVersion = 1,
            Description = "Reviewed premium bond acquisition",
            LotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Acquire, acquisition)
        };
    }

    private static AssetAccountingPostingCandidateDto BondAcquisitionIntakeCandidate(AssetAccountingPostingCandidateRequestDto request)
    {
        var lines = new[]
        {
            new GeneratedPostingLineDto("clean", "Assets:Investment", AccountingTemplateLineSideDto.Debit, "clean", 101_250m, "USD"),
            new GeneratedPostingLineDto("interest", "Assets:PurchasedInterest", AccountingTemplateLineSideDto.Debit, "purchased interest", 1_500m, "USD"),
            new GeneratedPostingLineDto("cash", "Assets:Cash", AccountingTemplateLineSideDto.Credit, "dirty cash", 102_750m, "USD")
        };
        var candidate = new PostingRuleJournalCandidateResultDto(null!, "bond-acquisition", "1", lines, null, Guid.NewGuid(),
            102_750m, 102_750m, 0m, true, false, true, false, request.RetainedEvidence.Select(e => e.EvidenceUri).ToArray(), []);
        var scope = request.Scope;
        var source = new PostingRuleJournalCandidateRequestDto(scope.FundProfileId, request.EconomicEvent.EventType,
            request.EventAmount, request.Currency, request.EconomicEvent.EffectiveDate, request.Actor, scope.LedgerBookId,
            scope.PeriodId, request.AccountingTimestamp, request.Description, scope.AccountingBasis, scope.LedgerBookId,
            scope.Dimensions, SourceEventId: request.EconomicEvent.EventId, TenantId: scope.TenantId, CompanyId: scope.CompanyId)
        {
            AssetLotMutation = request.LotMutation, RetainedEvidence = request.RetainedEvidence,
            EconomicEvent = request.EconomicEvent, ProjectionLineage = request.ProjectionLineage,
            BookPositionId = scope.BookPositionId, ExpectedPeriodVersion = request.ExpectedPeriodVersion
        };
        var spine = new AssetAccountingEventSpineDto(request.EconomicEvent.EventId, request.EventKind,
            request.EconomicEvent.EventVersion, 3, request.EconomicEvent.EffectiveDate, request.EventAmount, request.Currency,
            scope, request.EconomicEvent, request.ProjectionLineage, request.RetainedEvidence,
            [new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Drafted, request.AccountingTimestamp,
                request.Actor, request.RetainedEvidence)], DraftedCandidate: source, DraftedCandidateResult: candidate,
            DraftedCandidateFingerprint: AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(source),
            DraftedCandidateResultFingerprint: AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate),
            DraftedLotMutation: request.LotMutation, DraftedLotMutationFingerprint: AssetLotMutationInstructionValidator.Fingerprint(request.LotMutation));
        return new(spine, candidate);
    }

    private static void RegisterBondAcquisitionReviewConfiguration(IServiceCollection services,
        AssetAccountingPostingCandidateRequestDto request, IManualJournalEntryDraftStore store)
    {
        var configuration = Substitute.For<IAccountingConfigurationService>();
        var chart = new[] { "Assets:Cash", "Assets:Investment", "Assets:PurchasedInterest" }
            .Select(path => new ChartOfAccountsNodeDto(path, path, path, "Asset")).ToArray();
        configuration.GetWorkspaceAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new AccountingConfigurationWorkspaceDto(request.Scope.FundProfileId, request.Scope.LedgerBookId,
                AccountingConfigurationStatusDto.Draft, "test", DateTimeOffset.UtcNow, [], chart, [], [], [], []));
        services.AddSingleton(configuration);
        services.AddSingleton(store);
    }
}
