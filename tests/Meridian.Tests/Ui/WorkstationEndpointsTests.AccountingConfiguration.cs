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

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        workbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        var workbench = await workbenchResponse.Content.ReadFromJsonAsync<ManualJournalEntryWorkbenchDto>(ServerJsonOptions);
        submitted!.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        submitted.ApprovalId.Should().NotBeNullOrWhiteSpace();
        workbench!.Drafts.Should().ContainSingle(item => item.JournalEntryId == submitted.JournalEntryId);
        workbench.AuditTrail.Select(item => item.Action).Should().Contain("manual-je.submit-approval");
    }

    private static void RegisterAccountingConfigurationServices(IServiceCollection services)
    {
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<IManualJournalEntryDraftStore, InMemoryManualJournalEntryDraftStore>();
        services.AddSingleton<IManualJournalEntryWorkbenchService, ManualJournalEntryWorkbenchService>();
    }

    private static ManualJournalEntryDraftDto ManualJournalEntryDraft()
    {
        var now = DateTimeOffset.UtcNow;
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
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash", SecurityId: Guid.NewGuid()),
                new ManualJournalEntryLineDto("credit-income", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Income:Interest")
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
            ]);
    }
}
