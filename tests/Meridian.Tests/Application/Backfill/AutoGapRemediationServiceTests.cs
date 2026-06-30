using System.Net.Http;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Application.Scheduling;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;
using AppBackfillRequest = Meridian.Application.Backfill.BackfillRequest;
using QualityDataGap = Meridian.DataIntegration.Monitoring.DataQuality.DataGap;
using QualityGapSeverity = Meridian.DataIntegration.Monitoring.DataQuality.GapSeverity;
using StorageGapAnalysisResult = Meridian.Infrastructure.Adapters.Core.GapAnalysisResult;
using Meridian.Contracts.Backfill;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Guards automatic gap remediation for provider interruption and batch gap-scan recovery scenarios.
/// </summary>
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

        var gap = new QualityDataGap(
            Symbol: "AAPL",
            EventType: "Trade",
            GapStart: DateTimeOffset.UtcNow.AddMinutes(-10),
            GapEnd: DateTimeOffset.UtcNow.AddMinutes(-5),
            Duration: TimeSpan.FromMinutes(5),
            MissedSequenceStart: 1,
            MissedSequenceEnd: 10,
            EstimatedMissedEvents: 10,
            Severity: QualityGapSeverity.Significant,
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

        var scanResult = new StorageGapAnalysisResult
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

    [Fact]
    public async Task Scenario_MultiSymbolGapScan_BatchesDeterministicRequestAndHistory()
    {
        var gateway = new FakeGateway();
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

        var gapDate = new DateOnly(2026, 06, 29);
        var scanResult = new StorageGapAnalysisResult
        {
            FromDate = gapDate,
            ToDate = gapDate,
            Granularity = DataGranularity.Daily,
            SymbolGaps =
            {
                ["msft"] = BuildSingleDayGap("msft", gapDate),
                ["AAPL"] = BuildSingleDayGap("AAPL", gapDate)
            }
        };

        await service.HandleGapAnalysisResultAsync(scanResult, provider: "polygon");

        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.Provider.Should().Be("polygon");
        request.Symbols.Should().Equal("AAPL", "MSFT");
        request.From.Should().Be(gapDate);
        request.To.Should().Be(gapDate);

        var execution = history.GetRecentExecutions(10).Should().ContainSingle().Subject;
        execution.Symbols.Should().Equal("AAPL", "MSFT");
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.Statistics.TotalSymbols.Should().Be(2);
        execution.Statistics.SuccessfulSymbols.Should().Be(2);
        execution.AutoRemediationIdempotencyKey.Should().Be("AAPL,MSFT|polygon|2026-06-29|2026-06-29");
        execution.Warnings.Should().Contain("source=GapAnalyzerScan");
        execution.Warnings.Should().Contain("provider=polygon");
    }

    private sealed class FakeGateway : IBackfillExecutionGateway
    {
        public int Calls { get; private set; }
        public List<AppBackfillRequest> Requests { get; } = new();
        public Func<int, BackfillResult>? Handler { get; init; }

        public Task<BackfillResult> RunAsync(AppBackfillRequest request, CancellationToken ct = default)
        {
            Calls++;
            Requests.Add(request);

            if (Handler is not null)
            {
                return Task.FromResult(Handler(Calls));
            }

            return Task.FromResult(new BackfillResult(
                Success: true,
                Provider: request.Provider,
                Symbols: request.Symbols.ToArray(),
                From: request.From,
                To: request.To,
                BarsWritten: 10,
                StartedUtc: DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedUtc: DateTimeOffset.UtcNow));
        }
    }

    private static SymbolGapInfo BuildSingleDayGap(string symbol, DateOnly gapDate)
    {
        return new SymbolGapInfo
        {
            Symbol = symbol,
            FromDate = gapDate,
            ToDate = gapDate,
            HasGaps = true,
            GapDates = { gapDate }
        };
    }
}
