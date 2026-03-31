using FluentAssertions;
using Meridian.Infrastructure.Contracts;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

/// <summary>
/// Tests for RequiresCredentialAttribute, ICredentialContext, and AttributeCredentialResolver.
/// </summary>
public sealed class AttributeCredentialResolverTests
{
    // -------------------------------------------------------------------------
    // Helpers / test fixtures
    // -------------------------------------------------------------------------

    [RequiresCredential("API_KEY",
        EnvironmentVariables = new[] { "TEST_ATTR_CRED_APIKEY" },
        DisplayName = "API Key",
        Description = "A test API key")]
    private sealed class SingleKeyProvider { }

    [RequiresCredential("KEY_ID",
        EnvironmentVariables = new[] { "TEST_ATTR_CRED_KEYID", "TEST_ATTR_CRED_KEYID_ALT" },
        DisplayName = "Key ID")]
    [RequiresCredential("SECRET",
        EnvironmentVariables = new[] { "TEST_ATTR_CRED_SECRET" },
        Optional = true,
        DisplayName = "Secret")]
    private sealed class MultiKeyProvider { }

    [Meridian.Infrastructure.DataSources.DataSource(
        "test-schema-provider",
        "Test Schema Provider",
        Meridian.Infrastructure.DataSources.DataSourceType.Historical,
        Meridian.Infrastructure.DataSources.DataSourceCategory.Free)]
    [RequiresCredential("SCHEMA_KEY",
        EnvironmentVariables = new[] { "TEST_ATTR_SCHEMA_KEY" },
        DisplayName = "Schema Key")]
    private sealed class DataSourceCredentialProvider { }

    private sealed class NoCredentialProvider { }

    // -------------------------------------------------------------------------
    // RequiresCredentialAttribute construction
    // -------------------------------------------------------------------------

    [Fact]
    public void RequiresCredentialAttribute_SetsName()
    {
        var attr = new RequiresCredentialAttribute("MY_KEY");
        attr.Name.Should().Be("MY_KEY");
    }

    [Fact]
    public void RequiresCredentialAttribute_DefaultValues()
    {
        var attr = new RequiresCredentialAttribute("KEY");
        attr.EnvironmentVariables.Should().BeEmpty();
        attr.Optional.Should().BeFalse();
        attr.DisplayName.Should().BeNull();
        attr.Description.Should().BeNull();
    }

