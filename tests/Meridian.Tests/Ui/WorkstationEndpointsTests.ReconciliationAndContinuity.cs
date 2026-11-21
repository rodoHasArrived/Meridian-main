using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_WithGovernanceServices_ShouldExposeGovernanceWorkspacePayload()
    {
        // ViewReporting alongside the default so the reporting assertions below stay about deployment
        // readiness. The workspace withholds the reporting projection from a caller the reporting
        // routes would refuse, and a withheld projection would satisfy "profileCount is 0" for the
        // wrong reason -- authorization rather than an unavailable deployment.
        //
        // ViewStrategies for the same reason: the run cards and the cash-flow balances mirror the run
        // routes, which admit only the strategy permissions, and this test is about what the payload
        // reports for runs that exist rather than about who may see them.
        await using var app = await CreateAppAsync(services =>
        {
            var lookup = new StubSecurityReferenceLookup();
            lookup.Register("AAPL", new WorkstationSecurityReference(
                SecurityId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DisplayName: "Apple Inc.",
                AssetClass: "Equity",
                Currency: "USD",
                Status: SecurityStatusDto.Active,
                PrimaryIdentifier: "AAPL"));

            services.AddSingleton<IStrategyRepository>(new StrategyRunStore());
            services.AddSingleton<ISecurityReferenceLookup>(lookup);
            services.AddSingleton<PortfolioReadService>();
            services.AddSingleton<LedgerReadService>();
            services.AddSingleton<StrategyRunReadService>();
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        },
        currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewReporting | UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-governance-balanced"));
        await store.RecordRunAsync(BuildReconciliationMismatchRun("run-governance-breaks"));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var balanced = await reconciliationService.RunAsync(
            new ReconciliationRunRequest("run-governance-balanced"));
        var mismatch = await reconciliationService.RunAsync(
            new ReconciliationRunRequest("run-governance-breaks"));
        balanced.Should().NotBeNull();
        mismatch.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, "run-governance-balanced", balanced!);
        await RetainScopedReconciliationBreaksAsync(app, "run-governance-breaks", mismatch!);

        var client = app.GetTestClient();
        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");

        var workspace = governance.RootElement.GetProperty("workspace");
        workspace.GetProperty("totalRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("ledgerReadyRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("reconciledRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("openBreaks").GetInt32().Should().BeGreaterThan(0);
        workspace.GetProperty("securityIssues").GetInt32().Should().BeGreaterThan(0);

        var cashFlow = governance.RootElement.GetProperty("cashFlow");
        cashFlow.GetProperty("runsWithCashSignals").GetInt32().Should().Be(2);
        cashFlow.GetProperty("runsWithCashVariance").GetInt32().Should().Be(1);
        cashFlow.GetProperty("netVariance").GetDecimal().Should().Be(-100m);

        var reporting = governance.RootElement.GetProperty("reporting");
        reporting.GetProperty("profileCount").GetInt32().Should().Be(0,
            "embedded governance payloads must not synthesize reporting capability when the durable reporting deployment is unavailable");
        reporting.GetProperty("profiles").GetArrayLength().Should().Be(0);
        reporting.GetProperty("recommendedProfiles").GetArrayLength().Should().Be(0);
        reporting.GetProperty("deploymentCapability")
            .GetProperty("isReady")
            .GetBoolean()
            .Should().BeFalse();
        var controlCenter = governance.RootElement.GetProperty("controlCenter");
        controlCenter.GetProperty("closeReadiness").GetString().Should().NotBeNullOrWhiteSpace();
        controlCenter.GetProperty("blockerSeverityDistribution").GetArrayLength().Should().BeGreaterThan(0);
        controlCenter.GetProperty("agingCurves").GetArrayLength().Should().Be(3);
        controlCenter.GetProperty("ownerWorkload").GetArrayLength().Should().BeGreaterThan(0);
        controlCenter.GetProperty("slaBreachCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        controlCenter.GetProperty("trendSnapshots").GetArrayLength().Should().BeGreaterThan(0);
        controlCenter.GetProperty("drillLinks").EnumerateArray()
            .Select(link => link.GetProperty("href").GetString())
            .Should()
            .Contain(new[] { "/trading/readiness", "/accounting/reconciliation", "/reporting/report-packs", "/reporting/evidence" });
        controlCenter.GetProperty("alerts").GetArrayLength().Should().BeGreaterThan(0);

        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "open-breaks" &&
                               metric.GetProperty("value").GetString() != "0");

        var queue = governance.RootElement.GetProperty("reconciliationQueue");
        queue.GetArrayLength().Should().Be(2);

        var breakRun = queue.EnumerateArray()
            .Single(item => item.GetProperty("runId").GetString() == "run-governance-breaks");
        breakRun.GetProperty("reconciliationStatus").GetString().Should().Be("BreaksOpen");
        breakRun.GetProperty("openBreakCount").GetInt32().Should().BeGreaterThan(0);
        breakRun.GetProperty("securityCoverage").GetProperty("hasIssues").GetBoolean().Should().BeTrue();
        breakRun.GetProperty("securityCoverage").GetProperty("missingReferences").GetArrayLength().Should().BeGreaterThan(0);
        breakRun.GetProperty("cashFlow").GetProperty("cashVariance").GetDecimal().Should().Be(-100m);
        breakRun.GetProperty("latestReconciliation").GetProperty("hasSecurityCoverageIssues").GetBoolean().Should().BeTrue();
        breakRun.GetProperty("latestReconciliation").GetProperty("securityIssueCount").GetInt32().Should().BeGreaterThan(0);

        var balancedRun = queue.EnumerateArray()
            .Single(item => item.GetProperty("runId").GetString() == "run-governance-balanced");
        balancedRun.GetProperty("reconciliationStatus").GetString().Should().Be("SecurityCoverageOpen");
        balancedRun.GetProperty("breakCount").GetInt32().Should().Be(0);
        balancedRun.GetProperty("cashFlow").GetProperty("cashVariance").GetDecimal().Should().Be(0m);
        balancedRun.GetProperty("latestReconciliation").GetProperty("hasSecurityCoverageIssues").GetBoolean().Should().BeTrue();
        balancedRun.GetProperty("securityCoverage").GetProperty("missingReferences").EnumerateArray()
            .Should()
            .Contain(item => item.GetProperty("symbol").GetString() == "TSLA");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationStatementRoutes_ShouldUseCanonicalSharedRoutes()
    {
        var service = new StubReconciliationApiService();
        var accountId = Guid.NewGuid();
        var accounts = CreateStatementAccount(accountId);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
            services.AddSingleton<Meridian.PortfolioRecords.Accounts.IAccountQueryService>(accounts);
        }, currentUserPermissions: UserPermission.AdminMaintenance);

        var client = app.GetTestClient();

        var runs = await client.GetFromJsonAsync<List<StatementRunSummaryDto>>(
            UiApiRoutes.ReconciliationStatementRuns,
            ServerJsonOptions);
        runs.Should().ContainSingle(run => run.RunId == "statement-run-1");

        var run = await client.GetFromJsonAsync<StatementRunDto>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunById, "runId", "statement-run-1"),
            ServerJsonOptions);
        run.Should().NotBeNull();
        run!.MatchSummary.Should().NotBeNull();
        run.MatchSummary!.BreakCount.Should().Be(1);
        run.Breaks.Should().ContainSingle(statementBreak =>
            statementBreak.BreakId == "break-1"
            && statementBreak.Status == "Open"
            && statementBreak.Severity == StatementValidationSeverity.Error);
        run!.RunId.Should().Be("statement-run-1");

        var validation = await client.GetFromJsonAsync<StatementRunValidationDto>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunValidation, "runId", "statement-run-1"),
            ServerJsonOptions);
        validation.Should().NotBeNull();
        validation!.IsBlocked.Should().BeFalse();

        var breaks = await client.GetFromJsonAsync<List<StatementRunBreakDto>>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunBreaks, "runId", "statement-run-1"),
            ServerJsonOptions);
        breaks.Should().ContainSingle(item => item.RunId == "statement-run-1");

        var reconcileResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunReconcile, "runId", "statement-run-1"),
            new StatementRunReconcileRequestDto(Actor: "ops-user"),
            ServerJsonOptions);
        reconcileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconciled = await reconcileResponse.Content.ReadFromJsonAsync<StatementRunDto>(ServerJsonOptions);
        reconciled.Should().NotBeNull();
        reconciled!.RunId.Should().Be("statement-run-1");

        var createdResponse = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "samplebroker",
                SourceInstitution: "Sample Custodian",
                FundAccountId: accountId.ToString("D"),
                ExternalAccountId: "external-allowed",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 27),
                SourcePath: "/tmp/statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "ops-user",
                SourceFileHash: "ABC123"),
            ServerJsonOptions);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        service.CreatedRequests.Should().ContainSingle(request => request.FundAccountId == accountId.ToString("D"));

        var exceptions = await client.GetFromJsonAsync<List<StatementRunExceptionDto>>(
            UiApiRoutes.ReconciliationStatementExceptions,
            ServerJsonOptions);
        exceptions.Should().ContainSingle(item => item.RunId == "statement-run-1");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunMutations_ShouldRequireReconciliationPermission()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var client = app.GetTestClient();

        var createResponse = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "samplebroker",
                SourceInstitution: "Sample Custodian",
                FundAccountId: "fund-1",
                ExternalAccountId: "ext-1",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 27),
                SourcePath: "/tmp/statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "ops-user",
                SourceFileHash: "ABC123"),
            ServerJsonOptions);

        var reconcileResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunReconcile, "runId", "statement-run-1"),
            new StatementRunReconcileRequestDto(Actor: "ops-user"),
            ServerJsonOptions);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reconcileResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.CreatedRequests.Should().BeEmpty();
        service.ReconciledRunIds.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldCreateAndFetchRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-recon"));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest("run-recon"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.Summary.RunId.Should().Be("run-recon");
        created.Summary.BreakCount.Should().Be(0);
        created.Matches.Should().Contain(match => match.CheckId == "cash-balance");

        var latestResponse = await client.GetAsync(UiApiRoutes.RunsReconciliation.Replace("{runId}", "run-recon"));
        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await latestResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be(created.Summary.ReconciliationRunId);

        var byIdResponse = await client.GetAsync(
            UiApiRoutes.ReconciliationRunById.Replace(
                "{reconciliationRunId}",
                created.Summary.ReconciliationRunId));
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var byId = await byIdResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        byId.Should().NotBeNull();
        byId!.Summary.MatchCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRunById_ShouldRequireReconciliationMutationPermission()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var repository = app.Services.GetRequiredService<IReconciliationRunRepository>();
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-permission",
            runId: "run-recon-permission",
            createdAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            matchCount: 1,
            breakCount: 0));

        var client = app.GetTestClient();
        var byIdResponse = await client.GetAsync(
            UiApiRoutes.ReconciliationRunById.Replace(
                "{reconciliationRunId}",
                "recon-permission"));
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationCreateRun_ShouldRequireMutationPermission()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-recon-create-permission"));

        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationRuns,
            new ReconciliationRunRequest("run-recon-create-permission"));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldReturnNotFoundWhenNoRunExists()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var client = app.GetTestClient();

        var createResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest("missing-run"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var latestResponse = await client.GetAsync(UiApiRoutes.RunsReconciliation.Replace("{runId}", "missing-run"));
        latestResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationHistoryRoute_ShouldReturnDescendingHistory()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var repository = app.Services.GetRequiredService<IReconciliationRunRepository>();
        var strategyRuns = app.Services.GetRequiredService<IStrategyRepository>();
        await strategyRuns.RecordRunAsync(BuildReconciliationReadyRun("run-history"));
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-1",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            matchCount: 1,
            breakCount: 0));
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-2",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            matchCount: 2,
            breakCount: 1));
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-3",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero),
            matchCount: 3,
            breakCount: 0));

        var client = app.GetTestClient();
        var historyResponse = await client.GetAsync(UiApiRoutes.RunsReconciliationHistory.Replace("{runId}", "run-history"));
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await historyResponse.Content.ReadFromJsonAsync<List<ReconciliationRunSummary>>();
        history.Should().NotBeNull();
        history!.Should().HaveCount(3);
        history.Select(item => item.ReconciliationRunId).Should().ContainInOrder("recon-2", "recon-3", "recon-1");
        history[0].RunId.Should().Be("run-history");
        history[0].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero));
        history[0].BreakCount.Should().Be(1);
        history[1].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero));
        history[2].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationHistoryRoute_ShouldHandleEmptyHistory()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-no-history"));

        var client = app.GetTestClient();
        var response = await client.GetAsync(UiApiRoutes.RunsReconciliationHistory.Replace("{runId}", "run-no-history"));

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await response.Content.ReadFromJsonAsync<List<ReconciliationRunSummary>>();
        history.Should().NotBeNull();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldReturnBreaksForMismatchedRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun("run-breaks"));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest("run-breaks"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.Summary.BreakCount.Should().BeGreaterThan(0);
        created.Breaks.Should().NotBeEmpty();
        created.Breaks.Should().Contain(breakRow =>
            breakRow.Category == ReconciliationBreakCategory.AmountMismatch ||
            breakRow.Category == ReconciliationBreakCategory.MissingLedgerCoverage);
        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        (await repository.GetAllAsync(TestReconciliationQueueScope))
            .Should()
            .BeEmpty("generic reconciliation runs are comparison-only and do not own tenant/accounting scope");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueRoute_ShouldReadAuthoritativelyRetainedScopedCasework()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-queue-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest(runId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconciliation = await createResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var response = await client.GetAsync("/api/workstation/reconciliation/break-queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = await response.Content.ReadFromJsonAsync<List<ReconciliationBreakQueueItem>>(ServerJsonOptions);
        queue.Should().NotBeNull();
        queue!.Should().Contain(item =>
            item.RunId == runId &&
            reconciliation!.Breaks.Any(reconciliationBreak => item.BreakId == $"{runId}:{reconciliationBreak.CheckId}"));
        queue.Should().Contain(item =>
            item.RunId == runId &&
            !string.IsNullOrWhiteSpace(item.ExceptionRoute) &&
            !string.IsNullOrWhiteSpace(item.ToleranceProfileId) &&
            !string.IsNullOrWhiteSpace(item.RequiredSignoffRole) &&
            !string.IsNullOrWhiteSpace(item.SignoffStatus) &&
            item.SourceType == "provider-ledger" &&
            item.SourceFingerprint != null);
        queue.Should().Contain(item =>
            item.RunId == runId &&
            item.ExceptionRoute == "accounting-variance-escalation");
        queue.Should().NotContain(item =>
            string.Equals(item.ExceptionRoute, "governance-variance-escalation", StringComparison.OrdinalIgnoreCase));
        var seededBreaks = queue.Where(item => item.RunId == runId).ToArray();
        seededBreaks.Should().OnlyContain(item =>
            item.Measures != null && item.Measures.Count == 3 &&
            item.BlockedOutputs != null && item.BlockedOutputs.Count > 0);
        foreach (var measure in seededBreaks
                     .SelectMany(item => item.Measures!)
                     .Where(measure => measure.Expected.HasValue && measure.Actual.HasValue))
        {
            measure.Variance.Should().Be(measure.Actual!.Value - measure.Expected!.Value);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueRead_ShouldNotSeedStatementBreaks()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        });

        var queue = await app.GetTestClient().GetFromJsonAsync<List<ReconciliationBreakQueueItem>>(
            UiApiRoutes.ReconciliationBreakQueue,
            ServerJsonOptions);

        queue.Should().NotBeNull();
        queue.Should().BeEmpty();
        service.ListOpenStatementBreaksCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueUnavailable_ShouldFailClosedInsteadOfReportingHealthyEmpty()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationBreakQueueRepository>(_ => null!);
        });

        var client = app.GetTestClient();
        using var queue = await client.GetAsync(UiApiRoutes.ReconciliationBreakQueue);
        using var calibration = await client.GetAsync(UiApiRoutes.ReconciliationCalibrationSummary);
        using var accounting = await client.GetAsync(UiApiRoutes.WorkstationAccounting);

        queue.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        calibration.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        accounting.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_AccountingPayload_WithLedgerBookId_ShouldScopeBreakQueueAndOpenBreakMetrics()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var ledgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var otherLedgerBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-ledger-book-scope",
            strategyId: "scope-1",
            strategyName: "Ledger Book Scope",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero),
            fundProfileId: "fund-test"));

        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("scope-selected", ledgerBookId));
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("scope-other", otherLedgerBookId));
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("scope-unscoped", ledgerBookId: null));

        var client = app.GetTestClient();
        using var breakQueueResponse = await client.GetAsync($"{UiApiRoutes.ReconciliationBreakQueue}?ledgerBookId={ledgerBookId:D}");
        using var accountingResponse = await client.GetAsync($"{UiApiRoutes.WorkstationAccounting}?ledgerBookId={ledgerBookId:D}");

        breakQueueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        accountingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var breakQueue = await breakQueueResponse.Content.ReadFromJsonAsync<List<ReconciliationBreakQueueItem>>(ServerJsonOptions);
        breakQueue.Should().ContainSingle(item => item.BreakId == "scope-selected" && item.LedgerBookId == ledgerBookId);
        breakQueue.Should().NotContain(item =>
            string.Equals(item.BreakId, "scope-other", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.BreakId, "scope-unscoped", StringComparison.OrdinalIgnoreCase));

        using var accountingJson = await JsonDocument.ParseAsync(await accountingResponse.Content.ReadAsStreamAsync());
        var root = accountingJson.RootElement;
        var accountingBreakQueue = root.GetProperty("breakQueue").EnumerateArray().ToArray();
        root.GetProperty("breakQueue").EnumerateArray()
            .Should()
            .ContainSingle(item =>
                item.GetProperty("breakId").GetString() == "scope-selected" &&
                item.GetProperty("ledgerBookId").GetGuid() == ledgerBookId);
        accountingBreakQueue
            .Should()
            .NotContain(item =>
                string.Equals(item.GetProperty("breakId").GetString(), "scope-other", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.GetProperty("breakId").GetString(), "scope-unscoped", StringComparison.OrdinalIgnoreCase));
        root.GetProperty("metrics").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "open-breaks")
            .GetProperty("value")
            .GetString()
            .Should()
            .Be("1");
        root.GetProperty("workspace").GetProperty("openBreaks").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_AccountingPayload_WithNoStrategyRuns_ShouldExposeScopedStatementCasework()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("statement-only-case", Guid.NewGuid()) with
            {
                RunId = "statement-intake-run",
                StrategyName = "Statement reconciliation report",
                SourceType = "statement-reconciliation",
                SourceSystem = "statement-intake",
                SourceReference = "statement-run-1",
                SourceFingerprint = "statement-fingerprint"
            });

        using var response = await app.GetTestClient().GetAsync(UiApiRoutes.WorkstationAccounting);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;

        root.GetProperty("reconciliationQueue").GetArrayLength().Should().Be(0);
        root.GetProperty("breakQueue").EnumerateArray()
            .Should()
            .ContainSingle(item => item.GetProperty("breakId").GetString() == "statement-only-case");
        root.GetProperty("metrics").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "open-breaks")
            .GetProperty("value")
            .GetString()
            .Should()
            .Be("1");
        root.GetProperty("workspace").GetProperty("openBreaks").GetInt32().Should().Be(1);
        root.GetProperty("controlCenter").GetProperty("ownerWorkload").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_AccountingPayload_ShouldProjectOnlyRunsOwnedByAuthenticatedTenantAndCompany()
    {
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync("fund-owned", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-owned", "tenant-test", "tenant-test"));
        tenancy.ResolveAsync("fund-foreign", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-foreign", "tenant-foreign", "company-foreign"));
        tenancy.ResolveAsync("fund-unbound", Arg.Any<CancellationToken>())
            .Returns((FundProfileOwnership?)null);

        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterRunReadServices(services);
                services.AddSingleton(tenancy);
            },
            // The run cards mirror the run routes, which admit only the strategy permissions. This
            // test is about which tenant's runs are projected, so the caller is given the authority
            // to see run cards at all -- withholding them would satisfy the assertions below for the
            // wrong reason. ModifySecurityMaster is what opens the workspace.
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            "run-owned",
            "owned",
            "Owned",
            RunType.Paper,
            new DateTimeOffset(2026, 6, 20, 13, 0, 0, TimeSpan.Zero),
            fundProfileId: "fund-owned"));
        await store.RecordRunAsync(BuildRun(
            "run-foreign",
            "foreign",
            "Foreign",
            RunType.Paper,
            new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero),
            fundProfileId: "fund-foreign"));
        await store.RecordRunAsync(BuildRun(
            "run-unbound",
            "unbound",
            "Unbound",
            RunType.Paper,
            new DateTimeOffset(2026, 6, 20, 11, 0, 0, TimeSpan.Zero),
            fundProfileId: "fund-unbound"));
        await store.RecordRunAsync(BuildRun(
            "run-no-fund",
            "no-fund",
            "No Fund",
            RunType.Paper,
            new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero)));

        using var response = await app.GetTestClient().GetAsync(UiApiRoutes.WorkstationAccounting);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        var root = payload.RootElement;
        root.GetProperty("workspace").GetProperty("totalRuns").GetInt32().Should().Be(1);
        root.GetProperty("reconciliationQueue").EnumerateArray()
            .Should()
            .ContainSingle(item => item.GetProperty("runId").GetString() == "run-owned");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementReconcile_ShouldNotPublishCaseworkOutsideAuthoritativeIntake()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        });

        var client = app.GetTestClient();
        var reconcile = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunReconcile, "runId", "statement-run-1"),
            new StatementRunReconcileRequestDto(Actor: "browser-spoof", Reason: "publish statement breaks"));
        reconcile.StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await client.GetFromJsonAsync<List<ReconciliationBreakQueueItem>>(
            UiApiRoutes.ReconciliationBreakQueue,
            ServerJsonOptions);
        var second = await client.GetFromJsonAsync<List<ReconciliationBreakQueueItem>>(
            UiApiRoutes.ReconciliationBreakQueue,
            ServerJsonOptions);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        service.ListOpenStatementBreaksCallCount.Should().Be(0);
        first.Should().BeEmpty();
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationCalibrationSummary_ShouldAggregateToleranceProfiles()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-calibration-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest(runId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconciliation = await createResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var summary = await client
            .GetFromJsonAsync<ReconciliationCalibrationSummaryDto>(
                UiApiRoutes.ReconciliationCalibrationSummary,
                ServerJsonOptions);

        summary.Should().NotBeNull();
        summary!.TotalBreakCount.Should().BeGreaterThan(0);
        summary.ActiveBreakCount.Should().BeGreaterThan(0);
        summary.PendingSignoffCount.Should().BeGreaterThan(0);
        summary.MissingCalibrationMetadataCount.Should().Be(0);
        summary.Status.Should().BeOneOf(
            ReconciliationCalibrationStatusDto.ReviewRequired,
            ReconciliationCalibrationStatusDto.Blocked);
        summary.Profiles.Should().Contain(profile =>
            !string.IsNullOrWhiteSpace(profile.ToleranceProfileId) &&
            !string.IsNullOrWhiteSpace(profile.ExceptionRoute) &&
            profile.TotalBreakCount > 0 &&
            profile.PendingSignoffCount > 0);
        summary.Summary.Should().Contain("reconciliation");
        summary.BreakCountTrend.Should().BeGreaterThanOrEqualTo(0);
        summary.AutoMatchRate.Should().BeInRange(0m, 1m);
        summary.T0ClosureRate.Should().BeInRange(0m, 1m);
        summary.BreakCountAlertThreshold.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationCalibrationSummary_AfterResolution_ShouldMarkReadyForSignoff()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-calibration-resolved-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest(runId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconciliation = await createResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var queue = await client.GetFromJsonAsync<List<ReconciliationBreakQueueItem>>(
            UiApiRoutes.ReconciliationBreakQueue,
            ServerJsonOptions);
        queue.Should().NotBeNull();
        queue.Should().NotBeEmpty();

        foreach (var item in queue!)
        {
            if (item.Status == ReconciliationBreakQueueStatus.Open)
            {
                var review = await client.PostAsJsonAsync(
                    UiApiRoutes.ReconciliationBreakReview.Replace("{breakId}", item.BreakId, StringComparison.Ordinal),
                    new ReviewReconciliationBreakRequest(
                        BreakId: item.BreakId,
                        AssignedTo: "ops-review",
                        ReviewedBy: "qa-review",
                        ReviewNote: "Calibration review completed."));
                review.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            var resolve = await client.PostAsJsonAsync(
                UiApiRoutes.ReconciliationBreakResolve.Replace("{breakId}", item.BreakId, StringComparison.Ordinal),
                new ResolveReconciliationBreakRequest(
                    BreakId: item.BreakId,
                    Status: ReconciliationBreakQueueStatus.Resolved,
                    ResolvedBy: "qa-resolve",
                    ResolutionNote: "Tolerance profile accepted for sign-off.",
                    OperatorRationale: "Tolerance and routing evidence verified."));
            resolve.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var summary = await client.GetFromJsonAsync<ReconciliationCalibrationSummaryDto>(
            UiApiRoutes.ReconciliationCalibrationSummary,
            ServerJsonOptions);

        summary.Should().NotBeNull();
        summary!.Status.Should().Be(ReconciliationCalibrationStatusDto.Ready);
        summary.ActiveBreakCount.Should().Be(0);
        summary.OpenBreakCount.Should().Be(0);
        summary.InReviewBreakCount.Should().Be(0);
        summary.PendingSignoffCount.Should().Be(0);
        summary.CriticalOpenBreakCount.Should().Be(0);
        summary.SignedOffCount.Should().Be(summary.TotalBreakCount);
        summary.Profiles.Should().OnlyContain(profile => profile.PendingSignoffCount == 0);
        summary.Summary.Should().Contain("ready for accounting sign-off");
        summary.BreakCountTrend.Should().BeLessThanOrEqualTo(0);
        summary.T0ClosureRate.Should().Be(1m);
        summary.T0ClosureRateAlertTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueReviewRoute_ShouldHydrateQueueWithoutListBootstrap()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-review-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating the mismatch."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var operation = await response.Content.ReadFromJsonAsync<ReconciliationCaseworkOperationResult>(ServerJsonOptions);
        operation.Should().NotBeNull();
        operation!.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        operation.Item!.RunId.Should().Be(runId);
        operation.Item.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);
        // The server rewrites the client-supplied assignee ("ops-review") and reviewer ("qa-review")
        // from the authenticated session, exactly like ResolvedBy on the resolve route.
        operation.Item.AssignedTo.Should().Be("ops-user");
        operation.Item.ReviewedBy.Should().Be("ops-user");
        operation.Item.SignoffStatus.Should().Be("in-review");
        operation.Item.RequiredSignoffRole.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueReviewRoute_ShouldRewriteSpoofedAssigneeFromSession()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-review-spoof-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops.gov",
                ReviewedBy: "ops.gov",
                ReviewNote: "Browser sends a hardcoded placeholder identity."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var operation = await response.Content.ReadFromJsonAsync<ReconciliationCaseworkOperationResult>(ServerJsonOptions);
        operation!.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        // Neither the assignee nor the reviewer may pass through verbatim: the persisted identity is
        // the authenticated session user, so the assignee filter can answer "my breaks" and the
        // repository's AssignedTo ?? ReviewedBy ?? ResolvedBy audit-actor derivation names a real user.
        operation.Item!.AssignedTo.Should().Be("ops-user");
        operation.Item.ReviewedBy.Should().Be("ops-user");

        var persisted = await client.GetFromJsonAsync<ReconciliationBreakQueueItem>(
            $"/api/workstation/reconciliation/break-queue/{Uri.EscapeDataString(breakId)}",
            ServerJsonOptions);
        persisted!.AssignedTo.Should().Be("ops-user");
        persisted.ReviewedBy.Should().Be("ops-user");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueResolveRoute_ShouldRequireReviewBeforeResolve()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-resolve-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();

        var invalidResolve = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/resolve",
            new ResolveReconciliationBreakRequest(
                BreakId: breakId,
                Status: ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "qa-resolve",
                ResolutionNote: "Skipping review should fail.",
                OperatorRationale: "Attempted without review."));
        invalidResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockedResolve = await invalidResolve.Content.ReadFromJsonAsync<ReconciliationCaseworkOperationResult>(ServerJsonOptions);
        blockedResolve!.Outcome.State.Should().Be(OperationTerminalState.Blocked);

        var review = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating."));
        review.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolve = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/resolve",
            new ResolveReconciliationBreakRequest(
                BreakId: breakId,
                Status: ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "qa-resolve",
                ResolutionNote: "Issue resolved.",
                OperatorRationale: "Evidence reconciled against source records."));
        resolve.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolvedOperation = await resolve.Content.ReadFromJsonAsync<ReconciliationCaseworkOperationResult>(ServerJsonOptions);
        resolvedOperation.Should().NotBeNull();
        resolvedOperation!.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        resolvedOperation.Item!.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
        resolvedOperation.Item.ResolvedBy.Should().Be("ops-user");
        resolvedOperation.Item.SignoffStatus.Should().Be("ready-for-signoff");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueDispositionRoutes_ShouldDeriveExecutorAndRetainIndependentApprovalEvidence()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IStatementReconciliationCaseworkHandoffService>(sp =>
                new StatementReconciliationCaseworkHandoffService(
                    sp.GetRequiredService<IReconciliationBreakQueueRepository>()));
        });
        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        var measures = new[]
        {
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Value, 100m, 90m, -10m, 1m, "USD"),
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Quantity, null, null, null, null, "units", "Quantity was not supplied."),
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.CostBasis, null, null, null, null, "USD", "Cost basis was not supplied.")
        };
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("break-waive-route", null) with
            {
                Measures = measures,
                BlockedOutputs = ["FinalReport", "PeriodClose"]
            });
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("break-supersede-route", null) with
            {
                Measures = measures,
                BlockedOutputs = ["FinalReport"]
            });
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("replacement-break-route", null) with
            {
                Severity = ReconciliationBreakSeverity.Low,
                Measures = measures,
                BlockedOutputs = ["FinalReport"]
            });
        await repository.CreateIfMissingAsync(
            TestReconciliationQueueScope,
            BuildBreakQueueItem("break-nonmaterial-route", null) with
            {
                Severity = ReconciliationBreakSeverity.Low,
                Variance = 0.1m,
                ToleranceBand = 1m,
                Measures = measures,
                BlockedOutputs = ["FinalReport"]
            });
        var waiveCurrent = (await repository.GetByIdAsync(TestReconciliationQueueScope, "break-waive-route"))!;
        var supersedeCurrent = (await repository.GetByIdAsync(TestReconciliationQueueScope, "break-supersede-route"))!;
        var nonMaterialCurrent = (await repository.GetByIdAsync(TestReconciliationQueueScope, "break-nonmaterial-route"))!;
        var client = app.GetTestClient();

        var waiveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationBreakWaive, "breakId", waiveCurrent.BreakId),
            new ReconciliationCaseworkCommand(
                waiveCurrent.BreakId,
                ReconciliationCaseworkAction.Resolve,
                Actor: "browser-spoof",
                CommandId: "waive-route-command",
                CorrelationId: "waive-route-correlation",
                Source: "browser-workstation",
                ExpectedVersion: waiveCurrent.Version,
                Reason: "Approved policy exception.",
                EvidenceLinks: ["evidence:waive-route"],
                Privileged: true,
                ActionOrigin: OperationsActionOriginDto.AssistantDraft,
                ApprovalActor: "controller-reviewer",
                ApprovalReference: "approval:waive-route"));
        waiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var waiveOperation = await waiveResponse.Content.ReadFromJsonAsync<ReconciliationCaseworkOperationResult>(ServerJsonOptions);
        waiveOperation.Should().NotBeNull();
        waiveOperation!.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        waiveOperation.ErrorCode.Should().Be(nameof(ReconciliationBreakQueueTransitionErrorCode.None));
        waiveOperation.Item!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Waived);
        waiveOperation.Item.ResolvedBy.Should().Be("ops-user");
        waiveOperation.Item.DispositionApprovedBy.Should().Be("controller-reviewer");
        waiveOperation.Item.DispositionApprovalReference.Should().Be("approval:waive-route");

        var supersedeResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationBreakSupersede, "breakId", supersedeCurrent.BreakId),
            new ReconciliationCaseworkCommand(
                supersedeCurrent.BreakId,
                ReconciliationCaseworkAction.Resolve,
                Actor: "browser-spoof",
                CommandId: "supersede-route-command",
                CorrelationId: "supersede-route-correlation",
                Source: "browser-workstation",
                ExpectedVersion: supersedeCurrent.Version,
                Reason: "Corrected source created a replacement break.",
                EvidenceLinks: ["evidence:supersede-route"],
                ApprovalActor: "controller-reviewer",
                ApprovalReference: "approval:supersede-route",
                SupersedingBreakId: "replacement-break-route"));
        supersedeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var supersedeOperation = await supersedeResponse.Content.ReadFromJsonAsync<ReconciliationCaseworkOperationResult>(ServerJsonOptions);
        supersedeOperation.Should().NotBeNull();
        supersedeOperation!.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        supersedeOperation.ErrorCode.Should().Be(nameof(ReconciliationBreakQueueTransitionErrorCode.None));
        supersedeOperation.Item!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Superseded);
        supersedeOperation.Item.SupersedingBreakId.Should().Be("replacement-break-route");
        supersedeOperation.Item.DispositionApprovedBy.Should().Be("controller-reviewer");

        var nonMaterialResponse = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationBreakWaive, "breakId", nonMaterialCurrent.BreakId),
            new ReconciliationCaseworkCommand(
                nonMaterialCurrent.BreakId,
                ReconciliationCaseworkAction.Resolve,
                Actor: "browser-spoof",
                CommandId: "waive-nonmaterial-route-command",
                CorrelationId: "waive-nonmaterial-route-correlation",
                Source: "browser-workstation",
                ExpectedVersion: nonMaterialCurrent.Version,
                Reason: "Documented immaterial exception.",
                EvidenceLinks: ["evidence:waive-nonmaterial-route"],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft,
                ApprovalActor: "controller-reviewer",
                ApprovalReference: "approval:waive-nonmaterial-route"));
        nonMaterialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var waivedOperation = await nonMaterialResponse.Content.ReadFromJsonAsync<ReconciliationCaseworkOperationResult>(ServerJsonOptions);
        waivedOperation.Should().NotBeNull();
        waivedOperation!.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        waivedOperation.ErrorCode.Should().Be(nameof(ReconciliationBreakQueueTransitionErrorCode.None));
        waivedOperation.Item!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Waived);
        waivedOperation.Item.ResolvedBy.Should().Be("ops-user");
        waivedOperation.Item.DispositionApprovedBy.Should().Be("controller-reviewer");
        waivedOperation.Item.DispositionApprovalReference.Should().Be("approval:waive-nonmaterial-route");
        var retainedAudit = await repository.GetAuditHistoryAsync(nonMaterialCurrent.BreakId);
        var waiverAudit = retainedAudit.Last(item => item.EventType == "Waived");
        waiverAudit.Actor.Should().Be("ops-user");
        waiverAudit.Source.Should().Be("workstation-reconciliation-casework");
        retainedAudit.Should().Contain(item => item.EventType == "SlaChanged");
        retainedAudit.Should().NotContain(static item => item.Source == "browser-workstation");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunContinuityRoute_ShouldReturnSharedContinuityPayload()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun("run-continuity"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-continuity-paper",
            strategyId: "recon-strategy",
            strategyName: "Reconciliation Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero)) with
        {
            ParentRunId = "run-continuity",
            FundProfileId = "alpha-credit",
            FundDisplayName = "Alpha Credit"
        });

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-continuity"));

        var client = app.GetTestClient();
        using var continuity = await ReadJsonAsync(client, "/api/workstation/runs/run-continuity/continuity");

        continuity.RootElement.GetProperty("run").GetProperty("summary").GetProperty("runId").GetString().Should().Be("run-continuity");
        continuity.RootElement.GetProperty("run").GetProperty("summary").GetProperty("fundProfileId").GetString().Should().Be("alpha-credit");
        continuity.RootElement.GetProperty("lineage").GetProperty("childRuns").GetArrayLength().Should().Be(1);
        continuity.RootElement.GetProperty("lineage").GetProperty("childRuns")[0].GetProperty("runId").GetString().Should().Be("run-continuity-paper");
        continuity.RootElement.GetProperty("cashFlow").GetProperty("totalEntries").GetInt32().Should().Be(3);
        continuity.RootElement.GetProperty("cashFlow").GetProperty("projectedNetPosition").GetDecimal().Should().Be(-376m);
        continuity.RootElement.GetProperty("reconciliation").GetProperty("runId").GetString().Should().Be("run-continuity");
        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasCashFlow").GetBoolean().Should().BeTrue();
        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasReconciliation").GetBoolean().Should().BeTrue();
        continuity.RootElement
            .GetProperty("continuityStatus")
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(static warning => warning.GetProperty("code").GetString())
            .Should()
            .Contain("security-coverage");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ContinuityWarnings_ShouldFlowAcrossResearchTradingGovernanceAndReviewPacket()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies | UserPermission.ViewTrades);

        var runId = $"run-cross-surface-continuity-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun(runId, includeCashFlows: false, includeFills: false, promotionState: StrategyRunPromotionState.CandidateForLive));

        var client = app.GetTestClient();

        var reviewPacket = await client.GetFromJsonAsync<StrategyRunReviewPacketDto>($"/api/workstation/runs/{runId}/review-packet", ServerJsonOptions);
        reviewPacket.Should().NotBeNull();
        reviewPacket!.Continuity.Should().NotBeNull();
        reviewPacket.Continuity!.ContinuityStatus.HasFills.Should().BeFalse();

        var warningCodes = reviewPacket.Continuity.ContinuityStatus.Warnings.Select(static warning => warning.Code).ToArray();
        warningCodes.Should().Contain(["missing-fills", "lineage-promotion-gap"]);

        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);
        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => item.WorkItemId.StartsWith("continuity-missing-fills", StringComparison.Ordinal));
        inbox.Items.Should().Contain(item => item.WorkItemId.StartsWith("continuity-lineage-promotion-gap", StringComparison.Ordinal));

        using var continuity = await ReadJsonAsync(client, $"/api/workstation/runs/{runId}/continuity");
        var continuityWarnings = continuity.RootElement
            .GetProperty("continuityStatus")
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(static warning => warning.GetProperty("code").GetString())
            .ToArray();

        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasFills").GetBoolean().Should().BeFalse();
        continuityWarnings.Should().Contain(["missing-fills", "lineage-promotion-gap"]);
        continuityWarnings.Should().Contain(warningCodes);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunContinuityRoute_ShouldReturnNotFoundForMissingRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/no-such-run/continuity");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ContinuityDrillInsAcrossSurfaces_ShouldUseCanonicalRouteAndSharedSchema()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices, currentUserPermissions: UserPermission.ViewStrategies | UserPermission.ViewTrades);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await store.RecordRunAsync(BuildContinuityRun("run-continuity-shared"));
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-continuity-shared"));

        var client = app.GetTestClient();
        var canonicalContinuityRoute = UiApiRoutes.RunsContinuity.Replace("{runId}", "run-continuity-shared", StringComparison.Ordinal);

        using var research = await ReadJsonAsync(client, UiApiRoutes.WorkstationStrategy);
        using var trading = await ReadJsonAsync(client, UiApiRoutes.WorkstationTrading);
        using var briefing = await ReadJsonAsync(client, UiApiRoutes.WorkstationStrategyBriefing);

        ContainsStringValue(research.RootElement, canonicalContinuityRoute).Should().BeTrue();
        ContainsStringValue(trading.RootElement, canonicalContinuityRoute).Should().BeTrue();
        ContainsStringValue(briefing.RootElement, canonicalContinuityRoute).Should().BeTrue();

        using var continuity = await ReadJsonAsync(client, canonicalContinuityRoute);
        var propertyNames = continuity.RootElement.EnumerateObject().Select(static property => property.Name).ToArray();
        propertyNames.Should().BeEquivalentTo(["run", "lineage", "cashFlow", "reconciliation", "continuityStatus"]);
        continuity.RootElement.TryGetProperty("continuity", out _).Should().BeFalse("surfaces must not emit continuity-only envelope fields outside the shared DTO contract");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunReviewPacket_ShouldReturnStableActionableWorkItems()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var runId = $"run-review-packet-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun(runId) with
        {
            OperatorAcceptanceCriteria = ["Operator approved the review-packet evidence."],
            RetainedEvidenceReferences = [$"evidence://strategy-runs/{runId}"],
            ApprovalReferences = [$"approval://strategy-runs/{runId}"]
        });

        var client = app.GetTestClient();
        var first = await client.GetFromJsonAsync<StrategyRunReviewPacketDto>(
            $"/api/workstation/runs/{runId}/review-packet",
            ServerJsonOptions);
        var second = await client.GetFromJsonAsync<StrategyRunReviewPacketDto>(
            $"/api/workstation/runs/{runId}/review-packet",
            ServerJsonOptions);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Run.EvidenceLoop.Should().NotBeNull();
        first.Run.EvidenceLoop!.OperatorAcceptanceCriteria.Should()
            .ContainSingle("Operator approved the review-packet evidence.");
        first.Run.EvidenceLoop.RetainedEvidenceReferences.Should()
            .ContainSingle($"evidence://strategy-runs/{runId}");
        first.Run.EvidenceLoop.ApprovalReferences.Should()
            .ContainSingle($"approval://strategy-runs/{runId}");
        first.WorkItems.Should().NotBeEmpty();
        first.WorkItems.Select(static item => item.WorkItemId)
            .Should()
            .Equal(second!.WorkItems.Select(static item => item.WorkItemId));
        first.WorkItems.Should().OnlyContain(static item =>
            !string.IsNullOrWhiteSpace(item.Workspace) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute) &&
            !string.IsNullOrWhiteSpace(item.TargetPageTag));
        first.WorkItems.Should().Contain(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.Workspace == "Accounting" &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "FundReconciliation");
        first.WorkItems.Should().NotContain(static item =>
            string.Equals(item.Workspace, "Governance", StringComparison.OrdinalIgnoreCase));
        first.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{runId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview &&
            item.Workspace == "Trading" &&
            item.TargetRoute == UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", runId) &&
            item.TargetPageTag == "TradingShell");
        first.WorkItems.Should().NotContain(static item =>
            item.WorkItemId.StartsWith("operator-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunReviewPacket_ShouldReturnNotFoundForMissingRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var response = await app.GetTestClient().GetAsync("/api/workstation/runs/no-such-run/review-packet");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterRoutes_ShouldReturnSearchAndDetailPayloads()
    {
        var securityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var queryService = new StubSecurityMasterQueryService();
        queryService.Register(
            new SecuritySummaryDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                PrimaryIdentifier: "AAPL",
                Currency: "USD",
                Version: 4),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                Currency: "USD",
                CommonTerms: JsonSerializer.SerializeToElement(new { lotSize = 1 }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { primaryExchange = "NASDAQ" }),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Ticker,
                        "AAPL",
                        true,
                        new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero),
                        null,
                        null)
                ],
                Aliases: [],
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(queryService);
        });

        var client = app.GetTestClient();

        using var search = await ReadJsonAsync(client, "/api/workstation/security-master/securities?query=AAPL&take=5&activeOnly=true");
        var rows = search.RootElement;
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.SecurityId))).GetGuid().Should().Be(securityId);
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Status))).GetString().Should().Be("Active");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Classification))).GetProperty(CamelCase(nameof(SecurityClassificationSummaryDto.AssetClass))).GetString().Should().Be("Equity");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Classification))).GetProperty(CamelCase(nameof(SecurityClassificationSummaryDto.PrimaryIdentifierValue))).GetString().Should().Be("AAPL");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.EffectiveFrom))).ValueKind.Should().Be(JsonValueKind.Null);

        using var detail = await ReadJsonAsync(client, $"/api/workstation/security-master/securities/{securityId}");
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.SecurityId))).GetGuid().Should().Be(securityId);
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.Currency))).GetString().Should().Be("USD");
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.Version))).GetInt64().Should().Be(4);
    }
}
