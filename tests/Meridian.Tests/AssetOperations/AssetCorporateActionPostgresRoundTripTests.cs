using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>Real projection and mapping through governed draft, independent approval and atomic PostgreSQL posting.</summary>
[Trait("Category", "Integration")]
public sealed class AssetCorporateActionPostgresRoundTripTests
{
    [LedgerDatabaseFact]
    public Task SameSecurityStockSplit_RealMappedProjectionDraftApprovalPostAndRestartReconcile()
        => RunStockSplitAsync(multipleLots: false);

    [LedgerDatabaseFact]
    public Task SameSecurityStockSplit_TwoLotsWithAggregatedRuleLinesPostAndReplayAsOneBatch()
        => RunStockSplitAsync(multipleLots: true);

    private static async Task RunStockSplitAsync(bool multipleLots)
    {
        const string fund = "corporate-pipeline-fund";
        const string tenant = "corporate-pipeline-tenant";
        const string company = "corporate-pipeline-company";
        const string preparer = "fund-accountant";
        const string approver = "independent-controller";
        const string policy = "corporate-gaap";
        const string rule = "carrying-transfer";
        const string assetPath = "assets/investments";
        const string assetName = "Investments";
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var acquired = date.AddYears(-1);
        var periodStart = new DateOnly(date.Year, date.Month, 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = server.ConnectionString,
            SchemaName = server.CreateSchemaName("corporate_pipeline_ledger"),
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };
        var securityOptions = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("corporate_pipeline_security"),
            PreloadProjectionCache = false
        };
        var assetOptions = new AssetOperationsOptions
        { ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("corporate_pipeline_asset") };
        await new LedgerMigrationRunner(ledgerOptions).EnsureMigratedAsync(ct);
        await new SecurityMasterMigrationRunner(securityOptions).EnsureMigratedAsync(ct);
        await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync(ct);
        var securities = new PostgresSecurityMasterStore(securityOptions);
        PostgresAssetOperationsProjectionStore? assets = null;
        var journal = new PostgresLedgerJournalStore(ledgerOptions, backfillSecurityMaster: () => securities, backfillPositions: () => assets!);
        assets = new PostgresAssetOperationsProjectionStore(assetOptions, journal);
        var books = new PostgresLedgerBookService(journal);
        var bookId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        await journal.SaveLedgerBookAsync(new(bookId, fund, ownerId, FundStructureNodeKindDto.Fund,
            "Corporate action GAAP book", "USD", now, now, AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: policy, AccountingPolicyVersion: "v1"), ct);
        var period = await journal.SavePeriodAsync(new(Guid.NewGuid(), bookId, periodStart.Year, periodStart.Month,
            periodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture), periodStart, periodStart.AddMonths(1).AddDays(-1),
            "Open", now, null, 0), 0, ct: ct);
        var empty = JsonSerializer.SerializeToElement(new { });
        await securities.UpsertProjectionAsync(new SecurityProjectionRecord(securityId, "Equity", SecurityStatusDto.Active,
            "Ordinary shares", "USD", "ISIN", "TESTSPLIT" + securityId.ToString("N"), empty, empty, empty, 1,
            new DateTimeOffset(acquired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), null, [], []), ct);
        var security = (await securities.GetProjectionAsync(securityId, ct))!;
        RetainedEvidenceIdentityDto Evidence(string id, DateOnly effective, string subject, Guid subjectId, string hash)
            => new(id, "document://corporate-action/" + id, hash, "custodian", id,
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus, preparer, now, effective, 1, now,
                "retention-service", subject, subjectId.ToString("D"));
        var acquisitionEvidence = Evidence("original-acquisition", acquired, "OpenLotAcquisition", lotId, new string('a', 64));
        var successorEvidence = Evidence("reviewed-successor", acquired, "OpenLotAcquisition", successorId, new string('b', 64));
        var securityEvidence = Evidence("security-projection", acquired, "SecurityMasterProjection", securityId,
            OpenLotAmortization.SecurityHash(security));
        var account = new LedgerAccount(assetName, LedgerAccountType.Asset);
        var lot = await journal.SaveTaxLotAsync(new LedgerTaxLotRecord(lotId, bookId, account, "original-shares", acquired,
            100m, 100m, 10m, "USD", now, now, EvidenceRef: acquisitionEvidence.EvidenceId, Version: 1,
            SecurityId: securityId, BookPositionId: positionId,
            Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m, 1_000m, 1_000m, acquired, null, [acquisitionEvidence])), ct);
        var dimensions = new LedgerDimensionSetDto(FundId: fund, EntityId: company,
            InstrumentId: securityId, BookId: bookId.ToString("D"))
        { PositionId = positionId };
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction);
        var context = new AccountingBookContextDto(bookId, fund, ownerId, FundStructureNodeKindDto.Fund,
            "Corporate action GAAP book", "USD", AccountingBasisKindDto.Gaap, policy, "v1", period.PeriodId, dimensions);
        var acquisitionOrigin = new EconomicEventReferenceDto(Guid.NewGuid(), "Trade", 1, acquired, now,
            "custodian", acquisitionEvidence.SourceReference)
        { SecurityId = securityId, BookPositionId = positionId, RetainedEvidence = [acquisitionEvidence], EvidenceLinks = [acquisitionEvidence.EvidenceUri] };
        var roleId = Guid.NewGuid();
        var position = await assets.UpsertAsync(new InstrumentRoleDto(roleId, securityId, fund, "Fund", InstrumentRoleKinds.Holder,
                InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, acquired, OriginEvent: acquisitionOrigin,
                EvidenceLinks: acquisitionOrigin.EvidenceLinks),
            new BookPositionDto(positionId, securityId, roleId, context, BookPositionSides.Long, "Active", acquired,
                OriginEvent: acquisitionOrigin, EvidenceLinks: acquisitionOrigin.EvidenceLinks)
            { RetainedEvidence = [acquisitionEvidence] },
            null, 0, new(approver, "document://position-approval/corporate", "Retain owned shares for reviewed split.", now), ct);
        var reviewedPositionVersion = position.Version + 1;
        var successor = new OpenLotCorporateActionSuccessorDto(successorId, "split-shares", assetName, security, securityEvidence,
            positionId, reviewedPositionVersion, 200m, 100m, CorporateActionSuccessorRoleDto.Successor, [], successorEvidence, assetPath);
        var mutation = new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.CarryOver, securityId, securityId,
            200m, 1_000m, 1m, CorporateActionHoldingPeriodTreatmentDto.CarryOver, SourceLotId: lotId,
            ExpectedSourceLotVersion: lot.Version, SourceBefore: new(100m, 1_000m, 1_000m), SourceAfter: new(0m, 0m, 0m),
            TargetLotId: successorId, TargetOperation: CorporateActionLotTargetOperationDto.Create,
            TargetAfter: new(200m, 1_000m, 1_000m), BasisAmount: 1_000m, SourceQuantity: 100m,
            SourceCarryingAmount: 1_000m, SourceBasisAmount: 1_000m);
        var instruction = new OpenLotCorporateActionInstructionDto(actionId, CorporateActionAccountingTypeDto.StockSplit, date,
            lot.ToOpenLot(), security, securityEvidence, reviewedPositionVersion, [successor], [mutation], assetName);
        if (multipleLots)
        {
            var secondLotId = Guid.NewGuid();
            var secondSuccessorId = Guid.NewGuid();
            var secondAcquired = acquired.AddMonths(1);
            var secondAcquisitionEvidence = Evidence("second-original-acquisition", secondAcquired, "OpenLotAcquisition", secondLotId, new string('c', 64));
            var secondSuccessorEvidence = Evidence("second-reviewed-successor", secondAcquired, "OpenLotAcquisition", secondSuccessorId, new string('d', 64));
            var secondLot = await journal.SaveTaxLotAsync(lot with
            {
                TaxLotRecordId = secondLotId,
                LotId = "second-original-shares",
                AcquiredDate = secondAcquired,
                EvidenceRef = secondAcquisitionEvidence.EvidenceId,
                Acquisition = lot.Acquisition! with { HoldingPeriodStartDate = secondAcquired, Evidence = [secondAcquisitionEvidence] }
            }, ct);
            var secondSuccessor = successor with
            { TaxLotRecordId = secondSuccessorId, LotId = "second-split-shares", AcquisitionEvidence = secondSuccessorEvidence };
            instruction = instruction with
            {
                AdditionalPredecessors = [new(secondLot.ToOpenLot(), [secondSuccessor], assetName)],
                Mutations = [mutation, mutation with { SourceLotId = secondLotId, TargetLotId = secondSuccessorId }]
            };
        }
        var scope = new AssetAccountingEventScopeDto(securityId, security.Version, positionId, reviewedPositionVersion, bookId,
            period.PeriodId, AccountingBasisKindDto.Gaap, fund, tenant, company, dimensions);
        var prepared = CanonicalLotCorporateActionServiceTests.ProjectAndMap(instruction, scope, period.Version, assetPath, preparer, now, aggregateJournalLines: multipleLots);
        var mapped = prepared.Mapped;
        instruction = prepared.Reviewed;
        // Retain the reviewed corporate event on the governed position revision before drafting.
        // Acquisition origin remains intact while current projection lineage supplies event authority.
        var currentRole = (await assets.GetSecurityAsync(securityId, ct)).InstrumentRoles.Single();
        position = await assets.UpsertAsync(currentRole, position with
        {
            Version = reviewedPositionVersion,
            ProjectionLineage = mapped.Event.ProjectionLineage,
            RetainedEvidence = mapped.Event.RetainedEvidence
        }, null, position.Version,
            new(approver, "document://position-approval/corporate-revision", "Retain reviewed split projection and evidence.", now), ct);
        position.Version.Should().Be(reviewedPositionVersion);
        var policies = new AccountingPolicyService();
        await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Gaap, policy, "v1",
            "Governed corporate actions", acquired,
            RulePack: new AccountingPolicyRulePackDto("corporate-action-gaap", "v1",
                [new AccountingPolicyRuleDto(rule, AccountingTreatmentKindDto.General, "v1", SourceEventType: eventType,
                    RequiresEvidence: true, RequiresApproval: true, AllowsAutoPosting: false)])), ct);
        var configurationStore = new PostgresAccountingConfigurationStore(ledgerOptions);
        var configuration = new AccountingConfigurationService(configurationStore, configurationStore, books);
        await configuration.UpsertChartNodeAsync(new(fund, new("investments", assetPath, assetName, "Asset"), approver,
            CompanyId: company, LedgerBookId: bookId, TenantId: tenant), ct);
        await configuration.UpsertPostingRuleAsync(new(fund,
            new PostingRuleDto(rule, "Canonical stock split", eventType, "generated", "v1", EffectiveFrom: acquired, Priority: 100,
                Formulas: [new("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)],
                GeneratedPostings:
                [new("predecessor", assetPath, AccountingTemplateLineSideDto.Credit, "amount", 0m, "USD"),
                 new("successor", assetPath, AccountingTemplateLineSideDto.Debit, "amount", 0m, "USD")]), approver,
            CompanyId: company, LedgerBookId: bookId, TenantId: tenant), ct);
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)), books, policies, taxLotStore: journal);
        var referenceQuery = Substitute.For<ISecurityMasterQueryService>();
        referenceQuery.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(securityId, security.AssetClass, security.Status, security.DisplayName,
                security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
                security.Version, security.EffectiveFrom, security.EffectiveTo));
        referenceQuery.GetRecordedByIdAsOfAsync(securityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(securityId, security.AssetClass, security.Status, security.DisplayName,
                security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
                security.Version, security.EffectiveFrom, security.EffectiveTo));
        var spine = new AssetAccountingEventSpineService(assets, assets, referenceQuery, books, policies, configuration, candidateBuilder, journal);
        await new CanonicalLotCorporateActionService(journal, securities, assets, spine)
            .DraftAsync(mapped, instruction, assetPath, preparer, now, "Reviewed two-for-one stock split", ct);
        var eventId = mapped.Event.EconomicEvent.EventId;
        var drafted = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        drafted.DraftedLotMutation!.Intent.Should().Be(AssetLotMutationIntentDto.CorporateAction);
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(drafted.DraftedCandidateResult!.PostingCommand!.LotCorporateAction),
            JsonSerializer.SerializeToElement(instruction)).Should().BeTrue();
        drafted.DraftedCandidateResult.GeneratedPostingLines.Should().HaveCount(2);
        drafted.DraftedCandidate!.EventAmount.Should().Be(multipleLots ? 2_000m : 1_000m);
        (await journal.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty("drafting has no posting authority");
        var approvalId = Guid.NewGuid().ToString("D");
        var approvedAt = DateTimeOffset.UtcNow;
        var approvalEvidence = new RetainedEvidenceIdentityDto("split-approval", "document://corporate-action/split-approval", new string('e', 64),
            "approvals", approvalId, RetainedEvidenceIdentityValidator.AcceptedReviewStatus, approver, approvedAt, date, 1,
            approvedAt, approver, AssetAccountingEvidenceSubjects.PostingApproval,
            AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(eventId, 1, fund, bookId, period.PeriodId,
                AccountingBasisKindDto.Gaap, approvalId, drafted.DraftedCandidateFingerprint!, tenant, company));
        var request = new PostPostingRuleJournalCandidateRequestDto(drafted.DraftedCandidate!, approver, approvalId,
            ApprovalNotes: "Approve exact retained lot split and basis conservation.", TenantId: tenant, CompanyId: company)
        { ApprovalEvidence = [approvalEvidence] };
        var posted = await new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets)
            .PostCandidateAsync(request, ct);
        posted.WasReplay.Should().BeFalse();
        posted.TaxLotMutationBatchId.Should().NotBeNull();
        var retained = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        retained.Stages.Select(stage => stage.Stage).Should().Equal(AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected, AssetAccountingLifecycleStageDto.Drafted,
            AssetAccountingLifecycleStageDto.Approved, AssetAccountingLifecycleStageDto.Posted);
        var batch = (await journal.GetAtomicTaxLotPostingAsync(posted.TaxLotMutationBatchId!.Value, ct))!;
        if (!multipleLots)
            await AssertPostedLifecycleRejectsCounterfeitReceiptsAsync(retained, batch, ct);
        var groups = OpenLotCorporateAction.Groups(instruction);
        batch.Mutations.Should().HaveCount(groups.Count * 2);
        batch.Journal.Entry.Lines.Should().HaveCount(groups.Count * 2);
        batch.Journal.Entry.Lines.Select(line => line.Dimensions!.TaxLotId).Distinct().Should().HaveCount(groups.Count * 2);
        foreach (var group in groups)
        {
            var close = batch.Mutations.Single(row => row.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose
                && row.TaxLotRecordId == group.ExpectedLot.TaxLotRecordId);
            var successorRow = batch.Mutations.Single(row => row.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor
                && row.TaxLotRecordId == group.Successors[0].TaxLotRecordId);
            close.LotAfter.OpenQuantity.Should().Be(0m);
            var successorLot = successorRow.LotAfter.ToOpenLot();
            successorLot.OpenQuantity.Should().Be(200m);
            successorLot.OpenFunctionalCostBasis.Should().Be(1_000m);
            successorLot.AcquiredDate.Should().Be(group.ExpectedLot.AcquiredDate);
            successorLot.Acquisition.HoldingPeriodStartDate.Should().Be(group.ExpectedLot.Acquisition.HoldingPeriodStartDate);
            successorLot.Acquisition.CorporateActionLineage!.PredecessorTaxLotRecordId.Should().Be(group.ExpectedLot.TaxLotRecordId);
            successorLot.Acquisition.CorporateActionLineage.ReportingTags.Should().BeEmpty();
            var proof = CanonicalCorporateActionLotProjection.Project(batch.Journal.Entry, account,
                close.LotBefore!.ToOpenLot(), close.LotAfter.ToOpenLot(), [successorLot]);
            proof.Successors.Should().ContainSingle();
        }
        batch.Journal.Entry.Lines.Should().OnlyContain(line => line.Account == account);
        batch.Journal.Entry.Lines.Sum(line => line.Debit - line.Credit).Should().Be(0m);
        var restartedSecurities = new PostgresSecurityMasterStore(securityOptions);
        PostgresAssetOperationsProjectionStore? restartedAssets = null;
        var restartedJournal = new PostgresLedgerJournalStore(ledgerOptions, backfillSecurityMaster: () => restartedSecurities,
            backfillPositions: () => restartedAssets!);
        restartedAssets = new PostgresAssetOperationsProjectionStore(assetOptions, restartedJournal);
        var replay = await new AccountingPostingCandidatePostService(candidateBuilder, restartedJournal, assetAccountingEventStore: restartedAssets)
            .PostCandidateAsync(request, ct);
        replay.WasReplay.Should().BeTrue();
        replay.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        (await restartedJournal.GetByPeriodAsync(period.PeriodId, ct)).Should().ContainSingle();
        var openLots = await restartedJournal.ListOpenTaxLotsAsync(bookId, account, ct);
        openLots.Should().HaveCount(groups.Count);
        openLots.Should().OnlyContain(openLot => openLot.ToOpenLot().OpenQuantity == 200m);
        var repeatedBatch = (await restartedJournal.GetAtomicTaxLotPostingAsync(replay.TaxLotMutationBatchId!.Value, ct))!;
        repeatedBatch.Journal.Entry.Lines.Select(line => line.EntryId).Should().Equal(batch.Journal.Entry.Lines.Select(line => line.EntryId));
    }

    private static async Task AssertPostedLifecycleRejectsCounterfeitReceiptsAsync(
        AssetAccountingEventSpineDto posted, AtomicTaxLotJournalResult receipt, CancellationToken ct)
    {
        var source = receipt.Mutations.Single(row => row.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose);
        var target = receipt.Mutations.Single(row => row.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor);
        var authority = Substitute.For<ILedgerJournalStore>();
        authority.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([receipt.Journal]));
        AtomicTaxLotJournalResult? returnedReceipt = receipt;
        authority.GetAtomicTaxLotPostingAsync(receipt.MutationBatchId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(returnedReceipt));
        await AssetAccountingEventProjectionRules.ValidateDurablePostedImpactAsync(posted, authority, ct);

        var changedTags = receipt.Journal.Entry.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value);
        changedTags["lotCorporateActionHash"] = new string('f', 64);
        var changedJournal = new JournalEntry(receipt.Journal.Entry.JournalEntryId, receipt.Journal.Entry.Timestamp,
            receipt.Journal.Entry.Description, receipt.Journal.Entry.Lines, receipt.Journal.Entry.Metadata with { Tags = changedTags });
        (string Reason, AtomicTaxLotJournalResult? Receipt)[] counterfeits =
        [
            ("missing receipt", null),
            ("different batch kind", receipt with { MutationKind = AtomicTaxLotMutationKind.Acquisition }),
            ("missing predecessor close", receipt with { Mutations = [target] }),
            ("changed predecessor snapshot", receipt with { Mutations = [source with
                { LotBefore = source.LotBefore! with { OpenQuantity = source.LotBefore!.OpenQuantity - 1m } }, target] }),
            ("changed successor identity", receipt with { Mutations = [source, target with { TaxLotRecordId = Guid.NewGuid() }] }),
            ("changed successor version", receipt with { Mutations = [source, target with
                { ResultVersion = 2, LotAfter = target.LotAfter with { Version = 2 } }] }),
            ("changed successor carrying basis", receipt with { Mutations = [source, target with
                { LotAfter = target.LotAfter with { BasisAdjustment = new(receipt.MutationBatchId,
                    OpenLotBasisAdjustmentReasons.CorporateAction, target.LotAfter.OpenQuantity, 1_001m, 1_001m) } }] }),
            ("changed successor original basis", receipt with { Mutations = [source, target with
                { LotAfter = target.LotAfter with { Acquisition = target.LotAfter.Acquisition! with
                    { TransactionCostBasis = 1_001m, FunctionalCostBasis = 1_001m } } }] }),
            ("changed retained instruction hash", receipt with { Journal = receipt.Journal with { Entry = changedJournal } })
        ];
        foreach (var counterfeit in counterfeits)
        {
            returnedReceipt = counterfeit.Receipt;
            var attest = () => AssetAccountingEventProjectionRules.ValidateDurablePostedImpactAsync(posted, authority, ct);
            await attest.Should().ThrowAsync<InvalidOperationException>(counterfeit.Reason);
        }
    }
}