    [Fact]
    public void RequiresCredentialAttribute_NullOrEmptyName_Throws()
    {
        var act1 = () => new RequiresCredentialAttribute(null!);
        var act2 = () => new RequiresCredentialAttribute("");

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // AttributeCredentialResolver.ForType
    // -------------------------------------------------------------------------

    [Fact]
    public void ForType_NoEnvVarSet_ReturnsNull()
    {
        var ctx = AttributeCredentialResolver.ForType(typeof(SingleKeyProvider));
        ctx.Get("API_KEY").Should().BeNull();
    }

    [Fact]
    public void ForType_EnvVarSet_ReturnsValue()
    {
        var varName = "TEST_ATTR_CRED_APIKEY";
        try
        {
            Environment.SetEnvironmentVariable(varName, "test-resolved-key");
            var ctx = AttributeCredentialResolver.ForType(typeof(SingleKeyProvider));
            ctx.Get("API_KEY").Should().Be("test-resolved-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void ForType_UnknownName_ReturnsNull()
    {
        var ctx = AttributeCredentialResolver.ForType(typeof(SingleKeyProvider));
        ctx.Get("UNKNOWN_CRED").Should().BeNull();
    }

    [Fact]
    public void ForType_NoCredentialProvider_AllGetCallsReturnNull()
    {
        var ctx = AttributeCredentialResolver.ForType(typeof(NoCredentialProvider));
        ctx.Get("ANYTHING").Should().BeNull();
    }

    [Fact]
    public void ForType_FallsBackToSecondEnvVar()
    {
        var altVarName = "TEST_ATTR_CRED_KEYID_ALT";
        try
        {
            Environment.SetEnvironmentVariable(altVarName, "from-alt");
            var ctx = AttributeCredentialResolver.ForType(typeof(MultiKeyProvider));
            ctx.Get("KEY_ID").Should().Be("from-alt");
        }
        finally
        {
            Environment.SetEnvironmentVariable(altVarName, null);
        }
    }

    [Fact]
    public void ForType_FirstEnvVarTakesPrecedenceOverSecond()
    {
        var primary = "TEST_ATTR_CRED_KEYID";
        var alt = "TEST_ATTR_CRED_KEYID_ALT";
        try
        {
            Environment.SetEnvironmentVariable(primary, "primary-value");
            Environment.SetEnvironmentVariable(alt, "alt-value");
            var ctx = AttributeCredentialResolver.ForType(typeof(MultiKeyProvider));
            ctx.Get("KEY_ID").Should().Be("primary-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(primary, null);
            Environment.SetEnvironmentVariable(alt, null);
        }
    }

    [Fact]
    public void ForType_ConfigLookupFallback_UsedWhenEnvVarAbsent()
    {
        var ctx = AttributeCredentialResolver.ForType(
            typeof(SingleKeyProvider),
            configLookup: name => name == "API_KEY" ? "from-config" : null);

        ctx.Get("API_KEY").Should().Be("from-config");
    }

    [Fact]
    public void ForType_EnvVarTakesPrecedenceOverConfigLookup()
    {
        var varName = "TEST_ATTR_CRED_APIKEY";
        try
        {
            Environment.SetEnvironmentVariable(varName, "from-env");
            var ctx = AttributeCredentialResolver.ForType(
                typeof(SingleKeyProvider),
                configLookup: _ => "from-config");

            ctx.Get("API_KEY").Should().Be("from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // -------------------------------------------------------------------------
    // IsConfigured
    // -------------------------------------------------------------------------

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenNotSet()
    {
        var ctx = AttributeCredentialResolver.ForType(typeof(SingleKeyProvider));
        ctx.IsConfigured("API_KEY").Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenSet()
    {
        var varName = "TEST_ATTR_CRED_APIKEY";
        try
        {
            Environment.SetEnvironmentVariable(varName, "present");
            var ctx = AttributeCredentialResolver.ForType(typeof(SingleKeyProvider));
            ctx.IsConfigured("API_KEY").Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // -------------------------------------------------------------------------
    // GetAttributes helper
    // -------------------------------------------------------------------------

    [Fact]
    public void GetAttributes_ReturnsAllDeclaredAttributes()
    {
        var attrs = AttributeCredentialResolver.GetAttributes(typeof(MultiKeyProvider));
        attrs.Should().HaveCount(2);
        attrs.Select(a => a.Name).Should().BeEquivalentTo(new[] { "KEY_ID", "SECRET" });
    }

    [Fact]
    public void GetAttributes_NoAttributes_ReturnsEmpty()
    {
        var attrs = AttributeCredentialResolver.GetAttributes(typeof(NoCredentialProvider));
        attrs.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // CredentialSchemaRegistry
    // -------------------------------------------------------------------------

    [Fact]
    public void CredentialSchemaRegistry_FromTypes_IndexesSchemasByProviderType()
    {
        var registry = CredentialSchemaRegistry.FromTypes(new[]
        {
            typeof(SingleKeyProvider),
            typeof(MultiKeyProvider),
            typeof(NoCredentialProvider)
        });

        registry.All.Should().HaveCount(2);
        registry.ByProviderType.Keys.Should().Contain(new[] { typeof(SingleKeyProvider), typeof(MultiKeyProvider) });
        registry.Get(typeof(MultiKeyProvider))!.Fields.Select(field => field.Name)
            .Should().BeEquivalentTo(new[] { "KEY_ID", "SECRET" });
        registry.Get(typeof(NoCredentialProvider)).Should().BeNull();
    }

    [Fact]
    public void CredentialSchemaRegistry_DiscoverFromAssemblies_BuildsProviderIdConvenienceIndex()
    {
        var registry = CredentialSchemaRegistry.DiscoverFromAssemblies(typeof(DataSourceCredentialProvider).Assembly);

        var schema = registry.Get("test-schema-provider");

        schema.Should().NotBeNull();
        schema!.DisplayName.Should().Be("Test Schema Provider");
        schema.ProviderType.Should().Be(typeof(DataSourceCredentialProvider));
        schema.Fields.Should().ContainSingle(field => field.Name == "SCHEMA_KEY");
    }

    // -------------------------------------------------------------------------
    // ForType null guard
    // -------------------------------------------------------------------------

    [Fact]
    public void ForType_NullType_Throws()
    {
        var act = () => AttributeCredentialResolver.ForType(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
