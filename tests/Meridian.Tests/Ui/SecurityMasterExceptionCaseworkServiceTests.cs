using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the authority boundary between deployment-global Security Master records and
/// tenant/company-owned reconciliation casework.
/// </summary>
public sealed class SecurityMasterExceptionCaseworkServiceTests
{
    private static readonly ReconciliationBreakQueueScope AlphaScope = new("tenant-alpha", "company-alpha");
    private static readonly ReconciliationBreakQueueScope BetaScope = new("tenant-beta", "company-beta");

    [Fact]
    public async Task Endpoint_GetSecurityMasterConflicts_GlobalRead_DoesNotPublishCallerOwnedCasework()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var conflict = MakeConflict();
            await using var app = await CreateEndpointAppAsync(
                repository,
                conflictService: new StubSecurityMasterConflictService([conflict]),
                includeTenantScope: true);

            var response = await app.GetTestClient().GetAsync(UiApiRoutes.SecurityMasterConflicts);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await repository.GetAllAsync()).Should().BeEmpty(
                "reading deployment-global conflicts must not claim them for the caller's tenant");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_ResolveSecurityMasterConflict_GlobalMutation_DoesNotCreateMissingTenantCase()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var conflict = MakeConflict();
            var conflictService = new StubSecurityMasterConflictService([conflict]);
            await using var app = await CreateEndpointAppAsync(
                repository,
                conflictService: conflictService,
                includeTenantScope: true);
            var route = UiApiRoutes.SecurityMasterConflictResolve.Replace(
                "{conflictId:guid}",
                conflict.ConflictId.ToString("D"),
                StringComparison.Ordinal);

            var response = await app.GetTestClient().PostAsJsonAsync(
                route,
                new ResolveConflictRequest(
                    conflict.ConflictId,
                    "AcceptA",
                    "caller-controlled",
                    "CUSIP confirmed against custodian evidence."));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var resolved = await conflictService.GetConflictAsync(conflict.ConflictId, CancellationToken.None);
            resolved.Should().NotBeNull();
            resolved!.Status.Should().Be("Resolved");
            resolved.ResolvedBy.Should().Be("ops-user");
            (await repository.GetAllAsync()).Should().BeEmpty(
                "a global conflict resolution has no canonical tenant/company owner to publish");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_PatchOperatorOverrides_GlobalMutation_DoesNotPublishCallerOwnedCasework()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var overridesStore = new StubOperatorOverridesStore();
            var securityId = Guid.NewGuid();
            await using var app = await CreateEndpointAppAsync(
                repository,
                overridesStore: overridesStore,
                includeTenantScope: true);

            var response = await app.GetTestClient().PatchAsJsonAsync(
                UiApiRoutes.SecurityMasterOperatorOverrides.Replace(
                    "{securityId:guid}",
                    securityId.ToString("D"),
                    StringComparison.Ordinal),
                new OperatorOverridesPatchRequest(
                    new Dictionary<string, string> { ["sector"] = "Technology" },
                    RemoveKeys: null)
                {
                    ReasonCode = "CLASSIFICATION_CORRECTION"
                });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await overridesStore.GetAsync(securityId)).Should().NotBeNull();
            (await repository.GetAllAsync()).Should().BeEmpty(
                "a deployment-global override has no canonical tenant/company owner to publish");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_RunQualityReport_GlobalMutation_DoesNotPublishCallerOwnedCasework()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var qualityService = Substitute.For<ISecurityMasterDataQualityService>();
            var report = new SecurityMasterQualityReportDto(
                DateTimeOffset.UtcNow,
                SecuritiesScanned: 1,
                ViolationCount: 1,
                Violations:
                [
                    new DataQualityRuleViolationDto(
                        "MA001",
                        "Missing Maturity Date",
                        DataQualityRuleCategory.MinimumAttribute,
                        Guid.NewGuid(),
                        "assetSpecificTerms.maturity",
                        "Bond securities require a maturity date.",
                        DataQualityRuleSeverity.Error,
                        DateTimeOffset.UtcNow)
                ]);
            qualityService.RunQualityChecksAsync(Arg.Any<CancellationToken>()).Returns(report);
            await using var app = await CreateEndpointAppAsync(
                repository,
                qualityService: qualityService,
                includeTenantScope: true);

            var response = await app.GetTestClient().PostAsync(
                UiApiRoutes.SecurityMasterQualityReportRun,
                content: null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            await qualityService.Received(1).RunQualityChecksAsync(Arg.Any<CancellationToken>());
            (await repository.GetAllAsync()).Should().BeEmpty(
                "a deployment-global quality report has no canonical tenant/company owner to publish");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetAgingExceptionsAsync_ExistingOwnedCases_ReturnsOnlyExactScope()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root, new SecurityMasterReconciliationSlaPolicyProvider());
            var alpha = BuildOwnedCase(AlphaScope, "security-master-owned-alpha");
            var beta = BuildOwnedCase(BetaScope, "security-master-owned-beta");
            await repository.CreateIfMissingAsync(AlphaScope, alpha);
            await repository.CreateIfMissingAsync(BetaScope, beta);
            var service = BuildService(repository);

            var aging = await service.GetAgingExceptionsAsync(AlphaScope);

            aging.Should().ContainSingle().Which.BreakId.Should().Be(alpha.BreakId);
            aging.Should().NotContain(item => item.BreakId == beta.BreakId);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_GetAgingExceptions_WithoutTenantCompanyScope_FailsClosedWithoutWrites()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            await using var app = await CreateEndpointAppAsync(repository, includeTenantScope: false);

            var response = await app.GetTestClient().GetAsync(UiApiRoutes.SecurityMasterExceptionsAging);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await repository.GetAllAsync()).Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static ReconciliationBreakQueueItem BuildOwnedCase(
        ReconciliationBreakQueueScope scope,
        string breakId)
    {
        var detectedAt = DateTimeOffset.UtcNow.AddDays(-10);
        return new ReconciliationBreakQueueItem(
            BreakId: breakId,
            RunId: "security-master-conflicts",
            StrategyName: "Security Master exception casework",
            Category: ReconciliationBreakCategory.ClassificationGap,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 0m,
            Reason: "Scoped producer retained a Security Master exception.",
            AssignedTo: "security-master-steward",
            DetectedAt: detectedAt,
            LastUpdatedAt: detectedAt,
            Severity: ReconciliationBreakSeverity.High,
            RequiredSignoffRole: "Security Master steward",
            Team: "Security Master",
            StateTransitions: [])
        {
            TenantId = scope.TenantId,
            CompanyId = scope.CompanyId
        };
    }

    private static SecurityMasterExceptionCaseworkService BuildService(
        IReconciliationBreakQueueRepository repository)
        => new(repository, NullLogger<SecurityMasterExceptionCaseworkService>.Instance);

    private static FileReconciliationBreakQueueRepository BuildRepository(
        string root,
        IReconciliationSlaPolicyProvider? slaPolicyProvider = null)
        => new(root, NullLogger<FileReconciliationBreakQueueRepository>.Instance, slaPolicyProvider);

    private static async Task<WebApplication> CreateEndpointAppAsync(
        IReconciliationBreakQueueRepository repository,
        ISecurityMasterConflictService? conflictService = null,
        IOperatorOverridesStore? overridesStore = null,
        ISecurityMasterDataQualityService? qualityService = null,
        bool includeTenantScope = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ContractSecurityMasterQueryService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityValidationService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(conflictService ?? new StubSecurityMasterConflictService([]));
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterIngestStatusService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterImportService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterEventStore>());
        builder.Services.AddSingleton(overridesStore ?? new StubOperatorOverridesStore());
        builder.Services.AddSingleton(qualityService ?? Substitute.For<ISecurityMasterDataQualityService>());
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<SecurityMasterExceptionCaseworkService>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "ops-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                UserPermission.ViewSecurityMaster | UserPermission.ModifySecurityMaster | UserPermission.AdminMaintenance;
            if (includeTenantScope)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = AlphaScope.TenantId;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = AlphaScope.CompanyId;
            }

            await next();
        });
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }

    private static SecurityMasterConflict MakeConflict()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "IdentifierAmbiguity",
            "Identifiers.Cusip",
            "alpaca",
            "security-a",
            "polygon",
            "security-b",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "Open");

    private sealed class StubSecurityMasterConflictService(IReadOnlyList<SecurityMasterConflict> conflicts)
        : ISecurityMasterConflictService
    {
        private readonly Dictionary<Guid, SecurityMasterConflict> _conflicts =
            conflicts.ToDictionary(static item => item.ConflictId);

        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(
                _conflicts.Values
                    .Where(static item => string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase))
                    .ToArray());

        public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
            => Task.FromResult(_conflicts.GetValueOrDefault(conflictId));

        public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
        {
            if (!_conflicts.TryGetValue(request.ConflictId, out var conflict))
            {
                return Task.FromResult<SecurityMasterConflict?>(null);
            }

            var updated = conflict with
            {
                Status = string.Equals(request.Resolution, "Dismiss", StringComparison.OrdinalIgnoreCase)
                    ? "Dismissed"
                    : "Resolved",
                ResolvedBy = request.ResolvedBy,
                ResolvedReason = request.Reason,
                ResolvedAt = DateTimeOffset.UtcNow
            };
            _conflicts[request.ConflictId] = updated;
            return Task.FromResult<SecurityMasterConflict?>(updated);
        }

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
            => Task.CompletedTask;

        public Task RecordFieldConflictsAsync(
            SecurityProjectionRecord previous,
            SecurityProjectionRecord incoming,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task ReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class StubOperatorOverridesStore : IOperatorOverridesStore
    {
        private readonly Dictionary<Guid, OperatorOverridesDto> _overrides = [];

        public Task<OperatorOverridesDto?> GetAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(_overrides.GetValueOrDefault(securityId));

        public Task<OperatorOverridesDto> PatchAsync(
            Guid securityId,
            OperatorOverridesPatchRequest request,
            string updatedBy,
            CancellationToken ct = default,
            long? expectedCanonicalVersion = null)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (request.SetValues is not null)
            {
                foreach (var pair in request.SetValues)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            var updated = new OperatorOverridesDto(
                securityId,
                values,
                updatedBy,
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode = request.ReasonCode
            };
            _overrides[securityId] = updated;
            return Task.FromResult(updated);
        }

        public Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
            Guid securityId,
            OperatorOverrideDecision decision,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-security-master-casework-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
