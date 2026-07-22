using System.Net;
using FluentAssertions;
using Meridian.Identity;
using Meridian.Storage;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Identity;

[Collection("IdentityEnvironment")]
public sealed class InitialAccountBootstrapServiceTests
{
    [Fact]
    public async Task CreateAsync_WithLoopbackOneUseToken_CreatesAuditedAccountAndSession()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CreateAsync_WithLoopbackOneUseToken_CreatesAuditedAccountAndSession));
        using var environment = new EnvironmentVariableScope().Set(InitialAccountBootstrapService.TokenEnvironmentVariable, "one-use-token");
        var store = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });
        var registry = new UserProfileRegistry(null, store);
        var sessions = new LoginSessionService(new FakeHostEnvironment("Production"), registry);
        var service = new InitialAccountBootstrapService(store, sessions);

        var token = await service.CreateAsync(IPAddress.Loopback, "one-use-token", "owner", "twelve-characters", CancellationToken.None);

        token.Should().NotBeNull();
        sessions.ValidateSession(token!).Should().BeTrue();
        store.LoadAccounts().Should().ContainSingle(account => account.Username == "owner");
        Environment.GetEnvironmentVariable(InitialAccountBootstrapService.TokenEnvironmentVariable).Should().BeNull();
        (await service.CreateAsync(IPAddress.Loopback, "one-use-token", "other", "twelve-characters", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_FromNonLoopback_DoesNotCreateAccount()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CreateAsync_FromNonLoopback_DoesNotCreateAccount));
        using var environment = new EnvironmentVariableScope().Set(InitialAccountBootstrapService.TokenEnvironmentVariable, "token");
        var store = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });
        var sessions = new LoginSessionService(new FakeHostEnvironment("Production"), new UserProfileRegistry(null, store));
        var service = new InitialAccountBootstrapService(store, sessions);

        var result = await service.CreateAsync(IPAddress.Parse("192.0.2.1"), "token", "owner", "twelve-characters", CancellationToken.None);

        result.Should().BeNull();
        store.HasAccounts.Should().BeFalse();
    }
}
