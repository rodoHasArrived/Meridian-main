using FluentAssertions;
using Meridian.Storage;
using Xunit;

namespace Meridian.Tests.Storage;

[Collection("Sequential")]
public sealed class MeridianDatabaseEnvironmentTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues = new();

    public MeridianDatabaseEnvironmentTests()
    {
        foreach (var variable in TrackedVariables())
        {
            _originalValues[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    public void Dispose()
    {
        foreach (var (variable, value) in _originalValues)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
    }

    private static IEnumerable<string> TrackedVariables()
    {
        yield return MeridianDatabaseEnvironment.UnifiedVariable;
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables)
            yield return variable;
    }

    [Fact]
    public void ApplyUnifiedDatabaseUrl_WhenUnset_LeavesDomainVariablesUntouched()
    {
        var inherited = MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        inherited.Should().BeEmpty();
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables)
        {
            Environment.GetEnvironmentVariable(variable).Should().BeNull();
        }
    }

    [Fact]
    public void ApplyUnifiedDatabaseUrl_PopulatesUnsetDomainVariables()
    {
        Environment.SetEnvironmentVariable(
            MeridianDatabaseEnvironment.UnifiedVariable,
            "Host=localhost;Port=5432;Database=meridian;Username=meridian");

        var inherited = MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        inherited.Should().BeEquivalentTo(MeridianDatabaseEnvironment.PropagatedConnectionStringVariables);
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables)
        {
            Environment.GetEnvironmentVariable(variable)
                .Should().Be("Host=localhost;Port=5432;Database=meridian;Username=meridian");
        }
    }

    [Fact]
    public void ApplyUnifiedDatabaseUrl_NeverOverwritesExplicitDomainVariables()
    {
        Environment.SetEnvironmentVariable(
            MeridianDatabaseEnvironment.UnifiedVariable,
            "Host=shared;Database=meridian");
        Environment.SetEnvironmentVariable(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            "Host=dedicated-ledger;Database=ledger");

        var inherited = MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        inherited.Should().NotContain("MERIDIAN_LEDGER_CONNECTION_STRING");
        Environment.GetEnvironmentVariable("MERIDIAN_LEDGER_CONNECTION_STRING")
            .Should().Be("Host=dedicated-ledger;Database=ledger");
        Environment.GetEnvironmentVariable("MERIDIAN_BANKING_CONNECTION_STRING")
            .Should().Be("Host=shared;Database=meridian");
    }

    [Fact]
    public void ApplyUnifiedDatabaseUrl_IsIdempotent()
    {
        Environment.SetEnvironmentVariable(
            MeridianDatabaseEnvironment.UnifiedVariable,
            "Host=shared;Database=meridian");

        var first = MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();
        var second = MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        first.Should().NotBeEmpty();
        second.Should().BeEmpty("the first call already populated every domain variable");
    }

    [Fact]
    public void NormalizeToConnectionString_PassesKeywordFormThrough()
    {
        const string keywordForm = "Host=localhost;Port=5432;Database=meridian;Username=app;Password=secret";

        MeridianDatabaseEnvironment.NormalizeToConnectionString(keywordForm).Should().Be(keywordForm);
    }

    [Fact]
    public void NormalizeToConnectionString_ConvertsPostgresUrl()
    {
        var normalized = MeridianDatabaseEnvironment.NormalizeToConnectionString(
            "postgres://app:s%40cret@db.example.com:6432/meridian?sslmode=require&pooling=true");

        normalized.Should().Be(
            "Host=db.example.com;Port=6432;Database=meridian;Username=app;Password=s@cret;SSL Mode=require;pooling=true");
    }

    [Fact]
    public void NormalizeToConnectionString_DefaultsPortAndOmitsMissingParts()
    {
        var normalized = MeridianDatabaseEnvironment.NormalizeToConnectionString(
            "postgresql://db.example.com/meridian");

        normalized.Should().Be("Host=db.example.com;Port=5432;Database=meridian");
    }

    [Fact]
    public void NormalizeToConnectionString_QuotesValuesWithSpecialCharacters()
    {
        // A password containing separators must not corrupt the keyword string.
        var normalized = MeridianDatabaseEnvironment.NormalizeToConnectionString(
            "postgres://app:p%3Bss%3Dw%27d@db.example.com/meridian");

        normalized.Should().Be(
            "Host=db.example.com;Port=5432;Database=meridian;Username=app;Password='p;ss=w''d'");
    }

    [Fact]
    public void NormalizeToConnectionString_InvalidUrl_FailsWithActionableMessage()
    {
        var act = () => MeridianDatabaseEnvironment.NormalizeToConnectionString("postgres://[not-a-valid-uri");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{MeridianDatabaseEnvironment.UnifiedVariable}*");
    }
}
