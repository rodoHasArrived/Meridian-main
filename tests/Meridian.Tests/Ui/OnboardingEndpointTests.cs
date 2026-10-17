using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class OnboardingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AnonymousCaller_CannotReadOrCreateWorkspaces()
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync(authenticated: false);
        var client = app.GetTestClient();

        (await client.GetAsync(UiApiRoutes.OnboardingWorkspaces)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync(UiApiRoutes.OnboardingWorkspaces, fixture.Request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fixture.Subjects.Scopes.Should().BeEmpty();
        (await fixture.Store.ListAsync(Fixture.Tenant, Fixture.Company)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(UserPermission.None)]
    [InlineData(UserPermission.ViewStrategies)]
    public async Task CallerWithoutAccountingPermission_CannotReadOrCreateWorkspaces(UserPermission permission)
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync(permissions: permission);
        var client = app.GetTestClient();

        (await client.GetAsync(UiApiRoutes.OnboardingWorkspaces)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(UiApiRoutes.OnboardingWorkspaces, fixture.Request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Subjects.Scopes.Should().BeEmpty();
        (await fixture.Store.ListAsync(Fixture.Tenant, Fixture.Company)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(Fixture.Tenant, null)]
    [InlineData("all", Fixture.Company)]
    [InlineData(Fixture.Tenant, "all")]
    public async Task MissingOrUnresolvedScope_FailsClosed(string? tenant, string? company)
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync(tenant: tenant, company: company);
        var client = app.GetTestClient();

        (await client.GetAsync(UiApiRoutes.OnboardingWorkspaces)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(UiApiRoutes.OnboardingWorkspaces, fixture.Request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Subjects.Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_UsesAuthenticatedTenantCompanyAndOwner_AndVerifiesPopulation()
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync(permissions: UserPermission.ManageFundStructure);
        var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.OnboardingWorkspaces, new
        {
            fixture.Request.Name,
            Scope = fixture.Request.Scope with { TenantId = "spoofed-tenant", CompanyId = "spoofed-company" },
            fixture.Request.Criteria,
            OwnerId = "spoofed-owner",
            ActorId = "spoofed-actor"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var workspace = (await response.Content.ReadFromJsonAsync<OnboardingWorkspaceDto>())!;
        workspace.OwnerId.Should().Be(Fixture.Owner);
        workspace.Scope.TenantId.Should().Be(Fixture.Tenant);
        workspace.Scope.CompanyId.Should().Be(Fixture.Company);
        workspace.CriteriaHistory.Single().ActorId.Should().Be(Fixture.Owner);
        fixture.Subjects.Scopes.Should().ContainSingle().Which.Should().BeEquivalentTo(new CloseReadinessScopeDto(
            fixture.Request.Scope.FundProfileId, fixture.Request.Scope.LedgerBookId,
            Guid.Parse(Fixture.Account), fixture.Request.Scope.EntityId, null));
        (await fixture.Store.GetAsync("spoofed-tenant", "spoofed-company", workspace.WorkspaceId)).Should().BeNull();
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("unbound")]
    public async Task Create_RejectsForeignOrUnboundFundBeforeReadingPopulation(string ownership)
    {
        using var fixture = new Fixture();
        fixture.Tenancy.Ownership = ownership switch
        {
            "foreign-tenant" => new(Fixture.Fund, "other-tenant", Fixture.Company),
            "foreign-company" => new(Fixture.Fund, Fixture.Tenant, "other-company"),
            _ => null
        };
        await using var app = await fixture.CreateAppAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.OnboardingWorkspaces, fixture.Request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Subjects.Scopes.Should().BeEmpty();
        (await fixture.Store.ListAsync(Fixture.Tenant, Fixture.Company)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid-account")]
    [InlineData("unverified-membership")]
    public async Task Create_RejectsUnverifiableAccountPopulation(string scenario)
    {
        using var fixture = new Fixture();
        var request = fixture.Request;
        if (scenario == "invalid-account")
            request = request with { Scope = request.Scope with { AccountIds = ["not-an-account-id"] } };
        else
            fixture.Subjects.Status = "ScopeMismatch";
        await using var app = await fixture.CreateAppAsync();

        (await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.OnboardingWorkspaces, request))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await fixture.Store.ListAsync(Fixture.Tenant, Fixture.Company)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("criteria")]
    [InlineData("scope.accountIds")]
    [InlineData("criteria.requiredDates")]
    [InlineData("criteria.requiredKinds")]
    [InlineData("criteria.requiredReviewerIds")]
    public async Task Create_MalformedScopeOrCriteria_ReturnsBadRequestWithoutPersisting(string nullProperty)
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync();
        var body = JsonSerializer.SerializeToNode(fixture.Request, JsonOptions)!;
        var path = nullProperty.Split('.');
        if (path.Length == 1)
            body[path[0]] = null;
        else
            body[path[0]]![path[1]] = null;
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.OnboardingWorkspaces, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await fixture.Store.ListAsync(Fixture.Tenant, Fixture.Company)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("other-tenant", Fixture.Company)]
    [InlineData(Fixture.Tenant, "other-company")]
    public async Task ForeignWorkspace_CannotReadMutateReplayOrDownloadPacket(string tenant, string company)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.SeedComparedWorkspaceAsync();
        var packet = await fixture.Service.FreezePacketAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        workspace = (await fixture.Store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;
        var before = JsonSerializer.Serialize(workspace);
        var sourceCalls = fixture.Source.CaptureCalls;
        await using var app = await fixture.CreateAppAsync(tenant: tenant, company: company);
        var client = app.GetTestClient();

        (await client.GetFromJsonAsync<OnboardingWorkspaceDto[]>(UiApiRoutes.OnboardingWorkspaces)).Should().BeEmpty();
        (await client.GetAsync(Route(UiApiRoutes.OnboardingWorkspace, workspace))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Route(UiApiRoutes.OnboardingPacket, workspace)
            .Replace("{packetId:guid}", packet.PacketId, StringComparison.Ordinal))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Route(UiApiRoutes.OnboardingPacketExport, workspace)
            .Replace("{packetId:guid}", packet.PacketId, StringComparison.Ordinal))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync(Route(UiApiRoutes.OnboardingReplay, workspace)
            .Replace("{comparisonId}", workspace.Comparisons.Single().ComparisonId, StringComparison.Ordinal), null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var mutation in new[] { "criteria", "comparison", "assignment", "review", "packet" })
        {
            var (route, request) = Mutation(workspace, mutation, workspace.Version);
            (await client.PostAsJsonAsync(route, request)).StatusCode.Should().Be(HttpStatusCode.NotFound, mutation);
        }

        fixture.Source.CaptureCalls.Should().Be(sourceCalls);
        JsonSerializer.Serialize(await fixture.Store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))
            .Should().Be(before);
    }

    [Theory]
    [InlineData("criteria")]
    [InlineData("comparison")]
    [InlineData("assignment")]
    [InlineData("packet")]
    public async Task NonOwner_CannotChangeOwnerControlledDecisions(string mutation)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.SeedComparedWorkspaceAsync();
        await using var app = await fixture.CreateAppAsync(actor: "other-operator");
        var (route, request) = Mutation(workspace, mutation, workspace.Version);

        (await app.GetTestClient().PostAsJsonAsync(route, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await fixture.Store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!.Version.Should().Be(workspace.Version);
    }

    [Fact]
    public async Task Review_RequiresNamedIndependentReviewer_AndUsesAuthenticatedActor()
    {
        using var fixture = new Fixture();
        var workspace = await fixture.SeedComparedWorkspaceAsync();
        var body = new
        {
            ExpectedVersion = workspace.Version,
            workspace.DataRevision,
            Decision = "ChangesRequested",
            Notes = "Review retained cash discrepancy evidence.",
            EvidenceIds = new[] { "evidence:cash" },
            ReviewerId = Fixture.Reviewer,
            ActorId = Fixture.Reviewer
        };
        foreach (var actor in new[] { Fixture.Owner, "other-operator" })
        {
            await using var deniedApp = await fixture.CreateAppAsync(actor: actor);
            (await deniedApp.GetTestClient().PostAsJsonAsync(Route(UiApiRoutes.OnboardingReviews, workspace), body))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        await using var reviewerApp = await fixture.CreateAppAsync(actor: Fixture.Reviewer);
        var response = await reviewerApp.GetTestClient().PostAsJsonAsync(Route(UiApiRoutes.OnboardingReviews, workspace),
            body with { ReviewerId = "spoofed-reviewer", ActorId = "spoofed-actor" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var reviewed = (await response.Content.ReadFromJsonAsync<OnboardingWorkspaceDto>())!;
        reviewed.Reviews.Should().ContainSingle().Which.ReviewerId.Should().Be(Fixture.Reviewer);
        reviewed.Reviews.Single().EvidenceIds.Should().Contain("evidence:cash");
    }

    [Theory]
    [InlineData("criteria")]
    [InlineData("comparison")]
    [InlineData("assignment")]
    [InlineData("review")]
    [InlineData("packet")]
    public async Task StaleMutation_ReturnsConflictWithoutChangingHistory(string mutation)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.SeedComparedWorkspaceAsync();
        var before = JsonSerializer.Serialize(workspace);
        var sourceCalls = fixture.Source.CaptureCalls;
        await using var app = await fixture.CreateAppAsync(actor: mutation == "review" ? Fixture.Reviewer : Fixture.Owner);
        var (route, request) = Mutation(workspace, mutation, workspace.Version - 1);

        (await app.GetTestClient().PostAsJsonAsync(route, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        fixture.Source.CaptureCalls.Should().Be(sourceCalls);
        JsonSerializer.Serialize(await fixture.Store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))
            .Should().Be(before);
    }

    [Theory]
    [InlineData("assignment")]
    [InlineData("review")]
    public async Task SupportingEvidence_MustBeVerifiedInAuthenticatedScope(string mutation)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.SeedComparedWorkspaceAsync();
        fixture.Evidence.Setup(store => store.VerifyRetainedContentAsync("foreign-evidence", Fixture.Tenant, Fixture.Company,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await using var app = await fixture.CreateAppAsync(actor: mutation == "review" ? Fixture.Reviewer : Fixture.Owner);
        var (route, original) = Mutation(workspace, mutation, workspace.Version);
        object request = original switch
        {
            AssignOnboardingDifferenceRequestDto assignment => assignment with { EvidenceIds = ["foreign-evidence"] },
            ReviewOnboardingWorkspaceRequestDto review => review with { EvidenceIds = ["foreign-evidence"] },
            _ => throw new InvalidOperationException()
        };

        (await app.GetTestClient().PostAsJsonAsync(route, request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        fixture.Evidence.Verify(store => store.VerifyRetainedContentAsync("foreign-evidence", Fixture.Tenant, Fixture.Company,
            It.IsAny<CancellationToken>()), Times.Once);
        (await fixture.Store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!.Version.Should().Be(workspace.Version);
    }

    [Fact]
    public async Task OwnerCanCaptureAssignAndFreeze_ThenReadPacketAndReplayWithoutLiveSources()
    {
        using var fixture = new Fixture();
        var workspace = await fixture.Service.CreateAsync(fixture.Request, Fixture.Owner);
        await using var app = await fixture.CreateAppAsync();
        var client = app.GetTestClient();
        var comparisonResponse = await client.PostAsJsonAsync(Route(UiApiRoutes.OnboardingComparisons, workspace),
            new CaptureOnboardingComparisonRequestDto(workspace.Version, Fixture.Date, "external-books", "import-1", "account-map", "v1"));
        comparisonResponse.StatusCode.Should().Be(HttpStatusCode.OK, await comparisonResponse.Content.ReadAsStringAsync());
        workspace = (await comparisonResponse.Content.ReadFromJsonAsync<OnboardingWorkspaceDto>())!;
        var comparison = workspace.Comparisons.Single();
        comparison.ActorId.Should().Be(Fixture.Owner);
        var (assignmentRoute, assignment) = Mutation(workspace, "assignment", workspace.Version);
        var assignmentResponse = await client.PostAsJsonAsync(assignmentRoute, assignment);
        assignmentResponse.StatusCode.Should().Be(HttpStatusCode.OK, await assignmentResponse.Content.ReadAsStringAsync());
        workspace = (await assignmentResponse.Content.ReadFromJsonAsync<OnboardingWorkspaceDto>())!;
        workspace.Assignments.Should().ContainSingle().Which.ActorId.Should().Be(Fixture.Owner);
        var packetResponse = await client.PostAsJsonAsync(Route(UiApiRoutes.OnboardingPackets, workspace),
            new FreezeOnboardingPacketRequestDto(workspace.Version));
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK, await packetResponse.Content.ReadAsStringAsync());
        var packet = (await packetResponse.Content.ReadFromJsonAsync<OnboardingReadinessPacketDto>())!;
        packet.FrozenBy.Should().Be(Fixture.Owner);
        packet.Content.Readiness.IsReady.Should().BeFalse();
        packet.Content.Readiness.UnresolvedDifferenceCount.Should().Be(1);
        packet.Content.AuthorityPosture.Should().Be(OnboardingWorkspaceService.ReadOnlyAuthorityPosture);

        fixture.Source.FailOnCapture = true;
        var retained = await client.GetFromJsonAsync<OnboardingReadinessPacketDto>(Route(UiApiRoutes.OnboardingPacket, workspace)
            .Replace("{packetId:guid}", packet.PacketId, StringComparison.Ordinal));
        JsonSerializer.Serialize(retained).Should().Be(JsonSerializer.Serialize(packet));
        var replayResponse = await client.PostAsync(Route(UiApiRoutes.OnboardingReplay, workspace)
            .Replace("{comparisonId}", comparison.ComparisonId, StringComparison.Ordinal), null);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK, await replayResponse.Content.ReadAsStringAsync());
        var replay = await replayResponse.Content.ReadFromJsonAsync<OnboardingComparisonDto>();
        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(comparison));
        fixture.Source.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public async Task DownloadPacket_PreservesDecimalBytesAndVerifiableContentHash()
    {
        using var fixture = new Fixture();
        const decimal preciseAmount = 9007199254740993.0000001m;
        fixture.Source.ExternalAmount = preciseAmount;
        var workspace = await fixture.SeedComparedWorkspaceAsync();
        var packet = await fixture.Service.FreezePacketAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        fixture.Source.FailOnCapture = true;
        await using var app = await fixture.CreateAppAsync();

        var response = await app.GetTestClient().GetAsync(Route(UiApiRoutes.OnboardingPacketExport, workspace)
            .Replace("{packetId:guid}", packet.PacketId, StringComparison.Ordinal));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.Should().Be($"onboarding-readiness-{packet.PacketId}.json");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes).Should().Contain("\"externalAmount\":9007199254740993.0000001");
        var downloaded = JsonSerializer.Deserialize<OnboardingReadinessPacketDto>(bytes, JsonOptions)!;
        downloaded.Content.Comparisons.Single().Inputs.Observations.Single().ExternalAmount.Should().Be(preciseAmount);
        downloaded.ContentHash.Should().Be(packet.ContentHash);
        downloaded.HashAlgorithm.Should().Be("SHA-256");
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(downloaded.Content, JsonOptions)))
            .ToLowerInvariant().Should().Be(downloaded.ContentHash);
        fixture.Source.CaptureCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("criteria")]
    [InlineData("comparison")]
    [InlineData("assignment")]
    [InlineData("review")]
    [InlineData("packet")]
    public async Task NonInteractiveCredential_CannotPerformHumanDecisions(string mutation)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.SeedComparedWorkspaceAsync();
        var before = JsonSerializer.Serialize(workspace);
        await using var app = await fixture.CreateAppAsync(
            actor: mutation == "review" ? Fixture.Reviewer : Fixture.Owner, apiKey: true);
        var (route, request) = mutation == "create"
            ? (UiApiRoutes.OnboardingWorkspaces, (object)fixture.Request)
            : Mutation(workspace, mutation, workspace.Version);

        (await app.GetTestClient().PostAsJsonAsync(route, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        fixture.Subjects.Scopes.Should().BeEmpty();
        (await fixture.Store.ListAsync(Fixture.Tenant, Fixture.Company)).Should().ContainSingle();
        JsonSerializer.Serialize(await fixture.Store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))
            .Should().Be(before);
    }

    private static string Route(string template, OnboardingWorkspaceDto workspace)
        => template.Replace("{workspaceId:guid}", workspace.WorkspaceId, StringComparison.Ordinal);

    private static (string Route, object Request) Mutation(OnboardingWorkspaceDto workspace, string mutation, int version)
        => mutation switch
        {
            "criteria" => (Route(UiApiRoutes.OnboardingCriteria, workspace),
                new UpdateOnboardingCriteriaRequestDto(version, workspace.Criteria with { ReviewInstructions = "Recheck the selected dates." })),
            "comparison" => (Route(UiApiRoutes.OnboardingComparisons, workspace),
                new CaptureOnboardingComparisonRequestDto(version, Fixture.Date, "external-books", "import-1", "account-map", "v1")),
            "assignment" => (Route(UiApiRoutes.OnboardingDifferenceAssignment, workspace)
                    .Replace("{differenceKey}", Uri.EscapeDataString(workspace.CurrentDifferences.Single().DifferenceKey), StringComparison.Ordinal),
                new AssignOnboardingDifferenceRequestDto(version, "analyst", "Check cash evidence.", ["evidence:cash"])),
            "review" => (Route(UiApiRoutes.OnboardingReviews, workspace),
                new ReviewOnboardingWorkspaceRequestDto(version, workspace.DataRevision, "ChangesRequested", "Check cash evidence.", ["evidence:cash"])),
            "packet" => (Route(UiApiRoutes.OnboardingPackets, workspace), new FreezeOnboardingPacketRequestDto(version)),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

    private sealed class Fixture : IDisposable
    {
        public const string Tenant = "tenant-onboarding";
        public const string Company = "company-onboarding";
        public const string Owner = "onboarding-owner";
        public const string Reviewer = "controller";
        public const string Fund = "fund-onboarding";
        public const string Account = "104d9cb4-60ca-472a-9564-d2d664098405";
        public static readonly DateOnly Date = new(2026, 1, 31);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meridian-onboarding-endpoints-{Guid.NewGuid():N}");

        public Fixture()
        {
            Store = new FileOnboardingWorkspaceStore(Path.Combine(_directory, "workspaces.json"));
            Service = new OnboardingWorkspaceService(Store, Source);
        }

        public FileOnboardingWorkspaceStore Store { get; }
        public OnboardingWorkspaceService Service { get; }
        public RecordingSource Source { get; } = new();
        public SubjectSource Subjects { get; } = new();
        public TenancyRegistry Tenancy { get; } = new();
        public Mock<IEvidenceArtifactStore> Evidence { get; } = new(MockBehavior.Strict);
        public CreateOnboardingWorkspaceRequestDto Request { get; } = new("Selected account onboarding",
            new(Tenant, Company, "ba7467d1-26e0-4f3b-9baa-528063de6774", Fund,
                Guid.Parse("c0749249-2738-44be-b336-fb586f5b200b"), [Account], Date, Date),
            new([Date], ["Balance"], 0.01m, 0.0001m, 0.01m, 100m, [Reviewer], 1, false, "Check retained source evidence."));

        public async Task<OnboardingWorkspaceDto> SeedComparedWorkspaceAsync()
        {
            var workspace = await Service.CreateAsync(Request, Owner);
            return await Service.CompareAsync(Tenant, Company, workspace.WorkspaceId,
                new(workspace.Version, Date, "external-books", "import-1", "account-map", "v1"), Owner);
        }

        public async Task<WebApplication> CreateAppAsync(
            string actor = Owner, UserPermission permissions = UserPermission.AdminMaintenance,
            string? tenant = Tenant, string? company = Company, bool authenticated = true, bool apiKey = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(Service);
            builder.Services.AddSingleton<ICloseReadinessSubjectSource>(Subjects);
            builder.Services.AddSingleton<IFundProfileTenancyRegistry>(Tenancy);
            builder.Services.AddSingleton(Evidence.Object);
            builder.Services.AddSingleton<OnboardingComparisonSource>(_ =>
                throw new NotSupportedException("Source selection is outside these endpoint authorization tests."));
            builder.Services.AddSingleton(new FundScopedWriteTenantOptions(Enforce: true));
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
                _ => RateLimitPartition.GetNoLimiter("test")));
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (authenticated)
                {
                    context.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
                    context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                    if (tenant is not null)
                        context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenant;
                    if (company is not null)
                        context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = company;
                    if (apiKey)
                        context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;
                }
                await next();
            });
            app.UseRateLimiter();
            app.MapOnboardingEndpoints(JsonOptions);
            await app.StartAsync();
            return app;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingSource : IOnboardingSourceProvider
    {
        public int CaptureCalls { get; private set; }
        public bool FailOnCapture { get; set; }
        public decimal ExternalAmount { get; set; } = 101m;

        public Task<OnboardingSourceCaptureDto> CaptureAsync(OnboardingWorkspaceDto workspace,
            CaptureOnboardingComparisonRequestDto request, CancellationToken ct = default)
        {
            if (FailOnCapture)
                throw new InvalidOperationException("Historical reads must not capture live sources.");
            CaptureCalls++;
            var now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
            var observation = new OnboardingObservationDto("Balance", Fixture.Account, "USD", null,
                100m, ExternalAmount, ["source-1"], ["evidence:cash"]);
            var payload = JsonSerializer.Serialize(observation);
            var snapshot = new OnboardingSourceSnapshotDto("source-1", "RetainedComparisonSource", "import-1", "source-version-1",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
                now, request.AsOfDate, request.MappingVersion, payload, ["evidence:cash"]);
            return Task.FromResult(new OnboardingSourceCaptureDto([snapshot], [observation], [], null));
        }
    }

    private sealed class SubjectSource : ICloseReadinessSubjectSource
    {
        public string Status { get; set; } = "Ready";
        public List<CloseReadinessScopeDto> Scopes { get; } = [];

        public Task<CloseReadinessSubjectDto?> GetSubjectAsync(CloseReadinessScopeDto scope, CancellationToken ct = default)
        {
            Scopes.Add(scope);
            return Task.FromResult<CloseReadinessSubjectDto?>(new(scope, Status, DateTimeOffset.UtcNow, "membership-v1", [Fixture.Account]));
        }
    }

    private sealed class TenancyRegistry : IFundProfileTenancyRegistry
    {
        public FundProfileOwnership? Ownership { get; set; } = new(Fixture.Fund, Fixture.Tenant, Fixture.Company);
        public Task<FundProfileOwnership?> ResolveAsync(string fundProfileId, CancellationToken ct = default)
            => Task.FromResult(fundProfileId == Fixture.Fund ? Ownership : null);
        public Task<FundProfileOwnership> BindAsync(string fundProfileId, string tenantId, string? companyId = null, CancellationToken ct = default)
            => throw new NotSupportedException("Onboarding must never claim ownership of an external population.");
        public Task<bool> IsAccessibleAsync(string fundProfileId, string tenantId, string? companyId = null, CancellationToken ct = default)
            => throw new NotSupportedException("Onboarding requires retained ownership, not permissive unbound access.");
    }
}
