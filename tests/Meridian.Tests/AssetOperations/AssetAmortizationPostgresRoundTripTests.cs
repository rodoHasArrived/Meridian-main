using System.Globalization;
using System.Text.Json;
using FluentAssertions;
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
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>Exercises preparation, Rules Studio, independent approval and Posted authority together.</summary>
[Trait("Category", "Integration")]
public sealed class AssetAmortizationPostgresRoundTripTests
{
    [LedgerDatabaseFact]
    public Task CanonicalAmortization_Premium_ApprovedSpineRetainsHistoricalEvidenceAndReplaysAfterRestart()
        => AssertGovernedAmortizationAsync(premium: true);

    [LedgerDatabaseFact]
    public Task CanonicalAmortization_Discount_ApprovedSpineRetainsHistoricalEvidenceAndReplaysAfterRestart()
        => AssertGovernedAmortizationAsync(premium: false);

    private static async Task AssertGovernedAmortizationAsync(bool premium)
    {
        const string fund = "amortization-pipeline-fund";
        const string tenant = "amortization-pipeline-tenant";
        const string company = "amortization-pipeline-company";
        const string preparer = "fund-accountant";
        const string approver = "independent-controller";
        const string policy = "amortization-gaap";
        const string rule = "canonical-amortization";
        const string assetPath = "assets/investments";
        const string incomePath = "income/amortization";
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var acquired = date.AddYears(-1);
        var maturity = date.AddYears(1);
        var periodStart = new DateOnly(date.Year, date.Month, 1);
        var now = DateTimeOffset.UtcNow;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = server.ConnectionString,
            SchemaName = server.CreateSchemaName("amort_pipeline_ledger"),
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };
        var securityOptions = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("amort_pipeline_security"),
            PreloadProjectionCache = false
        };
        var assetOptions = new AssetOperationsOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("amort_pipeline_asset")
        };
        await new LedgerMigrationRunner(ledgerOptions).EnsureMigratedAsync(ct);
        await new SecurityMasterMigrationRunner(securityOptions).EnsureMigratedAsync(ct);
        await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync(ct);
        var securities = new PostgresSecurityMasterStore(securityOptions);
        PostgresAssetOperationsProjectionStore? assets = null;
        var journal = new PostgresLedgerJournalStore(ledgerOptions, backfillSecurityMaster: () => securities,
            backfillPositions: () => assets!);
        assets = new PostgresAssetOperationsProjectionStore(assetOptions, journal);
        var books = new PostgresLedgerBookService(journal);
        var bookId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        await journal.SaveLedgerBookAsync(new LedgerBookRecord(bookId, fund, ownerId, FundStructureNodeKindDto.Fund,
            "Canonical amortization GAAP book", "USD", now, now, AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: policy, AccountingPolicyVersion: "v1"), ct);
        var period = await journal.SavePeriodAsync(new LedgerAccountingPeriod(Guid.NewGuid(), bookId,
            periodStart.Year, periodStart.Month, periodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            periodStart, periodStart.AddMonths(1).AddDays(-1), "Open", now, null, 0), 0, ct: ct);
        var empty = JsonSerializer.SerializeToElement(new { });
        await securities.UpsertProjectionAsync(new SecurityProjectionRecord(securityId, "Bond", SecurityStatusDto.Active,
            "Governed fixed-rate bullet", "USD", "ISIN", "TESTAMORT" + securityId.ToString("N"),
            JsonSerializer.SerializeToElement(new
            {
                maturityDate = maturity.ToString("yyyy-MM-dd"),
                dayCountConvention = "30/360",
                couponRate = 10m,
                paymentFrequency = "annual",
                couponType = "Fixed",
                isCallable = false
            }), empty, empty, 1,
            new DateTimeOffset(acquired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), null, [], []), ct);
        var security = (await securities.GetProjectionAsync(securityId, ct))!;
        RetainedEvidenceIdentityDto Evidence(string id, DateOnly effective, string subjectType, string subjectId, string hash)
            => new(id, $"document://amortization/{id}", hash, "custodian", id,
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus, preparer, now, effective, 1, now,
                "retention-service", subjectType, subjectId);
        var acquisitionEvidence = Evidence("original-acquisition", acquired, "OpenLotAcquisition", lotId.ToString("D"), new string('a', 64));
        var securityEvidence = Evidence("security-projection", acquired, "SecurityMasterProjection", securityId.ToString("D"),
            OpenLotAmortization.SecurityHash(security));
        var eventEvidence = Evidence("amortization-period", date, AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"), new string('b', 64));
        var evidence = new[] { eventEvidence, acquisitionEvidence, securityEvidence };
        var price = premium ? 110m : 90m;
        var account = new LedgerAccount("Investments", LedgerAccountType.Asset);
        var lot = await journal.SaveTaxLotAsync(new LedgerTaxLotRecord(lotId, bookId, account, "amortization-owned-lot", acquired,
            100m, 100m, price, "USD", now, now, EvidenceRef: acquisitionEvidence.EvidenceId, Version: 1,
            SecurityId: securityId, BookPositionId: positionId, OriginalFace: 10_000m, BookedFactor: 1m, ParBasis: 100m,
            Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, price * 100m, price * 100m,
                acquired, new FaceValueAcquisitionTermsDto(100m, 1m, BondAmortizationMethod.StraightLine, null), [acquisitionEvidence])), ct);
        var dimensions = new LedgerDimensionSetDto(FundId: fund, EntityId: company,
            InstrumentId: securityId, BookId: bookId.ToString("D"))
        { PositionId = positionId };
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization);
        var economicEvent = new EconomicEventReferenceDto(eventId, eventType, 1, date, now, "custodian", eventEvidence.SourceReference,
            SourceContentHash: eventEvidence.ContentHashSha256)
        {
            SecurityId = securityId,
            BookPositionId = positionId,
            RetainedEvidence = evidence,
            EvidenceLinks = evidence.Select(item => item.EvidenceUri).ToArray()
        };
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "canonical-lot-amortization", OpenLotAmortization.ModelVersion,
            "test-fixture-v1", "base", date, DateTimeOffset.UtcNow, "custodian", eventEvidence.SourceReference, economicEvent)
        { BookPositionId = positionId, RetainedEvidence = evidence };
        var bookContext = new AccountingBookContextDto(bookId, fund, ownerId, FundStructureNodeKindDto.Fund,
            "Canonical amortization GAAP book", "USD", AccountingBasisKindDto.Gaap, policy, "v1", period.PeriodId, dimensions);
        var roleId = Guid.NewGuid();
        var position = await assets.UpsertAsync(new InstrumentRoleDto(roleId, securityId, fund, "Fund", InstrumentRoleKinds.Holder,
                InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, acquired, OriginEvent: economicEvent,
                EvidenceLinks: economicEvent.EvidenceLinks),
            new BookPositionDto(positionId, securityId, roleId, bookContext, BookPositionSides.Long, "Active", acquired,
                OriginEvent: economicEvent, ProjectionLineage: lineage, EvidenceLinks: economicEvent.EvidenceLinks)
            { RetainedEvidence = evidence }, null, 0,
            new AssetOperationsWriteApprovalDto(approver, "document://position-approval/amortization",
                "Retain the owned lot amortization event.", DateTimeOffset.UtcNow), ct);
        var preview = await new CanonicalLotAmortizationService(journal, securities, assets)
            .PreviewAsync(bookId, lotId, date, securityEvidence, ct);
        var movement = preview.Projection.FunctionalMovement;
        Math.Abs(movement).Should().Be(500m);
        var amount = Math.Abs(movement);
        var policies = new AccountingPolicyService();
        await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Gaap, policy, "v1",
            "Canonical amortization accounting", acquired,
            RulePack: new AccountingPolicyRulePackDto("canonical-amortization-pack", "v1",
                [new AccountingPolicyRuleDto(rule, AccountingTreatmentKindDto.General, "v1", SourceEventType: eventType,
                    RequiresEvidence: true, RequiresApproval: true, AllowsAutoPosting: false)])), ct);
        var configurationStore = new PostgresAccountingConfigurationStore(ledgerOptions);
        var configuration = new AccountingConfigurationService(configurationStore, configurationStore, books);
        foreach (var node in new[] { new ChartOfAccountsNodeDto("investments", assetPath, "Investments", "Asset"),
                     new ChartOfAccountsNodeDto("amortization-income", incomePath, "Amortization income", "Revenue") })
            await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(fund, node, approver,
                CompanyId: company, LedgerBookId: bookId, TenantId: tenant), ct);
        await configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(fund,
            new PostingRuleDto(rule, "Canonical lot amortization", eventType, "generated", "v1", EffectiveFrom: acquired, Priority: 100,
                Formulas: [new AccountingRuleFormulaDto("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)],
                GeneratedPostings:
                [new GeneratedPostingLineDto("investments", assetPath, premium ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit,
                    "amount", 0m, "USD"),
                 new GeneratedPostingLineDto("amortization-income", incomePath, premium ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
                    "amount", 0m, "USD")]), approver, CompanyId: company, LedgerBookId: bookId, TenantId: tenant), ct);
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)), books, policies, taxLotStore: journal);
        // Event-recorded reference lookup is a read-only authority boundary. The exact projection
        // also lives in PostgreSQL and is rechecked under the actual posting transaction's locks.
        var referenceQuery = Substitute.For<ISecurityMasterQueryService>();
        referenceQuery.GetRecordedByIdAsOfAsync(securityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(securityId, security.AssetClass, security.Status, security.DisplayName,
                security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
                security.Version, security.EffectiveFrom, security.EffectiveTo));
        var spine = new AssetAccountingEventSpineService(assets, assets, referenceQuery, books, policies, configuration, candidateBuilder, journal);
        var scope = new AssetAccountingEventScopeDto(securityId, security.Version, positionId, position.Version, bookId,
            period.PeriodId, AccountingBasisKindDto.Gaap, fund, tenant, company, dimensions);
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion, date,
            amount, amount, "USD",
            [new ProjectedAccountingEffectLineDto(assetPath, Math.Max(movement, 0m), Math.Max(-movement, 0m), "USD", Dimensions: dimensions),
             new ProjectedAccountingEffectLineDto(incomePath, Math.Max(-movement, 0m), Math.Max(movement, 0m), "USD", Dimensions: dimensions)]);
        await spine.ProjectAsync(new ProjectAssetAccountingEventRequestDto(AssetAccountingEventKindDto.DepreciationAmortization,
            scope, economicEvent, lineage, effect, amount, "USD", period.Version, preparer, DateTimeOffset.UtcNow, evidence), ct);
        await spine.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(AssetAccountingEventKindDto.DepreciationAmortization,
            scope, economicEvent, lineage, amount, "USD", preparer, DateTimeOffset.UtcNow, "Approved amortization period", 2, period.Version,
            RetainedEvidence: evidence, LotMutation: new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Amortize,
                AssetAccountId: assetPath, Amortization: preview.Instruction)), ct);
        var drafted = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        drafted.DraftedLotMutation!.Intent.Should().Be(AssetLotMutationIntentDto.Amortize);
        drafted.DraftedCandidateResult!.PostingCommand!.LotAmortization.Should().NotBeNull();
        var approvalId = Guid.NewGuid().ToString("D");
        var approvedAt = DateTimeOffset.UtcNow;
        var approvalEvidence = new RetainedEvidenceIdentityDto("period-approval", "document://amortization/period-approval", new string('e', 64),
            "approvals", approvalId, RetainedEvidenceIdentityValidator.AcceptedReviewStatus, approver, approvedAt, date, 1,
            approvedAt, approver, AssetAccountingEvidenceSubjects.PostingApproval,
            AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(eventId, 1, fund, bookId, period.PeriodId,
                AccountingBasisKindDto.Gaap, approvalId, drafted.DraftedCandidateFingerprint!, tenant, company));
        var request = new PostPostingRuleJournalCandidateRequestDto(drafted.DraftedCandidate!, approver, approvalId,
            ApprovalNotes: "Approve the retained carrying-value movement.", TenantId: tenant, CompanyId: company)
        { ApprovalEvidence = [approvalEvidence] };
        var posted = await new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets)
            .PostCandidateAsync(request, ct);
        posted.WasReplay.Should().BeFalse();
        posted.TaxLotMutationBatchId.Should().NotBeNull();
        var retained = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        retained.Stages.Select(stage => stage.Stage).Should().Equal(AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected, AssetAccountingLifecycleStageDto.Drafted,
            AssetAccountingLifecycleStageDto.Approved, AssetAccountingLifecycleStageDto.Posted);
        retained.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        retained.RetainedEvidence.Should().Contain(acquisitionEvidence);
        retained.RetainedEvidence.Should().Contain(securityEvidence);
        var reloaded = (await journal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single().ToOpenLot();
        reloaded.OpenFunctionalCostBasis.Should().Be(premium ? 10_500m : 9_500m);
        reloaded.Acquisition.Should().BeEquivalentTo(lot.Acquisition);
        var batch = (await journal.GetAtomicTaxLotPostingAsync(posted.TaxLotMutationBatchId!.Value, ct))!;
        batch.MutationKind.Should().Be(AtomicTaxLotMutationKind.Amortization);
        batch.Journal.Entry.Lines.Where(line => line.Account == account).Sum(line => line.Debit - line.Credit)
            .Should().Be(reloaded.OpenFunctionalCostBasis - lot.ToOpenLot().OpenFunctionalCostBasis);
        var restartedSecurities = new PostgresSecurityMasterStore(securityOptions);
        PostgresAssetOperationsProjectionStore? restartedAssets = null;
        var restartedJournal = new PostgresLedgerJournalStore(ledgerOptions, backfillSecurityMaster: () => restartedSecurities,
            backfillPositions: () => restartedAssets!);
        restartedAssets = new PostgresAssetOperationsProjectionStore(assetOptions, restartedJournal);
        var replay = await new AccountingPostingCandidatePostService(candidateBuilder, restartedJournal,
            assetAccountingEventStore: restartedAssets).PostCandidateAsync(request, ct);
        replay.WasReplay.Should().BeTrue();
        replay.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        (await restartedAssets.GetLatestAsync(eventId, 1, ct))!.Projection.SpineVersion.Should().Be(5);
        (await restartedJournal.GetByPeriodAsync(period.PeriodId, ct)).Should().ContainSingle();
        (await restartedJournal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single().Version.Should().Be(lot.Version + 1);
    }
}
