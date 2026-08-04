using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Operator scenario: a strategy reviewer may inspect covered-call evidence and preview an option
/// chain, but cannot start or cancel governed backtests without strategy-management authority.
/// Every covered-call route must also retain discoverable workstation tenant-scope metadata.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class CoveredCallEndpointAuthorizationTests
{
    private readonly EndpointTestFixture _fixture;

    public CoveredCallEndpointAuthorizationTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<string, string, bool, UserPermission[]> RouteAuthorizationCases =>
        new()
        {
            {
                "POST",
                UiApiRoutes.CoveredCallRuns,
                true,
                [UserPermission.ManageStrategies]
            },
            {
                "GET",
                UiApiRoutes.CoveredCallRuns,
                false,
                [UserPermission.ViewStrategies, UserPermission.ManageStrategies]
            },
            {
                "GET",
                UiApiRoutes.CoveredCallRunStatus,
                false,
                [UserPermission.ViewStrategies, UserPermission.ManageStrategies]
            },
            {
                "GET",
                UiApiRoutes.CoveredCallRunResult,
                false,
                [UserPermission.ViewStrategies, UserPermission.ManageStrategies]
            },
            {
                "POST",
                UiApiRoutes.CoveredCallRunCancel,
                true,
                [UserPermission.ManageStrategies]
            },
            {
                "POST",
                UiApiRoutes.CoveredCallChainPreview,
                false,
                [UserPermission.ViewStrategies, UserPermission.ManageStrategies]
            }
        };

    [Theory]
    [MemberData(nameof(RouteAuthorizationCases))]
    public void CoveredCallRoute_OperatorAuthority_DeclaresPermissionAndTenantCompanyScope(
        string method,
        string route,
        bool requireAll,
        UserPermission[] expectedPermissions)
    {
        var endpoint = FindEndpoint(method, route);

        endpoint.Should().NotBeNull($"the {method} {route} route should be mapped");

        var authorization = endpoint!.Metadata.GetMetadata<EndpointAuthorizationMetadata>();
        authorization.Should().NotBeNull($"{method} {route} must declare its permission gate");
        authorization!.RequireAll.Should().Be(requireAll);
        authorization.Permissions.Should().BeEquivalentTo(expectedPermissions);

        endpoint.Metadata.GetMetadata<WorkstationTenantScopeMetadata>()
            .Should().NotBeNull(
                $"{method} {route} must expose metadata for its stricter tenant-and-company scope gate");
    }

    [Fact]
    public async Task StartRun_ViewStrategiesOnly_ReturnsForbidden()
    {
        using var client = CreateScopedClient(UserPermission.ViewStrategies);

        var response = await client.PostAsJsonAsync(
            UiApiRoutes.CoveredCallRuns,
            BuildValidRunRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CancelRun_ViewStrategiesOnly_ReturnsForbidden()
    {
        using var client = CreateScopedClient(UserPermission.ViewStrategies);

        var response = await client.PostAsync(
            UiApiRoutes.CoveredCallRunCancel.Replace("{runId}", "reviewer-visible-run", StringComparison.Ordinal),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListRuns_ViewStrategiesOnly_PassesAuthorizationGate()
    {
        using var client = CreateScopedClient(UserPermission.ViewStrategies);

        var response = await client.GetAsync(UiApiRoutes.CoveredCallRuns);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PreviewChain_ViewStrategiesOnly_PassesAuthorizationGate()
    {
        using var client = CreateScopedClient(UserPermission.ViewStrategies);
        var request = new CoveredCallChainPreviewRequest(
            UnderlyingSymbol: "SPY",
            AsOf: new DateOnly(2026, 7, 15),
            MinStrike: 500m);

        var response = await client.PostAsJsonAsync(UiApiRoutes.CoveredCallChainPreview, request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StartRun_ManageStrategies_UsesRealScopedEvidenceVaultAuthority()
    {
        var evidenceStore = _fixture.Services.GetRequiredService<IEvidenceArtifactStore>();
        var owned = await SeedVaultAsync(
            evidenceStore,
            "covered-call-owned-authority",
            "endpoint-test-tenant",
            "endpoint-test-tenant");
        var foreign = await SeedVaultAsync(
            evidenceStore,
            "covered-call-foreign-authority",
            "foreign-tenant",
            "foreign-company");
        using var client = CreateScopedClient(UserPermission.ManageStrategies);

        var ownedResponse = await client.PostAsJsonAsync(
            UiApiRoutes.CoveredCallRuns,
            BuildValidRunRequest($"evidence://evidence-vault/{owned.VaultIdentity.VaultId}"));
        var foreignResponse = await client.PostAsJsonAsync(
            UiApiRoutes.CoveredCallRuns,
            BuildValidRunRequest($"evidence://evidence-vault/{foreign.VaultIdentity.VaultId}"));

        ownedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient CreateScopedClient(params UserPermission[] permissions)
    {
        var client = _fixture.CreatePermittedClient(permissions);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "directlending-admin");
        return client;
    }

    private RouteEndpoint? FindEndpoint(string method, string route) =>
        _fixture.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .FirstOrDefault(candidate =>
                NormalizeRoute(candidate.RoutePattern.RawText) == route &&
                MatchesMethod(candidate, method));

    private static CoveredCallBacktestRequest BuildValidRunRequest(
        string retainedEvidenceReference = "evidence://evidence-vault/ev-0123456789abcdef01234567") =>
        new(
            UnderlyingSymbol: "SPY",
            From: new DateOnly(2026, 1, 2),
            To: new DateOnly(2026, 3, 31),
            MinStrike: 500m)
        {
            OperatorAcceptanceCriteria = ["Review covered-call downside and assignment evidence."],
            RetainedEvidenceReferences = [retainedEvidenceReference]
        };

    private static Task<EvidenceVaultIntakeResponseDto> SeedVaultAsync(
        IEvidenceArtifactStore evidenceStore,
        string subjectId,
        string tenantId,
        string companyId) =>
        evidenceStore.WriteIntakeArtifactAsync(
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: "run",
                SubjectId: subjectId,
                IntakeChannel: "upload",
                FileName: $"{subjectId}.json",
                ContentBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"status\":\"reviewed\"}")),
                ContentType: "application/json",
                SourceSystem: "covered-call-endpoint-test",
                ReceivedBy: "endpoint-test")
            {
                TenantId = tenantId,
                Scope = companyId
            });

    private static string NormalizeRoute(string? rawRoute) =>
        string.IsNullOrEmpty(rawRoute) || rawRoute.StartsWith('/')
            ? rawRoute ?? string.Empty
            : "/" + rawRoute;

    private static bool MatchesMethod(RouteEndpoint endpoint, string method)
    {
        var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        return metadata is null || metadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }
}
