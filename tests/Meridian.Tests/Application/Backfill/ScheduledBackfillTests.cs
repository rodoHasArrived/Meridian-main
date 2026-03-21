using FluentAssertions;
using Meridian.Application.Scheduling;
using Meridian.Core.Scheduling;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Unit tests for the cron expression parser.
/// </summary>
public class CronExpressionParserTests
{
    #region Validation Tests

    [Theory]
    [InlineData("0 2 * * *")]        // Daily at 2am
    [InlineData("0 3 * * 0")]        // Weekly Sunday at 3am
    [InlineData("30 6 * * 1-5")]     // Weekdays at 6:30am
    [InlineData("0 0 1 * *")]        // First day of month
    [InlineData("*/15 * * * *")]     // Every 15 minutes
    [InlineData("0 0 * * *")]        // Midnight daily
    [InlineData("0 23 * * 1-5")]     // Weekdays at 11pm
    public void IsValid_ValidExpressions_ReturnsTrue(string cronExpression)
    {
        // Act & Assert
        CronExpressionParser.IsValid(cronExpression).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0 2 * *")]           // Only 4 fields
    [InlineData("0 2 * * * *")]       // 6 fields
    [InlineData("60 2 * * *")]        // Invalid minute (60)
    [InlineData("0 24 * * *")]        // Invalid hour (24)
    [InlineData("0 2 32 * *")]        // Invalid day (32)
    [InlineData("0 2 * 13 *")]        // Invalid month (13)
    [InlineData("0 2 * * 7")]         // Invalid day of week (7)
    [InlineData("abc")]               // Completely invalid
    public void IsValid_InvalidExpressions_ReturnsFalse(string cronExpression)
    {
        // Act & Assert
        CronExpressionParser.IsValid(cronExpression).Should().BeFalse();
    }

    #endregion

    #region Parsing Tests

    [Fact]
    public void TryParse_DailyAt2am_ParsesCorrectly()
    {
        // Arrange
        var cron = "0 2 * * *";

        // Act
        var success = CronExpressionParser.TryParse(cron, out var schedule);

        // Assert
        success.Should().BeTrue();
        schedule.Minutes.Should().ContainSingle().Which.Should().Be(0);
        schedule.Hours.Should().ContainSingle().Which.Should().Be(2);
        schedule.DaysOfMonth.Should().HaveCount(31);
        schedule.Months.Should().HaveCount(12);
        schedule.DaysOfWeek.Should().HaveCount(7);
    }

    [Fact]
    public void TryParse_WeeklySunday_ParsesCorrectly()
    {
        // Arrange
        var cron = "0 3 * * 0";

        // Act
        var success = CronExpressionParser.TryParse(cron, out var schedule);

        // Assert
        success.Should().BeTrue();
        schedule.DaysOfWeek.Should().ContainSingle().Which.Should().Be(0); // Sunday
    }

    [Fact]
    public void TryParse_RangeExpression_ParsesCorrectly()
    {
        // Arrange
        var cron = "0 9 * * 1-5"; // Weekdays

        // Act
        var success = CronExpressionParser.TryParse(cron, out var schedule);

        // Assert
        success.Should().BeTrue();
        schedule.DaysOfWeek.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void TryParse_StepExpression_ParsesCorrectly()
    {
        // Arrange
        var cron = "*/15 * * * *"; // Every 15 minutes

        // Act
        var success = CronExpressionParser.TryParse(cron, out var schedule);

        // Assert
        success.Should().BeTrue();
        schedule.Minutes.Should().BeEquivalentTo(new[] { 0, 15, 30, 45 });
    }

    [Fact]
    public void TryParse_ListExpression_ParsesCorrectly()
    {
        // Arrange
        var cron = "0 6,12,18 * * *"; // 6am, 12pm, 6pm

        // Act
        var success = CronExpressionParser.TryParse(cron, out var schedule);

        // Assert
        success.Should().BeTrue();
        schedule.Hours.Should().BeEquivalentTo(new[] { 6, 12, 18 });
    }

    #endregion

    #region Next Occurrence Tests

    [Fact]
    public void GetNextOccurrence_DailyAt2am_ReturnsNextDay()
    {
        // Arrange
        var cron = "0 2 * * *";
        var from = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);

        // Act
        var next = CronExpressionParser.GetNextOccurrence(cron, TimeZoneInfo.Utc, from);

        // Assert
        next.Should().NotBeNull();
        next!.Value.Hour.Should().Be(2);
        next.Value.Minute.Should().Be(0);
        next.Value.Day.Should().Be(16); // Next day
    }

    [Fact]
    public void GetNextOccurrence_DailyAt2am_BeforeTime_ReturnsSameDay()
    {
        // Arrange
        var cron = "0 2 * * *";
        var from = new DateTimeOffset(2024, 1, 15, 1, 0, 0, TimeSpan.Zero);

        // Act
        var next = CronExpressionParser.GetNextOccurrence(cron, TimeZoneInfo.Utc, from);

        // Assert
        next.Should().NotBeNull();
        next!.Value.Hour.Should().Be(2);
        next.Value.Day.Should().Be(15); // Same day
    }

    [Fact]
    public void GetNextOccurrence_WeeklySunday_ReturnsNextSunday()
    {
        // Arrange
        var cron = "0 3 * * 0";
        var from = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero); // Monday

        // Act
        var next = CronExpressionParser.GetNextOccurrence(cron, TimeZoneInfo.Utc, from);

        // Assert
        next.Should().NotBeNull();
        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        next.Value.Day.Should().Be(21); // Next Sunday
    }

