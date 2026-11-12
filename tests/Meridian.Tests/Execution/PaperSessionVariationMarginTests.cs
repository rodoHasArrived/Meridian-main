using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// Daily variation-margin settlements are durable session records: they persist before the book
/// changes, apply once per trading day, and every path that rebuilds a session book (live
/// projection, restart and replay) applies them in the same place among the fills.
/// </summary>
public sealed class PaperSessionVariationMarginTests : IDisposable
{
    private const string Es = "ESZ6";
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MondayClose = new(2026, 11, 2, 21, 0, 0, TimeSpan.Zero);
    private readonly string _tempDir;

    public PaperSessionVariationMarginTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "meridian_vm_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temporary session store.
        }
    }

    [Fact]
    public async Task Settlement_PersistsOnce_AndEveryRebuildAgrees()
    {
        var store = Store();
        await using var service = Service(store);
        var session = await service.CreateSessionAsync(new CreatePaperSessionDto("vm", null, 1_000_000m));
        await service.RecordFillAsync(session.SessionId, Future(OrderSide.Buy, 6_000m, Monday));

        var settlement = PaperSessionSettlementRecord.Create(
            PaperTradingPortfolio.DefaultAccountId, Es, new DateOnly(2026, 11, 2), 6_010m, MondayClose);
        (await service.RecordSettlementAsync(session.SessionId, settlement)).Should().BeTrue();
        service.GetActivePortfolio(session.SessionId)!.Cash.Should().Be(1_000_500m);

        (await service.RecordSettlementAsync(session.SessionId, settlement)).Should().BeFalse("the day is already settled");
        var conflicting = () => service.RecordSettlementAsync(session.SessionId, settlement with { SettlementPrice = 6_011m });
        await conflicting.Should().ThrowAsync<InvalidDataException>();
        service.GetLastSettlementDate(session.SessionId, PaperTradingPortfolio.DefaultAccountId, Es)
            .Should().Be(new DateOnly(2026, 11, 2));

        await service.RecordFillAsync(session.SessionId, Future(OrderSide.Sell, 6_020m, Monday.AddDays(1)));
        service.GetActivePortfolio(session.SessionId)!.Cash.Should().Be(1_001_000m);

        var replayed = await service.ReplaySessionAsync(session.SessionId);
        replayed!.Cash.Should().Be(1_001_000m);

        await using var restarted = Service(Store());
        await restarted.InitialiseAsync();
        restarted.GetActivePortfolio(session.SessionId)!.Cash.Should().Be(1_001_000m);
        (await store.LoadSettlementsAsync(session.SessionId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Settlement_IsAppliedBeforeLaterFills_EvenWhenRecordedFirst()
    {
        await using var service = Service(Store());
        var session = await service.CreateSessionAsync(new CreatePaperSessionDto("vm", null, 1_000_000m));
        await service.RecordFillAsync(session.SessionId, Future(OrderSide.Buy, 6_000m, Monday));
        await service.RecordSettlementAsync(session.SessionId, PaperSessionSettlementRecord.Create(
            PaperTradingPortfolio.DefaultAccountId, Es, new DateOnly(2026, 11, 2), 6_010m, MondayClose));

        // A second contract bought on Tuesday must not receive Monday's settlement.
        await service.RecordFillAsync(session.SessionId, Future(OrderSide.Buy, 6_012m, Monday.AddDays(1)));

        var position = service.GetActivePortfolio(session.SessionId)!.GetOpenVariationMarginPositions().Single();
        position.Quantity.Should().Be(2m);
        position.LastSettlementPrice.Should().Be(6_011m);
        service.GetActivePortfolio(session.SessionId)!.Cash.Should().Be(1_000_500m);
    }

    [Fact]
    public async Task CorruptSettlementLine_FailsTheLoad_InsteadOfDroppingCash()
    {
        var store = Store();
        await using var service = Service(store);
        var session = await service.CreateSessionAsync(new CreatePaperSessionDto("vm", null, 1_000_000m));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, session.SessionId, "settlements.jsonl"), "{not json\n");

        var load = () => store.LoadSettlementsAsync(session.SessionId);

        await load.Should().ThrowAsync<InvalidDataException>().WithMessage("*corrupt settlement*");
    }

    [Fact]
    public async Task StoreWithoutSettlementSupport_FailsClosed()
    {
        IPaperSessionStore legacy = new NoSettlementStore();
        var record = PaperSessionSettlementRecord.Create(
            PaperTradingPortfolio.DefaultAccountId, Es, new DateOnly(2026, 11, 2), 6_010m, MondayClose);

        var append = () => legacy.TryAppendSettlementAsync("s", record);

        await append.Should().ThrowAsync<NotSupportedException>();
        (await legacy.LoadSettlementsAsync("s")).Should().BeEmpty();
    }

    private JsonlFilePaperSessionStore Store() => new(_tempDir, NullLogger<JsonlFilePaperSessionStore>.Instance);

    private static PaperSessionPersistenceService Service(IPaperSessionStore store) =>
        new(NullLogger<PaperSessionPersistenceService>.Instance, store);

    // The OMS stamps the booking model before persisting a fill; session records replay as stamped.
    private static ExecutionReport Future(OrderSide side, decimal price, DateTimeOffset at) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ReportType = ExecutionReportType.Fill,
        Symbol = Es,
        Side = side,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = 1m,
        FilledQuantity = 1m,
        FillPrice = price,
        Commission = 0m,
        Timestamp = at,
        AssetClass = "Future",
        LotUnitKind = SecurityLotUnitKind.Contracts,
        LotMultiplier = 50m,
        BookedMoneyMultiplier = 50m,
        BookedVariationMargin = true
    };

    private sealed class NoSettlementStore : IPaperSessionStore
    {
        public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default) => Task.CompletedTask;
        public Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveLedgerJournalAsync(string sessionId, IReadOnlyList<PersistedJournalEntryDto> entries, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([]);
        public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutionReport>>([]);
        public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OrderState>>([]);
        public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
    }
}
