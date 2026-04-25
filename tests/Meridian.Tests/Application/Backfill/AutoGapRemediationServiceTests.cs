using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Scheduling;
using Meridian.Infrastructure.Adapters.Core;
using System.Net.Http;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

public sealed class AutoGapRemediationServiceTests
{
    [Fact]
    public async Task DuplicateTrigger_IsSuppressedByIdempotencyAndCooldown()
    {
        var gateway = new FakeGateway();
        var history = new BackfillExecutionHistory();
        var service = new AutoGapRemediationService(
            gateway,
            history,
            policy: new AutoGapRemediationPolicy(
                MinimumGapDuration: TimeSpan.FromMinutes(1),
                MinimumGapSize: 1,
                SymbolCooldown: TimeSpan.FromMinutes(30),
                ProviderCooldown: TimeSpan.Zero,
                MaxConcurrentRemediations: 2,
                DefaultProvider: "stooq"));

        var gap = new DataGap(
            Symbol: "AAPL",
            EventType: "Trade",
            GapStart: DateTimeOffset.UtcNow.AddMinutes(-10),
            GapEnd: DateTimeOffset.UtcNow.AddMinutes(-5),
            Duration: TimeSpan.FromMinutes(5),
            MissedSequenceStart: 1,
            MissedSequenceEnd: 10,
            EstimatedMissedEvents: 10,
            Severity: GapSeverity.Significant,
            PossibleCause: null);

        await service.HandleDataQualityGapAsync(gap);
        await service.HandleDataQualityGapAsync(gap);

        gateway.Calls.Should().Be(1);
        history.GetRecentExecutions(10).Should().HaveCount(1);
    }

    [Fact]
    public async Task TransientFailure_AllowsRetryForSameIdempotencyKey()
    {
        var gateway = new FakeGateway
        {
            Handler = call =>
            {
                if (call == 1)
                {
                    throw new HttpRequestException("Temporary upstream outage");
                }

                return new BackfillResult(
                    Success: true,
                    Provider: "stooq",
                    Symbols: new[] { "MSFT" },
                    From: new DateOnly(2026, 03, 20),
                    To: new DateOnly(2026, 03, 20),
                    BarsWritten: 5,
                    StartedUtc: DateTimeOffset.UtcNow.AddSeconds(-3),
                    CompletedUtc: DateTimeOffset.UtcNow);
            }
        };

        var history = new BackfillExecutionHistory();
        var service = new AutoGapRemediationService(
            gateway,
            history,
            policy: new AutoGapRemediationPolicy(
                MinimumGapDuration: TimeSpan.FromMinutes(1),
                MinimumGapSize: 1,
                SymbolCooldown: TimeSpan.Zero,
                ProviderCooldown: TimeSpan.Zero,
                MaxConcurrentRemediations: 2,
                DefaultProvider: "stooq"));

        var scanResult = new GapAnalysisResult
        {
            FromDate = new DateOnly(2026, 03, 20),
            ToDate = new DateOnly(2026, 03, 20),
            Granularity = DataGranularity.Daily,
            SymbolGaps =
            {
                ["MSFT"] = new SymbolGapInfo
                {
                    Symbol = "MSFT",
                    FromDate = new DateOnly(2026, 03, 20),
                    ToDate = new DateOnly(2026, 03, 20),
                    HasGaps = true,
                    GapDates = { new DateOnly(2026, 03, 20) }
                }
            }
        };

        await service.HandleGapAnalysisResultAsync(scanResult);
        await service.HandleGapAnalysisResultAsync(scanResult);

        gateway.Calls.Should().Be(2);
        history.GetRecentExecutions(10).Should().Contain(e => e.AutoRemediationLastOutcome == "FailedTransient");
        history.GetRecentExecutions(10).Should().Contain(e => e.AutoRemediationLastOutcome == "Completed");
    }

    private sealed class FakeGateway : IBackfillExecutionGateway
    {
        public int Calls { get; private set; }
        public Func<int, BackfillResult>? Handler { get; init; }

        public Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default)
        {
            Calls++;
            if (Handler is not null)
            {
                return Task.FromResult(Handler(Calls));
            }

            return Task.FromResult(new BackfillResult(
                Success: true,
                Provider: request.Provider,
                Symbols: request.Symbols,
                From: request.From,
                To: request.To,
                BarsWritten: 10,
                StartedUtc: DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedUtc: DateTimeOffset.UtcNow));
        }
    }
}
