using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for RBAC (Role-Based Access Control):
/// - Multi-user login via <c>MDC_USERS</c> JSON environment variable.
/// - Login response includes <c>role</c> and <c>permissions</c>.
/// - <c>GET /api/auth/me</c> returns the current user's profile.
/// - Each built-in role is mapped to the expected permission set.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class RoleAuthorizationTests : EndpointIntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public RoleAuthorizationTests(EndpointTestFixture fixture) : base(fixture) { }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs a JSON login and returns the parsed response body.
    /// </summary>
    private async Task<JsonElement?> LoginJsonAsync(string username, string password)
    {
        var payload = new { Username = username, Password = password };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/auth/login", content);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    // ── RolePermissions contract tests (no HTTP, pure unit) ──────────────────

    [Fact]
    public void RolePermissions_Admin_HasAllPermissions()
    {
        var perms = RolePermissions.For(UserRole.Admin);

        perms.Should().HaveFlag(UserPermission.ManageUsers);
        perms.Should().HaveFlag(UserPermission.AdminMaintenance);
        perms.Should().HaveFlag(UserPermission.ManageDirectLending);
        perms.Should().HaveFlag(UserPermission.ExecuteTrades);
    }

    [Fact]
    public void RolePermissions_Developer_CannotManageUsers()
    {
        var perms = RolePermissions.For(UserRole.Developer);

        perms.Should().HaveFlag(UserPermission.AdminMaintenance);
        perms.Should().HaveFlag(UserPermission.ManageStorage);
        perms.Should().NotHaveFlag(UserPermission.ManageUsers);
    }

    [Fact]
    public void RolePermissions_TradeDesk_CanExecuteTradesButCannotManageUsers()
    {
        var perms = RolePermissions.For(UserRole.TradeDesk);

        perms.Should().HaveFlag(UserPermission.ViewMarketData);
        perms.Should().HaveFlag(UserPermission.ExecuteTrades);
        perms.Should().HaveFlag(UserPermission.ManageOrders);
        perms.Should().NotHaveFlag(UserPermission.ManageUsers);
        perms.Should().NotHaveFlag(UserPermission.ModifyConfig);
    }

    [Fact]
    public void RolePermissions_Analysis_CanViewButCannotTrade()
    {
        var perms = RolePermissions.For(UserRole.Analysis);

        perms.Should().HaveFlag(UserPermission.ViewMarketData);
        perms.Should().HaveFlag(UserPermission.ViewHistoricalData);
        perms.Should().HaveFlag(UserPermission.ViewAnalytics);
        perms.Should().HaveFlag(UserPermission.ExportData);
        perms.Should().NotHaveFlag(UserPermission.ExecuteTrades);
        perms.Should().NotHaveFlag(UserPermission.ManageStrategies);
    }

    [Fact]
    public void RolePermissions_Accounting_CanManageDirectLendingButCannotTrade()
    {
        var perms = RolePermissions.For(UserRole.Accounting);

        perms.Should().HaveFlag(UserPermission.ViewTrades);
        perms.Should().HaveFlag(UserPermission.ExportData);
        perms.Should().HaveFlag(UserPermission.ViewDirectLending);
        perms.Should().HaveFlag(UserPermission.ManageDirectLending);
        perms.Should().NotHaveFlag(UserPermission.ExecuteTrades);
        perms.Should().NotHaveFlag(UserPermission.ModifyConfig);
    }

    [Fact]
    public void RolePermissions_Executive_CanViewEverythingButCannotModify()
    {
        var perms = RolePermissions.For(UserRole.Executive);

        perms.Should().HaveFlag(UserPermission.ViewMarketData);
        perms.Should().HaveFlag(UserPermission.ViewTrades);
        perms.Should().HaveFlag(UserPermission.ViewAnalytics);
        perms.Should().HaveFlag(UserPermission.ViewDirectLending);
        perms.Should().NotHaveFlag(UserPermission.ExecuteTrades);
        perms.Should().NotHaveFlag(UserPermission.ModifyConfig);
        perms.Should().NotHaveFlag(UserPermission.ManageUsers);
    }

    [Fact]
    public void RolePermissions_ReadOnly_HasMinimalAccess()
    {
        var perms = RolePermissions.For(UserRole.ReadOnly);

        perms.Should().HaveFlag(UserPermission.ViewMarketData);
        perms.Should().HaveFlag(UserPermission.ViewAnalytics);
        perms.Should().NotHaveFlag(UserPermission.ExportData);
        perms.Should().NotHaveFlag(UserPermission.ExecuteTrades);
        perms.Should().NotHaveFlag(UserPermission.ManageUsers);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Developer)]
    [InlineData(UserRole.TradeDesk)]
    [InlineData(UserRole.Analysis)]
    [InlineData(UserRole.Accounting)]
    [InlineData(UserRole.Executive)]
    [InlineData(UserRole.ReadOnly)]
    public void RolePermissions_HasPermission_ReturnsTrueForGrantedPermission(UserRole role)
    {
        // Every role grants at least ViewMarketData except Accounting
        var anyViewPermission = role == UserRole.Accounting
            ? UserPermission.ViewTrades
            : UserPermission.ViewMarketData;

        RolePermissions.HasPermission(role, anyViewPermission).Should().BeTrue();
    }

    [Fact]
    public void RolePermissions_ReadOnly_DoesNotHaveManageUsers()
    {
        RolePermissions.HasPermission(UserRole.ReadOnly, UserPermission.ManageUsers).Should().BeFalse();
    }

    // ── UserProfileRegistry tests ────────────────────────────────────────────

    [Fact]
    public void UserProfileRegistry_Legacy_AdminRoleAssignedForSingleUserEnvVar()
    {
        Environment.SetEnvironmentVariable("MDC_USERNAME", "admin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD", "pass1");
        Environment.SetEnvironmentVariable("MDC_USERS", null);
        try
        {
            var registry = new Meridian.Ui.Shared.UserProfileRegistry();
            registry.IsConfigured.Should().BeTrue();

            var profile = registry.Authenticate("admin", "pass1");
            profile.Should().NotBeNull();
            profile!.Role.Should().Be(UserRole.Admin);
            profile.Username.Should().Be("admin");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERNAME", null);
            Environment.SetEnvironmentVariable("MDC_PASSWORD", null);
        }
    }

    [Fact]
    public void UserProfileRegistry_MultiUser_CorrectRolesLoaded()
    {
        const string usersJson = """
            [
              {"username":"alice","password":"pass1","role":"TradeDesk"},
              {"username":"bob","password":"pass2","role":"Accounting"}
            ]
            """;
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        Environment.SetEnvironmentVariable("MDC_USERNAME", null);
        Environment.SetEnvironmentVariable("MDC_PASSWORD", null);
        try
        {
            var registry = new Meridian.Ui.Shared.UserProfileRegistry();
            registry.IsConfigured.Should().BeTrue();

            var alice = registry.Authenticate("alice", "pass1");
            alice.Should().NotBeNull();
            alice!.Role.Should().Be(UserRole.TradeDesk);

            var bob = registry.Authenticate("bob", "pass2");
            bob.Should().NotBeNull();
            bob!.Role.Should().Be(UserRole.Accounting);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public void UserProfileRegistry_MultiUser_WrongPasswordReturnsNull()
    {
        const string usersJson = """[{"username":"alice","password":"correct","role":"Developer"}]""";
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        try
        {
            var registry = new Meridian.Ui.Shared.UserProfileRegistry();
            var result = registry.Authenticate("alice", "wrong");
            result.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public void UserProfileRegistry_MultiUser_TakesPrecedenceOverLegacyEnvVars()
    {
        const string usersJson = """[{"username":"power","password":"pw","role":"Developer"}]""";
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        Environment.SetEnvironmentVariable("MDC_USERNAME", "legacy");
        Environment.SetEnvironmentVariable("MDC_PASSWORD", "pass");
        try
        {
            var registry = new Meridian.Ui.Shared.UserProfileRegistry();

            // MDC_USERS takes precedence — legacy user should not authenticate
            registry.Authenticate("legacy", "pass").Should().BeNull();
            registry.Authenticate("power", "pw")!.Role.Should().Be(UserRole.Developer);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
            Environment.SetEnvironmentVariable("MDC_USERNAME", null);
            Environment.SetEnvironmentVariable("MDC_PASSWORD", null);
        }
    }

    // ── HTTP login response includes role ────────────────────────────────────

    [Fact]
    public async Task LoginJson_WithValidMultiUserCredentials_ReturnsRoleInResponse()
    {
        const string usersJson = """[{"username":"trader","password":"t1","role":"TradeDesk"}]""";
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        try
        {
            var body = await LoginJsonAsync("trader", "t1");

            body.Should().NotBeNull();
            body!.Value.TryGetProperty("success", out var success).Should().BeTrue();
            success.GetBoolean().Should().BeTrue();
            body.Value.TryGetProperty("role", out var role).Should().BeTrue();
            role.GetString().Should().Be("TradeDesk");
            body.Value.TryGetProperty("permissions", out _).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task LoginJson_WithLegacyAdminCredentials_ReturnsAdminRole()
    {
        Environment.SetEnvironmentVariable("MDC_USERNAME", "sysadmin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD", "adminpass");
        Environment.SetEnvironmentVariable("MDC_USERS", null);
        try
        {
            var body = await LoginJsonAsync("sysadmin", "adminpass");

            body.Should().NotBeNull();
            body!.Value.TryGetProperty("role", out var role).Should().BeTrue();
            role.GetString().Should().Be("Admin");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERNAME", null);
            Environment.SetEnvironmentVariable("MDC_PASSWORD", null);
        }
    }

    // ── GET /api/auth/me ─────────────────────────────────────────────────────

    [Fact]
    public async Task AuthMe_WithoutSession_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthMe_AfterLogin_ReturnsCurrentUserProfile()
    {
        const string usersJson = """[{"username":"analyst","password":"a1","role":"Analysis"}]""";
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        try
        {
            // Login first
            using var loginContent = new StringContent(
                JsonSerializer.Serialize(new { Username = "analyst", Password = "a1" }),
                Encoding.UTF8, "application/json");

            using var cookieClient = Fixture.CreateNoRedirectClient();
            var loginResp = await cookieClient.PostAsync("/api/auth/login", loginContent);
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Extract the session cookie
            var sessionCookie = loginResp.Headers
                .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .FirstOrDefault(v => v.Contains("mdc-session"));

            sessionCookie.Should().NotBeNullOrWhiteSpace("a session cookie must be set after login");

            // Call /api/auth/me with the session cookie
            using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            meRequest.Headers.Add("Cookie", sessionCookie);
            var meResp = await cookieClient.SendAsync(meRequest);

            meResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meBody = await meResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            meBody.TryGetProperty("username", out var username).Should().BeTrue();
            username.GetString().Should().Be("analyst");
            meBody.TryGetProperty("role", out var role).Should().BeTrue();
            role.GetString().Should().Be("Analysis");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }
}
