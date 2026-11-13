using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("malformed")]
    public void BondIncomeAcquisition_AccountOwnershipCannotBeInferredFromMissingOrConflictingServerSources(string scenario)
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var owner = fixture.Book.FundStructureNodeId;
        var position = fixture.Position with
        {
            BookContext = fixture.Position.BookContext with
            {
                Dimensions = fixture.Position.BookContext.Dimensions! with
                { AccountId = scenario == "ambiguous" ? Guid.NewGuid().ToString("D") : scenario == "malformed" ? "chart-path-is-not-owner" : null }
            }
        };
        var resolve = () => BondIncomeAcquisitionOwnership.ResolveAccount(owner,
            scenario == "missing" ? FundStructureNodeKindDto.Fund : FundStructureNodeKindDto.Account, position);
        resolve.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Assets:Investment")]
    [InlineData("77777777-7777-7777-7777-777777777777")]
    public void BondIncomeAcquisition_AuthoritativeAccountNodeIgnoresCarryingGlMappingAsOwnership(string primaryAccount)
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var position = fixture.Position with { PrimaryAccountId = primaryAccount };
        BondIncomeAcquisitionOwnership.ResolveAccount(fixture.Book.FundStructureNodeId,
            FundStructureNodeKindDto.Account, position).Should().Be(fixture.Book.FundStructureNodeId);
    }

    [Fact]
    public void BondIncomeAcquisition_GuidShapedCarryingGlMappingCannotInventFinancialAccountOwnership()
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var position = fixture.Position with { PrimaryAccountId = Guid.NewGuid().ToString("D") };
        var resolve = () => BondIncomeAcquisitionOwnership.ResolveAccount(fixture.Book.FundStructureNodeId,
            FundStructureNodeKindDto.Fund, position);
        resolve.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task BondIncomeAcquisition_RealCandidateBindsActualBirthOwnerAndLotBeforeReviewFingerprint()
    {
        var (result, write, owner, lotId) = await BuildOwnedBondAcquisitionAsync(adopted: true);

        result.Candidate.HasBlockingIssues.Should().BeFalse(string.Join("; ", result.Candidate.Issues.Select(i => i.Code)));
        result.Spine.DraftedCandidateResult!.GeneratedPostingLines.Should().OnlyContain(line =>
            line.Dimensions!.AccountId == owner.ToString("D") && line.Dimensions.TaxLotId == lotId);
        write.Entry.Lines.Should().OnlyContain(line => line.Account.FinancialAccountId == owner.ToString("D")
            && line.Dimensions!.AccountId == owner.ToString("D")
            && line.Dimensions.BookId == result.Spine.Scope.LedgerBookId.ToString("D")
            && line.Dimensions.InstrumentId == result.Spine.Scope.SecurityId
            && line.Dimensions.PositionId == result.Spine.Scope.BookPositionId
            && line.Dimensions.TaxLotId == lotId);
        result.Spine.DraftedCandidate!.AssetLotMutation!.Acquisition!.LotId.Should().Be(lotId);
        result.Spine.DraftedCandidateFingerprint.Should().Be(
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(result.Spine.DraftedCandidate));
        result.Spine.DraftedCandidateResultFingerprint.Should().Be(
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(result.Spine.DraftedCandidateResult));

        var changed = result.Spine.DraftedCandidateResult with
        {
            GeneratedPostingLines = result.Spine.DraftedCandidateResult.GeneratedPostingLines.Select(line =>
                line with { Dimensions = line.Dimensions! with { TaxLotId = "another-physical-lot" } }).ToArray()
        };
        AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(changed).Should()
            .NotBe(result.Spine.DraftedCandidateResultFingerprint);
    }

    [Fact]
    public async Task BondIncomeAcquisition_LegacyCandidateKeepsUnownedJournalAndMissingLotDimensions()
    {
        // The real builder has no position resolver in this case: the legacy path must not need it.
        var (result, write, _, _) = await BuildOwnedBondAcquisitionAsync(adopted: false);
        result.Candidate.HasBlockingIssues.Should().BeFalse(string.Join("; ", result.Candidate.Issues.Select(i => i.Code)));
        write.Entry.Lines.Should().OnlyContain(line => line.Account.FinancialAccountId == null
            && line.Dimensions!.AccountId == null && line.Dimensions.TaxLotId == null);
        result.Spine.DraftedCandidate!.AssetLotMutation!.Acquisition!.BondIncome.Should().BeNull();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("lot")]
    [InlineData("position")]
    public async Task BondIncomeAcquisition_ConflictingGeneratedBirthScopeCannotBecomeReviewedCandidate(string conflict)
    {
        var build = () => BuildOwnedBondAcquisitionAsync(adopted: true, conflict);
        await build.Should().ThrowAsync<ArgumentException>().WithMessage("*conflicts*authoritative*");
    }

    [Theory]
    [InlineData("100.001")]
    [InlineData("100.0000000001")]
    public async Task BondIncomeAcquisition_ExactTenDecimalAndNativeThreeDecimalBirthFactsAreReviewable(string amount)
    {
        var result = await BuildOwnedBondAcquisitionAsync(adopted: true,
            amount: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
        result.Result.Candidate.HasBlockingIssues.Should().BeFalse();
    }

    [Fact]
    public async Task BondIncomeAcquisition_UnrepresentableBirthFactsCannotBeSilentlyRoundedBeforeReview()
    {
        var build = () => BuildOwnedBondAcquisitionAsync(adopted: true, amount: 100.00000000001m);
        await build.Should().ThrowAsync<ArgumentException>().WithMessage("*ten-decimal journal boundary*");
    }

    private static async Task<(AssetAccountingPostingCandidateDto Result, LedgerJournalEntryWrite Write,
        Guid Owner, string LotId)> BuildOwnedBondAcquisitionAsync(bool adopted, string? conflict = null, decimal amount = 100m)
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Acquisition);
        var owner = fixture.Book.FundStructureNodeId;
        var economicEvent = fixture.Request.EconomicEvent with
        { EvidenceLinks = fixture.Request.RetainedEvidence.Select(e => e.EvidenceUri).ToArray() };
        var lineage = fixture.Request.ProjectionLineage with { TriggerEvent = economicEvent };
        fixture = fixture with
        {
            Book = fixture.Book with { FundStructureNodeKind = FundStructureNodeKindDto.Account },
            Position = fixture.Position with
            {
                OriginEvent = economicEvent,
                ProjectionLineage = lineage,
                BookContext = fixture.Position.BookContext with
                { FundStructureNodeKind = FundStructureNodeKindDto.Account }
            },
            Request = fixture.Request with
            {
                EconomicEvent = economicEvent,
                ProjectionLineage = lineage,
                EventAmount = amount,
                ProjectedEffect = fixture.Request.ProjectedEffect with
                {
                    TotalDebits = amount,
                    TotalCredits = amount,
                    Lines = [fixture.Request.ProjectedEffect.Lines[0] with { Debit = amount },
                    fixture.Request.ProjectedEffect.Lines[1] with { AccountId = "Assets:Cash", Credit = amount }]
                }
            }
        };
        var projected = BuildProjectedSpine(fixture);
        var events = Substitute.For<IAssetAccountingEventProjectionStore>();
        events.GetAsync(projected.EventId, 1, 2, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(projected, ComputeFingerprint(projected)));
        events.AppendAsync(Arg.Any<AssetAccountingEventSpineDto>(), 2, fixture.Position.Version, Arg.Any<CancellationToken>())
            .Returns(call => { var spine = call.ArgAt<AssetAccountingEventSpineDto>(0); return new AssetAccountingEventAppendResult(spine, ComputeFingerprint(spine), false); });
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        positions.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(fixture.Security.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        var policies = Substitute.For<IAccountingPolicyService>();
        policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(BuildPolicy());
        var configuration = Substitute.For<IAccountingConfigurationService>();
        var sourceDryRun = BuildMatchingDryRun(fixture);
        var dryRun = sourceDryRun;
        if (conflict is not null)
            dryRun = dryRun with
            {
                GeneratedPostingLines = dryRun.GeneratedPostingLines.Select(line => line with
                {
                    Dimensions = line.Dimensions! with
                    {
                        AccountId = conflict == "owner" ? Guid.NewGuid().ToString("D") : null,
                        TaxLotId = conflict == "lot" ? "wrong-lot" : null,
                        PositionId = conflict == "position" ? Guid.NewGuid() : fixture.Position.PositionId
                    }
                }).ToArray()
            };
        var dryRunCalls = 0;
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++dryRunCalls == 1 ? sourceDryRun : dryRun);
        configuration.GetWorkspaceAsync(fixture.Book.FundProfileId, fixture.Book.LedgerBookId, Arg.Any<CancellationToken>(), null, null)
            .Returns(new AccountingConfigurationWorkspaceDto(fixture.Book.FundProfileId, fixture.Book.LedgerBookId,
                AccountingConfigurationStatusDto.Active, "v1", fixture.Book.UpdatedAt, [fixture.Book],
                [new("investment", "Assets:Investment", "Assets:Investment", "Asset"), new("cash", "Assets:Cash", "Assets:Cash", "Asset")], [], [], [], []));
        var builder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)),
            books, policies, instrumentPositions: adopted ? positions : null);
        LedgerJournalEntryWrite? write = null;
        var authorityBuilder = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
        authorityBuilder.BuildAuthoritativeCandidateWriteAsync(Arg.Any<PostingRuleJournalCandidateRequestDto>(),
                Arg.Any<AssetAccountingCandidateAuthorityContext>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var built = await builder.BuildAuthoritativeCandidateWriteAsync(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0),
                    call.ArgAt<AssetAccountingCandidateAuthorityContext>(1), call.ArgAt<CancellationToken>(2));
                write = built.Write;
                return built;
            });
        var service = new AssetAccountingEventSpineService(events, positions, securities, books, policies,
            configuration, authorityBuilder, Substitute.For<ILedgerJournalStore>(),
            bondIncomeService: Substitute.For<IBondIncomeService>());
        const string lotId = "actual-retained-bond-lot";
        var source = fixture.Request.RetainedEvidence[0];
        var acquired = new AssetAcquisitionLotDto(Guid.NewGuid(), lotId, fixture.Request.EconomicEvent.EffectiveDate,
            1m, amount, "Assets:Investment", 100m, 1m, 100m, Meridian.Contracts.FixedIncome.BondAmortizationMethod.NoAmortization)
        { BondIncome = adopted ? new(source.RetainedAtUtc, amount, 0m, 1m, [], [source]) : null };
        var result = await service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture) with
        { LotMutation = new(AssetLotMutationIntentDto.Acquire, acquired) });
        write.Should().NotBeNull(string.Join("; ", result.Candidate.Issues.Select(i => i.Code)));
        return (result, write!, owner, lotId);
    }
}
