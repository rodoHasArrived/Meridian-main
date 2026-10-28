using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.AssetOperations;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task FactorPaydownRecovery_LotDriftAfterRetainedProjection_CanBePreparedAgainWithoutRewritingHistory()
    {
        var fixture = await FactorRecoveryFixture.CreateAsync();
        fixture.AfterProject = () => fixture.CurrentLot = fixture.CurrentLot with { Version = fixture.CurrentLot.Version + 1 };
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        var preview = await FactorRecoveryPreviewAsync(client, fixture.Request);

        using var failed = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), preview, ServerJsonOptions);

        failed.StatusCode.Should().Be(HttpStatusCode.Conflict, await failed.Content.ReadAsStringAsync());
        (await failed.Content.ReadAsStringAsync()).Should().Contain("paid-down lots");
        var original = await fixture.LatestAsync();
        original.Projection.SpineVersion.Should().Be(2, "ProjectAsync really committed before the lot changed and drafting failed");
        original.Projection.DraftedCandidate.Should().BeNull();
        var expectedBefore = await fixture.Store.GetAsync(original.Projection.EventId, original.Projection.EventVersion, 1);
        var expectedBytes = JsonSerializer.Serialize(expectedBefore, ServerJsonOptions);
        var projectedBytes = JsonSerializer.Serialize(original, ServerJsonOptions);

        using var stale = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), preview, ServerJsonOptions);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("FACTOR_PAYDOWN_PREVIEW_STALE");
        (await fixture.LatestAsync()).Projection.SpineVersion.Should().Be(2);
        var reviewedAgain = await FactorRecoveryPreviewAsync(client, fixture.Request);
        reviewedAgain.ExpectedPreparationFingerprint.Should().NotBe(preview.ExpectedPreparationFingerprint);
        using var succeeded = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), reviewedAgain, ServerJsonOptions);

        succeeded.StatusCode.Should().Be(HttpStatusCode.OK, await succeeded.Content.ReadAsStringAsync());
        var result = (await succeeded.Content.ReadFromJsonAsync<AssetAccountingPostingCandidateDto>(FactorRecoveryReadOptions))!;
        result.Spine.EventId.Should().Be(original.Projection.EventId);
        result.Spine.EventVersion.Should().Be(original.Projection.EventVersion);
        result.Spine.SpineVersion.Should().Be(4);
        result.Spine.FactorPaydown!.Lots.Single().ExpectedLot.Version.Should().Be(fixture.CurrentLot.Version);
        var revised = (await fixture.Store.GetAsync(result.Spine.EventId, result.Spine.EventVersion, 3))!;
        revised.Projection.PreparationRevision!.PreviousSpineVersion.Should().Be(2);
        revised.Projection.PreparationRevision.PreviousCanonicalFingerprint.Should().Be(original.CanonicalFingerprint);
        JsonSerializer.Serialize(await fixture.Store.GetAsync(result.Spine.EventId, result.Spine.EventVersion, 1), ServerJsonOptions).Should().Be(expectedBytes);
        JsonSerializer.Serialize(await fixture.Store.GetAsync(result.Spine.EventId, result.Spine.EventVersion, 2), ServerJsonOptions).Should().Be(projectedBytes);
        using var replay = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), reviewedAgain, ServerJsonOptions);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        (await fixture.LatestAsync()).Projection.SpineVersion.Should().Be(4);
        await fixture.Ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }

    [Fact]
    public async Task FactorPaydownRecovery_FailedAccountCandidateAcceptsCorrectedReviewedAccounts_ThenRefusesChangingADraftedEvent()
    {
        var fixture = await FactorRecoveryFixture.CreateAsync();
        fixture.RejectCashAccount = "Assets:MissingCash";
        var wrongAccount = fixture.Request with { CashAccountPath = fixture.RejectCashAccount };
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        var preview = await FactorRecoveryPreviewAsync(client, wrongAccount);
        using var failed = await client.PostAsJsonAsync(FactorDraftRoute(wrongAccount), preview, ServerJsonOptions);
        failed.StatusCode.Should().Be(HttpStatusCode.Conflict, await failed.Content.ReadAsStringAsync());
        (await failed.Content.ReadAsStringAsync()).Should().Contain("chart account");
        var original = await fixture.LatestAsync();
        original.Projection.SpineVersion.Should().Be(2);
        var originalBytes = JsonSerializer.Serialize(original, ServerJsonOptions);

        var correctedPreview = await FactorRecoveryPreviewAsync(client, fixture.Request);
        using var corrected = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), correctedPreview, ServerJsonOptions);
        corrected.StatusCode.Should().Be(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        var drafted = await fixture.LatestAsync();
        drafted.Projection.SpineVersion.Should().Be(4);
        drafted.Projection.ProjectedEffect!.Lines.Should().Contain(line => line.AccountId == "Assets:Cash");
        drafted.Projection.ProjectedEffect.Lines.Should().NotContain(line => line.AccountId == fixture.RejectCashAccount);
        JsonSerializer.Serialize(await fixture.Store.GetAsync(original.Projection.EventId, original.Projection.EventVersion, 2), ServerJsonOptions).Should().Be(originalBytes);

        // A later preview may inspect the current inputs, but it must never revise a retained draft.
        using var refusedPreview = await client.PostAsJsonAsync(FactorPreparationRoute(wrongAccount), wrongAccount, ServerJsonOptions);
        refusedPreview.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await fixture.LatestAsync()).CanonicalFingerprint.Should().Be(drafted.CanonicalFingerprint);
        await fixture.Ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }

    [Fact]
    public async Task FactorPaydownRecovery_OldBuildLosesRealAppendCasToFreshReviewedPreparationAndDraft()
    {
        var fixture = await FactorRecoveryFixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        fixture.BeforeCandidateWrite = async () =>
        {
            if (Interlocked.Increment(ref calls) != 1)
                return;
            entered.SetResult();
            await release.Task;
        };
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        var originalPreview = await FactorRecoveryPreviewAsync(client, fixture.Request);
        var oldBuild = client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), originalPreview, ServerJsonOptions);
        AssetAccountingEventProjectionRecord? original = null;
        AssetAccountingEventProjectionRecord? winningDraft = null;
        try
        {
            // The first real Build has read Projected v2 and validated its lot before the external
            // candidate work pauses. A second HTTP operation gets a genuinely fresh lot snapshot.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            original = await fixture.LatestAsync();
            original.Projection.SpineVersion.Should().Be(2);
            fixture.CurrentLot = fixture.CurrentLot with { Version = fixture.CurrentLot.Version + 1 };
            var freshPreview = await FactorRecoveryPreviewAsync(client, fixture.Request);
            using var freshBuild = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), freshPreview, ServerJsonOptions);
            freshBuild.StatusCode.Should().Be(HttpStatusCode.OK, await freshBuild.Content.ReadAsStringAsync());
            winningDraft = await fixture.LatestAsync();
            winningDraft.Projection.SpineVersion.Should().Be(4);
            winningDraft.Projection.FactorPaydown!.Lots.Single().ExpectedLot.Version.Should().Be(fixture.CurrentLot.Version);
        }
        finally
        {
            release.TrySetResult();
        }
        using var losingBuild = await oldBuild.WaitAsync(TimeSpan.FromSeconds(10));
        losingBuild.StatusCode.Should().Be(HttpStatusCode.Conflict, await losingBuild.Content.ReadAsStringAsync());
        (await losingBuild.Content.ReadAsStringAsync()).Should().Contain("different canonical fingerprint");
        var latest = await fixture.LatestAsync();
        latest.CanonicalFingerprint.Should().Be(winningDraft!.CanonicalFingerprint);
        var refreshed = (await fixture.Store.GetAsync(latest.Projection.EventId, latest.Projection.EventVersion, 3))!;
        refreshed.Projection.PreparationRevision!.PreviousCanonicalFingerprint.Should().Be(original!.CanonicalFingerprint);
        refreshed.Projection.DraftedCandidate.Should().BeNull("version three belongs to the linked fresh preparation, never the old draft");
        var drafts = await fixture.Drafts.ListAsync(fixture.Request.FundProfileId, fixture.Request.LedgerBookId,
            tenantId: "company-alpha", companyId: "company-alpha");
        drafts.Should().ContainSingle().Which.AssetAccountingDraft!.Spine.SpineVersion.Should().Be(4);
        await fixture.Ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }

    // The operator wire contract writes exact decimal strings. Only this test's response
    // readers permit numeric strings; request and production serialization stay unchanged.
    private static JsonSerializerOptions FactorRecoveryReadOptions => new(ServerJsonOptions)
    {
        NumberHandling = ServerJsonOptions.NumberHandling | JsonNumberHandling.AllowReadingFromString
    };

    private static async Task<CreateFactorPaydownDraftRequestDto> FactorRecoveryPreviewAsync(HttpClient client, PrepareFactorPaydownRequestDto request)
    {
        using var response = await client.PostAsJsonAsync(FactorPreparationRoute(request), request, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var preparation = (await response.Content.ReadFromJsonAsync<FactorPaydownPreparationDto>(FactorRecoveryReadOptions))!;
        return new(request, preparation.PreparationFingerprint, "Reviewed recoverable factor paydown");
    }

    /// <summary>The real append/CAS store and real spine execute; only external authority ports are fixtures.</summary>
    private sealed class FactorRecoveryFixture
    {
        public required PrepareFactorPaydownRequestDto Request { get; init; }
        public required ILedgerJournalStore Ledger { get; init; }
        public required InMemoryAssetOperationsProjectionStore Store { get; init; }
        public required ILedgerBookService Books { get; init; }
        public required FactorPaydownLotInstructionService Prepare { get; init; }
        public required IAssetAccountingEventSpineService Spine { get; init; }
        public required IAccountingConfigurationService Configuration { get; init; }
        public required LedgerTaxLotRecord CurrentLot { get; set; }
        public ProjectAssetAccountingEventRequestDto? CurrentProjection { get; set; }
        public Action? AfterProject { get; set; }
        public string? RejectCashAccount { get; set; }
        public Func<Task>? BeforeCandidateWrite { get; set; }
        public InMemoryManualJournalEntryDraftStore Drafts { get; } = new();

        public async Task<AssetAccountingEventProjectionRecord> LatestAsync()
        {
            var source = CurrentProjection!;
            return (await Store.GetLatestAsync(source.EconomicEvent.EventId, source.EconomicEvent.EventVersion))!;
        }

        public void Register(IServiceCollection services)
        {
            services.AddSingleton(Books);
            services.AddSingleton(Ledger);
            services.AddSingleton(Prepare);
            services.AddSingleton(Spine);
            services.AddSingleton(Configuration);
            services.AddSingleton<IManualJournalEntryDraftStore>(Drafts);
            services.AddSingleton<IAccountingActionAuditStore>(new InMemoryAccountingActionAuditStore());
        }

        public static async Task<FactorRecoveryFixture> CreateAsync()
        {
            var seed = InstrumentPositionProjectionFixture.Create();
            var position = seed.BookPositions.Single();
            var context = position.BookContext;
            var lot = FactorPaydownTestData.PremiumLot(position.SecurityId, position.PositionId, context.LedgerBookId);
            var initial = FactorPaydownTestData.Build([lot], 0.95m, 0.90m, positionVersion: position.Version);
            var periodId = initial.Scope.PeriodId;
            var now = DateTimeOffset.UtcNow;
            var request = new PrepareFactorPaydownRequestDto(context.LedgerBookId, position.SecurityId, position.PositionId,
                periodId, 1, AccountingBasisKindDto.Gaap, "fund-alpha", FactorPaydownTestData.EffectiveDate,
                0.95m, 0.90m, "SecurityMaster", initial.EconomicEvent.SourceEntityId!, initial.EconomicEvent.SourceContentHash!,
                initial.EconomicEvent.OccurredAtUtc, [initial.EconomicEvent.RetainedEvidence[0]], "Assets:Cash", "Assets:Investment",
                "Income:Realized Gain", "Expense:Realized Loss");
            var store = new InMemoryAssetOperationsProjectionStore();
            await store.UpsertAsync(seed, InstrumentPositionProjectionFixture.Approval);
            var positions = Substitute.For<IInstrumentPositionProjectionStore>();
            positions.GetBookPositionAsync(position.PositionId, Arg.Any<CancellationToken>()).Returns(_ => position);
            var securities = Substitute.For<ISecurityMasterStore>();
            var empty = JsonSerializer.SerializeToElement(new { });
            securities.GetProjectionAsync(position.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityProjectionRecord(
                position.SecurityId, "Bond", SecurityStatusDto.Active, "MBS pool", "USD", "ISIN", "TESTMBS", empty, empty, empty, 1, now, null, [], []));
            var securityQuery = Substitute.For<ISecurityMasterQueryService>();
            securityQuery.GetRecordedByIdAsOfAsync(position.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new SecurityDetailDto(position.SecurityId, "Bond", SecurityStatusDto.Active, "MBS pool", "USD", empty, empty, [], [], 1,
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null));
            var books = Substitute.For<ILedgerBookService>();
            books.GetBookAsync(context.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(context.LedgerBookId,
                "fund-alpha", context.FundStructureNodeId, context.FundStructureNodeKind, "GAAP", "USD", now, now,
                AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: context.AccountingPolicyId, AccountingPolicyVersion: context.AccountingPolicyVersion));
            books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([new LedgerPeriodDto(periodId,
                context.LedgerBookId, 2026, 6, "June", new(2026, 6, 1), new(2026, 6, 30), LedgerPeriodStatusDto.Open, now, null, 1,
                AccountingBasisKindDto.Gaap, context.AccountingPolicyId, context.AccountingPolicyVersion)]);
            var ledger = Substitute.For<ILedgerJournalStore>();
            ledger.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(periodId, context.LedgerBookId,
                2026, 6, "June", new(2026, 6, 1), new(2026, 6, 30), "Open", now, null, 1));
            var policy = Substitute.For<IAccountingPolicyService>();
            policy.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(new AccountingPolicyDto(
                context.AccountingPolicyId!, AccountingBasisKindDto.Gaap, context.AccountingPolicyVersion!, "GAAP", new(2026, 1, 1), null, true,
                "{}", now, RulePack: new AccountingPolicyRulePackDto("factor-pack", "1", [new AccountingPolicyRuleDto("factor-rule", AccountingTreatmentKindDto.General, "1")])));
            var configuration = Substitute.For<IAccountingConfigurationService>();
            var authority = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
            var real = new AssetAccountingEventSpineService(store, positions, securityQuery, books, policy, configuration, authority, ledger, securities);
            var proxy = Substitute.For<IAssetAccountingEventSpineService>();
            var fixture = new FactorRecoveryFixture
            {
                Request = request,
                Ledger = ledger,
                Store = store,
                Books = books,
                Configuration = configuration,
                Prepare = new FactorPaydownLotInstructionService(ledger, securities, positions, new FactorPaydownProjectionService()),
                Spine = proxy,
                CurrentLot = FactorPaydownTestData.LotRecord(lot, new LedgerAccount("Assets:Investment", LedgerAccountType.Asset), now)
            };
            ledger.ListOpenTaxLotsByAssetScopeAsync(context.LedgerBookId, position.SecurityId, position.PositionId, request.EffectiveDate,
                Arg.Any<CancellationToken>()).Returns(_ => [fixture.CurrentLot]);
            ledger.GetTaxLotsByIdsAsync(context.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ => [fixture.CurrentLot]);
            var chart = new[] { "Assets:Cash", "Assets:Investment", "Income:Realized Gain", "Expense:Realized Loss" }
                .Select(path => new ChartOfAccountsNodeDto(path, path, path, path.StartsWith("Expense", StringComparison.Ordinal) ? "Expense" : path.StartsWith("Income", StringComparison.Ordinal) ? "Revenue" : "Asset")).ToArray();
            configuration.GetWorkspaceAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<string?>())
                .Returns(new AccountingConfigurationWorkspaceDto("fund-alpha", context.LedgerBookId, AccountingConfigurationStatusDto.Draft, "test", now, [], chart, [], [], [], []));
            configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>()).Returns(_ => fixture.DryRun());
            authority.BuildAuthoritativeCandidateWriteAsync(Arg.Any<PostingRuleJournalCandidateRequestDto>(), Arg.Any<AssetAccountingCandidateAuthorityContext>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var snapshot = fixture.CurrentProjection!;
                    if (fixture.BeforeCandidateWrite is { } before)
                        await before();
                    return fixture.CandidateWrite(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0), snapshot);
                });
            proxy.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call =>
                real.GetLatestAsync(call.ArgAt<Guid>(0), call.ArgAt<long>(1), call.ArgAt<CancellationToken>(2)));
            void Bind(ProjectAssetAccountingEventRequestDto projected)
            {
                fixture.CurrentProjection = projected;
                position = position with { OriginEvent = projected.EconomicEvent, ProjectionLineage = projected.ProjectionLineage, RetainedEvidence = projected.RetainedEvidence };
            }
            proxy.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                Bind(call.ArgAt<ProjectAssetAccountingEventRequestDto>(0));
                var retained = await real.ProjectAsync(fixture.CurrentProjection!, call.ArgAt<CancellationToken>(1));
                var drift = fixture.AfterProject;
                fixture.AfterProject = null;
                drift?.Invoke();
                return retained;
            });
            proxy.ReprepareUnpostedFactorPaydownAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<AssetLotMutationInstructionDto>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Bind(call.ArgAt<ProjectAssetAccountingEventRequestDto>(0));
                    return real.ReprepareUnpostedFactorPaydownAsync(fixture.CurrentProjection!, call.ArgAt<AssetLotMutationInstructionDto>(1), call.ArgAt<long>(2), call.ArgAt<string>(3), call.ArgAt<CancellationToken>(4));
                });
            proxy.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
                real.BuildPostingCandidateAsync(call.ArgAt<AssetAccountingPostingCandidateRequestDto>(0), call.ArgAt<CancellationToken>(1)));
            return fixture;
        }

        private RuleDryRunResultDto DryRun(ProjectAssetAccountingEventRequestDto? preparation = null)
        {
            var source = preparation ?? CurrentProjection!;
            var generated = source.ProjectedEffect.Lines.Select((line, index) => new GeneratedPostingLineDto($"factor-{index}", line.AccountId,
                line.Debit > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit, "factor", line.Debit > 0m ? line.Debit : line.Credit,
                line.Currency, line.Dimensions, line.Description)).ToArray();
            return new RuleDryRunResultDto(source.Scope.FundProfileId, source.Scope.LedgerBookId, source.EconomicEvent.EventType, source.EconomicEvent.EffectiveDate,
                source.EventAmount, source.Currency, true, "factor-rule", [new AccountingRuleDryRunMatchDto("factor-rule", "Factor", "1", 100, true, [], [])], [], [], generated);
        }

        private AccountingPostingCandidateWriteResult CandidateWrite(PostingRuleJournalCandidateRequestDto request,
            ProjectAssetAccountingEventRequestDto preparation)
        {
            if (preparation.ProjectedEffect.Lines.Any(line => line.AccountId == RejectCashAccount))
                throw new InvalidOperationException("Selected cash chart account is unavailable.");
            var dryRun = DryRun(preparation);
            var id = Guid.NewGuid();
            var lines = dryRun.GeneratedPostingLines.Select(line => new LedgerEntry(Guid.NewGuid(), id, request.AccountingTimestamp,
                new LedgerAccount(line.AccountPath, line.AccountPath.StartsWith("Expense", StringComparison.Ordinal) ? LedgerAccountType.Expense :
                    line.AccountPath.StartsWith("Income", StringComparison.Ordinal) ? LedgerAccountType.Revenue : LedgerAccountType.Asset),
                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m, line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m,
                request.Description)).ToArray();
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), request.AggregateId, request.PeriodId, request.EffectiveDate,
                request.AccountingTimestamp, $"factor:{request.SourceEventId:N}", SourceEventId: request.SourceEventId, ExpectedVersion: request.ExpectedPeriodVersion,
                LedgerBookId: request.LedgerBookId)
            {
                Actor = request.Actor,
                BookContext = request.BookContext,
                BookPositionId = request.BookPositionId,
                EconomicEvent = request.EconomicEvent,
                ProjectionLineage = request.ProjectionLineage,
                RulePackReference = request.RulePackReference,
                LotFactorPaydown = request.AssetLotMutation!.FactorPaydown,
                Evidence = request.RetainedEvidence.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                    item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc, item.RetainedBy,
                    item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference, Reviewer: item.ReviewedBy,
                    ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate, EvidenceVersion: item.EvidenceVersion,
                    ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray()
            };
            var write = new LedgerJournalEntryWrite(new JournalEntry(id, request.AccountingTimestamp, request.Description, lines,
                new JournalEntryMetadata(ActivityType: request.SourceEventType, EffectiveDate: request.EffectiveDate)), request.AggregateId, request.PeriodId,
                command.CommandId, AccountingBasis: request.AccountingBasis, AccountingPolicyId: request.PolicyId!,
                AccountingPolicyVersion: request.BookContext!.AccountingPolicyVersion!, RuleId: "factor-rule", RuleVersion: "1",
                SourceEventId: request.SourceEventId, PostingCommand: command, LedgerBookId: request.LedgerBookId);
            var candidate = new PostingRuleJournalCandidateResultDto(dryRun, "factor-rule", "1", dryRun.GeneratedPostingLines, command, id,
                dryRun.GeneratedPostingLines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => line.Amount),
                dryRun.GeneratedPostingLines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => line.Amount), 0m, true, false, true, false,
                request.EvidenceLinks, [])
            {
                BookContext = request.BookContext,
                BookPositionId = request.BookPositionId,
                EconomicEvent = request.EconomicEvent,
                ProjectionLineage = request.ProjectionLineage,
                RulePackReference = request.RulePackReference
            };
            return new(candidate, write);
        }
    }
}
