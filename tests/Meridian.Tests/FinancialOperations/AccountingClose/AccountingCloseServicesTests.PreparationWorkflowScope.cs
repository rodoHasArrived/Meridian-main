using FluentAssertions;
using Meridian.Contracts.Workstation;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseServicesTests
{
    [Theory]
    [InlineData(true, OperationsWorkflowStatusDto.NotStarted, false)]
    [InlineData(true, OperationsWorkflowStatusDto.Closed, false)]
    [InlineData(false, OperationsWorkflowStatusDto.NotStarted, false)]
    [InlineData(false, OperationsWorkflowStatusDto.ApprovalPending, false)]
    [InlineData(false, OperationsWorkflowStatusDto.Blocked, false)]
    [InlineData(false, OperationsWorkflowStatusDto.Closed, true)]
    public async Task Scenario_PrepareNextPeriod_PreviewMatchesAccountWideOpenWorkflowUniqueness(
        bool sameBook, OperationsWorkflowStatusDto status, bool canCreate)
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var existing = ExistingPreparationWorkflow(fixture, sameBook, status);
        SetPreparationWorkflowSummaries(fixture, () => [existing]);

        var preview = await fixture.PreviewAsync(template);

        preview.CanCreate.Should().Be(canCreate);
        if (canCreate)
        {
            preview.Issues.Should().NotContain(issue => issue.Code == "TargetPlanExists");
            var created = await fixture.CreateAsync(preview);
            created.TargetLedgerBookId.Should().Be(fixture.TargetBook.LedgerBookId);
        }
        else
        {
            preview.Issues.Should().Contain(issue => issue.Code == "TargetPlanExists" && issue.IsBlocking);
            var create = () => fixture.CreateAsync(preview);
            await create.Should().ThrowAsync<InvalidOperationException>();
            await fixture.Workflows.DidNotReceive().StartPreparedWorkflowAsync(
                Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scenario_PrepareNextPeriod_AccountWideConflictAfterPreviewDoesNotReserveTarget(bool legacyPeriodAlias)
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        IReadOnlyList<OperationsContinuityWorkflowSummaryDto> existing = [];
        SetPreparationWorkflowSummaries(fixture, () => existing);
        var preview = await fixture.PreviewAsync(template);
        preview.CanCreate.Should().BeTrue();
        var competitor = ExistingPreparationWorkflow(fixture, false, OperationsWorkflowStatusDto.NotStarted) with
        {
            PeriodId = legacyPeriodAlias ? fixture.TargetPeriod.Label : fixture.TargetPeriod.PeriodId.ToString("D")
        };
        existing = [competitor];

        var create = () => fixture.CreateAsync(preview);

        await create.Should().ThrowAsync<InvalidOperationException>();
        fixture.CreatedWorkflows.Should().BeEmpty();
        await fixture.Workflows.DidNotReceive().StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // A rejected preflight must leave no creation claim: closing the other book's workflow
        // permits the original request key and preview to create the target plan normally.
        existing = [competitor with { Status = OperationsWorkflowStatusDto.Closed }];
        var refreshed = await fixture.PreviewAsync(template);
        refreshed.CanCreate.Should().BeTrue();
        var recovered = await fixture.CreateAsync(preview);
        recovered.WasAlreadyCreated.Should().BeFalse();
        fixture.CreatedWorkflows.Should().ContainSingle();
        await fixture.Workflows.Received(1).StartPreparedWorkflowAsync(
            Arg.Any<OperationsStartWorkflowRequestDto>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scenario_PrepareNextPeriod_AnotherPeriodOpenWorkflowDoesNotBlockPreparation()
    {
        using var fixture = new PreparationFixture();
        var template = await fixture.CaptureAsync();
        var existing = ExistingPreparationWorkflow(fixture, false, OperationsWorkflowStatusDto.NotStarted) with
        {
            PeriodId = Guid.NewGuid().ToString("D")
        };
        SetPreparationWorkflowSummaries(fixture, () => [existing]);

        var preview = await fixture.PreviewAsync(template);
        var result = await fixture.CreateAsync(preview);

        preview.CanCreate.Should().BeTrue();
        result.TargetPeriodId.Should().Be(fixture.TargetPeriod.PeriodId);
    }

    private static OperationsContinuityWorkflowSummaryDto ExistingPreparationWorkflow(PreparationFixture fixture,
        bool sameBook, OperationsWorkflowStatusDto status) =>
        new(Guid.NewGuid(), fixture.SourceWorkflow.FundAccountId, fixture.TargetPeriod.PeriodId.ToString("D"),
            null, "custodian", status, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [],
            sameBook ? fixture.TargetBook.LedgerBookId : fixture.SourceBook.LedgerBookId);

    private static void SetPreparationWorkflowSummaries(PreparationFixture fixture,
        Func<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> read)
    {
        fixture.Workflows.ListAsync(Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<OperationsWorkflowStatusDto?>(),
            Arg.Any<CancellationToken>(), Arg.Any<Guid?>()).Returns(call =>
        {
            var accountId = call.ArgAt<Guid?>(0);
            var periodId = call.ArgAt<string?>(1);
            var status = call.ArgAt<OperationsWorkflowStatusDto?>(2);
            var bookId = call.ArgAt<Guid?>(4);
            return (IReadOnlyList<OperationsContinuityWorkflowSummaryDto>)read().Where(workflow =>
                (!accountId.HasValue || workflow.FundAccountId == accountId.Value)
                && (periodId is null || workflow.PeriodId == periodId)
                && (!status.HasValue || workflow.Status == status.Value)
                && (!bookId.HasValue || workflow.LedgerBookId == bookId.Value)).ToArray();
        });
    }
}
