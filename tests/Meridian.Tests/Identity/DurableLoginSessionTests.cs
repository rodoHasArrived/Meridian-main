using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity;

namespace Meridian.Tests.Identity;

[Collection("IdentityEnvironment")]
public sealed class DurableLoginSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-auth-{Guid.NewGuid():N}");
    private readonly EnvironmentVariableScope _environment;
    private readonly TestClock _clock = new();
    private string StorePath => Path.Combine(_root, "sessions.json");

    public DurableLoginSessionTests()
    {
        var hash = PasswordHashing.HashPassword("operator-password");
        _environment = new EnvironmentVariableScope()
            .Set("MDC_USERS", $$"""[{"username":"operator","passwordHash":"{{hash}}","role":"Accounting"}]""")
            .Set("MDC_DEMO_USERS", null).Set("MDC_USERNAME", null).Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", "required");
    }

    [Fact]
    public void RunningNodes_ObserveCreationLogoutAndRevocationWithoutRestartOrResurrection()
    {
        var first = CreateNode();
        var second = CreateNode();
        var token = first.CreateSession("operator", "operator-password")!;
        second.ValidateSession(token).Should().BeTrue();

        second.RemoveSession(token);
        first.ValidateSession(token).Should().BeFalse();
        var laterToken = first.CreateSession("operator", "operator-password")!;
        second.RevokeSessionsForUser("operator").Should().Be(1);
        first.GetSessionProfile(laterToken).Should().BeNull();

        first.CreateSession("operator", "operator-password").Should().NotBeNull();
        var restarted = CreateNode();
        restarted.ValidateSession(token).Should().BeFalse();
        restarted.ValidateSession(laterToken).Should().BeFalse();
        restarted.RevokeAllSessions().Should().Be(1);
        first.RevokeAllSessions().Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentNodes_PreserveEveryCommittedSession()
    {
        var nodes = Enumerable.Range(0, 4).Select(_ => CreateNode()).ToArray();
        var tokens = await Task.WhenAll(nodes.Select(node => Task.Run(
            () => node.CreateSession("operator", "operator-password")!)));

        tokens.Should().OnlyHaveUniqueItems();
        foreach (var token in tokens)
            CreateNode().ValidateSession(token).Should().BeTrue();
        nodes[0].RevokeAllSessions().Should().Be(4);
        foreach (var token in tokens)
            nodes[1].ValidateSession(token).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentFailures_LockOutAcrossNodesAndRestartUntilExactExpiry()
    {
        var nodes = Enumerable.Range(0, 5).Select(_ => CreateNode()).ToArray();
        var attempts = await Task.WhenAll(nodes.Select(node => Task.Run(
            () => node.TryCreateSession("operator", "wrong", "client"))));

        attempts.Count(attempt => attempt.Status == LoginAttemptStatus.InvalidCredentials).Should().Be(4);
        attempts.Count(attempt => attempt.Status == LoginAttemptStatus.LockedOut).Should().Be(1);
        attempts.Select(attempt => attempt.RemainingAttempts).Order().Should().Equal(0, 1, 2, 3, 4);
        CreateNode().TryCreateSession("operator", "operator-password", "client").RetryAfter
            .Should().Be(TimeSpan.FromMinutes(15));
        _clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        nodes[0].TryCreateSession("operator", "operator-password", "client").Status
            .Should().Be(LoginAttemptStatus.LockedOut);
        _clock.Advance(TimeSpan.FromSeconds(1));
        CreateNode().TryCreateSession("operator", "operator-password", "client").Status
            .Should().Be(LoginAttemptStatus.Succeeded);
        File.ReadAllText(StorePath).Should().NotContain("operator-password").And.NotContain("wrong");
    }

    [Fact]
    public void SessionExpiry_IsEnforcedByEveryRunningNode()
    {
        var first = CreateNode();
        var token = first.CreateSession("operator", "operator-password")!;
        var second = CreateNode();
        _clock.Advance(LoginSessionService.SessionDuration);

        first.ValidateSession(token).Should().BeFalse();
        second.GetSessionProfile(token).Should().BeNull();
        CreateNode().ValidateSession(token).Should().BeFalse();
    }

    [Fact]
    public void DisabledAccount_ReenablingDoesNotResurrectAnInvalidatedToken()
    {
        var node = CreateNode();
        var token = node.CreateSession("operator", "operator-password")!;
        var accounts = Environment.GetEnvironmentVariable("MDC_USERS")!;
        _environment.Set("MDC_USERS", accounts.Replace("\"role\":\"Accounting\"", "\"role\":\"Accounting\",\"disabled\":true"));
        node.GetSessionProfile(token).Should().BeNull();

        _environment.Set("MDC_USERS", accounts);
        node.ValidateSession(token).Should().BeFalse();
        CreateNode().ValidateSession(token).Should().BeFalse();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{not-json")]
    [InlineData("{\"version\":2,\"sessions\":[],\"failedAttempts\":[]}")]
    public void InvalidAuthoritativeStore_NeverUsesCachedAuthorityOrOverwritesTheEvidence(string invalidState)
    {
        var node = CreateNode();
        var token = node.CreateSession("operator", "operator-password")!;
        File.WriteAllText(StorePath, invalidState);

        Action read = () => node.GetSessionProfile(token);
        Action login = () => node.CreateSession("operator", "operator-password");
        Action restart = () => CreateNode();
        read.Should().Throw<Exception>();
        login.Should().Throw<Exception>();
        restart.Should().Throw<Exception>();
        File.ReadAllText(StorePath).Should().Be(invalidState);
    }

    [Fact]
    public void RemovedAuthoritativeStore_DoesNotLeaveCachedSessionsValid()
    {
        var node = CreateNode();
        var token = node.CreateSession("operator", "operator-password")!;
        File.Delete(StorePath);

        node.ValidateSession(token).Should().BeFalse();
        node.CreateSession("operator", "operator-password").Should().NotBeNull();
        CreateNode().ValidateSession(token).Should().BeFalse();
    }

    [Fact]
    public void LegacyHashedArray_MigratesWithoutOrphaningExistingSessionsOrPersistingRawTokens()
    {
        Directory.CreateDirectory(_root);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        File.WriteAllText(StorePath, JsonSerializer.Serialize(new[]
        {
            new { tokenHash, username = "operator", expiresAt = _clock.GetUtcNow() + LoginSessionService.SessionDuration }
        }));

        var node = CreateNode();
        node.ValidateSession(token).Should().BeTrue();
        node.TryCreateSession("operator", "wrong", "client");
        File.ReadAllText(StorePath).Should().Contain("failedAttempts").And.NotContain(token);
        CreateNode().ValidateSession(token).Should().BeTrue();
    }

    private LoginSessionService CreateNode()
        => new(new FakeHostEnvironment("Production"), new UserProfileRegistry(), new LoginSessionStoreOptions(StorePath), _clock);

    public void Dispose()
    {
        _environment.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
