using System.Net;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class OAuthTokenRefreshFailureTests
{
    [Fact]
    public async Task MalformedPersistedToken_DoesNotExposeJsonPathInLogs()
    {
        const string secret = "secret-in-malformed-token-key";
        var root = Path.Combine(Path.GetTempPath(), "meridian-oauth-errors", Guid.NewGuid().ToString("N"));
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".mdc"));
            await File.WriteAllTextAsync(Path.Combine(root, ".mdc", "oauth_tokens.json"),
                "{\"" + secret + "\":{\"AccessToken\":{}}}");

            await using var service = new OAuthTokenRefreshService(root, logger: logger);
            var initialize = () => service.InitializeAsync();
            var failure = (await initialize.Should().ThrowAsync<InvalidOperationException>()).Which;
            failure.ToString().Should().NotContain(secret);
            File.Exists(Path.Combine(root, ".mdc", "oauth_tokens.json")).Should().BeTrue();
            sink.Events.Should().Contain(entry => entry.Level == LogEventLevel.Warning);
            sink.Events.Should().OnlyContain(entry => entry.Exception == null);
            string.Join("\n", sink.Events.Select(entry => entry.RenderMessage())).Should().NotContain(secret);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RefreshFailure_DoesNotExposeProviderSecrets_AndCanRetry(bool transportFailure, bool scoped)
    {
        const string secret = "provider-echoed-bearer-secret";
        var root = Path.Combine(Path.GetTempPath(), "meridian-oauth-errors", Guid.NewGuid().ToString("N"));
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        using var handler = new RetryHandler(transportFailure, secret);
        using var client = new HttpClient(handler);
        try
        {
            var scope = scoped ? new ProviderCredentialScope("tenant", "connection", "account-a", "paper") : null;
            var otherScope = new ProviderCredentialScope("tenant", "connection", "account-b", "paper");
            var vault = new FileProviderCredentialStore(root);
            await vault.SaveScopedOAuthTokenAsync("alpaca", new OAuthToken("other-access", "Bearer", DateTimeOffset.UtcNow.AddHours(1)), otherScope);
            await using var service = new OAuthTokenRefreshService(root, httpClient: client, logger: logger, ownershipScope: scope);
            service.RegisterProvider(new OAuthProviderConfig("alpaca", "client",
                ClientSecret: secret, TokenEndpoint: "https://provider.example/token"));
            var original = new OAuthToken("original-access", "Bearer", DateTimeOffset.UtcNow.AddHours(1),
                RefreshToken: secret);
            await service.StoreTokenAsync("alpaca", original);
            var failures = new List<string>();
            service.OnRefreshFailed += (_, error) => failures.Add(error);

            var failed = await service.RefreshTokenAsync("alpaca");

            failed.Success.Should().BeFalse();
            failed.Token.Should().BeNull();
            failed.Error.Should().Be(transportFailure ? "Token refresh failed." : "Token refresh failed: HTTP 400.");
            failures.Should().ContainSingle().Which.Should().Be(failed.Error);
            service.GetToken("alpaca").Should().Be(original);
            sink.Events.Should().OnlyContain(entry => entry.Exception == null);
            string.Join("\n", sink.Events.Select(entry => entry.RenderMessage())).Should().NotContain(secret);

            var recovered = await service.RefreshTokenAsync("alpaca");

            recovered.Success.Should().BeTrue();
            recovered.Token!.AccessToken.Should().Be("replacement-access");
            recovered.Token.RefreshToken.Should().Be("replacement-refresh");
            handler.Calls.Should().Be(2);
            failures.Should().ContainSingle();
            var persisted = scoped ? await vault.ReadScopedOAuthTokensAsync(scope!) : await vault.ReadOAuthTokensAsync();
            persisted["alpaca"].RefreshToken.Should().Be("replacement-refresh");
            (await vault.ReadScopedOAuthTokensAsync(otherScope))["alpaca"].AccessToken.Should().Be("other-access");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RotatedToken_PersistenceFailureIsNotAcknowledgedAndCanBeRetainedForRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-oauth-errors", Guid.NewGuid().ToString("N"));
        using var handler = new RetryHandler(false, "provider-secret");
        using var client = new HttpClient(handler);
        try
        {
            var vault = new RejectingVault(new FileProviderCredentialStore(root));
            await using var service = new OAuthTokenRefreshService(root, httpClient: client, vault: vault);
            service.RegisterProvider(new OAuthProviderConfig("provider", "client", TokenEndpoint: "https://provider.example/token"));
            var original = new OAuthToken("original-access", "Bearer", DateTimeOffset.UtcNow.AddHours(1), "original-refresh");
            await service.StoreTokenAsync("provider", original);
            // Consume the handler's HTTP failure before the provider performs its real rotation.
            (await service.RefreshTokenAsync("provider")).Success.Should().BeFalse();
            var successes = 0;
            service.OnTokenRefreshed += (_, _) => successes++;
            vault.RejectWrites = true;

            var failed = await service.RefreshTokenAsync("provider");

            failed.Success.Should().BeFalse();
            failed.Token.Should().BeNull();
            failed.Error.Should().Be("Token refresh failed.");
            successes.Should().Be(0);
            service.GetToken("provider")!.RefreshToken.Should().Be("replacement-refresh");
            (await vault.ReadOAuthTokensAsync())["provider"].RefreshToken.Should().Be("original-refresh");

            vault.RejectWrites = false;
            await service.StoreTokenAsync("provider", service.GetToken("provider")!);
            var reopened = new FileProviderCredentialStore(root);
            (await reopened.ReadOAuthTokensAsync())["provider"].RefreshToken.Should().Be("replacement-refresh");
            handler.Calls.Should().Be(2, "persistence recovery must not need another remote token rotation");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedRemoteRotation_CommitsReplacementDespiteCallerCancellation(bool scoped)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-oauth-errors", Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        using var handler = new RetryHandler(false, "provider-secret");
        using var client = new HttpClient(handler);
        try
        {
            var vault = new CancelOnRotationVault(new FileProviderCredentialStore(root), cancellation);
            var scope = scoped ? new ProviderCredentialScope("tenant", "connection", "account", "paper") : null;
            await using var service = new OAuthTokenRefreshService(root, httpClient: client, vault: vault, ownershipScope: scope);
            service.RegisterProvider(new OAuthProviderConfig("provider", "client", TokenEndpoint: "https://provider.example/token"));
            await service.StoreTokenAsync("provider", new OAuthToken("original-access", "Bearer",
                DateTimeOffset.UtcNow.AddHours(1), "original-refresh"));
            (await service.RefreshTokenAsync("provider")).Success.Should().BeFalse();

            var refreshed = await service.RefreshTokenAsync("provider", cancellation.Token);

            cancellation.IsCancellationRequested.Should().BeTrue();
            vault.RotationCommitWasCancelable.Should().BeFalse();
            refreshed.Success.Should().BeTrue();
            var persisted = scoped ? await vault.ReadScopedOAuthTokensAsync(scope!) : await vault.ReadOAuthTokensAsync();
            persisted["provider"].RefreshToken.Should().Be("replacement-refresh");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConstructionOnUiContext_ReturnsBeforeAsynchronousVaultInitialization()
    {
        var constructed = new TaskCompletionSource<OAuthTokenRefreshService>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new NonPumpingContext();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            { constructed.SetResult(new OAuthTokenRefreshService(Path.Combine(Path.GetTempPath(), "meridian-oauth-context", Guid.NewGuid().ToString("N")), vault: new YieldingVault())); }
            catch (Exception ex) { constructed.SetException(ex); }
        })
        { IsBackground = true };
        thread.Start();

        await using var service = await constructed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5));

        context.PostCount.Should().Be(0, "constructing the service must not begin or block UI-bound vault work");
        service.GetAllTokens().Should().BeEmpty();
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref PostCount);
    }

    private sealed class YieldingVault : IOAuthTokenVault
    {
        public async Task<IReadOnlyDictionary<string, OAuthToken>> ReadOAuthTokensAsync(CancellationToken ct = default)
        {
            await Task.Yield();
            return new Dictionary<string, OAuthToken>();
        }
        public Task ImportOAuthTokensAsync(IReadOnlyDictionary<string, OAuthToken> tokens, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task SaveOAuthTokenAsync(string providerName, OAuthToken? token, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class CancelOnRotationVault(IOAuthTokenVault inner, CancellationTokenSource cancellation) : IOAuthTokenVault, IScopedOAuthTokenVault
    {
        public bool RotationCommitWasCancelable { get; private set; }
        public Task<IReadOnlyDictionary<string, OAuthToken>> ReadOAuthTokensAsync(CancellationToken ct = default)
            => inner.ReadOAuthTokensAsync(ct);
        public Task ImportOAuthTokensAsync(IReadOnlyDictionary<string, OAuthToken> tokens, CancellationToken ct = default)
            => inner.ImportOAuthTokensAsync(tokens, ct);
        public Task<IReadOnlyDictionary<string, OAuthToken>> ReadScopedOAuthTokensAsync(ProviderCredentialScope scope, CancellationToken ct = default)
            => ((IScopedOAuthTokenVault)inner).ReadScopedOAuthTokensAsync(scope, ct);
        public Task SaveScopedOAuthTokenAsync(string providerName, OAuthToken? token, ProviderCredentialScope scope, CancellationToken ct = default)
        {
            CancelAfterRotation(token, ct);
            return ((IScopedOAuthTokenVault)inner).SaveScopedOAuthTokenAsync(providerName, token, scope, ct);
        }
        public Task SaveOAuthTokenAsync(string providerName, OAuthToken? token, CancellationToken ct = default)
        {
            CancelAfterRotation(token, ct);
            return inner.SaveOAuthTokenAsync(providerName, token, ct);
        }
        private void CancelAfterRotation(OAuthToken? token, CancellationToken ct)
        {
            if (token?.RefreshToken == "replacement-refresh")
            {
                RotationCommitWasCancelable = ct.CanBeCanceled;
                cancellation.Cancel();
            }
        }
    }

    private sealed class RejectingVault(IOAuthTokenVault inner) : IOAuthTokenVault
    {
        public bool RejectWrites { get; set; }
        public Task<IReadOnlyDictionary<string, OAuthToken>> ReadOAuthTokensAsync(CancellationToken ct = default)
            => inner.ReadOAuthTokensAsync(ct);
        public Task ImportOAuthTokensAsync(IReadOnlyDictionary<string, OAuthToken> tokens, CancellationToken ct = default)
            => inner.ImportOAuthTokensAsync(tokens, ct);
        public Task SaveOAuthTokenAsync(string providerName, OAuthToken? token, CancellationToken ct = default)
            => RejectWrites ? Task.FromException(new IOException("secret-bearing storage failure"))
                : inner.SaveOAuthTokenAsync(providerName, token, ct);
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class RetryHandler(bool transportFailure, string secret) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                if (transportFailure)
                    throw new HttpRequestException(secret, new InvalidOperationException(secret));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    ReasonPhrase = secret,
                    Content = new StringContent(secret)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"replacement-access","refresh_token":"replacement-refresh","expires_in":3600}""")
            });
        }
    }
}
