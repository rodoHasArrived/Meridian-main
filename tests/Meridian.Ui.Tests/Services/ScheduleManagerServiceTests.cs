using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ScheduleManagerService"/> and its associated DTO models.
/// </summary>
public sealed class ScheduleManagerServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        var instance = ScheduleManagerService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = ScheduleManagerService.Instance;
        var b = ScheduleManagerService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── BackfillSchedule model ──────────────────────────────────────

    [Fact]
    public void BackfillSchedule_DefaultValues_ShouldBeCorrect()
    {
        var schedule = new BackfillSchedule();

        schedule.Id.Should().BeEmpty();
        schedule.Name.Should().BeEmpty();
        schedule.Description.Should().BeEmpty();
        schedule.CronExpression.Should().BeEmpty();
        schedule.CronDescription.Should().BeEmpty();
        schedule.IsEnabled.Should().BeFalse();
        schedule.Symbols.Should().NotBeNull().And.BeEmpty();
        schedule.Provider.Should().BeEmpty();
        schedule.Granularity.Should().Be("Daily");
        schedule.LookbackDays.Should().Be(0);
        schedule.Priority.Should().Be("Normal");
        schedule.Tags.Should().NotBeNull().And.BeEmpty();
        schedule.LastRunAt.Should().BeNull();
        schedule.NextRunAt.Should().BeNull();
        schedule.LastRunStatus.Should().BeEmpty();
        schedule.SuccessCount.Should().Be(0);
        schedule.FailureCount.Should().Be(0);
    }

    [Fact]
    public void BackfillSchedule_Symbols_ShouldSupportAddAndRead()
    {
        var schedule = new BackfillSchedule();
        schedule.Symbols.Add("SPY");
        schedule.Symbols.Add("AAPL");

        schedule.Symbols.Should().HaveCount(2);
        schedule.Symbols.Should().Contain("SPY");
    }

    // ── CreateBackfillScheduleRequest model ─────────────────────────

    [Fact]
    public void CreateBackfillScheduleRequest_DefaultValues_ShouldBeCorrect()
    {
        var request = new CreateBackfillScheduleRequest();

        request.Name.Should().BeEmpty();
        request.Description.Should().BeEmpty();
        request.CronExpression.Should().BeEmpty();
        request.Symbols.Should().NotBeNull().And.BeEmpty();
        request.Provider.Should().BeEmpty();
        request.Granularity.Should().Be("Daily");
        request.LookbackDays.Should().Be(7);
        request.Priority.Should().Be("Normal");
        request.Tags.Should().NotBeNull().And.BeEmpty();
        request.IsEnabled.Should().BeTrue();
    }

    // ── UpdateBackfillScheduleRequest model ─────────────────────────

    [Fact]
    public void UpdateBackfillScheduleRequest_DefaultValues_ShouldBeAllNull()
    {
        var request = new UpdateBackfillScheduleRequest();

        request.Name.Should().BeNull();
        request.Description.Should().BeNull();
        request.CronExpression.Should().BeNull();
        request.Symbols.Should().BeNull();
        request.Provider.Should().BeNull();
        request.Granularity.Should().BeNull();
        request.LookbackDays.Should().BeNull();
        request.Priority.Should().BeNull();
        request.Tags.Should().BeNull();
    }

    // ── ScheduleExecutionResult model ───────────────────────────────

    [Fact]
    public void ScheduleExecutionResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new ScheduleExecutionResult();

        result.Success.Should().BeFalse();
        result.ExecutionId.Should().BeEmpty();
        result.Message.Should().BeEmpty();
    }

    // ── ScheduleExecutionLog model ──────────────────────────────────

    [Fact]
    public void ScheduleExecutionLog_DefaultValues_ShouldBeCorrect()
    {
        var log = new ScheduleExecutionLog();

        log.Id.Should().BeEmpty();
        log.ScheduleId.Should().BeEmpty();
        log.ScheduleName.Should().BeEmpty();
        log.CompletedAt.Should().BeNull();
        log.Status.Should().BeEmpty();
        log.RecordsProcessed.Should().Be(0);
        log.RecordsFailed.Should().Be(0);
        log.ErrorMessage.Should().BeNull();
        log.Duration.Should().Be(TimeSpan.Zero);
        log.Details.Should().NotBeNull().And.BeEmpty();
    }

    // ── ScheduleTemplate model ──────────────────────────────────────

    [Fact]
    public void ScheduleTemplate_DefaultValues_ShouldBeCorrect()
    {
        var template = new ScheduleTemplate();

        template.Id.Should().BeEmpty();
        template.Name.Should().BeEmpty();
        template.Description.Should().BeEmpty();
        template.CronExpression.Should().BeEmpty();
        template.CronDescription.Should().BeEmpty();
        template.Category.Should().BeEmpty();
    }

    // ── MaintenanceSchedule model ───────────────────────────────────

    [Fact]
    public void MaintenanceSchedule_DefaultValues_ShouldBeCorrect()
    {
        var schedule = new MaintenanceSchedule();

        schedule.Id.Should().BeEmpty();
        schedule.Name.Should().BeEmpty();
        schedule.Description.Should().BeEmpty();
        schedule.MaintenanceType.Should().BeEmpty();
        schedule.CronExpression.Should().BeEmpty();
        schedule.CronDescription.Should().BeEmpty();
        schedule.IsEnabled.Should().BeFalse();
        schedule.TargetPath.Should().BeNull();
        schedule.Priority.Should().Be("Normal");
        schedule.MaxDurationMinutes.Should().Be(0);
        schedule.MaxRetries.Should().Be(0);
        schedule.LastRunAt.Should().BeNull();
        schedule.NextRunAt.Should().BeNull();
        schedule.LastRunStatus.Should().BeEmpty();
        schedule.SuccessCount.Should().Be(0);
        schedule.FailureCount.Should().Be(0);
    }

    // ── CronValidationResult model ──────────────────────────────────

    [Fact]
    public void CronValidationResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new CronValidationResult();

        result.IsValid.Should().BeFalse();
        result.Description.Should().BeEmpty();
        result.ErrorMessage.Should().BeNull();
        result.NextRuns.Should().NotBeNull().And.BeEmpty();
    }

    // ── DeleteResponse / EnableResponse ─────────────────────────────

    [Fact]
    public void DeleteResponse_DefaultValues_ShouldBeCorrect()
    {
        var response = new DeleteResponse();

        response.Success.Should().BeFalse();
        response.Message.Should().BeEmpty();
    }

    [Fact]
    public void EnableResponse_DefaultValues_ShouldBeCorrect()
    {
        var response = new EnableResponse();

        response.Success.Should().BeFalse();
        response.IsEnabled.Should().BeFalse();
    }
}
