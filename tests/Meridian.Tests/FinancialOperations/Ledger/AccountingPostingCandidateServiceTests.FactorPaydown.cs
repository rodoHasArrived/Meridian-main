using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.FinancialOperations.Ledger;

/// <summary>
/// The MBS factor paydown as a Rules Studio candidate under Asset Accounting Event Spine authority:
/// the candidate carries the governed per-lot FactorPaydown instruction, the server recomputes held
/// face, principal and the per-lot allocation from the lots of record, and the rule's lot-mutation
/// components (principal, basis relieved, realized gain/loss) produce the two- or three-line journal.
/// </summary>
public sealed partial class AccountingPostingCandidateServiceTests
{
    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_RecalculatesPersistedProjectionBeforeDrafting()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.BuildAsync();

        result.HasBlockingIssues.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        result.PostingCommand.Should().NotBeNull();
        result.PostingCommand!.EconomicEvent!.EventId.Should().Be(harness.Request.SourceEventId!.Value);
        result.PostingCommand.LotFactorPaydown.Should().BeEquivalentTo(harness.Instruction);
        result.TotalDebits.Should().Be(1_750m);
        result.TotalCredits.Should().Be(1_750m);
        result.GeneratedPostingLines.Should().HaveCount(2, "a par lot relieves exactly the principal, so no realized line is generated");
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_PremiumLotRelievesProRataBasisAndBooksTheLoss()
    {
        // The worked lot: 1,000,000 of par booked at 0.95 at 101.5 (basis 964,250) pays 0.95 -> 0.90.
        // Principal is 50,000, basis relieved is 964,250 x 0.90 / 0.95 = 50,750, and the 750
        // difference is a realized loss on the premium.
        var harness = await CreateAuthoritativeFactorHarnessAsync(
            [BuildFaceLotOfRecord(AuthoritativeFactorHarness.LedgerBookId, AuthoritativeFactorHarness.SecurityId,
                AuthoritativeFactorHarness.PositionId, originalFace: 950_000m, bookedFactor: 0.95m, parBasis: 100m, pricePercentOfPar: 101.5m)],
            priorFactor: 0.95m,
            newFactor: 0.90m);

        var result = await harness.BuildWriteAsync();

        result.Candidate.HasBlockingIssues.Should().BeFalse(string.Join("; ", result.Candidate.Issues.Select(issue => issue.Message)));
        result.Candidate.TotalDebits.Should().Be(50_750m);
        result.Candidate.TotalCredits.Should().Be(50_750m);
        var write = result.Write!;
        write.Entry.Lines.Should().HaveCount(3);
        write.Entry.Lines.Single(line => line.Account == LedgerAccounts.Cash).Debit.Should().Be(50_000m);
        var carrying = write.Entry.Lines.Single(line => line.Credit > 0m);
        carrying.Account.Should().Be(AuthoritativeFactorHarness.CarryingAccount, "the carrying credit adopts the lots' exact account");
        carrying.Credit.Should().Be(50_750m);
        write.Entry.Lines.Single(line => line.Account == LedgerAccounts.RealizedLoss).Debit.Should().Be(750m);
        write.Entry.Lines.Should().NotContain(line => line.Account == LedgerAccounts.RealizedGain);
        var command = write.PostingCommand!;
        command.LotFactorPaydown.Should().BeEquivalentTo(harness.Instruction);
        command.LotFactorPaydown!.Lots.Single().FunctionalBasisAfter.Should().Be(913_500m);
        command.ExpectedVersion.Should().Be(harness.Authority.ExpectedPeriodVersion);
        command.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Pending);
        write.Entry.Metadata.Tags!["securityMasterProvenance"].Should().Contain("currency:USD");
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_TamperedAmountFailsClosedAgainstServerCalculation()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.BuildAsync(harness.Request with { EventAmount = 1m });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.factor-paydown-amount-mismatch" && issue.BlocksCandidate);
        result.DryRunResult.GeneratedPostingLines.Should().OnlyContain(line => Math.Abs(line.Amount) == 1_750m,
            "the rule's lines come from the governed instruction's components, never from the submitted amount");
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_TamperedAllocationFailsClosedAgainstTheLotsOfRecord()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        var lot = harness.Instruction.Lots.Single();
        var shifted = harness.Instruction with
        {
            Lots = [lot with { Principal = lot.Principal + 0.01m, FunctionalBasisAfter = lot.FunctionalBasisAfter - 0.01m }]
        };

