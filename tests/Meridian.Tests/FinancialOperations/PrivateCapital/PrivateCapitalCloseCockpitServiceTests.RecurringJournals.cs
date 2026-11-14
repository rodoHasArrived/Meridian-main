using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitServiceTests
{
    private const string RecurringTenant = "tenant-alpha";
    private const string RecurringCompany = "company-alpha";

    [Theory]
    [InlineData("Drafted", "Draft", false)]
    [InlineData("Drafted", "Approved", false)]
    [InlineData("Blocked", null, false)]
    [InlineData("Claimed", null, false)]
    [InlineData("Drafted", "Posted", true)]
    [InlineData("Drafted", "Reversed", true)]
    [InlineData("Drafted", "Rebooked", true)]
    [InlineData("Blocked", "Reversed", false)]
    [InlineData("NoDraftRequired", null, true)]
    public async Task RecurringJournalInThePeriod_GatesCloseUntilPostedOrNothingIsDue(string state, string? approval, bool ready)
    {
        var activity = BuildActivity();
        var service = RecurringCockpit(activity, [Occurrence(activity.LedgerBookId!.Value, new(2026, 6, 30), state, approval)]);

        var cockpit = await service.GetCockpitAsync(FundProfileId, activity.LedgerBookId, FundAccountId, PeriodId, EntityId,
            tenantId: RecurringTenant, companyId: RecurringCompany);

        var lane = cockpit.Lanes.Should().ContainSingle(item => item.LaneId == "recurring-journals").Subject;
        lane.IsReady.Should().Be(ready);
        lane.RequiredForClose.Should().BeTrue();
        cockpit.IsReadyToClose.Should().Be(ready);
    }

    [Fact]
    public async Task RecurringJournalInAPeriodNamedByItsId_StillGatesClose()
    {
        // Production occurrences retain the accounting period's id, not a calendar-month label.
        var periodId = Guid.NewGuid().ToString("D");
        var activity = BuildActivity();
        var service = RecurringCockpit(activity,
            [Occurrence(activity.LedgerBookId!.Value, new(2026, 6, 30), "Blocked", null) with { PeriodId = periodId }]);

        var cockpit = await service.GetCockpitAsync(FundProfileId, activity.LedgerBookId, FundAccountId, periodId, EntityId,
            tenantId: RecurringTenant, companyId: RecurringCompany);

        cockpit.Lanes.Should().ContainSingle(item => item.LaneId == "recurring-journals").Which.IsReady.Should().BeFalse();
        cockpit.IsReadyToClose.Should().BeFalse();
    }

    [Fact]
    public async Task RecurringJournalsOutsideThePeriod_AddNoLaneAndDoNotGateClose()
    {
        var activity = BuildActivity();
        var service = RecurringCockpit(activity, [Occurrence(activity.LedgerBookId!.Value, new(2026, 7, 31), "Blocked", null)]);

        var cockpit = await service.GetCockpitAsync(FundProfileId, activity.LedgerBookId, FundAccountId, PeriodId, EntityId,
            tenantId: RecurringTenant, companyId: RecurringCompany);

        cockpit.Lanes.Should().NotContain(lane => lane.LaneId == "recurring-journals");
        cockpit.IsReadyToClose.Should().BeTrue();
    }

    [Fact]
    public async Task UnreadableRecurringQueue_BlocksCloseInsteadOfAssumingNothingIsOutstanding()
    {
        var activity = BuildActivity();
        var service = CreateSourceBackedCockpitService(new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]),
            recurringJournalQueueSource: new RecurringQueue(null));

        var cockpit = await service.GetCockpitAsync(FundProfileId, activity.LedgerBookId, FundAccountId, PeriodId, EntityId,
            tenantId: RecurringTenant, companyId: RecurringCompany);

        cockpit.Lanes.Should().ContainSingle(lane => lane.LaneId == "recurring-journals" && !lane.IsReady);
        cockpit.IsReadyToClose.Should().BeFalse();
    }

    private static PrivateCapitalCloseCockpitService RecurringCockpit(PrivateCapitalActivityProjectionDto activity,
        IReadOnlyList<RecurringJournalOccurrenceDto> occurrences)
        => CreateSourceBackedCockpitService(new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]),
            recurringJournalQueueSource: new RecurringQueue(occurrences));

    private static RecurringJournalOccurrenceDto Occurrence(Guid bookId, DateOnly effectiveDate, string state, string? approval)
        => new("occurrence-1", "management-fee", 1, "management-fee-template", 1, effectiveDate, FundProfileId, bookId,
            EntityId, null, state, state == "Drafted" ? Guid.NewGuid() : null, approval, [], ["evidence://fund-terms-2026"]);

    private sealed class RecurringQueue(IReadOnlyList<RecurringJournalOccurrenceDto>? occurrences) : IRecurringJournalQueueSource
    {
        public Task<RecurringJournalQueueDto> GetQueueAsync(string fundProfileId, Guid ledgerBookId, string entityId,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => occurrences is null
                ? Task.FromException<RecurringJournalQueueDto>(new IOException("The recurring registry is unavailable."))
                : Task.FromResult(new RecurringJournalQueueDto(fundProfileId, ledgerBookId, entityId, occurrences));
    }
}
