using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;

namespace Meridian.Tests.Application.Backfill;

public sealed class BackfillExecutionHistoryTests
{
    [Fact]
    public void FileBackedHistory_NullExecutionArrayLoadsAsEmptyForCompatibility()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"meridian-backfill-history-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "history.json");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "{\"version\":1,\"executions\":null}");

            var history = new BackfillExecutionHistory(path);

            history.GetRecentExecutions().Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileBackedHistory_RestartPreservesTerminalTypedSlaAndLegacyWarnings()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"meridian-backfill-history-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "history.json");

        try
        {
            var dueAt = new DateTimeOffset(2026, 07, 13, 22, 00, 00, TimeSpan.Zero);
            var typed = new BackfillExecutionLog
            {
                ExecutionId = "typed-remediation",
                ScheduleId = "auto-gap-remediation",
                Trigger = ExecutionTrigger.AutoRemediation,
                Status = ExecutionStatus.Running,
                StartedAt = dueAt.AddMinutes(-30),
                FromDate = new DateOnly(2026, 07, 10),
                ToDate = new DateOnly(2026, 07, 11),
                Symbols = { "AAPL" },
                AutoRemediationAttemptCount = 2,
                AutoRemediationSla = new BackfillRemediationSlaMetadata(
                    BackfillRemediationSlaTier.SameBusinessDay,
                    dueAt,
                    RequiresOwnerAssignment: true,
                    DownstreamWorkflow: "accounting",
                    ReasonCode: "CriticalWorkflow",
                    Provider: "polygon",
                    TriggerSource: AutoRemediationTriggerSource.QualityAlert)
            };
            var legacy = new BackfillExecutionLog
            {
                ExecutionId = "legacy-remediation",
                ScheduleId = "auto-gap-remediation",
                Trigger = ExecutionTrigger.AutoRemediation,
                Status = ExecutionStatus.Running,
                FromDate = new DateOnly(2026, 07, 9),
                ToDate = new DateOnly(2026, 07, 9),
                Symbols = { "SPY" },
                Warnings =
                {
                    "provider=stooq",
                    "sla-tier=Standard",
                    $"sla-due-utc={dueAt.AddDays(1):O}"
                }
            };

            var beforeRestart = new BackfillExecutionHistory(path);
            beforeRestart.AddExecution(typed);
            beforeRestart.AddExecution(legacy);
            typed.Status = ExecutionStatus.Completed;
            typed.CompletedAt = dueAt.AddMinutes(-5);
            typed.AutoRemediationLastOutcome = AutoRemediationOutcome.Completed.ToString();
            beforeRestart.UpdateExecution(typed);

            var afterRestart = new BackfillExecutionHistory(path);
            var restoredTyped = afterRestart.GetExecution("typed-remediation");
            var restoredLegacy = afterRestart.GetExecution("legacy-remediation");

            restoredTyped.Should().NotBeNull();
            restoredTyped!.Status.Should().Be(ExecutionStatus.Completed);
            restoredTyped.AutoRemediationLastOutcome.Should().Be("Completed");
            restoredTyped.AutoRemediationSla.Should().BeEquivalentTo(typed.AutoRemediationSla);
            restoredLegacy.Should().NotBeNull();
            restoredLegacy!.Warnings.Should().Contain($"sla-due-utc={dueAt.AddDays(1):O}");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
