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
using Meridian.Reporting;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>Advance-refunding projection, independent approval and posting retain both successor identities and bases.</summary>
[Trait("Category", "Integration")]
public sealed class AssetCorporateActionRefundingPostgresRoundTripTests
{
    [LedgerDatabaseFact]
    public Task AdvanceRefunding_RealMappedProjectionApprovalAndRestartConserveOriginalAndCurrentFaceBasis()
        => AssertGovernedRefundingAsync(60m);

    [LedgerDatabaseFact]
    public Task AdvanceRefunding_EqualAllocationsResolveDistinctSuccessorLotsAndReplayAfterRestart()
        => AssertGovernedRefundingAsync(50m);

    private static async Task AssertGovernedRefundingAsync(decimal refundedPercent)
    {
        const string fund = "refunding-pipeline-fund";
        const string tenant = "refunding-pipeline-tenant";
        const string company = "refunding-pipeline-company";
        const string preparer = "fund-accountant";
        const string approver = "independent-controller";
        const string policy = "corporate-gaap";
        const string rule = "carrying-transfer";
        const string assetPath = "assets/investments";
        const string assetName = "Investments";
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var acquired = date.AddYears(-1);
        var holdingPeriod = acquired.AddMonths(-3);
        var periodStart = new DateOnly(date.Year, date.Month, 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = server.ConnectionString,
            SchemaName = server.CreateSchemaName("refunding_pipeline_ledger"),
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };
        var securityOptions = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("refunding_pipeline_security"),
            PreloadProjectionCache = false
        };
        var assetOptions = new AssetOperationsOptions
        { ConnectionString = server.ConnectionString, Schema = server.CreateSchemaName("refunding_pipeline_asset") };
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
        var lotId = Guid.NewGuid();
        await journal.SaveLedgerBookAsync(new(bookId, fund, ownerId, FundStructureNodeKindDto.Fund,
            "Refunding GAAP book", "USD", now, now, AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: policy, AccountingPolicyVersion: "v1"), ct);
        var period = await journal.SavePeriodAsync(new(Guid.NewGuid(), bookId, periodStart.Year, periodStart.Month,
            periodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture), periodStart, periodStart.AddMonths(1).AddDays(-1),
            "Open", now, null, 0), 0, ct: ct);
        var empty = JsonSerializer.SerializeToElement(new { });
        async Task<SecurityProjectionRecord> RetainSecurity(Guid id, string name)
        {
            await securities.UpsertProjectionAsync(new SecurityProjectionRecord(id, "Bond", SecurityStatusDto.Active,
                name, "USD", "ISIN", "TESTREFUND" + id.ToString("N"), JsonSerializer.SerializeToElement(new
                {
                    maturityDate = date.AddYears(1).ToString("yyyy-MM-dd"),
                    dayCountConvention = "30/360",
                    couponRate = 10m,
                    paymentFrequency = "annual",
                    couponType = "Fixed",
                    isCallable = false
                }), empty, empty, 1,
                new DateTimeOffset(acquired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), null, [], []), ct);
            return (await securities.GetProjectionAsync(id, ct))!;
        }
        var security = await RetainSecurity(securityId, "Predecessor bond");
        RetainedEvidenceIdentityDto Evidence(string id, DateOnly effective, string subject, Guid subjectId, string hash)
            => new(id, "document://corporate-action/" + id, hash, "custodian", id,
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus, preparer, now, effective, 1, now,
                "retention-service", subject, subjectId.ToString("D"));
        RetainedEvidenceIdentityDto SecurityEvidence(SecurityProjectionRecord value)
            => Evidence("security-" + value.SecurityId.ToString("N"), acquired, "SecurityMasterProjection", value.SecurityId,
                OpenLotAmortization.SecurityHash(value)) with
            { EvidenceVersion = value.Version };
        var acquisitionEvidence = Evidence("original-acquisition", acquired, "OpenLotAcquisition", lotId, new string('a', 64));
        var account = new LedgerAccount(assetName, LedgerAccountType.Asset);
        await journal.SaveTaxLotAsync(new LedgerTaxLotRecord(lotId, bookId, account, "original-face", acquired,
            60m, 60m, 110m, "USD", now, now, EvidenceRef: acquisitionEvidence.EvidenceId, Version: 1,
            SecurityId: securityId, BookPositionId: positionId, OriginalFace: 6_000m, BookedFactor: 1m, ParBasis: 100m,
            Acquisition: new(LotQuantityBasis.Face, "USD", "USD", 1m, 6_600m, 6_600m, holdingPeriod,
                new(100m, 1m, BondAmortizationMethod.StraightLine, null), [acquisitionEvidence])), ct);
        var lot = (await journal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single();
        var dimensions = new LedgerDimensionSetDto(FundId: fund, EntityId: company,
            InstrumentId: securityId, BookId: bookId.ToString("D"))
        { PositionId = positionId };
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction);
        var context = new AccountingBookContextDto(bookId, fund, ownerId, FundStructureNodeKindDto.Fund,
            "Refunding GAAP book", "USD", AccountingBasisKindDto.Gaap, policy, "v1", period.PeriodId, dimensions);
        var acquisitionOrigin = new EconomicEventReferenceDto(Guid.NewGuid(), "Trade", 1, acquired, now,
            "custodian", acquisitionEvidence.SourceReference)
        { SecurityId = securityId, BookPositionId = positionId, RetainedEvidence = [acquisitionEvidence], EvidenceLinks = [acquisitionEvidence.EvidenceUri] };
        async Task<BookPositionDto> RetainPosition(SecurityProjectionRecord value, Guid id)
        {
            var roleId = Guid.NewGuid();
            var origin = acquisitionOrigin with { SecurityId = value.SecurityId, BookPositionId = id };
            return await assets.UpsertAsync(new InstrumentRoleDto(roleId, value.SecurityId, fund, "Fund", InstrumentRoleKinds.Holder,
                    InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, acquired, OriginEvent: origin,
                    EvidenceLinks: origin.EvidenceLinks),
                new BookPositionDto(id, value.SecurityId, roleId,
                    context with { Dimensions = dimensions with { InstrumentId = value.SecurityId, PositionId = id } },
                    BookPositionSides.Long, "Active", acquired,
                    OriginEvent: origin, EvidenceLinks: origin.EvidenceLinks)
                { RetainedEvidence = [acquisitionEvidence] },
                null, 0, new(approver, "document://position-approval/" + id.ToString("N"), "Retain reviewed bond ownership.", now), ct);
        }
        var position = await RetainPosition(security, positionId);
        // Establish the source's adjusted basis through the governed atomic amortization path.
        // Its original 6,600 acquisition basis remains immutable while current basis becomes 6,300.
        var amortization = new OpenLotAmortizationInstructionDto(lot.ToOpenLot(), security,
            SecurityEvidence(security), position.Version, date);
        var amortized = OpenLotAmortization.Project(amortization);
        amortized.FunctionalMovement.Should().Be(-300m);
        var amortizationId = Guid.NewGuid();
        var amortizationEventId = Guid.NewGuid();
        var amortizationKey = "refunding-prior-amortization:" + Guid.NewGuid().ToString("N");
        var amortizationDimensions = new LedgerLineDimensionSet(InstrumentId: securityId) { PositionId = positionId };
        var provenance = $"security-master:{securityId:N};snapshot:test-source-hash;approved:true";
        var amortizationJournal = new JournalEntry(amortizationId, now, "Prior reviewed source amortization",
            [new LedgerEntry(Guid.NewGuid(), amortizationId, now, account, 0m, 300m, "Prior reviewed source amortization", amortizationDimensions,
                 new LedgerEntryCurrency("USD", "USD", 0m, 300m, 1m)),
             new LedgerEntry(Guid.NewGuid(), amortizationId, now, new LedgerAccount("Amortization income", LedgerAccountType.Revenue),
                 300m, 0m, "Prior reviewed source amortization", amortizationDimensions, new LedgerEntryCurrency("USD", "USD", 300m, 0m, 1m))],
            new(SecurityId: securityId, EffectiveDate: date, IdempotencyKey: amortizationKey,
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["securityMasterProvenance"] = provenance,
                    ["securityMasterLineage"] = $"LOT:{securityId:N}:ledger-map:investment-lots:sm-approval:lot-controller:security-status:active:{provenance}"
                }));
        var amortizationEventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization);
        var amortizationEventEvidence = Evidence("prior-amortization-event", date,
            AssetAccountingEvidenceSubjects.Event, amortizationEventId, new string('d', 64));
        RetainedEvidenceIdentityDto[] amortizationEvidence =
            [acquisitionEvidence, amortization.SecurityEvidence, amortizationEventEvidence];
        var amortizationEvent = new EconomicEventReferenceDto(amortizationEventId, amortizationEventType, 1, date,
            now, amortizationEventEvidence.SourceSystem, amortizationEventEvidence.SourceReference,
            SourceContentHash: amortizationEventEvidence.ContentHashSha256)
        {
            SecurityId = securityId,
            BookPositionId = positionId,
            RetainedEvidence = amortizationEvidence,
            EvidenceLinks = amortizationEvidence.Select(item => item.EvidenceUri).ToArray()
        };
        var amortizationLineage = new ProjectionLineageDto(Guid.NewGuid(), null, "canonical-lot-amortization",
            OpenLotAmortization.ModelVersion, "refunding-fixture-v1", "base", date, now,
            amortizationEventEvidence.SourceSystem, amortizationEventEvidence.SourceReference, amortizationEvent)
        {
            BookPositionId = positionId,
            RetainedEvidence = amortizationEvidence,
            EvidenceLinks = amortizationEvent.EvidenceLinks
        };
        var amortizationPosting = new AccountingPostingCommandDto(Guid.NewGuid(), bookId, period.PeriodId, date, now,
            amortizationKey, AccountingPostingIntentDto.Adjustment, amortizationEventId, ExpectedVersion: period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "prior-amortization-controller-review",
            OperatorRationale: "Independently reviewed original basis and fixed-rate bond amortization.", LedgerBookId: bookId)
        {
            Actor = approver,
            SourceEventType = amortizationEventType,
            BookContext = context,
            BookPositionId = positionId,
            EconomicEvent = amortizationEvent,
            ProjectionLineage = amortizationLineage,
            RulePackReference = new("canonical-amortization", "1", "amortization", "1"),
            LotAmortization = amortization,
            Evidence = amortizationEvidence.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference,
                Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate,
                EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray()
        };
        var amortizationWrite = new LedgerJournalEntryWrite(amortizationJournal, bookId, period.PeriodId,
            SourceEventId: amortizationEventId, LedgerBookId: bookId, PostingCommand: amortizationPosting,
            AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: policy, AccountingPolicyVersion: "v1",
            RuleId: "amortization", RuleVersion: "1");
        await journal.AppendAssetPostingAsync(AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), bookId,
            amortizationWrite, amortizationEventId, amortizationKey, period.Version, AtomicTaxLotMutationKind.Amortization,
            amortizationEvidence, amortization: amortization), ct);
        lot = (await journal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single();
        lot.ToOpenLot().OpenQuantity.Should().Be(6_000m);
        lot.ToOpenLot().OpenFunctionalCostBasis.Should().Be(6_300m);
        var successors = new List<OpenLotCorporateActionSuccessorDto>();
        foreach (var refunded in new[] { true, false })
        {
            var percent = refunded ? refundedPercent : 100m - refundedPercent;
            var targetSecurity = await RetainSecurity(Guid.NewGuid(), refunded ? "Refunded successor bond" : "Unrefunded successor bond");
            var targetPosition = await RetainPosition(targetSecurity, Guid.NewGuid());
            var targetLotId = Guid.NewGuid();
            successors.Add(new(targetLotId, refunded ? "refunded-face" : "unrefunded-face", assetName,
                targetSecurity, SecurityEvidence(targetSecurity), targetPosition.PositionId, targetPosition.Version,
                6_000m * percent / 100m, percent,
                refunded ? CorporateActionSuccessorRoleDto.Refunded : CorporateActionSuccessorRoleDto.Unrefunded,
                refunded ? ["ScheduleD"] : [],
                Evidence("successor-" + targetLotId.ToString("N"), acquired, "OpenLotAcquisition", targetLotId, new string(refunded ? 'b' : 'c', 64)), assetPath));
        }
        var reviewedPositionVersion = position.Version + 1;
        var instruction = new OpenLotCorporateActionInstructionDto(Guid.NewGuid(), CorporateActionAccountingTypeDto.AdvanceRefunding, date,
            lot.ToOpenLot(), security, SecurityEvidence(security), reviewedPositionVersion, successors, [], assetName);
        var scope = new AssetAccountingEventScopeDto(securityId, security.Version, positionId, reviewedPositionVersion, bookId,
            period.PeriodId, AccountingBasisKindDto.Gaap, fund, tenant, company, dimensions);
        var prepared = CanonicalLotCorporateActionServiceTests.ProjectAndMap(instruction, scope, period.Version, assetPath, preparer, now);
        var mapped = prepared.Mapped;
        instruction = prepared.Reviewed;
        mapped.LotMutations.Mutations.Select(mutation => mutation.AllocationPercent).Should().Equal(refundedPercent / 100m, 1m - refundedPercent / 100m);
        var currentRole = (await assets.GetSecurityAsync(securityId, ct)).InstrumentRoles.Single();
        position = await assets.UpsertAsync(currentRole, position with
        {
            Version = reviewedPositionVersion,
            ProjectionLineage = mapped.Event.ProjectionLineage,
            RetainedEvidence = mapped.Event.RetainedEvidence
        }, null, position.Version,
            new(approver, "document://position-approval/refunding-revision", "Retain reviewed advance-refunding projection and evidence.", now), ct);
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
            new PostingRuleDto(rule, "Canonical advance refunding", eventType, "generated", "v1", EffectiveFrom: acquired, Priority: 100,
                Formulas: [new("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m),
                    new("refunded", AccountingRuleFormulaKindDto.PercentageOfSourceAmount, refundedPercent / 100m),
                    new("unrefunded", AccountingRuleFormulaKindDto.PercentageOfSourceAmount, 1m - refundedPercent / 100m)],
                GeneratedPostings:
                [new("predecessor", assetPath, AccountingTemplateLineSideDto.Credit, "amount", 0m, "USD"),
                 new("refunded", assetPath, AccountingTemplateLineSideDto.Debit, "refunded", 0m, "USD"),
                 new("unrefunded", assetPath, AccountingTemplateLineSideDto.Debit, "unrefunded", 0m, "USD")]), approver,
            CompanyId: company, LedgerBookId: bookId, TenantId: tenant), ct);
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)), books, policies, taxLotStore: journal);
        var referenceQuery = Substitute.For<ISecurityMasterQueryService>();
        referenceQuery.GetRecordedByIdAsOfAsync(securityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(securityId, security.AssetClass, security.Status, security.DisplayName,
                security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
                security.Version, security.EffectiveFrom, security.EffectiveTo));
        foreach (var reference in new[] { security }.Concat(successors.Select(target => target.Security)))
            referenceQuery.GetByIdAsync(reference.SecurityId, Arg.Any<CancellationToken>())
                .Returns(new SecurityDetailDto(reference.SecurityId, reference.AssetClass, reference.Status, reference.DisplayName,
                    reference.Currency, reference.CommonTerms, reference.AssetSpecificTerms, reference.Identifiers, reference.Aliases,
                    reference.Version, reference.EffectiveFrom, reference.EffectiveTo));
        var spine = new AssetAccountingEventSpineService(assets, assets, referenceQuery, books, policies, configuration, candidateBuilder, journal);
        await new CanonicalLotCorporateActionService(journal, securities, assets, spine)
            .DraftAsync(mapped, instruction, assetPath, preparer, now, "Reviewed advance refunding", ct);
        var eventId = mapped.Event.EconomicEvent.EventId;
        var drafted = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        drafted.DraftedLotMutation!.Intent.Should().Be(AssetLotMutationIntentDto.CorporateAction);
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(drafted.DraftedCandidateResult!.PostingCommand!.LotCorporateAction),
            JsonSerializer.SerializeToElement(instruction)).Should().BeTrue();
        (await journal.GetByPeriodAsync(period.PeriodId, ct)).Should().ContainSingle("drafting must not post another journal")
            .Which.Entry.JournalEntryId.Should().Be(amortizationId);
        var approvalId = Guid.NewGuid().ToString("D");
        var approvedAt = DateTimeOffset.UtcNow;
        var approvalEvidence = new RetainedEvidenceIdentityDto("refunding-approval", "document://corporate-action/refunding-approval", new string('e', 64),
            "approvals", approvalId, RetainedEvidenceIdentityValidator.AcceptedReviewStatus, approver, approvedAt, date, 1,
            approvedAt, approver, AssetAccountingEvidenceSubjects.PostingApproval,
            AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(eventId, 1, fund, bookId, period.PeriodId,
                AccountingBasisKindDto.Gaap, approvalId, drafted.DraftedCandidateFingerprint!, tenant, company));
        var request = new PostPostingRuleJournalCandidateRequestDto(drafted.DraftedCandidate!, approver, approvalId,
            ApprovalNotes: "Approve exact retained predecessor and refunded/unrefunded successor bases.", TenantId: tenant, CompanyId: company)
        { ApprovalEvidence = [approvalEvidence] };
        var posted = await new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets)
            .PostCandidateAsync(request, ct);
        posted.WasReplay.Should().BeFalse();
        posted.TaxLotMutationBatchId.Should().NotBeNull();
        var retained = (await assets.GetLatestAsync(eventId, 1, ct))!.Projection;
        retained.Stages.Select(stage => stage.Stage).Should().Equal(AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected, AssetAccountingLifecycleStageDto.Drafted,
            AssetAccountingLifecycleStageDto.Approved, AssetAccountingLifecycleStageDto.Posted);
        AssetAccountingEventSpineValidator.Validate(retained).Should().BeEmpty();
        var impact = retained.PostedJournalImpact!;
        var debitIndex = impact.Lines.ToList().FindIndex(line => line.Debit > 0m);
        var debit = impact.Lines[debitIndex];
        foreach (var invalidDimensions in new[]
        {
            debit.Dimensions! with { InstrumentId = securityId, PositionId = positionId },
            debit.Dimensions! with { InstrumentId = Guid.NewGuid() },
            debit.Dimensions! with { PositionId = Guid.NewGuid() },
            debit.Dimensions! with { BookId = Guid.NewGuid().ToString("D") },
            debit.Dimensions! with { EntityId = "another-company" }
        })
        {
            var changedLines = impact.Lines.ToArray();
            changedLines[debitIndex] = debit with { Dimensions = invalidDimensions };
            AssetAccountingEventSpineValidator.Validate(retained with
            { PostedJournalImpact = impact with { Lines = changedLines } }).Should().NotBeEmpty();
        }
        var restartedSecurities = new PostgresSecurityMasterStore(securityOptions);
        PostgresAssetOperationsProjectionStore? restartedAssets = null;
        var restartedJournal = new PostgresLedgerJournalStore(ledgerOptions, backfillSecurityMaster: () => restartedSecurities,
            backfillPositions: () => restartedAssets!);
        restartedAssets = new PostgresAssetOperationsProjectionStore(assetOptions, restartedJournal);
        var batch = (await restartedJournal.GetAtomicTaxLotPostingAsync(posted.TaxLotMutationBatchId!.Value, ct))!;
        var close = batch.Mutations.Single(row => row.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose);
        var successorRows = batch.Mutations.Where(row => row.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor).ToArray();
        var proof = CanonicalCorporateActionLotProjection.Project(batch.Journal.Entry, account,
            close.LotBefore!.ToOpenLot(), close.LotAfter.ToOpenLot(), successorRows.Select(row => row.LotAfter.ToOpenLot()).ToArray());
        proof.PredecessorAfter.OpenQuantity.Should().Be(0m);
        proof.Successors.Select(value => value.SecurityId).Should().Equal(successors.Select(value => value.Security.SecurityId));
        proof.Successors.Select(value => value.BookPositionId).Should().Equal(successors.Select(value => value.BookPositionId));
        var shares = new[] { refundedPercent / 100m, 1m - refundedPercent / 100m };
        proof.Successors.Select(value => value.OpenQuantity).Should().Equal(shares.Select(share => 6_000m * share));
        proof.Successors.Select(value => value.Acquisition.TransactionCostBasis).Should().Equal(shares.Select(share => 6_600m * share));
        proof.Successors.Select(value => value.Acquisition.FunctionalCostBasis).Should().Equal(shares.Select(share => 6_600m * share));
        proof.Successors.Select(value => value.OpenTransactionCostBasis).Should().Equal(shares.Select(share => 6_300m * share));
        proof.Successors.Select(value => value.OpenFunctionalCostBasis).Should().Equal(shares.Select(share => 6_300m * share));
        proof.Successors.Sum(value => value.Acquisition.FunctionalCostBasis).Should().Be(6_600m);
        proof.Successors.Sum(value => value.OpenFunctionalCostBasis).Should().Be(proof.PredecessorBefore.OpenFunctionalCostBasis);
        proof.Successors.Sum(value => value.OpenTransactionCostBasis).Should().Be(proof.PredecessorBefore.OpenTransactionCostBasis);
        proof.Successors.Should().OnlyContain(value => value.AcquiredDate == acquired
            && value.Acquisition.HoldingPeriodStartDate == holdingPeriod && value.Acquisition.QuantityBasis == LotQuantityBasis.Face
            && value.Acquisition.AcquisitionFxRateToFunctional == 1m);
        proof.Successors[0].Acquisition.CorporateActionLineage!.Role.Should().Be(CorporateActionSuccessorRoleDto.Refunded);
        proof.Successors[0].Acquisition.CorporateActionLineage!.ReportingTags.Should().Equal("ScheduleD");
        proof.Successors[1].Acquisition.CorporateActionLineage!.Role.Should().Be(CorporateActionSuccessorRoleDto.Unrefunded);
        proof.Successors[1].Acquisition.CorporateActionLineage!.ReportingTags.Should().BeEmpty();
        batch.Journal.Entry.Lines.Should().HaveCount(3).And.OnlyContain(line => line.Account == account);
        batch.Journal.Entry.Lines.Sum(line => line.Debit - line.Credit).Should().Be(0m);
        foreach (var successor in proof.Successors)
            (await restartedJournal.GetTaxLotsByIdsAsync(bookId, [successor.TaxLotRecordId], ct)).Single().ToOpenLot()
                .Should().BeEquivalentTo(successor);
        var replay = await new AccountingPostingCandidatePostService(candidateBuilder, restartedJournal, assetAccountingEventStore: restartedAssets)
            .PostCandidateAsync(request, ct);
        replay.WasReplay.Should().BeTrue();
        replay.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        (await restartedJournal.GetByPeriodAsync(period.PeriodId, ct)).Should().HaveCount(2);
        (await restartedJournal.ListOpenTaxLotsAsync(bookId, account, ct)).Should().HaveCount(2);
    }
}
