using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
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
    public async Task AccountingConfigurationProjectionSetEndpoint_ForwardsTrustedActorAndTenantScope()
    {
        var projectionService = new CapturingAccountingBasisProjectionSetService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IAccountingBasisProjectionSetService>(projectionService),
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserCompanyId: "company-alpha");
        var client = app.GetTestClient();
        var sourceEventId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var periodId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleProjectionSets,
            new AccountingBasisProjectionSetRequestDto(
                FundProfileId: "fund-alpha",
                SourceEventType: "CustodianInterestAccrual",
                EventAmount: 125.44m,
                Currency: "usd",
                EffectiveDate: new DateOnly(2026, 5, 31),
                Actor: "browser-user",
                SourceEventId: sourceEventId,
                AccountingTimestamp: DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
                Description: "Accrue custodian interest from retained source event",
                Targets:
                [
                    new AccountingBasisProjectionTargetDto(
                        AccountingBasisKindDto.Gaap,
                        ledgerBookId,
                        periodId,
                        PolicyId: "gaap-accrual-v1",
                        TreatmentKind: AccountingTreatmentKindDto.Accrual)
                ],
                TenantId: "client-tenant",
                CompanyId: "client-company",
                EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var projectionSet = await response.Content.ReadFromJsonAsync<AccountingBasisProjectionSetDto>(ServerJsonOptions);
        projectionSet.Should().NotBeNull();
        projectionSet!.SourceEventId.Should().Be(sourceEventId);
        projectionSet.Items.Should().ContainSingle(item =>
            item.AccountingBasis == AccountingBasisKindDto.Gaap &&
            item.LedgerBookId == ledgerBookId &&
            item.PeriodId == periodId);
        projectionService.Requests.Should().ContainSingle();
        var forwarded = projectionService.Requests.Single();
        forwarded.Actor.Should().Be("ops-user");
        forwarded.TenantId.Should().Be("company-alpha");
        forwarded.CompanyId.Should().Be("company-alpha");
    }

    [Fact]
    public async Task AccountingConfigurationPostingRuleCandidatePostEndpoint_ForwardsTrustedActorAndTenantScope()
    {
        var postService = new CapturingAccountingPostingCandidatePostService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IAccountingPostingCandidatePostService>(postService),
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserCompanyId: "company-alpha");
        var client = app.GetTestClient();
        var sourceEventId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var periodId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidatePosts,
            new PostPostingRuleJournalCandidateRequestDto(
                new PostingRuleJournalCandidateRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "CustodianInterestAccrual",
                    EventAmount: 125.44m,
                    Currency: "usd",
                    EffectiveDate: new DateOnly(2026, 5, 31),
                    Actor: "browser-user",
                    AggregateId: ledgerBookId,
                    PeriodId: periodId,
                    AccountingTimestamp: DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
                    Description: "Accrue custodian interest from retained source event",
                    AccountingBasis: AccountingBasisKindDto.Gaap,
                    LedgerBookId: ledgerBookId,
                    CorrelationId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    SourceEventId: sourceEventId,
                    PolicyId: "gaap-accrual-v1",
                    TreatmentKind: AccountingTreatmentKindDto.Accrual,
                    EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"],
                    TenantId: "client-tenant",
                    CompanyId: "client-company"),
                Actor: "browser-user",
                ApprovalId: "approval-generated-interest-202605",
                EvidenceLinks: ["approval://workpaper/generated-interest-202605"],
                TenantId: "client-tenant",
                CompanyId: "client-company"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var posted = await response.Content.ReadFromJsonAsync<PostedPostingRuleJournalCandidateResultDto>(ServerJsonOptions);
        posted.Should().NotBeNull();
        posted!.PostedJournal.LedgerBookId.Should().Be(ledgerBookId);
        postService.Requests.Should().ContainSingle();
        var forwarded = postService.Requests.Single();
        forwarded.Actor.Should().Be("ops-user");
        forwarded.TenantId.Should().Be("company-alpha");
        forwarded.CompanyId.Should().Be("company-alpha");
        forwarded.Candidate.Actor.Should().Be("browser-user");
        forwarded.Candidate.TenantId.Should().Be("client-tenant");
        forwarded.Candidate.CompanyId.Should().Be("client-company");
    }

    [Fact]
    public async Task AccountingConfigurationPostingRulePromotionApproval_RequiresAdminMaintenance()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();

        using var cashResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                "browser-user"),
            ServerJsonOptions);
        using var incomeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("income", "Income:Interest", "Interest Income", "Revenue"),
                "browser-user"),
            ServerJsonOptions);
        using var ruleResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRules,
            new UpsertPostingRuleRequest(
                "fund-alpha",
                new PostingRuleDto(
                    "rule-direct-lending-staged",
                    "Direct-lending staged interest rule",
                    "InterestAccrual",
                    "",
                    RuleVersion: "v3",
                    EffectiveFrom: new DateOnly(2027, 1, 1),
                    Priority: 10,
                    Scope: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                    Formulas:
                    [
                        new AccountingRuleFormulaDto("source", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                    ],
                    GeneratedPostings:
                    [
                        new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source", 0m),
                        new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source", 0m)
                    ]),
                "browser-user"),
            ServerJsonOptions);
        using var saveTestResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleTestCases,
            new UpsertAccountingRuleTestCaseRequest(
                FundProfileId: "fund-alpha",
                TestCase: new AccountingRuleTestCaseDto(
                    "direct-lending-staged-regression",
                    "Direct-lending staged regression",
                    new RuleDryRunRequestDto(
                        "fund-alpha",
                        "InterestAccrual",
                        175m,
                        "USD",
                        new DateOnly(2027, 2, 28),
                        "browser-user",
                        Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")),
                    ExpectedRuleId: "rule-direct-lending-staged",
                    ExpectedRuleVersion: "v3"),
                Actor: "browser-user",
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/direct-lending-staged-regression-rule-direct-lending-staged-v3"]),
            ServerJsonOptions);
        using var runTestsResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleTests,
            new ExecuteAccountingRuleTestCasesRequestDto(
                FundProfileId: "fund-alpha",
                Actor: "browser-user"),
            ServerJsonOptions);
        using var approvalResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRulePromotionApprovals,
            new ApprovePostingRulePromotionRequest(
                FundProfileId: "fund-alpha",
                RuleId: "rule-direct-lending-staged",
                RuleVersion: "v3",
                Actor: "browser-user",
                ApprovalId: "approval-rule-direct-lending-staged-v3",
                Notes: "Direct-lending operator attempted to approve a staged posting rule.",
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/rule-direct-lending-staged-review-v3"]),
            ServerJsonOptions);

        cashResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        incomeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ruleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        saveTestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        runTestsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var testSuite = await runTestsResponse.Content.ReadFromJsonAsync<AccountingRuleTestSuiteResultDto>(ServerJsonOptions);
        testSuite!.PassedCount.Should().Be(1);
        approvalResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AccountingConfigurationActivation_RequiresAdminMaintenance()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();

        using var cashResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                "browser-user"),
            ServerJsonOptions);
        using var incomeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("income", "Income:Interest", "Interest Income", "Revenue"),
                "browser-user"),
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
                Actor: "browser-user"),
            ServerJsonOptions);
        using var ruleResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRules,
            new UpsertPostingRuleRequest(
                "fund-alpha",
                new PostingRuleDto(
                    "rule-interest-accrual",
                    "Daily interest accrual",
                    "InterestAccrual",
                    "template-interest-accrual"),
                "browser-user"),
            ServerJsonOptions);
        using var activationResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationActivate,
            new ActivateAccountingConfigurationRequest(
                FundProfileId: "fund-alpha",
                Actor: "browser-user",
                EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]),
            ServerJsonOptions);

        cashResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        incomeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        templateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ruleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        activationResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AccountingConfigurationEndpoints_GoldenPath_PreviewsActivatesAndListsAudit()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserRole: UserRole.Accounting,
            currentUserRoleProfileName: "reporting-ops",
            currentUserCompanyId: "company-alpha");
        var client = app.GetTestClient();

        using var cashResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId: "fund-alpha",
                Node: new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                Actor: "browser-user",
                CorrelationId: "endpoint-chart-cash",
                CompanyId: "spoofed-company",
                TenantId: "spoofed-tenant"),
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
                CorrelationId: "endpoint-activate",
                EvidenceLinks: ["evidence://accounting/configuration/endpoint-activation-approval"]),
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
        var chartWorkspace = await cashResponse.Content.ReadFromJsonAsync<AccountingConfigurationWorkspaceDto>(ServerJsonOptions);
        var activated = await activateResponse.Content.ReadFromJsonAsync<AccountingConfigurationWorkspaceDto>(ServerJsonOptions);
        var audit = await auditResponse.Content.ReadFromJsonAsync<IReadOnlyList<AccountingActionAuditEventDto>>(ServerJsonOptions);
        preview.Should().NotBeNull();
        preview!.IsBalanced.Should().BeTrue();
        chartWorkspace.Should().NotBeNull();
        chartWorkspace!.TenantId.Should().Be("company-alpha");
        chartWorkspace.CompanyId.Should().Be("company-alpha");
        activated.Should().NotBeNull();
        activated!.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activated.TenantId.Should().Be("company-alpha");
        activated.CompanyId.Should().Be("company-alpha");
        audit.Should().NotBeNull();
        audit!.Select(item => item.Action).Should().Contain("configuration.activate");
        audit.Should().Contain(item =>
            item.Actor == "ops-user" &&
            item.CorrelationId == "endpoint-activate" &&
            item.CompanyId == "company-alpha" &&
            item.ReportGroupPrincipalIds != null &&
            item.ReportGroupPrincipalIds.Contains(UserRole.Accounting.ToString()) &&
            item.ReportGroupPrincipalIds.Contains("reporting-ops"));
    }

    [Fact]
    public async Task ManualJournalEntryWorkbenchEndpoints_SaveValidateAndSubmitDraft()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserCompanyId: "company-alpha");
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
                CorrelationId: "manual-je-submit",
                LedgerBookId: saved.LedgerBookId),
            ServerJsonOptions);
        var submitResponseBody = await submitResponse.Content.ReadAsStringAsync();
        using var workbenchResponse = await client.GetAsync($"{UiApiRoutes.LedgerManualJournalEntryWorkbench}?fundProfileId=fund-alpha");
        using var privateCapitalResponse = await client.GetAsync($"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha");
        using var filteredPrivateCapitalResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:capital-call:20260630")}");
        using var paymentIntentPrivateCapitalResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha&paymentIntentId={Uri.EscapeDataString("payment:fund-alpha:capital-call:20260630")}");
        using var fundEventRecordResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalFundEventRecord}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:capital-call:20260630")}");
        using var fundEventCommandCenterResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalFundEventCommandCenter}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:capital-call:20260630")}");
        using var capitalAccountSubledgerResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:lp-1")}");
        using var capitalAccountWorkbenchResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:lp-1")}");
        using var missingPrivateCapitalResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:missing")}");
        using var missingFundEventRecordResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalFundEventRecord}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:missing")}");
        using var missingFundEventCommandCenterResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalFundEventCommandCenter}?fundProfileId=fund-alpha&fundEventId={Uri.EscapeDataString("fund-event:fund-alpha:missing")}");
        using var missingFundEventCommandCenterSelectorResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalFundEventCommandCenter}?fundProfileId=fund-alpha");
        using var missingCapitalAccountSubledgerResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:missing")}");
        using var missingCapitalAccountWorkbenchResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench}?fundProfileId=fund-alpha&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:missing")}");
        using var accountingWorkspaceResponse = await client.GetAsync($"{UiApiRoutes.WorkstationAccounting}?fundProfileId=fund-alpha");
        using var reportingWorkspaceResponse = await client.GetAsync($"{UiApiRoutes.WorkstationReporting}?fundProfileId=fund-alpha");

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK, submitResponseBody);
        workbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        privateCapitalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        filteredPrivateCapitalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        paymentIntentPrivateCapitalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fundEventRecordResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fundEventCommandCenterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        capitalAccountSubledgerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        capitalAccountWorkbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        missingPrivateCapitalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        missingFundEventRecordResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingFundEventCommandCenterResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingFundEventCommandCenterSelectorResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingCapitalAccountSubledgerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingCapitalAccountWorkbenchResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        accountingWorkspaceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        reportingWorkspaceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        var workbench = await workbenchResponse.Content.ReadFromJsonAsync<ManualJournalEntryWorkbenchDto>(ServerJsonOptions);
        var privateCapitalActivity = await privateCapitalResponse.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        var filteredPrivateCapitalActivity = await filteredPrivateCapitalResponse.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        var paymentIntentPrivateCapitalActivity = await paymentIntentPrivateCapitalResponse.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        var fundEventRecord = await fundEventRecordResponse.Content.ReadFromJsonAsync<PrivateCapitalFundEventLedgerRecordDto>(ServerJsonOptions);
        var fundEventCommandCenter = await fundEventCommandCenterResponse.Content.ReadFromJsonAsync<PrivateCapitalFundEventCommandCenterDto>(ServerJsonOptions);
        var capitalAccountSubledger = await capitalAccountSubledgerResponse.Content.ReadFromJsonAsync<PrivateCapitalCapitalAccountSubledgerDto>(ServerJsonOptions);
        var capitalAccountWorkbench = await capitalAccountWorkbenchResponse.Content.ReadFromJsonAsync<CapitalAccountWorkbenchDto>(ServerJsonOptions);
        var missingPrivateCapitalActivity = await missingPrivateCapitalResponse.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        var accountingWorkspace = await accountingWorkspaceResponse.Content.ReadFromJsonAsync<WorkstationAccountingPayload>(ServerJsonOptions);
        var reportingWorkspace = await reportingWorkspaceResponse.Content.ReadFromJsonAsync<WorkstationAccountingPayload>(ServerJsonOptions);
        privateCapitalActivity.Should().NotBeNull();
        var reportOutputId = privateCapitalActivity!.ReportOutputs.Single().ReportOutputId;
        using var reportOutputResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalReportOutput}?fundProfileId=fund-alpha&reportOutputId={Uri.EscapeDataString(reportOutputId)}");
        using var missingReportOutputResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalReportOutput}?fundProfileId=fund-alpha&reportOutputId={Uri.EscapeDataString("report-output:fund-alpha:missing")}");
        using var missingReportOutputSelectorResponse = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalReportOutput}?fundProfileId=fund-alpha");
        reportOutputResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        missingReportOutputResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingReportOutputSelectorResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var reportOutput = await reportOutputResponse.Content.ReadFromJsonAsync<PrivateCapitalReportOutputDto>(ServerJsonOptions);
        submitted!.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        submitted.ApprovalId.Should().NotBeNullOrWhiteSpace();
        submitted.LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Submit &&
            transition.ToStatus == ManualJournalEntryStatusDto.Submitted &&
            transition.CorrelationId == "manual-je-submit");
        workbench!.Drafts.Should().ContainSingle(item => item.JournalEntryId == submitted.JournalEntryId);
        workbench.Drafts.Single(item => item.JournalEntryId == submitted.JournalEntryId).LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Submit &&
            transition.ToStatus == ManualJournalEntryStatusDto.Submitted &&
            transition.CorrelationId == "manual-je-submit");
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
        accountingWorkspace.Should().NotBeNull();
        accountingWorkspace!.ManualJournalWorkbench.Should().NotBeNull();
        accountingWorkspace.ManualJournalWorkbench!.PrivateCapitalActivity.Should().NotBeNull();
        accountingWorkspace.ManualJournalWorkbench.PrivateCapitalActivity!.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.ReportOutputCount == 1 &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1);
        reportingWorkspace.Should().NotBeNull();
        reportingWorkspace!.ManualJournalWorkbench.Should().NotBeNull();
        reportingWorkspace.ManualJournalWorkbench!.PrivateCapitalActivity.Should().NotBeNull();
        reportingWorkspace.ManualJournalWorkbench.PrivateCapitalActivity!.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.ReportOutputCount == 1 &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1);
        privateCapitalActivity.FundEvents.Should().ContainSingle(item =>
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
        reportOutput.Should().NotBeNull();
        reportOutput!.ReportOutputId.Should().Be(reportOutputId);
        reportOutput.ReportOutputType.Should().Be("CapitalCallNotice");
        reportOutput.FundEventId.Should().Be("fund-event:fund-alpha:capital-call:20260630");
        reportOutput.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
        reportOutput.ReportOutputRoute.Should().NotBeNullOrWhiteSpace();
        reportOutput.ReportOutputRoute!.Should().Contain(UiApiRoutes.LedgerPrivateCapitalReportOutput);
        reportOutput.ReportOutputRoute.Should().Contain("reportOutputId=" + Uri.EscapeDataString(reportOutputId));
        reportOutput.FundEventRecordRoute.Should().NotBeNullOrWhiteSpace();
        reportOutput.FundEventRecordRoute!.Should().Contain(UiApiRoutes.LedgerPrivateCapitalFundEventRecord);
        reportOutput.CapitalAccountSubledgerRoute.Should().NotBeNullOrWhiteSpace();
        reportOutput.CapitalAccountSubledgerRoute!.Should().Contain(UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger);
        reportOutput.EvidenceRoute.Should().NotBeNullOrWhiteSpace();
        reportOutput.EvidenceRoute!.Should().Contain("/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet");
        reportOutput.ApprovalRoute.Should().NotBeNullOrWhiteSpace();
        reportOutput.ApprovalRoute!.Should().Contain("approvalId=" + submitted.ApprovalId);
        reportOutput.ReadinessLabel.Should().Be("Ready");
        reportOutput.ReadinessReason.Should().Be("The report output has retained evidence and linked posting-ready fund-event impact.");
        reportOutput.NextAction.Should().Be("Review report output");
        reportOutput.NextActionRoute.Should().Be(reportOutput.ReportOutputRoute);
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
            item.PrimaryReportRoute.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase) &&
            item.PrimaryReportRoute.Contains("fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase) &&
            item.ReportWorkflowState == ManualJournalEntryStatusDto.Submitted.ToString() &&
            item.ReportLineProvenanceCount == 0 &&
            item.CapitalAccountSubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1 &&
            item.EvidenceLinkCount >= 1);
        filteredPrivateCapitalActivity.Should().NotBeNull();
        filteredPrivateCapitalActivity!.FundEventCount.Should().Be(1);
        filteredPrivateCapitalActivity.CapitalAccountCount.Should().Be(1);
        filteredPrivateCapitalActivity.PaymentIntents.Should().ContainSingle(item =>
            item.PaymentIntentId == "payment:fund-alpha:capital-call:20260630" &&
            item.WorkbenchRoute.Contains(UiApiRoutes.LedgerPrivateCapitalActivity, StringComparison.OrdinalIgnoreCase) &&
            item.WorkbenchRoute.Contains("paymentIntentId=payment%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase));
        paymentIntentPrivateCapitalActivity.Should().NotBeNull();
        paymentIntentPrivateCapitalActivity!.FundEventCount.Should().Be(1);
        paymentIntentPrivateCapitalActivity.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.PaymentIntentId == "payment:fund-alpha:capital-call:20260630");
        paymentIntentPrivateCapitalActivity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.PaymentIntentId == "payment:fund-alpha:capital-call:20260630");
        paymentIntentPrivateCapitalActivity.PaymentIntents.Should().ContainSingle(item =>
            item.PaymentIntentId == "payment:fund-alpha:capital-call:20260630" &&
            item.WorkbenchRoute.Contains(UiApiRoutes.LedgerPrivateCapitalActivity, StringComparison.OrdinalIgnoreCase) &&
            item.WorkbenchRoute.Contains("paymentIntentId=payment%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase));
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
            item.CapitalAccountSubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
        fundEventRecord.Should().NotBeNull();
        fundEventRecord!.FundEventId.Should().Be("fund-event:fund-alpha:capital-call:20260630");
        fundEventRecord.JournalEntryId.Should().Be(submitted.JournalEntryId);
        fundEventRecord.ApprovalId.Should().Be(submitted.ApprovalId);
        fundEventRecord.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Ready);
        fundEventRecord.ReadinessLabel.Should().Be("Ready");
        fundEventRecord.ReadinessReason.Should().Be("The event has retained evidence, posting-ready ledger impact, capital-account movement, and report output ready for publication.");
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
        fundEventRecord.EvidenceCategories.Should().HaveCount(7);
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "source-support" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "capital-account-subledger" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "ledger-impact" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "approval-state" &&
            item.IsReady &&
            item.EvidenceLinks.Contains(submitted.ApprovalId!) &&
            item.EvidenceLinks.Any(link => link.Contains("approvalId=" + submitted.ApprovalId, StringComparison.OrdinalIgnoreCase)));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        fundEventCommandCenter.Should().NotBeNull();
        fundEventCommandCenter!.FundEventId.Should().Be("fund-event:fund-alpha:capital-call:20260630");
        fundEventCommandCenter.CommandCenterRoute.Should().Contain(UiApiRoutes.LedgerPrivateCapitalFundEventCommandCenter);
        fundEventCommandCenter.Lanes.Should().HaveCount(10);
        fundEventCommandCenter.Lanes.Should().ContainSingle(item =>
            item.LaneId == "evidence" &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.WorkstationEvidenceSubjects, StringComparison.OrdinalIgnoreCase));
        fundEventCommandCenter.Lanes.Should().ContainSingle(item =>
            item.LaneId == "capital-account-impact" &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger, StringComparison.OrdinalIgnoreCase));
        fundEventCommandCenter.Lanes.Should().ContainSingle(item =>
            item.LaneId == "report-usage" &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.LedgerPrivateCapitalReportOutput, StringComparison.OrdinalIgnoreCase));
        fundEventCommandCenter.SupportPackages.Should().Contain(item =>
            item.PackageId == "evidence:fund-event:fund-alpha:capital-call:20260630" &&
            item.EvidenceLinkCount >= 1);
        fundEventCommandCenter.SupportPackages.Should().Contain(item =>
            item.PackageId.StartsWith("payment-intent:", StringComparison.OrdinalIgnoreCase));
        fundEventCommandCenter.SupportPackages.Should().Contain(item =>
            item.PackageId.StartsWith("report-output:", StringComparison.OrdinalIgnoreCase));
        fundEventCommandCenter.LiveCapabilities.Should().Contain(item =>
            item.Contains("Event-level evidence", StringComparison.OrdinalIgnoreCase));
        fundEventCommandCenter.PlannedCapabilities.Should().Contain("Native live treasury payment execution");
        capitalAccountSubledger.Should().NotBeNull();
        capitalAccountSubledger!.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
        capitalAccountSubledger.InvestorId.Should().Be("investor:lp-1");
        capitalAccountSubledger.Currency.Should().Be("USD");
        capitalAccountSubledger.ActivityRoute.Should().Contain(UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger);
        capitalAccountSubledger.ActivityRoute.Should().Contain("investorId=investor%3Alp-1");
        capitalAccountSubledger.ActivityRoute.Should().Contain("currency=USD");
        capitalAccountSubledger.Contributions.Should().Be(100m);
        capitalAccountSubledger.Distributions.Should().Be(0m);
        capitalAccountSubledger.Subscriptions.Should().Be(0m);
        capitalAccountSubledger.Redemptions.Should().Be(0m);
        capitalAccountSubledger.ManagementFees.Should().Be(0m);
        capitalAccountSubledger.OpeningNetActivity.Should().Be(0m);
        capitalAccountSubledger.EndingNetActivity.Should().Be(100m);
        capitalAccountSubledger.NetCapitalActivity.Should().Be(100m);
        capitalAccountSubledger.FundEventCount.Should().Be(1);
        capitalAccountSubledger.ApprovalQueueCount.Should().Be(1);
        capitalAccountSubledger.PostedFundEventCount.Should().Be(0);
        capitalAccountSubledger.PublishedReportOutputCount.Should().Be(0);
        capitalAccountSubledger.EvidenceLinkCount.Should().Be(1);
        capitalAccountSubledger.ValidationIssueCount.Should().Be(0);
        capitalAccountSubledger.FirstEffectiveDate.Should().Be(new DateOnly(2026, 6, 30));
        capitalAccountSubledger.LastEffectiveDate.Should().Be(new DateOnly(2026, 6, 30));
        capitalAccountSubledger.LastFundEventType.Should().Be("CapitalCall");
        capitalAccountSubledger.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Ready);
        capitalAccountSubledger.ReadinessLabel.Should().Be("Ready");
        capitalAccountSubledger.ReadinessReason.Should().Be("The capital-account subledger has retained evidence, posting-ready ledger impact, capital-account movement, and report output ready for publication.");
        capitalAccountSubledger.NextAction.Should().Be("Review report output");
        capitalAccountSubledger.NextActionRoute.Should().Be(reportOutput.ReportOutputRoute);
        capitalAccountSubledger.EvidenceLinks.Should().Contain("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint");
        capitalAccountSubledger.ActivityRoute
            .Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase)
            .Should()
            .BeTrue();
        capitalAccountSubledger.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.ApprovalId == submitted.ApprovalId &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Ready &&
            item.ReadinessLabel == "Ready" &&
            item.ReadinessReason == "The event has retained evidence, posting-ready ledger impact, capital-account movement, and report output ready for publication." &&
            item.NextAction == "Review report output" &&
            item.NextActionRoute == reportOutput.ReportOutputRoute);
        capitalAccountSubledger.SubledgerEntries.Should().ContainSingle(item =>
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.RunningNetActivity == 100m);
        capitalAccountSubledger.LedgerImpacts.Should().ContainSingle(item =>
            item.IsPostingReady &&
            item.LineCount == 2);
        capitalAccountSubledger.ReportOutputs.Should().ContainSingle(item =>
            item.IsReportReady &&
            item.ReportWorkflowState == ManualJournalEntryStatusDto.Submitted.ToString() &&
            item.ReadinessLabel == "Ready" &&
            item.ReadinessReason == "The report output has retained evidence and linked posting-ready fund-event impact." &&
            item.NextAction == "Review report output" &&
            item.NextActionRoute == reportOutput.ReportOutputRoute);
        capitalAccountSubledger.ValidationIssues.Should().BeEmpty();
        capitalAccountSubledger.EvidenceCategories.Should().HaveCount(7);
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "source-support" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "capital-account-subledger" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "ledger-impact" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "approval-state" &&
            item.IsReady &&
            item.EvidenceLinks.Contains(submitted.ApprovalId!) &&
            item.EvidenceLinks.Any(link => link.Contains("approvalId=" + submitted.ApprovalId, StringComparison.OrdinalIgnoreCase)));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/manual-je-endpoint"));
        capitalAccountWorkbench.Should().NotBeNull();
        capitalAccountWorkbench!.WorkbenchRoute.Should().Contain(UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench);
        capitalAccountWorkbench.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
        capitalAccountWorkbench.InvestorAccountCount.Should().Be(1);
        capitalAccountWorkbench.FundEventCount.Should().Be(1);
        capitalAccountWorkbench.StatementCount.Should().Be(1);
        capitalAccountWorkbench.AuditDrillThroughCount.Should().BeGreaterThanOrEqualTo(4);
        capitalAccountWorkbench.LiveCapabilities.Should().Contain("Investor-level capital account evidence");
        capitalAccountWorkbench.PlannedCapabilities.Should().Contain("Full cap-table administration");
        capitalAccountWorkbench.InvestorAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.NetCapitalActivity == 100m &&
            item.EvidenceCategories.Count == 7 &&
            item.FundEventRecords.Count == 1 &&
            item.ReportOutputs.Count == 1);
        capitalAccountWorkbench.AllocationRules.Should().Contain(item =>
            item.CategoryId == "source-support" &&
            item.IsSatisfied &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.WorkstationEvidenceSubjects, StringComparison.OrdinalIgnoreCase));
        capitalAccountWorkbench.AllocationRules.Should().Contain(item =>
            item.CategoryId == "capital-account-subledger" &&
            item.IsSatisfied &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger, StringComparison.OrdinalIgnoreCase));
        capitalAccountWorkbench.AllocationRules.Should().Contain(item =>
            item.CategoryId == "report-output" &&
            item.IsSatisfied &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.LedgerPrivateCapitalReportOutput, StringComparison.OrdinalIgnoreCase));
        capitalAccountWorkbench.StatementLineage.Should().ContainSingle(item =>
            item.ReportOutputId == reportOutputId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            !item.HasRestatementLineage &&
            item.RestatementStatus == "No restatement lineage retained." &&
            item.ReportOutputRoute != null &&
            item.ReportOutputRoute.Contains(UiApiRoutes.LedgerPrivateCapitalReportOutput, StringComparison.OrdinalIgnoreCase));
        capitalAccountWorkbench.AuditDrillThroughs.Should().Contain(item =>
            item.Kind == "subledger" &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger, StringComparison.OrdinalIgnoreCase));
        capitalAccountWorkbench.AuditDrillThroughs.Should().Contain(item =>
            item.Kind == "fund-event" &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.WorkstationEvidenceSubjects, StringComparison.OrdinalIgnoreCase));
        capitalAccountWorkbench.AuditDrillThroughs.Should().Contain(item =>
            item.Kind == "statement" &&
            item.Route != null &&
            item.Route.Contains(UiApiRoutes.LedgerPrivateCapitalReportOutput, StringComparison.OrdinalIgnoreCase));
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
    public async Task ManualJournalEntryWorkbenchEndpoints_StampsTrustedRoleProfileScopeOnAudit()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserRole: UserRole.Admin,
            currentUserRoleProfileName: "journal-controller",
            currentUserCompanyId: null);
        var client = app.GetTestClient();

        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("capital", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                "browser-user"),
            ServerJsonOptions);

        using var saveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryDrafts,
            new SaveManualJournalEntryDraftRequest(
                ManualJournalEntryDraft(),
                "spoofed-preparer",
                CorrelationId: "manual-je-audit-save"),
            ServerJsonOptions);
        var saveResponseBody = await saveResponse.Content.ReadAsStringAsync();
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK, saveResponseBody);
        var saved = await saveResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);

        using var submitResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntrySubmitApproval,
            new SubmitManualJournalEntryApprovalRequest(
                saved!.JournalEntryId,
                saved.FundProfileId,
                "spoofed-controller",
                saved.Version,
                CorrelationId: "manual-je-audit-submit",
                LedgerBookId: saved.LedgerBookId),
            ServerJsonOptions);
        var submitResponseBody = await submitResponse.Content.ReadAsStringAsync();
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK, submitResponseBody);

        using var workbenchResponse = await client.GetAsync($"{UiApiRoutes.LedgerManualJournalEntryWorkbench}?fundProfileId=fund-alpha");
        workbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var workbench = await workbenchResponse.Content.ReadFromJsonAsync<ManualJournalEntryWorkbenchDto>(ServerJsonOptions);

        workbench!.AuditTrail.Should().Contain(item =>
            item.Action == "manual-je.submit-approval" &&
            item.Actor == "ops-user" &&
            item.CorrelationId == "manual-je-audit-submit" &&
            item.ReportGroupPrincipalIds != null &&
            item.ReportGroupPrincipalIds.Contains(UserRole.Admin.ToString()) &&
            item.ReportGroupPrincipalIds.Contains("journal-controller"));
    }

    [Fact]
    public async Task ManualJournalEntryEvidenceEndpoint_AttachesEvidenceAndRejectsStaleVersions()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var draft = ManualJournalEntryDraft() with { EvidenceAttachments = [], EvidenceLinks = [] };

        using var saveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryDrafts,
            new SaveManualJournalEntryDraftRequest(draft, "browser-user", CorrelationId: "manual-je-save-for-evidence"),
            ServerJsonOptions);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);

        using var attachResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryEvidence,
            new AttachManualJournalEntryEvidenceRequest(
                saved!.JournalEntryId,
                saved.FundProfileId,
                "controller",
                saved.Version,
                new ManualJournalEntryEvidenceAttachmentDto(
                    "endpoint-controller-support",
                    "Endpoint controller support",
                    "SourceDocument",
                    "/api/workstation/evidence/subjects/accounting-record/endpoint-controller-support",
                    "EvidenceVault",
                    DateTimeOffset.UtcNow,
                    "controller",
                    LineId: "debit-cash"),
                CorrelationId: "manual-je-attach-evidence-endpoint",
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/endpoint-close-checklist"]),
            ServerJsonOptions);
        var attached = await attachResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        using var staleResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryEvidence,
            new AttachManualJournalEntryEvidenceRequest(
                saved.JournalEntryId,
                saved.FundProfileId,
                "controller",
                saved.Version,
                new ManualJournalEntryEvidenceAttachmentDto(
                    "stale-endpoint-controller-support",
                    "Stale endpoint controller support",
                    "SourceDocument",
                    "/api/workstation/evidence/subjects/accounting-record/stale-endpoint-controller-support",
                    "EvidenceVault",
                    DateTimeOffset.UtcNow,
                    "controller")),
            ServerJsonOptions);
        using var workbenchResponse = await client.GetAsync($"{UiApiRoutes.LedgerManualJournalEntryWorkbench}?fundProfileId=fund-alpha");

        attachResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        staleResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        workbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        attached!.Version.Should().Be(saved.Version + 1);
        attached.EvidenceAttachments.Should().ContainSingle(item =>
            item.AttachmentId == "endpoint-controller-support" &&
            item.LineId == "debit-cash");
        attached.EvidenceLinks.Should().Contain(new[]
        {
            "/api/workstation/evidence/subjects/accounting-record/endpoint-close-checklist",
            "/api/workstation/evidence/subjects/accounting-record/endpoint-controller-support"
        });
        var workbench = await workbenchResponse.Content.ReadFromJsonAsync<ManualJournalEntryWorkbenchDto>(ServerJsonOptions);
        workbench!.AuditTrail.Should().Contain(item =>
            item.Action == "manual-je.attach-evidence" &&
            item.CorrelationId == "manual-je-attach-evidence-endpoint");
    }

    [Fact]
    public async Task ManualJournalEntryEndpoints_RejectPeriodLockedMutations()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        var draft = ManualJournalEntryDraft();

        using var lockedSaveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryDrafts,
            new SaveManualJournalEntryDraftRequest(draft, "browser-user", PeriodIsLocked: true),
            ServerJsonOptions);
        using var validateResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryValidate,
            new ValidateManualJournalEntryDraftRequest(draft, "browser-user", PeriodIsLocked: true),
            ServerJsonOptions);
        using var saveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryDrafts,
            new SaveManualJournalEntryDraftRequest(draft, "browser-user"),
            ServerJsonOptions);
        var saved = await saveResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        using var lockedSubmitResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntrySubmitApproval,
            new SubmitManualJournalEntryApprovalRequest(
                saved!.JournalEntryId,
                saved.FundProfileId,
                "controller",
                saved.Version,
                PeriodIsLocked: true),
            ServerJsonOptions);

        lockedSaveResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        lockedSubmitResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var validated = await validateResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        validated!.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "manual-je.period-locked" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task ManualJournalEntryLifecycleEndpoint_RequiresAdminMaintenanceForReleaseTransitions()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("capital", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                "browser-user"),
            ServerJsonOptions);
        var submitted = await SaveAndSubmitManualJournalDraftAsync(client, ManualJournalEntryDraft(), "non-admin-lifecycle");

        using var validateResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(
                submitted.JournalEntryId,
                submitted.FundProfileId,
                JournalEntryLifecycleActionDto.Validate,
                "browser-user",
                submitted.Version,
                Notes: "Validate submitted journal before release."),
            ServerJsonOptions);
        using var approveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(
                submitted.JournalEntryId,
                submitted.FundProfileId,
                JournalEntryLifecycleActionDto.Approve,
                "browser-user",
                submitted.Version,
                Notes: "Non-admin operator attempted to approve a submitted journal."),
            ServerJsonOptions);

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var validated = await validateResponse.Content.ReadFromJsonAsync<JournalEntryLifecycleActionResultDto>(ServerJsonOptions);
        validated!.Transition.Action.Should().Be(JournalEntryLifecycleActionDto.Validate);
        validated.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
    }

    [Fact]
    public async Task ManualJournalEntryWorkbenchEndpoints_SaveAndSubmitRetainsFullDimensionalAccounting()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
                "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("capital", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                "browser-user"),
            ServerJsonOptions);

        var instrumentId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var dimensions = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-master",
            SleeveId: "sleeve-income",
            StrategyId: "strategy-direct-lending",
            InvestorId: "investor:lp-1",
            CapitalAccountId: "capital-account:fund-alpha:lp-1",
            InstrumentId: instrumentId,
            TaxLotId: "tax-lot-2026-06",
            CostCenterId: "cost-center-accounting",
            CounterpartyId: "counterparty-bank",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Class"] = "FundAlpha",
                ["Department"] = "Accounting"
            });
        var draft = ManualJournalEntryDraft() with
        {
            Dimensions = dimensions,
            Lines =
            [
                new ManualJournalEntryLineDto(
                    "debit-cash",
                    AccountingTemplateLineSideDto.Debit,
                    100m,
                    "USD",
                    "Assets:Cash",
                    EntityId: "entity-master",
                    SecurityId: instrumentId,
                    TaxLotId: "tax-lot-2026-06",
                    Dimensions: dimensions),
                new ManualJournalEntryLineDto(
                    "credit-capital",
                    AccountingTemplateLineSideDto.Credit,
                    100m,
                    "USD",
                    "Equity:Capital Contributions",
                    EntityId: "entity-master",
                    SecurityId: instrumentId,
                    TaxLotId: "tax-lot-2026-06",
                    Dimensions: dimensions)
            ]
        };

        var submitted = await SaveAndSubmitManualJournalDraftAsync(client, draft, "full-dimensions");
        using var workbenchResponse = await client.GetAsync($"{UiApiRoutes.LedgerManualJournalEntryWorkbench}?fundProfileId=fund-alpha");

        workbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var workbench = await workbenchResponse.Content.ReadFromJsonAsync<ManualJournalEntryWorkbenchDto>(ServerJsonOptions);
        var retained = workbench!.Drafts.Should().ContainSingle(item => item.JournalEntryId == submitted.JournalEntryId).Subject;
        submitted.Dimensions.Should().BeEquivalentTo(dimensions);
        foreach (var line in submitted.Lines)
        {
            line.Dimensions.Should().NotBeNull();
        }

        retained.Dimensions.Should().BeEquivalentTo(dimensions);
        foreach (var line in retained.Lines)
        {
            var lineDimensions = line.Dimensions;
            lineDimensions.Should().NotBeNull();
            lineDimensions!.FundId.Should().Be("fund-alpha");
            lineDimensions.EntityId.Should().Be("entity-master");
            lineDimensions.SleeveId.Should().Be("sleeve-income");
            lineDimensions.StrategyId.Should().Be("strategy-direct-lending");
            lineDimensions.InvestorId.Should().Be("investor:lp-1");
            lineDimensions.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
            lineDimensions.InstrumentId.Should().Be(instrumentId);
            lineDimensions.TaxLotId.Should().Be("tax-lot-2026-06");
            lineDimensions.CostCenterId.Should().Be("cost-center-accounting");
            lineDimensions.CounterpartyId.Should().Be("counterparty-bank");
            lineDimensions.ExternalGlDimensions.Should().ContainKey("Class").WhoseValue.Should().Be("FundAlpha");
            lineDimensions.ExternalGlDimensions.Should().ContainKey("Department").WhoseValue.Should().Be("Accounting");
        }
    }

    [Fact]
    public async Task AccountingRulesAndLifecycleEndpoints_DryRunApprovePostAndReverseDraft()
    {
        var manualJournalStore = new WritableEndpointLedgerJournalStore();
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<ILedgerJournalStore>(manualJournalStore);
                RegisterAccountingConfigurationServices(services);
            },
            currentUserPermissions: UserPermission.AdminMaintenance);
        await app.Services.GetRequiredService<IAccountingPolicyService>().CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Gaap,
            PolicyId: "gaap-interest-v1",
            Version: "v1",
            DisplayName: "GAAP interest accrual",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto(
                "gaap-interest-rules",
                "v1",
                [
                    new AccountingPolicyRuleDto(
                        "policy.interest-accrual",
                        AccountingTreatmentKindDto.Accrual,
                        RuleVersion: "v1",
                        SourceEventType: "InterestAccrual",
                        RequiresEvidence: true,
                        RequiresApproval: true,
                        AllowsAutoPosting: false)
                ])));
        var client = app.GetTestClient();
        var journalPreparerClient = app.GetTestClient();
        journalPreparerClient.DefaultRequestHeaders.Add("X-Meridian-Test-User", "journal-preparer");
        var journalControllerClient = app.GetTestClient();
        journalControllerClient.DefaultRequestHeaders.Add("X-Meridian-Test-User", "journal-controller");
        var sourceEventId = Guid.NewGuid();
        var candidateLedgerBookId = Guid.NewGuid();
        var candidatePeriodId = Guid.NewGuid();
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest("fund-alpha", new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"), "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest("fund-alpha", new ChartOfAccountsNodeDto("income", "Income:Interest", "Interest Income", "Revenue"), "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRules,
            new UpsertPostingRuleRequest(
                "fund-alpha",
                new PostingRuleDto(
                    "rule-generated-interest",
                    "Generated interest accrual",
                    "InterestAccrual",
                    "",
                    RuleVersion: "v2",
                    EffectiveFrom: new DateOnly(2026, 1, 1),
                    Priority: 10,
                    Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
                    Conditions:
                    [
                        new AccountingRuleConditionDto("threshold", "amount", AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual, "100")
                    ],
                    Formulas:
                    [
                        new AccountingRuleFormulaDto("source", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                    ],
                    GeneratedPostings:
                    [
                        new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source", 0m),
                        new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source", 0m)
                    ]),
                "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest("fund-alpha", new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"), "browser-user", LedgerBookId: candidateLedgerBookId),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest("fund-alpha", new ChartOfAccountsNodeDto("income", "Income:Interest", "Interest Income", "Revenue"), "browser-user", LedgerBookId: candidateLedgerBookId),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRules,
            new UpsertPostingRuleRequest(
                "fund-alpha",
                new PostingRuleDto(
                    "rule-generated-interest",
                    "Generated interest accrual",
                    "InterestAccrual",
                    "",
                    RuleVersion: "v2",
                    EffectiveFrom: new DateOnly(2026, 1, 1),
                    Priority: 10,
                    Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
                    Conditions:
                    [
                        new AccountingRuleConditionDto("threshold", "amount", AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual, "100")
                    ],
                    Formulas:
                    [
                        new AccountingRuleFormulaDto("source", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                    ],
                    GeneratedPostings:
                    [
                        new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source", 0m),
                        new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source", 0m)
                    ]),
                "browser-user",
                LedgerBookId: candidateLedgerBookId),
            ServerJsonOptions);

        using var dryRunResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleDryRun,
            new RuleDryRunRequestDto(
                "fund-alpha",
                "InterestAccrual",
                175m,
                "USD",
                new DateOnly(2026, 6, 30),
                "browser-user",
                Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                CounterpartyId: "counterparty-bank"),
            ServerJsonOptions);
        using var candidateResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidates,
            new PostingRuleJournalCandidateRequestDto(
                "fund-alpha",
                "InterestAccrual",
                175m,
                "USD",
                new DateOnly(2026, 6, 30),
                "browser-user",
                candidateLedgerBookId,
                candidatePeriodId,
                DateTimeOffset.Parse("2026-06-30T21:00:00Z"),
                "Build governed interest accrual draft candidate",
                AccountingBasis: AccountingBasisKindDto.Gaap,
                LedgerBookId: candidateLedgerBookId,
                Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                CounterpartyId: "counterparty-bank",
                SourceEventId: sourceEventId,
                PolicyId: "gaap-interest-v1",
                TreatmentKind: AccountingTreatmentKindDto.Accrual,
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/interest-accrual-source-event"]),
            ServerJsonOptions);
        using var projectionSetResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleProjectionSets,
            new AccountingBasisProjectionSetRequestDto(
                "fund-alpha",
                "InterestAccrual",
                175m,
                "USD",
                new DateOnly(2026, 6, 30),
                "browser-user",
                sourceEventId,
                DateTimeOffset.Parse("2026-06-30T21:00:00Z"),
                "Build governed multi-basis interest accrual draft candidates",
                [
                    new AccountingBasisProjectionTargetDto(
                        AccountingBasisKindDto.Gaap,
                        candidateLedgerBookId,
                        candidatePeriodId,
                        PolicyId: "gaap-interest-v1",
                        TreatmentKind: AccountingTreatmentKindDto.Accrual,
                        Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"))
                ],
                CounterpartyId: "counterparty-bank",
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/interest-accrual-source-event"]),
            ServerJsonOptions);
        using var ruleTestsResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleTests,
            new ExecuteAccountingRuleTestCasesRequestDto(
                FundProfileId: "fund-alpha",
                Actor: "browser-user",
                TestCases:
                [
                    new AccountingRuleTestCaseDto(
                        "interest-accrual-happy-path",
                        "Interest accrual happy path",
                        new RuleDryRunRequestDto(
                            "fund-alpha",
                            "InterestAccrual",
                            175m,
                            "USD",
                            new DateOnly(2026, 6, 30),
                            "browser-user",
                            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                            CounterpartyId: "counterparty-bank"),
                        ExpectedRuleId: "rule-generated-interest",
                        ExpectedRuleVersion: "v2"),
                    new AccountingRuleTestCaseDto(
                        "interest-accrual-expected-failure",
                        "Interest accrual expected failure",
                        new RuleDryRunRequestDto(
                            "fund-alpha",
                            "InterestAccrual",
                            175m,
                            "USD",
                            new DateOnly(2026, 6, 30),
                            "browser-user",
                            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                            CounterpartyId: "counterparty-bank"),
                        ExpectedRuleId: "rule-missing",
                        ExpectedRuleVersion: "v2")
                ]),
            ServerJsonOptions);
        using var savedRuleTestCaseResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleTestCases,
            new UpsertAccountingRuleTestCaseRequest(
                FundProfileId: "fund-alpha",
                TestCase: new AccountingRuleTestCaseDto(
                    "interest-accrual-saved-happy-path",
                    "Saved interest accrual happy path",
                    new RuleDryRunRequestDto(
                        "fund-alpha",
                        "InterestAccrual",
                        225m,
                        "USD",
                        new DateOnly(2026, 6, 30),
                        "browser-user",
                        Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                        CounterpartyId: "counterparty-bank"),
                ExpectedRuleId: "rule-generated-interest",
                ExpectedRuleVersion: "v2"),
            Actor: "browser-user",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/rule-test-interest-accrual-saved-happy-path-rule-generated-interest-v2"]),
            ServerJsonOptions);
        using var persistedRuleTestsResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleTests,
            new ExecuteAccountingRuleTestCasesRequestDto(
                FundProfileId: "fund-alpha",
                Actor: "browser-user"),
            ServerJsonOptions);
        var persistedRuleTests = await persistedRuleTestsResponse.Content.ReadFromJsonAsync<AccountingRuleTestSuiteResultDto>(ServerJsonOptions);
        persistedRuleTests!.Results.Should().OnlyContain(
            result => result.Passed,
            string.Join(" | ", persistedRuleTests.Results.Select(result =>
                $"{result.TestCaseId}:{string.Join(",", result.AssertionIssues.Select(issue => issue.Code))}:{result.DryRunResult.SelectedRuleId}")));
        using var assistantPromotionApprovalResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRulePromotionApprovals,
            new ApprovePostingRulePromotionRequest(
                FundProfileId: "fund-alpha",
                RuleId: "rule-generated-interest",
                RuleVersion: "v2",
                Actor: "controller",
                ApprovalId: "approval-rule-generated-interest-v2",
                Notes: "Assistant drafted approval should remain unpromoted.",
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/rule-generated-interest-review-v2-approval-rule-generated-interest-v2"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            ServerJsonOptions);
        using var promotionApprovalResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRulePromotionApprovals,
            new ApprovePostingRulePromotionRequest(
                FundProfileId: "fund-alpha",
                RuleId: "rule-generated-interest",
                RuleVersion: "v2",
                Actor: "controller",
                ApprovalId: "approval-rule-generated-interest-v2",
                Notes: "Controller approved the generated interest rule after dry-run regression review.",
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/rule-generated-interest-review-v2-approval-rule-generated-interest-v2"]),
            ServerJsonOptions);
        var assistantPromotionApprovalResponseBody = await assistantPromotionApprovalResponse.Content.ReadAsStringAsync();
        assistantPromotionApprovalResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, assistantPromotionApprovalResponseBody);
        var promotionApprovalResponseBody = await promotionApprovalResponse.Content.ReadAsStringAsync();
        promotionApprovalResponse.StatusCode.Should().Be(HttpStatusCode.OK, promotionApprovalResponseBody);
        var lifecyclePeriodId = Guid.NewGuid();
        var lifecycleDraft = ManualJournalEntryDraft() with
        {
            AccountingBasis = AccountingBasisKindDto.Gaap,
            PeriodId = lifecyclePeriodId.ToString("D")
        };
        var lifecycleLedgerBookId = lifecycleDraft.LedgerBookId!.Value;
        var lifecycleOpenedAt = DateTimeOffset.UtcNow;
        manualJournalStore.Seed(
            new LedgerBookRecord(
                lifecycleLedgerBookId,
                "fund-alpha",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Fund Alpha GAAP manual journal book",
                "USD",
                lifecycleOpenedAt,
                lifecycleOpenedAt,
                AccountingBasis: lifecycleDraft.AccountingBasis,
                AccountingPolicyId: "gaap-manual-journal-v1",
                AccountingPolicyVersion: "v1"),
            new LedgerAccountingPeriod(
                lifecyclePeriodId,
                lifecycleLedgerBookId,
                2026,
                6,
                "2026-06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Open",
                lifecycleOpenedAt,
                null,
                1));
        var configurationService = app.Services.GetRequiredService<IAccountingConfigurationService>();
        await configurationService.UpsertChartNodeAsync(
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("cash-book-unscoped", "Assets:Cash", "Cash", "Asset"),
                "browser-user",
                LedgerBookId: lifecycleDraft.LedgerBookId));
        await configurationService.UpsertChartNodeAsync(
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("capital-unscoped", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                "browser-user",
                LedgerBookId: lifecycleDraft.LedgerBookId));
        await configurationService.UpsertChartNodeAsync(
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("capital-fund", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                "browser-user"));
        using var lifecycleCapitalAccountResponse = await journalPreparerClient.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("capital-fund-http", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
                "browser-user",
                LedgerBookId: lifecycleDraft.LedgerBookId),
            ServerJsonOptions);
        lifecycleCapitalAccountResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lifecycleWorkspace = await configurationService.GetWorkspaceAsync("fund-alpha", lifecycleDraft.LedgerBookId);
        lifecycleWorkspace.ChartOfAccounts.Should().Contain(item => item.Path == "Assets:Cash");
        lifecycleWorkspace.ChartOfAccounts.Should().Contain(item => item.Path == "Equity:Capital Contributions");
        var submitted = await SaveAndSubmitManualJournalDraftAsync(journalPreparerClient, lifecycleDraft, "lifecycle");
        using var approveResponse = await journalControllerClient.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(
                submitted.JournalEntryId,
                submitted.FundProfileId,
                JournalEntryLifecycleActionDto.Approve,
                "controller",
                submitted.Version,
                Notes: "Controller approved the submitted manual journal.",
                EvidenceLinks: [ManualJournalApprovalEvidence(submitted)],
                LedgerBookId: submitted.LedgerBookId),
            ServerJsonOptions);
        var approveResponseBody = await approveResponse.Content.ReadAsStringAsync();
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK, approveResponseBody);
        var approved = await approveResponse.Content.ReadFromJsonAsync<JournalEntryLifecycleActionResultDto>(ServerJsonOptions);
        using var postResponse = await journalControllerClient.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(
                approved!.JournalEntry.JournalEntryId,
                approved.JournalEntry.FundProfileId,
                JournalEntryLifecycleActionDto.Post,
                "controller",
                approved.JournalEntry.Version,
                Notes: "Posted after controller approval.",
                EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)],
                LedgerBookId: approved.JournalEntry.LedgerBookId),
            ServerJsonOptions);
        var postResponseBody = await postResponse.Content.ReadAsStringAsync();
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK, postResponseBody);
        var posted = await postResponse.Content.ReadFromJsonAsync<JournalEntryLifecycleActionResultDto>(ServerJsonOptions);
        using var extendedJournalIdReverseResponse = await journalControllerClient.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(
                posted!.JournalEntry.JournalEntryId,
                posted.JournalEntry.FundProfileId,
                JournalEntryLifecycleActionDto.Reverse,
                "controller",
                posted.JournalEntry.Version,
                Notes: "Extended journal id evidence should not reverse a posted journal.",
                EvidenceLinks:
                [
                    $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{posted.JournalEntry.LedgerBookId!.Value:D}/{posted.JournalEntry.JournalEntryId:D}ffff{ManualJournalScopeQuery(posted.JournalEntry)}"
                ],
                LedgerBookId: posted.JournalEntry.LedgerBookId),
            ServerJsonOptions);
        using var reverseResponse = await journalControllerClient.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(
                posted.JournalEntry.JournalEntryId,
                posted.JournalEntry.FundProfileId,
                JournalEntryLifecycleActionDto.Reverse,
                "controller",
                posted.JournalEntry.Version,
                Notes: "Reverse after controller close review.",
                EvidenceLinks: [ManualJournalReversalEvidence(posted.JournalEntry)],
                LedgerBookId: posted.JournalEntry.LedgerBookId),
            ServerJsonOptions);

        dryRunResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        candidateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        projectionSetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ruleTestsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        savedRuleTestCaseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        persistedRuleTestsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        assistantPromotionApprovalResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        promotionApprovalResponse.StatusCode.Should().Be(HttpStatusCode.OK, promotionApprovalResponseBody);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        extendedJournalIdReverseResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        reverseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dryRun = await dryRunResponse.Content.ReadFromJsonAsync<RuleDryRunResultDto>(ServerJsonOptions);
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<PostingRuleJournalCandidateResultDto>(ServerJsonOptions);
        var projectionSet = await projectionSetResponse.Content.ReadFromJsonAsync<AccountingBasisProjectionSetDto>(ServerJsonOptions);
        var ruleTests = await ruleTestsResponse.Content.ReadFromJsonAsync<AccountingRuleTestSuiteResultDto>(ServerJsonOptions);
        var savedWorkspace = await savedRuleTestCaseResponse.Content.ReadFromJsonAsync<AccountingConfigurationWorkspaceDto>(ServerJsonOptions);
        var promotionWorkspace = await promotionApprovalResponse.Content.ReadFromJsonAsync<AccountingConfigurationWorkspaceDto>(ServerJsonOptions);
        var reverse = await reverseResponse.Content.ReadFromJsonAsync<JournalEntryLifecycleActionResultDto>(ServerJsonOptions);
        dryRun!.SelectedRuleId.Should().Be("rule-generated-interest");
        dryRun.IsPostingBalanced.Should().BeTrue();
        dryRun.GeneratedPostingLines.Should().HaveCount(2);
        candidate!.SelectedRuleId.Should().Be("rule-generated-interest");
        candidate.SelectedRuleVersion.Should().Be("v2");
        candidate.GeneratedPostingLines.Should().HaveCount(2);
        candidate.HasBlockingIssues.Should().BeFalse();
        candidate.CanSubmitForApproval.Should().BeTrue();
        candidate.CanPostWithoutAdditionalApproval.Should().BeFalse();
        candidate.PostingCommand.Should().NotBeNull();
        candidate.PostingCommand!.SourceEventId.Should().Be(sourceEventId);
        candidate.PostingCommand.LedgerBookId.Should().Be(candidateLedgerBookId);
        candidate.PostingCommand.SourceEventType.Should().Be("InterestAccrual");
        candidate.PostingCommand.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Pending);
        candidate.EvidenceLinks.Should().ContainSingle("/api/workstation/evidence/subjects/accounting-record/interest-accrual-source-event");
        candidate.Issues.Should().Contain(issue =>
            issue.Code == "JOURNAL_DRAFT_APPROVAL_REQUIRED" &&
            !issue.BlocksCandidate);
        projectionSet!.SourceEventId.Should().Be(sourceEventId);
        projectionSet.Items.Should().ContainSingle(item =>
            item.AccountingBasis == AccountingBasisKindDto.Gaap &&
            item.LedgerBookId == candidateLedgerBookId &&
            item.PeriodId == candidatePeriodId &&
            item.Status == "ReadyForApproval" &&
            item.Candidate.PostingCommand!.LedgerBookId == candidateLedgerBookId &&
            item.Candidate.PostingCommand.SourceEventId == sourceEventId);
        ruleTests!.TotalCount.Should().Be(2);
        ruleTests.PassedCount.Should().Be(1);
        ruleTests.FailedCount.Should().Be(1);
        ruleTests.Results.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-happy-path" &&
            item.Passed &&
            item.DryRunResult.SelectedRuleId == "rule-generated-interest");
        ruleTests.Results.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-expected-failure" &&
            !item.Passed &&
            item.AssertionIssues.Any(issue => issue.Code == "rule-test.expected-rule-mismatch"));
        savedWorkspace!.RuleTestCases.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-saved-happy-path" &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/rule-test-interest-accrual-saved-happy-path-rule-generated-interest-v2"));
        savedWorkspace.AuditTrail.Should().Contain(item => item.Action == "rule-test-case.upsert");
        savedWorkspace.RulesStudio.Should().NotBeNull();
        savedWorkspace.RulesStudio!.Summary.TotalRules.Should().Be(1);
        savedWorkspace.RulesStudio.Summary.GeneratedPostingRules.Should().Be(1);
        savedWorkspace.RulesStudio.Summary.RulesRequiringPromotionApproval.Should().Be(0);
        savedWorkspace.RulesStudio.Summary.PendingPromotionApprovalRules.Should().Be(0);
        savedWorkspace.RulesStudio.Summary.RulesWithSavedRegressionTests.Should().Be(1);
        savedWorkspace.RulesStudio.PromotionQueue.Should().BeEmpty();
        savedWorkspace.RulesStudio.Rules.Should().ContainSingle(item =>
            item.RuleId == "rule-generated-interest" &&
            item.UsesGeneratedPostings &&
            item.FormulaCount == 1 &&
            item.GeneratedPostingLineCount == 2 &&
            item.SavedTestCaseCount == 1 &&
            item.CanDryRun &&
            !item.CanRequestPromotion &&
            item.CanActivate);
        persistedRuleTests!.TotalCount.Should().Be(1);
        persistedRuleTests.PassedCount.Should().Be(1);
        persistedRuleTests.Results.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-saved-happy-path" &&
            item.DryRunResult.SelectedRuleId == "rule-generated-interest");
        var promotedRule = promotionWorkspace!.PostingRules.Should().ContainSingle(item => item.RuleId == "rule-generated-interest").Subject;
        promotedRule.RequiresPromotionApproval.Should().BeTrue();
        promotedRule.PromotionApproval!.ApprovalId.Should().Be("approval-rule-generated-interest-v2");
        promotedRule.PromotionApproval.ApprovedBy.Should().Be("ops-user");
        promotedRule.Versions.Should().Contain(version =>
            version.Version == "v2" &&
            version.PromotionApproval != null &&
            version.PromotionApproval.ApprovalId == "approval-rule-generated-interest-v2");
        promotionWorkspace.RulesStudio.Should().NotBeNull();
        promotionWorkspace.RulesStudio!.Summary.ApprovedPromotionRules.Should().Be(1);
        promotionWorkspace.RulesStudio.Summary.PendingPromotionApprovalRules.Should().Be(0);
        promotionWorkspace.RulesStudio.PromotionQueue.Should().BeEmpty();
        promotionWorkspace.RulesStudio.Rules.Should().ContainSingle(item =>
            item.RuleId == "rule-generated-interest" &&
            item.IsPromotionApproved &&
            item.PromotionApprovalId == "approval-rule-generated-interest-v2" &&
            !item.CanRequestPromotion &&
            item.CanActivate);
        promotionWorkspace.AuditTrail.Should().Contain(item =>
            item.Action == "posting-rule.promotion-approve" &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/accounting-record/rule-generated-interest-review-v2-approval-rule-generated-interest-v2"));
        approved.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        reverse!.GeneratedJournalEntries.Should().ContainSingle(item =>
            item.Status == ManualJournalEntryStatusDto.Draft &&
            item.EntryType == ManualJournalEntryTypeDto.Reversal &&
            item.ReversalOfJournalEntryId == posted.JournalEntry.JournalEntryId);
    }

    [Fact]
    public async Task AccountingConfigurationWorkspace_IncludesRulesStudioPromotionQueue()
    {
        await using var app = await CreateAppAsync(
            RegisterAccountingConfigurationServices,
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest("fund-alpha", new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"), "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationChart,
            new UpsertChartOfAccountsNodeRequest("fund-alpha", new ChartOfAccountsNodeDto("income", "Income:Interest", "Interest Income", "Revenue"), "browser-user"),
            ServerJsonOptions);
        await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRules,
            new UpsertPostingRuleRequest(
                "fund-alpha",
                new PostingRuleDto(
                    "rule-studio-interest",
                    "Studio interest accrual",
                    "InterestAccrual",
                    "",
                    RuleVersion: "v3",
                    EffectiveFrom: new DateOnly(2026, 1, 1),
                    EffectiveTo: new DateOnly(2026, 12, 31),
                    Priority: 25,
                    Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                    Conditions:
                    [
                        new AccountingRuleConditionDto("threshold", "amount", AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual, "100")
                    ],
                    Formulas:
                    [
                        new AccountingRuleFormulaDto("source", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                    ],
                    GeneratedPostings:
                    [
                        new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source", 0m),
                        new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source", 0m)
                    ],
                    RequiresPromotionApproval: true),
                "browser-user"),
            ServerJsonOptions);

        using var savedRuleTestCaseResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleTestCases,
            new UpsertAccountingRuleTestCaseRequest(
                FundProfileId: "fund-alpha",
                TestCase: new AccountingRuleTestCaseDto(
                    "studio-interest-happy-path",
                    "Studio interest happy path",
                    new RuleDryRunRequestDto(
                        "fund-alpha",
                        "InterestAccrual",
                        225m,
                        "USD",
                        new DateOnly(2026, 6, 30),
                        "browser-user",
                        Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")),
                    ExpectedRuleId: "rule-studio-interest",
                    ExpectedRuleVersion: "v3"),
                Actor: "browser-user",
                EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/regression-test-studio-interest-happy-path-rule-studio-interest-v3"]),
            ServerJsonOptions);

        savedRuleTestCaseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var workspace = await savedRuleTestCaseResponse.Content.ReadFromJsonAsync<AccountingConfigurationWorkspaceDto>(ServerJsonOptions);

        workspace!.RulesStudio.Should().NotBeNull();
        workspace.RulesStudio!.Summary.TotalRules.Should().Be(1);
        workspace.RulesStudio.Summary.EffectiveDatedRules.Should().Be(1);
        workspace.RulesStudio.Summary.GeneratedPostingRules.Should().Be(1);
        workspace.RulesStudio.Summary.RulesWithConditions.Should().Be(1);
        workspace.RulesStudio.Summary.RulesWithFormulas.Should().Be(1);
        workspace.RulesStudio.Summary.RulesRequiringPromotionApproval.Should().Be(1);
        workspace.RulesStudio.Summary.PendingPromotionApprovalRules.Should().Be(1);
        workspace.RulesStudio.Summary.RulesWithSavedRegressionTests.Should().Be(1);
        workspace.RulesStudio.PromotionQueue.Should().ContainSingle(item =>
            item.RuleId == "rule-studio-interest" &&
            item.RuleVersion == "v3" &&
            item.RegressionTestCaseCount == 1 &&
            item.MissingRegressionEvidenceCount == 0);
        workspace.RulesStudio.Rules.Should().ContainSingle(item =>
            item.RuleId == "rule-studio-interest" &&
            item.Priority == 25 &&
            item.EffectiveFrom == new DateOnly(2026, 1, 1) &&
            item.EffectiveTo == new DateOnly(2026, 12, 31) &&
            item.UsesGeneratedPostings &&
            item.ConditionCount == 1 &&
            item.FormulaCount == 1 &&
            item.GeneratedPostingLineCount == 2 &&
            item.SavedTestCaseCount == 1 &&
            item.RequiresPromotionApproval &&
            !item.IsPromotionApproved &&
            item.CanDryRun);
    }

    [Fact]
    public async Task ManualJournalEntryWorkbenchEndpoints_WhenReviewedAutomationSubmitsApproval_ReturnsConflictWithoutSubmission()
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
                "assistant",
                saved.Version,
                CorrelationId: "manual-je-submit",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            ServerJsonOptions);
        using var workbenchResponse = await client.GetAsync($"{UiApiRoutes.LedgerManualJournalEntryWorkbench}?fundProfileId=fund-alpha");

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        workbenchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var workbench = await workbenchResponse.Content.ReadFromJsonAsync<ManualJournalEntryWorkbenchDto>(ServerJsonOptions);
        workbench.Should().NotBeNull();
        workbench!.Drafts.Should().ContainSingle(item =>
            item.JournalEntryId == saved.JournalEntryId &&
            item.Status == ManualJournalEntryStatusDto.Draft);
        workbench.AuditTrail.Select(item => item.Action).Should().NotContain("manual-je.submit-approval");
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

    [Fact]
    public async Task LedgerPrivateCapitalActivity_WhenCapitalAccountFilterMatchesPostedChildImpact_RetainsFundEventLedgerRecord()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        const string fundEventId = "fund-event:fund-alpha:capital-call:shared-posted-endpoint";
        var lp1Journal = PostedCapitalCallJournal(
            timestamp,
            fundEventId,
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            100000m,
            "lp-1");
        var lp2Journal = PostedCapitalCallJournal(
            timestamp.AddMinutes(5),
            fundEventId,
            "capital-account:fund-alpha:lp-2",
            "investor:lp-2",
            200000m,
            "lp-2");
        var journalStore = new PostedPrivateCapitalEndpointLedgerJournalStore(
            new LedgerBookRecord(
                ledgerBookId,
                "fund-alpha",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Fund Alpha GAAP book",
                "USD",
                timestamp,
                timestamp),
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                6,
                "2026-06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Closed",
                timestamp,
                timestamp,
                1),
            new LedgerJournalEntryRecord(
                lp1Journal,
                Guid.NewGuid(),
                periodId,
                CommandId: null,
                CorrelationId: null,
                GlobalSequence: 1,
                CreatedAt: timestamp),
            new LedgerJournalEntryRecord(
                lp2Journal,
                Guid.NewGuid(),
                periodId,
                CommandId: null,
                CorrelationId: null,
                GlobalSequence: 2,
                CreatedAt: timestamp.AddMinutes(5)));
        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterAccountingConfigurationServices(services);
                services.AddSingleton<ILedgerJournalStore>(journalStore);
            },
            currentUserPermissions: UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"{UiApiRoutes.LedgerPrivateCapitalActivity}?fundProfileId=fund-alpha&ledgerBookId={ledgerBookId:D}&capitalAccountId={Uri.EscapeDataString("capital-account:fund-alpha:lp-2")}&investorId={Uri.EscapeDataString("investor:lp-2")}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var activity = await response.Content.ReadFromJsonAsync<PrivateCapitalActivityProjectionDto>(ServerJsonOptions);
        activity.Should().NotBeNull();
        activity!.FundEventCount.Should().Be(1);
        activity.CapitalAccountCount.Should().Be(1);
        activity.NetCapitalActivity.Should().Be(200000m);
        activity.FundEvents.Should().ContainSingle(item => item.FundEventId == fundEventId);
        activity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.NetActivity == 200000m);
        activity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.NetCapitalActivity == 200000m &&
            item.RunningNetActivity == 200000m);
        activity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2");
        activity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.ReportWorkflowState == "Missing");
        activity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.NetCapitalActivity == 300000m &&
            item.CapitalAccountOpeningNetActivity == 0m &&
            item.CapitalAccountEndingNetActivity == 200000m &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1 &&
            item.ReportOutputCount == 1 &&
            item.CapitalAccountSubledgerEntries.Single().CapitalAccountId == "capital-account:fund-alpha:lp-2");
        activity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.NetCapitalActivity == 200000m &&
            item.EndingNetActivity == 200000m &&
            item.FundEventRecords.Count == 1 &&
            item.SubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
    }

    private static void RegisterAccountingConfigurationServices(IServiceCollection services)
    {
        services.AddSingleton<IAccountingConfigurationStore, InMemoryAccountingConfigurationStore>();
        services.AddSingleton<IAccountingActionAuditStore, InMemoryAccountingActionAuditStore>();
        services.AddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.AddSingleton<IAccountingPolicyService, AccountingPolicyService>();
        services.AddSingleton<IAccountingBasisProjectionService, AccountingBasisProjectionService>();
        services.AddSingleton<IAccountingJournalDraftService, AccountingJournalDraftService>();
        services.AddSingleton<IAccountingPostingCandidateService, AccountingPostingCandidateService>();
        services.AddSingleton<IAccountingPostingCandidateWriteBuilder, AccountingPostingCandidateService>();
        services.AddSingleton<IAccountingPostingCandidatePostService>(sp =>
            new AccountingPostingCandidatePostService(
                sp.GetRequiredService<IAccountingPostingCandidateWriteBuilder>(),
                sp.GetService<ILedgerJournalStore>()));
        services.AddSingleton<IAccountingBasisProjectionSetService, AccountingBasisProjectionSetService>();
        services.AddSingleton<IManualJournalEntryDraftStore, InMemoryManualJournalEntryDraftStore>();
        services.AddSingleton<IManualJournalEntryWorkbenchService, ManualJournalEntryWorkbenchService>();
    }

    private sealed class CapturingAccountingBasisProjectionSetService : IAccountingBasisProjectionSetService
    {
        public List<AccountingBasisProjectionSetRequestDto> Requests { get; } = [];

        public Task<AccountingBasisProjectionSetDto> BuildProjectionSetAsync(
            AccountingBasisProjectionSetRequestDto request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            var items = request.Targets
                .Select(target => new AccountingBasisProjectionItemDto(
                    target.AccountingBasis,
                    target.LedgerBookId,
                    target.PeriodId,
                    BuildCandidate(request, target),
                    "ReadyForApproval"))
                .ToArray();

            return Task.FromResult(new AccountingBasisProjectionSetDto(
                request.SourceEventId,
                request.FundProfileId,
                request.SourceEventType,
                request.EffectiveDate,
                request.EventAmount,
                request.Currency.ToUpperInvariant(),
                DateTimeOffset.UnixEpoch,
                items));
        }

        private static PostingRuleJournalCandidateResultDto BuildCandidate(
            AccountingBasisProjectionSetRequestDto request,
            AccountingBasisProjectionTargetDto target)
        {
            var dryRun = new RuleDryRunResultDto(
                request.FundProfileId,
                target.LedgerBookId,
                request.SourceEventType,
                request.EffectiveDate,
                request.EventAmount,
                request.Currency,
                IsPostingBalanced: true,
                SelectedRuleId: "posting.interest-accrual",
                RuleMatches: [],
                GeneratedLines: [],
                ValidationIssues: [],
                GeneratedPostingLines: []);
            var command = new AccountingPostingCommandDto(
                Guid.NewGuid(),
                target.LedgerBookId,
                target.PeriodId,
                request.EffectiveDate,
                request.AccountingTimestamp,
                $"projection:{target.LedgerBookId:N}:{request.SourceEventId:N}",
                SourceEventId: request.SourceEventId,
                SourceEventType: request.SourceEventType,
                ApprovalState: AccountingPostingApprovalStateDto.Pending,
                LedgerBookId: target.LedgerBookId);

            return new PostingRuleJournalCandidateResultDto(
                dryRun,
                "posting.interest-accrual",
                "v1",
                [],
                command,
                Guid.NewGuid(),
                request.EventAmount,
                request.EventAmount,
                0m,
                true,
                false,
                true,
                false,
                request.EvidenceLinks,
                []);
        }
    }

    private sealed class CapturingAccountingPostingCandidatePostService : IAccountingPostingCandidatePostService
    {
        public List<PostPostingRuleJournalCandidateRequestDto> Requests { get; } = [];

        public Task<PostedPostingRuleJournalCandidateResultDto> PostCandidateAsync(
            PostPostingRuleJournalCandidateRequestDto request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            var candidate = BuildCandidate(request.Candidate);
            return Task.FromResult(new PostedPostingRuleJournalCandidateResultDto(
                candidate,
                new PostedLedgerJournalEntryResultDto(
                    candidate.JournalEntryId ?? Guid.NewGuid(),
                    request.Candidate.LedgerBookId ?? request.Candidate.AggregateId,
                    request.Candidate.AccountingBasis,
                    request.Candidate.PeriodId,
                    request.Candidate.AggregateId,
                    candidate.PostingCommand?.CommandId,
                    request.Candidate.SourceEventId,
                    request.Candidate.CorrelationId)));
        }

        private static PostingRuleJournalCandidateResultDto BuildCandidate(PostingRuleJournalCandidateRequestDto request)
        {
            var dryRun = new RuleDryRunResultDto(
                request.FundProfileId,
                request.LedgerBookId,
                request.SourceEventType,
                request.EffectiveDate,
                request.EventAmount,
                request.Currency,
                IsPostingBalanced: true,
                SelectedRuleId: "posting.interest-accrual",
                RuleMatches: [],
                GeneratedLines: [],
                ValidationIssues: [],
                GeneratedPostingLines: []);
            var command = new AccountingPostingCommandDto(
                Guid.NewGuid(),
                request.AggregateId,
                request.PeriodId,
                request.EffectiveDate,
                request.AccountingTimestamp,
                $"candidate-post:{request.AggregateId:N}:{request.SourceEventId:N}",
                SourceEventId: request.SourceEventId,
                CorrelationId: request.CorrelationId,
                SourceEventType: request.SourceEventType,
                ApprovalState: AccountingPostingApprovalStateDto.Approved,
                LedgerBookId: request.LedgerBookId);

            return new PostingRuleJournalCandidateResultDto(
                dryRun,
                "posting.interest-accrual",
                "v1",
                [],
                command,
                Guid.NewGuid(),
                request.EventAmount,
                request.EventAmount,
                0m,
                true,
                false,
                true,
                false,
                request.EvidenceLinks,
                []);
        }
    }

    private static async Task<ManualJournalEntryDraftDto> SaveAndSubmitManualJournalDraftAsync(
        HttpClient client,
        ManualJournalEntryDraftDto draft,
        string correlationSuffix)
    {
        using var saveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryDrafts,
            new SaveManualJournalEntryDraftRequest(
                draft,
                "browser-user",
                CorrelationId: $"manual-je-save-{correlationSuffix}",
                LedgerBookId: draft.LedgerBookId),
            ServerJsonOptions);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        saved!.ValidationIssues.Should().NotContain(
            issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical,
            string.Join(" | ", saved.ValidationIssues.Select(issue => $"{issue.Code}:{issue.Message}")));
        using var submitResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntrySubmitApproval,
            new SubmitManualJournalEntryApprovalRequest(
                saved.JournalEntryId,
                saved.FundProfileId,
                "controller",
                saved.Version,
                CorrelationId: $"manual-je-submit-{correlationSuffix}",
                LedgerBookId: saved.LedgerBookId),
            ServerJsonOptions);
        var submitResponseBody = await submitResponse.Content.ReadAsStringAsync();
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK, submitResponseBody);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ManualJournalEntryDraftDto>(ServerJsonOptions);
        submitted.Should().NotBeNull();
        return submitted!;
    }

    private static string ManualJournalApprovalEvidence(ManualJournalEntryDraftDto journalEntry)
        => $"/api/workstation/evidence/subjects/accounting-record/approval/ledger-book/{journalEntry.LedgerBookId!.Value:D}/{journalEntry.PeriodId}{ManualJournalScopeQuery(journalEntry)}";

    private static string ManualJournalPostingEvidence(ManualJournalEntryDraftDto journalEntry)
        => $"/api/workstation/evidence/subjects/accounting-record/posting/ledger-book/{journalEntry.LedgerBookId!.Value:D}/{journalEntry.PeriodId}{ManualJournalScopeQuery(journalEntry)}";

    private static string ManualJournalReversalEvidence(ManualJournalEntryDraftDto journalEntry)
        => $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{journalEntry.LedgerBookId!.Value:D}/{journalEntry.JournalEntryId:D}{ManualJournalScopeQuery(journalEntry)}";

    private static string ManualJournalScopeQuery(ManualJournalEntryDraftDto journalEntry)
    {
        var queryParts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(journalEntry.TenantId))
        {
            queryParts.Add($"tenantId={journalEntry.TenantId}");
        }

        if (!string.IsNullOrWhiteSpace(journalEntry.CompanyId))
        {
            queryParts.Add($"companyId={journalEntry.CompanyId}");
        }

        return queryParts.Count == 0 ? string.Empty : $"?{string.Join("&", queryParts)}";
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
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, amount, "USD", "Assets:Cash"),
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

    private static JournalEntry PostedCapitalCallJournal(
        DateTimeOffset timestamp,
        string fundEventId,
        string capitalAccountId,
        string investorId,
        decimal amount,
        string suffix)
    {
        var journalEntryId = Guid.NewGuid();
        return new JournalEntry(
            journalEntryId,
            timestamp,
            $"Posted Fund Alpha shared capital call - {suffix}",
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    amount,
                    0m,
                    $"Posted Fund Alpha shared capital call - {suffix}"),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Capital Contributions", LedgerAccountType.Equity, FinancialAccountId: capitalAccountId),
                    0m,
                    amount,
                    $"Posted Fund Alpha shared capital call - {suffix}")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: $"posted:fund-alpha:shared-capital-call:{suffix}",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: capitalAccountId,
                InvestorId: investorId,
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = $"source:shared-capital-call-{suffix}",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:shared-capital-call",
                    ["approvedBy"] = "controller"
                }));
    }

    private sealed class WritableEndpointLedgerJournalStore : ILedgerJournalStore
    {
        private readonly List<LedgerBookRecord> _books = [];
        private readonly List<LedgerAccountingPeriod> _periods = [];
        private readonly List<LedgerJournalEntryRecord> _records = [];

        public List<LedgerJournalEntryWrite> Appended { get; } = [];

        public void Seed(LedgerBookRecord book, LedgerAccountingPeriod period)
        {
            _books.RemoveAll(item => item.LedgerBookId == book.LedgerBookId);
            _periods.RemoveAll(item => item.PeriodId == period.PeriodId);
            _books.Add(book);
            _periods.Add(period);
        }

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var period = _periods.SingleOrDefault(item => item.PeriodId == entry.PeriodId)
                ?? throw new InvalidOperationException($"Ledger period '{entry.PeriodId:D}' was not found.");
            entry = AccountingPostingCommandValidator.NormalizeAndValidate(entry);
            LedgerPeriodPostingGuard.Validate(entry, period);
            Appended.Add(entry);
            _records.Add(new LedgerJournalEntryRecord(
                entry.Entry,
                entry.AggregateId,
                entry.PeriodId,
                entry.CommandId,
                entry.CorrelationId,
                _records.Count + 1,
                DateTimeOffset.UtcNow,
                entry.AccountingBasis,
                entry.AccountingPolicyId,
                entry.AccountingPolicyVersion,
                entry.RuleId,
                entry.RuleVersion,
                entry.SourceEventId,
                entry.SourceJournalEntryId,
                entry.PostingKind,
                entry.AdjustmentApproval));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _records.Where(record => record.PeriodId == periodId).ToArray());
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _records.Where(record => record.AggregateId == aggregateId).ToArray());
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(
                _periods.SingleOrDefault(period => period.PeriodId == periodId));
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var periods = _periods
                .Where(period => !ledgerBookId.HasValue || period.LedgerBookId == ledgerBookId.Value)
                .Where(period => string.IsNullOrWhiteSpace(status) || string.Equals(period.Status, status, StringComparison.OrdinalIgnoreCase))
                .Where(period =>
                {
                    if (string.IsNullOrWhiteSpace(fundProfileId) && !fundStructureNodeId.HasValue)
                    {
                        return true;
                    }

                    var book = period.LedgerBookId.HasValue
                        ? _books.SingleOrDefault(item => item.LedgerBookId == period.LedgerBookId.Value)
                        : null;
                    return book is not null &&
                           (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                           (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value);
                })
                .ToArray();
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(periods);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _periods.RemoveAll(item => item.PeriodId == period.PeriodId);
            _periods.Add(period);
            return Task.FromResult(period);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(
                _books.SingleOrDefault(book => book.LedgerBookId == ledgerBookId));
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var books = _books
                .Where(book => string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
                .Where(book => !fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value)
                .Where(book => !fundStructureNodeKind.HasValue || book.FundStructureNodeKind == fundStructureNodeKind.Value)
                .ToArray();
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>(books);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _books.RemoveAll(item => item.LedgerBookId == book.LedgerBookId);
            _books.Add(book);
            return Task.FromResult(book);
        }
    }

    private sealed class PostedPrivateCapitalEndpointLedgerJournalStore(
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        params LedgerJournalEntryRecord[] records) : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            throw new NotSupportedException("Test store is read-only.");

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                periodId == period.PeriodId ? records : []);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                records.Where(record => record.AggregateId == aggregateId).ToArray());
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(periodId == period.PeriodId ? period : null);
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches = (!ledgerBookId.HasValue || period.LedgerBookId == ledgerBookId.Value) &&
                          (string.IsNullOrWhiteSpace(status) || string.Equals(period.Status, status, StringComparison.OrdinalIgnoreCase)) &&
                          (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                          (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value);
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(matches ? [period] : []);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(period);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(ledgerBookId == book.LedgerBookId ? book : null);
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches = (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                          (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value) &&
                          (!fundStructureNodeKind.HasValue || book.FundStructureNodeKind == fundStructureNodeKind.Value);
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>(matches ? [book] : []);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(book);
        }
    }
}
