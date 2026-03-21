using FluentAssertions;
using Meridian.Infrastructure.DataSources;
using Xunit;

namespace Meridian.Tests.Infrastructure.DataSources;

public class CredentialConfigTests
{
    [Fact]
    public void Validate_VaultSourceWithoutProvider_ShouldReturnError()
    {
        var config = new CredentialConfig
        {
            Source = "Vault",
            VaultPath = "marketdata/alpaca/prod"
        };

        var result = config.Validate(requireApiKey: true);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("VaultProvider must be"));
    }

    [Fact]
    public void ResolveKeyId_VaultBridgeEnvVar_ShouldReturnValue()
    {
        const string envName = "MDC_VAULT__MARKETDATA_ALPACA_PROD__KEYID";
        Environment.SetEnvironmentVariable(envName, "vault-key-id");

        try
        {
            var config = new CredentialConfig
            {
                Source = "Vault",
                VaultProvider = "AwsSecretsManager",
                VaultPath = "marketdata/alpaca/prod"
            };

            config.ResolveKeyId().Should().Be("vault-key-id");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public void ResolveSecretKey_VaultBridgeJsonPayload_ShouldReturnValue()
    {
        const string envName = "MDC_VAULT_JSON__MARKETDATA_ALPACA_PROD";
        Environment.SetEnvironmentVariable(envName, "{\"secretKey\":\"vault-secret\"}");

        try
        {
            var config = new CredentialConfig
            {
                Source = "Vault",
                VaultProvider = "AzureKeyVault",
                VaultPath = "marketdata/alpaca/prod"
            };

            config.ResolveSecretKey().Should().Be("vault-secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }
}