        var result = await harness.BuildAsync(harness.Request with { AssetLotMutation = AuthoritativeFactorHarness.LotMutation(shifted) });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.factor-paydown-allocation-mismatch" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_WithoutTheLotMutationIsNotPostingAuthority()
    {
        // A bare factor journal would move cash against the carrying account and leave the lots of
        // record at their old factor and full basis.
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.BuildAsync(harness.Request with { AssetLotMutation = null });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.factor-paydown-lot-mutation-required" && issue.BlocksCandidate);
    }

    [Theory]
    [InlineData(AssetLotMutationIntentDto.None)]
    [InlineData(AssetLotMutationIntentDto.CapitalReturn)]
    [InlineData(AssetLotMutationIntentDto.CorporateAction)]
    public async Task BuildCandidateAsync_MbsFactorPaydown_PayloadUnderAnotherIntentNeverFeedsTheRuleComponents(
        AssetLotMutationIntentDto intent)
    {
        // The paydown's components reach the rule's formulas only under the FactorPaydown intent, like
        // every other paydown binding; a payload under another intent resolves no component line.
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        var mislabelled = AuthoritativeFactorHarness.LotMutation(harness.Instruction) with { Intent = intent };

        var result = await harness.BuildAsync(harness.Request with { AssetLotMutation = mislabelled });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.factor-paydown-lot-mutation-required" && issue.BlocksCandidate);
        result.DryRunResult.GeneratedPostingLines.Should().BeEmpty(
            "client-supplied components under another intent never resolve the rule's lot-mutation lines");
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_GenericCandidateWithoutSpineAuthorityIsRefused()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.Service.BuildCandidateAsync(harness.Request);

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.asset-authority-required" && issue.BlocksCandidate);
    }

