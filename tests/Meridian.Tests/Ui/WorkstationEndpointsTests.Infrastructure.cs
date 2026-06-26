using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Application.Monitoring;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.ProviderSdk;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Storage;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    // Must match the JsonSerializerOptions used in CreateAppAsync so that enum fields
    // serialized as strings by the server (via JsonStringEnumConverter) can be round-tripped.
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection>? configureServices = null,
        bool mapExecutionApi = false,
        bool mapPromotionApi = false,
        UserPermission currentUserPermissions = UserPermission.ModifySecurityMaster,
        UserRole? currentUserRole = null,
        string? currentUserRoleProfileName = null,
        string? currentUserCompanyId = "tenant-test",
        string currentUserName = "ops-user",
        bool? mapLedgerApi = null,
        [System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);
        builder.Services.TryAddSingleton<IReconciliationBreakQueueRepository>(_ =>
            new FileReconciliationBreakQueueRepository(
                Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                NullLogger<FileReconciliationBreakQueueRepository>.Instance));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var actor = context.Request.Headers.TryGetValue("X-Meridian-Test-User", out var testActor) &&
                !string.IsNullOrWhiteSpace(testActor.ToString())
                    ? testActor.ToString().Trim()
                    : currentUserName;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = currentUserPermissions;
            if (currentUserRole.HasValue)
            {
                context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = currentUserRole.Value;
            }

            if (!string.IsNullOrWhiteSpace(currentUserRoleProfileName))
            {
                context.Items[LoginSessionMiddleware.CurrentUserRoleProfileNameKey] = currentUserRoleProfileName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(currentUserCompanyId))
            {
                var companyId = currentUserCompanyId.Trim();
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = companyId;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = companyId;
            }

            await next();
        });

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        if (mapExecutionApi)
        {
            app.MapExecutionEndpoints(jsonOptions);
        }

        if (mapPromotionApi)
        {
            app.MapPromotionEndpoints(jsonOptions);
        }

        app.MapWorkstationEndpoints(jsonOptions);
        if (mapLedgerApi ?? IsLedgerEndpointTestFile(callerFilePath))
        {
            app.MapLedgerEndpoints(jsonOptions);
        }

        await app.StartAsync();
        return app;
    }

    private static bool IsLedgerEndpointTestFile(string callerFilePath)
    {
        var fileName = Path.GetFileName(callerFilePath);
        return string.Equals(fileName, "WorkstationEndpointsTests.Wave4.cs", StringComparison.Ordinal) ||
            string.Equals(fileName, "WorkstationEndpointsTests.AccountingConfiguration.cs", StringComparison.Ordinal);
    }

    private static void RegisterRunReadServices(IServiceCollection services)
    {
        var store = new StrategyRunStore();
        services.AddSingleton<IStrategyRepository>(store);
        services.AddSingleton<PortfolioReadService>();
        services.AddSingleton<LedgerReadService>();
        services.AddSingleton<StrategyRunReadService>();
        services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        services.AddSingleton<IReconciliationBreakQueueRepository>(_ =>
            new FileReconciliationBreakQueueRepository(
                Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                NullLogger<FileReconciliationBreakQueueRepository>.Instance));
        services.AddSingleton<ReconciliationProjectionService>();
        services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        services.AddSingleton<CashFlowProjectionService>();
        services.AddSingleton<StrategyRunContinuityService>();
        services.AddSingleton<StrategyRunReviewPacketService>();
        services.AddSingleton<WorkstationWorkflowSummaryService>();
    }

    private static void RegisterOperationsContinuityServices(IServiceCollection services)
    {
        services.AddSingleton<IOperationsStatusDerivationService, OperationsStatusDerivationService>();
        services.AddSingleton<IOperationsContinuityRepository, InMemoryOperationsContinuityRepository>();
        services.AddSingleton<IOperationsWorkflowAuditStore, InMemoryOperationsWorkflowAuditStore>();
        services.AddSingleton<ILedgerJournalStore, RecordingLedgerJournalStore>();
        services.AddSingleton<IOperationsContinuityWorkflowService, OperationsContinuityWorkflowService>();
        services.AddSingleton<IOperationsApprovalPolicyMatrixService, OperationsApprovalPolicyMatrixService>();
        services.AddSingleton<IOperationsCloseCalendarService, OperationsCloseCalendarService>();
        services.AddSingleton<IAccountingCloseManagementService, AccountingCloseManagementService>();
        services.AddSingleton<IAccountingReportPackageService, AccountingReportPackageService>();
        services.AddSingleton<IOperationsContinuityReconciliationBridge>(sp =>
            new OperationsContinuityReconciliationBridge(
                sp.GetRequiredService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IReconciliationRunService>()));
    }

    private static void RegisterDurableOperationsContinuityServices(IServiceCollection services, string dataRoot)
    {
        services.AddSingleton(new StorageOptions { RootPath = dataRoot });
        services.AddSingleton<IOperationsStatusDerivationService, OperationsStatusDerivationService>();
        services.AddSingleton<IOperationsContinuityRepository>(sp =>
            new FileOperationsContinuityRepository(
                dataRoot,
                sp.GetRequiredService<IOperationsStatusDerivationService>(),
                NullLogger<FileOperationsContinuityRepository>.Instance));
        services.AddSingleton<IOperationsWorkflowAuditStore>(_ =>
            new FileOperationsWorkflowAuditStore(
                dataRoot,
                NullLogger<FileOperationsWorkflowAuditStore>.Instance));
        services.AddSingleton<ILedgerJournalStore, RecordingLedgerJournalStore>();
        services.AddSingleton<IOperationsContinuityWorkflowService, OperationsContinuityWorkflowService>();
        services.AddSingleton<IOperationsApprovalPolicyMatrixService, OperationsApprovalPolicyMatrixService>();
        services.AddSingleton<IOperationsCloseCalendarService, OperationsCloseCalendarService>();
        services.AddSingleton<IAccountingCloseManagementService, AccountingCloseManagementService>();
        services.AddSingleton<IAccountingReportPackageService, AccountingReportPackageService>();
        services.AddSingleton<IOperationsContinuityReconciliationBridge>(sp =>
            new OperationsContinuityReconciliationBridge(
                sp.GetRequiredService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IReconciliationRunService>()));
    }

    private static void RegisterConfigStores(IServiceCollection services, string configPath)
    {
        services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
        services.AddSingleton(new Meridian.Ui.Shared.Services.ConfigStore(configPath));
    }

    private static OperationsLedgerJournalCandidateDto CreateOperationsLedgerJournalCandidate(Guid? aggregateId = null)
    {
        var securityId = Guid.Parse("2C0F364F-6020-4675-A7E2-27448950C5AF");
        var idempotencyKey = $"{securityId:N}:operations-continuity:20260531:AccrueInterestIncome:test-source-hash";
        return new OperationsLedgerJournalCandidateDto(
            JournalEntryId: null,
            AggregateId: aggregateId ?? Guid.NewGuid(),
            PeriodId: Guid.NewGuid(),
            Timestamp: DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            Description: "Operations continuity endpoint posting",
            Lines:
            [
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Cash",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 100m,
                    Credit: 0m),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Interest income",
                    AccountType: nameof(LedgerAccountType.Revenue),
                    Debit: 0m,
                    Credit: 100m)
            ],
            CommandId: Guid.NewGuid(),
            SourceEventId: Guid.NewGuid(),
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            RuleId: "operations-continuity-endpoint",
            RuleVersion: "v1",
            PostingKind: LedgerPostingKindDto.Originating,
            Metadata: new OperationsJournalEntryMetadataDto(
                ActivityType: "operations-continuity",
                Symbol: "OPS",
                SecurityId: securityId),
            IdempotencyKey: idempotencyKey,
            SecurityMasterProvenance: $"security-master:{securityId:N};snapshot:test-source-hash");
    }

    private static void RegisterPromotionServices(IServiceCollection services, string promotionRoot)
    {
        services.AddSingleton<BacktestToLivePromoter>();
        services.AddSingleton<IPromotionRecordStore>(_ => new JsonlPromotionRecordStore(
            promotionRoot,
            NullLogger<JsonlPromotionRecordStore>.Instance));
        services.AddSingleton<PromotionService>(sp => new PromotionService(
            sp.GetRequiredService<IStrategyRepository>(),
            sp.GetRequiredService<BacktestToLivePromoter>(),
            sp.GetRequiredService<IPromotionRecordStore>(),
            NullLogger<PromotionService>.Instance));
    }

    private static BrokeragePortfolioSyncService CreateFailedBrokerageSyncService(string root)
        => new(
            new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "alpaca"),
            catalogs: [],
            portfolioAdapters: [new ThrowingPortfolioAdapter("alpaca", "Alpaca credentials are missing.")],
            activityAdapters: [new ThrowingActivityAdapter("alpaca", "Alpaca credentials are missing.")],
            services: new ServiceCollection().BuildServiceProvider(),
            logger: NullLogger<BrokeragePortfolioSyncService>.Instance);

    private sealed class ThrowingPortfolioAdapter(string providerId, string message) : IBrokeragePortfolioSync
    {
        public string ProviderId { get; } = providerId;

        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ThrowingActivityAdapter(string providerId, string message) : IBrokerageActivitySync
    {
        public string ProviderId { get; } = providerId;

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(message);
        }
    }

    private static string NormalizePathForAssertion(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/');
}
