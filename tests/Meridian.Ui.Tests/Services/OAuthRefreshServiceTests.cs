using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Ui.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Tests.Services;

public sealed class OAuthRefreshServiceTests
{
    [Fact]
    public async Task SafeCheckAndRefreshTokensAsync_WhenWrapperThrows_ShouldEmitFailureEventAndLog()
    {
        var credentialService = new TestCredentialService
        {
            CredentialsException = new InvalidOperationException("credential enumeration failed")
        };

        var logger = new TestLogger<OAuthRefreshService>();
        var service = new OAuthRefreshService(credentialService, logger);
        OAuthWrapperFailureEventArgs? evt = null;
        service.WrapperOperationFailed += (_, e) => evt = e;

        await service.InvokeSafeCheckAndRefreshTokensForTestsAsync();

        service.WrapperFailureCount.Should().Be(1);
        evt.Should().NotBeNull();
        evt!.OperationName.Should().Be("CheckAndRefreshTokensAsync");
        evt.Exception.Should().BeOfType<InvalidOperationException>();
        evt.EmittedStructuredLog.Should().BeTrue();
        logger.Entries.Should().ContainSingle(x => x.LogLevel == LogLevel.Error && x.Message.Contains("CheckAndRefreshTokensAsync"));
    }

    [Fact]
    public async Task Start_ObservesInitialCheckFailureAndCanRestart()
    {
        var credentialService = new TestCredentialService
        {
            CredentialsException = new InvalidOperationException("credential enumeration failed")
        };
        using var service = new OAuthRefreshService(credentialService);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var failure = new TaskCompletionSource<OAuthWrapperFailureEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.WrapperOperationFailed += (_, args) => failure.TrySetResult(args);

        service.Start();
        var observed = await failure.Task.WaitAsync(timeout.Token);
        await service.StopAsync().WaitAsync(timeout.Token);

        observed.OperationName.Should().Be("CheckAndRefreshTokensAsync");
        service.WrapperFailureCount.Should().Be(1);

        credentialService.CredentialsException = null;
        credentialService.Credentials = [ExpiredCredential("tradestation")];
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TokenRefreshed += (_, _) => refreshed.TrySetResult();
        service.Start();
        await refreshed.Task.WaitAsync(timeout.Token);
        await service.StopAsync().WaitAsync(timeout.Token);

        credentialService.RefreshedProviderIds.Should().Equal("tradestation");
    }

