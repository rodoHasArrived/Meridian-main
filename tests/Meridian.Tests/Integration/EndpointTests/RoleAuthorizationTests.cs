using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Testing;
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
    public void RolePermissions_TradeDesk_CanExecuteTradesButCannotManageCredentialsOrUsers()
    {
        var perms = RolePermissions.For(UserRole.TradeDesk);

        perms.Should().HaveFlag(UserPermission.ViewMarketData);
        perms.Should().HaveFlag(UserPermission.ExecuteTrades);
        perms.Should().HaveFlag(UserPermission.ManageOrders);
        perms.Should().NotHaveFlag(UserPermission.ManageCredentials);
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

    [Fact]
    public void RolePermissions_GetCatalog_ReturnsBuiltInProfilesAndPermissionMetadata()
    {
        var catalog = RolePermissions.GetCatalog();

        catalog.Roles.Should().Contain(role =>
            role.Role == nameof(UserRole.Accounting) &&
            role.IsBuiltIn &&
            role.Permissions.Contains(nameof(UserPermission.ManageDirectLending)));
        catalog.Permissions.Should().Contain(permission =>
            permission.Name == nameof(UserPermission.ManageUsers) &&
            permission.Group == "Administration");
    }

    [Fact]
    public void UserProfileRegistry_MultiUser_CustomPermissionsOverrideBuiltInRolePermissions()
    {
        const string usersJson = """
            [{"username":"ledger-admin","password":"pw","role":"Accounting","roleProfileName":"Ledger Admin","permissions":["ViewTrades","ViewAnalytics","ViewConfig","ModifyConfig"]}]
            """;
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);

        try
        {
            var registry = new Meridian.Identity.UserProfileRegistry();

            var profile = registry.Authenticate("ledger-admin", "pw");

            profile.Should().NotBeNull();
            profile!.Role.Should().Be(UserRole.Accounting);
            profile.RoleProfileName.Should().Be("Ledger Admin");
            profile.Permissions.Should().HaveFlag(UserPermission.ModifyConfig);
            profile.Permissions.Should().NotHaveFlag(UserPermission.ManageDirectLending);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task RolePermissionProfileStore_Upsert_AddsCustomProfileToCatalogAndLookup()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(RolePermissionProfileStore_Upsert_AddsCustomProfileToCatalogAndLookup));
        var store = new Meridian.Identity.FileRolePermissionProfileStore(new StorageOptions { RootPath = artifacts.RootPath });

        var result = await store.UpsertAsync(
            new RolePermissionProfileUpsertRequestDto(
                ProfileName: "Close Reviewer",
                DisplayName: "Close Reviewer",
                Description: "Can review close evidence without changing user accounts.",
                BaseRole: nameof(UserRole.Accounting),
                PermissionNames: [nameof(UserPermission.ViewTrades), nameof(UserPermission.ViewAnalytics), nameof(UserPermission.ExportData)],
                RequestedBy: "ops-admin",
                Rationale: "Delegate month-end review without full administration."),
            actor: "ops-admin");

        result.Profile.Role.Should().Be("Close Reviewer");
        result.Profile.IsBuiltIn.Should().BeFalse();
        result.AuditEvent.Actor.Should().Be("ops-admin");
        result.Catalog.Roles.Should().Contain(role => role.Role == "Close Reviewer" && !role.IsBuiltIn);
        store.TryGetProfile("close reviewer", out var profile).Should().BeTrue();
        profile.Permissions.Should().Contain(nameof(UserPermission.ExportData));
    }

    [Fact]
    public void UserProfileRegistry_MultiUser_RoleProfileNameLoadsStoredCustomPermissions()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(UserProfileRegistry_MultiUser_RoleProfileNameLoadsStoredCustomPermissions));
        var store = new Meridian.Identity.FileRolePermissionProfileStore(new StorageOptions { RootPath = artifacts.RootPath });
        store.UpsertAsync(
            new RolePermissionProfileUpsertRequestDto(
                ProfileName: "Ledger Reviewer",
                DisplayName: "Ledger Reviewer",
                Description: "Review-only ledger close profile.",
                BaseRole: nameof(UserRole.Accounting),
                PermissionNames: [nameof(UserPermission.ViewTrades), nameof(UserPermission.ExportData)],
                RequestedBy: "ops-admin",
                Rationale: "Bind stored role profile to configured user."),
            actor: "ops-admin").GetAwaiter().GetResult();

        const string usersJson = """
            [{"username":"ledger-reviewer","password":"pw","role":"Accounting","roleProfileName":"Ledger Reviewer"}]
            """;
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);

        try
        {
            var registry = new Meridian.Identity.UserProfileRegistry(store);

            var profile = registry.Authenticate("ledger-reviewer", "pw");

            profile.Should().NotBeNull();
            profile!.Role.Should().Be(UserRole.Accounting);
            profile.RoleProfileName.Should().Be("Ledger Reviewer");
            profile.Permissions.Should().HaveFlag(UserPermission.ExportData);
            profile.Permissions.Should().NotHaveFlag(UserPermission.ManageDirectLending);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task AuthRoleProfiles_WithManageUsers_CreatesCustomProfileAndPreservesSessionPermissions()
    {
        var profileName = $"Close Reviewer {Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable("MDC_USERS", """[{"username":"admin","password":"pw","role":"Admin"}]""");
        try
        {
            using var cookieClient = Fixture.CreateNoRedirectClient();
            var loginResp = await cookieClient.PostAsJsonAsync("/api/auth/login", new { Username = "admin", Password = "pw" });
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var authCookies = ExtractAuthCookies(loginResp);

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/role-profiles")
            {
                Content = JsonContent.Create(new RolePermissionProfileUpsertRequestDto(
                    ProfileName: profileName,
                    DisplayName: profileName,
                    Description: "Close review authority profile.",
                    BaseRole: nameof(UserRole.Accounting),
                    PermissionNames: [nameof(UserPermission.ViewTrades), nameof(UserPermission.ExportData)],
                    RequestedBy: "admin",
                    Rationale: "Create scoped close-review authority.",
                    CorrelationId: "role-profile-test"))
            };
            createRequest.Headers.Add("Cookie", authCookies.CookieHeader);
            createRequest.Headers.Add("X-CSRF-Token", authCookies.CsrfToken);

            var createResp = await cookieClient.SendAsync(createRequest);

            createResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await createResp.Content.ReadFromJsonAsync<RolePermissionProfileUpsertResultDto>(JsonOptions);
            body.Should().NotBeNull();
            body!.Profile.Role.Should().Be(profileName);
            body.AuditEvent.CorrelationId.Should().Be("role-profile-test");
            body.Catalog.Roles.Should().Contain(role => role.Role == profileName && !role.IsBuiltIn);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }

        Environment.SetEnvironmentVariable("MDC_USERS", $$"""[{"username":"reviewer","password":"pw","role":"Accounting","roleProfileName":"{{profileName}}"}]""");
        try
        {
            using var reviewerClient = Fixture.CreateNoRedirectClient();
            var loginResp = await reviewerClient.PostAsJsonAsync("/api/auth/login", new { Username = "reviewer", Password = "pw" });
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var sessionCookie = loginResp.Headers
                .Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(header => header.Value)
                .First(value => value.Contains("mdc-session", StringComparison.OrdinalIgnoreCase));

            using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            meRequest.Headers.Add("Cookie", sessionCookie);
            var meResp = await reviewerClient.SendAsync(meRequest);

            meResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meBody = await meResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            meBody.TryGetProperty("roleProfileName", out var roleProfileName).Should().BeTrue();
            roleProfileName.GetString().Should().Be(profileName);
            var permissionNames = meBody.GetProperty("permissionNames")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToList();
            permissionNames.Should().Contain(nameof(UserPermission.ExportData));
            permissionNames.Should().NotContain(nameof(UserPermission.ManageDirectLending));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task AuthRoleProfiles_InvalidPermission_ReturnsBadRequest()
    {
        Environment.SetEnvironmentVariable("MDC_USERS", """[{"username":"admin","password":"pw","role":"Admin"}]""");
        try
        {
            using var cookieClient = Fixture.CreateNoRedirectClient();
            var loginResp = await cookieClient.PostAsJsonAsync("/api/auth/login", new { Username = "admin", Password = "pw" });
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var authCookies = ExtractAuthCookies(loginResp);

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/role-profiles")
            {
                Content = JsonContent.Create(new RolePermissionProfileUpsertRequestDto(
                    ProfileName: $"Bad Profile {Guid.NewGuid():N}",
                    DisplayName: "Bad Profile",
                    Description: "Invalid permission test.",
                    BaseRole: nameof(UserRole.Accounting),
                    PermissionNames: ["NoSuchPermission"],
                    RequestedBy: "admin",
                    Rationale: "Reject bad permission."))
            };
            createRequest.Headers.Add("Cookie", authCookies.CookieHeader);
            createRequest.Headers.Add("X-CSRF-Token", authCookies.CsrfToken);

            var createResp = await cookieClient.SendAsync(createRequest);

            createResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
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
            var registry = new Meridian.Identity.UserProfileRegistry();
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
            var registry = new Meridian.Identity.UserProfileRegistry();
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
            var registry = new Meridian.Identity.UserProfileRegistry();
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
            var registry = new Meridian.Identity.UserProfileRegistry();

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

    [Fact]
    public async Task AuthMe_WithCustomAdminPermissionOverride_DoesNotRegainBuiltInAdminPermissions()
    {
        const string usersJson = """[{"username":"limited-admin","password":"pw","role":"Admin","roleProfileName":"Limited Admin","permissions":["ViewMarketData"]}]""";
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        try
        {
            using var loginContent = new StringContent(
                JsonSerializer.Serialize(new { Username = "limited-admin", Password = "pw" }),
                Encoding.UTF8,
                "application/json");

            using var cookieClient = Fixture.CreateNoRedirectClient();
            var loginResp = await cookieClient.PostAsync("/api/auth/login", loginContent);
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var loginBody = await loginResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            GetPermissionNames(loginBody).Should().BeEquivalentTo([nameof(UserPermission.ViewMarketData)]);

            var sessionCookie = loginResp.Headers
                .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .FirstOrDefault(v => v.Contains("mdc-session"));

            sessionCookie.Should().NotBeNullOrWhiteSpace("a session cookie must be set after login");

            using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            meRequest.Headers.Add("Cookie", sessionCookie);
            var meResp = await cookieClient.SendAsync(meRequest);

            meResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meBody = await meResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            meBody.TryGetProperty("role", out var role).Should().BeTrue();
            role.GetString().Should().Be(nameof(UserRole.Admin));
            meBody.TryGetProperty("roleProfileName", out var roleProfileName).Should().BeTrue();
            roleProfileName.GetString().Should().Be("Limited Admin");
            var mePermissionNames = GetPermissionNames(meBody);
            mePermissionNames.Should().BeEquivalentTo([nameof(UserPermission.ViewMarketData)]);
            mePermissionNames.Should().NotContain(nameof(UserPermission.ManageCredentials));
            mePermissionNames.Should().NotContain(nameof(UserPermission.ExecuteTrades));
            mePermissionNames.Should().NotContain(nameof(UserPermission.ManageUsers));
            mePermissionNames.Should().NotContain(nameof(UserPermission.AdminMaintenance));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    private static string[] GetPermissionNames(JsonElement? body)
    {
        body.Should().NotBeNull();
        body!.Value.TryGetProperty("permissionNames", out var permissionNames).Should().BeTrue();
        return permissionNames.EnumerateArray().Select(permission => permission.GetString()!).ToArray();
    }

    private static AuthCookies ExtractAuthCookies(HttpResponseMessage response)
    {
        var setCookies = response.Headers
            .Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value)
            .ToArray();
        var sessionCookie = ExtractCookieValue(setCookies, "mdc-session");
        var csrfCookie = ExtractCookieValue(setCookies, "mdc-csrf");

        sessionCookie.Should().NotBeNullOrWhiteSpace("a session cookie must be set after login");
        csrfCookie.Should().NotBeNullOrWhiteSpace("a CSRF cookie must be set after login");

        return new AuthCookies($"mdc-session={sessionCookie}; mdc-csrf={csrfCookie}", csrfCookie!);
    }

    private static string? ExtractCookieValue(IEnumerable<string> setCookies, string cookieName)
    {
        var prefix = cookieName + "=";
        foreach (var setCookie in setCookies)
        {
            var segment = setCookie
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(part => part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (segment is not null)
            {
                return segment[prefix.Length..];
            }
        }

        return null;
    }

    private sealed record AuthCookies(string CookieHeader, string CsrfToken);

}
