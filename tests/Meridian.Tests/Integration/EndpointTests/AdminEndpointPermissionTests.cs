using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Security regression tests verifying that admin and provider-credential endpoints
/// enforce permission checks and return 403 Forbidden for unauthenticated requests.
///
/// Wave 1 security hardening: endpoint-level access controls.
/// The test fixture runs with MDC_AUTH_MODE=optional and no user credentials configured,
/// so all requests arrive without session context — the permission helpers return false
/// and all guarded endpoints must return 403.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class AdminEndpointPermissionTests
{
    private readonly HttpClient _client;

    public AdminEndpointPermissionTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // ── Admin maintenance endpoints ──────────────────────────────────────────

    [Fact]
    public async Task GetAdminMaintenanceSchedule_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/schedule");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostAdminMaintenanceRun_WithoutAuth_ReturnsForbidden()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/maintenance/run", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAdminMaintenanceRunById_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/run/any-run-id");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAdminMaintenanceHistory_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/maintenance/history");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostAdminSelftest_WithoutAuth_ReturnsForbidden()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/selftest", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Admin storage endpoints ──────────────────────────────────────────────

    [Fact]
    public async Task GetAdminStorageTiers_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/storage/tiers");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostAdminStorageMigrate_WithoutAuth_ReturnsForbidden()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/storage/migrate/hot", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAdminStorageUsage_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/storage/usage");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAdminStoragePermissions_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/storage/permissions");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Admin retention endpoints ─────────────────────────────────────────────

    [Fact]
    public async Task GetAdminRetention_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/retention");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteAdminRetention_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.DeleteAsync("/api/admin/retention/some-policy-id/delete");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostAdminRetentionApply_WithoutAuth_ReturnsForbidden()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/retention/apply", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Admin cleanup endpoints ──────────────────────────────────────────────

    [Fact]
    public async Task GetAdminCleanupPreview_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/cleanup/preview");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostAdminCleanupExecute_WithoutAuth_ReturnsForbidden()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/admin/cleanup/execute", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Admin config endpoints ───────────────────────────────────────────────

    [Fact]
    public async Task GetAdminShowConfig_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/show-config");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAdminQuickCheck_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/quick-check");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAdminErrorCodes_WithoutAuth_ReturnsForbidden()
    {
        var response = await _client.GetAsync("/api/admin/error-codes");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Provider credential endpoints ────────────────────────────────────────

    [Fact]
    public async Task PostProviderCredentialsValidate_WithoutAuth_ReturnsForbidden()
    {
        var payload = new
        {
            Credentials = new Dictionary<string, string> { { "ApiKey", "test-key" } }
        };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/providers/polygon/validate-credentials", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostProviderConnectionTest_WithoutAuth_ReturnsForbidden()
    {
        var payload = new
        {
            Credentials = new Dictionary<string, string> { { "ApiKey", "test-key" } }
        };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/providers/polygon/test-connection", content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
