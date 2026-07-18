using System.Runtime.CompilerServices;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class AccountingPositionSnapshotCaptureServiceTests
{
    private static readonly Guid FundId = Guid.Parse("8c45a243-a47b-4d53-b815-c030fc4fbf5f");
    private static readonly Guid EntityId = Guid.Parse("eeef45b5-e57f-464c-81f5-f653da6d5592");
    private static readonly Guid BookId = Guid.Parse("d48620fc-960b-40a2-aeab-1694028b5e31");
    private static readonly DateTimeOffset SourceAsOf = new(2026, 7, 15, 20, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CaptureBrokerageSyncAsync_WritesDailyAndDividendHistoryWithExactServerOwnedScope()
    {
        var accountId = Guid.NewGuid();
        var sourceAsOf = new DateTimeOffset(2026, 7, 15, 20, 30, 0, TimeSpan.Zero);
        var snapshots = new List<AccountSnapshotRecord>();
        var snapshotStore = Substitute.For<IPositionSnapshotStore>();
        snapshotStore.SaveSnapshotConditionallyAsync(
                Arg.Do<AccountSnapshotRecord>(snapshots.Add),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PositionSnapshotSaveOutcome.Appended));
        var dailySource = Substitute.For<IDailyValuationPortfolioSource>();
        dailySource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([DailySchedule(accountId, "daily-positions")]);
        var automatedSource = Substitute.For<IAutomatedJournalScheduleStore>();
        automatedSource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([DividendSchedule(accountId, "dividend-positions")]);
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(Account(accountId));
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>())
            .Returns(new LedgerBookRecord(
                BookId,
                "fund-alpha",
                FundId,
                FundStructureNodeKindDto.Fund,
                "Primary book",
                "USD",
                sourceAsOf.AddMonths(-1),
                sourceAsOf));
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync("fund-alpha", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-alpha", "tenant-a", "company-a"));
        var service = new AccountingPositionSnapshotCaptureService(
            snapshotStore,
            dailySource,
            automatedSource,
            accounts,
            ledger,
            tenancy);

        var captured = await service.CaptureBrokerageSyncAsync(Projection(accountId), sourceAsOf);

        captured.Should().Be(2);
        snapshots.Should().HaveCount(2);
        snapshots.Select(static snapshot => snapshot.RunId)
            .Should().BeEquivalentTo("daily-positions", "dividend-positions");
        snapshots.Should().OnlyContain(snapshot =>
            snapshot.AccountId == accountId.ToString("D") &&
            snapshot.AsOf == sourceAsOf &&
            snapshot.TenantId == "tenant-a" &&
            snapshot.CompanyId == "company-a" &&
            snapshot.FundProfileId == "fund-alpha" &&
            snapshot.LedgerBookId == BookId &&
            snapshot.EntityId == EntityId.ToString("D"));
        snapshots.Should().OnlyContain(snapshot =>
            snapshot.Positions.Count == 1 &&
            snapshot.Positions[0].Symbol == "AAPL" &&
            snapshot.Positions[0].Quantity == 100m &&
            snapshot.Positions[0].CostBasis == 180m &&
            snapshot.Positions[0].RealisedPnl == 0m);
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_EquivalentConcurrentStoreWinnerDoesNotInflateCaptureCount()
    {
        var accountId = Guid.NewGuid();
        var snapshotStore = Substitute.For<IPositionSnapshotStore>();
        snapshotStore
            .GetLatestSnapshotAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AccountSnapshotRecord?>(null));
        snapshotStore
            .SaveSnapshotConditionallyAsync(Arg.Any<AccountSnapshotRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PositionSnapshotSaveOutcome.EquivalentAlreadyExists));
        var service = CreateConfiguredService(
            accountId,
            snapshotStore,
            [DailySchedule(accountId, "concurrent-retry")],
            []);

        var captured = await service.CaptureBrokerageSyncAsync(Projection(accountId), SourceAsOf);

        captured.Should().Be(0);
        await snapshotStore.Received(1)
            .SaveSnapshotConditionallyAsync(Arg.Any<AccountSnapshotRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_RejectsConflictingOwnerBindingsForSameRunAndAccount()
    {
        var accountId = Guid.NewGuid();
        var snapshotStore = Substitute.For<IPositionSnapshotStore>();
        var dailySource = Substitute.For<IDailyValuationPortfolioSource>();
        dailySource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([DailySchedule(accountId, "shared-run")]);
        var automatedSource = Substitute.For<IAutomatedJournalScheduleStore>();
        automatedSource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([DividendSchedule(accountId, "shared-run") with { CompanyId = "company-b" }]);
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(accountId, Arg.Any<CancellationToken>()).Returns(Account(accountId));
        var service = new AccountingPositionSnapshotCaptureService(
            snapshotStore,
            dailySource,
            automatedSource,
            accounts,
            Substitute.For<ILedgerJournalStore>(),
            Substitute.For<IFundProfileTenancyRegistry>());

        var act = () => service.CaptureBrokerageSyncAsync(
            Projection(accountId),
            new DateTimeOffset(2026, 7, 15, 20, 30, 0, TimeSpan.Zero));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*conflicting accounting owners*");
        await snapshotStore.DidNotReceive()
            .SaveSnapshotConditionallyAsync(Arg.Any<AccountSnapshotRecord>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureBrokerageSyncAsync_RequiresBothScheduleSources(bool missingDailySource)
    {
        var accountId = Guid.NewGuid();
        var dailySource = missingDailySource ? null : Substitute.For<IDailyValuationPortfolioSource>();
        var automatedSource = missingDailySource ? Substitute.For<IAutomatedJournalScheduleStore>() : null;
        var service = new AccountingPositionSnapshotCaptureService(
            new RecordingPositionSnapshotStore(),
            dailySource,
            automatedSource,
            Substitute.For<IAccountQueryService>(),
            Substitute.For<ILedgerJournalStore>(),
            Substitute.For<IFundProfileTenancyRegistry>());

        var act = () => service.CaptureBrokerageSyncAsync(Projection(accountId), SourceAsOf);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*both daily-valuation and automated-journal schedule sources*");
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_MissingProviderTimestampFailsClosed()
    {
        var accountId = Guid.NewGuid();
        var service = new AccountingPositionSnapshotCaptureService(
            new RecordingPositionSnapshotStore(),
            Substitute.For<IDailyValuationPortfolioSource>(),
            Substitute.For<IAutomatedJournalScheduleStore>(),
            Substitute.For<IAccountQueryService>(),
            Substitute.For<ILedgerJournalStore>(),
            Substitute.For<IFundProfileTenancyRegistry>());

        var act = () => service.CaptureBrokerageSyncAsync(Projection(accountId), default);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("sourceAsOfUtc")
            .WithMessage("*source timestamp is required*");
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_NoMatchingCandidateDoesNotRequireCaptureDependencies()
    {
        var accountId = Guid.NewGuid();
        var dailySource = Substitute.For<IDailyValuationPortfolioSource>();
        dailySource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([DailySchedule(Guid.NewGuid(), "other-account")]);
        var automatedSource = Substitute.For<IAutomatedJournalScheduleStore>();
        automatedSource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        var service = new AccountingPositionSnapshotCaptureService(
            snapshotStore: null,
            dailySource,
            automatedSource,
            accounts: null,
            ledger: null,
            tenancy: null);

        var captured = await service.CaptureBrokerageSyncAsync(Projection(accountId), SourceAsOf);

        captured.Should().Be(0);
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_ConfiguredCandidateRejectsMissingCaptureDependencies()
    {
        var accountId = Guid.NewGuid();
        var dailySource = Substitute.For<IDailyValuationPortfolioSource>();
        dailySource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([DailySchedule(accountId, "configured-run")]);
        var automatedSource = Substitute.For<IAutomatedJournalScheduleStore>();
        automatedSource.ListAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        var service = new AccountingPositionSnapshotCaptureService(
            snapshotStore: null,
            dailySource,
            automatedSource,
            accounts: null,
            ledger: null,
            tenancy: null);

        var act = () => service.CaptureBrokerageSyncAsync(Projection(accountId), SourceAsOf);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*configured scopes*required services are unavailable*");
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("position")]
    public async Task CaptureBrokerageSyncAsync_RejectsSourceCurrencyOutsideAuthoritativeAccountScope(
        string mismatch)
    {
        var accountId = Guid.NewGuid();
        var store = new RecordingPositionSnapshotStore();
        var service = CreateConfiguredService(
            accountId,
            store,
            [DailySchedule(accountId, "currency-run")],
            []);
        var projection = mismatch == "balance"
            ? Projection(accountId, balanceCurrency: "EUR")
            : Projection(accountId, positionCurrency: "EUR");

        var act = () => service.CaptureBrokerageSyncAsync(projection, SourceAsOf);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*currency*");
        store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_RejectsObservationOlderThanRetainedHistory()
    {
        var accountId = Guid.NewGuid();
        var store = new RecordingPositionSnapshotStore();
        store.Seed(Snapshot(accountId, "ordered-run", SourceAsOf.AddMinutes(1)));
        var service = CreateConfiguredService(
            accountId,
            store,
            [DailySchedule(accountId, "ordered-run")],
            []);

        var act = () => service.CaptureBrokerageSyncAsync(Projection(accountId), SourceAsOf);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*newer observation*");
        store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_SameTimestampEquivalentPayloadSkipsAppendRegardlessOfPositionOrder()
    {
        var accountId = Guid.NewGuid();
        var store = new RecordingPositionSnapshotStore();
        store.Seed(Snapshot(
            accountId,
            "retry-run",
            SourceAsOf,
            positions:
            [
                new PositionRecord("MSFT", 20m, 400m, 100m, 0m),
                new PositionRecord("AAPL", 100m, 180m, 750m, 0m)
            ]));
        var service = CreateConfiguredService(
            accountId,
            store,
            [DailySchedule(accountId, "retry-run")],
            []);

        var captured = await service.CaptureBrokerageSyncAsync(
            Projection(accountId, includeSecondPosition: true),
            SourceAsOf);

        captured.Should().Be(0);
        store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_SameTimestampDifferentPayloadFailsClosed()
    {
        var accountId = Guid.NewGuid();
        var store = new RecordingPositionSnapshotStore();
        store.Seed(Snapshot(accountId, "conflict-run", SourceAsOf));
        store.Seed(Snapshot(accountId, "conflict-run", SourceAsOf) with { Cash = 50_001m });
        var service = CreateConfiguredService(
            accountId,
            store,
            [DailySchedule(accountId, "conflict-run")],
            []);

        var act = () => service.CaptureBrokerageSyncAsync(Projection(accountId), SourceAsOf);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different payload at the same source timestamp*");
        store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureBrokerageSyncAsync_LaterHistoryConflictPreventsEveryEarlierWrite()
    {
        var accountId = Guid.NewGuid();
        var store = new RecordingPositionSnapshotStore();
        store.Seed(Snapshot(accountId, "z-conflict-run", SourceAsOf) with { Cash = 50_001m });
        var service = CreateConfiguredService(
            accountId,
            store,
            [DailySchedule(accountId, "a-valid-run")],
            [DividendSchedule(accountId, "z-conflict-run")]);

        var act = () => service.CaptureBrokerageSyncAsync(Projection(accountId), SourceAsOf);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different payload at the same source timestamp*");
        store.Saved.Should().BeEmpty(
            "all scope ownership and history checks must finish before the first append");
    }

    private static AccountingPositionSnapshotCaptureService CreateConfiguredService(
        Guid accountId,
        IPositionSnapshotStore snapshotStore,
        IReadOnlyList<DailyValuationScheduleWorkItem> dailyItems,
        IReadOnlyList<AutomatedJournalScheduleWorkItem> automatedItems,
        string accountCurrency = "USD")
    {
        var dailySource = Substitute.For<IDailyValuationPortfolioSource>();
        dailySource.ListAsync(Arg.Any<CancellationToken>()).Returns(dailyItems);
        var automatedSource = Substitute.For<IAutomatedJournalScheduleStore>();
        automatedSource.ListAsync(Arg.Any<CancellationToken>()).Returns(automatedItems);
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(Account(accountId, accountCurrency));
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>())
            .Returns(new LedgerBookRecord(
                BookId,
                "fund-alpha",
                FundId,
                FundStructureNodeKindDto.Fund,
                "Primary book",
                "USD",
                SourceAsOf.AddMonths(-1),
                SourceAsOf));
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync("fund-alpha", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-alpha", "tenant-a", "company-a"));
        return new AccountingPositionSnapshotCaptureService(
            snapshotStore,
            dailySource,
            automatedSource,
            accounts,
            ledger,
            tenancy);
    }

    private static DailyValuationScheduleWorkItem DailySchedule(Guid accountId, string runId)
        => new(
            "daily-schedule",
            "fund-alpha",
            "USD",
            "ops",
            BookId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            [],
            "closing-marks",
            "Closing marks",
            "Close",
            "controller",
            DateTimeOffset.UtcNow.AddDays(-1),
            "Daily valuation",
            EntityId: EntityId.ToString("D"),
            TenantId: "tenant-a",
            CompanyId: "company-a",
            PositionSnapshotScopes: [new DailyValuationPositionSnapshotScope(runId, accountId.ToString("D"))]);

    private static AutomatedJournalScheduleWorkItem DividendSchedule(Guid accountId, string runId)
        => new(
            "dividend-schedule",
            AutomatedJournalScheduleKind.DividendCapture,
            "fund-alpha",
            BookId,
            "2026-07",
            EntityId.ToString("D"),
            "USD",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            new DateOnly(2026, 8, 1),
            new TimeOnly(1, 0),
            "UTC",
            "ops",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            PositionSnapshotScopes: [new AutomatedJournalPositionSnapshotScope(runId, accountId.ToString("D"))]);

    private static AccountSummaryDto Account(Guid accountId, string baseCurrency = "USD")
        => new(
            AccountId: accountId,
            AccountType: AccountTypeDto.Brokerage,
            EntityId: EntityId,
            FundId: FundId,
            SleeveId: null,
            VehicleId: null,
            AccountCode: "BRK-001",
            DisplayName: "Primary brokerage",
            BaseCurrency: baseCurrency,
            Institution: "Alpaca",
            IsActive: true,
            EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-1),
            EffectiveTo: null,
            PortfolioId: null,
            LedgerReference: null,
            StrategyId: null,
            RunId: null);

    private static AccountSnapshotRecord Snapshot(
        Guid accountId,
        string runId,
        DateTimeOffset asOf,
        IReadOnlyList<PositionRecord>? positions = null)
    {
        positions ??= [new PositionRecord("AAPL", 100m, 180m, 750m, 0m)];
        return new AccountSnapshotRecord(
            runId,
            accountId.ToString("D"),
            "Primary brokerage",
            BrokerageAccountKindDto.TaxableBrokerage.ToString(),
            50_000m,
            0m,
            positions.Sum(static position => position.UnrealisedPnl),
            0m,
            positions,
            asOf,
            "tenant-a",
            "company-a",
            "fund-alpha",
            BookId,
            EntityId.ToString("D"));
    }

    private static FundAccountBrokerageSyncActivityDto Projection(
        Guid accountId,
        string balanceCurrency = "USD",
        string? positionCurrency = null,
        bool includeSecondPosition = false)
    {
        var now = DateTimeOffset.UtcNow;
        var link = new WorkstationBrokerageAccountLinkDto(
            accountId,
            "alpaca",
            "PA-123",
            "Primary brokerage",
            now.AddDays(-1),
            "ops",
            BrokerageAccountKindDto.TaxableBrokerage);
        var status = new WorkstationBrokerageSyncStatusDto(
            accountId,
            "alpaca",
            "PA-123",
            WorkstationBrokerageSyncHealth.Healthy,
            true,
            false,
            now,
            now,
            null,
            1,
            0,
            0,
            0,
            0,
            [],
            BrokerageAccountKindDto.TaxableBrokerage);
        var positions = new List<FundAccountBrokeragePositionDto>
        {
            new(
                "aapl",
                100m,
                180m,
                187.5m,
                18_750m,
                750m,
                "equity",
                null,
                Currency: positionCurrency)
        };
        if (includeSecondPosition)
        {
            positions.Add(new FundAccountBrokeragePositionDto(
                "MSFT",
                20m,
                400m,
                405m,
                8_100m,
                100m,
                "equity",
                null,
                Currency: positionCurrency));
        }

        return new FundAccountBrokerageSyncActivityDto(
            accountId,
            link,
            status,
            new FundAccountBrokerageBalanceSnapshotDto(50_000m, 125_000m, 95_000m, balanceCurrency, 0m),
            positions,
            [],
            [],
            [],
            now,
            "raw.json",
            "projection.json");
    }

    private sealed class RecordingPositionSnapshotStore : IPositionSnapshotStore
    {
        private readonly List<AccountSnapshotRecord> _retained = [];

        public List<AccountSnapshotRecord> Saved { get; } = [];

        public void Seed(AccountSnapshotRecord snapshot) => _retained.Add(snapshot);

        public async Task SaveSnapshotAsync(
            AccountSnapshotRecord snapshot,
            CancellationToken ct = default)
        {
            _ = await SaveSnapshotConditionallyAsync(snapshot, ct);
        }

        public Task<PositionSnapshotSaveOutcome> SaveSnapshotConditionallyAsync(
            AccountSnapshotRecord snapshot,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Saved.Add(snapshot);
            return Task.FromResult(PositionSnapshotSaveOutcome.Appended);
        }

        public Task<AccountSnapshotRecord?> GetLatestSnapshotAsync(
            string runId,
            string accountId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(AllSnapshots()
                .Where(snapshot => MatchesScope(snapshot, runId, accountId))
                .OrderByDescending(static snapshot => snapshot.AsOf)
                .FirstOrDefault());
        }

        public Task<AccountSnapshotRecord?> GetLatestSnapshotAsync(
            string runId,
            string accountId,
            PositionSnapshotOwnerScope ownerScope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(AllSnapshots()
                .Where(snapshot =>
                    MatchesScope(snapshot, runId, accountId) &&
                    MatchesOwner(snapshot, ownerScope))
                .OrderByDescending(static snapshot => snapshot.AsOf)
                .FirstOrDefault());
        }

        public async IAsyncEnumerable<AccountSnapshotRecord> GetSnapshotHistoryAsync(
            string runId,
            string accountId,
            DateTimeOffset from,
            DateTimeOffset to,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var snapshot in AllSnapshots().Where(snapshot =>
                         MatchesScope(snapshot, runId, accountId) &&
                         snapshot.AsOf >= from &&
                         snapshot.AsOf <= to))
            {
                ct.ThrowIfCancellationRequested();
                yield return snapshot;
                await Task.Yield();
            }
        }

        public async IAsyncEnumerable<AccountSnapshotRecord> GetSnapshotHistoryAsync(
            string runId,
            string accountId,
            PositionSnapshotOwnerScope ownerScope,
            DateTimeOffset from,
            DateTimeOffset to,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var snapshot in AllSnapshots().Where(snapshot =>
                         MatchesScope(snapshot, runId, accountId) &&
                         MatchesOwner(snapshot, ownerScope) &&
                         snapshot.AsOf >= from &&
                         snapshot.AsOf <= to))
            {
                ct.ThrowIfCancellationRequested();
                yield return snapshot;
                await Task.Yield();
            }
        }

        private IEnumerable<AccountSnapshotRecord> AllSnapshots() => _retained.Concat(Saved);

        private static bool MatchesScope(AccountSnapshotRecord snapshot, string runId, string accountId)
            => string.Equals(snapshot.RunId, runId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(snapshot.AccountId, accountId, StringComparison.OrdinalIgnoreCase);

        private static bool MatchesOwner(AccountSnapshotRecord snapshot, PositionSnapshotOwnerScope owner)
            => string.Equals(snapshot.TenantId, owner.TenantId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(snapshot.CompanyId, owner.CompanyId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(snapshot.FundProfileId, owner.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
               snapshot.LedgerBookId == owner.LedgerBookId &&
               string.Equals(snapshot.EntityId, owner.EntityId, StringComparison.OrdinalIgnoreCase);
    }
}
