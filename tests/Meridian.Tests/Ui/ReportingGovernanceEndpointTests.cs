using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Reporting;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ReportingGovernanceEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void MutationContracts_ContainNoCallerControlledAuthorityOrEvidenceFields()
    {
        AssertOnlyProperties<ReportingGovernanceVersionRequestDto>("ExpectedVersion");
        AssertOnlyProperties<ReportingGovernanceApprovalRequestDto>("ExpectedVersion", "DecisionNote");
        AssertOnlyProperties<ReportingGovernanceRestatementRequestDto>("ExpectedVersion", "Reason");
        AssertOnlyProperties<ReportingGovernanceRestatementApprovalRequestDto>("ExpectedVersion");
    }

    [Fact]
    public async Task Get_UsesAuthenticatedScopeAndReturnsImmutableGovernanceProjection()
    {
        var coordinator = new RecordingCoordinator();
        await using var app = await CreateAppAsync(coordinator, UserPermission.ViewReporting);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/reporting/runs/run-001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        coordinator.LastOperation.Should().Be("get");
        coordinator.LastCaller.Should().NotBeNull();
        coordinator.LastCaller!.ActorId.Should().Be("server-operator");
        coordinator.LastCaller.TenantId.Should().Be("tenant-a");
        coordinator.LastCaller.CompanyId.Should().Be("company-a");
        coordinator.LastCaller.Permissions.Should().Be(UserPermission.ViewReporting);
        coordinator.LastCaller.Origin.Should().Be(ReportingCommandOrigin.HumanOperator);
        coordinator.LastCaller.CorrelationId.Should().Be("server-correlation-001");

        var payload = await response.Content.ReadFromJsonAsync<GovernedReportingRunDto>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.RunId.Should().Be("run-001");
        payload.Scope.TenantId.Should().Be("tenant-a");
        payload.Access.PolicyHash.Should().Be(Hash('a'));
        payload.Snapshot.SnapshotHash.Should().Be(Hash('b'));
        payload.Version.Should().Be(7);
    }

    [Fact]
    public async Task Validate_IgnoresInjectedAuthorityAndEvidenceAndUsesOnlyServerCallerContext()
    {
        var coordinator = new RecordingCoordinator();
        await using var app = await CreateAppAsync(coordinator, UserPermission.ManageReporting);
        var content = new StringContent(
            """
            {
              "expectedVersion": 7,
              "actorId": "attacker",
              "tenantId": "tenant-b",
              "companyId": "company-b",
              "organizationId": "org-b",
              "permissions": ["ReleaseRun"],
              "origin": "ReviewedAutomation",
              "snapshotHash": "forged",
              "manifestHash": "forged",
              "artifactPath": "c:/forged.pdf",
              "evidenceIds": ["forged"]
            }
            """,
            Encoding.UTF8,
            "application/json");

        var response = await app.GetTestClient().PostAsync(
            "/api/fund-structure/reporting/runs/run-001/validate",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        coordinator.LastOperation.Should().Be("validate");
        coordinator.LastExpectedVersion.Should().Be(7);
        coordinator.LastCaller!.ActorId.Should().Be("server-operator");
        coordinator.LastCaller.TenantId.Should().Be("tenant-a");
        coordinator.LastCaller.CompanyId.Should().Be("company-a");
        coordinator.LastCaller.Origin.Should().Be(ReportingCommandOrigin.HumanOperator);
        coordinator.LastCaller.Permissions.Should().Be(UserPermission.ManageReporting);
    }

    [Theory]
    [InlineData("/api/fund-structure/reporting/runs/run-001/validate", "{\"expectedVersion\":7}", "validate", HttpStatusCode.OK)]
    [InlineData("/api/fund-structure/reporting/runs/run-001/submit", "{\"expectedVersion\":7}", "submit", HttpStatusCode.OK)]
    [InlineData("/api/fund-structure/reporting/runs/run-001/approve", "{\"expectedVersion\":7,\"decisionNote\":\"Reviewed independently\"}", "approve", HttpStatusCode.OK)]
    [InlineData("/api/fund-structure/reporting/runs/run-001/release", "{\"expectedVersion\":7}", "release", HttpStatusCode.OK)]
    [InlineData("/api/fund-structure/reporting/runs/run-001/restatement-requests", "{\"expectedVersion\":7,\"reason\":\"Correct retained source data\"}", "request-restatement", HttpStatusCode.Created)]
    [InlineData("/api/fund-structure/reporting/runs/restatement-requests/restatement-001/approve", "{\"expectedVersion\":1}", "approve-restatement", HttpStatusCode.OK)]
    public async Task CanonicalMutationRoutes_InvokeOnlyEndpointFacade(
        string path,
        string json,
        string expectedOperation,
        HttpStatusCode expectedStatus)
    {
        var coordinator = new RecordingCoordinator();
        await using var app = await CreateAppAsync(coordinator, UserPermission.AdminMaintenance);

        var response = await app.GetTestClient().PostAsync(
            path,
            new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(expectedStatus);
        coordinator.LastOperation.Should().Be(expectedOperation);
    }

    [Fact]
    public async Task MissingExpectedVersion_ReturnsValidationProblemWithoutCallingCoordinator()
    {
        var coordinator = new RecordingCoordinator();
        await using var app = await CreateAppAsync(coordinator, UserPermission.ManageReporting);

        var response = await app.GetTestClient().PostAsync(
            "/api/fund-structure/reporting/runs/run-001/validate",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        coordinator.LastOperation.Should().BeNull();
        (await response.Content.ReadAsStringAsync()).Should().Contain("expectedVersion");
    }

    [Fact]
    public async Task Approve_WithManageOnlyPermission_Returns403BeforeCoordinator()
    {
        var coordinator = new RecordingCoordinator();
        await using var app = await CreateAppAsync(coordinator, UserPermission.ManageReporting);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/fund-structure/reporting/runs/run-001/approve",
            new ReportingGovernanceApprovalRequestDto(7, "Independent review"),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        coordinator.LastOperation.Should().BeNull();
    }

    [Fact]
    public async Task MissingCompanyScope_Returns403WithoutCallingCoordinator()
    {
        var coordinator = new RecordingCoordinator();
        await using var app = await CreateAppAsync(
            coordinator,
            UserPermission.ViewReporting,
            companyId: null);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/reporting/runs/run-001");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        coordinator.LastOperation.Should().BeNull();
    }

    [Fact]
    public async Task ConcurrencyFailure_Returns409ProblemDetails()
    {
        var coordinator = new RecordingCoordinator
        {
            ExceptionToThrow = new ReportingGovernanceConcurrencyException("stale reporting version")
        };
        await using var app = await CreateAppAsync(coordinator, UserPermission.ManageReporting);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/fund-structure/reporting/runs/run-001/submit",
            new ReportingGovernanceVersionRequestDto(6),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("stale reporting version");
    }

    [Fact]
    public async Task MissingCoordinator_Returns503()
    {
        await using var app = await CreateAppAsync(
            coordinator: null,
            permissions: UserPermission.ViewReporting);

        var response = await app.GetTestClient().GetAsync("/api/fund-structure/reporting/runs/run-001");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private static void AssertOnlyProperties<T>(params string[] expected)
    {
        typeof(T).GetProperties()
            .Select(static property => property.Name)
            .Should()
            .BeEquivalentTo(expected);
    }

    private static async Task<WebApplication> CreateAppAsync(
        IReportingGovernanceEndpointCoordinator? coordinator,
        UserPermission permissions,
        string? tenantId = "tenant-a",
        string? companyId = "company-a")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        if (coordinator is not null)
        {
            builder.Services.AddSingleton(coordinator);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.TraceIdentifier = "server-correlation-001";
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "server-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.ReportingAnalyst;
            if (tenantId is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenantId;
            }

            if (companyId is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = companyId;
            }

            await next();
        });
        app.MapReportingGovernanceEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static GovernedReportingRun SeedRun(string runId = "run-001")
    {
        var authority = SeedAuthority();
        return new GovernedReportingRun(
            runId,
            "series-001",
            Revision: 1,
            "investor-statement",
            "3",
            new ReportingOperationalScope(
                "tenant-a",
                "tenant-a",
                "company-a",
                "fund-a",
                "book-a",
                "2026-06"),
            new ReportingAccessScope(
                "policy-a",
                "4",
                ReportingGovernanceAccessMode.Restricted,
                "server-operator",
                ["server-operator", "ReportingAnalyst"],
                Hash('a')),
            new ReportingCertifiedSnapshotScope(
                "tenant-a",
                "tenant-a",
                "company-a",
                "fund-a",
                "book-a",
                "2026-06",
                "snapshot-001",
                Hash('b'),
                "reconciliation-001",
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),
            authority,
            new DateTimeOffset(2026, 7, 1, 0, 1, 0, TimeSpan.Zero),
            RestatementOfRunId: null,
            GovernedReportingExecutionState.Succeeded,
            GovernedReportingState.Draft,
            Version: 7,
            Readiness: null,
            Approval: null,
            Release: null,
            AuditTrail: []);
    }

    private static ReportingAuthorityScope SeedAuthority() =>
        new(
            "server-operator",
            "tenant-a",
            "tenant-a",
            "company-a",
            [ReportingGovernancePermission.CreateRun],
            ReportingCommandOrigin.HumanOperator,
            "create-correlation",
            ["ReportingAnalyst"]);

    private static ReportingRestatementRequest SeedRestatementRequest() =>
        new(
            "restatement-001",
            "run-001",
            "series-001",
            PredecessorRevision: 1,
            PredecessorVersion: 7,
            "Correct retained source data",
            [new ReportingRestatementChangedLine("nav", "100", "101", ["evidence-001"])],
            SeedAuthority(),
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero),
            ReportingRestatementRequestState.PendingApproval,
            Version: 1,
            ApprovedBy: null,
            ApprovedAtUtc: null,
            DraftRunId: null,
            AuditTrail: []);

    private static string Hash(char value) => new(value, 64);

    private sealed class RecordingCoordinator : IReportingGovernanceEndpointCoordinator
    {
        public string? LastOperation { get; private set; }
        public long? LastExpectedVersion { get; private set; }
        public ReportingGovernanceCallerContext? LastCaller { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task<GovernedReportingRun> GetAsync(
            string runId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run("get", null, caller, SeedRun(runId));

        public Task<IReadOnlyList<GovernedReportingRun>> ListAsync(
            string seriesId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run<IReadOnlyList<GovernedReportingRun>>("list", null, caller, [SeedRun()]);

        public Task<GovernedReportingRun> CreateFromCompletedCertifiedManifestAsync(
            string manifestRunId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run("create", null, caller, SeedRun(manifestRunId));

        public Task<GovernedReportingRun> ValidateAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run("validate", expectedVersion, caller, SeedRun(runId));

        public Task<GovernedReportingRun> SubmitAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run("submit", expectedVersion, caller, SeedRun(runId));

        public Task<GovernedReportingRun> ApproveAsync(
            string runId,
            long expectedVersion,
            string decisionNote,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run("approve", expectedVersion, caller, SeedRun(runId));

        public Task<GovernedReportingRun> ReleaseAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run("release", expectedVersion, caller, SeedRun(runId));

        public Task<ReportingRestatementRequest> RequestRestatementAsync(
            string predecessorRunId,
            long expectedPredecessorVersion,
            string reason,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run("request-restatement", expectedPredecessorVersion, caller, SeedRestatementRequest());

        public Task<ReportingRestatementApprovalResult> ApproveRestatementAsync(
            string requestId,
            long expectedRequestVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Run(
                "approve-restatement",
                expectedRequestVersion,
                caller,
                new ReportingRestatementApprovalResult(
                    SeedRestatementRequest() with
                    {
                        State = ReportingRestatementRequestState.Approved,
                        Version = 2,
                        DraftRunId = "run-002"
                    },
                    SeedRun("run-002") with
                    {
                        Revision = 2,
                        RestatementOfRunId = "run-001",
                        Version = 1
                    }));

        private Task<T> Run<T>(
            string operation,
            long? expectedVersion,
            ReportingGovernanceCallerContext caller,
            T result)
        {
            LastOperation = operation;
            LastExpectedVersion = expectedVersion;
            LastCaller = caller;
            return ExceptionToThrow is null
                ? Task.FromResult(result)
                : Task.FromException<T>(ExceptionToThrow);
        }
    }
}
