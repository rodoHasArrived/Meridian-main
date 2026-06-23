using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.FinancialOperations.AccountingClose;

/// <summary>
/// Guards month-end accounting close scenarios where FX rates, posting balance, source-event lineage,
/// period locks, and evidence gates must remain deterministic for operator replay.
/// </summary>
public sealed class AccountingCloseServicesTests
{
    [Fact]
    public async Task Scenario_ClosePlan_DependencyWaitsUntilPredecessorIsSignedOff()
    {
        var workflowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);

        var plan = await service.GetPeriodPlanAsync(workflowId);

        plan.Should().NotBeNull();
        var dependentTask = plan!.Tasks.Single(task => task.TaskId == "report-certification");
        dependentTask.Dependencies.Should().ContainSingle(dependency => dependency.DependsOnTaskId == "reconciliation-review");
        dependentTask.Status.Should().Be(CloseTaskStatusDto.WaitingOnDependency);
        plan.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "CloseTaskWaitingOnDependency" &&
            issue.TargetId == "report-certification");
    }

    [Fact]
    public async Task Scenario_ClosePlan_DependencyAdvancesAfterPredecessorIsSignedOff()
    {
        var workflowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Done",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);

        var plan = await service.GetPeriodPlanAsync(workflowId);

        plan.Should().NotBeNull();
        var dependentTask = plan!.Tasks.Single(task => task.TaskId == "report-certification");
        dependentTask.Dependencies.Should().ContainSingle(dependency => dependency.DependsOnTaskId == "reconciliation-review");
        dependentTask.Status.Should().Be(CloseTaskStatusDto.NotStarted);
        plan.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "CloseTaskWaitingOnDependency" &&
            issue.TargetId == "report-certification");
    }

    [Fact]
    public async Task Scenario_ClosePlan_SignOffRequiresActorIndependentFromTaskAcknowledgement()
    {
        var workflowId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending",
                firstTaskAcknowledgedBy: "close-preparer"));
        var service = new AccountingCloseManagementService(workflowService);
        var evidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-signoff";
        var request = new SignOffCloseTaskRequestDto(
            workflowId,
            "reconciliation-review",
            "Controller",
            ManualJournalEntryStatusDto.Approved,
            "close-preparer",
            "Controller sign-off retained.",
            [evidence]);

        var sameActor = async () => await service.SignOffCloseTaskAsync(request, "close-preparer");

        await sameActor.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*independent from acknowledgement actor 'close-preparer'*");

        var plan = await service.SignOffCloseTaskAsync(request with { Actor = "controller-reviewer" }, "controller-reviewer");

        plan.Should().NotBeNull();
        var signedOffTask = plan!.Tasks.Single(task => task.TaskId == "reconciliation-review");
        signedOffTask.Status.Should().Be(CloseTaskStatusDto.SignedOff);
        signedOffTask.SignOffs.Should().ContainSingle(signOff =>
            signOff.Actor == "controller-reviewer" &&
            signOff.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            signOff.EvidenceLinks.Contains(evidence));
        signedOffTask.SignOffRequirements.Should().ContainSingle().Subject.IsSatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_ClosePlan_RejectedSignOffBlocksTaskAndCloseCalendar()
    {
        var workflowId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);
        var evidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-review-rejection";
        var plan = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Rejected,
                "controller-reviewer",
                "Close support packet rejected pending remediation.",
                [evidence]),
            "controller-reviewer");

        plan.Should().NotBeNull();
        var rejectedTask = plan!.Tasks.Single(task => task.TaskId == "reconciliation-review");
        rejectedTask.Status.Should().Be(CloseTaskStatusDto.Blocked);
        rejectedTask.SignOffs.Should().ContainSingle(signOff =>
            signOff.Actor == "controller-reviewer" &&
            signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
            signOff.EvidenceLinks.Contains(evidence));
        rejectedTask.SignOffRequirements.Should().ContainSingle().Subject.IsSatisfied.Should().BeFalse();
        plan.CloseCalendar.Should().ContainSingle(milestone =>
            milestone.TaskId == "reconciliation-review" &&
            milestone.IsBlocked &&
            !milestone.IsSatisfied &&
            milestone.ApprovedSignOffCount == 0);
        plan.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseTaskSignOffRejected" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "reconciliation-review");
    }

    [Fact]
    public async Task Scenario_ClosePlan_RejectedSignOffBlocksAdditionalRoleDecisionsUntilRemediation()
    {
        var workflowId = Guid.Parse("45454545-4545-4545-4545-454545454545");
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(BuildCloseWorkflow(
                workflowId,
                firstTaskStatus: "Pending",
                secondTaskStatus: "Pending"));
        var service = new AccountingCloseManagementService(workflowService);
        var rejectionEvidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-review-rejection";
        var approvalEvidence = $"evidence:close-task:reconciliation-review:Controller:2026-03:book:{ledgerBookId:D}:control-review-approval-after-rejection";

        await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Rejected,
                "controller-reviewer",
                "Close support packet rejected pending remediation.",
                [rejectionEvidence]),
            "controller-reviewer");

        var approvalAfterRejection = async () => await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "reconciliation-review",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "controller-reviewer-2",
                "Attempted approval before remediation.",
                [approvalEvidence]),
            "controller-reviewer-2");

        await approvalAfterRejection.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*retained rejected sign-off*remediated*");

        var plan = await service.GetPeriodPlanAsync(workflowId);
        plan.Should().NotBeNull();
        var rejectedTask = plan!.Tasks.Single(task => task.TaskId == "reconciliation-review");
        rejectedTask.Status.Should().Be(CloseTaskStatusDto.Blocked);
        rejectedTask.SignOffs.Should().ContainSingle(signOff =>
            signOff.Actor == "controller-reviewer" &&
            signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
            signOff.EvidenceLinks.Contains(rejectionEvidence));
    }

    [Fact]
    public void Scenario_MonthEndFxTranslation_ReplayUsesStableAdjustmentIdAndRateLineage()
    {
        var service = new FxTranslationService();
        var rate = new FxRate("EUR", "USD", new DateOnly(2026, 03, 31), 1.10m, "fx:ecb:20260331", "ECB-EURUSD-20260331");

        var first = service.Translate("ledger-a", new DateOnly(2026, 03, 31), "Cash", 100m, rate);
        var replay = service.Translate("ledger-a", new DateOnly(2026, 03, 31), "Cash", 100m, rate);

        first.ReportingAmount.Should().Be(110m);
        first.AdjustmentAmount.Should().Be(10m);
        first.SourceEventId.Should().Be("fx:ecb:20260331");
        first.RateId.Should().Be("ECB-EURUSD-20260331");
        replay.AdjustmentId.Should().Be(first.AdjustmentId);
    }

    [Fact]
    public void Scenario_MonthEndTrialBalance_OutOfBalanceActivityBlocksCloseEvidence()
    {
        var projection = new TrialBalanceProjectionService();
        var entries = ImmutableArray.Create(
            NewEntry("evt-1", "approval-1", new JournalLine("Cash", 100m, "USD", true, "evt-1", "approval-1")),
            NewEntry("evt-2", "approval-2", new JournalLine("Revenue", 90m, "USD", false, "evt-2", "approval-2")));

        var trial = projection.BuildTrialBalance(entries);
        var stateMachine = new MonthEndCloseStateMachine();
        var validating = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(true, true, true), []);

        var next = stateMachine.Transition(validating, new CloseEvidence(true, true, true), projection.IsBalanced(trial));

        projection.IsBalanced(trial).Should().BeFalse();
        next.State.Should().Be(ClosePeriodState.Blocked);
        next.Blockers.Should().Contain("Trial balance is out of balance.");
    }

    [Fact]
    public void Scenario_MonthEndTrialBalance_PreservesLineDimensionsAsSeparateCloseRows()
    {
        var projection = new TrialBalanceProjectionService();
        var entityAlpha = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var entityBeta = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-beta",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Operations"
            });
        var entries = ImmutableArray.Create(
            NewEntry(
                "evt-entity-alpha",
                "approval-entity-alpha",
                new JournalLine("Cash", 125m, "USD", true, "evt-entity-alpha", "approval-entity-alpha", Dimensions: entityAlpha),
                new JournalLine("Revenue", 125m, "USD", false, "evt-entity-alpha", "approval-entity-alpha", Dimensions: entityAlpha)),
            NewEntry(
                "evt-entity-beta",
                "approval-entity-beta",
                new JournalLine("Cash", 75m, "USD", true, "evt-entity-beta", "approval-entity-beta", Dimensions: entityBeta),
                new JournalLine("Revenue", 75m, "USD", false, "evt-entity-beta", "approval-entity-beta", Dimensions: entityBeta)));

        var trial = projection.BuildTrialBalance(entries);

        trial.Where(line => line.AccountCode == "Cash").Should().HaveCount(2);
        var alphaCash = trial.Single(line => line.AccountCode == "Cash" && line.Net == 125m);
        alphaCash.Dimensions.Should().NotBeNull();
        alphaCash.Dimensions!.EntityId.Should().Be("entity-alpha");
        alphaCash.Dimensions.ExternalGlDimensions["Department"].Should().Be("Investments");
        alphaCash.SourceEventIds.Should().Contain("evt-entity-alpha");
        var betaCash = trial.Single(line => line.AccountCode == "Cash" && line.Net == 75m);
        betaCash.Dimensions.Should().NotBeNull();
        betaCash.Dimensions!.EntityId.Should().Be("entity-beta");
        betaCash.Dimensions.ExternalGlDimensions["Department"].Should().Be("Operations");
        betaCash.SourceEventIds.Should().Contain("evt-entity-beta");
        projection.IsBalanced(trial).Should().BeTrue();
    }

    [Fact]
    public void Scenario_MonthEndTrialBalance_FiltersByRequestedDimensionScope()
    {
        var projection = new TrialBalanceProjectionService();
        var entityAlpha = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha", CostCenterId: "investment-ops");
        var entityBeta = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-beta", CostCenterId: "investment-ops");
        var entries = ImmutableArray.Create(
            NewEntry(
                "evt-alpha",
                "approval-alpha",
                new JournalLine("Cash", 40m, "USD", true, "evt-alpha", "approval-alpha", Dimensions: entityAlpha),
                new JournalLine("Revenue", 40m, "USD", false, "evt-alpha", "approval-alpha", Dimensions: entityAlpha)),
            NewEntry(
                "evt-beta",
                "approval-beta",
                new JournalLine("Cash", 60m, "USD", true, "evt-beta", "approval-beta", Dimensions: entityBeta),
                new JournalLine("Revenue", 60m, "USD", false, "evt-beta", "approval-beta", Dimensions: entityBeta)));

        var filtered = projection.BuildTrialBalance(entries, new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha"));

        filtered.Should().HaveCount(2);
        filtered.Select(line => line.Dimensions?.EntityId).Should().OnlyContain(entityId => entityId == "entity-alpha");
        filtered.Sum(line => line.Debit).Should().Be(40m);
        filtered.Sum(line => line.Credit).Should().Be(40m);
        filtered.SelectMany(line => line.SourceEventIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should()
            .BeEquivalentTo(["evt-alpha"]);
        projection.IsBalanced(filtered).Should().BeTrue();
    }

    [Fact]
    public void Scenario_MonthEndRollForward_PreservesDimensionScopedRows()
    {
        var projection = new TrialBalanceProjectionService();
        var entityAlpha = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha", CostCenterId: "investment-ops");
        var entityBeta = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-beta", CostCenterId: "fund-admin");
        var opening = ImmutableArray.Create(
            new TrialBalanceLine(
                "Cash",
                500m,
                0m,
                500m,
                SourceEventIds: ImmutableArray.Create("opening-alpha"),
                ApprovalIds: ImmutableArray.Create("approval-opening-alpha"),
                Dimensions: entityAlpha),
            new TrialBalanceLine(
                "Cash",
                250m,
                0m,
                250m,
                SourceEventIds: ImmutableArray.Create("opening-beta"),
                ApprovalIds: ImmutableArray.Create("approval-opening-beta"),
                Dimensions: entityBeta));
        var activity = ImmutableArray.Create(
            new TrialBalanceLine(
                "Cash",
                75m,
                0m,
                75m,
                SourceEventIds: ImmutableArray.Create("activity-alpha"),
                ApprovalIds: ImmutableArray.Create("approval-activity-alpha"),
                Dimensions: entityAlpha),
            new TrialBalanceLine(
                "Cash",
                25m,
                0m,
                25m,
                SourceEventIds: ImmutableArray.Create("activity-beta"),
                ApprovalIds: ImmutableArray.Create("approval-activity-beta"),
                Dimensions: entityBeta));

        var rollForward = projection.BuildRollForward(opening, activity, []);

        rollForward.Should().HaveCount(2);
        var alpha = rollForward.Single(line => line.Dimensions?.EntityId == "entity-alpha");
        alpha.OpeningBalance.Should().Be(500m);
        alpha.Activity.Should().Be(75m);
        alpha.ClosingBalance.Should().Be(575m);
        alpha.Dimensions!.CostCenterId.Should().Be("investment-ops");
        alpha.SourceEventIds.Should().BeEquivalentTo(["activity-alpha"]);
        alpha.ApprovalIds.Should().BeEquivalentTo(["approval-activity-alpha"]);
        var beta = rollForward.Single(line => line.Dimensions?.EntityId == "entity-beta");
        beta.OpeningBalance.Should().Be(250m);
        beta.Activity.Should().Be(25m);
        beta.ClosingBalance.Should().Be(275m);
        beta.Dimensions!.CostCenterId.Should().Be("fund-admin");
        beta.SourceEventIds.Should().BeEquivalentTo(["activity-beta"]);
        beta.ApprovalIds.Should().BeEquivalentTo(["approval-activity-beta"]);
    }

    [Fact]
    public void Scenario_MonthEndRollForward_PreservesDimensionScopedFxAdjustments()
    {
        var projection = new TrialBalanceProjectionService();
        var fx = new FxTranslationService();
        var entityAlpha = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            BookId: "book-gaap",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var entityBeta = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-beta",
            BookId: "book-gaap",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Operations"
            });
        var activity = ImmutableArray.Create(
            new TrialBalanceLine(
                "Cash",
                100m,
                0m,
                100m,
                SourceEventIds: ImmutableArray.Create("activity-alpha"),
                ApprovalIds: ImmutableArray.Create("approval-activity-alpha"),
                Dimensions: entityAlpha),
            new TrialBalanceLine(
                "Cash",
                50m,
                0m,
                50m,
                SourceEventIds: ImmutableArray.Create("activity-beta"),
                ApprovalIds: ImmutableArray.Create("approval-activity-beta"),
                Dimensions: entityBeta));
        var adjustments = fx.TranslateTrialBalance(
            "ledger-a",
            new DateOnly(2026, 03, 31),
            activity,
            new FxRate("EUR", "USD", new DateOnly(2026, 03, 31), 1.10m, "fx:ecb:20260331", "ECB-EURUSD-20260331"));

        var rollForward = projection.BuildRollForward([], activity, adjustments);

        adjustments.Should().HaveCount(2);
        adjustments.Should().OnlyContain(adjustment => adjustment.Dimensions != null);
        adjustments.Select(adjustment => adjustment.AdjustmentId).Should().OnlyHaveUniqueItems();
        var alpha = rollForward.Single(line => line.Dimensions?.EntityId == "entity-alpha");
        alpha.Activity.Should().Be(100m);
        alpha.TranslationAdjustment.Should().Be(10m);
        alpha.ClosingBalance.Should().Be(110m);
        alpha.Dimensions!.ExternalGlDimensions["Department"].Should().Be("Investments");
        alpha.SourceEventIds.Should().BeEquivalentTo(["activity-alpha", "fx:ecb:20260331"]);
        alpha.ApprovalIds.Should().BeEquivalentTo(["approval-activity-alpha"]);
        var beta = rollForward.Single(line => line.Dimensions?.EntityId == "entity-beta");
        beta.Activity.Should().Be(50m);
        beta.TranslationAdjustment.Should().Be(5m);
        beta.ClosingBalance.Should().Be(55m);
        beta.Dimensions!.ExternalGlDimensions["Department"].Should().Be("Operations");
        beta.SourceEventIds.Should().BeEquivalentTo(["activity-beta", "fx:ecb:20260331"]);
        beta.ApprovalIds.Should().BeEquivalentTo(["approval-activity-beta"]);
    }

    [Fact]
    public void Scenario_MonthEndPosting_OutOfBalanceJournalIsRejectedBeforeReplay()
    {
        var posting = new AccountingPostingService();
        var entry = NewEntry("evt-oob", "approval-oob", new JournalLine("Cash", 100m, "USD", true, "evt-oob", "approval-oob"));

        var result = posting.PostWithResult("ledger-a", [entry]);

        result.Accepted.Should().BeFalse();
        result.RejectedReasons.Should().ContainSingle(reason => reason.Contains("out of balance", StringComparison.OrdinalIgnoreCase));
        posting.Replay("ledger-a").Should().BeEmpty();
    }

    [Fact]
    public void Scenario_MonthEndPosting_ClosedPeriodRejectsLateJournal()
    {
        var posting = new AccountingPostingService();
        var lockedPeriod = new ClosePeriod(
            "ledger-a",
            new DateOnly(2026, 03, 01),
            ClosePeriodState.Closed,
            new CloseEvidence(true, true, true),
            [],
            DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
            "controller");

        var result = posting.PostWithResult("ledger-a", [BalancedEntry("evt-late", "approval-late")], lockedPeriod);

        result.Accepted.Should().BeFalse();
        result.RejectedReasons.Should().ContainSingle(reason => reason.Contains("locked", StringComparison.OrdinalIgnoreCase));
        posting.Replay("ledger-a").Should().BeEmpty();
    }

    [Fact]
    public void Scenario_MonthEndClose_EvidenceChecksGateClosedState()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(false, false, false), []);
        var evidence = new CloseEvidence(
            TrialBalanceSignedOff: true,
            ReconciliationSignedOff: true,
            ApprovalsCompleted: true,
            Checks: ImmutableArray.Create(new CloseEvidenceCheck("packet", "Controller packet", true, false, "evt-close", "approval-close", "Controller approval is pending.")));

        var next = stateMachine.Transition(current, evidence, isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Blocked);
        next.Blockers.Should().ContainSingle(reason => reason.Contains("Controller packet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scenario_MonthEndClose_AllGatesPassingLocksThePeriod()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(true, true, true), []);

        var next = stateMachine.Transition(current, new CloseEvidence(true, true, true), isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Closed);
        next.Blockers.Should().BeEmpty();
        next.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Scenario_MonthEndPosting_ReplayOrdersByPeriodDateAndJournalIdWithAuditLineage()
    {
        var posting = new AccountingPostingService();
        var idB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var idA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        posting.Post("ledger-a", [
            BalancedEntry("evt-b", "approval-b", idB),
            BalancedEntry("evt-a", "approval-a", idA)
        ]);

        var replay = posting.Replay("ledger-a");
        var audit = posting.Audit("ledger-a");
        replay[0].JournalEntryId.Should().Be(idA);
        replay[1].JournalEntryId.Should().Be(idB);
        audit[0].SourceEventId.Should().Be("evt-a");
        audit[0].ApprovalId.Should().Be("approval-a");
        audit[0].AccountCodes.Should().BeEquivalentTo("Cash", "Revenue");
    }

    private static JournalEntry BalancedEntry(string sourceEventId, string approvalId, Guid? journalEntryId = null)
        => new(
            journalEntryId ?? Guid.NewGuid(),
            "ledger-a",
            new DateOnly(2026, 03, 02),
            sourceEventId,
            "balanced accrual",
            ImmutableArray.Create(
                new JournalLine("Cash", 100m, "USD", true, sourceEventId, approvalId),
                new JournalLine("Revenue", 100m, "USD", false, sourceEventId, approvalId)));

    private static JournalEntry NewEntry(string sourceEventId, string approvalId, params JournalLine[] lines)
        => new(
            Guid.NewGuid(),
            "ledger-a",
            new DateOnly(2026, 03, 31),
            sourceEventId,
            "month-end source event",
            lines.Select(line => string.IsNullOrWhiteSpace(line.ApprovalId) ? line with { ApprovalId = approvalId } : line).ToImmutableArray());

    private static OperationsContinuityWorkflowDto BuildCloseWorkflow(
        Guid workflowId,
        string firstTaskStatus,
        string secondTaskStatus,
        string? firstTaskAcknowledgedBy = null,
        string? secondTaskAcknowledgedBy = null)
    {
        var now = DateTimeOffset.Parse("2026-04-01T00:00:00Z");
        return new OperationsContinuityWorkflowDto(
            workflowId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "2026-03",
            SecurityMasterSnapshotId: null,
            BrokerSource: "custodian",
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            Version: 7,
            Status: OperationsWorkflowStatusDto.ApprovalPending,
            BrokerIntakeState: OperationsBrokerIntakeStateDto.Complete,
            SecurityMasterState: OperationsSecurityMasterStateDto.Complete,
            LedgerPostingState: OperationsLedgerPostingStateDto.Complete,
            ReconciliationState: OperationsReconciliationStateDto.Complete,
            ApprovalState: OperationsApprovalStateDto.Pending,
            Gates: [],
            Timeline: [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(false, null, "Awaiting close certification.", []),
            CloseChecklist:
            [
                new OperationsCloseChecklistTaskDto(
                    "reconciliation-review",
                    OperationsGateKeyDto.Reconciliation,
                    "Reconciliation review",
                    "Controller",
                    "Retained reconciliation approval evidence",
                    RequiredApprovalCount: 1,
                    ExpiresOn: null,
                    DueDate: new DateOnly(2026, 04, 2),
                    Status: firstTaskStatus,
                    BlockingReason: null,
                    EvidencePointer: null,
                    RemediationRoute: "/workstation/accounting/close",
                    CanAcknowledge: true,
                    AcknowledgedAtUtc: firstTaskAcknowledgedBy is null ? null : now,
                    AcknowledgedBy: firstTaskAcknowledgedBy),
                new OperationsCloseChecklistTaskDto(
                    "report-certification",
                    OperationsGateKeyDto.Approval,
                    "Report certification",
                    "Controller",
                    "Retained report certification evidence",
                    RequiredApprovalCount: 1,
                    ExpiresOn: null,
                    DueDate: new DateOnly(2026, 04, 3),
                    Status: secondTaskStatus,
                    BlockingReason: null,
                    EvidencePointer: null,
                    RemediationRoute: "/workstation/reporting",
                    CanAcknowledge: true,
                    AcknowledgedAtUtc: secondTaskAcknowledgedBy is null ? null : now,
                    AcknowledgedBy: secondTaskAcknowledgedBy)
            ],
            EvidenceLinks: [],
            Blockers: [],
            NextActions: [],
            LedgerBookId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
    }
}
