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
            Credentials =
            [
                new CredentialWithMetadata
                {
                    Resource = $"{CredentialService.OAuthTokenResource}.alpaca",
                    IsOAuthToken = true,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(1),
                    CanAutoRefresh = true
                }
            ],
            RefreshException = new InvalidOperationException("refresh failure")
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

    private sealed class TestCredentialService : CredentialService
    {
        public IReadOnlyList<CredentialWithMetadata> Credentials { get; set; } = Array.Empty<CredentialWithMetadata>();
        public Exception? RefreshException { get; set; }

        public override IReadOnlyList<CredentialWithMetadata> GetAllCredentialsWithMetadata() => Credentials;

        public override Task<OAuthRefreshResult> RefreshOAuthTokenAsync(string providerId)
        {
            if (RefreshException is not null)
            {
                throw RefreshException;
            }

            return Task.FromResult(new OAuthRefreshResult { Success = true, NewExpiration = DateTime.UtcNow.AddHours(1) });
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