    [Fact]
    public async Task ScheduledAndManualTriggersJoinOneRefreshPerProvider()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var credentialService = new TestCredentialService
        {
            Credentials = [ExpiredCredential("tradestation")],
            Refresh = async (provider, ct) =>
            {
                if (provider == "tradestation")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }

                return new OAuthRefreshResult { Success = true };
            }
        };
        using var service = new OAuthRefreshService(credentialService);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var published = new ConcurrentQueue<string>();
        service.TokenRefreshed += (_, args) => published.Enqueue(args.ProviderId);

        service.Start();
        await entered.Task.WaitAsync(timeout.Token);
        var pendingCheck = service.InvokeSafeCheckAndRefreshTokensForTestsAsync(timeout.Token);
        var manual = service.RefreshTokenAsync("tradestation", timeout.Token);
        var abandonedJoin = service.RefreshTokenAsync("tradestation", cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandonedJoin);
        (await service.RefreshTokenAsync("another-provider", timeout.Token)).Should().BeTrue();
        credentialService.RefreshedProviderIds.Should().Equal("tradestation", "another-provider");

        release.TrySetResult();
        (await manual).Should().BeTrue();
        await pendingCheck;
        await service.StopAsync().WaitAsync(timeout.Token);

        credentialService.RefreshedProviderIds.Should().Equal("tradestation", "another-provider");
        published.Should().Equal("another-provider", "tradestation");
    }

    [Fact]
    public async Task StopAsync_CancelsScheduledCredentialRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var credentialService = new TestCredentialService
        {
            Credentials = [ExpiredCredential("tradestation")],
            Refresh = async (_, ct) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return new OAuthRefreshResult { Success = true };
                }
                finally
                {
                    cancellationObserved.TrySetResult();
                }
            }
        };
        using var service = new OAuthRefreshService(credentialService);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        service.Start();
        await entered.Task.WaitAsync(timeout.Token);
        await service.StopAsync().WaitAsync(timeout.Token);

        cancellationObserved.Task.IsCompletedSuccessfully.Should().BeTrue();
        service.WrapperFailureCount.Should().Be(0);
    }

    [Fact]
    public async Task SafeCheckExpiringTokensAsync_WhenWarningSubscriberThrows_ShouldEmitThrottledWrapperEvents()
    {
        var credentialService = new TestCredentialService
        {
            Credentials =
            [
                new CredentialWithMetadata
                {
                    Resource = $"{CredentialService.OAuthTokenResource}.ibkr",
                    IsOAuthToken = true,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                    CanAutoRefresh = false
                }
            ]
        };

        var logger = new TestLogger<OAuthRefreshService>();
        var service = new OAuthRefreshService(credentialService, logger);
        var events = new List<OAuthWrapperFailureEventArgs>();
        service.WrapperOperationFailed += (_, e) => events.Add(e);
        service.TokenExpirationWarning += (_, _) => throw new InvalidOperationException("observer failed");

        await service.InvokeSafeCheckExpiringTokensForTestsAsync();
        await service.InvokeSafeCheckExpiringTokensForTestsAsync();

        service.WrapperFailureCount.Should().Be(2);
        events.Should().HaveCount(2);
        events[0].EmittedStructuredLog.Should().BeTrue();
        events[1].EmittedStructuredLog.Should().BeFalse();
        logger.Entries.Count(x => x.LogLevel == LogLevel.Error && x.Message.Contains("CheckExpiringTokensAsync")).Should().Be(1);
    }

    [Fact]
    public async Task SafeCheckAndRefreshTokensAsync_WhenWrapperFailureHandlerThrows_ShouldNotEscapeSafeWrapper()
    {
        var credentialService = new TestCredentialService
        {
            CredentialsException = new InvalidOperationException("credential enumeration failed")
        };

        var logger = new TestLogger<OAuthRefreshService>();
        var service = new OAuthRefreshService(credentialService, logger);
        service.WrapperOperationFailed += (_, _) => throw new InvalidOperationException("event subscriber failed");

        var act = () => service.InvokeSafeCheckAndRefreshTokensForTestsAsync();

        await act.Should().NotThrowAsync();
        logger.Entries.Should().Contain(x => x.LogLevel == LogLevel.Error && x.Message.Contains("event handler threw"));
    }

    [Fact]
    public void GetTokenStatuses_WithHostPrefix_ShouldMapProviderIdAndSkipRefreshTokenEntries()
    {
        var expiresAt = DateTime.UtcNow.AddHours(2);
        var credentialService = new TestCredentialService
        {
            Prefix = "Meridian.OAuth",
            Credentials =
            [
                new CredentialWithMetadata { Resource = "Meridian.OAuth.ibkr", IsOAuthToken = true, ExpiresAt = expiresAt, CanAutoRefresh = true },
                new CredentialWithMetadata { Resource = "Meridian.OAuth.ibkr.refresh", IsOAuthToken = true },
                new CredentialWithMetadata { Resource = "oauth_token.other", IsOAuthToken = true }
            ]
        };

        using var service = new OAuthRefreshService(credentialService);

        var statuses = service.GetTokenStatuses();

        statuses.Should().ContainSingle();
        statuses[0].ProviderId.Should().Be("ibkr");
        statuses[0].ExpiresAt.Should().Be(expiresAt);
        statuses[0].CanAutoRefresh.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAndRefreshTokens_WithHostPrefix_ShouldRefreshByProviderIdOnly()
    {
        var credentialService = new TestCredentialService
        {
            Prefix = "Meridian.OAuth",
            Credentials =
            [
                new CredentialWithMetadata { Resource = "Meridian.OAuth.ibkr", IsOAuthToken = true, ExpiresAt = DateTime.UtcNow.AddMinutes(2), CanAutoRefresh = true },
                new CredentialWithMetadata { Resource = "Meridian.OAuth.ibkr.refresh", IsOAuthToken = true, ExpiresAt = DateTime.UtcNow.AddMinutes(2), CanAutoRefresh = true }
            ]
        };

        using var service = new OAuthRefreshService(credentialService);
        var refreshed = new List<string>();
        service.TokenRefreshed += (_, e) => refreshed.Add(e.ProviderId);

        await service.InvokeSafeCheckAndRefreshTokensForTestsAsync();

        credentialService.RefreshedProviderIds.Should().Equal("ibkr");
        refreshed.Should().Equal("ibkr");
    }

    [Fact]
    public async Task SetAutoRefreshAsync_ShouldAddressHostPrefixedResource()
    {
        var credentialService = new TestCredentialService { Prefix = "Meridian.OAuth" };
        using var service = new OAuthRefreshService(credentialService);

        await service.SetAutoRefreshAsync("ibkr", enabled: true);

        credentialService.UpdatedResources.Should().Equal("Meridian.OAuth.ibkr");
    }

    private static CredentialWithMetadata ExpiredCredential(string providerId) => new()
    {
        Resource = $"{CredentialService.OAuthTokenResource}.{providerId}",
        IsOAuthToken = true,
        ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
        CanAutoRefresh = true
    };

    private sealed class TestCredentialService : CredentialService
    {
        public string? Prefix { get; set; }
        public ConcurrentQueue<string> RefreshedProviderIds { get; } = new();
        public List<string> UpdatedResources { get; } = [];

        public override string OAuthTokenResourcePrefix => Prefix ?? base.OAuthTokenResourcePrefix;

        public override Task UpdateMetadataAsync(string resource, Action<CredentialMetadataUpdate> updateAction, CancellationToken ct = default)
        {
            UpdatedResources.Add(resource);
            return Task.CompletedTask;
        }

        public IReadOnlyList<CredentialWithMetadata> Credentials { get; set; } = Array.Empty<CredentialWithMetadata>();
        public Exception? CredentialsException { get; set; }
        public Exception? RefreshException { get; set; }
        public Func<string, CancellationToken, Task<OAuthRefreshResult>>? Refresh { get; init; }

        public override IReadOnlyList<CredentialWithMetadata> GetAllCredentialsWithMetadata()
        {
            if (CredentialsException is not null)
            {
                throw CredentialsException;
            }

            return Credentials;
        }

        public override Task<OAuthRefreshResult> RefreshOAuthTokenAsync(string providerId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (RefreshException is not null)
            {
                throw RefreshException;
            }

            RefreshedProviderIds.Enqueue(providerId);
            return Refresh?.Invoke(providerId, ct)
                ?? Task.FromResult(new OAuthRefreshResult { Success = true, NewExpiration = DateTime.UtcNow.AddHours(1) });
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
