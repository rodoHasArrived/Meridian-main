using FluentAssertions;
using Meridian.Contracts.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseServicesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scenario_PrepareNextPeriod_PrunesExpiredUnclaimedPreviewsButRetainsCreationRecovery(bool interrupted)
    {
        var clock = new PreparationRetentionClock(DateTimeOffset.UtcNow);
        using var fixture = new PreparationFixture(clock);
        var template = await fixture.CaptureAsync();
        var unused = await fixture.PreviewAsync(template);
        var claimed = await fixture.PreviewAsync(template);
        if (interrupted)
        {
            fixture.FailNextConfiguration = true;
            var create = () => fixture.CreateAsync(claimed);
            await create.Should().ThrowAsync<IOException>();
        }
        else
        {
            await fixture.CreateAsync(claimed);
        }

        clock.Advance(TimeSpan.FromMinutes(16));
        var fresh = await fixture.PreviewAsync(template);
        var restarted = fixture.NewService();

        (await restarted.GetPreviewAsync(unused.PreviewId)).Should().BeNull();
        (await restarted.GetPreviewAsync(claimed.PreviewId)).Should().NotBeNull();
        (await restarted.GetPreviewAsync(fresh.PreviewId)).Should().NotBeNull();
        var recovered = await restarted.CreateAsync(
            new CreatePreparedClosePlanRequestDto(claimed.PreviewId, "prepare-october"),
            PreparationFixture.Actor, PreparationFixture.Tenant, PreparationFixture.Company);
        recovered.WorkflowId.Should().Be(fixture.CreatedWorkflows.Single().WorkflowId);
        recovered.TemplateId.Should().Be(template.TemplateId);
        recovered.TemplateVersion.Should().Be(template.Version);
        recovered.History.Should().Contain(item => item.EventType == "PlanCreated");
    }

    private sealed class PreparationRetentionClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
