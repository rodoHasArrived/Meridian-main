using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ProviderCredentialResolverTests
{
    [Theory]
    [InlineData("CHANGE_ME")]
    [InlineData("your-key-here")]
    [InlineData("<API_KEY>")]
    public void ResolveCredential_PlaceholderConfigValue_ReturnsNull(string configValue)
    {
        const string envVarName = "MERIDIAN_TEST_PROVIDER_CREDENTIAL_RESOLVER";
        var previousValue = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, null);

        try
        {
            var resolver = new ProviderCredentialResolver();

            var resolved = resolver.ResolveCredential(envVarName, configValue, "Test Credential");

            resolved.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, previousValue);
        }
    }

    [Fact]
    public void ResolveCredential_RealConfigValue_ReturnsTrimmedValue()
    {
        const string envVarName = "MERIDIAN_TEST_PROVIDER_CREDENTIAL_RESOLVER";
        var previousValue = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, null);

        try
        {
            var resolver = new ProviderCredentialResolver();

            var resolved = resolver.ResolveCredential(envVarName, "  real-secret-value  ", "Test Credential");

            resolved.Should().Be("real-secret-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, previousValue);
        }
    }
}
