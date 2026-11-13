using Meridian.TestSupport;

namespace Meridian.Tests.TestSupport;

public sealed class PostgresTestConnectionResolverTests
{
    private const string Domain = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING";

    [Fact]
    public void ExplicitDomainEndpointWinsWithoutReadingSharedFallback()
    {
        var reads = new List<string>();
        var values = new Dictionary<string, string?>
        {
            [Domain] = "Host=domain-fixture;Database=domain",
            [PostgresTestServer.TestConnectionStringEnvironmentVariable] = "Host=shared-fixture;Database=shared"
        };

        var result = PostgresTestServer.ResolveExternalConnectionString(Domain, name =>
        {
            reads.Add(name);
            return values.GetValueOrDefault(name);
        });

        Assert.Equal(values[Domain], result);
        Assert.Equal([Domain], reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void MissingOrBlankDomainEndpointUsesTestOnlyFallback(string? domainValue)
    {
        var values = new Dictionary<string, string?>
        {
            [Domain] = domainValue,
            [PostgresTestServer.TestConnectionStringEnvironmentVariable] = "Host=shared-fixture;Database=shared"
        };

        var result = PostgresTestServer.ResolveExternalConnectionString(Domain, values.GetValueOrDefault);

        Assert.Equal(values[PostgresTestServer.TestConnectionStringEnvironmentVariable], result);
        Assert.Equal(domainValue, values[Domain]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void NoUsableExternalEndpointLeavesContainerFallbackAvailable(string? aliasValue)
    {
        var values = new Dictionary<string, string?>
        {
            [PostgresTestServer.TestConnectionStringEnvironmentVariable] = aliasValue
        };

        Assert.Null(PostgresTestServer.ResolveExternalConnectionString(Domain, values.GetValueOrDefault));
        Assert.False(values.ContainsKey(Domain));
    }
}
