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
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// A lot acquired through the Asset Accounting Event Spine must leave the lot of record with
/// canonical acquisition facts, or production disposal - which projects every open lot through the
/// canonical contract - refuses it and routes it to the legacy backfill queue.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AssetAcquisitionLotPostgresRoundTripTests
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
        => AssertSpineAcquisitionRetainsCanonicalFactsAsync(faceLot: false);

    [LedgerDatabaseFact]
    public Task SpineFaceAcquisition_WithStatedAmortization_RetainsCanonicalFaceFacts()
        => AssertSpineAcquisitionRetainsCanonicalFactsAsync(faceLot: true);

    private static async Task AssertSpineAcquisitionRetainsCanonicalFactsAsync(bool faceLot)
    {
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
        await new SecurityMasterMigrationRunner(securityOptions).EnsureMigratedAsync(ct);
        await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync(ct);

        var journal = new PostgresLedgerJournalStore(ledgerOptions);
        var books = new PostgresLedgerBookService(journal);
        var assets = new PostgresAssetOperationsProjectionStore(assetOptions, journal);
        var securityId = Guid.NewGuid();
        var securityQuery = await CreateSecurityAsync(securityOptions, securityId, ct);
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
        await ConfigureAcquisitionRuleAsync(configuration, bookId, ct);
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)),
            books, policies, taxLotStore: journal);
        var spineService = new AssetAccountingEventSpineService(
            assets, assets, securityQuery, books, policies, configuration, candidateBuilder, journal);

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
        var projectedEffect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey,
            lineage.ModelVersion, EffectiveDate, Amount, Amount, "USD",
            [new ProjectedAccountingEffectLineDto(InvestmentAccount, Amount, 0m, "USD", Dimensions: dimensions),
             new ProjectedAccountingEffectLineDto("assets/cash", 0m, Amount, "USD", Dimensions: dimensions)]);
        var scope = new AssetAccountingEventScopeDto(securityId, 1, positionId, position.Version, bookId, periodId,
            AccountingBasisKindDto.Gaap, Fund, Tenant, Company, dimensions);
        await spineService.ProjectAsync(new ProjectAssetAccountingEventRequestDto(
            AssetAccountingEventKindDto.Acquisition, scope, economicEvent, lineage, projectedEffect,
            Amount, "USD", period.Version, Preparer, DateTimeOffset.UtcNow, [retainedEvidence]), ct);

        var lotRecordId = Guid.NewGuid();
        await spineService.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(
            AssetAccountingEventKindDto.Acquisition, scope, economicEvent, lineage, Amount, "USD",
            Preparer, DateTimeOffset.UtcNow, "100 shares at USD 25", 2, period.Version,
            RetainedEvidence: [retainedEvidence],
            LotMutation: new AssetLotMutationInstructionDto(
                AssetLotMutationIntentDto.Acquire,
                faceLot
                    // 2,500 face at 100 per 100 of par: quantity is the face per 100 of par.
                    ? new AssetAcquisitionLotDto(lotRecordId, "lot-acquired", EffectiveDate, Quantity, UnitCost,
                        InvestmentAccount, OriginalFace: Quantity * 100m, BookedFactor: 1m, ParBasis: 100m,
                        AmortizationMethod: BondAmortizationMethod.StraightLine)
                    : new AssetAcquisitionLotDto(lotRecordId, "lot-acquired", EffectiveDate, Quantity, UnitCost,
                        InvestmentAccount))), ct);
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
        var posting = new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets);
        var postRequest = new PostPostingRuleJournalCandidateRequestDto(
            candidate, Approver, approvalId, ApprovalNotes: "Approve the confirmed acquisition.",
            TenantId: Tenant, CompanyId: Company)
        {
            ApprovalEvidence = [approvalEvidence]
        };
        var posted = await posting.PostCandidateAsync(postRequest, ct);
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
                new FaceValueAcquisitionTermsDto(100m, 1m, BondAmortizationMethod.StraightLine, null));
        }
        else
        {
            facts.FaceValueTerms.Should().BeNull();
        }
        facts.AcquisitionCurrency.Should().Be("USD");
        facts.FunctionalCurrency.Should().Be("USD");
        facts.AcquisitionFxRateToFunctional.Should().Be(1m);
        facts.TransactionCostBasis.Should().Be(Amount);
        facts.FunctionalCostBasis.Should().Be(Amount);
        facts.HoldingPeriodStartDate.Should().Be(EffectiveDate);
        var lotEvidence = facts.Evidence.Should().ContainSingle().Subject;
        lotEvidence.SubjectType.Should().Be("OpenLotAcquisition");
        lotEvidence.SubjectId.Should().Be(lotRecordId.ToString("D"));
        lotEvidence.EvidenceUri.Should().Be(retainedEvidence.EvidenceUri);
        lotEvidence.ContentHashSha256.Should().Be(sourceHash);
        lotEvidence.ReviewedBy.Should().Be(Approver, "the independent approver reviewed the lot-bound facts");

        lot.ToOpenLot().OpenFunctionalCostBasis.Should().Be(Amount);
        var relief = CanonicalOpenLotDisposalGuard.Validate(
            [lot],
            [new LedgerTaxLotDisposalSelection(lot.TaxLotRecordId, lot.LotId, lot.Version, lot.OpenQuantity,
                40m, 0, retainedEvidence.EvidenceId, UnitCost, 40m * UnitCost)],
            LedgerTaxLotReliefMethod.Fifo,
            "USD");
        relief.Quantity.Should().Be(faceLot ? 40m * 100m : 40m, "face relief is stated in face, units relief in units");
        relief.FunctionalCostBasis.Should().Be(40m * UnitCost);

        // A retry replays the retained batch exactly rather than colliding on the fact-bearing fingerprint.
        var replay = await new AccountingPostingCandidatePostService(
                candidateBuilder, new PostgresLedgerJournalStore(ledgerOptions),
                assetAccountingEventStore: new PostgresAssetOperationsProjectionStore(
                    assetOptions, new PostgresLedgerJournalStore(ledgerOptions)))
            .PostCandidateAsync(postRequest, ct);
        replay.WasReplay.Should().BeTrue();
        replay.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
    }

    private static async Task<SecurityMasterQueryService> CreateSecurityAsync(
        SecurityMasterOptions options, Guid securityId, CancellationToken ct)
    {
        var events = new PostgresSecurityMasterEventStore(options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshots = new PostgresSecurityMasterSnapshotStore(options);
        var store = new PostgresSecurityMasterStore(options);
        var rebuilder = new SecurityMasterAggregateRebuilder(events, snapshots);
        var service = new SecurityMasterService(events, snapshots, store, rebuilder, options,
            NullLogger<SecurityMasterService>.Instance);
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
        AccountingConfigurationService configuration, Guid bookId, CancellationToken ct)
    {
        foreach (var node in new[] {
            new ChartOfAccountsNodeDto("investments", InvestmentAccount, "Investments", "Asset"),
            new ChartOfAccountsNodeDto("cash", "assets/cash", "Cash", "Asset") })
        {
            await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
                Fund, node, Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
        }
        await configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(Fund,
            new PostingRuleDto(Rule, "Asset acquisition", "AssetAccounting.Acquisition", "generated", "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1), Priority: 100,
                Formulas: [new AccountingRuleFormulaDto("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)],
                GeneratedPostings: [
                    new GeneratedPostingLineDto("investments", InvestmentAccount, AccountingTemplateLineSideDto.Debit, "amount", 0m, "USD"),
                    new GeneratedPostingLineDto("cash", "assets/cash", AccountingTemplateLineSideDto.Credit, "amount", 0m, "USD")]),
            Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
    }
}
