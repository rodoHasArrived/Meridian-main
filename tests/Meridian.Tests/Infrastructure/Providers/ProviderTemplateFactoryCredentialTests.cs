using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderTemplateFactoryCredentialTests
{
    [RequiresCredential(
        "DEMO_API_KEY",
        EnvironmentVariables = new[] { "DEMO_API_KEY" },
        DisplayName = "Demo API Key")]
    private sealed class AttributeOnlyProvider : IProviderMetadata
    {
        public string ProviderId => "demo";
        public string ProviderDisplayName => "Demo Provider";
        public string ProviderDescription => "Provider used for catalog credential metadata tests.";
        public int ProviderPriority => 10;
        public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.BackfillBarsOnly;
    }

    [Fact]
    public void ToCatalogEntry_DerivesCredentialFieldsFromRequiresCredentialAttributes()
    {
        var entry = ProviderTemplateFactory.ToCatalogEntry(new AttributeOnlyProvider());

        entry.RequiresCredentials.Should().BeTrue();
        entry.CredentialFields.Should().ContainSingle();
        entry.CredentialFields[0].Name.Should().Be("DEMO_API_KEY");
        entry.CredentialFields[0].EnvironmentVariable.Should().Be("DEMO_API_KEY");
        entry.CredentialFields[0].EnvironmentVariableAliases.Should().BeEmpty();
        entry.CredentialFields[0].DisplayName.Should().Be("Demo API Key");
        entry.CredentialFields[0].Required.Should().BeTrue();
    }

    [RequiresCredential(
        "DEMO_ALIAS_KEY",
        EnvironmentVariables = new[] { "DEMO_ALIAS_KEY", "DEMO__ALIASKEY" },
        DisplayName = "Demo Alias Key")]
    private sealed class AttributeAliasProvider : IProviderMetadata
    {
        public string ProviderId => "demo-alias";
        public string ProviderDisplayName => "Demo Alias Provider";
        public string ProviderDescription => "Provider used for credential alias catalog tests.";
        public int ProviderPriority => 20;
        public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.BackfillBarsOnly;
    }

    [Fact]
    public void ToCatalogEntry_PreservesCredentialEnvironmentVariableAliases()
    {
        var entry = ProviderTemplateFactory.ToCatalogEntry(new AttributeAliasProvider());

        entry.CredentialFields.Should().ContainSingle();
        entry.CredentialFields[0].EnvironmentVariable.Should().Be("DEMO_ALIAS_KEY");
        entry.CredentialFields[0].EnvironmentVariableAliases.Should().Equal("DEMO__ALIASKEY");
        entry.CredentialFields[0].AllEnvironmentVariables.Should().Equal("DEMO_ALIAS_KEY", "DEMO__ALIASKEY");
    }
}