    [Fact]
    public void GetNextOccurrence_InvalidExpression_ReturnsNull()
    {
        // Arrange
        var cron = "invalid";

        // Act
        var next = CronExpressionParser.GetNextOccurrence(cron, TimeZoneInfo.Utc, DateTimeOffset.UtcNow);

        // Assert
        next.Should().BeNull();
    }

    #endregion

    #region Description Tests

    [Fact]
    public void GetDescription_ValidExpression_ReturnsDescription()
    {
        // Arrange
        var cron = "0 2 * * *";

        // Act
        var description = CronExpressionParser.GetDescription(cron);

        // Assert
        description.Should().Contain("02:00");
    }

    [Fact]
    public void GetDescription_InvalidExpression_ReturnsError()
    {
        // Arrange
        var cron = "invalid";

        // Act
        var description = CronExpressionParser.GetDescription(cron);

        // Assert
        description.Should().Contain("Invalid");
    }

    #endregion
}

/// <summary>
/// Unit tests for the BackfillSchedule class.
/// </summary>
public class BackfillScheduleTests
{
    #region Creation Tests

    [Fact]
    public void Constructor_DefaultValues_SetsDefaults()
    {
        // Act
        var schedule = new BackfillSchedule();

        // Assert
        schedule.ScheduleId.Should().NotBeNullOrEmpty();
        schedule.Enabled.Should().BeTrue();
        schedule.CronExpression.Should().Be("0 2 * * *");
        schedule.TimeZoneId.Should().Be("UTC");
        schedule.BackfillType.Should().Be(ScheduledBackfillType.GapFill);
        schedule.LookbackDays.Should().Be(30);
        schedule.Granularity.Should().Be(DataGranularity.Daily);
        schedule.Priority.Should().Be(BackfillPriority.Normal);
    }

    [Fact]
    public void ScheduleId_IsUnique()
    {
        // Act
        var schedule1 = new BackfillSchedule();
        var schedule2 = new BackfillSchedule();

        // Assert
        schedule1.ScheduleId.Should().NotBe(schedule2.ScheduleId);
    }

    #endregion

    #region CalculateNextExecution Tests

