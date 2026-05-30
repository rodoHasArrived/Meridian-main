using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Storage.SecurityMaster;
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

public sealed class SecurityMasterExceptionCaseworkServiceTests
{
    [Fact]
    public async Task SeedOpenConflictCasesAsync_CreatesDurableCaseWithAuditMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var service = BuildService(repository);
            var conflict = MakeConflict();

            await service.SeedOpenConflictCasesAsync([conflict], "data-steward");

            var item = await repository.GetByIdAsync($"security-master:conflict:{conflict.ConflictId:N}");
            item.Should().NotBeNull();
            item!.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
            item.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Open);
            item.Category.Should().Be(ReconciliationBreakCategory.ClassificationGap);
            item.AssignedTo.Should().Be("data-steward");
            item.RequiredSignoffRole.Should().Be("Security Master steward");
            item.SignoffStatus.Should().Be("pending-signoff");
            item.ExceptionRoute.Should().Be("security-master/conflicts");
            item.RoutingTarget.Should().Be("/api/security-master/conflicts");
            item.RoutingDetail.Should().Be(conflict.ConflictId.ToString("D"));
            item.UpstreamSyncCursor.Should().Be($"security-master-conflict:{conflict.ConflictId:N}:Open");
            item.ExplainabilitySummary.Should().Contain("providerA=alpaca");
            item.ExplainabilitySummary.Should().Contain("providerB=polygon");

            var audit = await repository.GetAuditHistoryAsync(item.BreakId);
            audit.Should().ContainSingle(entry =>
                entry.EventType == "CaseCreated" &&
                entry.BreakId == item.BreakId &&
                entry.RequiredSignoffRole == "Security Master steward");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ApplyResolvedConflictAsync_TransitionsDurableCaseThroughReviewAndResolutionWithoutSignOff()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var service = BuildService(repository);
            var conflict = MakeConflict();
            await service.SeedOpenConflictCasesAsync([conflict], "data-steward");

            var resolved = conflict with { Status = "Resolved" };
            await service.ApplyResolvedConflictAsync(
                resolved,
                new ResolveConflictRequest(conflict.ConflictId, "AcceptA", "approver", "CUSIP confirmed against custodian feed."));

            var item = await repository.GetByIdAsync($"security-master:conflict:{conflict.ConflictId:N}");
            item.Should().NotBeNull();
            item!.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
            item.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);
            item.ResolvedBy.Should().Be("approver");
            item.SignedOffBy.Should().BeNull();
            item.SignoffStatus.Should().Be("ready-for-signoff");
            item.SignoffHistory.Should().ContainSingle(record =>
                record.Actor == "approver" &&
                record.Role == "Security Master steward" &&
                record.Decision == ReconciliationBreakQueueStatus.Resolved.ToString());

            var auditTypes = (await repository.GetAuditHistoryAsync(item.BreakId))
                .Select(entry => entry.EventType)
                .ToArray();
            auditTypes.Should().Contain("CaseCreated");
            auditTypes.Should().Contain("ReviewStarted");
            auditTypes.Should().Contain("Resolved");
            auditTypes.Should().NotContain("SignedOff");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SeedOperatorOverrideCaseAsync_CreatesGovernedOverrideCase()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var service = BuildService(repository);
            var securityId = Guid.NewGuid();
            var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
            var overrides = new OperatorOverridesDto(
                securityId,
                new Dictionary<string, string> { ["sector"] = "Technology" },
                "analyst",
                updatedAt)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode = "CLASSIFICATION_CORRECTION",
                AuditTrail =
                [
                    new SecurityOverrideAuditEntryDto(
                        "Patched",
                        "analyst",
                        updatedAt,
                        SecurityOverrideApprovalStatusDto.Pending,
                        "CLASSIFICATION_CORRECTION",
                        "Sector override requires review.")
                ]
            };

            await service.SeedOperatorOverrideCaseAsync(overrides, "analyst");

            var item = await repository.GetByIdAsync($"security-master:override:{securityId:N}");
            item.Should().NotBeNull();
            item!.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
            item.Category.Should().Be(ReconciliationBreakCategory.ClassificationGap);
            item.AssignedTo.Should().Be("analyst");
            item.RequiredSignoffRole.Should().Be("Security Master steward");
            item.SignoffStatus.Should().Be("pending-signoff");
            item.ExceptionRoute.Should().Be("security-master/operator-overrides");
            item.RoutingTarget.Should().Be($"/api/security-master/{securityId:D}/operator-overrides");
            item.UpstreamSyncCursor.Should().Contain("Pending");
            item.ExplainabilitySummary.Should().Contain("overrideCount=1");
            item.RecommendedAction.Should().Contain("Approve");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SeedOperatorOverrideCaseAsync_ApprovedOverrideResolvesExistingCaseworkWithoutSignOff()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var service = BuildService(repository);
            var securityId = Guid.NewGuid();
            var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            var pending = new OperatorOverridesDto(
                securityId,
                new Dictionary<string, string> { ["sector"] = "Technology" },
                "analyst",
                createdAt)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode = "CLASSIFICATION_CORRECTION"
            };
            await service.SeedOperatorOverrideCaseAsync(pending, "analyst");

            var reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var approved = pending with
            {
                UpdatedAt = reviewedAt,
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Approved,
                ReviewedBy = "security-steward",
                ReviewedAt = reviewedAt
            };

            await service.SeedOperatorOverrideCaseAsync(approved, "analyst");

            var item = await repository.GetByIdAsync($"security-master:override:{securityId:N}");
            item.Should().NotBeNull();
            item!.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
            item.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);
            item.ResolvedBy.Should().Be("security-steward");
            item.SignedOffBy.Should().BeNull();
            item.SignoffStatus.Should().Be("ready-for-signoff");
            item.SignoffHistory.Should().Contain(record =>
                record.Actor == "security-steward" &&
                record.Role == "Security Master steward" &&
                record.Decision == ReconciliationBreakQueueStatus.Resolved.ToString());

            var auditTypes = (await repository.GetAuditHistoryAsync(item.BreakId))
                .Select(static entry => entry.EventType)
                .ToArray();
            auditTypes.Should().Contain("ReviewStarted");
            auditTypes.Should().Contain("Resolved");
            auditTypes.Should().NotContain("SignedOff");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SeedOperatorOverrideCaseAsync_ApprovedOverrideWithoutReviewerFallsBackToRequesterAndWaitsForSignOff()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var service = BuildService(repository);
            var securityId = Guid.NewGuid();
            var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            var pending = new OperatorOverridesDto(
                securityId,
                new Dictionary<string, string> { ["sector"] = "Technology" },
                "analyst",
                createdAt)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode = "CLASSIFICATION_CORRECTION"
            };
            await service.SeedOperatorOverrideCaseAsync(pending, "analyst");

            var approved = pending with
            {
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Approved,
                ReviewedBy = null,
                ReviewedAt = null
            };

            await service.SeedOperatorOverrideCaseAsync(approved, "analyst");

            var item = await repository.GetByIdAsync($"security-master:override:{securityId:N}");
            item.Should().NotBeNull();
            item!.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
            item.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Resolved);
            item.ResolvedBy.Should().Be("analyst");
            item.SignedOffBy.Should().BeNull();
            item.SignoffStatus.Should().Be("ready-for-signoff");
            item.SignoffHistory.Should().Contain(record =>
                record.Actor == "analyst" &&
                record.Decision == ReconciliationBreakQueueStatus.Resolved.ToString());
            item.SignoffHistory.Should().NotContain(record =>
                string.Equals(record.Actor, "security-master-casework", StringComparison.OrdinalIgnoreCase));

            var auditTypes = (await repository.GetAuditHistoryAsync(item.BreakId))
                .Select(static entry => entry.EventType)
                .ToArray();
            auditTypes.Should().Contain("Resolved");
            auditTypes.Should().NotContain("SignedOff");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_GetSecurityMasterConflicts_SeedsDurableCasework()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var conflict = MakeConflict();
            await using var app = await CreateEndpointAppAsync(
                repository,
                conflictService: new StubSecurityMasterConflictService([conflict]));

            var response = await app.GetTestClient().GetAsync(UiApiRoutes.SecurityMasterConflicts);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var item = await repository.GetByIdAsync($"security-master:conflict:{conflict.ConflictId:N}");
            item.Should().NotBeNull();
            item!.AssignedTo.Should().Be("ops-user");
            item.ExceptionRoute.Should().Be("security-master/conflicts");
            item.RoutingDetail.Should().Be(conflict.ConflictId.ToString("D"));
            item.SignoffStatus.Should().Be("pending-signoff");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_PatchOperatorOverrides_SeedsGovernedOverrideCasework()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = BuildRepository(root);
            var overridesStore = new StubOperatorOverridesStore();
            var securityId = Guid.NewGuid();
            await using var app = await CreateEndpointAppAsync(repository, overridesStore: overridesStore);

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
            var item = await repository.GetByIdAsync($"security-master:override:{securityId:N}");
            item.Should().NotBeNull();
            item!.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
            item.AssignedTo.Should().Be("ops-user");
            item.ExceptionRoute.Should().Be("security-master/operator-overrides");
            item.RoutingTarget.Should().Be($"/api/security-master/{securityId:D}/operator-overrides");
            item.ExplainabilitySummary.Should().Contain("approvalStatus=Pending");
            item.UpstreamSyncCursor.Should().Contain("Pending");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static SecurityMasterExceptionCaseworkService BuildService(IReconciliationBreakQueueRepository repository)
        => new(repository, NullLogger<SecurityMasterExceptionCaseworkService>.Instance);

    private static FileReconciliationBreakQueueRepository BuildRepository(string root)
        => new(root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);

    private static async Task<WebApplication> CreateEndpointAppAsync(
        IReconciliationBreakQueueRepository repository,
        ISecurityMasterConflictService? conflictService = null,
        IOperatorOverridesStore? overridesStore = null)
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
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<SecurityMasterExceptionCaseworkService>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "ops-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                UserPermission.ViewSecurityMaster | UserPermission.ModifySecurityMaster;
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
        private readonly Dictionary<Guid, SecurityMasterConflict> _conflicts = conflicts.ToDictionary(static item => item.ConflictId);

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
                    : "Resolved"
            };
            _conflicts[request.ConflictId] = updated;
            return Task.FromResult<SecurityMasterConflict?>(updated);
        }

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
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
            CancellationToken ct = default)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_overrides.TryGetValue(securityId, out var existing))
            {
                foreach (var pair in existing.Values)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            if (request.RemoveKeys is not null)
            {
                foreach (var key in request.RemoveKeys)
                {
                    values.Remove(key);
                }
            }

            if (request.SetValues is not null)
            {
                foreach (var pair in request.SetValues)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            var updated = new OperatorOverridesDto(securityId, values, updatedBy, DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode = request.ReasonCode,
                AuditTrail =
                [
                    new SecurityOverrideAuditEntryDto(
                        "Patched",
                        updatedBy,
                        DateTimeOffset.UtcNow,
                        SecurityOverrideApprovalStatusDto.Pending,
                        request.ReasonCode,
                        "Operator override requires steward review.")
                ]
            };
            _overrides[securityId] = updated;
            return Task.FromResult(updated);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-security-master-casework-tests", Guid.NewGuid().ToString("N"));
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
