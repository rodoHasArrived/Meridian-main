using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task LedgerCrossPeriodReports_WhenSummaryLedgerBookDrifts_ShouldFailClosed()
    {
        var periodId = Guid.Parse("aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa");
        var periodLedgerBookId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var driftedSummaryLedgerBookId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var period = new LedgerPeriodDto(
            periodId,
            periodLedgerBookId,
            FiscalYear: 2026,
            PeriodNo: 12,
            Label: "December 2026",
            StartDate: new DateOnly(2026, 12, 1),
            EndDate: new DateOnly(2026, 12, 31),
            Status: LedgerPeriodStatusDto.HardClosed,
            OpenedAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            ClosedAt: new DateTimeOffset(2027, 1, 3, 18, 0, 0, TimeSpan.Zero),
            Version: 4);
        var summary = new LedgerPeriodSummaryDto(
            periodId,
            driftedSummaryLedgerBookId,
            FiscalYear: 2026,
            PeriodNo: 12,
            Label: "December 2026",
            TrialBalance:
            [
                new LedgerPeriodTrialBalanceLineDto(
                    "Cash",
                    "Asset",
                    Symbol: null,
                    FinancialAccountId: "gl-cash",
                    DebitTotal: 1_000m,
                    CreditTotal: 0m,
                    Balance: 1_000m,
                    EntryCount: 1)
            ],
            TotalDebits: 1_000m,
            TotalCredits: 1_000m,
            NetIncome: 0m,
            PeriodOnPeriodVariance: null,
            OpenBreakCount: 0,
            SignoffStatus: LedgerPeriodSignoffStatusDto.SignedOff,
            CompletedAt: new DateTimeOffset(2027, 1, 3, 18, 0, 0, TimeSpan.Zero));

        await using var app = await CreateAppAsync(
            services => services.AddSingleton<ILedgerBookService>(new DriftedLedgerBookReportService(period, summary)),
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        foreach (var route in new[] { UiApiRoutes.LedgerReportsTrialBalance, UiApiRoutes.LedgerReportsPnlSummary })
        {
            using var response = await client.GetAsync($"{route}?ledgerBookId={periodLedgerBookId:D}&fundProfileId=fund-alpha");

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("Closed-period summary");
            body.Should().Contain(driftedSummaryLedgerBookId.ToString("D"));
            body.Should().Contain(periodLedgerBookId.ToString("D"));
        }
    }

    [Fact]
    public async Task LedgerCrossPeriodTrialBalanceReport_ShouldFilterByCanonicalDimensionAliases()
    {
        var periodId = Guid.Parse("aaaaaaaa-2222-4222-8222-aaaaaaaaaaaa");
        var ledgerBookId = Guid.Parse("11111111-2222-4222-8222-111111111111");
        var instrumentId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var period = new LedgerPeriodDto(
            periodId,
            ledgerBookId,
            FiscalYear: 2026,
            PeriodNo: 12,
            Label: "December 2026",
            StartDate: new DateOnly(2026, 12, 1),
            EndDate: new DateOnly(2026, 12, 31),
            Status: LedgerPeriodStatusDto.HardClosed,
            OpenedAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            ClosedAt: new DateTimeOffset(2027, 1, 3, 18, 0, 0, TimeSpan.Zero),
            Version: 4);
        var dimensions = new LedgerDimensionSetDto(
            FundId: " fund-alpha ",
            EntityId: " entity-master ",
            SleeveId: " sleeve-alpha ",
            StrategyId: "strategy-credit",
            InvestorId: "investor-lp-1",
            CapitalAccountId: "capital-lp-1",
            InstrumentId: instrumentId,
            TaxLotId: "taxlot-2026-001",
            CostCenterId: "cc-investments",
            CounterpartyId: "counterparty-bank",
            ExternalGlDimensions: new Dictionary<string, string>
            {
                [" Department "] = " InvestmentOps ",
                ["department"] = "ShadowDepartment"
            },
            OrganizationId: "org-operations",
            PortfolioId: "portfolio-credit",
            BookId: "book-gaap",
            AccountId: "acct-investments",
            CustomerId: "customer-investor-services",
            VendorId: "vendor-custodian",
            ProjectId: "project-close");
        var summary = new LedgerPeriodSummaryDto(
            periodId,
            ledgerBookId,
            FiscalYear: 2026,
            PeriodNo: 12,
            Label: "December 2026",
            TrialBalance:
            [
                new LedgerPeriodTrialBalanceLineDto(
                    "Investments",
                    "Asset",
                    Symbol: null,
                    FinancialAccountId: "gl-investments",
                    DebitTotal: 1_000m,
                    CreditTotal: 0m,
                    Balance: 1_000m,
                    EntryCount: 1,
                    Dimensions: dimensions),
                new LedgerPeriodTrialBalanceLineDto(
                    "Management fees",
                    "Expense",
                    Symbol: null,
                    FinancialAccountId: "gl-fees",
                    DebitTotal: 250m,
                    CreditTotal: 0m,
                    Balance: 250m,
                    EntryCount: 1,
                    Dimensions: dimensions with { FundId = "fund-beta" })
            ],
            TotalDebits: 1_250m,
            TotalCredits: 0m,
            NetIncome: -250m,
            PeriodOnPeriodVariance: null,
            OpenBreakCount: 0,
            SignoffStatus: LedgerPeriodSignoffStatusDto.SignedOff,
            CompletedAt: new DateTimeOffset(2027, 1, 3, 18, 0, 0, TimeSpan.Zero));

        await using var app = await CreateAppAsync(
            services => services.AddSingleton<ILedgerBookService>(new DriftedLedgerBookReportService(period, summary)),
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsTrialBalance}?ledgerBookId={ledgerBookId:D}&dimensionFundId=fund-alpha&dimensionEntityId=entity-master&dimensionSleeveId=sleeve-alpha&dimensionStrategyId=strategy-credit&dimensionInvestorId=investor-lp-1&dimensionCapitalAccountId=capital-lp-1&dimensionInstrumentId={instrumentId:D}&dimensionBookId=book-gaap&dimensionOrganizationId=org-operations&dimensionPortfolioId=portfolio-credit&dimensionAccountId=acct-investments&dimensionCustomerId=customer-investor-services&dimensionVendorId=vendor-custodian&dimensionProjectId=project-close&dimensionTaxLotId=taxlot-2026-001&dimensionCostCenterId=cc-investments&dimensionCounterpartyId=counterparty-bank&externalGlDimensionKey=Department&externalGlDimensionValue=InvestmentOps");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var lines = document.RootElement.GetProperty("lines");
        lines.GetArrayLength().Should().Be(1);
        var line = lines[0];
        line.GetProperty("accountName").GetString().Should().Be("Investments");
        var returnedDimensions = line.GetProperty("dimensions");
        returnedDimensions.GetProperty("fundId").GetString().Should().Be("fund-alpha");
        returnedDimensions.GetProperty("entityId").GetString().Should().Be("entity-master");
        returnedDimensions.GetProperty("sleeveId").GetString().Should().Be("sleeve-alpha");
        returnedDimensions.GetProperty("strategyId").GetString().Should().Be("strategy-credit");
        returnedDimensions.GetProperty("investorId").GetString().Should().Be("investor-lp-1");
        returnedDimensions.GetProperty("capitalAccountId").GetString().Should().Be("capital-lp-1");
        returnedDimensions.GetProperty("instrumentId").GetGuid().Should().Be(instrumentId);
        returnedDimensions.GetProperty("bookId").GetString().Should().Be("book-gaap");
        returnedDimensions.GetProperty("organizationId").GetString().Should().Be("org-operations");
        returnedDimensions.GetProperty("portfolioId").GetString().Should().Be("portfolio-credit");
        returnedDimensions.GetProperty("accountId").GetString().Should().Be("acct-investments");
        returnedDimensions.GetProperty("customerId").GetString().Should().Be("customer-investor-services");
        returnedDimensions.GetProperty("vendorId").GetString().Should().Be("vendor-custodian");
        returnedDimensions.GetProperty("projectId").GetString().Should().Be("project-close");
        returnedDimensions.GetProperty("taxLotId").GetString().Should().Be("taxlot-2026-001");
        returnedDimensions.GetProperty("costCenterId").GetString().Should().Be("cc-investments");
        returnedDimensions.GetProperty("counterpartyId").GetString().Should().Be("counterparty-bank");
        returnedDimensions.GetProperty("externalGlDimensions").GetProperty("Department").GetString().Should().Be("InvestmentOps");
        var canonicalTotalDebits = document.RootElement.GetProperty("totalDebits").GetDecimal();

        using var messyEquivalentResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsTrialBalance}?ledgerBookId={ledgerBookId:D}&dimensionFundId=%20fund-alpha%20&dimensionEntityId=%20entity-master%20&dimensionSleeveId=%20sleeve-alpha%20&dimensionStrategyId=strategy-credit&dimensionInvestorId=investor-lp-1&dimensionCapitalAccountId=capital-lp-1&dimensionInstrumentId={instrumentId:D}&dimensionBookId=book-gaap&dimensionOrganizationId=org-operations&dimensionPortfolioId=portfolio-credit&dimensionAccountId=acct-investments&dimensionCustomerId=customer-investor-services&dimensionVendorId=vendor-custodian&dimensionProjectId=project-close&dimensionTaxLotId=taxlot-2026-001&dimensionCostCenterId=cc-investments&dimensionCounterpartyId=counterparty-bank&externalGlDimensionKey=%20Department%20&externalGlDimensionValue=%20InvestmentOps%20");

        messyEquivalentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var messyEquivalentDocument = await JsonDocument.ParseAsync(await messyEquivalentResponse.Content.ReadAsStreamAsync());
        messyEquivalentDocument.RootElement.GetProperty("lines").GetArrayLength().Should().Be(1);
        messyEquivalentDocument.RootElement.GetProperty("totalDebits").GetDecimal().Should().Be(canonicalTotalDebits);
        var messyEquivalentDimensions = messyEquivalentDocument.RootElement.GetProperty("lines")[0].GetProperty("dimensions");
        messyEquivalentDimensions.GetProperty("fundId").GetString().Should().Be("fund-alpha");
        messyEquivalentDimensions.GetProperty("externalGlDimensions").GetProperty("Department").GetString().Should().Be("InvestmentOps");

        using var mismatchResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsTrialBalance}?ledgerBookId={ledgerBookId:D}&dimensionFundId=fund-missing&dimensionBookId=book-gaap");

        mismatchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var mismatchDocument = await JsonDocument.ParseAsync(await mismatchResponse.Content.ReadAsStreamAsync());
        mismatchDocument.RootElement.GetProperty("lines").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyMatrix_ExposesServerOwnedRules()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.OperationsContinuityApprovalPolicyMatrix);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var matrix = await response.Content.ReadFromJsonAsync<OperationsApprovalPolicyMatrixDto>(ServerJsonOptions);
        matrix.Should().NotBeNull();
        matrix!.Rows.Should().Contain(row =>
            row.PolicyKey == "operations-continuity.approve" &&
            row.Gate == OperationsGateKeyDto.Approval &&
            row.RequiresIndependentReviewer &&
            row.RequiredDistinctApprovals == 2 &&
            row.RequiresReportPack &&
            row.RequiresChecklistControlApprovals &&
            row.AuditEventType == "approval-approved");
        matrix.Rows.Should().Contain(row =>
            row.PolicyKey == "operations-continuity.reopen" &&
            row.RequiredPermission == nameof(UserPermission.AdminMaintenance));
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyRuleUpsert_UpdatesMatrixWithAuditEvidence()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityApprovalPolicyRules,
            new OperationsApprovalPolicyRuleUpsertRequestDto(
                PolicyKey: "operations-continuity.approve",
                WorkflowArea: "Operations close",
                Action: "Approve submitted workflow",
                Gate: OperationsGateKeyDto.Approval,
                Trigger: "Workflow is submitted and assigned reviewer evidence is present.",
                RequiredPermission: nameof(UserPermission.AdminMaintenance),
                SubmitterRole: "Accounting operator",
                ReviewerRole: "Controller",
                RequiredDistinctApprovals: 3,
                RequiresIndependentReviewer: true,
                RequiresReportPack: true,
                RequiresChecklistControlApprovals: true,
                EvidenceRequirement: "Controller packet plus three distinct approval-gate control approvals.",
                AuditEventType: "approval-approved",
                Route: UiApiRoutes.OperationsContinuityApprovalApprove,
                Severity: "Critical",
                RequestedBy: "untrusted-browser-user",
                Rationale: "Tighten close approval governance for controller review.",
                CorrelationId: "approval-policy-test"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationsApprovalPolicyRuleUpsertResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Rule.PolicyKey.Should().Be("operations-continuity.approve");
        result.Rule.RequiredDistinctApprovals.Should().Be(3);
        result.AuditEvent.Actor.Should().Be("ops-user");
        result.AuditEvent.CorrelationId.Should().Be("approval-policy-test");
        result.Matrix.Rows.Should().Contain(row =>
            row.PolicyKey == "operations-continuity.approve" &&
            row.RequiredDistinctApprovals == 3 &&
            row.ReviewerRole == "Controller");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyRuleUpsert_WithoutAdminMaintenance_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityApprovalPolicyRules,
            new OperationsApprovalPolicyRuleUpsertRequestDto(
                PolicyKey: "operations-continuity.approve",
                WorkflowArea: "Operations close",
                Action: "Approve submitted workflow",
                Gate: OperationsGateKeyDto.Approval,
                Trigger: "Workflow is submitted.",
                RequiredPermission: nameof(UserPermission.AdminMaintenance),
                SubmitterRole: "Accounting operator",
                ReviewerRole: "Controller",
                RequiredDistinctApprovals: 2,
                RequiresIndependentReviewer: true,
                RequiresReportPack: true,
                RequiresChecklistControlApprovals: true,
                EvidenceRequirement: "Controller packet.",
                AuditEventType: "approval-approved",
                Route: UiApiRoutes.OperationsContinuityApprovalApprove,
                Severity: "Critical",
                RequestedBy: "ops-user",
                Rationale: "Should be rejected."));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalPolicyRuleUpsert_InvalidApprovalCount_ReturnsBadRequest()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityApprovalPolicyRules,
            new OperationsApprovalPolicyRuleUpsertRequestDto(
                PolicyKey: "operations-continuity.approve",
                WorkflowArea: "Operations close",
                Action: "Approve submitted workflow",
                Gate: OperationsGateKeyDto.Approval,
                Trigger: "Workflow is submitted.",
                RequiredPermission: nameof(UserPermission.AdminMaintenance),
                SubmitterRole: "Accounting operator",
                ReviewerRole: "Controller",
                RequiredDistinctApprovals: 0,
                RequiresIndependentReviewer: true,
                RequiresReportPack: true,
                RequiresChecklistControlApprovals: true,
                EvidenceRequirement: "Controller packet.",
                AuditEventType: "approval-approved",
                Route: UiApiRoutes.OperationsContinuityApprovalApprove,
                Severity: "Critical",
                RequestedBy: "ops-user",
                Rationale: "Invalid count."));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendar_ExposesWorkflowDuePosture()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();
        var ledgerBookId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-07",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor",
                LedgerBookId: ledgerBookId));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);

        using var calendarResponse = await client.GetAsync($"{UiApiRoutes.OperationsContinuityCloseCalendar}?fundAccountId={fundAccountId}&periodId=2026-07");

        calendarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var calendar = await calendarResponse.Content.ReadFromJsonAsync<OperationsCloseCalendarDto>(ServerJsonOptions);
        calendar.Should().NotBeNull();
        var calendarItem = calendar!.Items.Should().ContainSingle(item =>
            item.WorkflowId == start!.Workflow!.WorkflowId &&
            item.FundAccountId == fundAccountId &&
            item.PeriodId == "2026-07" &&
            item.Status == OperationsWorkflowStatusDto.CollectingBrokerData &&
            item.NextDueTaskId == "close-gate-brokeringest" &&
            item.OpenChecklistCount > 0 &&
            item.RequiredApprovalCount > 0 &&
            item.Route.Contains(start.Workflow.WorkflowId.ToString(), StringComparison.OrdinalIgnoreCase)).Subject;
        calendarItem.ReadinessScore.Should().BeLessThan(100);
        calendarItem.ReadinessComponents.Should().NotBeNull();
        calendarItem.ReadinessComponents!.Select(static component => component.Key)
            .Should()
            .BeEquivalentTo(
            [
                "security-master",
                "provider-freshness",
                "positions",
                "cash",
                "ledger",
                "pricing",
                "reconciliation",
                "reports",
                "approvals"
            ]);
        calendarItem.ReadinessBlockers.Should().NotBeNullOrEmpty();
        calendarItem.ReadinessBlockers!.Select(static blocker => blocker.Code)
            .Should()
            .Contain(
            [
                "SECURITY_MASTER_RESOLUTION_REQUIRED",
                "BROKER_SYNC_STALE",
                "BROKER_CASH_COVERAGE_INCOMPLETE",
                "LEDGER_POSTING_REQUIRED",
                "RECONCILIATION_CRITICAL_BREAKS_OPEN",
                "REPORT_PACK_REQUIRED",
                "APPROVAL_REQUIRED"
            ]);
        calendarItem.ReadinessNextActions.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LedgerCloseManagementEndpoints_ProjectClosePlanAndRetainLateAdjustment()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();
        var ledgerBookId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-07",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor",
                LedgerBookId: ledgerBookId));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;
        start.Workflow.LedgerBookId.Should().Be(ledgerBookId);

        using var listResponse = await client.GetAsync($"{UiApiRoutes.OperationsContinuity}?fundAccountId={fundAccountId:D}&periodId=2026-07&ledgerBookId={ledgerBookId:D}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflowSummaries = await listResponse.Content.ReadFromJsonAsync<List<OperationsContinuityWorkflowSummaryDto>>(ServerJsonOptions);
        workflowSummaries.Should().ContainSingle();
        workflowSummaries![0].WorkflowId.Should().Be(workflowId);
        workflowSummaries[0].LedgerBookId.Should().Be(ledgerBookId);

        var planRoute = UiApiRoutes.LedgerCloseManagementPeriodPlan.Replace("{workflowId:guid}", workflowId.ToString("D"));

        using var planResponse = await client.GetAsync(planRoute);

        planResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await planResponse.Content.ReadFromJsonAsync<ClosePeriodPlanDto>(ServerJsonOptions);
        plan.Should().NotBeNull();
        plan!.ClosePlanId.Should().Be($"close-plan-{workflowId:D}");
        plan.FundProfileId.Should().Be(fundAccountId.ToString("D"));
        plan.LedgerBookId.Should().Be(ledgerBookId);
        plan.PeriodId.Should().Be("2026-07");
        plan.PeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        plan.PeriodEnd.Should().Be(new DateOnly(2026, 7, 31));
        plan.IsPeriodLocked.Should().BeFalse();
        plan.MaterialityPolicy.RequiresLateAdjustmentApproval.Should().BeTrue();
        plan.Tasks.Should().NotBeEmpty();
        plan.Tasks.Skip(1).Should().OnlyContain(task => task.Dependencies.Count > 0);
        plan.CloseCalendar.Should().HaveSameCount(plan.Tasks);
        plan.CloseCalendar.Select(static milestone => milestone.DueDate).Should().BeInAscendingOrder();
        var firstMilestone = plan.CloseCalendar.Should().ContainSingle(milestone => milestone.TaskId == plan.Tasks[0].TaskId).Subject;
        firstMilestone.DisplayName.Should().Be(plan.Tasks[0].DisplayName);
        firstMilestone.Owner.Should().Be(plan.Tasks[0].Owner);
        firstMilestone.Status.Should().Be(plan.Tasks[0].Status);
        firstMilestone.RequiredSignOffCount.Should().Be(plan.Tasks[0].SignOffRequirements.Sum(static requirement => requirement.RequiredApprovalCount));
        firstMilestone.ApprovedSignOffCount.Should().Be(0);
        firstMilestone.IsPeriodLocked.Should().BeFalse();
        var taskToSignOff = plan.Tasks[0];
        var requiredSignOff = taskToSignOff.SignOffRequirements.Should().ContainSingle().Subject;
        requiredSignOff.Role.Should().Be(taskToSignOff.Owner);
        requiredSignOff.RequiredApprovalCount.Should().BeGreaterThan(0);
        requiredSignOff.ApprovedCount.Should().Be(0);
        requiredSignOff.IsSatisfied.Should().BeFalse();
        requiredSignOff.EvidenceRequirement.Should().Contain("Evidence link");
        var requiredRole = requiredSignOff.Role;
        var dependentTask = plan.Tasks.Skip(1).FirstOrDefault();
        if (dependentTask is not null)
        {
            var dependentRole = dependentTask.SignOffRequirements.Should().ContainSingle().Subject.Role;
            using var blockedSignOffResponse = await client.PostAsJsonAsync(
                UiApiRoutes.LedgerCloseManagementTaskSignOffs,
                new SignOffCloseTaskRequestDto(
                    workflowId,
                    dependentTask.TaskId,
                    dependentRole,
                    ManualJournalEntryStatusDto.Approved,
                    "untrusted-controller",
                    "Controller should not sign off a task before dependencies are complete.",
                    EvidenceLinks: [$"evidence:close-task:{dependentTask.TaskId}:{dependentRole}:2026-07:book:{ledgerBookId:D}:dependency-block-signoff"]),
                ServerJsonOptions);

            blockedSignOffResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        using var assistantSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "assistant",
                "Assistant drafted sign-off should not satisfy close control.",
                EvidenceLinks: [$"evidence:close-task:{taskToSignOff.TaskId}:Controller:2026-07:controller-signoff"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            ServerJsonOptions);

        assistantSignOffResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var weakEvidenceSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller tried to sign off with generic support evidence.",
                EvidenceLinks: ["evidence:close-task:support-packet"]),
            ServerJsonOptions);

        weakEvidenceSignOffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var wrongTaskEvidenceSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller tried to sign off with evidence for another close period.",
                EvidenceLinks: ["evidence:close-task:other-task:Controller:2026-08:controller-signoff"]),
            ServerJsonOptions);

        wrongTaskEvidenceSignOffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var wrongRoleEvidenceSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller tried to sign off with evidence for a different close role.",
                EvidenceLinks: [$"evidence:close-task:{taskToSignOff.TaskId}:tax-reviewer:2026-07:approval"]),
            ServerJsonOptions);

        wrongRoleEvidenceSignOffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var splitEvidenceSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller tried to sign off with split evidence.",
                EvidenceLinks:
                [
                    "evidence:close-task:support-signoff",
                    $"evidence:close-task:{taskToSignOff.TaskId}:support-packet"
                ]),
            ServerJsonOptions);

        splitEvidenceSignOffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var unassignedRoleSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                "Tax reviewer",
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "A non-matrix role should not sign off this close task.",
                EvidenceLinks: [$"evidence:close-task:{taskToSignOff.TaskId}:tax-reviewer:2026-07:book:{ledgerBookId:D}:tax-reviewer-signoff"]),
            ServerJsonOptions);

        unassignedRoleSignOffResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var missingBookSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                requiredRole,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller tried to sign off with evidence that omits the selected ledger book.",
                EvidenceLinks: [$"evidence:close-task:{taskToSignOff.TaskId}:{requiredRole}:2026-07:controller-signoff"]),
            ServerJsonOptions);

        missingBookSignOffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedBookSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                requiredRole,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller tried to sign off with an extended ledger-book evidence token.",
                EvidenceLinks: [$"evidence:close-task:{taskToSignOff.TaskId}:{requiredRole}:2026-07:book:{ledgerBookId:D}ffff:controller-signoff"]),
            ServerJsonOptions);

        extendedBookSignOffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedTaskSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                requiredRole,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller tried to sign off with an extended close task evidence token.",
                EvidenceLinks: [$"evidence:close-task:{taskToSignOff.TaskId}ffff:{requiredRole}:2026-07:book:{ledgerBookId:D}:controller-signoff"]),
            ServerJsonOptions);

        extendedTaskSignOffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var controllerSignOffEvidence = $"evidence:close-task:{taskToSignOff.TaskId}:{requiredRole}:2026-07:book:{ledgerBookId:D}:controller-signoff";
        using var signOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                requiredRole,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Controller retained close checklist sign-off.",
                EvidenceLinks: [controllerSignOffEvidence]),
            ServerJsonOptions);

        signOffResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var signedOffPlan = await signOffResponse.Content.ReadFromJsonAsync<ClosePeriodPlanDto>(ServerJsonOptions);
        signedOffPlan.Should().NotBeNull();
        var signedOffTask = signedOffPlan!.Tasks.Should().Contain(task => task.TaskId == taskToSignOff.TaskId).Subject;
        signedOffTask.Status.Should().Be(CloseTaskStatusDto.SignedOff);
        var satisfiedRequirement = signedOffTask.SignOffRequirements.Should().ContainSingle().Subject;
        satisfiedRequirement.Role.Should().Be(requiredRole);
        satisfiedRequirement.ApprovedCount.Should().Be(1);
        satisfiedRequirement.IsSatisfied.Should().BeTrue();
        var signedOffMilestone = signedOffPlan.CloseCalendar.Should().ContainSingle(milestone => milestone.TaskId == taskToSignOff.TaskId).Subject;
        signedOffMilestone.IsSatisfied.Should().BeTrue();
        signedOffMilestone.ApprovedSignOffCount.Should().Be(1);
        signedOffMilestone.RequiredSignOffCount.Should().Be(requiredSignOff.RequiredApprovalCount);
        signedOffMilestone.EvidenceLinks.Should().Contain(controllerSignOffEvidence);
        var signOff = signedOffTask.SignOffs.Should().ContainSingle(row => row.Role == requiredRole).Subject;
        signOff.Actor.Should().Be("ops-user");
        signOff.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Approved);
        signOff.SignedAtUtc.Should().NotBeNull();
        signOff.Notes.Should().Be("Controller retained close checklist sign-off.");
        signOff.EvidenceLinks.Should().Contain(controllerSignOffEvidence);

        using var duplicateSignOffResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementTaskSignOffs,
            new SignOffCloseTaskRequestDto(
                workflowId,
                taskToSignOff.TaskId,
                requiredRole,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-controller",
                "Duplicate sign-off should fail.",
                EvidenceLinks: [$"evidence:close-task:{taskToSignOff.TaskId}:{requiredRole}:2026-07:book:{ledgerBookId:D}:duplicate-signoff"]),
            ServerJsonOptions);

        duplicateSignOffResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var assistantAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Assistant drafted late adjustment should not be retained.",
                RequestedBy: "assistant",
                EvidenceLinks: ["evidence:late-adjustment:2026-07:review"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            ServerJsonOptions);

        assistantAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var weakAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment with generic support only.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: ["evidence:close-task:support-packet"]),
            ServerJsonOptions);

        weakAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var wrongPeriodAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment with evidence for the wrong close period.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: ["evidence:late-adjustment:2026-08:review"]),
            ServerJsonOptions);

        wrongPeriodAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedPeriodAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment with an extended close-period evidence token.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: [$"evidence:late-adjustment:2026-070:book:{ledgerBookId:D}:review"]),
            ServerJsonOptions);

        extendedPeriodAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var splitAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment with split evidence.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks:
                [
                    "evidence:late-adjustment:support-packet",
                    "evidence:journal-entry:11111111-1111-1111-1111-111111111111:support-packet"
                ]),
            ServerJsonOptions);

        splitAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var missingBookAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment with evidence that omits the selected ledger book.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: ["evidence:late-adjustment:2026-07:review"]),
            ServerJsonOptions);

        missingBookAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedBookAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment with an extended ledger-book evidence token.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: [$"evidence:late-adjustment:2026-07:book:{ledgerBookId:D}ffff:review"]),
            ServerJsonOptions);

        extendedBookAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedJournalAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment with an extended journal-entry evidence token.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: [$"evidence:late-adjustment:11111111-1111-1111-1111-111111111111ffff:book:{ledgerBookId:D}:review"]),
            ServerJsonOptions);

        extendedJournalAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var requestEvidence = $"evidence:late-adjustment:2026-07:book:{ledgerBookId:D}:review";
        using var adjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Material close adjustment after reconciliation review.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: [requestEvidence]),
            ServerJsonOptions);

        adjustmentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adjustedPlan = await adjustmentResponse.Content.ReadFromJsonAsync<ClosePeriodPlanDto>(ServerJsonOptions);
        adjustedPlan.Should().NotBeNull();
        var adjustment = adjustedPlan!.LateAdjustments.Should().ContainSingle().Subject;
        adjustment.RequestedBy.Should().Be("ops-user");
        adjustment.Currency.Should().Be("USD");
        adjustment.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Submitted);
        adjustment.MaterialityPolicy.AmountThreshold.Should().Be(10_000m);
        adjustment.EvidenceLinks.Should().Contain(requestEvidence);
        adjustedPlan.ValidationIssues.Should().Contain(issue =>
            issue.Code == "LateAdjustmentRequiresApproval" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Warning &&
            issue.TargetId == adjustment.RequestId);

        using var duplicateAdjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: adjustment.JournalEntryId,
                Amount: 15_000m,
                Currency: "usd",
                Reason: "Duplicate material close adjustment request.",
                RequestedBy: "untrusted-browser-user",
                EvidenceLinks: [$"evidence:late-adjustment:{adjustment.JournalEntryId:D}:book:{ledgerBookId:D}:duplicate-review"]),
            ServerJsonOptions);

        duplicateAdjustmentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var assistantReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "assistant",
                "Assistant drafted review should not approve a material late adjustment.",
                EvidenceLinks: [$"evidence:late-adjustment:{adjustment.RequestId}:controller-approval"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            ServerJsonOptions);

        assistantReviewResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var weakReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Generic support packet should not approve a material late adjustment.",
                EvidenceLinks: ["evidence:late-adjustment:support-packet"]),
            ServerJsonOptions);

        weakReviewResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var wrongPeriodReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Controller tried to approve a material late adjustment with wrong-period evidence.",
                EvidenceLinks: ["evidence:late-adjustment:controller-approval:2026-08"]),
            ServerJsonOptions);

        wrongPeriodReviewResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedPeriodReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Controller tried to approve a material late adjustment with extended-period evidence.",
                EvidenceLinks: [$"evidence:late-adjustment:controller-approval:2026-070:book:{ledgerBookId:D}"]),
            ServerJsonOptions);

        extendedPeriodReviewResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var splitReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Controller tried to approve a material late adjustment with split evidence.",
                EvidenceLinks:
                [
                    "evidence:late-adjustment:controller-approval:support-packet",
                    $"evidence:late-adjustment-request:{adjustment.RequestId}:support-packet"
                ]),
            ServerJsonOptions);

        splitReviewResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var missingBookReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Controller tried to approve a material late adjustment with evidence that omits the selected ledger book.",
                EvidenceLinks: [$"evidence:late-adjustment:{adjustment.RequestId}:controller-approval"]),
            ServerJsonOptions);

        missingBookReviewResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedBookReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Controller tried to approve a material late adjustment with an extended ledger-book evidence token.",
                EvidenceLinks: [$"evidence:late-adjustment:{adjustment.RequestId}:book:{ledgerBookId:D}ffff:controller-approval"]),
            ServerJsonOptions);

        extendedBookReviewResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var extendedRequestReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Controller tried to approve a material late adjustment with an extended request evidence token.",
                EvidenceLinks: [$"evidence:late-adjustment:{adjustment.RequestId}ffff:book:{ledgerBookId:D}:controller-approval"]),
            ServerJsonOptions);

        extendedRequestReviewResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var reviewEvidence = $"evidence:late-adjustment:{adjustment.RequestId}:book:{ledgerBookId:D}:controller-approval";
        using var selfReviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "untrusted-reviewer",
                "Requester attempted to approve their own material late adjustment.",
                EvidenceLinks:
                [
                    requestEvidence,
                    reviewEvidence
                ]),
            ServerJsonOptions);

        selfReviewResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var selfReviewBody = await selfReviewResponse.Content.ReadAsStringAsync();
        selfReviewBody.Should().Contain("independent from requester");

        using var reviewRequest = new HttpRequestMessage(HttpMethod.Post, UiApiRoutes.LedgerCloseManagementLateAdjustmentReview)
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new ReviewLateAdjustmentRequestDto(
                    workflowId,
                    adjustment.RequestId,
                    ManualJournalEntryStatusDto.Approved,
                    "untrusted-reviewer",
                    "Controller approved material late adjustment for close package.",
                    EvidenceLinks:
                    [
                        requestEvidence,
                        reviewEvidence
                    ]),
                options: ServerJsonOptions)
        };
        reviewRequest.Headers.Add("X-Meridian-Test-User", "controller-reviewer");
        using var reviewResponse = await client.SendAsync(reviewRequest);

        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviewedPlan = await reviewResponse.Content.ReadFromJsonAsync<ClosePeriodPlanDto>(ServerJsonOptions);
        reviewedPlan.Should().NotBeNull();
        var reviewedAdjustment = reviewedPlan!.LateAdjustments.Should().ContainSingle().Subject;
        reviewedAdjustment.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Approved);
        reviewedAdjustment.DecidedBy.Should().Be("controller-reviewer");
        reviewedAdjustment.DecidedAtUtc.Should().NotBeNull();
        reviewedAdjustment.DecisionNotes.Should().Be("Controller approved material late adjustment for close package.");
        reviewedAdjustment.EvidenceLinks.Should().Contain(reviewEvidence);
        reviewedPlan.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "LateAdjustmentRequiresApproval" &&
            issue.TargetId == adjustment.RequestId);
    }

    [Fact]
    public async Task LedgerCloseManagementService_BlocksLateAdjustmentAfterPeriodLock()
    {
        var workflowId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        var closedWorkflow = new OperationsContinuityWorkflowDto(
            workflowId,
            fundAccountId,
            "2026-12",
            SecurityMasterSnapshotId: null,
            BrokerSource: "custodian",
            CreatedAtUtc: now.AddDays(-2),
            UpdatedAtUtc: now,
            Version: 7,
            OperationsWorkflowStatusDto.Closed,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Complete,
            OperationsReconciliationStateDto.Complete,
            OperationsApprovalStateDto.Approved,
            Gates: [],
            Timeline: [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                true,
                "report-pack-2026-12",
                BlockingReason: null,
                EvidenceLinks: []),
            CloseChecklist: [],
            EvidenceLinks: [],
            Blockers: [],
            NextActions: [],
            ClosePackage: new OperationsClosePackagePublicationDto(
                "close-package-2026-12",
                "report-pack-2026-12",
                "close-manifest-2026-12",
                "/workstation/accounting/close/2026-12",
                "close-evidence-hash",
                now,
                "controller",
                "Final close package signed off.",
                [new OperationsEvidenceLinkDto("close-package-evidence", "Close package", "/evidence/close", "EvidenceVault", now)],
                ChecklistControlApprovals: []));
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(closedWorkflow);
        var service = new AccountingCloseManagementService(workflowService);

        var plan = await service.GetPeriodPlanAsync(workflowId);
        var act = async () => await service.RequestLateAdjustmentAsync(
            new CreateLateAdjustmentRequestDto(
                workflowId,
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                2_500m,
                "usd",
                "Attempt late adjustment after period lock.",
                "ops-user",
                EvidenceLinks: ["evidence:late-adjustment:locked-period"]),
            "ops-user");

        plan.Should().NotBeNull();
        plan!.IsPeriodLocked.Should().BeTrue();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*close period is locked by close package 'close-package-2026-12'*");
    }

    [Fact]
    public async Task LedgerCloseManagementService_TracksMultiApprovalSignOffRequirement()
    {
        var workflowId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var workflowService = Substitute.For<IOperationsContinuityWorkflowService>();
        var workflow = new OperationsContinuityWorkflowDto(
            workflowId,
            fundAccountId,
            "2026-12",
            SecurityMasterSnapshotId: null,
            BrokerSource: "custodian",
            CreatedAtUtc: now.AddDays(-1),
            UpdatedAtUtc: now,
            Version: 3,
            OperationsWorkflowStatusDto.ApprovalPending,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Complete,
            OperationsReconciliationStateDto.Complete,
            OperationsApprovalStateDto.ReviewerAssigned,
            Gates: [],
            Timeline: [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                true,
                "report-pack-2026-12",
                BlockingReason: null,
                EvidenceLinks: []),
            CloseChecklist:
            [
                new OperationsCloseChecklistTaskDto(
                    "approval-control",
                    OperationsGateKeyDto.Approval,
                    "Controller approval",
                    "Controller",
                    "Evidence link and gate completion audit",
                    2,
                    ExpiresOn: null,
                    DueDate: new DateOnly(2026, 12, 31),
                    "Pending",
                    BlockingReason: null,
                    EvidencePointer: null,
                    RemediationRoute: "/workstation/accounting/close",
                    CanAcknowledge: true,
                    AcknowledgedAtUtc: null,
                    AcknowledgedBy: null)
            ],
            EvidenceLinks: [],
            Blockers: [],
            NextActions: []);
        workflowService.GetAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);
        var service = new AccountingCloseManagementService(workflowService);

        var initialPlan = await service.GetPeriodPlanAsync(workflowId);

        var initialRequirement = initialPlan!.Tasks.Should().ContainSingle().Subject.SignOffRequirements.Should().ContainSingle().Subject;
        initialRequirement.Role.Should().Be("Controller");
        initialRequirement.RequiredApprovalCount.Should().Be(2);
        initialRequirement.ApprovedCount.Should().Be(0);
        initialRequirement.IsSatisfied.Should().BeFalse();

        var extendedRoleEvidence = async () => await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "approval-control",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "reviewer-extended-role",
                "Extended role token must not satisfy controller sign-off evidence.",
                EvidenceLinks: ["evidence:close-task:approval-control:ControllerBackup:2026-12:controller-signoff"]),
            "reviewer-extended-role");
        await extendedRoleEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*close task sign-off evidence must reference*");

        var extendedPeriodEvidence = async () => await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "approval-control",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "reviewer-extended-period",
                "Extended period token must not satisfy controller sign-off evidence.",
                EvidenceLinks: ["evidence:close-task:approval-control:Controller:2026-120:controller-signoff"]),
            "reviewer-extended-period");
        await extendedPeriodEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*close task sign-off evidence must reference*");

        var rejectedPlan = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "approval-control",
                "Controller",
                ManualJournalEntryStatusDto.Rejected,
                "reviewer-reject",
                "Controller rejected the first close sign-off review.",
                EvidenceLinks: ["evidence:close-task:approval-control:Controller:2026-12:controller-rejection"]),
            "reviewer-reject");

        var rejectedTask = rejectedPlan!.Tasks.Should().ContainSingle().Subject;
        var rejectedRequirement = rejectedTask.SignOffRequirements.Should().ContainSingle().Subject;
        rejectedRequirement.ApprovedCount.Should().Be(0);
        rejectedRequirement.IsSatisfied.Should().BeFalse();
        rejectedTask.Status.Should().Be(CloseTaskStatusDto.NotStarted);
        rejectedTask.SignOffs.Should().ContainSingle(signOff =>
            signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
            signOff.Actor == "reviewer-reject");

        var firstPlan = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "approval-control",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "reviewer-a",
                "First controller retained close sign-off.",
                EvidenceLinks: ["evidence:close-task:approval-control:Controller:2026-12:controller-signoff-a"]),
            "reviewer-a");

        var firstRequirement = firstPlan!.Tasks.Should().ContainSingle().Subject.SignOffRequirements.Should().ContainSingle().Subject;
        firstRequirement.ApprovedCount.Should().Be(1);
        firstRequirement.IsSatisfied.Should().BeFalse();
        firstPlan.Tasks.Single().Status.Should().Be(CloseTaskStatusDto.InProgress);

        var secondPlan = await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "approval-control",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "reviewer-b",
                "Second controller retained close sign-off.",
                EvidenceLinks: ["evidence:close-task:approval-control:Controller:2026-12:controller-signoff-b"]),
            "reviewer-b");

        var signedOffTask = secondPlan!.Tasks.Should().ContainSingle().Subject;
        var satisfiedRequirement = signedOffTask.SignOffRequirements.Should().ContainSingle().Subject;
        satisfiedRequirement.ApprovedCount.Should().Be(2);
        satisfiedRequirement.IsSatisfied.Should().BeTrue();
        signedOffTask.Status.Should().Be(CloseTaskStatusDto.SignedOff);
        signedOffTask.SignOffs.Should().HaveCount(3);

        var thirdSignOff = async () => await service.SignOffCloseTaskAsync(
            new SignOffCloseTaskRequestDto(
                workflowId,
                "approval-control",
                "Controller",
                ManualJournalEntryStatusDto.Approved,
                "reviewer-c",
                "Third controller sign-off should exceed the requirement.",
                EvidenceLinks: ["evidence:close-task:approval-control:Controller:2026-12:controller-signoff-c"]),
            "reviewer-c");
        await thirdSignOff.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has 2 approved sign-off decision(s) for role 'Controller'*");
    }

    [Fact]
    public async Task LedgerAccountingReportPackageEndpoint_AssemblesCertificationAndRestatementState()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-07",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-alpha",
                PeriodId: "2026-07",
                Actor: "browser-user",
                CloseWorkflowId: start!.Workflow!.WorkflowId,
                CapitalAccountId: "capital-account-lp-1",
                InvestorId: "investor-lp-1",
                BeginningCapital: 100_000m,
                Contributions: 25_000m,
                Distributions: 5_000m,
                RealizedGainLoss: 12_500m,
                Nav: 132_500m,
                RestatementReasonCode: "late-adjustment",
                PriorPackageId: "accounting-report-package-fund-alpha-2026-06",
                EvidenceLinks: ["evidence:restatement:report-package:2026-07"]),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var package = await response.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        package.Should().NotBeNull();
        package!.FinancialStatements.PackageId.Should().Be("accounting-report-package-fund-alpha-2026-07");
        package.FinancialStatements.StatementIds.Should().Contain(["balance-sheet", "income-statement", "trial-balance"]);
        package.InvestorCapitalStatements.Should().ContainSingle(statement =>
            statement.CapitalAccountId == "capital-account-lp-1" &&
            statement.InvestorId == "investor-lp-1" &&
            statement.EndingCapital == 132_500m);
        package.RealizedGainLoss.RealizedGainLoss.Should().Be(12_500m);
        package.NavPackage.Nav.Should().Be(132_500m);
        package.Certification.Actor.Should().Be("ops-user");
        package.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.NavPackage.Restatement.Should().NotBeNull();
        package.NavPackage.Restatement!.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Submitted);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseChecklistIncomplete" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "CloseSignOffMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().Contain(issue => issue.Code == "PeriodNotLocked");

        using var historyResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsAccountingPackages}?fundProfileId=fund-alpha&periodId=2026-07");

        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packages = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingReportPackageBundleDto>>(ServerJsonOptions);
        packages.Should().ContainSingle(row => row.FinancialStatements.PackageId == package.FinancialStatements.PackageId);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageEndpoint_BlocksRestatementCertificationWithoutPriorPackageAndEvidence()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-restatement",
                PeriodId: "2026-09",
                Actor: "browser-user",
                BeginningCapital: 250_000m,
                RealizedGainLoss: -7_500m,
                Nav: 242_500m,
                RestatementReasonCode: "nav-correction",
                EvidenceLinks: ["evidence:report-package:2026-09"]),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var package = await response.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        package.Should().NotBeNull();
        package!.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.NavPackage.Restatement.Should().NotBeNull();
        package.NavPackage.Restatement!.PriorPackageId.Should().Be("prior-package:unresolved");
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "RestatementPriorPackageMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "RestatementEvidenceMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageEndpoint_RequiresCertificationEvidenceFamilies()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var sparseResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-evidence",
                PeriodId: "2026-10",
                Actor: "browser-user",
                BeginningCapital: 150_000m,
                Contributions: 20_000m,
                Distributions: 5_000m,
                RealizedGainLoss: 4_000m,
                Nav: 169_000m,
                EvidenceLinks: ["evidence:report-package:2026-10"]),
            ServerJsonOptions);

        sparseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sparse = await sparseResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        sparse.Should().NotBeNull();
        sparse!.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        sparse.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportLedgerEvidenceMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        sparse.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportReconciliationEvidenceMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        sparse.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportNavEvidenceMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        using var unscopedEvidenceResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-evidence",
                PeriodId: "2026-11",
                Actor: "browser-user",
                LedgerBookId: ledgerBookId,
                BeginningCapital: 150_000m,
                Contributions: 20_000m,
                Distributions: 5_000m,
                RealizedGainLoss: 4_000m,
                Nav: 169_000m,
                EvidenceLinks:
                [
                    "evidence:ledger:trial-balance:2026-11",
                    "evidence:reconciliation:gl-tie-out:2026-11",
                    "evidence:report-render:financial-statements:2026-11",
                    "evidence:nav:support-package:2026-11"
                ]),
            ServerJsonOptions);

        unscopedEvidenceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unscopedEvidence = await unscopedEvidenceResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        unscopedEvidence.Should().NotBeNull();
        unscopedEvidence!.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        unscopedEvidence.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportLedgerEvidenceMissingLedgerBookScope" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        unscopedEvidence.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportReconciliationEvidenceMissingLedgerBookScope" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        unscopedEvidence.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportRenderEvidenceMissingLedgerBookScope" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        unscopedEvidence.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportNavEvidenceMissingLedgerBookScope" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        using var completeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-evidence",
                PeriodId: "2026-12",
                Actor: "browser-user",
                LedgerBookId: ledgerBookId,
                BeginningCapital: 150_000m,
                Contributions: 20_000m,
                Distributions: 5_000m,
                RealizedGainLoss: 4_000m,
                Nav: 169_000m,
                EvidenceLinks:
                [
                    $"evidence:ledger:trial-balance:2026-12:book:{ledgerBookId:D}",
                    $"evidence:reconciliation:gl-tie-out:2026-12:book:{ledgerBookId:D}",
                    $"evidence:report-render:financial-statements:2026-12:book:{ledgerBookId:D}",
                    $"evidence:nav:support-package:2026-12:book:{ledgerBookId:D}"
                ]),
            ServerJsonOptions);

        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var complete = await completeResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        complete.Should().NotBeNull();
        complete!.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        complete.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        complete.FinancialStatements.CertificationState.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        complete.NavPackage.CertificationState.Should().Be(AccountingCertificationStateDto.ReadyForReview);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageCertificationEndpoint_BlocksReadyPackageWithoutCloseWorkflowAndRequiresBookEvidence()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using var buildResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-certify",
                PeriodId: "2026-12",
                Actor: "browser-user",
                LedgerBookId: ledgerBookId,
                BeginningCapital: 200_000m,
                Contributions: 10_000m,
                Distributions: 1_000m,
                RealizedGainLoss: 8_000m,
                Nav: 217_000m,
                EvidenceLinks:
                [
                    $"evidence:ledger:trial-balance:2026-12:book:{ledgerBookId:D}",
                    $"evidence:reconciliation:gl-tie-out:2026-12:book:{ledgerBookId:D}",
                    $"evidence:report-render:financial-statements:2026-12:book:{ledgerBookId:D}",
                    $"evidence:nav:support-package:2026-12:book:{ledgerBookId:D}"
                ]),
            ServerJsonOptions);
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ready = await buildResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        ready.Should().NotBeNull();
        ready!.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        var genericCertificationEvidence = $"evidence:report-certification:controller-approval:{ready.FinancialStatements.PackageId}:{ready.Certification.CertificationId}:{ready.FinancialStatements.PeriodId}";
        var certificationEvidence = $"{genericCertificationEvidence}:book:{ledgerBookId:D}";

        using var assistantCertifyResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackageCertification,
            new CertifyAccountingReportPackageRequestDto(
                ready.FinancialStatements.PackageId,
                "assistant",
                "Assistant drafted certification should not certify the retained report package.",
                [certificationEvidence],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            ServerJsonOptions);

        assistantCertifyResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var genericCertifyResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackageCertification,
            new CertifyAccountingReportPackageRequestDto(
                ready.FinancialStatements.PackageId,
                "browser-user",
                "Controller tried to certify the retained report package without ledger-book provenance.",
                [genericCertificationEvidence]),
            ServerJsonOptions);

        genericCertifyResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var certifyResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackageCertification,
            new CertifyAccountingReportPackageRequestDto(
                ready.FinancialStatements.PackageId,
                "browser-user",
                "Controller certified the retained report package.",
                [certificationEvidence]),
            ServerJsonOptions);

        certifyResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var historyResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsAccountingPackages}?fundProfileId=fund-certify&periodId=2026-12&ledgerBookId={ledgerBookId:D}");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingReportPackageBundleDto>>(ServerJsonOptions);
        history.Should().ContainSingle(row =>
            row.FinancialStatements.PackageId == ready.FinancialStatements.PackageId &&
            row.Certification.State == AccountingCertificationStateDto.ReadyForReview);

        using var otherBookHistoryResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsAccountingPackages}?fundProfileId=fund-certify&periodId=2026-12&ledgerBookId={Guid.Parse("22222222-2222-2222-2222-222222222222"):D}");
        otherBookHistoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var otherBookHistory = await otherBookHistoryResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingReportPackageBundleDto>>(ServerJsonOptions);
        otherBookHistory.Should().BeEmpty();

        var exportArtifact = ready.ExportArtifacts.First(row => row.ArtifactKind == "financial-statements");
        exportArtifact.CertificationState.Should().Be(AccountingCertificationStateDto.ReadyForReview);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageHistoryEndpoint_FiltersByDimensionScope()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var alphaDimensions = new LedgerDimensionSetDto(
            FundId: "fund-dimensioned",
            EntityId: "entity-alpha",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            },
            BookId: ledgerBookId.ToString("D"));
        var betaDimensions = alphaDimensions with
        {
            EntityId = "entity-beta",
            ExternalGlDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Operations"
            }
        };

        using var alphaResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            BuildDimensionedPackageRequest("capital-account-alpha", alphaDimensions),
            ServerJsonOptions);
        using var betaResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            BuildDimensionedPackageRequest("capital-account-beta", betaDimensions),
            ServerJsonOptions);

        alphaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        betaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var alpha = await alphaResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);

        using var historyResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsAccountingPackages}?fundProfileId=fund-dimensioned&periodId=2026-12&ledgerBookId={ledgerBookId:D}&entityId=entity-alpha&externalGl.Department=Investments");

        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingReportPackageBundleDto>>(ServerJsonOptions);
        history.Should().ContainSingle(row => row.FinancialStatements.PackageId == alpha!.FinancialStatements.PackageId);
        var dimensionedHistory = history![0];
        dimensionedHistory.FinancialStatements.Dimensions.EntityId.Should().Be("entity-alpha");
        dimensionedHistory.FinancialStatements.Dimensions.ExternalGlDimensions.Should().Contain("Department", "Investments");
        dimensionedHistory.DimensionScope.Should().NotBeNull();
        var dimensionScope = dimensionedHistory.DimensionScope!;
        dimensionScope.HasExplicitScope.Should().BeTrue();
        dimensionScope.ScopedDimensionKeys.Should().Contain(["bookId", "capitalAccountId", "entityId", "externalGl.Department", "fundId"]);
        dimensionScope.CertificationEvidenceToken.Should().Be($"dimension-scope:{dimensionScope.ScopeHash}");

        AccountingReportPackageRequestDto BuildDimensionedPackageRequest(
            string capitalAccountId,
            LedgerDimensionSetDto dimensions)
            => new(
                FundProfileId: "fund-dimensioned",
                PeriodId: "2026-12",
                Actor: "browser-user",
                LedgerBookId: ledgerBookId,
                CapitalAccountId: capitalAccountId,
                BeginningCapital: 100_000m,
                Contributions: 25_000m,
                Distributions: 5_000m,
                RealizedGainLoss: 10_000m,
                Nav: 130_000m,
                EvidenceLinks:
                [
                    $"evidence:ledger:trial-balance:2026-12:book:{ledgerBookId:D}",
                    $"evidence:reconciliation:gl-tie-out:2026-12:book:{ledgerBookId:D}",
                    $"evidence:report-render:financial-statements:2026-12:book:{ledgerBookId:D}",
                    $"evidence:nav:support-package:2026-12:book:{ledgerBookId:D}"
                ],
                Dimensions: dimensions);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageEndpoint_UsesAuthenticatedTenantScope()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserCompanyId: "tenant-alpha");
        var client = app.GetTestClient();
        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-tenant",
                PeriodId: "2026-12",
                Actor: "browser-user",
                LedgerBookId: ledgerBookId,
                BeginningCapital: 100_000m,
                Contributions: 25_000m,
                Distributions: 5_000m,
                RealizedGainLoss: 10_000m,
                Nav: 130_000m,
                EvidenceLinks:
                [
                    $"evidence:ledger:trial-balance:2026-12:book:{ledgerBookId:D}",
                    $"evidence:reconciliation:gl-tie-out:2026-12:book:{ledgerBookId:D}",
                    $"evidence:report-render:financial-statements:2026-12:book:{ledgerBookId:D}",
                    $"evidence:nav:support-package:2026-12:book:{ledgerBookId:D}"
                ],
                TenantId: "spoofed-tenant",
                CompanyId: "spoofed-company"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var package = await response.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        package.Should().NotBeNull();
        package!.TenantId.Should().Be("tenant-alpha");
        package.CompanyId.Should().Be("tenant-alpha");
        package.FinancialStatements.PackageId.Should().Contain("tenant-tenant-alpha-company-tenant-alpha");

        using var historyResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerReportsAccountingPackages}?fundProfileId=fund-tenant&periodId=2026-12&ledgerBookId={ledgerBookId:D}");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingReportPackageBundleDto>>(ServerJsonOptions);
        history.Should().ContainSingle(row => row.FinancialStatements.PackageId == package.FinancialStatements.PackageId);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageCertificationEndpoint_RequiresAdminMaintenance()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();
        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using var buildResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-certify-permission",
                PeriodId: "2027-02",
                Actor: "browser-user",
                LedgerBookId: ledgerBookId,
                BeginningCapital: 200_000m,
                Contributions: 10_000m,
                Distributions: 1_000m,
                RealizedGainLoss: 8_000m,
                Nav: 217_000m,
                EvidenceLinks:
                [
                    $"evidence:ledger:trial-balance:2027-02:book:{ledgerBookId:D}",
                    $"evidence:reconciliation:gl-tie-out:2027-02:book:{ledgerBookId:D}",
                    $"evidence:report-render:financial-statements:2027-02:book:{ledgerBookId:D}",
                    $"evidence:nav:support-package:2027-02:book:{ledgerBookId:D}"
                ]),
            ServerJsonOptions);
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ready = await buildResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        ready.Should().NotBeNull();
        ready!.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        var certificationEvidence = $"evidence:report-certification:controller-approval:{ready.FinancialStatements.PackageId}:{ready.Certification.CertificationId}:{ready.FinancialStatements.PeriodId}";

        using var certifyResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackageCertification,
            new CertifyAccountingReportPackageRequestDto(
                ready.FinancialStatements.PackageId,
                "browser-user",
                "Direct-lending operator attempted to certify the retained report package.",
                [certificationEvidence]),
            ServerJsonOptions);

        certifyResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageCertificationEndpoint_BlocksDraftPackage()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using var buildResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-draft-certify",
                PeriodId: "2027-01",
                Actor: "browser-user",
                LedgerBookId: ledgerBookId,
                Nav: 10_000m,
                EvidenceLinks: ["evidence:report-package:2027-01"]),
            ServerJsonOptions);
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await buildResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        draft.Should().NotBeNull();
        draft!.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        var certificationEvidence = $"evidence:report-certification:controller-approval:{draft.FinancialStatements.PackageId}:{draft.Certification.CertificationId}:{draft.FinancialStatements.PeriodId}:book:{ledgerBookId:D}";

        using var certifyResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackageCertification,
            new CertifyAccountingReportPackageRequestDto(
                draft.FinancialStatements.PackageId,
                "browser-user",
                "Attempt to certify without complete retained evidence.",
                [certificationEvidence]),
            ServerJsonOptions);

        certifyResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LedgerAccountingReportPackageEndpoint_BlocksCertificationWhenMaterialLateAdjustmentIsUnapproved()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-08",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var adjustmentResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustments,
            new CreateLateAdjustmentRequestDto(
                WorkflowId: workflowId,
                JournalEntryId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Amount: 25_000m,
                Currency: "usd",
                Reason: "Material NAV true-up discovered after close review.",
                RequestedBy: "browser-user",
                EvidenceLinks: ["evidence:late-adjustment:2026-08:nav-true-up"]),
            ServerJsonOptions);
        adjustmentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adjustmentPlan = await adjustmentResponse.Content.ReadFromJsonAsync<ClosePeriodPlanDto>(ServerJsonOptions);
        var adjustment = adjustmentPlan!.LateAdjustments.Should().ContainSingle().Subject;

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-beta",
                PeriodId: "2026-08",
                Actor: "browser-user",
                CloseWorkflowId: workflowId,
                CapitalAccountId: "capital-account-lp-2",
                InvestorId: "investor-lp-2",
                BeginningCapital: 200_000m,
                Contributions: 10_000m,
                Distributions: 2_000m,
                RealizedGainLoss: 3_500m,
                Nav: 211_500m,
                EvidenceLinks: ["evidence:report-package:2026-08"]),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var package = await response.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        package.Should().NotBeNull();
        package!.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "LateAdjustmentApprovalPending" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        using var reviewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(
                workflowId,
                adjustment.RequestId,
                ManualJournalEntryStatusDto.Approved,
                "browser-user",
                "Controller approval retained before report certification.",
                EvidenceLinks: [$"evidence:late-adjustment:{adjustment.RequestId}:nav-true-up-approval"]),
            ServerJsonOptions);
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var reviewedPackageResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerReportsAccountingPackage,
            new AccountingReportPackageRequestDto(
                FundProfileId: "fund-beta",
                PeriodId: "2026-08",
                Actor: "browser-user",
                CloseWorkflowId: workflowId,
                CapitalAccountId: "capital-account-lp-2",
                InvestorId: "investor-lp-2",
                BeginningCapital: 200_000m,
                Contributions: 10_000m,
                Distributions: 2_000m,
                RealizedGainLoss: 3_500m,
                Nav: 211_500m,
                EvidenceLinks: ["evidence:report-package:2026-08"]),
            ServerJsonOptions);
        reviewedPackageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviewedPackage = await reviewedPackageResponse.Content.ReadFromJsonAsync<AccountingReportPackageBundleDto>(ServerJsonOptions);
        reviewedPackage.Should().NotBeNull();
        reviewedPackage!.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "LateAdjustmentApprovalPending" &&
            issue.TargetId == adjustment.RequestId);
    }

    [Fact]
    public async Task LedgerAccountingCloseAndReportPackages_RetainDurableHistoryAcrossRestart()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-tests", "accounting-productization", Guid.NewGuid().ToString("N"));
        var fundAccountId = Guid.NewGuid();
        Guid workflowId;

        await using (var app = await CreateAppAsync(
            services => RegisterDurableOperationsContinuityServices(services, dataRoot),
            currentUserPermissions: UserPermission.AdminMaintenance))
        {
            var client = app.GetTestClient();

            using var startResponse = await client.PostAsJsonAsync(
                UiApiRoutes.OperationsContinuity,
                new OperationsStartWorkflowRequestDto(
                    fundAccountId,
                    "2026-07",
                    SecurityMasterSnapshotId: null,
                    BrokerSource: "custodian",
                    Actor: "local-actor"));
            startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
            workflowId = start!.Workflow!.WorkflowId;

            using var adjustmentResponse = await client.PostAsJsonAsync(
                UiApiRoutes.LedgerCloseManagementLateAdjustments,
                new CreateLateAdjustmentRequestDto(
                    WorkflowId: workflowId,
                    JournalEntryId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Amount: 12_000m,
                    Currency: "usd",
                    Reason: "Retained material close adjustment.",
                    RequestedBy: "browser-user",
                    EvidenceLinks: ["evidence:close:durable-late-adjustment"]),
                ServerJsonOptions);
            adjustmentResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var packageResponse = await client.PostAsJsonAsync(
                UiApiRoutes.LedgerReportsAccountingPackage,
                new AccountingReportPackageRequestDto(
                    FundProfileId: "fund-alpha",
                    PeriodId: "2026-07",
                    Actor: "browser-user",
                    CloseWorkflowId: workflowId,
                    CapitalAccountId: "capital-account-lp-1",
                    InvestorId: "investor-lp-1",
                    BeginningCapital: 100_000m,
                    Contributions: 5_000m,
                    Distributions: 1_000m,
                    RealizedGainLoss: 7_500m,
                    Nav: 111_500m,
                    RestatementReasonCode: null,
                    PriorPackageId: null,
                    EvidenceLinks: ["evidence:report-package:durable"]),
                ServerJsonOptions);
            packageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        File.Exists(Path.Combine(dataRoot, "accounting", "close-management-late-adjustments.json")).Should().BeTrue();
        File.Exists(Path.Combine(dataRoot, "accounting", "accounting-report-packages.json")).Should().BeTrue();

        await using var restartedApp = await CreateAppAsync(
            services => RegisterDurableOperationsContinuityServices(services, dataRoot),
            currentUserPermissions: UserPermission.AdminMaintenance);
        var restartedClient = restartedApp.GetTestClient();

        var planRoute = UiApiRoutes.LedgerCloseManagementPeriodPlan.Replace("{workflowId:guid}", workflowId.ToString("D"));
        using var planResponse = await restartedClient.GetAsync(planRoute);
        planResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var closePlan = await planResponse.Content.ReadFromJsonAsync<ClosePeriodPlanDto>(ServerJsonOptions);
        closePlan.Should().NotBeNull();
        closePlan!.LateAdjustments.Should().ContainSingle(row =>
            row.JournalEntryId == Guid.Parse("22222222-2222-2222-2222-222222222222") &&
            row.Amount == 12_000m &&
            row.RequestedBy == "ops-user");

        using var historyResponse = await restartedClient.GetAsync(
            $"{UiApiRoutes.LedgerReportsAccountingPackages}?fundProfileId=fund-alpha&periodId=2026-07");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packages = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingReportPackageBundleDto>>(ServerJsonOptions);
        packages.Should().ContainSingle(row =>
            row.FinancialStatements.PackageId == "accounting-report-package-fund-alpha-2026-07" &&
            row.InvestorCapitalStatements.Single().EndingCapital == 111_500m);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_PrivateCapitalCloseCockpit_ReturnsSharedCloseAggregate()
    {
        var captured = new List<(string? FundProfileId, Guid? LedgerBookId, Guid? FundAccountId, string? PeriodId, string? EntityId)>();
        var ledgerBookId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var fundAccountId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IPrivateCapitalCloseCockpitService>(
                new StubPrivateCapitalCloseCockpitService(
                    BuildPrivateCapitalCloseCockpit(ledgerBookId, fundAccountId),
                    captured));
        });
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"{UiApiRoutes.OperationsPrivateCapitalCloseCockpit}?fundProfileId=fund-alpha&ledgerBookId={ledgerBookId:D}&fundAccountId={fundAccountId:D}&periodId=2026-06&entityId=entity-master");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cockpit = await response.Content.ReadFromJsonAsync<PrivateCapitalCloseCockpitDto>(ServerJsonOptions);
        cockpit.Should().NotBeNull();
        cockpit!.OverallStatus.Should().Be(EvidenceStatusDto.Ready);
        cockpit.Lanes.Should().ContainSingle(lane => lane.LaneId == "period-lock" && lane.IsReady);
        captured.Should().ContainSingle(call =>
            call.FundProfileId == "fund-alpha" &&
            call.LedgerBookId == ledgerBookId &&
            call.FundAccountId == fundAccountId &&
            call.PeriodId == "2026-06" &&
            call.EntityId == "entity-master");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_PrivateCapitalCloseCockpit_WithoutReadPermission_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IPrivateCapitalCloseCockpitService>(
                new StubPrivateCapitalCloseCockpitService(BuildPrivateCapitalCloseCockpit(Guid.NewGuid(), Guid.NewGuid())));
        }, currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.OperationsPrivateCapitalCloseCockpit);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendarItemUpsert_UpdatesDueOwnerWithAuditEvidence()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-08",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        var startBody = await startResponse.Content.ReadAsStringAsync();
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK, startBody);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityCloseCalendarItems,
            new OperationsCloseCalendarItemUpsertRequestDto(
                WorkflowId: workflowId,
                TaskId: "close-gate-brokeringest",
                DueDate: new DateOnly(2026, 8, 3),
                Owner: "Controller",
                RequestedBy: "untrusted-browser-user",
                Rationale: "Move broker intake close task to controller queue.",
                CorrelationId: "close-calendar-test"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OperationsCloseCalendarItemUpsertResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Item.WorkflowId.Should().Be(workflowId);
        result.Item.NextDueTaskId.Should().Be("close-gate-brokeringest");
        result.Item.NextDueDate.Should().Be(new DateOnly(2026, 8, 3));
        result.Item.NextDueOwner.Should().Be("Controller");
        result.AuditEvent.Actor.Should().Be("ops-user");
        result.AuditEvent.CorrelationId.Should().Be("close-calendar-test");
        result.Calendar.Items.Should().Contain(item =>
            item.WorkflowId == workflowId &&
            item.NextDueDate == new DateOnly(2026, 8, 3) &&
            item.NextDueOwner == "Controller");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendarItemUpsert_WithoutAdminMaintenance_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityCloseCalendarItems,
            new OperationsCloseCalendarItemUpsertRequestDto(
                WorkflowId: Guid.NewGuid(),
                TaskId: "close-gate-brokeringest",
                DueDate: new DateOnly(2026, 8, 3),
                Owner: "Controller",
                RequestedBy: "ops-user",
                Rationale: "Should be rejected."));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseCalendarItemUpsert_InvalidTask_ReturnsBadRequest()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                "2026-09",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuityCloseCalendarItems,
            new OperationsCloseCalendarItemUpsertRequestDto(
                WorkflowId: start!.Workflow!.WorkflowId,
                TaskId: "missing-task",
                DueDate: new DateOnly(2026, 9, 3),
                Owner: "Controller",
                RequestedBy: "ops-user",
                Rationale: "Invalid task."));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_CloseReadiness_ExposesControllerScore()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                FundAccountId: Guid.NewGuid(),
                PeriodId: "2026-10",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor"));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var readinessResponse = await client.GetAsync(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityCloseReadiness, "workflowId", workflowId.ToString("D")));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<OperationsCloseReadinessDto>(ServerJsonOptions);
        readiness.Should().NotBeNull();
        readiness!.IsReadyToClose.Should().BeFalse();
        readiness.Score.Should().BeLessThan(100);
        readiness.Components.Select(static component => component.Key)
            .Should()
            .BeEquivalentTo(
            [
                "security-master",
                "provider-freshness",
                "positions",
                "cash",
                "ledger",
                "pricing",
                "reconciliation",
                "reports",
                "approvals"
            ]);
        readiness.Blockers.Select(static blocker => blocker.Code)
            .Should()
            .Contain(
            [
                "SECURITY_MASTER_RESOLUTION_REQUIRED",
                "BROKER_SYNC_STALE",
                "LEDGER_POSTING_REQUIRED",
                "RECONCILIATION_CRITICAL_BREAKS_OPEN",
                "REPORT_PACK_REQUIRED",
                "APPROVAL_REQUIRED"
            ]);
        readiness.NextActions.Should().OnlyContain(action => !string.IsNullOrWhiteSpace(action.Route));
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ListDetailTimeline_ExposeSharedLifecycleEvidence()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterOperationsContinuityServices(services);
        });
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                FundAccountId: Guid.NewGuid(),
                PeriodId: "2026-05",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                Actor: "local-actor",
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto(
                        EvidenceId: "ops-start-evidence",
                        Label: "Start packet",
                        Route: "/workstation/evidence/subjects/reconciliation-review/fund-1",
                        Source: "integration-test",
                        CapturedAtUtc: DateTimeOffset.UtcNow)
                ]));
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        start.Should().NotBeNull();
        start!.Success.Should().BeTrue();
        var workflowId = start.Workflow!.WorkflowId;

        using var listResponse = await client.GetAsync(UiApiRoutes.OperationsContinuity);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<List<OperationsContinuityWorkflowSummaryDto>>(ServerJsonOptions);
        listed.Should().Contain(item => item.WorkflowId == workflowId);

        using var detailResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflowId.ToString()));
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<OperationsContinuityWorkflowDto>(ServerJsonOptions);
        detail.Should().NotBeNull();
        detail!.EvidenceLinks.Should().Contain(link => link.EvidenceId == "ops-start-evidence");

        using var timelineResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityTimeline, "workflowId", workflowId.ToString()));
        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<List<OperationsTimelineEntryDto>>(ServerJsonOptions);
        timeline.Should().NotBeNullOrEmpty();
        timeline!.SelectMany(static entry => entry.References).Should().Contain(reference => reference.EvidenceId == "ops-start-evidence");
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ApprovalAndClose_ExposeSharedCloseBlockers()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterOperationsContinuityServices(services);
        });
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-06",
                null,
                "custodian",
                "local-actor",
                EvidenceLinks: []));
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var submitResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityApprovalSubmit, "workflowId", workflowId.ToString()),
            new OperationsSubmitApprovalRequestDto(
                ExpectedVersion: start.Workflow.Version,
                Actor: "ops-user",
                Reviewer: "controller",
                Rationale: "Submitting from endpoint test",
                ReportPackId: "rp-test"));
        submitResponse.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        var submit = await submitResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        submit.Should().NotBeNull();
        submit!.Blockers.Should().NotBeEmpty();

        using var closeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityClose, "workflowId", workflowId.ToString()),
            new OperationsCloseWorkflowRequestDto(
                ExpectedVersion: start.Workflow.Version,
                Actor: "ops-user",
                Rationale: "Attempt close for blocker validation",
                ReportPackId: "rp-test"));
        closeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        var close = await closeResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        close.Should().NotBeNull();
        close!.Blockers.Select(static blocker => blocker.Code)
            .Should()
            .Contain(code =>
                string.Equals(code, "OPERATIONS_GATES_NOT_PASSED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "APPROVAL_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "LEDGER_POSTING_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "REPORT_PACK_REQUIRED", StringComparison.OrdinalIgnoreCase));

        using var timelineResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityTimeline, "workflowId", workflowId.ToString()));
        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var timelineJson = await timelineResponse.Content.ReadAsStringAsync();
        using var timelineDoc = JsonDocument.Parse(timelineJson);
        timelineDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task OperationsContinuityEndpoints_ChecklistAndAcknowledgement_ShouldRequireEvidenceBackedCompletion()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterOperationsContinuityServices(services);
        });
        var client = app.GetTestClient();

        using var startResponse = await client.PostAsJsonAsync(
            UiApiRoutes.OperationsContinuity,
            new OperationsStartWorkflowRequestDto(Guid.NewGuid(), "2026-07", null, "custodian", "local-actor", EvidenceLinks: []));
        var start = await startResponse.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        var workflowId = start!.Workflow!.WorkflowId;

        using var checklistResponse = await client.GetAsync(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityChecklist, "workflowId", workflowId.ToString()));
        checklistResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var checklist = await checklistResponse.Content.ReadFromJsonAsync<List<OperationsCloseChecklistTaskDto>>(ServerJsonOptions);
        checklist.Should().NotBeNullOrEmpty();

        var firstTask = checklist![0];
        using var ackResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityChecklistAcknowledge, "workflowId", workflowId.ToString()), "taskId", firstTask.TaskId),
            new OperationsChecklistAcknowledgeRequestDto(start.Workflow.Version, "ops-user", "ack"));
        ackResponse.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
    }

    private static PrivateCapitalCloseCockpitDto BuildPrivateCapitalCloseCockpit(Guid ledgerBookId, Guid fundAccountId)
        => new(
            FundProfileId: "fund-alpha",
            LedgerBookId: ledgerBookId,
            FundAccountId: fundAccountId,
            PeriodId: "2026-06",
            EntityId: "entity-master",
            ProjectedAtUtc: new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero),
            CockpitRoute: UiApiRoutes.OperationsPrivateCapitalCloseCockpit,
            OverallStatus: EvidenceStatusDto.Ready,
            IsReadyToClose: true,
            ReadinessScore: 100,
            WorkflowCount: 1,
            FundEventCount: 1,
            CapitalAccountCount: 1,
            ReportOutputCount: 1,
            DeliveredReportOutputCount: 1,
            ReadyLaneCount: 1,
            BlockedLaneCount: 0,
            Lanes:
            [
                new PrivateCapitalCloseCockpitLaneDto(
                    "period-lock",
                    "Period lock",
                    EvidenceStatusDto.Ready,
                    true,
                    "Period lock evidence retained.",
                    "/api/workstation/operations/continuity/22222222-2222-2222-2222-222222222222",
                    0,
                    [])
            ],
            Workflows:
            [
                new PrivateCapitalCloseCockpitWorkflowDto(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    fundAccountId,
                    "2026-06",
                    OperationsWorkflowStatusDto.Closed,
                    100,
                    true,
                    "/api/workstation/operations/continuity/22222222-2222-2222-2222-222222222222",
                    "close-package-fund-alpha-2026-06",
                    "/operations-continuity/close-package/manifest-fund-alpha-2026-06",
                    0,
                    0,
                    new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero))
            ],
            Blockers: [],
            NextActions: [],
            LiveCapabilities: ["Fund/book/period close lane projection"],
            PlannedCapabilities: ["Live payment release"]);

    private sealed class StubPrivateCapitalCloseCockpitService : IPrivateCapitalCloseCockpitService
    {
        private readonly PrivateCapitalCloseCockpitDto _cockpit;
        private readonly List<(string? FundProfileId, Guid? LedgerBookId, Guid? FundAccountId, string? PeriodId, string? EntityId)>? _captured;

        public StubPrivateCapitalCloseCockpitService(
            PrivateCapitalCloseCockpitDto cockpit,
            List<(string? FundProfileId, Guid? LedgerBookId, Guid? FundAccountId, string? PeriodId, string? EntityId)>? captured = null)
        {
            _cockpit = cockpit;
            _captured = captured;
        }

        public Task<PrivateCapitalCloseCockpitDto> GetCockpitAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            Guid? fundAccountId = null,
            string? periodId = null,
            string? entityId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _captured?.Add((fundProfileId, ledgerBookId, fundAccountId, periodId, entityId));
            return Task.FromResult(_cockpit);
        }
    }

    private sealed class DriftedLedgerBookReportService(
        LedgerPeriodDto period,
        LedgerPeriodSummaryDto summary) : ILedgerBookService
    {
        public Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default)
            => Task.FromResult<LedgerBookDto?>(null);

        public Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerBookDto>>([]);

        public Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(
                query.LedgerBookId is null || query.LedgerBookId == period.LedgerBookId
                    ? [period]
                    : []);

        public Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerPeriodDto>>([]);

        public Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult<LedgerPeriodSummaryDto?>(periodId == period.PeriodId ? summary : null);

        public Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(
            Guid periodId,
            CloseLedgerPeriodRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
