using FluentAssertions;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Tests.DataIntegration;

public sealed class CredentialStoreExtensionsTests
{
    [Fact]
    public async Task GetKeySecretPairAsync_ReadsStandardKeyNames()
    {
        var store = new FakeCredentialStore();
        await store.SetCredentialAsync("alpaca", "keyId", "key");
        await store.SetCredentialAsync("alpaca", "secretKey", "secret");

        var result = await store.GetKeySecretPairAsync("alpaca");

        result.KeyId.Should().Be("key");
        result.SecretKey.Should().Be("secret");
    }

    [Fact]
    public async Task IsProviderConfiguredAsync_UsesValidationResult()
    {
        var store = new FakeCredentialStore();
        store.Validation = new CredentialValidationResult(
            "alpaca",
            IsValid: true,
            MissingCredentials: [],
            ExpiredCredentials: [],
            ValidCredentials: ["keyId", "secretKey"],
            Message: "configured");

        var configured = await store.IsProviderConfiguredAsync("alpaca");

        configured.Should().BeTrue();
    }

    [Fact]
    public void RegisterApiKey_AddsStandardMetadata()
    {
        var store = new FakeCredentialStore();

        store.RegisterApiKey("polygon", "POLYGON_API_KEY", alternateEnvVars: ["POLYGON_KEY"]);

        store.GetProviderCredentials("polygon").Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CredentialMetadata(
                "polygon",
                "apiKey",
                CredentialType.ApiKey,
                IsRequired: true,
                "API key for polygon",
                "POLYGON_API_KEY",
                ["POLYGON_KEY"]));
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<(string Provider, string Key), string> _values = new();
        private readonly List<CredentialMetadata> _metadata = new();

        public CredentialValidationResult Validation { get; set; } = new(
            "default",
            IsValid: false,
            MissingCredentials: [],
            ExpiredCredentials: [],
            ValidCredentials: [],
            Message: "not configured");

        public Task<CredentialResult> GetCredentialAsync(string provider, string key, CancellationToken ct = default)
        {
            var result = _values.TryGetValue((provider, key), out var value)
                ? new CredentialResult(value, CredentialSource.Cache)
                : CredentialResult.NotFound;

            return Task.FromResult(result);
        }

        public Task SetCredentialAsync(string provider, string key, string value, CancellationToken ct = default)
        {
            _values[(provider, key)] = value;
            return Task.CompletedTask;
        }

        public Task<bool> HasValidCredentialAsync(string provider, string key, CancellationToken ct = default)
            => Task.FromResult(_values.ContainsKey((provider, key)));

        public Task<CredentialResult> RefreshCredentialAsync(string provider, string key, CancellationToken ct = default)
            => GetCredentialAsync(provider, key, ct);

        public Task<CredentialValidationResult> ValidateProviderCredentialsAsync(string provider, CancellationToken ct = default)
            => Task.FromResult(Validation with { Provider = provider });

        public IReadOnlyList<CredentialMetadata> GetRegisteredCredentials() => _metadata;

        public IReadOnlyList<CredentialMetadata> GetProviderCredentials(string provider)
            => _metadata.Where(x => string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase)).ToArray();

        public void RegisterCredential(CredentialMetadata metadata) => _metadata.Add(metadata);

        public void ClearCache(string? provider = null)
        {
            if (provider is null)
            {
                _values.Clear();
                return;
            }

            foreach (var key in _values.Keys.Where(x => string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _values.Remove(key);
            }
        }
    }
}
