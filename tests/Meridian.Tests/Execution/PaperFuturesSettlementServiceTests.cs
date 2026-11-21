using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// The end-of-day job settles each open paper futures position once per closed NYSE trading day,
/// at that day's mark, catching up missed days oldest first and never settling on a missing price.
/// One E-mini contract (multiplier 50) bought on Monday 2 November 2026 at 6,000.
/// </summary>
public sealed class PaperFuturesSettlementServiceTests
{
    private const string Es = "ESZ6";
    private static readonly DateTimeOffset MondayMorning = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WednesdayEvening = new(2026, 11, 4, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunOnce_SettlesEachClosedDay_SkipsAMissingPrice_AndIsIdempotent()
    {
        var prices = new FakeMarkPriceSource
        {
            [new DateOnly(2026, 11, 2)] = 6_010m,
            [new DateOnly(2026, 11, 4)] = 6_020m
        };
        var (service, persistence, sessionId) = await ArrangeAsync(prices, WednesdayEvening, enabled: true);

        var applied = await service.RunOnceAsync();

        applied.Should().Be(2, "Monday and Wednesday settle; Tuesday has no price and is left alone");
        persistence.GetActivePortfolio(sessionId)!.Cash.Should().Be(1_001_000m, "(6,020 - 6,000) x 50");
        persistence.GetLastSettlementDate(sessionId, PaperTradingPortfolio.DefaultAccountId, Es)
            .Should().Be(new DateOnly(2026, 11, 4));

        (await service.RunOnceAsync()).Should().Be(0, "every closed day up to now is settled");
    }

    [Fact]
    public async Task RunOnce_DoesNotSettleADayBeforeItsClose()
    {
        var prices = new FakeMarkPriceSource { [new DateOnly(2026, 11, 2)] = 6_010m };
        var beforeMondayClose = new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero);
        var (service, persistence, sessionId) = await ArrangeAsync(prices, beforeMondayClose, enabled: true);

        (await service.RunOnceAsync()).Should().Be(0);
        persistence.GetActivePortfolio(sessionId)!.Cash.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task RunOnce_DoesNothing_WhenDisabled()
    {
        var prices = new FakeMarkPriceSource { [new DateOnly(2026, 11, 2)] = 6_010m };
        var (service, persistence, sessionId) = await ArrangeAsync(prices, WednesdayEvening, enabled: false);

        (await service.RunOnceAsync()).Should().Be(0);
        persistence.GetActivePortfolio(sessionId)!.Cash.Should().Be(1_000_000m);
    }

    private static async Task<(PaperFuturesSettlementService Service, PaperSessionPersistenceService Persistence, string SessionId)> ArrangeAsync(
        FakeMarkPriceSource prices,
        DateTimeOffset now,
        bool enabled)
    {
        var persistence = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto("vm-eod", null, 1_000_000m));
        await persistence.RecordFillAsync(session.SessionId, new ExecutionReport
        {
            OrderId = "es-open",
            ReportType = ExecutionReportType.Fill,
            Symbol = Es,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            FillPrice = 6_000m,
            Commission = 0m,
            Timestamp = MondayMorning,
            AssetClass = "Future",
            LotUnitKind = SecurityLotUnitKind.Contracts,
            LotMultiplier = 50m,
            BookedMoneyMultiplier = 50m,
            BookedVariationMargin = true
        });

        var provider = new ServiceCollection()
            .AddSingleton(persistence)
            .AddSingleton<IMarkPriceSource>(prices)
            .BuildServiceProvider();
        var service = new PaperFuturesSettlementService(
            provider,
            Microsoft.Extensions.Options.Options.Create(new PaperFuturesSettlementOptions { Enabled = enabled }),
            NullLogger<PaperFuturesSettlementService>.Instance,
            new FixedTimeProvider(now));
        return (service, persistence, session.SessionId);
    }

    private sealed class FakeMarkPriceSource : Dictionary<DateOnly, decimal>, IMarkPriceSource
    {
        public Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(TryGetValue(asOf, out var price)
                ? new MarkPriceQuote(price, "test-close", $"test:{symbol}:{asOf:yyyy-MM-dd}", PriceAsOf: asOf)
                : null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
