using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Meridian.Tests.Storage;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// A lot acquired through the Asset Accounting Event Spine must leave the lot of record with
/// canonical acquisition facts, or production disposal - which projects every open lot through the
/// canonical contract - refuses it and routes it to the legacy backfill queue.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class AssetAcquisitionLotPostgresRoundTripTests
{
    private const string Tenant = "acquisition-test-tenant";
    private const string Company = "acquisition-test-company";
    private const string Fund = "acquisition-test-fund";
    private const string Preparer = "fund-accountant";
    private const string Approver = "independent-controller";
    private const string Policy = "acquisition-gaap";
    private const string Rule = "asset-acquisition";
    private const string InvestmentAccount = "assets/investments";
    private const decimal Quantity = 100m;
    private const decimal UnitCost = 25m;
    private const decimal Amount = Quantity * UnitCost;
    // Posting stamps the journal with the current accounting time, and the period guard rejects a
    // posting date outside the open period, so the scenario lives in the current UTC month.
    private static readonly DateOnly EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly PeriodStart = new(EffectiveDate.Year, EffectiveDate.Month, 1);

    [LedgerDatabaseFact]
    public Task SpineUnitAcquisition_ApprovePost_RetainsCanonicalAcquisitionFactsTheDisposalGuardAccepts()
        => AssertSpineAcquisitionAsync(faceLot: false);

    [LedgerDatabaseFact]
    public Task SpineFaceAcquisition_WithStatedAmortization_RetainsCanonicalFaceFacts()
        => AssertSpineAcquisitionAsync(faceLot: true);

    [LedgerDatabaseFact]
    public Task SpineFaceAcquisition_ExplicitNoAmortization_RetainsCanonicalFaceFacts()
        => AssertSpineAcquisitionAsync(faceLot: true, method: BondAmortizationMethod.NoAmortization);

    [LedgerDatabaseFact]
    public Task SpineFaceAcquisition_MissingMethod_RefusesDraftWithoutJournalOrLot()
        => AssertSpineAcquisitionAsync(faceLot: true, method: null);

    private static async Task AssertSpineAcquisitionAsync(bool faceLot,
        BondAmortizationMethod? method = BondAmortizationMethod.StraightLine,
        bool legacyDraft = false, bool retainedApproval = false, bool receiptBacked = false)
    {
        var acquiredQuantity = receiptBacked ? 1_000m : Quantity;
        var acquiredUnitCost = receiptBacked ? 101.25m : UnitCost;
        var acquiredAmount = acquiredQuantity * acquiredUnitCost;
        var journalAmount = acquiredAmount + (receiptBacked ? 1_500m : 0m);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = server.ConnectionString,
            SchemaName = server.CreateSchemaName("acq_ledger"),
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };
        var securityOptions = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("acq_security"),
            PreloadProjectionCache = false
        };
        var assetOptions = new AssetOperationsOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("acq_asset")
        };
        await new LedgerMigrationRunner(ledgerOptions).EnsureMigratedAsync(ct);
        await SecurityMasterTestCapabilityFloor.InitializeAndActivateAsync(server, securityOptions, ct);
        await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync(ct);

        var securities = new PostgresSecurityMasterStore(securityOptions);
        PostgresAssetOperationsProjectionStore? assets = null;
        var journal = new PostgresLedgerJournalStore(ledgerOptions,
            backfillSecurityMaster: () => securities, backfillPositions: () => assets,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);
        var books = new PostgresLedgerBookService(journal);
        assets = new PostgresAssetOperationsProjectionStore(assetOptions, journal);
        var securityId = Guid.NewGuid();
        var securityQuery = await CreateSecurityAsync(securityOptions, securityId, faceLot, ct);
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await journal.SaveLedgerBookAsync(new LedgerBookRecord(
            bookId, Fund, ownerId, FundStructureNodeKindDto.Fund, "Acquisition GAAP book", "USD", now, now,
            AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: Policy, AccountingPolicyVersion: "v1"), ct);
        var period = await journal.SavePeriodAsync(new LedgerAccountingPeriod(
            periodId, bookId, PeriodStart.Year, PeriodStart.Month,
            PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            PeriodStart, PeriodStart.AddMonths(1).AddDays(-1),
            "Open", now, null, 0), 0, ct: ct);

        var policies = new AccountingPolicyService();
        await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Gaap, Policy, "v1", "Acquisition accounting", new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto("acquisition-pack", "v1",
                [new AccountingPolicyRuleDto(Rule, AccountingTreatmentKindDto.General, "v1",
                    SourceEventType: "AssetAccounting.Acquisition", RequiresEvidence: true,
                    RequiresApproval: true, AllowsAutoPosting: false)])), ct);
        var configurationStore = new PostgresAccountingConfigurationStore(ledgerOptions);
        var configuration = new AccountingConfigurationService(configurationStore, configurationStore, books);
        await ConfigureAcquisitionRuleAsync(configuration, bookId, ct, receiptBacked);
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)),
            books, policies, taxLotStore: journal);
        var settlementService = receiptBacked ? CreateGoldenBondSettlementService(securityId) : null;
        var spineService = new AssetAccountingEventSpineService(
            legacyDraft ? new LegacyAcquisitionDraftFixtureStore(assets) : assets,
            assets, securityQuery, books, policies, configuration, candidateBuilder, journal,
            bondSettlementConsiderationService: settlementService);

        var positionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var dimensions = new LedgerDimensionSetDto(FundId: Fund, EntityId: Company,
            InstrumentId: securityId, BookId: bookId.ToString("D"))
        { PositionId = positionId };
        var sourceHash = new string('b', 64);
        var observedAt = DateTimeOffset.UtcNow;
        var retainedEvidence = new RetainedEvidenceIdentityDto(
            "trade-confirmation", $"document://acquisitions/{eventId:D}/confirmation", sourceHash,
            "test-custodian", $"trade:{eventId:D}", RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            Preparer, observedAt, EffectiveDate, 1, observedAt, "retention-service",
            AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"));
        var economicEvent = new EconomicEventReferenceDto(eventId, "AssetAccounting.Acquisition", 1,
            EffectiveDate, observedAt, "test-custodian", $"trade:{eventId:D}", SourceContentHash: sourceHash)
        {
            SecurityId = securityId,
            BookPositionId = positionId,
            EvidenceLinks = [retainedEvidence.EvidenceUri],
            RetainedEvidence = [retainedEvidence]
        };
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "asset-acquisition", "v1", "test-fixture-v1",
            "base", EffectiveDate, DateTimeOffset.UtcNow, "test-custodian", $"trade:{eventId:D}", economicEvent)
        {
            BookPositionId = positionId,
            RetainedEvidence = [retainedEvidence]
        };
        var bookContext = new AccountingBookContextDto(bookId, Fund, ownerId, FundStructureNodeKindDto.Fund,
            "Acquisition GAAP book", "USD", AccountingBasisKindDto.Gaap, Policy, "v1", periodId, dimensions);
        var roleId = Guid.NewGuid();
        var role = new InstrumentRoleDto(roleId, securityId, Fund, "Fund", InstrumentRoleKinds.Holder,
            InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, new DateOnly(2026, 1, 1),
            Version: 1, OriginEvent: economicEvent, EvidenceLinks: [retainedEvidence.EvidenceUri]);
        var position = await assets.UpsertAsync(role, new BookPositionDto(positionId, securityId, roleId,
            bookContext, BookPositionSides.Long, "Active", new DateOnly(2026, 1, 1), Version: 1,
            OriginEvent: economicEvent, ProjectionLineage: lineage, EvidenceLinks: [retainedEvidence.EvidenceUri])
        { RetainedEvidence = [retainedEvidence] }, null, 0,
            new AssetOperationsWriteApprovalDto(Approver, "document://position-approval/acquisition",
                "Retain the explicitly identified holding for this test.", DateTimeOffset.UtcNow), ct);
        var projectedLines = new List<ProjectedAccountingEffectLineDto>
        {
            new(InvestmentAccount, acquiredAmount, 0m, "USD", Dimensions: dimensions),
            new("assets/cash", 0m, journalAmount, "USD", Dimensions: dimensions)
        };
        if (receiptBacked)
            projectedLines.Add(new("assets/purchased-interest", 1_500m, 0m, "USD", Dimensions: dimensions));
        var projectedEffect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey,
            lineage.ModelVersion, EffectiveDate, journalAmount, journalAmount, "USD", projectedLines);
        var scope = new AssetAccountingEventScopeDto(securityId, 1, positionId, position.Version, bookId, periodId,
            AccountingBasisKindDto.Gaap, Fund, Tenant, Company, dimensions);
        await spineService.ProjectAsync(new ProjectAssetAccountingEventRequestDto(
            AssetAccountingEventKindDto.Acquisition, scope, economicEvent, lineage, projectedEffect,
            acquiredAmount, "USD", period.Version, Preparer, DateTimeOffset.UtcNow, [retainedEvidence]), ct);

        var lotRecordId = Guid.NewGuid();
        var draftRequest = new AssetAccountingPostingCandidateRequestDto(
            AssetAccountingEventKindDto.Acquisition, scope, economicEvent, lineage, acquiredAmount, "USD",
            Preparer, DateTimeOffset.UtcNow, "100 shares at USD 25", 2, period.Version,
            RetainedEvidence: [retainedEvidence],
            LotMutation: new AssetLotMutationInstructionDto(
                AssetLotMutationIntentDto.Acquire,
                faceLot
                    // 2,500 face at 100 per 100 of par: quantity is the face per 100 of par.
                    ? new AssetAcquisitionLotDto(lotRecordId, "lot-acquired", EffectiveDate, acquiredQuantity, acquiredUnitCost,
                        InvestmentAccount, OriginalFace: acquiredQuantity * 100m, BookedFactor: 1m, ParBasis: 100m,
                        AmortizationMethod: method)
                    : new AssetAcquisitionLotDto(lotRecordId, "lot-acquired", EffectiveDate, Quantity, UnitCost,
                        InvestmentAccount)));
        if (receiptBacked)
        {
            var prepared = await settlementService!.PrepareAsync(new BondSettlementConsiderationRequest(
                new BondAnalyticsRequest(securityId, new BondCalculationContext(DateTimeOffset.UtcNow, EffectiveDate), CleanPrice: 101.25m),
                100_000m, 1m, 0m, 102_750m, retainedEvidence.EvidenceId, "assets/cash", "assets/purchased-interest"), ct);
            prepared.IsBlocked.Should().BeFalse();
            draftRequest = draftRequest with
            {
                Description = "100,000 face premium bond with purchased interest",
                LotMutation = draftRequest.LotMutation! with
                { Acquisition = draftRequest.LotMutation.Acquisition! with { SettlementConsideration = prepared.Settlement } }
            };
        }
        if (faceLot && method is null)
        {
            var draft = () => spineService.BuildPostingCandidateAsync(draftRequest, ct);
            await draft.Should().ThrowAsync<ArgumentException>().WithMessage("*explicit amortization method*");
            var unchanged = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
            unchanged.SpineVersion.Should().Be(2);
            unchanged.Stages[^1].Stage.Should().Be(AssetAccountingLifecycleStageDto.Projected);
            unchanged.DraftedCandidate.Should().BeNull();
            (await journal.GetByPeriodAsync(periodId, ct: ct)).Should().BeEmpty();
            (await journal.ListOpenTaxLotsByAssetScopeAsync(bookId, securityId, positionId, EffectiveDate, ct))
                .Should().BeEmpty();
            return;
        }
        await spineService.BuildPostingCandidateAsync(draftRequest, ct);
        var drafted = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        drafted.Stages[^1].Stage.Should().Be(AssetAccountingLifecycleStageDto.Drafted);
        var candidate = drafted.DraftedCandidate!;

        var approvalId = Guid.NewGuid().ToString("D");
        var approvedAt = DateTimeOffset.UtcNow;
        var approvalEvidence = new RetainedEvidenceIdentityDto(
            $"acquisition-approval-{approvalId}", $"document://acquisitions/{eventId:D}/approval",
            new string('e', 64), "test-approvals", $"approval:{approvalId}",
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus, Approver, approvedAt, EffectiveDate, 1,
            approvedAt, Approver, AssetAccountingEvidenceSubjects.PostingApproval,
            AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
                eventId, 1, Fund, bookId, periodId, AccountingBasisKindDto.Gaap, approvalId,
                drafted.DraftedCandidateFingerprint!, Tenant, Company));
        var posting = new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets,
            bondSettlementConsiderationService: settlementService);
        var postRequest = new PostPostingRuleJournalCandidateRequestDto(
            candidate, Approver, approvalId, ApprovalNotes: "Approve the confirmed acquisition.",
            TenantId: Tenant, CompanyId: Company)
        {
            ApprovalEvidence = [approvalEvidence]
        };
        if (legacyDraft)
        {
            if (retainedApproval)
                await assets.AppendAsync(drafted with
                {
                    SpineVersion = drafted.SpineVersion + 1,
                    Stages = [.. drafted.Stages, new(AssetAccountingLifecycleStageDto.Approved, approvedAt,
                        Approver, [approvalEvidence], approvalId, postRequest.ApprovalNotes)]
                }, drafted.SpineVersion, position.Version, ct);
            var incomplete = () => posting.PostCandidateAsync(postRequest, ct);
            await incomplete.Should().ThrowAsync<ArgumentException>().WithMessage("*explicit amortization method*");
            (await journal.GetByPeriodAsync(periodId, ct: ct)).Should().BeEmpty();
            (await journal.ListOpenTaxLotsByAssetScopeAsync(bookId, securityId, positionId, EffectiveDate, ct))
                .Should().BeEmpty();
            (await assets.GetLatestAsync(eventId, 1, ct))!.Projection.SpineVersion
                .Should().Be(retainedApproval ? 4 : 3);
            postRequest = CompleteLegacyTerms(postRequest, method!.Value);
            var invalid = postRequest with
            {
                AcquisitionTermsCompletion = postRequest.AcquisitionTermsCompletion! with
                { Evidence = postRequest.AcquisitionTermsCompletion!.Evidence with { SubjectId = new string('a', 64) } }
            };
            var unreviewed = () => posting.PostCandidateAsync(invalid, ct);
            await unreviewed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*independent retained review*");
            (await journal.GetByPeriodAsync(periodId, ct: ct)).Should().BeEmpty();
            (await assets.GetLatestAsync(eventId, 1, ct))!.Projection.SpineVersion
                .Should().Be(retainedApproval ? 4 : 3);
            drafted.Stages.Single(stage => stage.Stage == AssetAccountingLifecycleStageDto.Drafted).AttestedAtUtc
                .Should().Be(candidate.AccountingTimestamp);
            foreach (var (invalidRequest, error) in InvalidReviewedCompletions(postRequest))
            {
                var refused = () => posting.PostCandidateAsync(invalidRequest, ct);
                await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage(error);
                (await journal.GetByPeriodAsync(periodId, ct: ct)).Should().BeEmpty();
                (await journal.ListOpenTaxLotsByAssetScopeAsync(bookId, securityId, positionId, EffectiveDate, ct))
                    .Should().BeEmpty();
                (await assets.GetLatestAsync(eventId, 1, ct))!.Projection.SpineVersion
                    .Should().Be(retainedApproval ? 4 : 3);
            }
        }
        var originalApproval = retainedApproval ? (await assets.GetAsync(eventId, 1, 4, ct))!.Projection : null;
        PostedPostingRuleJournalCandidateResultDto posted;
        if (receiptBacked)
            (posted, postRequest) = await PostGoldenBondThroughReviewAsync(ledgerOptions, configuration, configurationStore,
                journal, spineService, posting, new AssetAccountingPostingCandidateDto(drafted, drafted.DraftedCandidateResult!), ct);
        else
            posted = await posting.PostCandidateAsync(postRequest, ct);
        posted.WasReplay.Should().BeFalse();
        posted.TaxLotMutationBatchId.Should().NotBeNull();

        var lot = (await journal.ListOpenTaxLotsByAssetScopeAsync(bookId, securityId, positionId, EffectiveDate, ct))
            .Should().ContainSingle().Subject;
        lot.TaxLotRecordId.Should().Be(lotRecordId);
        lot.Acquisition.Should().NotBeNull(
            "a spine acquisition must retain the canonical facts production disposal projects");
        var facts = lot.Acquisition!;
        facts.QuantityBasis.Should().Be(faceLot ? LotQuantityBasis.Face : LotQuantityBasis.Units);
        if (faceLot)
        {
            facts.FaceValueTerms.Should().BeEquivalentTo(
                new FaceValueAcquisitionTermsDto(100m, 1m, method!.Value, null));
        }
        else
        {
            facts.FaceValueTerms.Should().BeNull();
        }
        facts.AcquisitionCurrency.Should().Be("USD");
        facts.FunctionalCurrency.Should().Be("USD");
        facts.AcquisitionFxRateToFunctional.Should().Be(1m);
        facts.TransactionCostBasis.Should().Be(acquiredAmount);
        facts.FunctionalCostBasis.Should().Be(acquiredAmount);
        facts.HoldingPeriodStartDate.Should().Be(EffectiveDate);
        facts.Evidence.Should().HaveCount(legacyDraft ? 2 : 1);
        var lotEvidence = facts.Evidence.Single(item => item.EvidenceUri == retainedEvidence.EvidenceUri);
        lotEvidence.SubjectType.Should().Be("OpenLotAcquisition");
        lotEvidence.SubjectId.Should().Be(lotRecordId.ToString("D"));
        lotEvidence.EvidenceUri.Should().Be(retainedEvidence.EvidenceUri);
        lotEvidence.ContentHashSha256.Should().Be(sourceHash);
        lotEvidence.ReviewedBy.Should().Be(Approver, "the independent approver reviewed the lot-bound facts");

        lot.ToOpenLot().OpenFunctionalCostBasis.Should().Be(acquiredAmount);
        var relief = CanonicalOpenLotDisposalGuard.Validate(
            [lot],
            [new LedgerTaxLotDisposalSelection(lot.TaxLotRecordId, lot.LotId, lot.Version, lot.OpenQuantity,
                40m, 0, retainedEvidence.EvidenceId, acquiredUnitCost, 40m * acquiredUnitCost)],
            LedgerTaxLotReliefMethod.Fifo,
            "USD");
        relief.Quantity.Should().Be(faceLot ? 40m * 100m : 40m, "face relief is stated in face, units relief in units");
        relief.FunctionalCostBasis.Should().Be(40m * acquiredUnitCost);

        // A retry replays the retained batch exactly rather than colliding on the fact-bearing fingerprint.
        var replaySecurities = new PostgresSecurityMasterStore(securityOptions);
        PostgresAssetOperationsProjectionStore? replayAssets = null;
        var replayJournal = new PostgresLedgerJournalStore(ledgerOptions,
            backfillSecurityMaster: () => replaySecurities, backfillPositions: () => replayAssets,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);
        replayAssets = new PostgresAssetOperationsProjectionStore(assetOptions, replayJournal);
        var replay = await new AccountingPostingCandidatePostService(
                candidateBuilder, replayJournal, assetAccountingEventStore: replayAssets,
                bondSettlementConsiderationService: settlementService)
            .PostCandidateAsync(postRequest, ct);
        replay.WasReplay.Should().BeTrue();
        replay.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        if (receiptBacked)
        {
            var retainedJournal = (await replayJournal.GetByPeriodAsync(periodId, ct: ct)).Should().ContainSingle().Subject.Entry;
            retainedJournal.Lines.Sum(line => line.Debit).Should().Be(102_750m);
            retainedJournal.Lines.Sum(line => line.Credit).Should().Be(102_750m);
            retainedJournal.Lines.Should().ContainSingle(line => line.Account == new LedgerAccount("Investments", LedgerAccountType.Asset)
                && line.Debit == 101_250m && line.Credit == 0m);
            retainedJournal.Lines.Should().ContainSingle(line => line.Account == new LedgerAccount("Purchased accrued interest", LedgerAccountType.Asset)
                && line.Debit == 1_500m && line.Credit == 0m);
            retainedJournal.Lines.Should().ContainSingle(line => line.Account == new LedgerAccount("Cash", LedgerAccountType.Asset)
                && line.Credit == 102_750m && line.Debit == 0m);
            (await replayJournal.ListOpenTaxLotsByAssetScopeAsync(bookId, securityId, positionId, EffectiveDate, ct))
                .Should().ContainSingle().Which.Acquisition!.FunctionalCostBasis.Should().Be(101_250m);
        }
        if (legacyDraft)
        {
            var retainedDraft = (await assets.GetAsync(eventId, 1, 3, ct))!.Projection;
            JsonElement.DeepEquals(JsonSerializer.SerializeToElement(retainedDraft),
                JsonSerializer.SerializeToElement(drafted)).Should().BeTrue("completion must not rewrite the original draft");
            retainedDraft.DraftedLotMutation!.Acquisition!.AmortizationMethod.Should().BeNull();
            if (originalApproval is not null)
                JsonElement.DeepEquals(JsonSerializer.SerializeToElement((await assets.GetAsync(eventId, 1, 4, ct))!.Projection),
                    JsonSerializer.SerializeToElement(originalApproval)).Should().BeTrue("completion must not rewrite the original approval");
            var latest = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
            latest.EventId.Should().Be(eventId);
            latest.EventVersion.Should().Be(1);
            latest.Stages.Single(stage => stage.Stage == AssetAccountingLifecycleStageDto.Posted).Evidence
                .Should().Contain(postRequest.AcquisitionTermsCompletion!.Evidence);
            var batch = (await journal.GetAtomicTaxLotPostingAsync(posted.TaxLotMutationBatchId!.Value, ct))!;
            batch.RetainedEvidence.Should().Contain(postRequest.AcquisitionTermsCompletion.Evidence);
            batch.Journal.Entry.Metadata.EvidenceReferences.Should()
                .Contain(item => item.SubjectType == AssetAcquisitionTermsCompletionDto.EvidenceSubjectType
                    && item.SubjectId == postRequest.AcquisitionTermsCompletion.ApprovalSubjectId());
            var changed = CompleteLegacyTerms(postRequest, BondAmortizationMethod.StraightLine);
            var collision = () => posting.PostCandidateAsync(changed, ct);
            await collision.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
            (await journal.GetByPeriodAsync(periodId, ct: ct)).Should().ContainSingle();
        }
    }

    private static async Task<SecurityMasterQueryService> CreateSecurityAsync(
        SecurityMasterOptions options, Guid securityId, bool faceLot, CancellationToken ct)
    {
        var events = new PostgresSecurityMasterEventStore(options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshots = new PostgresSecurityMasterSnapshotStore(options);
        var store = new PostgresSecurityMasterStore(options);
        var rebuilder = new SecurityMasterAggregateRebuilder(events, snapshots);
        var service = new SecurityMasterService(events, snapshots, store, rebuilder, options,
            NullLogger<SecurityMasterService>.Instance);
        // Only a face-value class may be booked with a Face quantity basis (LotQuantityBasisRules),
        // so the face-lot cases acquire a bond and the unit-lot cases an equity.
        if (faceLot)
        {
            await service.CreateAsync(new CreateSecurityRequest(securityId, "Bond",
                JsonSerializer.SerializeToElement(new
                {
                    displayName = "Acquisition test bond",
                    currency = "USD",
                    countryOfRisk = "US",
                    issuerName = "Fixture issuer"
                }),
                JsonSerializer.SerializeToElement(new { maturity = "2036-01-15", couponRate = 0.05m }),
                [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "ACQB" + securityId.ToString("N"), true,
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))],
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "test", Preparer, null, "Test bond"), ct);
            return new SecurityMasterQueryService(events, store, rebuilder);
        }

        await service.CreateAsync(new CreateSecurityRequest(securityId, "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Acquisition test equity",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Fixture issuer",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACQ" + securityId.ToString("N"), true,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "test", Preparer, null, "Test equity"), ct);
        return new SecurityMasterQueryService(events, store, rebuilder);
    }

    private static async Task ConfigureAcquisitionRuleAsync(
        AccountingConfigurationService configuration, Guid bookId, CancellationToken ct, bool receiptBacked = false)
    {
        foreach (var node in new[] {
            new ChartOfAccountsNodeDto("investments", InvestmentAccount, "Investments", "Asset"),
            new ChartOfAccountsNodeDto("cash", "assets/cash", "Cash", "Asset") })
        {
            await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
                Fund, node, Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
        }
        if (receiptBacked)
            await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(Fund,
                new ChartOfAccountsNodeDto("purchased-interest", "assets/purchased-interest", "Purchased accrued interest", "Asset"),
                Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
        var formulas = new List<AccountingRuleFormulaDto> { new("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m) };
        var postings = new List<GeneratedPostingLineDto>
        {
            new("investments", InvestmentAccount, AccountingTemplateLineSideDto.Debit, "amount", 0m, "USD"),
            new("cash", "assets/cash", AccountingTemplateLineSideDto.Credit, receiptBacked ? "dirty" : "amount", 0m, "USD")
        };
        if (receiptBacked)
        {
            formulas.Add(new("purchased", AccountingRuleFormulaKindDto.FixedAmount, 1_500m));
            formulas.Add(new("dirty", AccountingRuleFormulaKindDto.FixedAmount, 102_750m));
            postings.Add(new("purchased-interest", "assets/purchased-interest", AccountingTemplateLineSideDto.Debit, "purchased", 0m, "USD"));
        }
        await configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(Fund,
            new PostingRuleDto(Rule, "Asset acquisition", "AssetAccounting.Acquisition", "generated", "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1), Priority: 100,
                Formulas: formulas, GeneratedPostings: postings),
            Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
    }
}
