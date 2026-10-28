using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_RealApprovalBridgePostsOneCanonicalJournalAndAtomicLotMutationAfterRestartRetry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = timeout.Token;
        await using var fixture = await AmortFixture.CreateAsync(premium: true,
            accountingBasis: AccountingBasisKindDto.Gaap, factorPool: true);
        const string fund = "amort-fund";
        const string tenant = "factor-approval-tenant";
        const string company = "factor-approval-company";
        const string preparer = "fund-accountant";
        const string approver = "independent-controller";
        const string rule = "factor-approval-principal";
        const string cashPath = "assets/cash";
        const string carryingPath = "assets/investments";
        const string gainPath = "income/realized-gain";
        const string lossPath = "expense/realized-loss";
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-real-factor-approval-" + Guid.NewGuid().ToString("N"));
        var assetOptions = new AssetOperationsOptions
        {
            ConnectionString = fixture.Options.ConnectionString,
            Schema = "approval_asset_" + Guid.NewGuid().ToString("N")
        };
        try
        {
            Directory.CreateDirectory(dataRoot);
            await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync(ct);
            var assets = new PostgresAssetOperationsProjectionStore(assetOptions, fixture.Store);
            var positionSnapshot = await fixture.Positions.GetSecurityAsync(fixture.Security.SecurityId, ct);
            var retainedPosition = positionSnapshot.BookPositions.Single();
            var dimensions = new LedgerDimensionSetDto(FundId: fund, EntityId: company,
                InstrumentId: fixture.Security.SecurityId, BookId: fixture.BookId.ToString("D"))
            { PositionId = fixture.PositionId };
            // The event store has the exact already-retained position version. The atomic ledger
            // still locks/rechecks the original reference authority configured by AmortFixture.
            await assets.UpsertAsync(positionSnapshot.InstrumentRoles.Single(), retainedPosition, null, 0,
                new AssetOperationsWriteApprovalDto(approver, "document://factor-test/position",
                    "Retain the existing owned position for the accounting-event stream.", DateTimeOffset.UtcNow), ct);
            var books = new PostgresLedgerBookService(fixture.Store);
            var policies = new AccountingPolicyService();
            await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Gaap,
                "amort-policy", "1", "Factor principal accounting", new DateOnly(2025, 1, 1),
                RulePack: new AccountingPolicyRulePackDto("factor-approval-pack", "1",
                    [new AccountingPolicyRuleDto(rule, AccountingTreatmentKindDto.General, "1",
                        SourceEventType: "AssetAccounting.CorporateAction", RequiresEvidence: true,
                        RequiresApproval: true, AllowsAutoPosting: false)])), ct);
            var configurationStore = new PostgresAccountingConfigurationStore(fixture.Options);
            var configuration = new AccountingConfigurationService(configurationStore, configurationStore, books);
            foreach (var (id, path, name, type) in new[]
            {
                ("cash", cashPath, LedgerAccounts.Cash.Name, "Asset"),
                ("carrying", carryingPath, AmortAccount.Name, "Asset"),
                ("gain", gainPath, LedgerAccounts.RealizedGain.Name, "Revenue"),
                ("loss", lossPath, LedgerAccounts.RealizedLoss.Name, "Expense")
            })
                await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(fund,
                    new ChartOfAccountsNodeDto(id, path, name, type), approver,
                    CompanyId: company, LedgerBookId: fixture.BookId, TenantId: tenant), ct);
            await configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(fund,
                new PostingRuleDto(rule, "Reviewed MBS principal paydown", "AssetAccounting.CorporateAction", "generated", "1",
                    EffectiveFrom: new DateOnly(2025, 1, 1), Priority: 100,
                    Formulas:
                    [new(OpenLotFactorPaydown.PrincipalComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m),
                     new(OpenLotFactorPaydown.BasisRelievedComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m),
                     new(OpenLotFactorPaydown.RealizedGainComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m),
                     new(OpenLotFactorPaydown.RealizedLossComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m)],
                    GeneratedPostings:
                    [new("cash", cashPath, AccountingTemplateLineSideDto.Debit, OpenLotFactorPaydown.PrincipalComponent, 0m, "USD"),
                     new("carrying", carryingPath, AccountingTemplateLineSideDto.Credit, OpenLotFactorPaydown.BasisRelievedComponent, 0m, "USD"),
                     new("gain", gainPath, AccountingTemplateLineSideDto.Credit, OpenLotFactorPaydown.RealizedGainComponent, 0m, "USD"),
                     new("loss", lossPath, AccountingTemplateLineSideDto.Debit, OpenLotFactorPaydown.RealizedLossComponent, 0m, "USD")]),
                approver, CompanyId: company, LedgerBookId: fixture.BookId, TenantId: tenant), ct);
            // This read-only reference adapter returns the exact PostgreSQL projection. The actual
            // atomic posting rechecks its version/hash under locks. This is not historical market-data proof.
            var reference = Substitute.For<ISecurityMasterQueryService>();
            var security = fixture.Security;
            var detail = new SecurityDetailDto(security.SecurityId, security.AssetClass, security.Status,
                security.DisplayName, security.Currency, security.CommonTerms, security.AssetSpecificTerms,
                security.Identifiers, security.Aliases, security.Version, security.EffectiveFrom, security.EffectiveTo);
            reference.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(detail);
            reference.GetRecordedByIdAsOfAsync(security.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(detail);
            var assetOperations = new AssetOperationsReadService(assets, reference, positionProjectionStore: assets);
            var candidateBuilder = new AccountingPostingCandidateService(configuration,
                new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)),
                books, policies, assetOperationsQueryService: assetOperations,
                taxLotStore: fixture.Store, factorPaydownProjector: new FactorPaydownProjectionService());
            var spine = new AssetAccountingEventSpineService(assets, assets, reference, books, policies,
                configuration, candidateBuilder, fixture.Store, fixture.Securities);
            var sourceHash = Sha256Digest.ComputeUtf8("retained-factor:" + fixture.PositionId.ToString("D"));
            var projectedFactor = FactorPaydownTestData.Projection([fixture.Lot.ToOpenLot()], 0.95m, 0.90m,
                date: FirstPaydown, sourceHash: sourceHash);
            var now = DateTimeOffset.UtcNow;
            var factorEvidence = new RetainedEvidenceIdentityDto("factor-schedule-approval-bridge",
                "document://factor-test/reviewed-schedule", sourceHash, "custodian", projectedFactor.EconomicEvent!.SourceEntityId!,
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus, "source-controller", now, FirstPaydown,
                1, now, "retention-service", AssetAccountingEvidenceSubjects.Event, projectedFactor.EconomicEvent.EventId.ToString("D"));
            var preparation = await new FactorPaydownLotInstructionService(fixture.Store, fixture.Securities,
                fixture.Positions, new FactorPaydownProjectionService()).PrepareAsync(new FactorPaydownPreparationRequest(
                    fixture.BookId, security.SecurityId, fixture.PositionId, fixture.Period.PeriodId, fixture.Period.Version,
                    AccountingBasisKindDto.Gaap, fund, FirstPaydown, 0.95m, 0.90m,
                    projectedFactor.EconomicEvent.SourceDomain!, projectedFactor.EconomicEvent.SourceEntityId!, sourceHash,
                    projectedFactor.EconomicEvent.OccurredAtUtc, [factorEvidence], cashPath, carryingPath, gainPath, lossPath,
                    tenant, company, dimensions), preparer, DateTimeOffset.UtcNow, ct);
            // Publish the independently reviewed prospective factor model using the full graph
            // projection contract: approved next position version N and projector-produced state
            // N+1. The dedicated position/state tuple contract instead commits equal versions.
            // Both remain source projections; the lot of record changes only in atomic posting.
            var sourcePosition = (await fixture.Positions.GetBookPositionAsync(fixture.PositionId, ct))!;
            var eventPosition = (await assets.GetBookPositionAsync(fixture.PositionId, ct))!;
            eventPosition.Version.Should().Be(sourcePosition.Version);
            var observedPosition = sourcePosition with
            {
                Version = checked(sourcePosition.Version + 1),
                BookContext = sourcePosition.BookContext with { Dimensions = dimensions, PeriodId = fixture.Period.PeriodId },
                RetainedEvidence = preparation.ProjectRequest.RetainedEvidence,
                EvidenceLinks = preparation.ProjectRequest.EconomicEvent.EvidenceLinks
            };
            var prospective = new FactorPaydownProjectionService().Project(new FactorPaydownProjectionRequest(
                security.SecurityId, fixture.PositionId, observedPosition.Version, observedPosition.Version,
                fixture.Lot.ToFaceValueLot()!.ParFace, 0.95m, 0.90m, "USD", FirstPaydown,
                projectedFactor.EconomicEvent.OccurredAtUtc, projectedFactor.EconomicEvent.SourceDomain,
                projectedFactor.EconomicEvent.SourceEntityId!, sourceHash,
                preparation.ProjectRequest.EconomicEvent.EvidenceLinks,
                GeneratedAtUtc: preparation.ProjectRequest.ProjectionLineage.GeneratedAtUtc));
            prospective.ProducesPostingCandidate.Should().BeTrue();
            var observedEvent = prospective.EconomicEvent! with
            {
                EventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
                RetainedEvidence = preparation.ProjectRequest.RetainedEvidence,
                EvidenceLinks = preparation.ProjectRequest.EconomicEvent.EvidenceLinks
            };
            var observedLineage = prospective.Lineage! with
            {
                TriggerEvent = observedEvent,
                RetainedEvidence = preparation.ProjectRequest.RetainedEvidence,
                EvidenceLinks = preparation.ProjectRequest.EconomicEvent.EvidenceLinks
            };
            var observedState = prospective.EconomicState! with
            {
                SourceEvent = observedEvent,
                ProjectionLineage = observedLineage,
                RetainedEvidence = preparation.ProjectRequest.RetainedEvidence,
                EvidenceLinks = preparation.ProjectRequest.EconomicEvent.EvidenceLinks
            };
            observedPosition = observedPosition with { CurrentEconomicState = observedState, ProjectionLineage = observedLineage };
            var observation = new AssetOperationsProjectionDto(
                new AssetOperationSubjectDto(security.SecurityId, security.AssetClass, security.DisplayName,
                    security.PrimaryIdentifierValue, ["FactorProcessing"]), [], [], [], [], [], [], [], [],
                new AssetOperationsReadinessDto(security.SecurityId, "ReviewRequired", [], [], [], [],
                    DateTimeOffset.UtcNow, "SecurityMaster", security.SecurityId.ToString("D")), [])
            {
                InstrumentRoles = positionSnapshot.InstrumentRoles,
                BookPositions = [observedPosition],
                PositionEconomicStates = [observedState],
                ProjectionLineages = [observedLineage],
                RetainedEvidence = preparation.ProjectRequest.RetainedEvidence
            };
            var sourceApproval = new AssetOperationsWriteApprovalDto("source-controller",
                factorEvidence.EvidenceUri, "Retain the reviewed prospective factor graph before accounting.", DateTimeOffset.UtcNow);
            await fixture.Positions.UpsertAsync(observation, sourceApproval, ct);
            await assets.UpsertAsync(observation, sourceApproval, ct);
            preparation = await new FactorPaydownLotInstructionService(fixture.Store, fixture.Securities,
                fixture.Positions, new FactorPaydownProjectionService()).PrepareAsync(new FactorPaydownPreparationRequest(
                    fixture.BookId, security.SecurityId, fixture.PositionId, fixture.Period.PeriodId, fixture.Period.Version,
                    AccountingBasisKindDto.Gaap, fund, FirstPaydown, 0.95m, 0.90m,
                    projectedFactor.EconomicEvent.SourceDomain!, projectedFactor.EconomicEvent.SourceEntityId!, sourceHash,
                    projectedFactor.EconomicEvent.OccurredAtUtc, [factorEvidence], cashPath, carryingPath, gainPath, lossPath,
                    tenant, company, dimensions), preparer, DateTimeOffset.UtcNow, ct);
            var projected = await spine.ProjectAsync(preparation.ProjectRequest, ct);
            // PostgreSQL retains microseconds; force seven additional ticks in the immutable
            // original timestamp so restart replay must prove that exact schema equivalence.
            var accountingTicks = DateTimeOffset.UtcNow.UtcTicks;
            var accountingAt = new DateTimeOffset(accountingTicks - accountingTicks % 10 + 7, TimeSpan.Zero);
            var candidate = await spine.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(
                preparation.ProjectRequest.EventKind, preparation.ProjectRequest.Scope,
                preparation.ProjectRequest.EconomicEvent, preparation.ProjectRequest.ProjectionLineage,
                preparation.ProjectRequest.EventAmount, preparation.ProjectRequest.Currency, preparer,
                accountingAt, "Reviewed factor principal paydown", projected.Spine.SpineVersion,
                fixture.Period.Version, preparation.ProjectRequest.RetainedEvidence, preparation.LotMutation), ct);
            candidate.Candidate.HasBlockingIssues.Should().BeFalse();
            var canonical = new AccountingPostingCandidatePostService(candidateBuilder, fixture.Store,
                assetAccountingEventStore: assets);
            var vault = new FileEvidenceArtifactStore(dataRoot, NullLogger<FileEvidenceArtifactStore>.Instance);
            var draftPath = Path.Combine(dataRoot, "drafts.json");
            ManualJournalEntryWorkbenchService Restart() => new(new FileManualJournalEntryDraftStore(draftPath), configuration,
                configurationStore, reference, fixture.Store,
                evidenceArtifacts: vault, assetCandidatePosting: canonical, assetAccountingSource: spine);
            var workbench = Restart();
            var draft = await workbench.SaveAssetAccountingDraftAsync(candidate, preparer, ct);
            var submitted = await workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
                draft.JournalEntryId, fund, preparer, draft.Version, CorrelationId: "real-factor-submit",
                LedgerBookId: fixture.BookId, TenantId: tenant, CompanyId: company), ct);
            var approved = await workbench.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
                draft.JournalEntryId, fund, JournalEntryLifecycleActionDto.Approve, approver, submitted.Version,
                Notes: "Approve the exact reviewed principal, basis relief and realized loss.", CorrelationId: "real-factor-approve",
                LedgerBookId: fixture.BookId, TenantId: tenant, CompanyId: company), ct);
            approved.JournalEntry.AssetAccountingApproval.Should().NotBeNull();
            var post = new JournalEntryLifecycleActionRequestDto(draft.JournalEntryId, fund,
                JournalEntryLifecycleActionDto.Post, approver, approved.JournalEntry.Version,
                Notes: "Post the independently approved canonical factor paydown.", CorrelationId: "real-factor-post",
                LedgerBookId: fixture.BookId, TenantId: tenant, CompanyId: company);

            var posted = await workbench.ApplyLifecycleActionAsync(post, ct);
            var replay = await Restart().ApplyLifecycleActionAsync(post, ct);

            posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
            replay.JournalEntry.JournalEntryId.Should().Be(draft.JournalEntryId);
            replay.JournalEntry.AssetAccountingApproval.Should().BeEquivalentTo(approved.JournalEntry.AssetAccountingApproval);
            var actual = (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId], ct)).Single();
            actual.CurrentFactor.Should().Be(0.90m);
            actual.OpenQuantity.Should().Be(fixture.Lot.OpenQuantity);
            actual.ToOpenLot().OpenTransactionCostBasis.Should().Be(913_500m);
            actual.ToOpenLot().OpenFunctionalCostBasis.Should().Be(913_500m);
            var journals = await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId, ct);
            var journal = journals.Should().ContainSingle().Subject;
            journal.Entry.JournalEntryId.Should().Be(draft.JournalEntryId);
            var retained = (await assets.GetLatestAsync(candidate.Spine.EventId, candidate.Spine.EventVersion, ct))!.Projection;
            retained.PostedJournalImpact!.JournalEntryId.Should().Be(draft.JournalEntryId);
            retained.TaxLotMutationBatchId.Should().NotBeNull();
            var approvalStage = retained.Stages.Single(stage => stage.Stage == AssetAccountingLifecycleStageDto.Approved);
            var approvalEvidence = approvalStage.Evidence.Single();
            approvalEvidence.ReviewedAtUtc.Should().Be(approved.JournalEntry.AssetAccountingApproval!.ApprovedAtUtc);
            approvalEvidence.RetainedAtUtc.Should().BeOnOrAfter(approvalEvidence.ReviewedAtUtc);
            approvalStage.AttestedAtUtc.Should().Be(approvalEvidence.RetainedAtUtc);
            var batch = (await fixture.Store.GetAtomicTaxLotPostingAsync(retained.TaxLotMutationBatchId!.Value, ct))!;
            batch.MutationKind.Should().Be(AtomicTaxLotMutationKind.FactorPaydown);
            batch.FactorPaydown.Should().BeEquivalentTo(preparation.Instruction);
            batch.Mutations.Should().ContainSingle().Which.LotAfter.CurrentFactor.Should().Be(0.90m);
            journal.Entry.Lines.Single(line => line.Account == LedgerAccounts.Cash).Debit.Should().Be(50_000m);
            journal.Entry.Lines.Single(line => line.Account == AmortAccount).Credit.Should().Be(50_750m);
            journal.Entry.Lines.Single(line => line.Account == LedgerAccounts.RealizedLoss).Debit.Should().Be(750m);
            journal.Entry.Metadata.EvidenceReferences.Should().Contain(item =>
                item.SubjectType == AssetAccountingEvidenceSubjects.PostingApproval &&
                item.ContentHash == approved.JournalEntry.AssetAccountingApproval!.ContentHashSha256);
            var queue = await Restart().GetWorkbenchAsync(fund, fixture.BookId, ct, tenant, company);
            queue.Drafts.Should().ContainSingle().Which.JournalEntryId.Should().Be(draft.JournalEntryId);

            var retainedPost = posted.JournalEntry.AssetAccountingPostIntent!.CanonicalRequest;
            var reviewedInstruction = retainedPost.Candidate.AssetLotMutation!.FactorPaydown!;
            var changedInstruction = reviewedInstruction with { ExpectedSecurityHash = new string('f', 64) };
            var changedApproval = retainedPost.ApprovalEvidence.Single() with { ContentHashSha256 = new string('f', 64) };
            var alteredRetries = new (string Authority, PostPostingRuleJournalCandidateRequestDto Request)[]
            {
                ("candidate fingerprint", retainedPost with
                {
                    Candidate = retainedPost.Candidate with { Description = "Changed after the retained approval" }
                }),
                ("reviewed lot instruction", retainedPost with
                {
                    Candidate = retainedPost.Candidate with
                    {
                        AssetLotMutation = retainedPost.Candidate.AssetLotMutation with { FactorPaydown = changedInstruction }
                    }
                }),
                ("approval artifact hash", retainedPost with { ApprovalEvidence = [changedApproval] }),
                ("approving actor", retainedPost with { Actor = "another-controller" }),
                ("tenant scope", retainedPost with { TenantId = "another-tenant" })
            };
            var restartedCanonical = new AccountingPostingCandidatePostService(candidateBuilder, fixture.Restart(),
                assetAccountingEventStore: assets);
            foreach (var (authority, alteredRequest) in alteredRetries)
            {
                var alteredReplay = () => restartedCanonical.PostCandidateAsync(alteredRequest, ct);
                await alteredReplay.Should().ThrowAsync<InvalidOperationException>(
                    $"a restart retry cannot replace the committed {authority}");
            }

            (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId], ct))
                .Should().ContainSingle().Which.Should().BeEquivalentTo(actual,
                    "rejected retries cannot increment or alter the paid-down lot of record");
            (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId, ct)).Should().ContainSingle()
                .Which.Should().BeEquivalentTo(journal);
            (await fixture.Store.GetAtomicTaxLotPostingAsync(batch.MutationBatchId, ct)).Should().BeEquivalentTo(batch);
            (await assets.GetLatestAsync(candidate.Spine.EventId, candidate.Spine.EventVersion, ct))!.Projection
                .Should().BeEquivalentTo(retained, "rejected retries cannot append lifecycle authority");
        }
        finally
        {
            await using var cleanup = new NpgsqlConnection(fixture.Options.ConnectionString);
            await cleanup.OpenAsync(CancellationToken.None);
            await using var drop = cleanup.CreateCommand();
            drop.CommandText = $"drop schema if exists \"{assetOptions.Schema}\" cascade";
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, true);
        }
    }
}
