using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MonthlyAutomationEndpoints_IsolateTenant_RearmWithAudit_AndRejectStaleOrRunningWrites()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var owned = await store.SaveAsync(MonthlySchedule("owned") with
        {
            TenantId = "tenant-test",
            CompanyId = "tenant-test"
        });
        await store.SaveAsync(MonthlySchedule("other") with
        {
            TenantId = "tenant-other",
            CompanyId = "tenant-other"
        });
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<IAutomatedJournalScheduleStore>(store);
                services.AddSingleton<IManualJournalEntryDraftStore>(draftStore);
                services.AddSingleton<TimeProvider>(TimeProvider.System);
            },
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        var list = await client.GetFromJsonAsync<AutomatedJournalScheduleWorkItem[]>(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            ServerJsonOptions);
        var rearmResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            owned,
            ServerJsonOptions);
        var rearmed = await rearmResponse.Content.ReadFromJsonAsync<AutomatedJournalScheduleWorkItem>(ServerJsonOptions);
        var staleResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            owned,
            ServerJsonOptions);
        var running = await store.SaveAsync(rearmed! with { State = AutomatedJournalScheduleStateDto.Running });
        var runningResponse = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            running,
            ServerJsonOptions);

        list.Should().ContainSingle().Which.ScheduleId.Should().Be("owned");
        rearmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        rearmed!.RunHistory.Should().ContainSingle(history =>
            history.HistoryKind == AutomatedJournalScheduleHistoryKind.Rearm &&
            history.Actor == "ops-user" &&
            history.PreviousVersion == owned.Version &&
            history.ResultVersion == rearmed.Version);
        staleResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var staleProblem = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync()))
        {
            var problem = staleProblem.RootElement;
            problem.GetProperty("type").GetString().Should().Be(ApiProblemTypes.VersionConflict);
            problem.GetProperty("resourceId").GetString().Should().Be("owned");
            problem.GetProperty("expectedVersion").GetString().Should().Be(
                owned.Version.ToString(CultureInfo.InvariantCulture));
            problem.GetProperty("currentVersion").GetString().Should().Be(
                rearmed!.Version.ToString(CultureInfo.InvariantCulture));
        }

        runningResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "tenant-test")]
    public async Task AutomationScheduleMutations_RequireTenantAndCompany_WhenRolloutGateIsDisabled(
        string? companyId,
        string? tenantId)
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<IAutomatedJournalScheduleStore>(store);
                services.AddSingleton(FundScopedWriteTenantOptions.Disabled);
            },
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance,
            currentUserCompanyId: companyId,
            currentUserTenantId: tenantId);
        var client = app.GetTestClient();

        var configure = await client.PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlySchedules,
            MonthlySchedule("tenant-scope-required"),
            ServerJsonOptions);
        var runMonthly = await client.PostAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlyRunDue,
            content: null);
        var runDaily = await client.PostAsync(
            UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketRunDue,
            content: null);

        configure.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        runMonthly.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        runDaily.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await store.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task MonthlyRunDueEndpoint_ResolvesWorkerAndExecutesOneShot()
    {
        var store = new InMemoryAutomatedJournalScheduleStore();
        var configurationStore = new InMemoryAccountingConfigurationStore();
        var auditStore = new InMemoryAccountingActionAuditStore();
        var configuration = new AccountingConfigurationService(configurationStore, auditStore);
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(draftStore, configuration, auditStore);
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configuration);
        var runner = new AutomatedJournalIntakeRunner(intake, new FeeScheduleAccrualEventProducer());
        var worker = new AutomatedJournalScheduledWorker(
            store,
            runner,
            NullLogger<AutomatedJournalScheduledWorker>.Instance);
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<IAutomatedJournalScheduleStore>(store);
                services.AddSingleton(worker);
                services.AddSingleton<TimeProvider>(TimeProvider.System);
            },
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);

        var response = await app.GetTestClient().PostAsync(
            UiApiRoutes.LedgerJournalAutomationMonthlyRunDue,
            content: null);
        var result = await response.Content.ReadFromJsonAsync<AutomatedJournalScheduledBatchResult>(ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task DailyMarkToMarketBatchLifecycle_RequiresLedgerCertificationPermission()
    {
        await using var app = await CreateAppAsync(
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.ManageDirectLending);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationDailyMarkToMarketBatchLifecycle,
            new DailyValuationBatchLifecycleRequestDto(
                "daily-mtm-alpha",
                "fund-alpha",
                "browser-user",
                "Attempt to release retained valuation batch.",
                ["evidence://daily-mtm/batch/daily-mtm-alpha"]),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CapitalCallIssuanceIntake_RequiresLedgerMutationPermission()
    {
        await using var app = await CreateAppAsync(
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.ModifySecurityMaster);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationCapitalCallIssuanceIntake,
            CapitalCallIssuanceRequest(),
            CapitalCallRequestJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CapitalCallIssuanceIntake_LandsGovernedDraftsInApprovalQueue()
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(
            "fund-alpha",
            LedgerBookId: null,
            AccountingConfigurationStatusDto.Draft,
            "test",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts:
            [
                new ChartOfAccountsNodeDto(
                    NodeId: "Assets:Capital Call Receivable",
                    Path: "Assets:Capital Call Receivable",
                    AccountName: "Capital Call Receivable",
                    AccountType: "Asset"),
                new ChartOfAccountsNodeDto(
                    NodeId: "Equity:Investor Capital",
                    Path: "Equity:Investor Capital",
                    AccountName: "Investor Capital",
                    AccountType: "Equity")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [],
            TenantId: "tenant-test",
            CompanyId: "tenant-test"));
        var configuration = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configuration,
            new InMemoryAccountingActionAuditStore());
        var runner = new AutomatedJournalIntakeRunner(
            new AutomatedJournalDraftIntakeService(workbench, draftStore, configuration),
            new FeeScheduleAccrualEventProducer(),
            manualJournalWorkbench: workbench);
        await using var app = await CreateAppAsync(
            services => services.AddSingleton(runner),
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationCapitalCallIssuanceIntake,
            CapitalCallIssuanceRequest(),
            CapitalCallRequestJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("readiness").GetString().Should().Be("Ready");
        var created = payload.RootElement.GetProperty("intake").GetProperty("created");
        created.GetArrayLength().Should().Be(2);

        var drafts = await draftStore.ListAsync("fund-alpha", tenantId: "tenant-test", companyId: "tenant-test");
        drafts.Should().HaveCount(2);
        drafts.Should().OnlyContain(
            draft => draft.Status == ManualJournalEntryStatusDto.Draft,
            "capital-call issuance must land in the approval queue, never post");
        // Actor is server-resolved from the session, not taken from the request body.
        drafts.Should().OnlyContain(draft => draft.PreparedBy == "ops-user");
    }

    [Fact]
    public async Task CapitalCallFundingIntake_RequiresLedgerMutationPermission()
    {
        await using var app = await CreateAppAsync(
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.ModifySecurityMaster);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationCapitalCallFundingIntake,
            CapitalCallFundingRequest(),
            CapitalCallRequestJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CapitalCallFundingIntake_LandsGovernedDraftsInApprovalQueue()
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(CapitalCallFundingTestData.Workspace(
            "fund-alpha",
            tenantId: "tenant-test",
            companyId: "tenant-test"));
        var configuration = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configuration,
            new InMemoryAccountingActionAuditStore());
        // The fixture ledger holds a posted 600k/400k issuance for call-1, layered onto the
        // workbench projection so the funding run can corroborate the open receivable.
        var callEffectiveDate = new DateOnly(2026, 3, 15);
        var activitySource = new PostedActivityManualJournalWorkbench(
            workbench,
            [
                CapitalCallFundingTestData.PostedIssuanceEvent(
                    "fund-alpha", "call-1", "cmt-1", "lp-1", 600_000m, callEffectiveDate, DateTimeOffset.UtcNow),
                CapitalCallFundingTestData.PostedIssuanceEvent(
                    "fund-alpha", "call-1", "cmt-2", "lp-2", 400_000m, callEffectiveDate, DateTimeOffset.UtcNow)
            ],
            [
                CapitalCallFundingTestData.PostedIssuanceImpact(
                    "fund-alpha", "call-1", "cmt-1", "lp-1", 600_000m, callEffectiveDate),
                CapitalCallFundingTestData.PostedIssuanceImpact(
                    "fund-alpha", "call-1", "cmt-2", "lp-2", 400_000m, callEffectiveDate)
            ]);
        var runner = new AutomatedJournalIntakeRunner(
            new AutomatedJournalDraftIntakeService(workbench, draftStore, configuration),
            new FeeScheduleAccrualEventProducer(),
            manualJournalWorkbench: activitySource);
        await using var app = await CreateAppAsync(
            services => services.AddSingleton(runner),
            mapLedgerApi: true,
            currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerJournalAutomationCapitalCallFundingIntake,
            CapitalCallFundingRequest(),
            CapitalCallRequestJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("readiness").GetString().Should().Be("Ready");
        var created = payload.RootElement.GetProperty("intake").GetProperty("created");
        created.GetArrayLength().Should().Be(2);

        var drafts = await draftStore.ListAsync("fund-alpha", tenantId: "tenant-test", companyId: "tenant-test");
        drafts.Should().HaveCount(2);
        drafts.Should().OnlyContain(
            draft => draft.Status == ManualJournalEntryStatusDto.Draft,
            "capital-call funding must land in the approval queue, never post");
        // Actor is server-resolved from the session, not taken from the request body.
        drafts.Should().OnlyContain(draft => draft.PreparedBy == "ops-user");
    }

    // Serializes enums as numbers: the server binds request bodies with default web JSON
    // options, which carry no string-enum converter for the Meridian.Ledger enums.
    private static readonly JsonSerializerOptions CapitalCallRequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static RunCapitalCallFundingDraftIntakeRequest CapitalCallFundingRequest()
        => new(
            FundProfileId: "fund-alpha",
            // Without a ledger book the workbench correctly grades the draft NeedsFix
            // (manual-je.book-missing); the endpoint contract expects the caller to name the book.
            LedgerBookId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Currency: "USD",
            Actor: "client-supplied-actor",
            CallId: "call-1",
            ReceivedDate: new DateOnly(2026, 3, 20),
            Fundings:
            [
                new CapitalCallFundingInput(
                    "cmt-1",
                    CapitalAccountId: "ca-lp-1",
                    InvestorId: "lp-1",
                    FundedAmount: 600_000m,
                    EvidenceLinks: ["evidence://funding/cmt-1/remittance"]),
                new CapitalCallFundingInput(
                    "cmt-2",
                    CapitalAccountId: "ca-lp-2",
                    InvestorId: "lp-2",
                    FundedAmount: 400_000m,
                    EvidenceLinks: ["evidence://funding/cmt-2/remittance"])
            ],
            PeriodId: "2026-03",
            EntityId: "entity-alpha",
            AsOf: new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));

    private static RunCapitalCallIssuanceDraftIntakeRequest CapitalCallIssuanceRequest()
        => new(
            FundProfileId: "fund-alpha",
            // Without a ledger book the workbench correctly grades the draft NeedsFix
            // (manual-je.book-missing); the endpoint contract expects the caller to name the book.
            LedgerBookId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Currency: "USD",
            Actor: "client-supplied-actor",
            CallId: "call-1",
            AmountToCall: 1_000_000m,
            NoticeDate: new DateOnly(2026, 3, 15),
            DueDate: new DateOnly(2026, 3, 29),
            Commitments:
            [
                new CapitalCallCommitmentInput(
                    "cmt-1",
                    CapitalAccountId: "ca-lp-1",
                    InvestorId: "lp-1",
                    TotalCommitment: 6_000_000m,
                    CommitmentDate: new DateOnly(2025, 1, 1),
                    EvidenceLinks: ["evidence://commitments/cmt-1/subscription-agreement"]),
                new CapitalCallCommitmentInput(
                    "cmt-2",
                    CapitalAccountId: "ca-lp-2",
                    InvestorId: "lp-2",
                    TotalCommitment: 4_000_000m,
                    CommitmentDate: new DateOnly(2025, 1, 1),
                    EvidenceLinks: ["evidence://commitments/cmt-2/subscription-agreement"])
            ],
            PeriodId: "2026-03",
            EntityId: "entity-alpha",
            AsOf: new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

    private static AutomatedJournalScheduleWorkItem MonthlySchedule(string scheduleId)
        => new(
            ScheduleId: scheduleId,
            Kind: AutomatedJournalScheduleKind.DividendCapture,
            FundProfileId: "fund-alpha",
            LedgerBookId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            PeriodId: "2026-07",
            EntityId: "entity-alpha",
            Currency: "USD",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            DueDate: new DateOnly(2026, 8, 1),
            DueTimeLocal: new TimeOnly(9, 0),
            TimeZoneId: "UTC",
            Actor: "client-supplied-actor",
            Positions: [new DividendAccrualPosition("AAPL", 100m)]);
}