    [Theory]
    [InlineData("above")]
    [InlineData("below")]
    public async Task BuildCandidateAsync_MbsFactorPaydown_LotNotAtThePositionsPriorFactorFailsClosed(string where)
    {
        // The position's observation pays 0.98 -> 0.9625. A lot still at its booked factor of 1 was
        // never brought to 0.98, and a lot already paid down to 0.97 has absorbed a later observation;
        // neither can take this paydown, so the scope refuses rather than restating either.
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        var scope = harness.TaxLots.OpenLots[0];
        harness.TaxLots.OpenLots[0] = where == "above"
            ? BuildFaceLotOfRecord(scope.LedgerBookId, scope.SecurityId, scope.BookPositionId,
                originalFace: 100_000m, bookedFactor: 1m, parBasis: 100m)
            : BuildFaceLotOfRecord(scope.LedgerBookId, scope.SecurityId, scope.BookPositionId,
                originalFace: 98_000m, bookedFactor: 0.98m, parBasis: 100m, currentFactor: 0.97m);

        var result = await harness.BuildAsync();

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-lot-factor-stale" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_PartiallyRelievedLotContributesOnlyItsOpenFace()
    {
        // Half the lot has already been relieved, so only half its face is still held and only half
        // the principal pays down. A held face taken from the position projection could not see this.
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        await harness.RebuildAsync([BuildFaceLotOfRecord(AuthoritativeFactorHarness.LedgerBookId, AuthoritativeFactorHarness.SecurityId,
            AuthoritativeFactorHarness.PositionId, originalFace: 98_000m, bookedFactor: 0.98m, parBasis: 100m, openQuantity: 490m)]);

        var result = await harness.BuildAsync();

        result.HasBlockingIssues.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        result.TotalDebits.Should().Be(875m);
        harness.Instruction.Lots.Single().FunctionalBasisAfter.Should().Be(48_125m, "49,000 of open basis x 0.9625 / 0.98");
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_IgnoresLotsAcquiredAfterTheEffectiveDate()
    {
        // Pool factors publish after the fact, so a paydown is always posted with a lag. A second
        // piece of the same pool bought between the effective date and the posting did not hold the
        // position over the paydown period; counting it would double the principal. The factor half
        // of the projection is selected as-of the effective date, and the face half must be too.
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        var scope = harness.TaxLots.OpenLots[0];
        harness.TaxLots.OpenLots.Add(BuildFaceLotOfRecord(
            scope.LedgerBookId,
            scope.SecurityId,
            scope.BookPositionId,
            originalFace: 96_250m,
            bookedFactor: 0.9625m,
            parBasis: 100m,
            acquiredDate: new DateOnly(2026, 6, 10),
            lotId: "lot-fnpool1-2026-06"));

        var result = await harness.BuildAsync();

        result.HasBlockingIssues.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        result.TotalDebits.Should().Be(1_750m, "the later lot did not hold the position over the paydown period");
        result.PostingCommand!.LotFactorPaydown!.Lots.Should().ContainSingle();
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_LotWithoutRecordedFaceTermsFailsClosed()
    {
        // A lot booked before the par conventions were retained states nothing rather than
        // defaulting to a price-per-100 quote at factor 1. The candidate must refuse to guess.
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        var scope = harness.TaxLots.OpenLots[0];
        harness.TaxLots.OpenLots[0] = BuildFaceLotOfRecord(
            scope.LedgerBookId,
            scope.SecurityId,
            scope.BookPositionId,
            originalFace: 98_000m,
            bookedFactor: 0.98m,
            parBasis: 100m,
            withFaceTerms: false);

        var result = await harness.BuildAsync();

        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-lot-face-terms-missing" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_PositionWithNoOpenLotFailsClosed()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        harness.TaxLots.OpenLots.Clear();

        var result = await harness.BuildAsync();

        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-lot-of-record-missing" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_WithoutALotOfRecordResolverFailsClosed()
    {
        // The lot of record is a required resolver for typed factor-paydown candidates; a
        // composition that omits it must refuse the candidate rather than fall back to the caller.
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        var withoutLots = new AccountingPostingCandidateService(
            harness.Configuration,
            harness.Drafts,
            harness.LedgerBooks,
            harness.Policies,
            harness.AssetOperations,
            harness.Projector);

        var result = await withoutLots.BuildAuthoritativeCandidateWriteAsync(harness.Request, harness.Authority);

        result.Candidate.HasBlockingIssues.Should().BeTrue();
        result.Candidate.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-projection-resolver-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_MissingHolderRoleFailsClosed()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        harness.AssetOperations.Detail = harness.AssetOperations.Detail! with { InstrumentRoles = [] };

        var result = await harness.BuildAsync();

        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-holder-role-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_OmittedLineageCannotBypassAuthoritativeRecalculation()
    {
        // Under spine authority the lineage gate fires before the typed recalculation: a candidate
        // without complete projection lineage is refused before any dry run.
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.BuildAsync(harness.Request with
        {
            EventAmount = 1m,
            ProjectionLineage = null
        });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.asset-authority-lineage-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_ClientSelectedModelCannotBypassAuthoritativeRecalculation()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.BuildAsync(harness.Request with
        {
            EventAmount = 1m,
            ProjectionLineage = harness.Request.ProjectionLineage! with { ModelKey = "client-selected-model" }
        });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-factor-lineage-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_MissingRulePackReferenceFailsClosed()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.BuildAsync(harness.Request with
        {
            RulePackReference = null
        });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.asset-authority-rule-pack-mismatch" && issue.BlocksCandidate);
    }

    private static Task<AuthoritativeFactorHarness> CreateAuthoritativeFactorHarnessAsync(
        IReadOnlyList<LedgerTaxLotRecord>? lots = null,
        decimal priorFactor = 0.98m,
        decimal newFactor = 0.9625m)
        => AuthoritativeFactorHarness.CreateAsync(lots, priorFactor, newFactor);

    /// <summary>
    /// A factor-paydown candidate under spine authority: Rules Studio seeded with the four-account
    /// paydown rule keyed on lot-mutation components, the policy rule pack, Asset Operations
    /// publishing the position's factor state and lineage from the same projector run the
    /// instruction was built from, and the lots of record the paydown applies to. The default pool is
    /// 100,000 of par booked at 0.98 (98,000 of face) at par, paying 0.98 -> 0.9625: 1,750 of
    /// principal relieving exactly 1,750 of basis.
    /// </summary>
    private sealed class AuthoritativeFactorHarness
    {
        public static readonly Guid LedgerBookId = Guid.Parse("c1000000-0000-4000-8000-000000000001");
        public static readonly Guid PeriodId = Guid.Parse("c1000000-0000-4000-8000-000000000002");
        public static readonly Guid OwnerNodeId = Guid.Parse("c1000000-0000-4000-8000-000000000003");
        public static readonly Guid SecurityId = Guid.Parse("c1000000-0000-4000-8000-000000000004");
        public static readonly Guid RoleId = Guid.Parse("c1000000-0000-4000-8000-000000000005");
        public static readonly Guid PositionId = Guid.Parse("c1000000-0000-4000-8000-000000000006");
        public static readonly DateOnly EffectiveDate = new(2026, 5, 31);
        public static readonly LedgerAccount CarryingAccount = new("Investments", LedgerAccountType.Asset, "FNPOOL1");
        public const string RulePackId = "gaap-mbs-rules";
        public const string RuleId = "posting.mbs-factor-paydown";
        private const string PolicyId = "gaap-mbs-v1";
        private const string Evidence = "evidence://factor/row";
        private static readonly string EventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction);

        private AuthoritativeFactorHarness(
            AccountingConfigurationService configuration,
            AccountingPolicyService policies,
            StaticLedgerBookService ledgerBooks,
            LedgerDimensionSetDto dimensions,
            AccountingBookContextDto bookContext)
        {
            Configuration = configuration;
            Policies = policies;
            LedgerBooks = ledgerBooks;
            Dimensions = dimensions;
            BookContext = bookContext;
            Drafts = new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies));
            Service = new AccountingPostingCandidateService(configuration, Drafts, ledgerBooks, policies, AssetOperations, Projector, TaxLots);
        }

        public AccountingPostingCandidateService Service { get; }
        public PostingRuleJournalCandidateRequestDto Request { get; private set; } = null!;
        public AssetAccountingCandidateAuthorityContext Authority { get; private set; } = null!;
        public OpenLotFactorPaydownInstructionDto Instruction { get; private set; } = null!;
        public StaticAssetOperationsQueryService AssetOperations { get; } = new();
        public StaticTaxLotStore TaxLots { get; } = new();
        public AccountingConfigurationService Configuration { get; }
        public AccountingJournalDraftService Drafts { get; }
        public StaticLedgerBookService LedgerBooks { get; }
        public AccountingPolicyService Policies { get; }
        public FactorPaydownProjectionService Projector { get; } = new();
        public LedgerDimensionSetDto Dimensions { get; }
        public AccountingBookContextDto BookContext { get; }
        public decimal PriorFactor { get; private set; }
        public decimal NewFactor { get; private set; }

        public static async Task<AuthoritativeFactorHarness> CreateAsync(
            IReadOnlyList<LedgerTaxLotRecord>? lots, decimal priorFactor, decimal newFactor)
        {
            var dimensions = new LedgerDimensionSetDto(
                "fund-alpha",
                "entity-master",
                InstrumentId: SecurityId,
                BookId: LedgerBookId.ToString("D"))
            {
                PositionId = PositionId
            };
            var bookContext = new AccountingBookContextDto(
                LedgerBookId,
                "fund-alpha",
                OwnerNodeId,
                FundStructureNodeKindDto.Fund,
                "Fund Alpha GAAP",
                "USD",
                AccountingBasisKindDto.Gaap,
                PolicyId,
                "v1",
                PeriodId,
                dimensions);
            var configuration = new AccountingConfigurationService(
                new InMemoryAccountingConfigurationStore(),
                new InMemoryAccountingActionAuditStore());
            await SeedConfigurationAsync(configuration);
            var policies = new AccountingPolicyService();
            await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(
                AccountingBasisKindDto.Gaap,
                PolicyId,
                "v1",
                "GAAP MBS factor treatment",
                new DateOnly(2026, 1, 1),
                RulePack: new AccountingPolicyRulePackDto(
                    RulePackId,
                    "v1",
                    [
                        new AccountingPolicyRuleDto(
                            RuleId,
                            AccountingTreatmentKindDto.Amortization,
                            "v1",
                            EventType,
                            RequiresEvidence: true,
                            RequiresApproval: true,
                            AllowsAutoPosting: false)
                    ])));
            var ledgerBook = new LedgerBookDto(
                LedgerBookId,
                "fund-alpha",
                OwnerNodeId,
                FundStructureNodeKindDto.Fund,
                "Fund Alpha GAAP",
                "USD",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                AccountingBasis: AccountingBasisKindDto.Gaap,
                AccountingPolicyId: PolicyId,
                AccountingPolicyVersion: "v1");
            var period = new LedgerPeriodDto(
                PeriodId,
                LedgerBookId,
                2026,
                5,
                "May 2026",
                new DateOnly(2026, 5, 1),
                EffectiveDate,
                LedgerPeriodStatusDto.Open,
                DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                null,
                1,
                AccountingBasisKindDto.Gaap,
                PolicyId,
                "v1");
            var harness = new AuthoritativeFactorHarness(configuration, policies, new StaticLedgerBookService(ledgerBook, period), dimensions, bookContext);
            await harness.RebuildAsync(lots ?? [DefaultLot()], priorFactor, newFactor);
            return harness;
        }

        public static LedgerTaxLotRecord DefaultLot()
            => BuildFaceLotOfRecord(LedgerBookId, SecurityId, PositionId, originalFace: 98_000m, bookedFactor: 0.98m, parBasis: 100m);

        public static AssetLotMutationInstructionDto LotMutation(OpenLotFactorPaydownInstructionDto instruction)
            => new(AssetLotMutationIntentDto.FactorPaydown, AssetAccountId: CarryingAccount.Name, FactorPaydown: instruction);

        /// <summary>
        /// Replaces the lots of record and rebuilds the instruction, the published position state and
        /// the candidate request from them, exactly as a preparation service would before drafting.
        /// </summary>
        public async Task RebuildAsync(IReadOnlyList<LedgerTaxLotRecord> lots, decimal? priorFactor = null, decimal? newFactor = null)
        {
            PriorFactor = priorFactor ?? PriorFactor;
            NewFactor = newFactor ?? NewFactor;
            TaxLots.OpenLots.Clear();
            TaxLots.OpenLots.AddRange(lots);
            var open = (await TaxLots.ListOpenTaxLotsByAssetScopeAsync(LedgerBookId, SecurityId, PositionId, EffectiveDate))
                .Select(static lot => lot.ToOpenLot())
                .ToArray();
            Instruction = FactorPaydownTestData.Build(open, PriorFactor, NewFactor, securityVersion: 1, positionVersion: 4,
                periodId: PeriodId, periodVersion: 1, fund: "fund-alpha", date: EffectiveDate,
                accountingBasis: AccountingBasisKindDto.Gaap, tenantId: null, companyId: null, dimensions: Dimensions);
            var projection = FactorPaydownTestData.Projection(open, PriorFactor, NewFactor, positionVersion: 4, EffectiveDate);
            var role = new InstrumentRoleDto(
                RoleId,
                SecurityId,
                "fund-alpha",
                "Fund",
                InstrumentRoleKinds.Holder,
                InstrumentAccountingSides.Debit,
                InstrumentEconomicSides.Asset,
                new DateOnly(2026, 1, 1),
                Version: 2,
                EvidenceLinks: [Evidence]);
            var position = new BookPositionDto(
                PositionId,
                SecurityId,
                RoleId,
                BookContext,
                BookPositionSides.Long,
                "Active",
                new DateOnly(2026, 1, 1),
                Version: 4,
                CurrentEconomicState: projection.EconomicState,
                ProjectionLineage: projection.Lineage,
                EvidenceLinks: [Evidence]);
            var subject = new AssetOperationSubjectDto(
                SecurityId,
                "MortgageBackedSecurity",
                "Agency MBS Pool",
                "FNPOOL1",
                ["FactorProcessing"]);
            var readiness = new AssetOperationsReadinessDto(
                SecurityId,
                "Ready",
                [],
                [],
                [],
                [],
                DateTimeOffset.Parse("2026-05-31T01:00:00Z"),
                "AssetOperations",
                PositionId.ToString("D"));
            AssetOperations.Detail = new AssetOperationsDetailDto(subject, [], [], [], [], [], [], [], [], readiness, [])
            {
                InstrumentRoles = [role],
                BookPositions = [position],
                PositionEconomicStates = [projection.EconomicState!],
                ProjectionLineages = [projection.Lineage!]
            };
            Request = new PostingRuleJournalCandidateRequestDto(
                "fund-alpha",
                EventType,
                Instruction.PrincipalPaydown,
                "USD",
                EffectiveDate,
                "preparer@meridian.local",
                LedgerBookId,
                PeriodId,
                DateTimeOffset.Parse("2026-05-31T12:00:00Z"),
                "MBS factor principal paydown",
                AccountingBasisKindDto.Gaap,
                LedgerBookId,
                Dimensions,
                InstrumentSymbol: "FNPOOL1",
                SourceEventId: Instruction.EconomicEvent.EventId,
                PolicyId: PolicyId,
                TreatmentKind: AccountingTreatmentKindDto.Amortization,
                EvidenceLinks: Instruction.EconomicEvent.EvidenceLinks)
            {
                BookContext = BookContext,
                BookPositionId = PositionId,
                EconomicEvent = Instruction.EconomicEvent,
                ProjectionLineage = Instruction.Lineage,
                RulePackReference = new AccountingRulePackReferenceDto(RulePackId, "v1", RuleId, "v1"),
                AssetLotMutation = LotMutation(Instruction),
                RetainedEvidence = Instruction.EconomicEvent.RetainedEvidence,
                ExpectedPeriodVersion = 1
            };
            Authority = new AssetAccountingCandidateAuthorityContext(
                Instruction.EconomicEvent.EventId, 1, 2, 3, new string('a', 64), AssetAccountingEventKindDto.CorporateAction,
                SecurityId, PositionId, 4, LedgerBookId, PeriodId, 1, RulePackId, "v1", SecurityVersion: 1);
        }

        public async Task<PostingRuleJournalCandidateResultDto> BuildAsync(PostingRuleJournalCandidateRequestDto? request = null)
            => (await BuildWriteAsync(request)).Candidate;

        public Task<AccountingPostingCandidateWriteResult> BuildWriteAsync(PostingRuleJournalCandidateRequestDto? request = null)
            => Service.BuildAuthoritativeCandidateWriteAsync(request ?? Request, Authority);

        /// <summary>
        /// The four paydown accounts and the rule whose lines resolve to the governed instruction's
        /// lot-mutation components: cash takes the principal, the carrying account gives up the basis
        /// relieved, and the realized gain or loss line carries the difference (a zero component
        /// generates no line). Line dimensions inherit the candidate's dimensions.
        /// </summary>
        private static async Task SeedConfigurationAsync(AccountingConfigurationService configuration)
        {
            foreach (var (nodeId, path, name, type) in new[]
            {
                ("cash", "assets/cash", LedgerAccounts.Cash.Name, "Asset"),
                ("investments", "assets/investments", CarryingAccount.Name, "Asset"),
                ("realized-gain", "income/realized-gain", LedgerAccounts.RealizedGain.Name, "Revenue"),
                ("realized-loss", "expense/realized-loss", LedgerAccounts.RealizedLoss.Name, "Expense")
            })
            {
                await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
                    "fund-alpha",
                    new ChartOfAccountsNodeDto(nodeId, path, name, type),
                    "controller@meridian.local",
                    LedgerBookId: LedgerBookId));
            }

            await configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
                "fund-alpha",
                new PostingRuleDto(
                    RuleId,
                    "MBS factor principal paydown",
                    EventType,
                    TemplateId: "generated",
                    RuleVersion: "v1",
                    EffectiveFrom: new DateOnly(2026, 1, 1),
                    Priority: 100,
                    Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                    Formulas:
                    [
                        new AccountingRuleFormulaDto(OpenLotFactorPaydown.PrincipalComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m),
                        new AccountingRuleFormulaDto(OpenLotFactorPaydown.BasisRelievedComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m),
                        new AccountingRuleFormulaDto(OpenLotFactorPaydown.RealizedGainComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m),
                        new AccountingRuleFormulaDto(OpenLotFactorPaydown.RealizedLossComponent, AccountingRuleFormulaKindDto.LotMutationComponent, 0m)
                    ],
                    GeneratedPostings:
                    [
                        new GeneratedPostingLineDto("cash", "assets/cash", AccountingTemplateLineSideDto.Debit,
                            OpenLotFactorPaydown.PrincipalComponent, 0m, "USD", Description: "Principal received"),
                        new GeneratedPostingLineDto("investments", "assets/investments", AccountingTemplateLineSideDto.Credit,
                            OpenLotFactorPaydown.BasisRelievedComponent, 0m, "USD", Description: "Basis relieved pro rata"),
                        new GeneratedPostingLineDto("realized-gain", "income/realized-gain", AccountingTemplateLineSideDto.Credit,
                            OpenLotFactorPaydown.RealizedGainComponent, 0m, "USD", Description: "Realized gain on paydown"),
                        new GeneratedPostingLineDto("realized-loss", "expense/realized-loss", AccountingTemplateLineSideDto.Debit,
                            OpenLotFactorPaydown.RealizedLossComponent, 0m, "USD", Description: "Realized loss on paydown")
                    ]),
                "controller@meridian.local",
                LedgerBookId: LedgerBookId));
        }
    }
}
