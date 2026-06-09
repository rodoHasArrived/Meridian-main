using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task AccountingConfigurationEndpoints_WhenServiceMissing_ReturnsNotImplemented()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.LedgerAccountingConfiguration);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task AccountingConfigurationEndpoints_WithoutLedgerPermission_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.LedgerAccountingConfiguration);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AccountingConfigurationEndpoints_GoldenPath_PreviewsActivatesAndListsAudit()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var cashResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                Actor: "browser-user",
                CorrelationId: "endpoint-chart-cash"),
            ServerJsonOptions);
        using var incomeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("interest-income", "Income:Interest", "Interest Income", "Revenue"),
                Actor: "browser-user",
                CorrelationId: "endpoint-chart-income"),
            ServerJsonOptions);
        using var templateResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationTemplates,
            new UpsertJournalEntryTemplateRequest(
                FundProfileId: "fund-alpha",
                Template: new JournalEntryTemplateDto(
                    TemplateId: "template-interest-accrual",
                    DisplayName: "Interest accrual",
                    Description: "Recognize daily accrued interest.",
                    Lines:
                    [
                        new JournalEntryTemplateLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 100m),
                        new JournalEntryTemplateLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, 100m)
                    ]),
                Actor: "browser-user",
                CorrelationId: "endpoint-template"),
            ServerJsonOptions);
        using var ruleResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRules,
            new UpsertPostingRuleRequest(
                FundProfileId: "fund-alpha",
                Rule: new PostingRuleDto("rule-interest-accrual", "Daily interest accrual", "InterestAccrual", "template-interest-accrual"),
                Actor: "browser-user",
                CorrelationId: "endpoint-rule"),
            ServerJsonOptions);
        using var previewResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPreview,
            new PreviewJournalTemplateRequest(
                FundProfileId: "fund-alpha",
                TemplateId: "template-interest-accrual",
                Actor: "browser-user",
                CorrelationId: "endpoint-preview"),
            ServerJsonOptions);
        using var activateResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationActivate,
            new ActivateAccountingConfigurationRequest(
                FundProfileId: "fund-alpha",
                Actor: "browser-user",
                CorrelationId: "endpoint-activate"),
            ServerJsonOptions);
        using var auditResponse = await client.GetAsync($"{UiApiRoutes.LedgerAccountingConfigurationAudit}?fundProfileId=fund-alpha");

        cashResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        incomeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        templateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ruleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await previewResponse.Content.ReadFromJsonAsync<AccountingJournalTemplatePreviewDto>(ServerJsonOptions);
        var activated = await activateResponse.Content.ReadFromJsonAsync<AccountingConfigurationWorkspaceDto>(ServerJsonOptions);
        var audit = await auditResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingActionAuditEventDto>>(ServerJsonOptions);
        preview.Should().NotBeNull();
        preview!.IsBalanced.Should().BeTrue();
        activated.Should().NotBeNull();
        activated!.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        audit.Should().NotBeNull();
        audit!.Select(item => item.Action).Should().Contain("configuration.activate");
        audit.Should().Contain(item => item.Actor == "ops-user" && item.CorrelationId == "endpoint-activate");
    }

    [Fact]
    public async Task ManualJournalEntryWorkbenchEndpoints_SaveValidateAndSubmitDraft()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                Actor: "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("interest-income", "Income:Interest", "Interest Income", "Revenue"),
                Actor: "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("capital-contributions", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                Actor: "browser-user"),
            ServerJsonOptions);
        var draft = ManualJournalEntryDraft();

        using var validateResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryValidate,
            new ValidateManualJournalEntryDraftRequest(draft, "browser-user"),
            ServerJsonOptions);
        using var saveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryDrafts,
            new SaveManualJournalEntryDraftRequest(draft, "browser-user", CorrelationId: "manual-je-save"),
            ServerJsonOptions);
        var saved = await saveResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        using var submitResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntrySubmitApproval,
            new SubmitManualJournalEntryApprovalRequest(
                saved!.JournalEntryId,
                saved.FundProfileId,
                "controller",
                saved.Version,
                CorrelationId: "manual-je-submit"),
            ServerJsonOptions);
        using var workbenchResponse = await client.GetAsync($"{UiApiRoutes.LedgerManualJournalEntryWorkbench}?fundProfileId=fund-alpha");
        using var privateCapitalResponse = await client.GetAsync($"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha");
        using var filteredPrivateCapitalResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:capital-call:20260630")}");
        using var fundEventRecordResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalFundEventRecord}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:capital-call:20260630")}");
        using var capitalAccountSubledgerResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:lp-1")}");
        using var missingPrivateCapitalResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:missing")}");
        using var missingFundEventRecordResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalFundEventRecord}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:missing")}");
        using var missingCapitalAccountSubledgerResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:missing")}");

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        workbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        privateCapitalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        filteredPrivateCapitalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fundEventRecordResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        capitalAccountSubledgerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        missingPrivateCapitalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        missingFundEventRecordResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingCapitalAccountSubledgerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        var workbench = await workbenchResponse.Content.ReadFromJsonAsync<ManualJournalEntryWorkbenchDto>(ServerJsonOptions);
        var privateCapitalActivity = await privateCapitalResponse.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        var filteredPrivateCapitalActivity = await filteredPrivateCapitalResponse.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        var fundEventRecord = await fundEventRecordResponse.Content.ReadFromJsonAsync<PrivateCapitalFundEventLedgerRecordDto>(ServerJsonOptions);
        var capitalAccountSubledger = await capitalAccountSubledgerResponse.Content.ReadFromJsonAsync<PrivateCapitalCapitalAccountSubledgerDto>(ServerJsonOptions);
        var missingPrivateCapitalActivity = await missingPrivateCapitalResponse.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        submitted!.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        submitted.ApprovalId.Should().NotBeNullOrWhiteSpace();
        workbench!.Drafts.Should().ContainSingle(item => item.JournalEntryId == submitted.JournalEntryId);
        workbench.AuditTrail.Select(item => item.Action).Should().Contain("manual-je.submit-approval");
        workbench.PrivateCapitalActivity.Should().NotBeNull();
        workbench.PrivateCapitalActivity!.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.NetCapitalActivity == 100m);
        workbench.PrivateCapitalActivity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.NetActivity == 100m);
        workbench.PrivateCapitalActivity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.NetCapitalActivity == 100m &&
            item.RunningNetActivity == 100m);
        workbench.PrivateCapitalActivity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.TotalDebits == 100m &&
            item.TotalCredits == 100m &&
            item.IsPostingReady);
        workbench.PrivateCapitalActivity.ReportOutputs.Should().ContainSingle(item =>
            item.ReportOutputType == "CapitalCallNotice" &&
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.IsReportReady &&
            item.EvidenceLinkCount == 1);
        privateCapitalActivity.Should().NotBeNull();
        privateCapitalActivity!.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.NetCapitalActivity == 100m);
        privateCapitalActivity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.NetActivity == 100m);
        privateCapitalActivity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.NetCapitalActivity == 100m &&
            item.RunningNetActivity == 100m);
        privateCapitalActivity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.LineCount == 2 &&
            item.EvidenceLinks.Count == 1);
        privateCapitalActivity.ReportOutputs.Should().ContainSingle(item =>
            item.ReportOutputType == "CapitalCallNotice" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.ReportWorkflowState == ManualJournalEntryStatusDto.Submitted.ToString() &&
            item.ReportRoute.Contains("fundProfileId=fund-alpha", StringComparison.OrdinalIgnoreCase) &&
            item.ReportRoute.Contains("fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase));
        privateCapitalActivity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.JournalEntryId == submitted.JournalEntryId &&
            item.GrossAmount == 100m &&
            item.CapitalAccountOpeningNetActivity == 0m &&
            item.CapitalAccountEndingNetActivity == 100m &&
            item.Memo == "Manual close adjustment" &&
            item.PaymentIntentId == "payment:fund-alpha:capital-call:20260630" &&
            item.SettlementReference == "settlement:fund-alpha:capital-call:20260630" &&
            item.ActivityRoute.Contains("fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase) &&
            item.ActivityRoute.Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.ActivityRoute.Contains("investorId=investor%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.EvidenceRoute.Contains("/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet", StringComparison.OrdinalIgnoreCase) &&
            item.ApprovalId == submitted.ApprovalId &&
            item.ApprovalRoute != null &&
            item.ApprovalRoute.Contains("approvalId=" + submitted.ApprovalId, StringComparison.OrdinalIgnoreCase) &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1 &&
            item.ReportOutputCount == 1 &&
            item.ValidationIssueCount == 0 &&
            item.PrimaryReportOutputType == "CapitalCallNotice" &&
            item.PrimaryReportRoute != null &&
            item.PrimaryReportRoute.Contains("fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase) &&
            item.ReportWorkflowState == ManualJournalEntryStatusDto.Submitted.ToString() &&
            item.ReportLineProvenanceCount == 0 &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Ready &&
            item.ReadinessLabel == "Ready" &&
            item.NextAction == "Review report output" &&
            item.NextActionRoute == item.PrimaryReportRoute &&
            item.CapitalAccountSubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1 &&
            item.EvidenceLinkCount == 1);
        filteredPrivateCapitalActivity.Should().NotBeNull();
        filteredPrivateCapitalActivity!.FundEventCount.Should().Be(1);
        filteredPrivateCapitalActivity.CapitalAccountCount.Should().Be(1);
        filteredPrivateCapitalActivity.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630");
        filteredPrivateCapitalActivity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.RunningNetActivity == 100m);
        filteredPrivateCapitalActivity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630");
        filteredPrivateCapitalActivity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630");
        filteredPrivateCapitalActivity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.JournalEntryId == submitted.JournalEntryId &&
            item.CapitalAccountOpeningNetActivity == 0m &&
            item.CapitalAccountEndingNetActivity == 100m &&
            item.ActivityRoute.Contains("fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase) &&
            item.ActivityRoute.Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.EvidenceRoute.Contains("/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet", StringComparison.OrdinalIgnoreCase) &&
            item.ApprovalId == submitted.ApprovalId &&
            item.ApprovalRoute != null &&
            item.ApprovalRoute.Contains("approvalId=" + submitted.ApprovalId, StringComparison.OrdinalIgnoreCase) &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1 &&
            item.ReportOutputCount == 1 &&
            item.PrimaryReportOutputType == "CapitalCallNotice" &&
            item.ReportWorkflowState == ManualJournalEntryStatusDto.Submitted.ToString() &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Ready &&
            item.ReadinessLabel == "Ready" &&
            item.NextAction == "Review report output" &&
            item.NextActionRoute == item.PrimaryReportRoute &&
            item.CapitalAccountSubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
        fundEventRecord.Should().NotBeNull();
        fundEventRecord!.FundEventId.Should().Be("fund-event:fund-alpha:capital-call:20260630");
        fundEventRecord.JournalEntryId.Should().Be(submitted.JournalEntryId);
        fundEventRecord.ApprovalId.Should().Be(submitted.ApprovalId);
        fundEventRecord.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Ready);
        fundEventRecord.NextAction.Should().Be("Review report output");
        fundEventRecord.NextActionRoute.Should().Be(fundEventRecord.PrimaryReportRoute);
        fundEventRecord.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.RunningNetActivity == 100m);
        fundEventRecord.LedgerImpacts.Should().ContainSingle(item =>
            item.LineCount == 2 &&
            item.IsPostingReady);
        fundEventRecord.ReportOutputs.Should().ContainSingle(item =>
            item.ReportOutputType == "CapitalCallNotice" &&
            item.IsReportReady);
        capitalAccountSubledger.Should().NotBeNull();
        capitalAccountSubledger!.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
        capitalAccountSubledger.InvestorId.Should().Be("investor:lp-1");
        capitalAccountSubledger.Contributions.Should().Be(100m);
        capitalAccountSubledger.OpeningNetActivity.Should().Be(0m);
        capitalAccountSubledger.EndingNetActivity.Should().Be(100m);
        capitalAccountSubledger.NetCapitalActivity.Should().Be(100m);
        capitalAccountSubledger.FundEventCount.Should().Be(1);
        capitalAccountSubledger.ApprovalQueueCount.Should().Be(1);
        capitalAccountSubledger.EvidenceLinkCount.Should().Be(1);
        capitalAccountSubledger.ActivityRoute
            .Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase)
            .Should()
            .BeTrue();
        capitalAccountSubledger.FundEventRecords.Should().ContainSingle(item => item.FundEventId == "fund-event:fund-alpha:capital-call:20260630");
        capitalAccountSubledger.SubledgerEntries.Should().ContainSingle(item => item.RunningNetActivity == 100m);
        capitalAccountSubledger.LedgerImpacts.Should().ContainSingle(item => item.IsPostingReady);
        capitalAccountSubledger.ReportOutputs.Should().ContainSingle(item => item.IsReportReady);
        missingPrivateCapitalActivity.Should().NotBeNull();
        missingPrivateCapitalActivity!.FundEventCount.Should().Be(0);
        missingPrivateCapitalActivity.CapitalAccountCount.Should().Be(0);
        missingPrivateCapitalActivity.FundEvents.Should().BeEmpty();
        missingPrivateCapitalActivity.CapitalAccounts.Should().BeEmpty();
        missingPrivateCapitalActivity.CapitalAccountSubledgerEntries.Should().BeEmpty();
        missingPrivateCapitalActivity.LedgerImpacts.Should().BeEmpty();
        missingPrivateCapitalActivity.ReportOutputs.Should().BeEmpty();
        missingPrivateCapitalActivity.FundEventRecords.Should().BeEmpty();
        missingPrivateCapitalActivity.CapitalAccountSubledgers.Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerPrivateCapitalCapitalAccountSubledger_WhenMultipleInvestorSubledgersMatch_ReturnsBadRequestUntilDisambiguated()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                Actor: "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("capital-contributions", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                Actor: "browser-user"),
            ServerJsonOptions);

        var lp1Draft = ManualJournalEntryDraft(
            fundEventId: "fund-event:fund-alpha:capital-call:lp-1",
            capitalAccountId: "capital-account:fund-alpha:shared",
            investorId: "investor:lp-1",
            amount: 100m);
        var lp2Draft = ManualJournalEntryDraft(
            fundEventId: "fund-event:fund-alpha:capital-call:lp-2",
            capitalAccountId: "capital-account:fund-alpha:shared",
            investorId: "investor:lp-2",
            amount: 75m);
        await SaveAndSubmitManualJournalDraftAsync(client, lp1Draft, "lp-1");
        await SaveAndSubmitManualJournalDraftAsync(client, lp2Draft, "lp-2");

        using var ambiguousResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:shared")}");
        using var lp2Response = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:shared")}&investorId={Uri.EscapeDataString("investor:lp-2")}&currency=USD");

        ambiguousResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ambiguousBody = await ambiguousResponse.Content.ReadAsStringAsync();
        ambiguousBody.Should().Contain("Provide investorId and currency");
        lp2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var lp2Subledger = await lp2Response.Content.ReadFromJsonAsync<PrivateCapitalCapitalAccountSubledgerDto>(ServerJsonOptions);
        lp2Subledger.Should().NotBeNull();
        lp2Subledger!.CapitalAccountId.Should().Be("capital-account:fund-alpha:shared");
        lp2Subledger.InvestorId.Should().Be("investor:lp-2");
        lp2Subledger.Currency.Should().Be("USD");
        lp2Subledger.EndingNetActivity.Should().Be(75m);
        lp2Subledger.FundEventRecords.Should().ContainSingle(item => item.FundEventId == "fund-event:fund-alpha:capital-call:lp-2");
        lp2Subledger.FundEventRecords.Should().NotContain(item => item.FundEventId == "fund-event:fund-alpha:capital-call:lp-1");
        lp2Subledger.ActivityRoute.Should().Contain("investorId=investor%3Alp-2");
        lp2Subledger.ActivityRoute.Should().Contain("currency=USD");
    }

    private static void RegisterAccountingConfigurationServices(IServiceCollection services)
    {
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<IManualJournalEntryDraftStore, InMemoryManualJournalEntryDraftStore>();
        services.AddSingleton<IManualJournalEntryWorkbenchService, ManualJournalEntryWorkbenchService>();
    }

    private static async Task<ManualJournalEntryDraftDto> SaveAndSubmitManualJournalDraftAsync(
        HttpClient client,
        ManualJournalEntryDraftDto draft,
        string correlationSuffix)
    {
        using var saveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryDrafts,
            new SaveManualJournalEntryDraftRequest(draft, "browser-user", CorrelationId: $"manual-je-save-{correlationSuffix}"),
            ServerJsonOptions);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        using var submitResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntrySubmitApproval,
            new SubmitManualJournalEntryApprovalRequest(
                saved!.JournalEntryId,
                saved.FundProfileId,
                "controller",
                saved.Version,
                CorrelationId: $"manual-je-submit-{correlationSuffix}"),
            ServerJsonOptions);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        submitted.Should().NotBeNull();
        return submitted!;
    }

    private static ManualJournalEntryDraftDto ManualJournalEntryDraft(
        string fundEventId = "fund-event:fund-alpha:capital-call:20260630",
        string capitalAccountId = "capital-account:fund-alpha:lp-1",
        string? investorId = "investor:lp-1",
        decimal amount = 100m)
    {
        var now = DateTimeOffset.UtcNow;
        var fundEventSuffix = fundEventId.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "event";
        return new ManualJournalEntryDraftDto(
            Guid.NewGuid(),
            ManualJournalEntryStatusDto.Draft,
            "fund-alpha",
            Guid.NewGuid(),
            AccountingBasisKindDto.Primary,
            new DateOnly(2026, 6, 30),
            "2026-06",
            "entity-master",
            "fund-alpha",
            "USD",
            "Manual close adjustment",
            "browser-user",
            now,
            now,
            0,
            Lines:
            [
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, amount, "USD", "Assets:Cash", SecurityId: Guid.NewGuid()),
                new ManualJournalEntryLineDto("credit-capital", AccountingTemplateLineSideDto.Credit, amount, "USD", "Equity:Capital Contributions")
            ],
            EvidenceLinks: [],
            ValidationIssues: [],
            EvidenceAttachments:
            [
                new ManualJournalEntryEvidenceAttachmentDto(
                    "endpoint-source-doc",
                    "Endpoint JE support",
                    "SourceDocument",
                    "/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint",
                    "EvidenceVault",
                    now,
                    "browser-user")
            ],
            EntryType: ManualJournalEntryTypeDto.CapitalCall,
            TreasuryContext: new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: $"manual-je:fund-alpha:capital-call:{fundEventSuffix}",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: capitalAccountId,
                InvestorId: investorId,
                PaymentIntentId: $"payment:fund-alpha:capital-call:{fundEventSuffix}",
                SettlementReference: $"settlement:fund-alpha:capital-call:{fundEventSuffix}"));
    }
}