    [Fact]
    public void CalculateNextExecution_ValidCron_ReturnsNextTime()
    {
        // Arrange
        var schedule = new BackfillSchedule
        {
            CronExpression = "0 2 * * *"
        };

        // Act
        var next = schedule.CalculateNextExecution();

        // Assert
        next.Should().NotBeNull();
        next!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void CalculateNextExecution_WithFromTime_ReturnsCorrectTime()
    {
        // Arrange
        var schedule = new BackfillSchedule
        {
            CronExpression = "0 2 * * *"
        };
        var from = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);

        // Act
        var next = schedule.CalculateNextExecution(from);

        // Assert
        next.Should().NotBeNull();
        next!.Value.Hour.Should().Be(2);
        next.Value.Day.Should().Be(16);
    }

    #endregion

    #region CreateJob Tests

    [Fact]
    public void CreateJob_DefaultDates_UsesLookbackDays()
    {
        // Arrange
        var schedule = new BackfillSchedule
        {
            Name = "Test Schedule",
            LookbackDays = 7,
            Symbols = new List<string> { "SPY", "AAPL" }
        };

        // Act
        var job = schedule.CreateJob();

        // Assert
        job.Should().NotBeNull();
        job.Name.Should().Contain("Test Schedule");
        job.Symbols.Should().BeEquivalentTo(new[] { "SPY", "AAPL" });
        job.FromDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7));
    }

    [Fact]
    public void CreateJob_CustomDates_UsesProvidedDates()
    {
        // Arrange
        var schedule = new BackfillSchedule
        {
            Name = "Test Schedule"
        };
        var fromDate = new DateOnly(2024, 1, 1);
        var toDate = new DateOnly(2024, 1, 31);

        // Act
        var job = schedule.CreateJob(fromDate, toDate);

        // Assert
        job.FromDate.Should().Be(fromDate);
        job.ToDate.Should().Be(toDate);
    }

    [Fact]
    public void CreateJob_CopiesOptionsFromSchedule()
    {
        // Arrange
        var schedule = new BackfillSchedule
        {
            BackfillType = ScheduledBackfillType.GapFill,
            MaxConcurrentRequests = 5,
            MaxRetries = 2,
            SkipExistingData = true,
            AutoPauseOnRateLimit = false,
            Priority = BackfillPriority.High
        };

        // Act
        var job = schedule.CreateJob();

        // Assert
        job.Options.FillGapsOnly.Should().BeTrue();
        job.Options.MaxConcurrentRequests.Should().Be(5);
        job.Options.MaxRetries.Should().Be(2);
        job.Options.SkipExistingData.Should().BeTrue();
        job.Options.AutoPauseOnRateLimit.Should().BeFalse();
        job.Options.Priority.Should().Be(10); // High priority
    }

    #endregion

    #region Preset Tests

    [Fact]
    public void DailyGapFill_CreatesCorrectSchedule()
    {
        // Act
        var schedule = BackfillSchedulePresets.DailyGapFill("Daily Test", new[] { "SPY" });

        // Assert
        schedule.Name.Should().Be("Daily Test");
        schedule.CronExpression.Should().Be("0 2 * * *");
        schedule.BackfillType.Should().Be(ScheduledBackfillType.GapFill);
        schedule.LookbackDays.Should().Be(7);
        schedule.Priority.Should().Be(BackfillPriority.Normal);
    }

    [Fact]
    public void WeeklyFullBackfill_CreatesCorrectSchedule()
    {
        // Act
        var schedule = BackfillSchedulePresets.WeeklyFullBackfill("Weekly Test");

        // Assert
        schedule.CronExpression.Should().Be("0 3 * * 0");
        schedule.BackfillType.Should().Be(ScheduledBackfillType.FullBackfill);
        schedule.LookbackDays.Should().Be(30);
        schedule.Priority.Should().Be(BackfillPriority.Low);
    }

    [Fact]
    public void EndOfDayUpdate_CreatesCorrectSchedule()
    {
        // Act
        var schedule = BackfillSchedulePresets.EndOfDayUpdate("EOD Test");

        // Assert
        schedule.CronExpression.Should().Be("0 23 * * 1-5");
        schedule.BackfillType.Should().Be(ScheduledBackfillType.EndOfDay);
        schedule.LookbackDays.Should().Be(1);
        schedule.Priority.Should().Be(BackfillPriority.High);
    }

    [Fact]
    public void MonthlyDeepBackfill_CreatesCorrectSchedule()
    {
        // Act
        var schedule = BackfillSchedulePresets.MonthlyDeepBackfill("Monthly Test");

        // Assert
        schedule.CronExpression.Should().Be("0 1 1-7 * 0");
        schedule.LookbackDays.Should().Be(365);
        schedule.Priority.Should().Be(BackfillPriority.Deferred);
    }

    #endregion
}

