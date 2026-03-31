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
        entry.CredentialFields[0].DisplayName.Should().Be("Demo API Key");
        entry.CredentialFields[0].Required.Should().BeTrue();
    }
}
