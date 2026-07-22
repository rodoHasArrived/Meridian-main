using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class BackfillExecutionContractProjectionTests
{
    [Fact]
    public void Build_ProjectsTypedAndLegacySlaWithoutChangingEnvelopeNames()
    {
        var now = new DateTimeOffset(2026, 07, 13, 16, 00, 00, TimeSpan.Zero);
        var typed = new BackfillExecutionLog
        {
            ExecutionId = "typed",
            Trigger = ExecutionTrigger.AutoRemediation,
            Status = ExecutionStatus.Running,
            FromDate = new DateOnly(2026, 07, 10),
            ToDate = new DateOnly(2026, 07, 11),
            Symbols = { "AAPL" },
            AutoRemediationTriggerReason = "critical close gap",
            AutoRemediationSla = new BackfillRemediationSlaMetadata(
                BackfillRemediationSlaTier.SameBusinessDay,
                now.AddHours(8),
                RequiresOwnerAssignment: true,
                DownstreamWorkflow: "accounting",
                ReasonCode: "CriticalWorkflow",
                Provider: " Polygon ",
                TriggerSource: AutoRemediationTriggerSource.QualityAlert)
        };
        var legacy = new BackfillExecutionLog
        {
            ExecutionId = "legacy",
            Trigger = ExecutionTrigger.AutoRemediation,
            Status = ExecutionStatus.Running,
            Symbols = { "SPY" },
            Warnings =
            {
                "sla-tier=Standard",
                $"sla-due-utc={now.AddMinutes(-1):O}",
                "sla-requires-owner=false",
                "downstream-workflow=reporting",
                "sla-reason=HistoricalCompatibility"
            }
        };

        var response = BackfillExecutionContractProjection.Build(
            [typed, legacy],
            defaultProvider: " Stooq ",
            nowUtc: now);

        response.Executions.Should().HaveCount(2);
        response.Total.Should().Be(2);
        response.AutoRemediation.Total.Should().Be(2);
        response.AutoRemediation.DefaultProvider.Should().Be("stooq");

        var typedSla = response.Executions.Single(row => row.Id == "typed").AutoRemediationSla;
        typedSla.Should().NotBeNull();
        typedSla!.Tier.Should().Be(BackfillRemediationSlaTierDto.SameBusinessDay);
        typedSla.Status.Should().Be(BackfillRemediationSlaStatusDto.Open);
        typedSla.Provider.Should().Be("polygon");
        typedSla.IsCompatibilityDerived.Should().BeFalse();

        var legacySla = response.Executions.Single(row => row.Id == "legacy").AutoRemediationSla;
        legacySla.Should().NotBeNull();
        legacySla!.Status.Should().Be(BackfillRemediationSlaStatusDto.Overdue);
        legacySla.Provider.Should().Be("stooq");
        legacySla.IsCompatibilityDerived.Should().BeTrue();
    }

    [Fact]
    public void Build_FailedRemediationRemainsFailedAfterItsDeadline()
    {
        var now = new DateTimeOffset(2026, 07, 13, 16, 00, 00, TimeSpan.Zero);
        var failed = new BackfillExecutionLog
        {
            ExecutionId = "failed",
            Trigger = ExecutionTrigger.AutoRemediation,
            Status = ExecutionStatus.Failed,
            AutoRemediationLastOutcome = AutoRemediationOutcome.FailedPermanent.ToString(),
            AutoRemediationSla = new BackfillRemediationSlaMetadata(
                BackfillRemediationSlaTier.Standard,
                now.AddHours(-1),
                RequiresOwnerAssignment: false,
                DownstreamWorkflow: "research",
                ReasonCode: "HistoricalGap",
                Provider: "stooq",
                TriggerSource: AutoRemediationTriggerSource.DataQualityGap)
        };

        var response = BackfillExecutionContractProjection.Build([failed], "stooq", now);

        response.Executions.Single().AutoRemediationSla!.Status
            .Should().Be(BackfillRemediationSlaStatusDto.Failed);
    }
}