/// <summary>
/// Unit tests for the BackfillExecutionLog and BackfillExecutionHistory classes.
/// </summary>
public class BackfillExecutionLogTests
{
    #region ExecutionLog Tests

    [Fact]
    public void Constructor_DefaultValues_SetsDefaults()
    {
        // Act
        var log = new BackfillExecutionLog();

        // Assert
        log.ExecutionId.Should().NotBeNullOrEmpty();
        log.Trigger.Should().Be(ExecutionTrigger.Scheduled);
        log.Status.Should().Be(ExecutionStatus.Pending);
        log.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void Duration_WithStartAndEnd_ReturnsCorrectDuration()
    {
        // Arrange
        var log = new BackfillExecutionLog
        {
            StartedAt = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2024, 1, 1, 10, 30, 0, TimeSpan.Zero)
        };

        // Act & Assert
        log.Duration.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Duration_WithoutCompletion_ReturnsNull()
    {
        // Arrange
        var log = new BackfillExecutionLog
        {
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act & Assert
        log.Duration.Should().BeNull();
    }

    #endregion

    #region ExecutionHistory Tests

    [Fact]
    public void AddExecution_StoresExecution()
    {
        // Arrange
        var history = new BackfillExecutionHistory();
        var execution = new BackfillExecutionLog
        {
            ScheduleId = "test-schedule"
        };

        // Act
        history.AddExecution(execution);

        // Assert
        history.GetExecution(execution.ExecutionId).Should().NotBeNull();
    }

    [Fact]
    public void GetRecentExecutions_ReturnsInDescendingOrder()
    {
        // Arrange
        var history = new BackfillExecutionHistory();
        var older = new BackfillExecutionLog
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var newer = new BackfillExecutionLog
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        history.AddExecution(older);
        history.AddExecution(newer);

        // Act
        var recent = history.GetRecentExecutions(10);

        // Assert
        recent.Should().HaveCount(2);
        recent[0].ExecutionId.Should().Be(newer.ExecutionId);
        recent[1].ExecutionId.Should().Be(older.ExecutionId);
    }

    [Fact]
    public void GetExecutionsForSchedule_FiltersCorrectly()
    {
        // Arrange
        var history = new BackfillExecutionHistory();
        history.AddExecution(new BackfillExecutionLog { ScheduleId = "schedule-a" });
        history.AddExecution(new BackfillExecutionLog { ScheduleId = "schedule-b" });
        history.AddExecution(new BackfillExecutionLog { ScheduleId = "schedule-a" });

        // Act
        var executions = history.GetExecutionsForSchedule("schedule-a");

        // Assert
        executions.Should().HaveCount(2);
        executions.Should().AllSatisfy(e => e.ScheduleId.Should().Be("schedule-a"));
    }

    [Fact]
    public void GetFailedExecutions_FiltersFailedOnly()
    {
        // Arrange
        var history = new BackfillExecutionHistory();
        history.AddExecution(new BackfillExecutionLog { Status = ExecutionStatus.Completed });
        history.AddExecution(new BackfillExecutionLog { Status = ExecutionStatus.Failed });
        history.AddExecution(new BackfillExecutionLog { Status = ExecutionStatus.Failed });

        // Act
        var failed = history.GetFailedExecutions();

        // Assert
        failed.Should().HaveCount(2);
        failed.Should().AllSatisfy(e => e.Status.Should().Be(ExecutionStatus.Failed));
    }

    [Fact]
    public void GetScheduleSummary_CalculatesCorrectStats()
    {
        // Arrange
        var history = new BackfillExecutionHistory();
        var scheduleId = "test-schedule";

        history.AddExecution(new BackfillExecutionLog
        {
            ScheduleId = scheduleId,
            Status = ExecutionStatus.Completed,
            Statistics = new ExecutionStatistics { TotalBarsRetrieved = 100 }
        });
        history.AddExecution(new BackfillExecutionLog
        {
            ScheduleId = scheduleId,
            Status = ExecutionStatus.Completed,
            Statistics = new ExecutionStatistics { TotalBarsRetrieved = 200 }
        });
        history.AddExecution(new BackfillExecutionLog
        {
            ScheduleId = scheduleId,
            Status = ExecutionStatus.Failed
        });

        // Act
        var summary = history.GetScheduleSummary(scheduleId);

        // Assert
        summary.TotalExecutions.Should().Be(3);
        summary.SuccessfulExecutions.Should().Be(2);
        summary.FailedExecutions.Should().Be(1);
        summary.TotalBarsRetrieved.Should().Be(300);
    }

    [Fact]
    public void GetSystemSummary_CalculatesOverallStats()
    {
        // Arrange
        var history = new BackfillExecutionHistory();

        history.AddExecution(new BackfillExecutionLog
        {
            Status = ExecutionStatus.Completed,
            Statistics = new ExecutionStatistics { GapsFilled = 5 },
            ProviderStats = new Dictionary<string, ProviderUsageStats>
            {
                ["yahoo"] = new() { ProviderName = "yahoo", RequestCount = 10 }
            }
        });
        history.AddExecution(new BackfillExecutionLog
        {
            Status = ExecutionStatus.Completed,
            Statistics = new ExecutionStatistics { GapsFilled = 3 },
            ProviderStats = new Dictionary<string, ProviderUsageStats>
            {
                ["yahoo"] = new() { ProviderName = "yahoo", RequestCount = 5 }
            }
        });

        // Act
        var summary = history.GetSystemSummary();

        // Assert
        summary.TotalExecutions.Should().Be(2);
        summary.CompletedExecutions.Should().Be(2);
        summary.TotalGapsFilled.Should().Be(8);
        summary.ProviderUsage["yahoo"].Should().Be(15);
    }

    #endregion
}

/// <summary>
/// Unit tests for the ExecutionStatistics class.
/// </summary>
public class ExecutionStatisticsTests
{
    [Fact]
    public void SuccessRate_WithRequests_CalculatesCorrectly()
    {
        // Arrange
        var stats = new ExecutionStatistics
        {
            TotalRequests = 100,
            SuccessfulRequests = 95,
            FailedRequests = 5
        };

        // Act & Assert
        stats.SuccessRate.Should().BeApproximately(0.95, 0.001);
    }

    [Fact]
    public void SuccessRate_NoRequests_ReturnsZero()
    {
        // Arrange
        var stats = new ExecutionStatistics();

        // Act & Assert
        stats.SuccessRate.Should().Be(0);
    }

    [Fact]
    public void GapsRemaining_CalculatesCorrectly()
    {
        // Arrange
        var stats = new ExecutionStatistics
        {
            GapsDetected = 10,
            GapsFilled = 7
        };

        // Act & Assert
        stats.GapsRemaining.Should().Be(3);
    }
}

/// <summary>
/// Unit tests for the ScheduleStatusSummary record.
/// </summary>
public class ScheduleStatusSummaryTests
{
    [Fact]
    public void OverallSuccessRate_WithExecutions_CalculatesCorrectly()
    {
        // Arrange
        var summary = new ScheduleStatusSummary
        {
            TotalExecutions = 10,
            TotalSuccesses = 8,
            TotalFailures = 2
        };

        // Act & Assert
        summary.OverallSuccessRate.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void OverallSuccessRate_NoExecutions_ReturnsZero()
    {
        // Arrange
        var summary = new ScheduleStatusSummary();

        // Act & Assert
        summary.OverallSuccessRate.Should().Be(0);
    }
}
