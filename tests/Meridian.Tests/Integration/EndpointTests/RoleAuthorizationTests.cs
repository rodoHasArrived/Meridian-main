using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
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

    private const string PwHash = "pbkdf2-sha256$210000$sojUSudyG6r1NOVSUOiReg==$ktvN4ENX7BYpcwa4bgEIV037Ak8ktnT7fmjabd68qMo=";
    private const string Pass1Hash = "pbkdf2-sha256$210000$gxadGi1uP6lnWXkOOWivJw==$7eLeyYf5oRzAO7sPjPTcqRzQytrGuU2NRxi6aiEb3Hw=";
    private const string Pass2Hash = "pbkdf2-sha256$210000$uW+Zrqf+fiaFdwl8kzxHlA==$BAogTzsYmLJAhD1ZXy4534Ll7nsJdcodl54yUDNAIw0=";
    private const string CorrectHash = "pbkdf2-sha256$210000$OWWO5woPFXjNDMS2xH/Zhg==$BhBDOCqpOzn0Ev9a0T1hQrI1jSsS2gzCPw0y8LwGZ4o=";
    private const string PassHash = "pbkdf2-sha256$210000$ECKlpXI2tTxIV7I7sOEYEg==$j9rXTDIWWDm9x63UxGLfWKOblUvRHWJDZr/Ygrn3sPY=";
    private const string T1Hash = "pbkdf2-sha256$210000$gsKsf7ov8zGR8qO3EmT5aw==$jcyrrsMzlYHDR2PAQVBomw2MyvjbIrBQZMM3o6Pgtu4=";
    private const string A1Hash = "pbkdf2-sha256$210000$xzVxfH5/KLIwNGxmKoK94g==$6jvefF6BbU/DOTFHoqhk/b9/Dzaq+LbuKyMZkVEKySw=";
    private const string AdminPassHash = "pbkdf2-sha256$210000$RwygFvkip6YbLobhSB8FwQ==$JVXOoM8ZMW5NsQznykddTiHghz7NlLwZEP5hVHJrxDA=";

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
        var usersJson = $$"""
            [{"username":"ledger-admin","passwordHash":"{{PwHash}}","role":"Accounting","roleProfileName":"Ledger Admin","companyId":"company-alpha","permissions":["ViewTrades","ViewAnalytics","ViewConfig","ModifyConfig"]}]
            """;
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);

        try
        {
            var registry = new Meridian.Identity.UserProfileRegistry();

            var profile = registry.Authenticate("ledger-admin", "pw");

            profile.Should().NotBeNull();
            profile!.Role.Should().Be(UserRole.Accounting);
            profile.RoleProfileName.Should().Be("Ledger Admin");
            profile.CompanyId.Should().Be("company-alpha");
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

        var usersJson = $$"""
            [{"username":"ledger-reviewer","passwordHash":"{{PwHash}}","role":"Accounting","roleProfileName":"Ledger Reviewer"}]
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
        await using var isolated = await IsolatedEndpointTestScope.CreateAsync();
        var profileName = $"Close Reviewer {Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable("MDC_USERS", $$"""[{"username":"admin","passwordHash":"{{PwHash}}","role":"Admin"}]""");
        try
        {
            using var cookieClient = isolated.Fixture.CreateNoRedirectClient();
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

        Environment.SetEnvironmentVariable("MDC_USERS", $$"""[{"username":"reviewer","passwordHash":"{{PwHash}}","role":"Accounting","roleProfileName":"{{profileName}}"}]""");
        try
        {
            using var reviewerClient = isolated.Fixture.CreateNoRedirectClient();
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
        await using var isolated = await IsolatedEndpointTestScope.CreateAsync();
        Environment.SetEnvironmentVariable("MDC_USERS", $$"""[{"username":"admin","passwordHash":"{{PwHash}}","role":"Admin"}]""");
        try
        {
            using var cookieClient = isolated.Fixture.CreateNoRedirectClient();
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
    public void PasswordHashing_HashPassword_VerifiesAndRejectsWrongPassword()
    {
        var hash = Meridian.Identity.PasswordHashing.HashPassword("operator-secret");

        hash.Should().StartWith("pbkdf2-sha256$");
        hash.Should().NotContain("operator-secret");
        Meridian.Identity.PasswordHashing.VerifyPassword("operator-secret", hash).Should().BeTrue();
        Meridian.Identity.PasswordHashing.VerifyPassword("wrong-secret", hash).Should().BeFalse();
    }

    [Fact]
    public void UserProfileRegistry_MultiUser_PlaintextPasswordFieldDoesNotConfigureAccount()
    {
        Environment.SetEnvironmentVariable("MDC_USERS", """[{"username":"plain","password":"pw","role":"Admin"}]""");
        Environment.SetEnvironmentVariable("MDC_USERNAME", null);
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
        try
        {
            var registry = new Meridian.Identity.UserProfileRegistry();

            registry.IsConfigured.Should().BeFalse();
            registry.Authenticate("plain", "pw").Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task FileUserAccountStore_ResetDisableAndAudit_DoesNotPersistPlaintextPassword()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(FileUserAccountStore_ResetDisableAndAudit_DoesNotPersistPlaintextPassword));
        var store = new Meridian.Identity.FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });

        var created = await store.UpsertAsync(
            new UserAccountUpsertRequestDto(
                Username: "ops-admin",
                Role: nameof(UserRole.Admin),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ManageUsers)],
                NewPassword: "create-secret",
                PasswordHash: null,
                IsDisabled: false,
                PasswordResetRequired: false,
                RequestedBy: "security-admin",
                Rationale: "Create governed account.",
                CorrelationId: "account-create-test",
                CompanyId: "company-alpha"),
            actor: "security-admin");

        created.Account.Username.Should().Be("ops-admin");
        created.Account.CompanyId.Should().Be("company-alpha");
        var accountPath = Path.Combine(artifacts.RootPath, "governance", "user-accounts.json");
        var accountJson = await File.ReadAllTextAsync(accountPath);
        accountJson.Should().Contain("pbkdf2-sha256");
        accountJson.Should().Contain("company-alpha");
        accountJson.Should().NotContain("create-secret");

        var reset = await store.ResetPasswordAsync(
            new UserPasswordResetRequestDto(
                Username: "ops-admin",
                NewPassword: "reset-secret",
                PasswordHash: null,
                PasswordResetRequired: true,
                RevokeSessions: true,
                RequestedBy: "security-admin",
                Rationale: "Rotate account password.",
                CorrelationId: "password-reset-test"),
            actor: "security-admin",
            revokedSessionCount: 2);
        reset.Account.PasswordResetRequired.Should().BeTrue();
        reset.RevokedSessionCount.Should().Be(2);

        var disabled = await store.SetDisabledAsync(
            new UserAccountDisableRequestDto(
                Username: "ops-admin",
                IsDisabled: true,
                RevokeSessions: true,
                RequestedBy: "security-admin",
                Rationale: "Disable departed operator.",
                CorrelationId: "account-disable-test"),
            actor: "security-admin",
            revokedSessionCount: 1);
        disabled.Account.IsDisabled.Should().BeTrue();
        disabled.RevokedSessionCount.Should().Be(1);

        accountJson = await File.ReadAllTextAsync(accountPath);
        accountJson.Should().NotContain("reset-secret");

        var audit = await store.GetAuditEventsAsync(limit: 10);
        audit.Select(item => item.EventType).Should().Contain([
            "user-account-created",
            "user-password-reset",
            "user-account-disabled"
        ]);
        audit.Should().Contain(item => item.RevokedSessionCount == 2);
    }

    [Fact]
    public void UserProfileRegistry_Legacy_AdminRoleAssignedForSingleUserPasswordHashEnvVar()
    {
        Environment.SetEnvironmentVariable("MDC_USERNAME", "admin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", Pass1Hash);
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
            Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
        }
    }

    [Fact]
    public void UserProfileRegistry_MultiUser_CorrectRolesLoaded()
    {
        var usersJson = $$"""
            [
              {"username":"alice","passwordHash":"{{Pass1Hash}}","role":"TradeDesk"},
              {"username":"bob","passwordHash":"{{Pass2Hash}}","role":"Accounting"}
            ]
            """;
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        Environment.SetEnvironmentVariable("MDC_USERNAME", null);
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
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
        var usersJson = $$"""[{"username":"alice","passwordHash":"{{CorrectHash}}","role":"Developer"}]""";
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
        var usersJson = $$"""[{"username":"power","passwordHash":"{{PwHash}}","role":"Developer"}]""";
        Environment.SetEnvironmentVariable("MDC_USERS", usersJson);
        Environment.SetEnvironmentVariable("MDC_USERNAME", "legacy");
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", PassHash);
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
            Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
        }
    }

    // ── HTTP login response includes role ────────────────────────────────────

    [Fact]
    public async Task LoginJson_WithValidMultiUserCredentials_ReturnsRoleInResponse()
    {
        var usersJson = $$"""[{"username":"trader","passwordHash":"{{T1Hash}}","role":"TradeDesk"}]""";
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
    public async Task LoginJson_WithLegacyAdminPasswordHashCredentials_ReturnsAdminRole()
    {
        Environment.SetEnvironmentVariable("MDC_USERNAME", "sysadmin");
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", AdminPassHash);
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
            Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
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
        var usersJson = $$"""[{"username":"analyst","passwordHash":"{{A1Hash}}","role":"Analysis","companyId":"company-alpha"}]""";
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
            meBody.TryGetProperty("companyId", out var companyId).Should().BeTrue();
            companyId.GetString().Should().Be("company-alpha");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task AuthMe_WithCustomAdminPermissionOverride_DoesNotRegainBuiltInAdminPermissions()
    {
        var usersJson = $$"""[{"username":"limited-admin","passwordHash":"{{PwHash}}","role":"Admin","roleProfileName":"Limited Admin","permissions":["ViewMarketData"]}]""";
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

    [Fact]
    public async Task AuthAccounts_WithManageUsers_AdministersAccountLifecycleAndRevokesSessions()
    {
        await using var isolated = await IsolatedEndpointTestScope.CreateAsync();
        var username = $"ops-{Guid.NewGuid():N}";
        using var adminClient = isolated.Fixture.CreatePermittedClient(UserPermission.ManageUsers);

        var createResponse = await adminClient.PutAsJsonAsync(
            $"/api/auth/accounts/{username}",
            new UserAccountUpsertRequestDto(
                Username: username,
                Role: nameof(UserRole.Accounting),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ViewTrades)],
                NewPassword: "initial-pass",
                PasswordHash: null,
                IsDisabled: false,
                PasswordResetRequired: false,
                RequestedBy: "account-admin",
                Rationale: "Create account through product administration.",
                CorrelationId: "auth-account-create",
                CompanyId: "company-alpha"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createBody = await createResponse.Content.ReadFromJsonAsync<UserAccountMutationResultDto>(JsonOptions);
        createBody.Should().NotBeNull();
        createBody!.Account.CompanyId.Should().Be("company-alpha");

        var listResponse = await adminClient.GetAsync("/api/auth/accounts");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        listJson.Should().Contain(username);
        listJson.Should().Contain("company-alpha");
        listJson.Should().NotContain("passwordHash");
        listJson.Should().NotContain("initial-pass");

        using var sessionClient = isolated.Fixture.CreateNoRedirectClient();
        var loginResponse = await sessionClient.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = "initial-pass" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        loginBody.GetProperty("companyId").GetString().Should().Be("company-alpha");
        var sessionCookie = loginResponse.Headers
            .Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value)
            .First(value => value.Contains("mdc-session", StringComparison.OrdinalIgnoreCase));

        var resetResponse = await adminClient.PostAsJsonAsync(
            $"/api/auth/accounts/{username}/password-reset",
            new UserPasswordResetRequestDto(
                Username: username,
                NewPassword: "rotated-pass",
                PasswordHash: null,
                PasswordResetRequired: true,
                RevokeSessions: true,
                RequestedBy: "account-admin",
                Rationale: "Rotate password and revoke active sessions.",
                CorrelationId: "auth-account-reset"));
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var resetBody = await resetResponse.Content.ReadFromJsonAsync<UserAccountMutationResultDto>(JsonOptions);
        resetBody.Should().NotBeNull();
        resetBody!.RevokedSessionCount.Should().BeGreaterThan(0);
        resetBody.Account.PasswordResetRequired.Should().BeTrue();

        using var staleMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        staleMeRequest.Headers.Add("Cookie", sessionCookie);
        var staleMeResponse = await sessionClient.SendAsync(staleMeRequest);
        staleMeResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var rotatedLoginResponse = await sessionClient.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = "rotated-pass" });
        rotatedLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotatedLoginBody = await rotatedLoginResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        rotatedLoginBody.GetProperty("passwordResetRequired").GetBoolean().Should().BeTrue();

        var disableResponse = await adminClient.PostAsJsonAsync(
            $"/api/auth/accounts/{username}/disable",
            new UserAccountDisableRequestDto(
                Username: username,
                IsDisabled: true,
                RevokeSessions: true,
                RequestedBy: "account-admin",
                Rationale: "Disable account through product administration.",
                CorrelationId: "auth-account-disable"));
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var disableBody = await disableResponse.Content.ReadFromJsonAsync<UserAccountMutationResultDto>(JsonOptions);
        disableBody.Should().NotBeNull();
        disableBody!.Account.IsDisabled.Should().BeTrue();
        disableBody.RevokedSessionCount.Should().BeGreaterThan(0);

        var disabledLoginResponse = await sessionClient.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = "rotated-pass" });
        disabledLoginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var auditResponse = await adminClient.GetAsync("/api/auth/audit?limit=20");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await auditResponse.Content.ReadFromJsonAsync<IReadOnlyList<UserAccountAuditEventDto>>(JsonOptions);
        audit.Should().NotBeNull();
        audit!.Select(item => item.EventType).Should().Contain([
            "user-account-created",
            "user-password-reset",
            "user-account-disabled",
            "user-sessions-revoked"
        ]);
    }

    [Fact]
    public async Task AuthScopedAccess_WithAutomationOrigin_ReturnsBadRequestWithoutMutatingAuthority()
    {
        await using var isolated = await IsolatedEndpointTestScope.CreateAsync();
        using var adminClient = isolated.Fixture.CreatePermittedClient(UserPermission.ManageUsers);
        var blockedPrincipal = $"assistant-admin-{Guid.NewGuid():N}";
        var createResponse = await adminClient.PostAsJsonAsync(
            "/api/auth/access-assignments",
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: blockedPrincipal,
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Admin),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ManageUsers)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "assistant-agent",
                Rationale: "Assistant drafted a scoped authority grant.",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var createError = await createResponse.Content.ReadAsStringAsync();
        createError.Should().Contain("Reviewed automation cannot grant scoped access assignments");

        var blockedList = await adminClient.GetFromJsonAsync<IReadOnlyList<UserAccessAssignmentDto>>(
            $"/api/auth/access-assignments?principalId={blockedPrincipal}&includeRevoked=true",
            JsonOptions);
        blockedList.Should().NotBeNull();
        blockedList.Should().BeEmpty("assistant-origin scoped authority grants must fail before persistence");

        var retainedPrincipal = $"close-reviewer-{Guid.NewGuid():N}";
        var allowedCreateResponse = await adminClient.PostAsJsonAsync(
            "/api/auth/access-assignments",
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: retainedPrincipal,
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Accounting),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ViewTrades)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "account-admin",
                Rationale: "Grant temporary close review authority."));
        allowedCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await allowedCreateResponse.Content.ReadFromJsonAsync<UserAccessAssignmentMutationResultDto>(JsonOptions);
        created.Should().NotBeNull();

        var revokeResponse = await adminClient.PostAsJsonAsync(
            $"/api/auth/access-assignments/{created!.Assignment.AssignmentId}/revoke",
            new UserAccessAssignmentRevokeRequestDto(
                created.Assignment.AssignmentId,
                ExpectedVersion: created.Assignment.Version,
                RequestedBy: "automation-agent",
                Rationale: "Automation requested scoped authority revocation.",
                ActionOrigin: OperationsActionOriginDto.AutomationAssistant));

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var revokeError = await revokeResponse.Content.ReadAsStringAsync();
        revokeError.Should().Contain("Reviewed automation cannot revoke scoped access assignments");

        var retainedList = await adminClient.GetFromJsonAsync<IReadOnlyList<UserAccessAssignmentDto>>(
            $"/api/auth/access-assignments?principalId={retainedPrincipal}&includeRevoked=true",
            JsonOptions);
        retainedList.Should().NotBeNull();
        var retained = retainedList.Should().ContainSingle().Subject;
        retained.Version.Should().Be(created.Assignment.Version);
        retained.RevokedAtUtc.Should().BeNull();
        retained.RevokedBy.Should().BeNull();
    }

    [Fact]
    public async Task AccountAdministration_SessionlessMalformedBody_IsRefusedBeforeBinding()
    {
        await using var isolated = await IsolatedEndpointTestScope.CreateAsync();
        Environment.SetEnvironmentVariable("MDC_USERS", $$"""[{"username":"admin","passwordHash":"{{PwHash}}","role":"Admin"}]""");
        try
        {
            using var client = isolated.Fixture.CreateNoRedirectClient();
            foreach (var (method, path) in new[]
            {
                (HttpMethod.Put, "/api/auth/accounts/target-user"),
                (HttpMethod.Post, "/api/auth/sessions/revoke"),
                (HttpMethod.Post, "/api/auth/role-profiles"),
                (HttpMethod.Post, "/api/auth/access-assignments")
            })
            {
                using var request = new HttpRequestMessage(method, path)
                {
                    // Deliberately malformed JSON: an endpoint filter only runs after binding, so
                    // this body would be parsed and answered with a binding 400 without ever
                    // presenting a session. The middleware guard must refuse it first.
                    Content = new StringContent("{", Encoding.UTF8, "application/json")
                };

                var response = await client.SendAsync(request);

                response.StatusCode.Should().Be(
                    HttpStatusCode.Unauthorized,
                    $"a sessionless {method} {path} with a malformed body must be refused before binding");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task AccountAdministration_MalformedBodyWithoutManageUsers_IsForbiddenBeforeBinding()
    {
        await using var isolated = await IsolatedEndpointTestScope.CreateAsync();
        Environment.SetEnvironmentVariable(
            "MDC_USERS",
            $$"""[{"username":"fund-ops","passwordHash":"{{PwHash}}","role":"Accounting"}]""");
        try
        {
            using var client = isolated.Fixture.CreateNoRedirectClient();
            var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { Username = "fund-ops", Password = "pw" });
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var authCookies = ExtractAuthCookies(loginResp);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/role-profiles")
            {
                Content = new StringContent("{", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Cookie", authCookies.CookieHeader);
            request.Headers.Add("X-CSRF-Token", authCookies.CsrfToken);

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                "a session without ManageUsers must be refused before its malformed body is bound");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
    }

    [Fact]
    public async Task AccountAdministration_MalformedBodyWithManageUsers_StillFailsBinding()
    {
        await using var isolated = await IsolatedEndpointTestScope.CreateAsync();
        Environment.SetEnvironmentVariable("MDC_USERS", $$"""[{"username":"admin","passwordHash":"{{PwHash}}","role":"Admin"}]""");
        try
        {
            using var client = isolated.Fixture.CreateNoRedirectClient();
            var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { Username = "admin", Password = "pw" });
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var authCookies = ExtractAuthCookies(loginResp);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/role-profiles")
            {
                Content = new StringContent("{", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Cookie", authCookies.CookieHeader);
            request.Headers.Add("X-CSRF-Token", authCookies.CsrfToken);

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(
                HttpStatusCode.BadRequest,
                "an authorized caller's malformed body is still answered by binding, proving the guard sits before binding rather than replacing it");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_USERS", null);
        }
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

    /// <summary>
    /// Owns a disposable endpoint host for scenarios that persist account, role-profile, or scoped-access state.
    /// The host's unique data root prevents one destructive authorization scenario from changing later logins.
    /// </summary>
    private sealed class IsolatedEndpointTestScope : IAsyncDisposable
    {
        private readonly EndpointTestFixture _fixture;

        private IsolatedEndpointTestScope(EndpointTestFixture fixture) => _fixture = fixture;

        public EndpointTestFixture Fixture => _fixture;

        public static async Task<IsolatedEndpointTestScope> CreateAsync()
        {
            var fixture = new EndpointTestFixture();
            try
            {
                await fixture.InitializeAsync();
                return new IsolatedEndpointTestScope(fixture);
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
    }

}
