using System.Collections.Specialized;
using System.Windows.Threading;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class PostedLedgerBasisArrivalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GaapOnlyOpenJournal_RemainsVisibleWhenSummaryIsMissingInEitherArrivalOrder(bool journalFirst)
    {
        WpfTestThread.Run(async () =>
        {
            var summary = Pending<List<LedgerPeriodTrialBalanceLineDto>>();
            var journal = Pending<List<LedgerJournalEntryDto>>();
            var client = new Client { Summary = _ => summary.Task, Journal = _ => journal.Task };
            using var model = new PostedLedgerViewModel(client);
            var load = model.RefreshAsync();

            if (journalFirst)
            {
                journal.SetResult(Journals(Client.PeriodA, Client.BookA, AccountingBasisKindDto.Gaap));
                await Dispatcher.Yield(DispatcherPriority.Background);
                model.JournalLines.Should().ContainSingle();
                model.SelectedBasis.Should().Be(AccountingBasisKindDto.Gaap);
                model.BalanceSummaryText.Should().Be("Trial balance not loaded.");
                summary.SetResult(ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Fail("Open period", 404));
            }
            else
            {
                summary.SetResult(ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Fail("Open period", 404));
                await Dispatcher.Yield(DispatcherPriority.Background);
                model.Bases.Should().BeEmpty();
                journal.SetResult(Journals(Client.PeriodA, Client.BookA, AccountingBasisKindDto.Gaap));
            }
            await load;

            model.Bases.Select(row => row.Basis).Should().Equal(AccountingBasisKindDto.Gaap);
            model.SelectedBasisRow!.Basis.Should().Be(AccountingBasisKindDto.Gaap);
            model.JournalLines.Should().ContainSingle().Which.AccountName.Should().Be("Gaap cash");
            model.HasPeriodNotice.Should().BeTrue();
            model.HasTrialBalanceError.Should().BeFalse();
            model.TrialBalance.Should().BeEmpty();
            model.BalanceSummaryText.Should().Be("Trial balance not loaded.");
            await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);
            model.ProofDrawer.StatusText.Should().Be("Ready");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LaterResponse_PreservesTheBasisUnionAndTheOperatorsSelection(bool journalFirst)
    {
        WpfTestThread.Run(async () =>
        {
            var summary = Pending<List<LedgerPeriodTrialBalanceLineDto>>();
            var journal = Pending<List<LedgerJournalEntryDto>>();
            var client = new Client { Summary = _ => summary.Task, Journal = _ => journal.Task };
            using var model = new PostedLedgerViewModel(client);
            var load = model.RefreshAsync();
            var chosen = journalFirst ? AccountingBasisKindDto.Gaap : AccountingBasisKindDto.Cash;

            if (journalFirst)
                journal.SetResult(Journals(Client.PeriodA, Client.BookA, AccountingBasisKindDto.Primary, AccountingBasisKindDto.Gaap));
            else
                summary.SetResult(Summary(AccountingBasisKindDto.Primary, AccountingBasisKindDto.Cash));
            await Dispatcher.Yield(DispatcherPriority.Background);
            model.SelectedBasisRow = model.Bases.Single(row => row.Basis == chosen);

            if (journalFirst)
                summary.SetResult(Summary(AccountingBasisKindDto.Primary, AccountingBasisKindDto.Cash));
            else
                journal.SetResult(Journals(Client.PeriodA, Client.BookA, AccountingBasisKindDto.Primary, AccountingBasisKindDto.Gaap));
            await load;

            model.Bases.Select(row => row.Basis).Should().Equal(
                AccountingBasisKindDto.Primary, AccountingBasisKindDto.Gaap, AccountingBasisKindDto.Cash);
            model.SelectedBasis.Should().Be(chosen);
            model.SelectedBasisRow!.Basis.Should().Be(chosen);
            model.Bases.Single(row => row.Basis == chosen).IsSelected.Should().BeTrue();
            model.SelectedBasisRow = model.Bases.Single(row => row.Basis == AccountingBasisKindDto.Gaap);
            model.JournalLines.Should().ContainSingle().Which.AccountName.Should().Be("Gaap cash");
            model.SelectedBasisRow = model.Bases.Single(row => row.Basis == AccountingBasisKindDto.Cash);
            model.TrialBalance.Should().ContainSingle().Which.AccountName.Should().Be("Cash cash");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateSummary_ClosesLoadedOrPendingProofBeforeReplacingJournalRows(bool pendingProof)
    {
        WpfTestThread.Run(async () =>
        {
            var summary = Pending<List<LedgerPeriodTrialBalanceLineDto>>();
            var evidence = Pending<EvidencePacketDto>();
            var client = new Client { Summary = _ => summary.Task };
            using var model = new PostedLedgerViewModel(client);
            var load = model.RefreshAsync();
            var selected = model.JournalLines.Single().DebitProof!;
            if (pendingProof)
                client.Proof = _ => evidence.Task;
            var open = model.OpenAmountProofCommand.ExecuteAsync(selected);
            if (!pendingProof)
            {
                await open;
                model.ProofDrawer.StatusText.Should().Be("Ready");
                model.ProofDrawer.Evidence.Should().NotBeEmpty();
            }
            model.ProofDrawer.IsOpen.Should().BeTrue();
            var openAtRowReset = new List<bool>();
            model.JournalLines.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                    openAtRowReset.Add(model.ProofDrawer.IsOpen);
            };

            summary.SetResult(Summary(AccountingBasisKindDto.Gaap));
            await load;

            openAtRowReset.Should().NotBeEmpty().And.OnlyContain(isOpen => !isOpen);
            model.ProofDrawer.IsOpen.Should().BeFalse();
            model.ProofDrawer.Evidence.Should().BeEmpty();
            // The Primary journal basis survives even though the summary carries only GAAP.
            model.SelectedBasis.Should().Be(AccountingBasisKindDto.Primary);
            model.Bases.Select(row => row.Basis).Should().Equal(AccountingBasisKindDto.Primary, AccountingBasisKindDto.Gaap);
            if (pendingProof)
            {
                evidence.SetResult(ApiResponse<EvidencePacketDto>.Ok(Client.Packet(selected)));
                await open;
                model.ProofDrawer.IsOpen.Should().BeFalse();
                model.ProofDrawer.Evidence.Should().BeEmpty();
            }
            await model.OpenAmountProofCommand.ExecuteAsync(selected);
            model.ProofDrawer.StatusText.Should().Be("Blocked", "the replaced amount instance cannot reopen old proof");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewPeriodOrBook_ResetsBasisSelectionAndIgnoresOutgoingSummary(bool changeBook)
    {
        WpfTestThread.Run(async () =>
        {
            var outgoingSummary = Pending<List<LedgerPeriodTrialBalanceLineDto>>();
            var incomingPeriod = changeBook ? Client.PeriodB : Client.PeriodPrior;
            var client = new Client
            {
                Summary = period => period == Client.PeriodA ? outgoingSummary.Task
                    : Task.FromResult(ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Fail("Open period", 404)),
                Journal = period => Task.FromResult(Journals(period,
                    period == Client.PeriodB ? Client.BookB : Client.BookA,
                    AccountingBasisKindDto.Primary, AccountingBasisKindDto.Gaap))
            };
            using var model = new PostedLedgerViewModel(client);
            var oldLoad = model.RefreshAsync();
            var outgoingBasis = model.Bases.Single(row => row.Basis == AccountingBasisKindDto.Gaap);
            model.SelectedBasisRow = outgoingBasis;
            await model.OpenAmountProofCommand.ExecuteAsync(model.JournalLines.Single().DebitProof);

            if (changeBook)
                await model.SelectBookAsync(Client.BookB);
            else
                await model.SelectPeriodAsync(Client.PeriodPrior);
            outgoingSummary.SetResult(Summary(AccountingBasisKindDto.Cash));
            await oldLoad;

            model.SelectedPeriodId.Should().Be(incomingPeriod);
            model.SelectedBasis.Should().Be(AccountingBasisKindDto.Primary);
            model.SelectedBasisRow.Should().NotBeSameAs(outgoingBasis);
            model.Bases.Select(row => row.Basis).Should().Equal(AccountingBasisKindDto.Primary, AccountingBasisKindDto.Gaap);
            model.JournalLines.Should().ContainSingle().Which.DebitProof!.PeriodId.Should().Be(incomingPeriod);
            model.ProofDrawer.IsOpen.Should().BeFalse();
            model.ProofDrawer.Evidence.Should().BeEmpty();
        });
    }

    private static TaskCompletionSource<ApiResponse<T>> Pending<T>() where T : class
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ApiResponse<List<LedgerPeriodTrialBalanceLineDto>> Summary(params AccountingBasisKindDto[] bases)
        => ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok(bases.Select(basis =>
            new LedgerPeriodTrialBalanceLineDto($"{basis} cash", "Asset", null, "cash", 100m, 0m, 100m, 1,
                AccountingBasis: basis)).ToList());

    private static ApiResponse<List<LedgerJournalEntryDto>> Journals(Guid period, Guid book, params AccountingBasisKindDto[] bases)
        => ApiResponse<List<LedgerJournalEntryDto>>.Ok(bases.Select(basis =>
        {
            var journal = Guid.NewGuid();
            return new LedgerJournalEntryDto(journal, period, book, Guid.NewGuid(), null, null, 1, Client.Now, Client.Now,
                "Posted journal", 100m, 100m, true,
                [new(Guid.NewGuid(), journal, Client.Now, $"{basis} cash", "Asset", "SAME", "cash", 100m, 0m, "Posted amount")],
                AccountingBasis: basis);
        }).ToList());

    private sealed class Client : ILedgerReportsApiClient
    {
        public static readonly Guid BookA = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public static readonly Guid BookB = Guid.Parse("10000000-0000-0000-0000-000000000002");
        public static readonly Guid PeriodA = Guid.Parse("20000000-0000-0000-0000-000000000001");
        public static readonly Guid PeriodB = Guid.Parse("20000000-0000-0000-0000-000000000002");
        public static readonly Guid PeriodPrior = Guid.Parse("20000000-0000-0000-0000-000000000003");
        public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-01T12:00:00Z");

        public Func<Guid, Task<ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>>> Summary { get; init; }
            = _ => Task.FromResult(ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Fail("Open period", 404));
        public Func<Guid, Task<ApiResponse<List<LedgerJournalEntryDto>>>> Journal { get; init; }
            = period => Task.FromResult(Journals(period, BookA, AccountingBasisKindDto.Primary));
        public Func<PostedLedgerAmountSelection, Task<ApiResponse<EvidencePacketDto>>> Proof { get; set; }
            = selection => Task.FromResult(ApiResponse<EvidencePacketDto>.Ok(Packet(selection)));

        public Task<ApiResponse<List<LedgerBookDto>>> GetBooksAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<List<LedgerBookDto>>.Ok([
                new(BookA, "fund-a", BookA, FundStructureNodeKindDto.Fund, "A fund", "USD", Now, Now),
                new(BookB, "fund-b", BookB, FundStructureNodeKindDto.Fund, "B fund", "USD", Now, Now)]));

        public Task<ApiResponse<List<LedgerPeriodDto>>> GetPeriodsAsync(Guid? ledgerBookId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<List<LedgerPeriodDto>>.Ok(ledgerBookId == BookA
                ? [Period(PeriodA, BookA, 9), Period(PeriodPrior, BookA, 8)] : [Period(PeriodB, BookB, 9)]));

        private static LedgerPeriodDto Period(Guid period, Guid book, int number)
            => new(period, book, 2026, number, $"Period {number}", new(2026, number, 1), new(2026, number, 28),
                LedgerPeriodStatusDto.Open, Now, null, 1);

        public Task<ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>> GetTrialBalanceAsync(Guid periodId, CancellationToken ct = default)
            => Summary(periodId);
        public Task<ApiResponse<List<LedgerJournalEntryDto>>> GetJournalEntriesAsync(Guid periodId, CancellationToken ct = default)
            => Journal(periodId);
        public Task<ApiResponse<LedgerPeriodPnlSummaryDto>> GetPnlSummaryAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<LedgerPeriodPnlSummaryDto>.Fail("Open period", 404));

        public Task<ApiResponse<EvidencePacketDto>> GetAmountProofAsync(
            string subjectId, Guid ledgerBookId, Guid periodId, string fundProfileId, CancellationToken ct = default)
        {
            var ids = subjectId.Split(':');
            return Proof(new(Guid.Parse(ids[0]), Guid.Parse(ids[1]), ledgerBookId, periodId, fundProfileId,
                ids[2], 100m, "USD", "Posted cash"));
        }

        public static EvidencePacketDto Packet(PostedLedgerAmountSelection selection)
        {
            var subject = new EvidenceSubjectDto(selection.SubjectId, "ledger-amount", "Selected debit", "Accounting", null, "PostedLedger");
            var digest = new string('a', 64);
            var route = $"/workstation/evidence/vault/ev-arrival?ledgerAmountSubjectId={Uri.EscapeDataString(selection.SubjectId)}"
                + $"&ledgerBookId={selection.LedgerBookId:D}&periodId={selection.PeriodId:D}&fundProfileId={selection.FundProfileId}&expectedContentHash={digest}";
            var retainedSubject = $"{selection.FundProfileId}:{selection.LedgerBookId:D}:{selection.PeriodId:D}:{selection.SubjectId}";
            return new(subject, Now,
                [new("retained-source", subject, "source-document", EvidenceStatusDto.Ready, new(Now, false, null), "vault", "Retained source",
                    [new("retained-source", "source-document", null, route, Now, digest, true, "ledger-amount", retainedSubject)], [])],
                [], new(100, EvidenceStatusDto.Ready, [], [], [], [], []), [], [])
            {
                LedgerAmount = new(selection.SubjectId, new("tenant-a", "company-a", selection.FundProfileId, selection.LedgerBookId, selection.PeriodId),
                    selection.Amount, selection.Currency, EvidenceStatusDto.Ready,
                    [new("retained-source", "source-document", "Retained supporting source", route, "vault", Now, EvidenceStatusDto.Ready, digest)], [])
            };
        }
    }
}
