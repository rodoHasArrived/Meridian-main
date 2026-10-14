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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task ReferenceAuthority_PostingCarriesApprovedVersionsAndOnlyExactHistoricalReceiptsOmitThem(
        bool disposal, bool historicalReceipt)
        => AssertReferenceRetryAsync(disposal, historicalReceipt);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task ReferenceAuthority_SeparateStoreWithoutReceiptReadPreservesVersionsAndRefusesHistoricalDowngrade(
        bool disposal, bool historicalReceipt)
        => AssertReferenceRetryAsync(disposal, historicalReceipt, unsupportedReceiptRead: true);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public Task ReferenceAuthority_ChangedPayloadCannotReplayOrDowngradeTheRetainedFingerprint(
        bool disposal, bool historicalReceipt, bool unsupportedReceiptRead)
        => AssertReferenceRetryAsync(disposal, historicalReceipt, unsupportedReceiptRead, changedPayload: true);

    private static async Task AssertReferenceRetryAsync(
        bool disposal, bool historicalReceipt, bool unsupportedReceiptRead = false, bool changedPayload = false)
    {
        var fixture = BuildFixture(disposal ? AssetAccountingEventKindDto.Disposal : AssetAccountingEventKindDto.Acquisition);
        var effect = fixture.Request.ProjectedEffect;
        fixture = fixture with
        {
            Request = fixture.Request with
            {
                ProjectedEffect = effect with
                {
                    Lines = [effect.Lines[0] with { AccountId = disposal ? "Cash" : "Assets:Investment" },
                effect.Lines[1] with { AccountId = disposal ? "Assets:Investment" : "Cash" }]
                }
            }
        };
        var retained = new Dictionary<long, AssetAccountingEventProjectionRecord>();
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.AppendAsync(Arg.Any<AssetAccountingEventSpineDto>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var spine = call.ArgAt<AssetAccountingEventSpineDto>(0);
                retained.Add(spine.SpineVersion, new(spine, ComputeFingerprint(spine)));
                return new AssetAccountingEventAppendResult(spine, ComputeFingerprint(spine), false);
            });
        eventStore.GetAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => retained.GetValueOrDefault(call.ArgAt<long>(2)));
        eventStore.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => retained.Count == 0 ? null : retained[retained.Keys.Max()]);
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        positions.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(fixture.Security.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        var policies = Substitute.For<IAccountingPolicyService>();
        policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(BuildPolicy());
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(BuildMatchingDryRun(fixture));
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetLedgerBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(
            fixture.Book.LedgerBookId, fixture.Book.FundProfileId, fixture.Book.FundStructureNodeId,
            fixture.Book.FundStructureNodeKind, fixture.Book.DisplayName, fixture.Book.BaseCurrency,
            fixture.Book.CreatedAt, fixture.Book.UpdatedAt, AccountingBasis: fixture.Book.AccountingBasis,
            AccountingPolicyId: fixture.Book.AccountingPolicyId, AccountingPolicyVersion: fixture.Book.AccountingPolicyVersion));
        var authorityBuilder = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
        AccountingPostingCandidateWriteResult? stableWrite = null;
        authorityBuilder.BuildAuthoritativeCandidateWriteAsync(Arg.Any<PostingRuleJournalCandidateRequestDto>(),
                Arg.Any<AssetAccountingCandidateAuthorityContext>(), Arg.Any<CancellationToken>())
            .Returns(call => stableWrite ??= BuildReferenceCandidateWrite(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0), fixture));
        var atomic = Substitute.For<IAtomicTaxLotJournalStore>();
        var observed = new List<AtomicTaxLotJournalCommand>();
        AtomicTaxLotJournalResult? committed = null;
        atomic.AppendAssetPostingAsync(Arg.Any<AtomicTaxLotJournalCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.ArgAt<AtomicTaxLotJournalCommand>(0);
            observed.Add(command);
            if (committed is not null)
            {
                if (command.CanonicalFingerprint != committed.CanonicalFingerprint)
                    throw new LedgerValidationException("Atomic tax-lot posting identity collision: changed canonical fingerprint.");
                return committed with { IsExactReplay = true };
            }
            var write = AccountingPostingCommandValidator.NormalizeAndValidate(command.Journal,
                requirePostingCommand: true, requireExpectedVersion: true);
            var journal = new LedgerJournalEntryRecord(write.Entry, write.AggregateId, write.PeriodId, write.CommandId,
                write.CorrelationId, 1, fixture.Request.ProjectedAtUtc.AddHours(1), write.AccountingBasis,
                write.AccountingPolicyId, write.AccountingPolicyVersion, write.RuleId, write.RuleVersion, write.SourceEventId);
            var fingerprint = historicalReceipt
                ? (command with { ReferenceVersions = null }).WithComputedFingerprint().CanonicalFingerprint
                : command.CanonicalFingerprint;
            return committed = new(command.MutationBatchId, command.MutationKind, fingerprint, false,
                journal, [], [], command.RetainedEvidence);
        });
        ledger.GetByAggregateAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>())
            .Returns(_ => committed is null ? [] : new[] { committed.Journal });
        ledger.GetAtomicTaxLotPostingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => committed);

        var lotId = Guid.NewGuid();
        var date = fixture.Request.EconomicEvent.EffectiveDate;
        var source = fixture.Request.RetainedEvidence.Single();
        var acquisitionEvidence = source with { SubjectType = "OpenLotAcquisition", SubjectId = lotId.ToString("D") };
        var lot = new LedgerTaxLotRecord(lotId, fixture.Book.LedgerBookId, new("Assets:Investment", LedgerAccountType.Asset),
            "reference-lot", date, 10m, 10m, 10m, "USD", fixture.Request.ProjectedAtUtc, fixture.Request.ProjectedAtUtc,
            Version: 1, SecurityId: fixture.Security.SecurityId, BookPositionId: fixture.Position.PositionId,
            Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m, 100m, 100m, date, null, [acquisitionEvidence]));
        ledger.GetTaxLotsByIdsAsync(fixture.Book.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([lot]);
        ledger.ListOpenTaxLotsAsync(fixture.Book.LedgerBookId, lot.Account, Arg.Any<CancellationToken>()).Returns([lot]);
        var instruction = disposal
            ? new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Dispose,
                DisposalSelections: [new(lotId, lot.LotId, 1, 10m, 10m, 0, source.EvidenceId, 10m, 100m)],
                ReliefMethod: "Fifo", PolicyRevision: "reference-fifo", AssetAccountId: lot.Account.Name)
            : new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Acquire,
                new(lotId, lot.LotId, date, 10m, 10m, lot.Account.Name));
        var service = new AssetAccountingEventSpineService(eventStore, positions, securities, books, policies,
            configuration, authorityBuilder, ledger);
        await service.ProjectAsync(fixture.Request);
        var drafted = await service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture) with { LotMutation = instruction });
        var candidate = drafted.Spine.DraftedCandidate!;
        var reviewed = candidate.AccountingTimestamp.AddMinutes(10);
        var approval = source with
        {
            EvidenceId = "reference-approval",
            EvidenceUri = "evidence://reference/approval",
            ReviewedBy = "approver",
            ReviewedAtUtc = reviewed,
            RetainedAtUtc = reviewed,
            SourceReference = "reference-approval",
            SubjectType = AssetAccountingEvidenceSubjects.PostingApproval,
            SubjectId = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(candidate.SourceEventId!.Value,
                candidate.EconomicEvent!.EventVersion, candidate.FundProfileId, fixture.Book.LedgerBookId, candidate.PeriodId,
                candidate.AccountingBasis, "reference-approval", drafted.Spine.DraftedCandidateFingerprint!, candidate.TenantId, candidate.CompanyId)
        };
        var request = new PostPostingRuleJournalCandidateRequestDto(candidate, "approver", "reference-approval") { ApprovalEvidence = [approval] };
        // This concrete adapter deliberately uses the interface's default unsupported receipt
        // method, while the separately composed atomic store retains its posting authority.
        ILedgerJournalStore postingLedger = unsupportedReceiptRead ? new JournalWithoutAtomicReceipts(ledger) : ledger;
        var postService = new AccountingPostingCandidatePostService(Substitute.For<IAccountingPostingCandidateWriteBuilder>(),
            postingLedger, atomic, eventStore, authorityBuilder);
        (await postService.PostCandidateAsync(request)).WasReplay.Should().BeFalse();
        var receiptBefore = committed;
        // Reconstruct the public service; replay authority comes from the retained spine and receipt.
        postService = new AccountingPostingCandidatePostService(Substitute.For<IAccountingPostingCandidateWriteBuilder>(),
            postingLedger, atomic, eventStore, authorityBuilder);
        // Alter the canonical journal payload without changing the retained approval attestation.
        var retryRequest = changedPayload ? request with { CorrelationId = Guid.NewGuid().ToString("D") } : request;
        if (changedPayload || (historicalReceipt && unsupportedReceiptRead))
        {
            var retry = () => postService.PostCandidateAsync(retryRequest);
            await retry.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
            observed[1].CanonicalFingerprint.Should().NotBe(committed!.CanonicalFingerprint);
        }
        else
        {
            (await postService.PostCandidateAsync(retryRequest)).WasReplay.Should().BeTrue();
            observed[1].CanonicalFingerprint.Should().Be(committed!.CanonicalFingerprint);
        }
        observed.Should().HaveCount(2);
        observed[0].ReferenceVersions.Should().Be(new AtomicLotReferenceVersions(fixture.Security.Version, fixture.Position.Version));
        observed[1].ReferenceVersions.Should().Be(historicalReceipt && !unsupportedReceiptRead && !changedPayload
            ? null : observed[0].ReferenceVersions);
        committed.Should().BeSameAs(receiptBefore);
        retained.Should().HaveCount(5);
    }

    private sealed class JournalWithoutAtomicReceipts(ILedgerJournalStore inner) : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) => inner.AppendAsync(entry, ct);
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
            => inner.GetByPeriodAsync(periodId, ct);
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
            => inner.GetByAggregateAsync(aggregateId, ct);
        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) => inner.GetPeriodAsync(periodId, ct);
        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(Guid? ledgerBookId = null, string? status = null,
            string? fundProfileId = null, Guid? fundStructureNodeId = null, CancellationToken ct = default)
            => inner.ListPeriodsAsync(ledgerBookId, status, fundProfileId, fundStructureNodeId, ct);
        public Task<LedgerAccountingPeriod> SavePeriodAsync(LedgerAccountingPeriod period, long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null, CancellationToken ct = default)
            => inner.SavePeriodAsync(period, expectedVersion, closeEvent, ct);
        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) => inner.GetLedgerBookAsync(ledgerBookId, ct);
        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(string? fundProfileId = null, Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null, CancellationToken ct = default)
            => inner.ListLedgerBooksAsync(fundProfileId, fundStructureNodeId, fundStructureNodeKind, ct);
        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default) => inner.SaveLedgerBookAsync(book, ct);
        public Task<IReadOnlyList<LedgerTaxLotRecord>> GetTaxLotsByIdsAsync(Guid ledgerBookId, IReadOnlyList<Guid> taxLotRecordIds,
            CancellationToken ct = default) => inner.GetTaxLotsByIdsAsync(ledgerBookId, taxLotRecordIds, ct);
        public Task<IReadOnlyList<LedgerTaxLotRecord>> ListOpenTaxLotsAsync(Guid ledgerBookId, LedgerAccount account,
            CancellationToken ct = default) => inner.ListOpenTaxLotsAsync(ledgerBookId, account, ct);
    }

    private static AccountingPostingCandidateWriteResult BuildReferenceCandidateWrite(
        PostingRuleJournalCandidateRequestDto request, Fixture fixture)
    {
        var result = BuildSuccessorCandidateWrite(request, fixture);
        var write = result.Write!;
        var entry = write.Entry;
        var lines = entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account with { FinancialAccountId = null }, line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency)).ToArray();
        return result with { Write = write with { Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) } };
    }
}
