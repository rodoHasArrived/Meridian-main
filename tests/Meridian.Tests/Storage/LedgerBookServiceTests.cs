using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Storage;

public sealed class LedgerBookServiceTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task CreateBookAsync_WhenScopeAlreadyExists_ReturnsExistingBook()
    {
        var store = new InMemoryLedgerJournalStore();
        var service = new PostgresLedgerBookService(store);
        var nodeId = Guid.NewGuid();
        var request = new CreateLedgerBookRequest(
            "alpha-fund",
            nodeId,
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "usd");

        var first = await service.CreateBookAsync(request);
        var second = await service.CreateBookAsync(request with { DisplayName = "Alpha Fund Duplicate" });

        second.LedgerBookId.Should().Be(first.LedgerBookId);
        second.DisplayName.Should().Be("Alpha Fund");
        (await service.ListBooksAsync(new LedgerBookQuery("alpha-fund", nodeId))).Should().ContainSingle();
    }

    [Fact]
    public async Task ClosePeriodAsync_SoftClose_PersistsSummaryAndPropagatesInboxWorkItem()
    {
        var store = new InMemoryLedgerJournalStore();
        var inbox = new InMemoryOperatorInboxService();
        var service = new PostgresLedgerBookService(store, inbox);
        await inbox.UpsertItemAsync(new OperatorWorkItemDto(
            WorkItemId: "reconciliation-break-alpha",
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: "Reconciliation break requires review",
            Detail: "Existing cash variance.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-01-15T10:00:00Z"),
            Workspace: "Accounting",
            TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
            TargetPageTag: "FundReconciliation"));

        var book = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"));
        var previous = await store.SavePeriodAsync(new LedgerAccountingPeriod(
            Guid.NewGuid(),
            book.LedgerBookId,
            2026,
            1,
            "2026-P01",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            "HardClosed",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            0), expectedVersion: 0);
        await store.AppendAsync(BuildBalancedEntry(previous.PeriodId, revenue: 800m, expense: 300m));

        var current = await service.CreatePeriodAsync(new CreateLedgerPeriodRequest(
            book.LedgerBookId,
            2026,
            2,
            "2026-P02",
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 28)));
        await store.AppendAsync(BuildBalancedEntry(current.PeriodId, revenue: 1_200m, expense: 300m));

        var result = await service.ClosePeriodAsync(
            current.PeriodId,
            new CloseLedgerPeriodRequest(
                LedgerPeriodCloseKindDto.SoftClose,
                ClosedBy: "fund-controller",
                Notes: "Month-end soft close.",
                RequiredSignoffRole: "Fund Controller",
                ToleranceProfileId: "month-end-25bp"));

        result.Period.Status.Should().Be(LedgerPeriodStatusDto.SoftClosed);
        result.Summary.TotalDebits.Should().Be(1_500m);
        result.Summary.TotalCredits.Should().Be(1_500m);
        result.Summary.NetIncome.Should().Be(900m);
        result.Summary.PeriodOnPeriodVariance.Should().Be(400m);
        result.Summary.OpenBreakCount.Should().Be(1);
        result.Summary.SignoffStatus.Should().Be(LedgerPeriodSignoffStatusDto.Pending);
        result.Summary.TrialBalance.Should().Contain(row =>
            row.AccountName == "Management fees" &&
            row.AccountType == nameof(LedgerAccountType.Revenue) &&
            row.Balance == 1_200m);
        result.WorkItem.Kind.Should().Be(OperatorWorkItemKindDto.LedgerPeriodClose);
        result.WorkItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        result.WorkItem.TargetPageTag.Should().Be("FundReconciliation");
        result.WorkItem.Detail.Should().Contain("Fund Controller");
        result.WorkItem.Detail.Should().Contain("month-end-25bp");
        result.WorkItem.Detail.Should().Contain("FundReconciliation");
        result.WorkItem.RequiredSignoffRole.Should().Be("Fund Controller");
        result.WorkItem.ToleranceProfileId.Should().Be("month-end-25bp");
        result.WorkItem.SignoffStatus.Should().Be(nameof(LedgerPeriodSignoffStatusDto.Pending));

        var contributedItems = await inbox.GetItemsAsync();
        contributedItems.Should().ContainSingle(item =>
            item.WorkItemId == result.WorkItem.WorkItemId &&
            item.Kind == OperatorWorkItemKindDto.LedgerPeriodClose &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "FundReconciliation" &&
            item.RequiredSignoffRole == "Fund Controller" &&
            item.ToleranceProfileId == "month-end-25bp");
    }

    [Fact]
    public async Task ClosePeriodAsync_WhenPeriodAlreadySoftClosed_RejectsSecondSoftClose()
    {
        var store = new InMemoryLedgerJournalStore();
        var service = new PostgresLedgerBookService(store);
        var book = await service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"));
        var period = await service.CreatePeriodAsync(new CreateLedgerPeriodRequest(
            book.LedgerBookId,
            2026,
            3,
            "2026-P03",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)));

        await service.ClosePeriodAsync(
            period.PeriodId,
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.SoftClose, "fund-controller"));
        var act = () => service.ClosePeriodAsync(
            period.PeriodId,
            new CloseLedgerPeriodRequest(LedgerPeriodCloseKindDto.SoftClose, "fund-controller"));

        await act.Should().ThrowAsync<LedgerPeriodTransitionException>()
            .WithMessage("*Cannot transition*SoftClosed to SoftClosed*");
    }

    [Fact]
    public async Task CreateBookAsync_WhenCanceled_PropagatesCancellation()
    {
        var service = new PostgresLedgerBookService(new InMemoryLedgerJournalStore());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.CreateBookAsync(new CreateLedgerBookRequest(
            "alpha-fund",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Alpha Fund",
            "USD"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LedgerEndpoints_CreateListAndClosePeriod_PropagatesCloseWorkItemToOperatorInbox()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var book = await PostJsonAsync<LedgerBookDto>(
            client,
            UiApiRoutes.LedgerBooks,
            new CreateLedgerBookRequest(
                "alpha-fund",
                Guid.Parse("59f045cb-f681-4b0c-943d-44c946f78214"),
                FundStructureNodeKindDto.Fund,
                "Alpha Fund",
                "USD"));
        var period = await PostJsonAsync<LedgerPeriodDto>(
            client,
            UiApiRoutes.LedgerPeriods,
            new CreateLedgerPeriodRequest(
                book.LedgerBookId,
                2026,
                4,
                "2026-P04",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 30)));

        var openPeriods = await client.GetFromJsonAsync<IReadOnlyList<LedgerPeriodDto>>(
            $"{UiApiRoutes.LedgerPeriods}?ledgerBookId={book.LedgerBookId:D}&openOnly=true",
            ServerJsonOptions);
        openPeriods.Should().ContainSingle(p => p.PeriodId == period.PeriodId);

        var closeRoute = UiApiRoutes.WithParam(UiApiRoutes.LedgerPeriodClose, "periodId", period.PeriodId.ToString());
        var close = await PostJsonAsync<LedgerPeriodCloseResultDto>(
            client,
            closeRoute,
            new CloseLedgerPeriodRequest(
                LedgerPeriodCloseKindDto.HardClose,
                ClosedBy: "fund-controller",
                RequiredSignoffRole: "Fund Controller",
                ToleranceProfileId: "close-tolerance-v1"));

        close.Period.Status.Should().Be(LedgerPeriodStatusDto.HardClosed);
        close.WorkItem.Kind.Should().Be(OperatorWorkItemKindDto.LedgerPeriodClose);
        close.WorkItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        close.WorkItem.TargetPageTag.Should().Be("FundReconciliation");
        close.WorkItem.RequiredSignoffRole.Should().Be("Fund Controller");
        close.WorkItem.ToleranceProfileId.Should().Be("close-tolerance-v1");

        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            UiApiRoutes.WorkstationOperatorInbox,
            ServerJsonOptions);
        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == close.WorkItem.WorkItemId &&
            item.Kind == OperatorWorkItemKindDto.LedgerPeriodClose &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "FundReconciliation" &&
            item.RequiredSignoffRole == "Fund Controller" &&
            item.ToleranceProfileId == "close-tolerance-v1");
    }

    [Fact]
    public async Task OperatorInbox_ShouldAttachLedgerPeriodCloseNavigationWhenContributedItemIsSparse()
    {
        await using var app = await CreateAppAsync();
        var inboxService = app.Services.GetRequiredService<IOperatorInboxService>();
        await inboxService.UpsertItemAsync(new OperatorWorkItemDto(
            WorkItemId: "ledger-period-close-sparse",
            Kind: OperatorWorkItemKindDto.LedgerPeriodClose,
            Label: "SoftClosed sign-off required",
            Detail: "Period close requires controller sign-off.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-04-30T16:00:00Z")));

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>(
            UiApiRoutes.WorkstationOperatorInbox,
            ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == "ledger-period-close-sparse" &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "FundReconciliation" &&
            item.Workspace == "Accounting");
    }

    [Fact]
    public void LedgerBookMigration_DefinesLedgerBooksAndBookScopedPeriods()
    {
        var sql = ReadMigration("V_ledger_003__ledger_books.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.ledger_books");
        sql.Should().Contain("fund_structure_node_id uuid not null");
        sql.Should().Contain("add column if not exists ledger_book_id uuid null");
        sql.Should().Contain("ux_accounting_periods_book_fiscal_period");
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string route, object request)
    {
        using var response = await client.PostAsJsonAsync(route, request, ServerJsonOptions);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<T>(ServerJsonOptions);
        payload.Should().NotBeNull();
        return payload!;
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(options =>
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter("ledger-tests")));
        builder.Services.AddSingleton<IOperatorInboxService, InMemoryOperatorInboxService>();
        builder.Services.AddSingleton<ILedgerJournalStore, InMemoryLedgerJournalStore>();
        builder.Services.AddSingleton<ILedgerBookService>(sp =>
            new PostgresLedgerBookService(
                sp.GetRequiredService<ILedgerJournalStore>(),
                sp.GetRequiredService<IOperatorInboxService>()));

        var app = builder.Build();
        app.UseRateLimiter();
        app.MapLedgerEndpoints(ServerJsonOptions);
        app.MapWorkstationEndpoints(ServerJsonOptions);

        await app.StartAsync();
        return app;
    }

    private static LedgerJournalEntryWrite BuildBalancedEntry(Guid periodId, decimal revenue, decimal expense)
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-02-28T21:00:00Z");
        const string description = "Month-end revenue and expense posting";
        var lines = new[]
        {
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount("Cash", LedgerAccountType.Asset),
                debit: revenue,
                credit: 0m,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount("Management fees", LedgerAccountType.Revenue),
                debit: 0m,
                credit: revenue,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount("Operating expense", LedgerAccountType.Expense),
                debit: expense,
                credit: 0m,
                description),
            new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount("Cash", LedgerAccountType.Asset),
                debit: 0m,
                credit: expense,
                description)
        };

        return new LedgerJournalEntryWrite(
            new JournalEntry(journalEntryId, timestamp, description, lines),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId);
    }

    private static string ReadMigration(string fileName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "Ledger", "Migrations", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Storage")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }

    private sealed class InMemoryLedgerJournalStore : ILedgerJournalStore
    {
        private readonly Dictionary<Guid, LedgerBookRecord> _books = [];
        private readonly Dictionary<Guid, LedgerAccountingPeriod> _periods = [];
        private readonly Dictionary<Guid, List<LedgerJournalEntryRecord>> _entriesByPeriod = [];
        private long _sequence;

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_entriesByPeriod.TryGetValue(entry.PeriodId, out var entries))
            {
                entries = [];
                _entriesByPeriod[entry.PeriodId] = entries;
            }

            entries.Add(new LedgerJournalEntryRecord(
                entry.Entry,
                entry.AggregateId,
                entry.PeriodId,
                entry.CommandId,
                entry.CorrelationId,
                ++_sequence,
                DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _entriesByPeriod.TryGetValue(periodId, out var entries)
                    ? entries.ToArray()
                    : []);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var records = _entriesByPeriod.Values
                .SelectMany(static entries => entries)
                .Where(entry => entry.AggregateId == aggregateId)
                .ToArray();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(records);
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_periods.GetValueOrDefault(periodId));
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<LedgerAccountingPeriod> periods = _periods.Values;
            if (ledgerBookId.HasValue)
            {
                periods = periods.Where(period => period.LedgerBookId == ledgerBookId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                periods = periods.Where(period => string.Equals(period.Status, status, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(fundProfileId) || fundStructureNodeId.HasValue)
            {
                periods = periods.Where(period =>
                    period.LedgerBookId is { } id &&
                    _books.TryGetValue(id, out var book) &&
                    (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                    (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value));
            }

            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(
                periods
                    .OrderBy(static period => period.StartDate)
                    .ThenBy(static period => period.PeriodNo)
                    .ToArray());
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_periods.TryGetValue(period.PeriodId, out var current))
            {
                if (current.Version != expectedVersion)
                {
                    throw new InvalidOperationException("Simulated period version conflict.");
                }

                var updated = period with { Version = expectedVersion + 1 };
                _periods[period.PeriodId] = updated;
                return Task.FromResult(updated);
            }

            if (expectedVersion != 0)
            {
                throw new InvalidOperationException("Simulated period version conflict.");
            }

            var saved = period with { Version = 1 };
            _periods[period.PeriodId] = saved;
            return Task.FromResult(saved);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_books.GetValueOrDefault(ledgerBookId));
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<LedgerBookRecord> books = _books.Values;
            if (!string.IsNullOrWhiteSpace(fundProfileId))
            {
                books = books.Where(book => string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase));
            }

            if (fundStructureNodeId.HasValue)
            {
                books = books.Where(book => book.FundStructureNodeId == fundStructureNodeId.Value);
            }

            if (fundStructureNodeKind.HasValue)
            {
                books = books.Where(book => book.FundStructureNodeKind == fundStructureNodeKind.Value);
            }

            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>(
                books.OrderBy(static book => book.DisplayName, StringComparer.Ordinal).ToArray());
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _books[book.LedgerBookId] = book;
            return Task.FromResult(book);
        }
    }
}
