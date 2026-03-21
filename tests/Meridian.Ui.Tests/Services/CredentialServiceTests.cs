using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="CredentialService"/> — singleton lifecycle, credential retrieval,
/// metadata operations, OAuth refresh, event raising, and model validation.
/// </summary>
public sealed class CredentialServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        CredentialService.Instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = CredentialService.Instance;
        var b = CredentialService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Instance_ThreadSafety_ShouldReturnSameInstance()
    {
        CredentialService? i1 = null, i2 = null;
        var t1 = Task.Run(() => i1 = CredentialService.Instance);
        var t2 = Task.Run(() => i2 = CredentialService.Instance);
        Task.WaitAll(t1, t2);

        i1.Should().NotBeNull();
        i1.Should().BeSameAs(i2);
    }

    // ── OAuthTokenResource constant ──────────────────────────────────

    [Fact]
    public void OAuthTokenResource_ShouldBeDefined()
    {
        CredentialService.OAuthTokenResource.Should().Be("oauth_token");
    }

    // ── GetAllCredentialsWithMetadata ─────────────────────────────────

    [Fact]
    public void GetAllCredentialsWithMetadata_Default_ShouldReturnEmptyList()
    {
        var svc = CredentialService.Instance;
        var result = svc.GetAllCredentialsWithMetadata();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    // ── RefreshOAuthTokenAsync ────────────────────────────────────────

    [Fact]
    public async Task RefreshOAuthTokenAsync_Default_ShouldReturnFailure()
    {
        var svc = CredentialService.Instance;
        var result = await svc.RefreshOAuthTokenAsync("test-provider");

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshOAuthTokenAsync_WithEmptyProvider_ShouldReturnFailure()
    {
        var svc = CredentialService.Instance;
        var result = await svc.RefreshOAuthTokenAsync(string.Empty);

        result.Success.Should().BeFalse();
    }

    // ── UpdateMetadataAsync ──────────────────────────────────────────

    [Fact]
    public async Task UpdateMetadataAsync_Default_ShouldCompleteWithoutException()
    {
        var svc = CredentialService.Instance;
        var act = async () => await svc.UpdateMetadataAsync("test", m => m.AutoRefreshEnabled = true);
        await act.Should().NotThrowAsync();
    }

    // ── GetMetadata ──────────────────────────────────────────────────

    [Fact]
    public void GetMetadata_Default_ShouldReturnNull()
    {
        var svc = CredentialService.Instance;
        var result = svc.GetMetadata("nonexistent");
        result.Should().BeNull();
    }

    // ── CredentialExpiring event ──────────────────────────────────────

    [Fact]
    public void CredentialExpiring_ShouldSupportSubscription()
    {
        var svc = new TestCredentialService();
        CredentialExpirationEventArgs? received = null;
        svc.CredentialExpiring += (_, args) => received = args;

        svc.RaiseExpiring("test-resource", DateTime.UtcNow.AddHours(1));

        received.Should().NotBeNull();
        received!.Resource.Should().Be("test-resource");
    }

    [Fact]
    public void CredentialExpiring_WithMultipleSubscribers_ShouldNotifyAll()
    {
        var svc = new TestCredentialService();
        var callCount = 0;
        svc.CredentialExpiring += (_, _) => Interlocked.Increment(ref callCount);
        svc.CredentialExpiring += (_, _) => Interlocked.Increment(ref callCount);

        svc.RaiseExpiring("key", DateTime.UtcNow);

        callCount.Should().Be(2);
    }

    // ── Model tests: CredentialExpirationEventArgs ────────────────────

    [Fact]
    public void CredentialExpirationEventArgs_ShouldStoreValues()
    {
        var now = DateTime.UtcNow;
        var args = new CredentialExpirationEventArgs("my-resource", now);

        args.Resource.Should().Be("my-resource");
        args.ExpiresAt.Should().Be(now);
    }

    // ── Model tests: CredentialWithMetadata ───────────────────────────

    [Fact]
    public void CredentialWithMetadata_ShouldHaveDefaultValues()
    {
        var cred = new CredentialWithMetadata();

        cred.Resource.Should().BeEmpty();
        cred.IsOAuthToken.Should().BeFalse();
        cred.ExpiresAt.Should().BeNull();
        cred.CanAutoRefresh.Should().BeFalse();
        cred.AutoRefreshEnabled.Should().BeFalse();
    }

    [Fact]
    public void CredentialWithMetadata_ShouldAcceptValues()
    {
        var expires = DateTime.UtcNow.AddDays(30);
        var cred = new CredentialWithMetadata
        {
            Resource = "alpaca-key",
            IsOAuthToken = true,
            ExpiresAt = expires,
            CanAutoRefresh = true,
            AutoRefreshEnabled = true
        };

        cred.Resource.Should().Be("alpaca-key");
        cred.IsOAuthToken.Should().BeTrue();
        cred.ExpiresAt.Should().Be(expires);
        cred.CanAutoRefresh.Should().BeTrue();
        cred.AutoRefreshEnabled.Should().BeTrue();
    }

    // ── Model tests: OAuthRefreshResult ──────────────────────────────

    [Fact]
    public void OAuthRefreshResult_ShouldHaveDefaultValues()
    {
        var result = new OAuthRefreshResult();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
        result.NewExpiration.Should().BeNull();
    }

    [Fact]
    public void OAuthRefreshResult_SuccessCase_ShouldStoreValues()
    {
        var expiry = DateTime.UtcNow.AddHours(1);
        var result = new OAuthRefreshResult
        {
            Success = true,
            NewExpiration = expiry
        };

        result.Success.Should().BeTrue();
        result.NewExpiration.Should().Be(expiry);
        result.ErrorMessage.Should().BeNull();
    }

    // ── Model tests: CredentialMetadataUpdate ────────────────────────

    [Fact]
    public void CredentialMetadataUpdate_ShouldHaveDefaultValues()
    {
        var update = new CredentialMetadataUpdate();
        update.AutoRefreshEnabled.Should().BeFalse();
    }

    // ── Model tests: CredentialMetadataInfo ──────────────────────────

    [Fact]
    public void CredentialMetadataInfo_ShouldHaveDefaultValues()
    {
        var info = new CredentialMetadataInfo();
        info.AutoRefreshEnabled.Should().BeFalse();
        info.ExpiresAt.Should().BeNull();
    }

    // ── Helper ───────────────────────────────────────────────────────

    private sealed class TestCredentialService : CredentialService
    {
        public void RaiseExpiring(string resource, DateTime expiresAt)
            => OnCredentialExpiring(new CredentialExpirationEventArgs(resource, expiresAt));
    }
}
