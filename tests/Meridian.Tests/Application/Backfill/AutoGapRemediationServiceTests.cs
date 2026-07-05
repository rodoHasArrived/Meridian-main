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
    public void BackfillRemediationSlaPolicy_CriticalWorkflow_RequiresSameBusinessDayOwnerAssignment()
    {
        var observedAt = new DateTimeOffset(2026, 06, 30, 14, 00, 00, TimeSpan.Zero);
        var policy = new BackfillRemediationSlaPolicy(
            StandardWindow: TimeSpan.FromHours(48),
            SameBusinessDayWindow: TimeSpan.FromHours(8));

        var decision = policy.Classify(
            AutoRemediationTriggerSource.QualityAlert,
            severity: "Critical",
            downstreamWorkflow: "Accounting Close",
            observedAtUtc: observedAt);

        decision.Tier.Should().Be(BackfillRemediationSlaTier.SameBusinessDay);
        decision.RequiresOwnerAssignment.Should().BeTrue();
        decision.DownstreamWorkflow.Should().Be("accounting close");
        decision.ReasonCode.Should().Be("CriticalWorkflow");
        decision.DueAtUtc.Should().Be(observedAt.AddHours(8));
    }

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
    public async Task ProviderCooldown_NormalizesProviderNameAcrossSymbolsAndRetainedEvidence()
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
                ProviderCooldown: TimeSpan.FromMinutes(30),
                MaxConcurrentRemediations: 2,
                DefaultProvider: "stooq"));

        await service.HandleQualityAlertAsync(new QualityAlertRemediationSignal(
            Symbol: "AAPL",
            From: new DateOnly(2026, 06, 29),
            To: new DateOnly(2026, 06, 29),
            Provider: " Polygon ",
            AlertId: "dq-provider-001",
            Reason: "missing-close-bar"));
        await service.HandleQualityAlertAsync(new QualityAlertRemediationSignal(
            Symbol: "MSFT",
            From: new DateOnly(2026, 06, 29),
            To: new DateOnly(2026, 06, 29),
            Provider: "polygon",
            AlertId: "dq-provider-002",
            Reason: "missing-close-bar"));

        gateway.Calls.Should().Be(1);
        gateway.Requests.Should().ContainSingle().Which.Provider.Should().Be("polygon");

        var execution = history.GetRecentExecutions(10).Should().ContainSingle().Subject;
        execution.AutoRemediationIdempotencyKey.Should().Be("AAPL|polygon|2026-06-29|2026-06-29");
        execution.AutoRemediationSla.Should().NotBeNull();
        execution.AutoRemediationSla!.Provider.Should().Be("polygon");
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
        var laterGapDate = gapDate.AddDays(7);
        var scanResult = new StorageGapAnalysisResult
        {
            FromDate = gapDate,
            ToDate = laterGapDate,
            Granularity = DataGranularity.Daily,
            SymbolGaps =
            {
                ["msft"] = BuildGap("msft", gapDate, laterGapDate),
                ["AAPL"] = BuildGap("AAPL", gapDate)
            }
        };

        await service.HandleGapAnalysisResultAsync(scanResult, provider: "polygon");

        gateway.Requests.Should().HaveCount(2);
        var request = gateway.Requests.Should().ContainSingle(r => r.From == gapDate && r.To == gapDate).Subject;
        request.Provider.Should().Be("polygon");
        request.Symbols.Should().Equal("AAPL", "MSFT");
        request.From.Should().Be(gapDate);
        request.To.Should().Be(gapDate);

        var execution = history.GetRecentExecutions(10)
            .Should()
            .ContainSingle(e => e.FromDate == gapDate && e.ToDate == gapDate)
            .Subject;
        execution.Symbols.Should().Equal("AAPL", "MSFT");
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.Statistics.TotalSymbols.Should().Be(2);
        execution.Statistics.SuccessfulSymbols.Should().Be(2);
        execution.AutoRemediationIdempotencyKey.Should().Be("AAPL,MSFT|polygon|2026-06-29|2026-06-29");
        execution.AutoRemediationTriggerReason.Should().Contain("scan:Daily:1");
        execution.AutoRemediationTriggerReason.Should().Contain("scan:Daily:2");
        execution.AutoRemediationSla.Should().NotBeNull();
        execution.AutoRemediationSla!.TriggerSource.Should().Be(AutoRemediationTriggerSource.GapAnalyzerScan);
        execution.AutoRemediationSla.Provider.Should().Be("polygon");
        execution.AutoRemediationSla.Tier.Should().Be(BackfillRemediationSlaTier.Standard);
        execution.AutoRemediationSla.RequiresOwnerAssignment.Should().BeFalse();
        execution.AutoRemediationSla.DownstreamWorkflow.Should().Be("unassigned");
        execution.Warnings.Should().BeEmpty("SLA metadata must be typed, not smuggled through warnings");
    }

    [Fact]
    public async Task Scenario_CriticalQualityAlert_RetainsSameBusinessDaySlaMetadata()
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
                DefaultProvider: "stooq"),
            slaPolicy: new BackfillRemediationSlaPolicy(
                StandardWindow: TimeSpan.FromHours(48),
                SameBusinessDayWindow: TimeSpan.FromHours(8)));

        await service.HandleQualityAlertAsync(new QualityAlertRemediationSignal(
            Symbol: "SPY",
            From: new DateOnly(2026, 06, 29),
            To: new DateOnly(2026, 06, 29),
            Provider: "polygon",
            AlertId: "dq-001",
            Reason: "missing-close-bar",
            GapSize: 1,
            Severity: "Critical",
            DownstreamWorkflow: "accounting"));

        var execution = history.GetRecentExecutions(10).Should().ContainSingle().Subject;
        execution.Symbols.Should().Equal("SPY");
        execution.Status.Should().Be(ExecutionStatus.Completed);
        execution.AutoRemediationTriggerReason.Should().Be("alert:dq-001:missing-close-bar");
        execution.AutoRemediationSla.Should().NotBeNull();
        execution.AutoRemediationSla!.TriggerSource.Should().Be(AutoRemediationTriggerSource.QualityAlert);
        execution.AutoRemediationSla.Provider.Should().Be("polygon");
        execution.AutoRemediationSla.Tier.Should().Be(BackfillRemediationSlaTier.SameBusinessDay);
        execution.AutoRemediationSla.RequiresOwnerAssignment.Should().BeTrue();
        execution.AutoRemediationSla.DownstreamWorkflow.Should().Be("accounting");
        execution.AutoRemediationSla.ReasonCode.Should().Be("CriticalWorkflow");
        execution.AutoRemediationSla.DueAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task EvaluateRemediationSla_CriticalFailedAlert_ReturnsOverdueOwnerEscalation()
    {
        var gateway = new FakeGateway
        {
            Handler = _ => throw new HttpRequestException("Polygon outage")
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
                DefaultProvider: "stooq"),
            slaPolicy: new BackfillRemediationSlaPolicy(
                StandardWindow: TimeSpan.FromHours(48),
                SameBusinessDayWindow: TimeSpan.FromMinutes(15)));

        await service.HandleQualityAlertAsync(new QualityAlertRemediationSignal(
            Symbol: "spy",
            From: new DateOnly(2026, 06, 29),
            To: new DateOnly(2026, 06, 29),
            Provider: "polygon",
            AlertId: "dq-002",
            Reason: "missing-close-bar",
            GapSize: 1,
            Severity: "Critical",
            DownstreamWorkflow: "Accounting Close"));

        var snapshot = service.EvaluateRemediationSla(DateTimeOffset.UtcNow.AddMinutes(30));

        snapshot.Total.Should().Be(1);
        snapshot.OverdueCount.Should().Be(1);
        snapshot.RequiresOwnerAssignmentCount.Should().Be(1);

        var item = snapshot.Items.Should().ContainSingle().Subject;
        item.Status.Should().Be(BackfillRemediationSlaStatus.Overdue);
        item.ExecutionStatus.Should().Be(ExecutionStatus.Failed);
        item.LastOutcome.Should().Be(AutoRemediationOutcome.FailedTransient.ToString());
        item.Tier.Should().Be(BackfillRemediationSlaTier.SameBusinessDay);
        item.RequiresOwnerAssignment.Should().BeTrue();
        item.DownstreamWorkflow.Should().Be("accounting close");
        item.ReasonCode.Should().Be("CriticalWorkflow");
        item.Provider.Should().Be("polygon");
        item.Symbols.Should().Equal("SPY");
        item.IdempotencyKey.Should().Be("SPY|polygon|2026-06-29|2026-06-29");
        item.TimeRemaining.Should().BeLessThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task EvaluateRemediationSla_CompletedAlert_DoesNotCountAsOverdue()
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
                DefaultProvider: "stooq"),
            slaPolicy: new BackfillRemediationSlaPolicy(
                StandardWindow: TimeSpan.FromHours(48),
                SameBusinessDayWindow: TimeSpan.FromMinutes(15)));

        await service.HandleQualityAlertAsync(new QualityAlertRemediationSignal(
            Symbol: "SPY",
            From: new DateOnly(2026, 06, 29),
            To: new DateOnly(2026, 06, 29),
            Provider: "polygon",
            AlertId: "dq-003",
            Reason: "missing-close-bar",
            GapSize: 1,
            Severity: "Critical",
            DownstreamWorkflow: "Accounting"));

        var snapshot = service.EvaluateRemediationSla(DateTimeOffset.UtcNow.AddMinutes(30));

        snapshot.Total.Should().Be(1);
        snapshot.OverdueCount.Should().Be(0);
        snapshot.DueSoonCount.Should().Be(0);
        snapshot.RequiresOwnerAssignmentCount.Should().Be(0);

        var item = snapshot.Items.Should().ContainSingle().Subject;
        item.Status.Should().Be(BackfillRemediationSlaStatus.Completed);
        item.ExecutionStatus.Should().Be(ExecutionStatus.Completed);
        item.RequiresOwnerAssignment.Should().BeTrue();
        item.TimeRemaining.Should().BeLessThan(TimeSpan.Zero);
    }

    [Fact]
    public void EvaluateRemediationSla_LegacyWarningMetadata_StillProducesSlaItem()
    {
        var gateway = new FakeGateway();
        var history = new BackfillExecutionHistory();
        var service = new AutoGapRemediationService(gateway, history);

        var dueAt = DateTimeOffset.UtcNow.AddHours(-1);
        history.AddExecution(new BackfillExecutionLog
        {
            ScheduleId = "auto-gap-remediation",
            Trigger = ExecutionTrigger.AutoRemediation,
            Status = ExecutionStatus.Running,
            FromDate = new DateOnly(2026, 06, 29),
            ToDate = new DateOnly(2026, 06, 29),
            Symbols = { "SPY" },
            Warnings =
            {
                "provider=polygon",
                "sla-tier=SameBusinessDay",
                $"sla-due-utc={dueAt:O}",
                "sla-requires-owner=true",
                "downstream-workflow=accounting",
                "sla-reason=CriticalWorkflow"
            }
        });

        var snapshot = service.EvaluateRemediationSla();

        var item = snapshot.Items.Should().ContainSingle(
            "pre-upgrade executions with warning-based SLA metadata must still surface").Subject;
        item.Tier.Should().Be(BackfillRemediationSlaTier.SameBusinessDay);
        item.Status.Should().Be(BackfillRemediationSlaStatus.Overdue);
        item.Provider.Should().Be("polygon");
        item.RequiresOwnerAssignment.Should().BeTrue();
        item.ReasonCode.Should().Be("CriticalWorkflow");
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

    private static SymbolGapInfo BuildGap(string symbol, params DateOnly[] gapDates)
    {
        var info = new SymbolGapInfo
        {
            Symbol = symbol,
            FromDate = gapDates.Min(),
            ToDate = gapDates.Max(),
            HasGaps = true
        };

        info.GapDates.AddRange(gapDates);
        return info;
    }
}
