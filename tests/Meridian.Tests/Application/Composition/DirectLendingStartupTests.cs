using FluentAssertions;
using Meridian.Application.Composition;

namespace Meridian.Tests.Application.Composition;

public sealed class DirectLendingStartupTests : IDisposable
{
    private readonly string? _originalConnectionString;
    private readonly string? _originalSchema;

    public DirectLendingStartupTests()
    {
        _originalConnectionString = Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable);
        _originalSchema = Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable);
    }

    [Fact]
    public void EnsureEnvironmentDefaults_SetsSchemaDefaultWithoutInventingConnectionString()
    {
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);

        DirectLendingStartup.EnsureEnvironmentDefaults();

        Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable)
            .Should().BeNull();
        Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable)
            .Should().Be(DirectLendingStartup.DefaultSchema);
        DirectLendingStartup.IsConfigured().Should().BeFalse();
    }

    [Fact]
    public void EnsureEnvironmentDefaults_PreservesExistingValues()
    {
        const string existingConnectionString = "Host=remote;Port=5432;Database=custom;Username=alice;Password=topsecret";
        const string existingSchema = "custom_schema";

        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, existingConnectionString);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, existingSchema);

        DirectLendingStartup.EnsureEnvironmentDefaults();

        Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable)
            .Should().Be(existingConnectionString);
        Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable)
            .Should().Be(existingSchema);
        DirectLendingStartup.IsConfigured().Should().BeTrue();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, _originalConnectionString);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, _originalSchema);
    }
}
