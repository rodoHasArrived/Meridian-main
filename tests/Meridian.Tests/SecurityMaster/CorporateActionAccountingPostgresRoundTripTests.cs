using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.AssetOperations;
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

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Exercises the registered in-process accounting lane against PostgreSQL. The explicit source,
/// policy and lot fixtures are test evidence, not certification of source fan-out or legacy binding.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CorporateActionAccountingPostgresRoundTripTests
{
    private const string Tenant = "corpact-test-tenant";
    private const string Company = "corpact-test-company";
    private const string Fund = "corpact-test-fund";
    private const string Preparer = "fund-accountant";
    private const string Approver = "independent-controller";
    private const string Policy = "corpact-gaap";
    private const string Rule = "cash-dividend";
    private const decimal Amount = 120m;
    private static readonly DateOnly EffectiveDate = new(2026, 8, 14);
    private static readonly CorporateActionCaseTransitionAuthorityDto PreparationAuthority =
        new(true, false, true, false, false);

    [LedgerDatabaseFact]
    public async Task CashDividend_AttachApprovePost_ReloadsOneBalancedJournalAndReplaysReceipt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = server.ConnectionString, SchemaName = server.CreateSchemaName("ca_ledger"),
            RequireGovernedPostingCommand = true, RequireExpectedVersion = true
        };
        var securityOptions = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("ca_security"),
            PreloadProjectionCache = false
        };
        var assetOptions = new AssetOperationsOptions
        {
            ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("ca_asset")
        };
        await new LedgerMigrationRunner(ledgerOptions).EnsureMigratedAsync(ct);
        await new SecurityMasterMigrationRunner(securityOptions).EnsureMigratedAsync(ct);
        await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync(ct);

        var journal = new PostgresLedgerJournalStore(ledgerOptions);
        var books = new PostgresLedgerBookService(journal);
        var assets = new PostgresAssetOperationsProjectionStore(assetOptions, journal);
        var operations = new PostgresCorporateActionOperationsStore(securityOptions);
        var preparation = new CorporateActionOperationsService(operations);
        var securityId = Guid.NewGuid();
        var securityQuery = await CreateSecurityAsync(securityOptions, securityId, ct);
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await journal.SaveLedgerBookAsync(new LedgerBookRecord(
            bookId, Fund, ownerId, FundStructureNodeKindDto.Fund, "Dividend GAAP book", "USD", now, now,
            AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: Policy, AccountingPolicyVersion: "v1"), ct);
        var period = await journal.SavePeriodAsync(new LedgerAccountingPeriod(
            periodId, bookId, 2026, 8, "August 2026", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            "Open", now, null, 0), 0, ct: ct);
        var caseScope = new CorporateActionCaseScopeDto(
            Tenant, Company, FundProfileId: Fund, LedgerBookId: bookId.ToString("D"),
            PeriodId: periodId.ToString("D"), AccountingBasis: "Gaap", FunctionalCurrency: "USD");
        var processingCase = await CreateAccountingReviewCaseAsync(operations, preparation, securityId, caseScope, ct);

        var policies = new AccountingPolicyService();
        await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Gaap, Policy, "v1", "Dividend accounting", new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto("corpact-pack", "v1",
                [new AccountingPolicyRuleDto(Rule, AccountingTreatmentKindDto.General, "v1",
                    SourceEventType: "AssetAccounting.CorporateAction", RequiresEvidence: true,
                    RequiresApproval: true, AllowsAutoPosting: false)])), ct);
        var configurationStore = new PostgresAccountingConfigurationStore(ledgerOptions);
        var configuration = new AccountingConfigurationService(configurationStore, configurationStore, books);
        await ConfigureDividendRuleAsync(configuration, bookId, ct);
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)),
            books, policies, taxLotStore: journal);
        var spineService = new AssetAccountingEventSpineService(
            assets, assets, securityQuery, books, policies, configuration, candidateBuilder, journal);
        var positionId = Guid.NewGuid();
        var eventId = processingCase.CorporateActionId;
        var dimensions = new LedgerDimensionSetDto(FundId: Fund, EntityId: Company,
            InstrumentId: securityId, BookId: bookId.ToString("D")) { PositionId = positionId };
        var sourceHash = CorporateActionEconomicFingerprint.Compute(
            (await operations.GetSourceProposalAsync(processingCase.ProposalId, ct))!.ProposedAction);
        var observedAt = DateTimeOffset.UtcNow;
        var retainedEvidence = new RetainedEvidenceIdentityDto(
            "dividend-notice", $"document://corporate-actions/{eventId:D}/notice", sourceHash,
            "test-custodian", $"dividend:{eventId:D}", RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            Preparer, observedAt, EffectiveDate, 1, observedAt, Preparer,
            AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"));
        var economicEvent = new EconomicEventReferenceDto(eventId, "AssetAccounting.CorporateAction", 1,
            EffectiveDate, observedAt, "test-custodian", $"dividend:{eventId:D}", SourceContentHash: sourceHash)
        {
            SecurityId = securityId, BookPositionId = positionId, RetainedEvidence = [retainedEvidence]
        };
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "cash-dividend", "v1", "test-fixture-v1",
            "base", EffectiveDate, DateTimeOffset.UtcNow, "test-custodian", $"dividend:{eventId:D}", economicEvent)
        {
            BookPositionId = positionId, RetainedEvidence = [retainedEvidence]
        };
        var bookContext = new AccountingBookContextDto(bookId, Fund, ownerId, FundStructureNodeKindDto.Fund,
            "Dividend GAAP book", "USD", AccountingBasisKindDto.Gaap, Policy, "v1", periodId, dimensions);
        var roleId = Guid.NewGuid();
        var role = new InstrumentRoleDto(roleId, securityId, Fund, "Fund", InstrumentRoleKinds.Holder,
            InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, new DateOnly(2026, 1, 1),
            Version: 1, OriginEvent: economicEvent, EvidenceLinks: [retainedEvidence.EvidenceUri]);
        var position = await assets.UpsertAsync(role, new BookPositionDto(positionId, securityId, roleId,
            bookContext, BookPositionSides.Long, "Active", new DateOnly(2026, 1, 1), Version: 1,
            OriginEvent: economicEvent, ProjectionLineage: lineage, EvidenceLinks: [retainedEvidence.EvidenceUri])
        { RetainedEvidence = [retainedEvidence] }, null, 0,
            new AssetOperationsWriteApprovalDto(Approver, "document://position-approval/dividend",
                "Retain the explicitly identified holding for this test.", DateTimeOffset.UtcNow), ct);
        var lot = await journal.SaveTaxLotAsync(new LedgerTaxLotRecord(
            Guid.NewGuid(), bookId, new LedgerAccount("Investments", LedgerAccountType.Asset, "DIVIDEND"),
            "dividend-lot", new DateOnly(2026, 1, 1), 500m, 500m, 10m, "USD", now, now,
            EvidenceRef: retainedEvidence.EvidenceUri, Version: 1, SecurityId: securityId, BookPositionId: positionId), ct);
        var projectedEffect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey,
            lineage.ModelVersion, EffectiveDate, Amount, Amount, "USD",
            [new ProjectedAccountingEffectLineDto("assets/cash", Amount, 0m, "USD", Dimensions: dimensions),
             new ProjectedAccountingEffectLineDto("income/dividends", 0m, Amount, "USD", Dimensions: dimensions)]);
        var scope = new AssetAccountingEventScopeDto(securityId, 1, positionId, position.Version, bookId, periodId,
            AccountingBasisKindDto.Gaap, Fund, Tenant, Company, dimensions);
        var projected = await spineService.ProjectAsync(new ProjectAssetAccountingEventRequestDto(
            AssetAccountingEventKindDto.CorporateAction, scope, economicEvent, lineage, projectedEffect,
            Amount, "USD", period.Version, Preparer, DateTimeOffset.UtcNow, [retainedEvidence]), ct);
        projected.Spine.SpineVersion.Should().Be(2);
        await spineService.BuildPostingCandidateAsync(new AssetAccountingPostingCandidateRequestDto(
            AssetAccountingEventKindDto.CorporateAction, scope, economicEvent, lineage, Amount, "USD",
            Preparer, DateTimeOffset.UtcNow, "500 shares at USD 0.24 dividend", 2, period.Version,
            RetainedEvidence: [retainedEvidence]), ct);
        var drafted = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        drafted.Stages[^1].Stage.Should().Be(AssetAccountingLifecycleStageDto.Drafted);
        drafted.DraftedCandidateResult!.IsBalanced.Should().BeTrue();
        var posting = new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets);
        var accounting = new CorporateActionCaseAccountingService(operations, assets, posting, books);
        var policyDecision = JsonSerializer.SerializeToElement(new
        {
            policyId = Policy, policyVersion = "v1", rulePackId = "corpact-pack", rulePackVersion = "v1",
            selectedRuleId = Rule, selectedRuleVersion = "v1", eventId, bookId, periodId,
            draftedCandidateFingerprint = drafted.DraftedCandidateFingerprint
        });
        var policyEvidence = await preparation.AddEvidenceAsync(new AddCorporateActionEvidenceRequestDto(
            processingCase.CaseId, processingCase.Version, "retain-policy-decision", Tenant, Company,
            CorporateActionEvidenceKinds.OperatorAnalysis, $"document://corporate-actions/{eventId:D}/policy",
            Preparer, EvidenceHash: AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(policyDecision),
            Metadata: policyDecision, ScopeAssertion: caseScope), ct);
        var lotSnapshot = JsonSerializer.SerializeToElement(lot);
        var lotEvidence = await preparation.AddEvidenceAsync(new AddCorporateActionEvidenceRequestDto(
            processingCase.CaseId, policyEvidence.Case.Version, "retain-lot-snapshot", Tenant, Company,
            CorporateActionEvidenceKinds.TaxLotSnapshot, $"document://corporate-actions/{eventId:D}/lots",
            Preparer, EvidenceHash: AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(lotSnapshot),
            Metadata: lotSnapshot, ScopeAssertion: caseScope), ct);
        // These request bindings are explicit test inputs. Their general source/lot resolution is a
        // separate review finding; success here must not be used to certify that unresolved authority.
        var attached = await accounting.AttachProjectionAsync(new AttachCorporateActionAccountingProjectionRequestDto(
            processingCase.CaseId, lotEvidence.Case.Version, "attach-dividend", Tenant, Company, eventId, 1,
            drafted.SpineVersion, AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(projected.Spine),
            drafted.DraftedCandidateFingerprint!,
            "corporate-action-posting/v1:" + drafted.DraftedCandidateFingerprint,
            policyEvidence.Evidence.EvidenceId, 1, lotEvidence.Evidence.EvidenceId, 1, Preparer, caseScope,
            Authority: PreparationAuthority), ct);
        var ready = await TransitionAsync(preparation, attached.Case, CorporateActionCaseStates.ReadyForApproval, ct);
        var approvalRequest = new ApproveCorporateActionCaseAccountingRequestDto(
            ready.CaseId, ready.Version, "approve-dividend", Tenant, Company, attached.Projection.ProjectionId,
            "Verified retained dividend notice, holding, balanced journal and open period.",
            $"document://corporate-actions/{eventId:D}/approval", new string('e', 64), Approver, caseScope,
            Authority: new CorporateActionAccountingDecisionAuthorityDto(true, false));
        var selfApproval = () => accounting.ApproveAsync(approvalRequest with { Actor = Preparer }, ct);
        (await selfApproval.Should().ThrowAsync<CorporateActionOperationException>())
            .Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
        var approved = await accounting.ApproveAsync(approvalRequest, ct);
        approved.Case.State.Should().Be(CorporateActionCaseStates.Approved);
        var postRequest = new PostCorporateActionCaseAccountingRequestDto(approved.Case.CaseId, approved.Case.Version,
            "post-dividend", Tenant, Company, attached.Projection.ProjectionId, approved.Approval.ApprovalId,
            "Post the independently approved dividend.", Approver, caseScope,
            Authority: new CorporateActionAccountingDecisionAuthorityDto(false, true));
        var posted = await accounting.PostAsync(postRequest, ct);
        posted.Replayed.Should().BeFalse();
        posted.Case.State.Should().Be(CorporateActionCaseStates.Posted);
        posted.Posting.TotalDebits.Should().Be(Amount);
        posted.Posting.TotalCredits.Should().Be(Amount);

        // Reconstruct every persistence-facing service to prove the result is not process-local.
        var reopenedJournal = new PostgresLedgerJournalStore(ledgerOptions);
        var reopenedAssets = new PostgresAssetOperationsProjectionStore(assetOptions, reopenedJournal);
        var reopenedOperations = new PostgresCorporateActionOperationsStore(securityOptions);
        var reopened = new CorporateActionCaseAccountingService(reopenedOperations, reopenedAssets,
            new AccountingPostingCandidatePostService(candidateBuilder, reopenedJournal,
                assetAccountingEventStore: reopenedAssets), new PostgresLedgerBookService(reopenedJournal));
        var replay = await reopened.PostAsync(postRequest, ct);
        replay.Replayed.Should().BeTrue();
        replay.Posting.Should().BeEquivalentTo(posted.Posting);
        var entries = await reopenedJournal.GetByPeriodAsync(periodId, ct);
        entries.Should().ContainSingle();
        entries.Single().Entry.JournalEntryId.Should().Be(posted.Posting.JournalEntryId);
        entries.Single().Entry.Lines.Sum(line => line.Debit).Should().Be(Amount);
        entries.Single().Entry.Lines.Sum(line => line.Credit).Should().Be(Amount);
        var reloaded = (await reopenedAssets.GetLatestAsync(eventId, 1, ct))!.Projection;
        reloaded.Stages.Select(stage => stage.Stage).Should().Equal(
            AssetAccountingLifecycleStageDto.Expected, AssetAccountingLifecycleStageDto.Projected,
            AssetAccountingLifecycleStageDto.Drafted, AssetAccountingLifecycleStageDto.Approved,
            AssetAccountingLifecycleStageDto.Posted);
        reloaded.PostedJournalImpact!.JournalEntryId.Should().Be(posted.Posting.JournalEntryId);
        (await reopenedOperations.GetCaseAsync(processingCase.CaseId, Tenant, Company, ct))!
            .State.Should().Be(CorporateActionCaseStates.Posted);
        (await reopenedJournal.ListOpenTaxLotsByAssetScopeAsync(bookId, securityId, positionId, EffectiveDate, ct))
            .Should().ContainSingle().Which.OpenQuantity.Should().Be(500m);
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
            JsonSerializer.SerializeToElement(new { displayName = "Dividend test equity", currency = "USD",
                countryOfRisk = "US", issuerName = "Fixture issuer", exchange = "XNYS", lotSize = 1, tickSize = 0.01m }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "DIV" + securityId.ToString("N"), true,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "test", Preparer, null, "Test equity"), ct);
        return new SecurityMasterQueryService(events, store, rebuilder);
    }

    private static async Task<CorporateActionProcessingCaseDto> CreateAccountingReviewCaseAsync(
        PostgresCorporateActionOperationsStore store, CorporateActionOperationsService service,
        Guid securityId, CorporateActionCaseScopeDto scope, CancellationToken ct)
    {
        var action = new CorporateActionDto(Guid.NewGuid(), securityId, CorporateActionEventTypes.Dividend,
            EffectiveDate, new DateOnly(2026, 8, 28), 0.24m, "USD", null, null, null, null, null, null, null,
            RecordDate: new DateOnly(2026, 8, 15), LifecycleState: CorporateActionLifecycleStates.Confirmed);
        var proposal = await service.RecordSourceProposalAsync(new RecordCorporateActionSourceProposalRequestDto(
            action, new CorporateActionProviderEventIdentityDto("test-custodian", action.CorpActId.ToString("D"),
                "v1", DateTimeOffset.UtcNow, EvidenceHash: CorporateActionEconomicFingerprint.Compute(action),
                EvidenceReference: $"document://corporate-actions/{action.CorpActId:D}/notice",
                ReleaseStatus: CorporateActionProviderReleaseStatusDto.AcceptanceEligible), Preparer), ct);
        // Matches the existing PostgreSQL source-acceptance fixture. Public source decisions remain
        // gated separately until affected-scope fan-out authority is available.
        var accepted = await store.AcceptSourceProposalAsync(new AcceptCorporateActionSourceProposalRequestDto(
            proposal.ProposalId, proposal.Version, "accept-dividend", scope, Preparer,
            Reason: "Retain explicit fixture source."), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            new string('a', 64), ct);
        var evidenced = await service.AddEvidenceAsync(new AddCorporateActionEvidenceRequestDto(
            accepted.Case.CaseId, accepted.Case.Version, "retain-notice", Tenant, Company,
            CorporateActionEvidenceKinds.CustodianNotice,
            proposal.ProviderIdentity.EvidenceReference!, Preparer,
            EvidenceHash: proposal.ProviderIdentity.EvidenceHash, ScopeAssertion: scope), ct);
        var confirmed = await TransitionAsync(service, evidenced.Case, CorporateActionCaseStates.TermsConfirmed, ct);
        return await TransitionAsync(service, confirmed, CorporateActionCaseStates.AccountingReview, ct);
    }

    private static async Task<CorporateActionProcessingCaseDto> TransitionAsync(
        CorporateActionOperationsService service, CorporateActionProcessingCaseDto processingCase,
        string target, CancellationToken ct) =>
        (await service.TransitionCaseAsync(new TransitionCorporateActionCaseRequestDto(
            processingCase.CaseId, processingCase.Version, "transition-" + target, Tenant, Company,
            target, Preparer, "Validated fixture preparation.", Authority: PreparationAuthority,
            ScopeAssertion: processingCase.Scope), ct)).Case;

    private static async Task ConfigureDividendRuleAsync(
        AccountingConfigurationService configuration, Guid bookId, CancellationToken ct)
    {
        foreach (var node in new[] {
            new ChartOfAccountsNodeDto("cash", "assets/cash", "Cash", "Asset"),
            new ChartOfAccountsNodeDto("dividends", "income/dividends", "Dividend Income", "Revenue") })
        {
            await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
                Fund, node, Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
        }
        await configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(Fund,
            new PostingRuleDto(Rule, "Cash dividend", "AssetAccounting.CorporateAction", "generated", "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1), Priority: 100,
                Formulas: [new AccountingRuleFormulaDto("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)],
                GeneratedPostings: [
                    new GeneratedPostingLineDto("cash", "assets/cash", AccountingTemplateLineSideDto.Debit, "amount", 0m, "USD"),
                    new GeneratedPostingLineDto("income", "income/dividends", AccountingTemplateLineSideDto.Credit, "amount", 0m, "USD")]),
            Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
    }
}
