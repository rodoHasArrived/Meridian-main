using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [LedgerDatabaseFact]
    public Task CanonicalAmortization_Actual360ConstantYield_PremiumUsesCalendarCoupons()
        => AssertGovernedAmortizationAsync(premium: true, constantYield: true, convention: "Actual/360");

    [LedgerDatabaseFact]
    public Task CanonicalAmortization_Actual365ConstantYield_DiscountUsesCalendarCouponsAcrossLeapYear()
        => AssertGovernedAmortizationAsync(premium: false, constantYield: true, convention: "Actual/365F");

    [LedgerDatabaseFact]
    public Task CanonicalAmortization_UnversionedRetainedInstruction_RequiresFreshPreviewWithoutMutation()
        => AssertGovernedAmortizationAsync(premium: true, constantYield: true, convention: "Actual/365", legacy: true);

    private static async Task AssertGovernedAmortizationAsync(
        bool premium, bool constantYield = false, string convention = "30/360", bool legacy = false)
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Posting timestamps use the live clock. Anchor coupon dates on the first of the
        // current month to keep a real open posting period and avoid day-31 schedule drift.
        var date = legacy ? new DateOnly(2025, 7, 1) : new DateOnly(today.Year, today.Month, 1);
        var halfLifeYears = constantYield && !premium ? 2 : 1;
        var acquired = legacy ? new DateOnly(2025, 1, 1) : date.AddYears(-halfLifeYears);
        var maturity = legacy ? new DateOnly(2026, 1, 1) : date.AddYears(halfLifeYears);
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
        using var provider = ComposePrimaryHost(ledgerOptions, securityOptions, assetOptions);
        var securities = provider.GetRequiredService<ISecurityMasterStore>();
        var journal = provider.GetRequiredService<PostgresLedgerJournalStore>();
        var assets = provider.GetRequiredService<PostgresAssetOperationsProjectionStore>();
        provider.GetRequiredService<LedgerJournalStoreOptions>().RequireGovernedPostingCommand.Should().BeTrue();
        provider.GetRequiredService<LedgerJournalStoreOptions>().RequireExpectedVersion.Should().BeTrue();
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
                dayCountConvention = convention,
                couponRate = legacy ? 10m : constantYield ? (premium ? 5m : 0m) : 10m,
                paymentFrequency = legacy ? "semiannual" : "annual",
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
        var price = premium ? 110m : constantYield ? 40.96m : 90m;
        var account = new LedgerAccount("Investments", LedgerAccountType.Asset);
        var lot = await journal.SaveTaxLotAsync(new LedgerTaxLotRecord(lotId, bookId, account, "amortization-owned-lot", acquired,
            100m, 100m, price, "USD", now, now, EvidenceRef: acquisitionEvidence.EvidenceId, Version: 1,
            SecurityId: securityId, BookPositionId: positionId, OriginalFace: 10_000m, BookedFactor: 1m, ParBasis: 100m,
            Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, price * 100m, price * 100m,
                acquired, new FaceValueAcquisitionTermsDto(100m, 1m,
                    constantYield ? BondAmortizationMethod.ConstantYield : BondAmortizationMethod.StraightLine,
                    constantYield ? (premium ? 0m : 0.25m) : null), [acquisitionEvidence])), ct);
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
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "canonical-lot-amortization",
            legacy ? "canonical-lot-amortization-v1" : OpenLotAmortization.ModelVersion,
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
        var preview = await provider.GetRequiredService<CanonicalLotAmortizationService>()
            .PreviewAsync(bookId, lotId, date, securityEvidence, ct);
        preview.Instruction.CalculationVersion.Should().Be(OpenLotAmortization.ModelVersion);
        if (legacy)
        {
            // Rehydrate the exact pre-versioning instruction shape. Historical calculations
            // remain available for receipt replay, but unposted drafts require a fresh preview.
            var payload = JsonSerializer.SerializeToNode(preview.Instruction)!.AsObject();
            payload.Remove(nameof(OpenLotAmortizationInstructionDto.CalculationVersion));
            var retainedInstruction = JsonSerializer.Deserialize<OpenLotAmortizationInstructionDto>(payload.ToJsonString())!;
            retainedInstruction.CalculationVersion.Should().BeNull();
            preview = new(retainedInstruction, OpenLotAmortization.Project(retainedInstruction));
        }
        (await journal.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty();
        (await journal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single().Should().BeEquivalentTo(lot);
        var movement = preview.Projection.FunctionalMovement;
        // Independently known cash flows: premium 11,000 less a 500 coupon at zero yield;
        // discount 4,096 grows at 25% with no coupon to 6,400 after two of four years.
        var expectedAmount = legacy ? decimal.Round(500m * 362m / 365m, 12, MidpointRounding.ToEven)
            : constantYield && !premium ? 2_304m : 500m;
        Math.Abs(movement).Should().Be(expectedAmount);
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
        var postingService = new AccountingPostingCandidatePostService(candidateBuilder, journal, assetAccountingEventStore: assets);
        var unapproved = () => postingService.PostCandidateAsync(request with { ApprovalEvidence = [] }, ct);
        await unapproved.Should().ThrowAsync<InvalidOperationException>();
        var selfApproved = () => postingService.PostCandidateAsync(request with { Actor = preparer }, ct);
        await selfApproved.Should().ThrowAsync<InvalidOperationException>().WithMessage("*independent*");
        (await journal.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty();
        (await journal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single().Should().BeEquivalentTo(lot);
        (await assets.GetLatestAsync(eventId, 1, ct))!.Projection.SpineVersion.Should().Be(drafted.SpineVersion);
        if (legacy)
        {
            var staleCalculation = () => postingService.PostCandidateAsync(request, ct);
            await staleCalculation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*fresh*preview*");
            (await journal.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty();
            (await journal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single().Should().BeEquivalentTo(lot);
            (await assets.GetLatestAsync(eventId, 1, ct))!.Projection.SpineVersion.Should().Be(drafted.SpineVersion);
            return;
        }
        var posted = await postingService.PostCandidateAsync(request, ct);
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
        reloaded.OpenFunctionalCostBasis.Should().Be(price * 100m + (premium ? -expectedAmount : expectedAmount));
        reloaded.Acquisition.Should().BeEquivalentTo(lot.Acquisition);
        var batch = (await journal.GetAtomicTaxLotPostingAsync(posted.TaxLotMutationBatchId!.Value, ct))!;
        batch.MutationKind.Should().Be(AtomicTaxLotMutationKind.Amortization);
        batch.Journal.Entry.Lines.Where(line => line.Account == account).Sum(line => line.Debit - line.Credit)
            .Should().Be(reloaded.OpenFunctionalCostBasis - lot.ToOpenLot().OpenFunctionalCostBasis);
        using var restartedProvider = ComposePrimaryHost(ledgerOptions, securityOptions, assetOptions);
        var restartedJournal = restartedProvider.GetRequiredService<PostgresLedgerJournalStore>();
        var restartedAssets = restartedProvider.GetRequiredService<PostgresAssetOperationsProjectionStore>();
        var replay = await new AccountingPostingCandidatePostService(candidateBuilder, restartedJournal,
            assetAccountingEventStore: restartedAssets).PostCandidateAsync(request, ct);
        replay.WasReplay.Should().BeTrue();
        replay.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        (await restartedAssets.GetLatestAsync(eventId, 1, ct))!.Projection.SpineVersion.Should().Be(5);
        (await restartedJournal.GetByPeriodAsync(period.PeriodId, ct)).Should().ContainSingle();
        (await restartedJournal.GetTaxLotsByIdsAsync(bookId, [lotId], ct)).Single().Version.Should().Be(lot.Version + 1);
    }

    private static ServiceProvider ComposePrimaryHost(
        LedgerJournalStoreOptions ledger, SecurityMasterOptions securities, AssetOperationsOptions assets)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MERIDIAN_DATABASE_URL"] = ledger.ConnectionString,
            ["MERIDIAN_LEDGER_CONNECTION_STRING"] = ledger.ConnectionString,
            ["MERIDIAN_LEDGER_SCHEMA"] = ledger.SchemaName,
            ["MERIDIAN_SECURITY_MASTER_CONNECTION_STRING"] = securities.ConnectionString,
            ["MERIDIAN_SECURITY_MASTER_SCHEMA"] = securities.Schema,
            ["MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING"] = assets.ConnectionString,
            ["MERIDIAN_ASSET_OPERATIONS_SCHEMA"] = assets.Schema,
            ["MERIDIAN_SECURITY_MASTER_PRELOAD_CACHE"] = "false"
        }).Build();
        var services = new ServiceCollection().AddLogging()
            .DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        var options = CompositionOptions.Minimal with { Configuration = configuration, EnableProcessWideHostedServices = false };
        new StorageFeatureRegistration().Register(services, options);
        new LedgerFeatureRegistration().Register(services, options);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
