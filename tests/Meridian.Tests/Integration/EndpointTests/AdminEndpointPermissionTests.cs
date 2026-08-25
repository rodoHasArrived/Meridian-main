using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Security regression tests verifying that admin and provider-credential endpoints
/// enforce permission checks and refuse unauthenticated requests.
///
/// Wave 1 security hardening: endpoint-level access controls.
/// The test fixture runs with MDC_AUTH_MODE=optional and no user credentials configured,
/// so all requests arrive without session context and every guarded endpoint must refuse
/// them. Which refusal depends on how the route is guarded: a route guarded inside its
/// handler answers 403, because the permission helper simply returns false; a route that
/// declares its permission (W9-GOV-008) answers 401, because a declared route separates
/// "no session at all" from "session without this permission". Both refuse the request.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class AdminEndpointPermissionTests : IClassFixture<EndpointTestFixture>
{
    private readonly HttpClient _client;

    public AdminEndpointPermissionTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // ── Admin maintenance endpoints ──────────────────────────────────────────

    [Fact]
    public async Task GetAdminMaintenanceSchedule_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/schedule");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAdminMaintenanceRun_WithoutAuth_ReturnsUnauthorized()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/maintenance/run", content);
        // Declares AdminMaintenance/ManageStorage since W9-GOV-008: a request with no
        // session is unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminMaintenanceRunById_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/run/any-run-id");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminMaintenanceHistory_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/history");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAdminSelftest_WithoutAuth_ReturnsUnauthorized()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/selftest", content);
        // Declares AdminMaintenance/ManageStorage since W9-GOV-008: a request with no
        // session is unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Admin storage endpoints ──────────────────────────────────────────────

    [Fact]
    public async Task GetAdminStorageTiers_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/storage/tiers");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAdminStorageMigrate_WithoutAuth_ReturnsUnauthorized()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/storage/migrate/hot", content);
        // Declares AdminMaintenance/ManageStorage since W9-GOV-008: a request with no
        // session is unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminStorageUsage_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/storage/usage");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminStoragePermissions_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/storage/permissions");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Admin retention endpoints ─────────────────────────────────────────────

    [Fact]
    public async Task GetAdminRetention_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/retention");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAdminRetention_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.DeleteAsync("/api/admin/retention/some-policy-id/delete");
        // Declares AdminMaintenance/ManageStorage since W9-GOV-008: a request with no
        // session is unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAdminRetentionApply_WithoutAuth_ReturnsUnauthorized()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/retention/apply", content);
        // Declares AdminMaintenance/ManageStorage since W9-GOV-008: a request with no
        // session is unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Admin cleanup endpoints ──────────────────────────────────────────────

    [Fact]
    public async Task GetAdminCleanupPreview_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/cleanup/preview");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAdminCleanupExecute_WithoutAuth_ReturnsUnauthorized()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/cleanup/execute", content);
        // Declares AdminMaintenance/ManageStorage since W9-GOV-008: a request with no
        // session is unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Admin config endpoints ───────────────────────────────────────────────

    [Fact]
    public async Task GetAdminShowConfig_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/show-config");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminQuickCheck_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/quick-check");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminErrorCodes_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/error-codes");
        // Declares its permission since W9-GOV-008, lifted from the check the handler already
        // made: a request with no session is unauthenticated rather than forbidden, and is now
        // refused by the endpoint filter before the handler runs. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Provider credential endpoints ────────────────────────────────────────

    [Fact]
    public async Task PostProviderCredentialsValidate_WithoutAuth_ReturnsUnauthorized()
    {
        var payload = new
        {
            Credentials = new Dictionary<string, string> { { "ApiKey", "test-key" } }
        };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/providers/polygon/validate-credentials", content);
        // Declares ManageCredentials since W9-GOV-008, so a request with no session is
        // unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostProviderConnectionTest_WithoutAuth_ReturnsUnauthorized()
    {
        var payload = new
        {
            Credentials = new Dictionary<string, string> { { "ApiKey", "test-key" } }
        };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/providers/polygon/test-connection", content);
        // Declares ManageCredentials since W9-GOV-008, so a request with no session is
        // unauthenticated rather than forbidden. Still refused.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
