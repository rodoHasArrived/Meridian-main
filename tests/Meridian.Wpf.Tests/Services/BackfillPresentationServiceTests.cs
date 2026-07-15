using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Backfill;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class BackfillPresentationServiceTests
{
    [Fact]
    public void BuildSymbolProgress_ShowsRangeProviderFallbackRetryAndLiveState()
    {
        var observedAt = new DateTimeOffset(2026, 07, 13, 16, 15, 00, TimeSpan.Zero);
        var symbol = new BackfillProviderSymbolProgressDto(
            Symbol: "AAPL",
            RangeStart: new DateOnly(2026, 07, 10),
            RangeEnd: new DateOnly(2026, 07, 11),
            TotalDays: 2,
            CompletedDays: 1,
            PercentComplete: 50,
            IsCompleted: false,
            IsFailed: false,
            IsSkipped: false,
            CurrentProvider: "polygon",
            CurrentStatus: "Downloading",
            ProviderAttempt: 2,
            RetryRound: 1,
            Operation: "historical-bars",
            AttemptStartedAt: observedAt.AddMinutes(-1),
            LastUpdatedAt: observedAt,
            Error: null);
        var attempt = new BackfillProviderAttemptProgressDto(
            "AAPL", "polygon", symbol.RangeStart, symbol.RangeEnd, 2, 1,
            "historical-bars", "Downloading", 125, observedAt.AddMinutes(-1), observedAt, null);
        var snapshot = new BackfillProviderProgressSnapshotDto(
            new Dictionary<string, BackfillProviderSymbolProgressDto> { ["AAPL"] = symbol },
            [attempt], 50, 1, 0, 0, 0, observedAt);

        var rows = BackfillPresentationService.BuildSymbolProgress(
            new BackfillRunProgressResponse(null, true, snapshot, observedAt));

        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Symbol = "AAPL",
            RangeText = "2026-07-10 — 2026-07-11",
            CurrentProvider = "polygon",
            FallbackAttemptText = "Fallback · attempt 2",
            RetryText = "Retry 1",
            ProgressText = "50.0%",
            BarsText = "125 bars",
            LiveState = "Downloading"
        });
    }

    [Fact]
    public void BuildRemediationQueue_UsesEnvelopeDefaultProviderAndSlaSortKeys()
    {
        var dueAt = new DateTimeOffset(2026, 07, 13, 18, 00, 00, TimeSpan.Zero);
        var response = new BackfillExecutionHistoryResponse
        {
            AutoRemediation = new BackfillAutoRemediationSummary { DefaultProvider = "polygon" },
            Executions =
            [
                new BackfillExecution
                {
                    Id = "open",
                    Trigger = "AutoRemediation",
                    Symbols = ["MSFT"],
                    AutoRemediationSla = new BackfillRemediationSlaDto
                    {
                        Tier = BackfillRemediationSlaTierDto.Standard,
                        Status = BackfillRemediationSlaStatusDto.Open,
                        DueAtUtc = dueAt,
                        Provider = string.Empty
                    }
                },
                new BackfillExecution
                {
                    Id = "overdue",
                    Trigger = "AutoRemediation",
                    Symbols = ["AAPL"],
                    AutoRemediationSla = new BackfillRemediationSlaDto
                    {
                        Tier = BackfillRemediationSlaTierDto.SameBusinessDay,
                        Status = BackfillRemediationSlaStatusDto.Overdue,
                        DueAtUtc = dueAt.AddHours(-1),
                        Provider = "stooq",
                        IsCompatibilityDerived = true
                    }
                }
            ]
        };

        var rows = BackfillPresentationService.BuildRemediationQueue(response);

        rows.Select(row => row.ExecutionId).Should().Equal("overdue", "open");
        rows[0].SlaTierSort.Should().Be(0);
        rows[0].SlaStatusSort.Should().Be(0);
        rows[0].IsCompatibilityDerived.Should().BeTrue();
        rows[1].Provider.Should().Be("polygon");
    }
}
