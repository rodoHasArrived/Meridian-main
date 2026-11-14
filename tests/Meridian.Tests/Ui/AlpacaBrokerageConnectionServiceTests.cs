using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// PRD-002: Alpaca paper connect, status and revoke act only on a connection the tenant owns.
/// All fixtures are synthetic; no request leaves the process.
/// </summary>
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaBrokerageConnectionServiceTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string ConnectionA = "alpaca-paper-a";
    private const string ConnectionB = "alpaca-paper-b";
    private const string AccountA = "PA-ACCOUNT-A";
    private const string AccountB = "PA-ACCOUNT-B";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "alpaca-brokerage", Guid.NewGuid().ToString("N"));
    private readonly AlpacaEnvScope _env = AlpacaEnvScope.Clear();
    private readonly FakeAlpaca _alpaca = new();
    private readonly FileProviderCredentialStore _store;
    private readonly AlpacaBrokerageConnectionService _service;

    public AlpacaBrokerageConnectionServiceTests()
    {
        Directory.CreateDirectory(_root);
        var config = new Meridian.Application.UI.ConfigStore(Path.Combine(_root, "appsettings.json"));
        config.SaveAsync(new AppConfig(DataRoot: _root)).GetAwaiter().GetResult();
        var connections = new ProviderConnectionService(config);
        connections.UpsertForTenantAsync(new(ConnectionA, "alpaca", "Tenant A paper", ExternalAccountId: AccountA), TenantA, "paper").GetAwaiter().GetResult();
        connections.UpsertForTenantAsync(new(ConnectionB, "alpaca", "Tenant B paper", ExternalAccountId: AccountB), TenantB, "paper").GetAwaiter().GetResult();
        connections.UpsertForTenantAsync(new("alpaca-live-a", "alpaca", "Tenant A live", ExternalAccountId: "LA-ACCOUNT-A"), TenantA, "live").GetAwaiter().GetResult();
        connections.UpsertForTenantAsync(new("alpaca-paper-c1", "alpaca", "Tenant C paper 1", ExternalAccountId: "PA-ACCOUNT-C1"), "tenant-c", "paper").GetAwaiter().GetResult();
        connections.UpsertForTenantAsync(new("alpaca-paper-c2", "alpaca", "Tenant C paper 2", ExternalAccountId: "PA-ACCOUNT-C2"), "tenant-c", "paper").GetAwaiter().GetResult();
        connections.UpsertForTenantAsync(new("polygon-a", "polygon", "Tenant A Polygon", ExternalAccountId: "polygon-account"), TenantA, "production").GetAwaiter().GetResult();

        _alpaca.Accounts["key-a"] = AccountA;
        _alpaca.Accounts["key-a-rotated"] = AccountA;
        _alpaca.Accounts["key-b"] = AccountB;
        _alpaca.Accounts["live-key-a"] = "LA-ACCOUNT-A";

        _store = new FileProviderCredentialStore(_root);
        var clients = new StubHttpClientFactory(_alpaca);
        var lifecycle = new ProviderConnectionLifecycleService(
            _store,
            new ConfigStore(config.ConfigPath),
            NullLogger<ProviderConnectionLifecycleService>.Instance,
            clients);
        _service = new AlpacaBrokerageConnectionService(
            NullLogger<AlpacaBrokerageConnectionService>.Instance, lifecycle, clients);
    }

    public void Dispose()
    {
        _env.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Connect_TwoTenants_KeepPaperAccountsIsolated()
    {
        var a = await _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a", "secret-a"), actor: "operator-a");
        var b = await _service.ConnectAsync(ConnectionB, TenantB, Keys("key-b", "secret-b"), actor: "operator-b");

        a.State.Should().Be(BrokerageConnectionStateDto.Connected);
        a.ExternalAccountId.Should().Be(AccountA);
        a.MaskedKeyId.Should().NotContain("key-a");
        b.State.Should().Be(BrokerageConnectionStateDto.Connected);
        b.ExternalAccountId.Should().Be(AccountB);

        (await ReadScopedAsync(TenantA, ConnectionA, AccountA))!.Get("SecretKey").Should().Be("secret-a");
        (await ReadScopedAsync(TenantB, ConnectionB, AccountB))!.Get("SecretKey").Should().Be("secret-b");
        (await _store.ReadForProviderAsync("alpaca")).Should().BeNull("no provider-wide record may be created");
        var vaultText = await File.ReadAllTextAsync(_store.VaultPath);
        vaultText.Should().NotContain("secret-a").And.NotContain("secret-b");

        (await _service.GetStatusAsync(ConnectionA, TenantA)).ExternalAccountId.Should().Be(AccountA);
        (await _service.GetStatusAsync(ConnectionB, TenantB)).ExternalAccountId.Should().Be(AccountB);
    }

    [Fact]
    public async Task ForeignConnection_CannotBeReadConnectedOrRevoked()
    {
        await _service.ConnectAsync(ConnectionB, TenantB, Keys("key-b", "secret-b"));
        var callsBefore = _alpaca.Calls;

        Func<Task> status = () => _service.GetStatusAsync(ConnectionB, TenantA);
        Func<Task> connect = () => _service.ConnectAsync(ConnectionB, TenantA, Keys("key-a", "secret-a"));
        Func<Task> revoke = () => _service.RevokeAsync(ConnectionB, TenantA);
        Func<Task> unknown = () => _service.GetStatusAsync("no-such-connection", TenantA);
        Func<Task> otherFamily = () => _service.ConnectAsync("polygon-a", TenantA, Keys("key-a", "secret-a"));

        await status.Should().ThrowAsync<UnauthorizedAccessException>();
        await connect.Should().ThrowAsync<UnauthorizedAccessException>();
        await revoke.Should().ThrowAsync<UnauthorizedAccessException>();
        await unknown.Should().ThrowAsync<UnauthorizedAccessException>();
        await otherFamily.Should().ThrowAsync<UnauthorizedAccessException>();

        _alpaca.Calls.Should().Be(callsBefore, "a refused request must not send keys to the provider");
        (await ReadScopedAsync(TenantB, ConnectionB, AccountB))!.Get("KeyId").Should().Be("key-b");
        (await _service.GetStatusAsync(ConnectionB, TenantB)).IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_WithKeysForAnotherAccount_IsRefusedAndKeepsTheWorkingCredential()
    {
        await _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a", "secret-a"));

        // key-b is a valid Alpaca key, but for tenant B's account.
        Func<Task> wrongAccount = () => _service.ConnectAsync(ConnectionA, TenantA, Keys("key-b", "secret-b"));

        (await wrongAccount.Should().ThrowAsync<InvalidDataException>()).Which.Message.Should().NotContain("secret-b");
        var retained = await ReadScopedAsync(TenantA, ConnectionA, AccountA);
        retained!.Get("KeyId").Should().Be("key-a");
        (await _service.GetStatusAsync(ConnectionA, TenantA)).State.Should().Be(BrokerageConnectionStateDto.Connected);
    }

    [Fact]
    public async Task Connect_WithAnotherEnvironment_IsRefusedBeforeAnyProviderCall()
    {
        Func<Task> liveKeysOnPaperConnection = () => _service.ConnectAsync(
            ConnectionA, TenantA, new AlpacaBrokerageConnectionRequestDto("live-key-a", "live-secret", "live"));

        await liveKeysOnPaperConnection.Should().ThrowAsync<InvalidDataException>();
        _alpaca.Calls.Should().Be(0);
        (await ReadScopedAsync(TenantA, ConnectionA, AccountA)).Should().BeNull();
    }

    [Fact]
    public async Task Connect_LiveConnection_UsesLiveEndpointAndWarns()
    {
        var status = await _service.ConnectAsync(
            "alpaca-live-a", TenantA, new AlpacaBrokerageConnectionRequestDto("live-key-a", "live-secret", "live"));

        status.State.Should().Be(BrokerageConnectionStateDto.Connected);
        status.Environment.Should().Be("live");
        status.Warnings.Should().Contain(warning => warning.Contains("Live Alpaca endpoint", StringComparison.OrdinalIgnoreCase));
        _alpaca.RequestUris.Should().OnlyContain(uri => uri == "https://api.alpaca.markets/v2/account");
    }

    [Fact]
    public async Task Connect_PaperConnection_SendsKeysOnlyToPaperEndpoint()
    {
        // An omitted environment means the connection's own environment.
        var status = await _service.ConnectAsync(
            ConnectionA, TenantA, new AlpacaBrokerageConnectionRequestDto("key-a", "secret-a", null), actor: "operator-a");

        status.State.Should().Be(BrokerageConnectionStateDto.Connected);
        status.Environment.Should().Be("paper");
        status.VerifiedAt.Should().NotBeNull();
        _alpaca.RequestUris.Should().OnlyContain(uri => uri == "https://paper-api.alpaca.markets/v2/account");
        var stored = await ReadScopedAsync(TenantA, ConnectionA, AccountA);
        stored!.AuditMetadata["lastVerifiedBy"].Should().Be("operator-a");
    }

    [Fact]
    public async Task StaleVerification_CannotOverwriteANewerCredentialGeneration()
    {
        // The first connect's recorded verification (its second provider call) is held until a
        // rotation has saved and verified a newer generation.
        var staleVerificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleVerification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldKeyCalls = 0;
        _alpaca.BeforeResponse = async keyId =>
        {
            if (keyId == "key-a" && Interlocked.Increment(ref oldKeyCalls) == 2)
            {
                staleVerificationStarted.TrySetResult();
                await releaseStaleVerification.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
        };

        var stale = _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a", "secret-a"), actor: "operator-old");
        await staleVerificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var rotated = await _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a-rotated", "secret-rotated"), actor: "operator-new");
        rotated.State.Should().Be(BrokerageConnectionStateDto.Connected);
        var newerGeneration = (await ReadScopedAsync(TenantA, ConnectionA, AccountA))!.CredentialGeneration;

        releaseStaleVerification.SetResult();
        var staleResult = await stale;

        staleResult.IsConnected.Should().BeFalse("the stale caller's keys are no longer the stored credential");
        staleResult.LastError.Should().Contain("changed during verification");
        var current = await ReadScopedAsync(TenantA, ConnectionA, AccountA);
        current!.Get("KeyId").Should().Be("key-a-rotated");
        current.CredentialGeneration.Should().Be(newerGeneration);
        current.AuditMetadata["lastVerifiedBy"].Should().Be("operator-new");
        current.LastError.Should().BeNull();
        (await _service.GetStatusAsync(ConnectionA, TenantA)).State.Should().Be(BrokerageConnectionStateDto.Connected);
    }

    [Fact]
    public async Task Revoke_OneConnection_LeavesTheOtherUsable()
    {
        await _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a", "secret-a"));
        await _service.ConnectAsync(ConnectionB, TenantB, Keys("key-b", "secret-b"));

        var revoked = await _service.RevokeAsync(ConnectionA, TenantA, actor: "operator-a");

        revoked.IsConfigured.Should().BeFalse();
        revoked.State.Should().Be(BrokerageConnectionStateDto.NotConfigured);
        (await ReadScopedAsync(TenantA, ConnectionA, AccountA)).Should().BeNull();
        var other = await _service.GetStatusAsync(ConnectionB, TenantB);
        other.State.Should().Be(BrokerageConnectionStateDto.Connected);
        (await ReadScopedAsync(TenantB, ConnectionB, AccountB))!.Get("SecretKey").Should().Be("secret-b");
        // The surviving connection still verifies and can be reconnected.
        (await _service.ConnectAsync(ConnectionB, TenantB, Keys("key-b", "secret-b"))).IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task UnnamedConnection_ResolvesOnlyTheTenantsSingleOwnedPaperConnection()
    {
        // Tenant A owns one paper and one live connection: only the paper one is implicit.
        var a = await _service.ConnectAsync(null, TenantA, Keys("key-a", "secret-a"));
        var b = await _service.ConnectAsync(null, TenantB, Keys("key-b", "secret-b"));

        a.ExternalAccountId.Should().Be(AccountA);
        b.ExternalAccountId.Should().Be(AccountB);
        (await _service.GetStatusAsync(null, TenantA)).ExternalAccountId.Should().Be(AccountA);
        (await ReadScopedAsync(TenantA, ConnectionA, AccountA))!.Get("KeyId").Should().Be("key-a");
        (await ReadScopedAsync(TenantB, ConnectionB, AccountB))!.Get("KeyId").Should().Be("key-b");
        (await _service.RevokeAsync(null, TenantA)).IsConfigured.Should().BeFalse();
        (await _service.GetStatusAsync(null, TenantB)).IsConnected.Should().BeTrue();
    }

    [Theory]
    [InlineData("tenant-without-connections", "No owned Alpaca paper connection")]
    [InlineData("tenant-c", "more than one")]
    public async Task UnnamedConnection_WithoutExactlyOneOwnedPaperConnection_IsRefused(string tenantId, string expectedReason)
    {
        await _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a", "secret-a"));
        var callsBefore = _alpaca.Calls;

        var status = await _service.GetStatusAsync(null, tenantId);
        Func<Task> connect = () => _service.ConnectAsync(null, tenantId, Keys("key-a", "secret-a"));
        Func<Task> revoke = () => _service.RevokeAsync(null, tenantId);

        status.IsConfigured.Should().BeFalse();
        status.ExternalAccountId.Should().BeNull();
        status.MaskedKeyId.Should().BeNull("another tenant's credential must not be described");
        status.Warnings.Should().ContainSingle().Which.Should().Contain(expectedReason);
        (await connect.Should().ThrowAsync<InvalidDataException>()).Which.Message.Should().Contain(expectedReason);
        await revoke.Should().ThrowAsync<InvalidDataException>();
        _alpaca.Calls.Should().Be(callsBefore);
        (await _service.GetStatusAsync(ConnectionA, TenantA)).IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_WithMissingKeys_ReportsErrorWithoutCallingProvider()
    {
        var status = await _service.ConnectAsync(ConnectionA, TenantA, new AlpacaBrokerageConnectionRequestDto(" ", null, "paper"));

        status.IsConfigured.Should().BeFalse();
        status.LastError.Should().Contain("required");
        _alpaca.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("transport")]
    [InlineData("json")]
    [InlineData("missing-account")]
    public async Task Connect_WhenVerificationFails_StoresNothingAndDoesNotExposeProviderSecrets(string failureKind)
    {
        const string echoed = "provider-echoed-secret";
        _alpaca.Failure = (failureKind, echoed);

        var failed = await _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a", "super-secret-value"), actor: "operator-a");

        failed.IsConnected.Should().BeFalse();
        failed.IsConfigured.Should().BeFalse("keys that were never verified are not stored");
        failed.LastError.Should().StartWith("Alpaca account verification failed");
        failed.LastError.Should().NotContain(echoed).And.NotContain("super-secret-value");
        failed.Warnings.Should().NotContain(warning => warning.Contains(echoed, StringComparison.Ordinal));
        (await ReadScopedAsync(TenantA, ConnectionA, AccountA)).Should().BeNull();

        _alpaca.Failure = null;
        var recovered = await _service.ConnectAsync(ConnectionA, TenantA, Keys("key-a", "secret-a"), actor: "operator-a");
        recovered.State.Should().Be(BrokerageConnectionStateDto.Connected);
    }

    private static AlpacaBrokerageConnectionRequestDto Keys(string keyId, string secretKey)
        => new(keyId, secretKey, "paper");

    private Task<ProviderCredentialReadResult?> ReadScopedAsync(string tenantId, string connectionId, string accountId, string environment = "paper")
        => _store.ReadScopedAsync("alpaca", new ProviderCredentialScope(tenantId, connectionId, accountId, environment));

    /// <summary>Synthetic Alpaca /v2/account: each key id maps to one account; unknown keys get 401.</summary>
    private sealed class FakeAlpaca : HttpMessageHandler
    {
        private int _calls;
        public ConcurrentDictionary<string, string> Accounts { get; } = new(StringComparer.Ordinal);
        public ConcurrentQueue<string> RequestUris { get; } = new();
        public Func<string, Task>? BeforeResponse { get; set; }
        public (string Kind, string Secret)? Failure { get; set; }
        public int Calls => Volatile.Read(ref _calls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            RequestUris.Enqueue(request.RequestUri!.ToString());
            var keyId = request.Headers.GetValues("APCA-API-KEY-ID").Single();
            request.Headers.GetValues("APCA-API-SECRET-KEY").Should().ContainSingle();
            if (BeforeResponse is { } before)
                await before(keyId);

            if (Failure is { } failure)
            {
                if (failure.Kind == "transport")
                    throw new HttpRequestException(failure.Secret, new IOException(failure.Secret));
                return new HttpResponseMessage(failure.Kind == "http" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
                {
                    ReasonPhrase = failure.Secret,
                    Content = new StringContent(
                        failure.Kind == "json" ? "{\"id\":{\"" + failure.Secret + "\":1}}" : "{}", Encoding.UTF8, "application/json")
                };
            }

            if (!Accounts.TryGetValue(keyId, out var account))
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id = "alpaca-account-id", account_number = account }), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class AlpacaEnvScope : IDisposable
    {
        private static readonly string[] Names =
        [
            AlpacaCredentialEnvironment.KeyIdName,
            AlpacaCredentialEnvironment.SecretKeyName,
            AlpacaCredentialEnvironment.TradingEnvironmentName,
            "APCA_API_KEY_ID",
            "APCA_API_SECRET_KEY",
            "ALPACA__KEYID",
            "ALPACA__SECRETKEY",
            "ALPACA_BROKERAGE_CONNECTED_AT",
            "ALPACA_BROKERAGE_VERIFIED_AT",
            "ALPACA_BROKERAGE_ACCOUNT_ID",
            "ALPACA_BROKERAGE_LAST_ERROR",
            "DOTNET_ENVIRONMENT"
        ];

        private readonly Dictionary<string, (string? Process, string? User)> _values;

        private AlpacaEnvScope()
        {
            _values = Names.ToDictionary(static name => name, static name => (
                Process: Environment.GetEnvironmentVariable(name),
                User: ReadUserEnvironment(name)));
        }

        public static AlpacaEnvScope Clear()
        {
            var scope = new AlpacaEnvScope();
            foreach (var name in Names)
            {
                Environment.SetEnvironmentVariable(name, null);
                TrySetUserEnvironment(name, null);
            }

            return scope;
        }

        public void Dispose()
        {
            foreach (var (name, value) in _values)
            {
                Environment.SetEnvironmentVariable(name, value.Process);
                TrySetUserEnvironment(name, value.User);
            }
        }

        private static string? ReadUserEnvironment(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            }
            catch (PlatformNotSupportedException)
            {
                return null;
            }
        }

        private static void TrySetUserEnvironment(string name, string? value)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
            }
            catch (PlatformNotSupportedException)
            {
                // Process-level restore is enough where user env storage is not available.
            }
        }
    }
}
