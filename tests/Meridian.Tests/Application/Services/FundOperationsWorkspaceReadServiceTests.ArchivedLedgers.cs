using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed partial class FundOperationsWorkspaceReadServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetWorkspaceAsync_ArchivedLedgers_HydratesOnlySelectedFundRunsWithoutDurableAuthority(bool durableAuthority)
    {
        var root = CreateTempDirectory();
        try
        {
            const string fundProfileId = "fund-archive-selection";
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(root),
                new JsonlStrategyRunLedgerArchive(root, new AtomicFileWriterAdapter()));
            var runs = new[]
            {
                BuildRun("archive-selected-1", "strategy-1", "Selected One", fundProfileId, "Archive Fund"),
                BuildRun("archive-excluded", "strategy-2", "Excluded", fundProfileId, "Archive Fund"),
                BuildRun("archive-selected-3", "strategy-3", "Selected Three", fundProfileId, "Archive Fund"),
                BuildRun("archive-foreign", "strategy-4", "Foreign", "foreign-fund", "Foreign Fund")
            };
            // BuildRun replaces Start's retained inputs; let the durable store bind their canonical hash.
            foreach (var run in runs)
                await store.RecordRunAsync(run with { InputHashSha256 = null });
            (await store.QueryRunsAsync(new StrategyRunRepositoryQuery()))
                .Should().OnlyContain(run => run.RetainedJournalEntryCount == 2 && run.Metrics!.Ledger.JournalEntryCount == 0,
                    "the production repository intentionally returns bounded summaries");

            var repository = new ArchiveReadTrackingRepository(store);
            var asOf = new DateTimeOffset(2026, 4, 12, 0, 0, 0, TimeSpan.Zero);
            var book = new LedgerBookRecord(Guid.NewGuid(), fundProfileId, Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Authoritative empty book", "USD", asOf.AddDays(-1), asOf);
            var journalStore = durableAuthority ? new ReadOnlyFundLedgerJournalStore(book, []) : null;
            var securityMaster = new NullSecurityMasterQueryService();
            var service = new FundOperationsWorkspaceReadService(new InMemoryFundAccountService(), repository,
                new PortfolioReadService(), new NavAttributionService(securityMaster), new ReportGenerationService(securityMaster),
                ledgerJournalStore: journalStore);

            var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(fundProfileId, AsOf: asOf,
                SelectedLedgerIds: ["archive-selected-1", "archive-selected-3"]));

            workspace.RecordedRunCount.Should().Be(2);
            workspace.RelatedRunIds.Should().BeEquivalentTo(["archive-selected-1", "archive-selected-3"]);
            if (durableAuthority)
            {
                workspace.Ledger.JournalEntryCount.Should().Be(0, "an empty governed book must remain authoritative");
                repository.DetailRequests.Should().BeEmpty("research archives cannot replace governed journal authority");
            }
            else
            {
                workspace.Ledger.JournalEntryCount.Should().Be(4);
                workspace.Ledger.Journal.Select(row => row.JournalEntryId).Should().BeEquivalentTo(
                    new[] { runs[0], runs[2] }.SelectMany(run => run.Metrics!.Ledger.Journal).Select(entry => entry.JournalEntryId));
                workspace.Nav.TotalNav.Should().NotBe(0m);
                repository.DetailRequests.Should().BeEquivalentTo(["archive-selected-1", "archive-selected-3"],
                    "unselected and foreign-fund archives must never be opened");
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign-fund")]
    [InlineData("different-run")]
    [InlineData("different-archive")]
    [InlineData("incomplete-journal")]
    public async Task GetWorkspaceAsync_ArchivedLedgerDetailMustMatchAdmittedSummary(string mismatch)
    {
        var root = CreateTempDirectory();
        try
        {
            const string fundProfileId = "fund-archive-identity";
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(root),
                new JsonlStrategyRunLedgerArchive(root, new AtomicFileWriterAdapter()));
            await store.RecordRunAsync(BuildRun("archive-identity", "strategy", "Archive Identity", fundProfileId, "Archive Fund")
                with { InputHashSha256 = null });
            var repository = new ArchiveReadTrackingRepository(store, detail => mismatch switch
            {
                "missing" => null,
                "foreign-fund" => detail with { FundProfileId = "foreign-fund" },
                "different-run" => detail with { RunId = "foreign-run" },
                "different-archive" => detail with
                {
                    OutputMetadata = new Dictionary<string, string>(detail.OutputMetadata)
                    {
                        [StrategyRunLedgerReference.HashMetadataKey] = new string('0', 64)
                    }
                },
                "incomplete-journal" => detail with { Metrics = detail.Metrics! with { Ledger = new Meridian.Ledger.Ledger() } },
                _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
            });
            var securityMaster = new NullSecurityMasterQueryService();
            var service = new FundOperationsWorkspaceReadService(new InMemoryFundAccountService(), repository,
                new PortfolioReadService(), new NavAttributionService(securityMaster), new ReportGenerationService(securityMaster));

            var read = () => service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(fundProfileId));

            await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*scoped archive reference*");
            repository.DetailRequests.Should().Equal("archive-identity");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private sealed class ArchiveReadTrackingRepository(
        IStrategyRepository inner,
        Func<StrategyRunEntry, StrategyRunEntry?>? transformDetail = null) : IStrategyRepository
    {
        public List<string> DetailRequests { get; } = [];

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default) => inner.RecordRunAsync(entry, ct);
        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, CancellationToken ct = default) => inner.GetRunsAsync(strategyId, ct);
        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) => inner.GetLatestRunAsync(strategyId, ct);
        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default) => inner.GetAllRunsAsync(ct);

        public async Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DetailRequests.Add(runId);
            var detail = await inner.GetRunByIdAsync(runId, ct);
            return detail is null || transformDetail is null ? detail : transformDetail(detail);
        }
    }
}
