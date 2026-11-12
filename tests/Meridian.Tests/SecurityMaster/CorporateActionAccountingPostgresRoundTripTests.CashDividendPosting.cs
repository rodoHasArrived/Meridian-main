using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
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
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class CorporateActionAccountingPostgresRoundTripTests
{
    [LedgerDatabaseFact]
    public async Task CashDividendNativePost_CommitsJournalSpineAndCaseAtomically_AndRollsBackFailedCallback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = server.ConnectionString,
            SchemaName = server.CreateSchemaName("ca_ledger"),
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };
        var securityOptions = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("ca_security"),
            PreloadProjectionCache = false,
            EnableSinglePrincipalCashDividendAttestations = true
        };
        var assetOptions = new AssetOperationsOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("ca_asset")
        };
        await new LedgerMigrationRunner(ledgerOptions).EnsureMigratedAsync(ct);
        await SecurityMasterTestCapabilityFloor.InitializeAndActivateAsync(server, securityOptions, ct);
        await new AssetOperationsMigrationRunner(assetOptions).EnsureMigratedAsync(ct);

        var journal = new PostgresLedgerJournalStore(ledgerOptions);
        var books = new PostgresLedgerBookService(journal);
        var assets = new PostgresAssetOperationsProjectionStore(assetOptions, journal);
        var operations = new PostgresCorporateActionOperationsStore(securityOptions, journal);
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
            periodId, bookId, PeriodStart.Year, PeriodStart.Month, PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            PeriodStart, PeriodStart.AddMonths(1).AddDays(-1),
            "Open", now, null, 0), 0, ct: ct);
        var caseScope = new CorporateActionCaseScopeDto(
            Tenant, Company, FundProfileId: Fund, LedgerBookId: bookId.ToString("D"),
            PeriodId: periodId.ToString("D"), AccountingBasis: "GAAP", FunctionalCurrency: "USD");
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
        await ConfigureReceivableDividendRuleAsync(configuration, bookId, ct);
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)),
            books, policies, taxLotStore: journal);
        var spineService = new AssetAccountingEventSpineService(
            assets, assets, securityQuery, books, policies, configuration, candidateBuilder, journal);
        var positionId = Guid.NewGuid();
        var eventId = processingCase.CorporateActionId;
        var dimensions = new LedgerDimensionSetDto(FundId: Fund, EntityId: Company,
            InstrumentId: securityId, BookId: bookId.ToString("D"))
        { PositionId = positionId };
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
            SecurityId = securityId,
            BookPositionId = positionId,
            EvidenceLinks = [retainedEvidence.EvidenceUri],
            RetainedEvidence = [retainedEvidence]
        };
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "cash-dividend", "v1", "test-fixture-v1",
            "base", EffectiveDate, DateTimeOffset.UtcNow, "test-custodian", $"dividend:{eventId:D}", economicEvent)
        {
            BookPositionId = positionId,
            RetainedEvidence = [retainedEvidence]
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
        // Seed the pre-existing holding explicitly in compatibility mode. Dividend approval,
        // posting and replay continue through the governed journal store.
        var historicalSeed = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions
        {
            ConnectionString = ledgerOptions.ConnectionString,
            SchemaName = ledgerOptions.SchemaName,
            RequireGovernedPostingCommand = false,
            RequireExpectedVersion = true
        });
        var lot = await historicalSeed.SaveTaxLotAsync(new LedgerTaxLotRecord(
            Guid.NewGuid(), bookId, new LedgerAccount("Investments", LedgerAccountType.Asset, ("DIV" + securityId.ToString("N")).ToUpperInvariant()),
            "dividend-lot", new DateOnly(2026, 1, 1), 500m, 500m, 10m, "USD", now, now,
            EvidenceRef: retainedEvidence.EvidenceUri, Version: 1, SecurityId: securityId, BookPositionId: positionId), ct);
        var projectedEffect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey,
            lineage.ModelVersion, EffectiveDate, Amount, Amount, "USD",
            [new ProjectedAccountingEffectLineDto("assets/dividend-receivable", Amount, 0m, "USD", Dimensions: dimensions),
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
        drafted.DraftedCandidate!.InstrumentSymbol.Should().Be(("DIV" + securityId.ToString("N")).ToUpperInvariant());
        var posting = new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets);
        var accounting = new CorporateActionCaseAccountingService(operations, assets, posting, books);
        var policyDecision = JsonSerializer.SerializeToElement(new
        {
            policyId = Policy,
            policyVersion = "v1",
            rulePackId = "corpact-pack",
            rulePackVersion = "v1",
            selectedRuleId = Rule,
            selectedRuleVersion = "v1",
            eventId,
            bookId,
            periodId,
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

        // These operands deliberately exercise the native producer/ledger transaction boundary.
        // Complete source eligibility, canonical inventory and reconciliation population are tested
        // separately by the application resolver; this fixture does not claim a source certification.
        var roleBinding = new CashDividendAccountRoleBindingDto(bookId, "assets/dividend-receivable", "income/dividends",
            "corpact-pack", "v1", Rule, "v1", DividendReceivableAccountName: "Dividend receivable",
            DividendIncomeAccountName: "Dividend income");
        var declaration = (await operations.ChangeCashDividendDeclarationAsync(new(Guid.NewGuid(), 0, Tenant, Company,
            bookId, true, CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
            "Synthetic bounded native posting fixture", Preparer, new(true, CanBindAccountingRoles: true), roleBinding), ct)).Declaration;
        var snapshot = JsonSerializer.Serialize(new { schemaVersion = 1, nativeFixture = true, sourceHash, gross = Amount });
        var snapshotHash = Sha256Digest.ComputeUtf8(snapshot);
        var attestationRequest = new AttestCorporateActionCashDividendRequestDto(ready.CaseId, ready.Version,
            "attest-native-dividend", Tenant, Company, attached.Projection.ProjectionId, declaration.Revision,
            snapshotHash, "Reviewed this synthetic receivable/income dividend", "cash-dividend-eligibility:" + snapshotHash,
            snapshotHash, CorporateActionCashDividendAttestationPolicy.Confirmation, Preparer, caseScope, Authority: new(true, false));
        var source = ready.SourceSnapshot!.ProviderIdentity;
        var receipt = new CorporateActionCashDividendAttestationDto(PreparerAttestationApprovalBoundary.NewReceiptId(),
            ready.CaseId, attached.Projection.ProjectionId, ready.Version, declaration.DeclarationId, declaration.Revision,
            declaration.PolicyVersion, declaration.PolicyHash, Guid.NewGuid(),
            CorporateActionCashDividendAttestationValidation.RelatedEventKey(securityId, EffectiveDate), EffectiveDate,
            source.ProviderId, source.SourceEventId, source.SourceEventVersion, source.EvidenceHash!, snapshotHash, snapshot,
            Amount, Amount, PeriodStart, Preparer, DateTimeOffset.UtcNow, attestationRequest.Reason,
            attestationRequest.EvidenceReference, snapshotHash, attestationRequest.Confirmation,
            CorporateActionCashDividendAttestationPolicy.Disclosure, AccountRoleBinding: roleBinding);
        var attested = await operations.AttestCashDividendAsync(attestationRequest, receipt, Guid.NewGuid(), new string('e', 64), ct);
        var noLeaseProjection = drafted with
        {
            SpineVersion = drafted.SpineVersion + 1,
            Stages = [.. drafted.Stages, new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.PreparerAttested,
                attested.Attestation.AttestedAtUtc, Preparer, drafted.RetainedEvidence,
                PreparerAttestationApprovalBoundary.ReceiptReference(attested.Attestation.AttestationId), attested.Attestation.Reason)],
            PreparerAttestation = new(attested.Attestation, bookId, periodId, Tenant, Company, eventId, 1,
                drafted.DraftedCandidateFingerprint!, attached.Projection.PostingIntentHash)
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => assets.AppendAsync(noLeaseProjection,
            drafted.SpineVersion, drafted.Scope.ExpectedBookPositionVersion, ct));
        var postRequest = new PostAttestedCorporateActionCashDividendRequestDto(ready.CaseId, attested.Case.Version,
            "post-native-dividend", Tenant, Company, attached.Projection.ProjectionId, attested.Attestation.AttestationId,
            "Post the bounded preparer-attested dividend", Preparer, caseScope, Authority: new(false, true));
        async Task<CorporateActionCashDividendPostingDto> PostNative(CorporateActionCashDividendPostingLease lease, CancellationToken postingCt)
        {
            var lower = await posting.PostAttestedCashDividendCandidateAsync(new(drafted.DraftedCandidate!, Preparer,
                attested.Attestation.AttestationId, Tenant, Company, postRequest.Reason), lease, postingCt);
            var impact = lower.JournalImpact!;
            await Assert.ThrowsAsync<CorporateActionValidationException>(() => lease.EnsureCanonicalInstrumentAsync(
                "ANOTHER-TICKER", securityId, drafted.Scope.ExpectedSecurityVersion,
                drafted.DraftedCandidate!.AccountingTimestamp, postingCt));
            await Assert.ThrowsAsync<CorporateActionValidationException>(() => lease.EnsureCanonicalInstrumentAsync(
                null, securityId, drafted.Scope.ExpectedSecurityVersion, drafted.DraftedCandidate!.AccountingTimestamp, postingCt));
            await Assert.ThrowsAsync<CorporateActionValidationException>(() => lease.EnsureCanonicalInstrumentAsync(
                drafted.DraftedCandidate!.InstrumentSymbol, securityId, drafted.Scope.ExpectedSecurityVersion + 1,
                drafted.DraftedCandidate.AccountingTimestamp, postingCt));
            await Assert.ThrowsAsync<CorporateActionValidationException>(() => lease.EnsureCanonicalInstrumentAsync(
                drafted.DraftedCandidate!.InstrumentSymbol, Guid.NewGuid(), drafted.Scope.ExpectedSecurityVersion,
                drafted.DraftedCandidate.AccountingTimestamp, postingCt));
            await lease.EnsureCanonicalInstrumentAsync(drafted.DraftedCandidate!.InstrumentSymbol,
                securityId, drafted.Scope.ExpectedSecurityVersion, drafted.DraftedCandidate.AccountingTimestamp, postingCt);
            await lease.EnsureCurrentAsync(Preparer, bookId, periodId,
                attached.Projection.DraftedCandidateFingerprint, attached.Projection.PostingIntentHash, postingCt);
            var retainedJournal = (await lease.GetJournalAsync(journal, impact.JournalEntryId, postingCt))!;
            var repeatedWrite = new LedgerJournalEntryWrite(retainedJournal.Entry, bookId, periodId,
                retainedJournal.CommandId, retainedJournal.CorrelationId, retainedJournal.AccountingBasis,
                retainedJournal.AccountingPolicyId, retainedJournal.AccountingPolicyVersion,
                retainedJournal.RuleId, retainedJournal.RuleVersion, retainedJournal.SourceEventId,
                PostingCommand: lower.Candidate.PostingCommand, LedgerBookId: bookId);
            var reusedLease = await Assert.ThrowsAsync<CorporateActionValidationException>(() =>
                lease.AppendJournalAsync(journal, repeatedWrite, postingCt));
            reusedLease.Message.Should().Contain("unused");
            return new(Guid.NewGuid(), ready.CaseId, attached.Projection.ProjectionId, lease.Attestation.AttestationId,
                impact.JournalEntryId, bookId, periodId, impact.AccountingBasis.ToString(), "USD", impact.TotalDebits, impact.TotalCredits,
                "Posted", null, Preparer, impact.PostedAtUtc);
        }

        CorporateActionCashDividendPostingLease? escapedLease = null;
        var rolledBack = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.ExecuteAttestedCashDividendPostingAsync(
            postRequest, async (lease, postingCt) =>
            {
                escapedLease = lease;
                await PostNative(lease, postingCt);
                throw new InvalidOperationException("Synthetic failure after journal and Posted spine, before case receipt commit");
            }, new string('f', 64), ct));
        rolledBack.Message.Should().Be("Synthetic failure after journal and Posted spine, before case receipt commit");
        (await journal.GetByPeriodAsync(periodId, ct)).Should().BeEmpty();
        (await assets.GetLatestAsync(eventId, 1, ct))!.Projection.Stages[^1].Stage.Should().Be(AssetAccountingLifecycleStageDto.Drafted);
        (await operations.GetCaseAsync(ready.CaseId, Tenant, Company, ct))!.State.Should().Be(CorporateActionCaseStates.PreparerAttested);
        await Assert.ThrowsAsync<CorporateActionValidationException>(() => escapedLease!.EnsureCurrentAsync(Preparer,
            bookId, periodId, attached.Projection.DraftedCandidateFingerprint, attached.Projection.PostingIntentHash, ct));

        var postingHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePosting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preservedConfiguration = await configuration.GetWorkspaceAsync(Fund, bookId, ct, Tenant, Company);
        var securityProjectionStore = new PostgresSecurityMasterStore(securityOptions);
        var preservedSecurityProjection = (await securityProjectionStore.GetProjectionAsync(securityId, ct))!;
        var eligibilityResolver = Substitute.For<ICorporateActionCashDividendEligibilityResolver>();
        eligibilityResolver.ResolveAsync(Arg.Any<CorporateActionProcessingCaseDto>(),
            Arg.Any<CorporateActionCaseAccountingProjectionDto>(), Arg.Any<AssetAccountingEventSpineDto>(),
            Arg.Any<CancellationToken>()).Returns(new CorporateActionCashDividendEligibilityDto(securityId, EffectiveDate,
                receipt.RelatedEventKey, source.ProviderId, source.SourceEventId, source.SourceEventVersion, source.EvidenceHash!,
                ready.SourceSnapshot.ProposedAction.DividendPerShare!.Value, Amount, Amount, PeriodStart,
                snapshotHash, snapshot, roleBinding));
        // Forward the application's actual factory callback into the real native transaction.
        // The hook only pauses after it produces a result so concurrent exclusion is observable.
        var heldNativeStore = Substitute.For<ICorporateActionCashDividendAccountingStore>();
        heldNativeStore.ExecuteAttestedCashDividendPostingAsync(Arg.Any<PostAttestedCorporateActionCashDividendRequestDto>(),
            Arg.Any<Func<CorporateActionCashDividendPostingLease, CancellationToken, Task<CorporateActionCashDividendPostingDto>>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.ArgAt<PostAttestedCorporateActionCashDividendRequestDto>(0);
            var callback = call.ArgAt<Func<CorporateActionCashDividendPostingLease, CancellationToken, Task<CorporateActionCashDividendPostingDto>>>(1);
            var fingerprint = call.ArgAt<string>(2);
            var cancellation = call.ArgAt<CancellationToken>(3);
            return operations.ExecuteAttestedCashDividendPostingAsync(request, async (lease, postingCt) =>
            {
                var result = await callback(lease, postingCt);
                postingHeld.SetResult(true);
                await releasePosting.Task.WaitAsync(postingCt);
                return result;
            }, fingerprint, cancellation);
        });
        var nativeApplication = new CorporateActionCaseAccountingService(operations, assets, posting, books,
            heldNativeStore, eligibilityResolver);
        var postingTask = nativeApplication.PostAttestedCashDividendAsync(postRequest, ct);
        CorporateActionCashDividendPostingResultDto posted;
        try
        {
            var enteredOrFailed = await Task.WhenAny(postingHeld.Task, postingTask).WaitAsync(TimeSpan.FromSeconds(15), ct);
            if (ReferenceEquals(enteredOrFailed, postingTask))
                await postingTask;
            await postingHeld.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            // An ordinary lot writer must wait for the complete-book entitlement boundary;
            // updating a sibling quantity cannot slip between preview recheck and journal commit.
            var ordinaryLotWrite = historicalSeed.SaveTaxLotAsync(lot with { OpenQuantity = 499m }, ct);
            var ordinaryConfigurationWrite = configurationStore.SaveAsync(preservedConfiguration, ct);
            var ordinarySecurityWrite = securityProjectionStore.UpsertProjectionAsync(preservedSecurityProjection, ct);
            var concurrentWithdrawal = operations.ChangeCashDividendDeclarationAsync(new(Guid.NewGuid(), 1, Tenant,
                Company, bookId, false, declaration.PolicyVersion, declaration.PolicyHash,
                "Withdraw concurrently with native posting", Preparer, new(true)), ct);
            await Task.Delay(100, ct);
            ordinaryLotWrite.IsCompleted.Should().BeFalse();
            ordinaryConfigurationWrite.IsCompleted.Should().BeFalse();
            ordinarySecurityWrite.IsCompleted.Should().BeFalse();
            concurrentWithdrawal.IsCompleted.Should().BeFalse();
            releasePosting.TrySetResult(true);
            posted = await postingTask;
            (await ordinaryLotWrite).OpenQuantity.Should().Be(499m);
            await ordinaryConfigurationWrite;
            await ordinarySecurityWrite;
            (await concurrentWithdrawal).Declaration.Enabled.Should().BeFalse();
        }
        finally { releasePosting.TrySetResult(true); }
        posted.Replayed.Should().BeFalse();
        posted.Case.State.Should().Be(CorporateActionCaseStates.Posted);
        var journalRecord = (await journal.GetByPeriodAsync(periodId, ct)).Should().ContainSingle().Subject;
        var journalEntry = journalRecord.Entry;
        journalEntry.JournalEntryId.Should().Be(posted.Posting.JournalEntryId);
        posted.Posting.AccountingBasis.Should().Be(attached.Projection.AccountingBasis);
        posted.Posting.PostedAtUtc.Should().Be(journalRecord.CreatedAt);
        journalEntry.Lines.Sum(line => line.Debit).Should().Be(Amount);
        journalEntry.Lines.Sum(line => line.Credit).Should().Be(Amount);
        journalEntry.Lines.Should().OnlyContain(line => line.Account.Symbol == drafted.DraftedCandidate!.InstrumentSymbol);
        journalEntry.Metadata.Symbol.Should().Be(drafted.DraftedCandidate.InstrumentSymbol);
        journalEntry.Metadata.PreparerAttestation!.Receipt.AttestationId.Should().Be(attested.Attestation.AttestationId);
        journalEntry.Metadata.Tags!.Should().NotContainKey("approvalId");
        var latest = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        latest.Stages.Select(stage => stage.Stage).Should().Equal(AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected, AssetAccountingLifecycleStageDto.Drafted,
            AssetAccountingLifecycleStageDto.PreparerAttested, AssetAccountingLifecycleStageDto.Posted);
        latest.PreparerAttestation!.Receipt.AttestationId.Should().Be(attested.Attestation.AttestationId);
        foreach (var stage in new[] { AssetAccountingLifecycleStageDto.Reconciled, AssetAccountingLifecycleStageDto.Reported })
        {
            var reference = "native-dividend-" + stage;
            var evidenceAt = DateTimeOffset.UtcNow;
            var followOnEvidence = new RetainedEvidenceIdentityDto(reference, "document://native-dividend/" + reference,
                new string('9', 64), "fixture-reconciliation", reference, RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
                Approver, evidenceAt, EffectiveDate, 1, evidenceAt, Approver,
                stage == AssetAccountingLifecycleStageDto.Reconciled ? AssetAccountingEvidenceSubjects.Reconciliation : AssetAccountingEvidenceSubjects.Report,
                AssetAccountingEvidenceSubjects.LifecycleSubjectId(eventId, Fund, bookId, Tenant, Company, reference));
            var appendedStage = await spineService.AppendLifecycleStageAsync(new(eventId, 1, stage, latest.SpineVersion,
                latest.Scope.ExpectedBookPositionVersion, Fund, Approver, evidenceAt, reference, [followOnEvidence],
                TenantId: Tenant, CompanyId: Company), ct);
            latest = appendedStage.Spine;
            latest.PreparerAttestation!.Receipt.AttestationId.Should().Be(attested.Attestation.AttestationId);
        }
        var originalProof = await operations.GetCashDividendPostedProofAsync(posted.Posting.JournalEntryId, Tenant, Company, ct);
        originalProof!.Attestation.Should().BeEquivalentTo(attested.Attestation);
        originalProof.Declaration.Should().Be(declaration);
        securityOptions.EnableSinglePrincipalCashDividendAttestations = false;
        var reopened = new PostgresCorporateActionOperationsStore(securityOptions, new PostgresLedgerJournalStore(ledgerOptions));
        var reopenedApplication = new CorporateActionCaseAccountingService(reopened);
        var replay = await reopenedApplication.PostAttestedCashDividendAsync(postRequest with { Authority = new(false, false) }, ct);
        replay.Replayed.Should().BeTrue();
        replay.Posting.Should().Be(posted.Posting);
        (await reopened.GetCashDividendPostedProofAsync(posted.Posting.JournalEntryId, Tenant, Company, ct))!
            .Declaration.Enabled.Should().BeTrue();
        (await reopened.GetCashDividendPostedProofAsync(posted.Posting.JournalEntryId, "foreign-tenant", Company, ct)).Should().BeNull();
        var readback = (await reopened.GetCaseAsync(ready.CaseId, Tenant, Company, ct))!.AccountingStatus!;
        readback.ApprovalId.Should().BeNull();
        readback.CashDividendAttestationId.Should().Be(attested.Attestation.AttestationId);
        readback.PostedJournalEntryId.Should().Be(posted.Posting.JournalEntryId);
        await AssertCashDividendHistoryImmutableAsync(securityOptions, ct);
        await using var nativeConnection = new Npgsql.NpgsqlConnection(securityOptions.ConnectionString);
        await nativeConnection.OpenAsync(ct);
        await using var reservationCount = nativeConnection.CreateCommand();
        reservationCount.CommandText = $"select count(*),sum(gross_usd) from \"{securityOptions.Schema}\".cash_dividend_attestation_reservations";
        await using var reservationReader = await reservationCount.ExecuteReaderAsync(ct);
        (await reservationReader.ReadAsync(ct)).Should().BeTrue();
        reservationReader.GetInt64(0).Should().Be(1);
        reservationReader.GetDecimal(1).Should().Be(Amount);
    }

    private static async Task ConfigureReceivableDividendRuleAsync(AccountingConfigurationService configuration,
        Guid bookId, CancellationToken ct)
    {
        foreach (var node in new[] {
            new ChartOfAccountsNodeDto("dividend-receivable", "assets/dividend-receivable", "Dividend receivable", "Asset"),
            new ChartOfAccountsNodeDto("dividend-income", "income/dividends", "Dividend income", "Revenue") })
            await configuration.UpsertChartNodeAsync(new(Fund, node, Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
        await configuration.UpsertPostingRuleAsync(new(Fund,
            new PostingRuleDto(Rule, "Cash dividend declaration", "AssetAccounting.CorporateAction", "generated", "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1), Priority: 100,
                Formulas: [new AccountingRuleFormulaDto("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)],
                GeneratedPostings: [
                    new GeneratedPostingLineDto("receivable", "assets/dividend-receivable", AccountingTemplateLineSideDto.Debit, "amount", 0m, "USD"),
                    new GeneratedPostingLineDto("income", "income/dividends", AccountingTemplateLineSideDto.Credit, "amount", 0m, "USD")]),
            Approver, CompanyId: Company, LedgerBookId: bookId, TenantId: Tenant), ct);
    }
}
