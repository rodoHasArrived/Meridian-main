using FluentAssertions;
using Meridian.Application.Composition;

namespace Meridian.Tests.Application.Composition;

public sealed class SecurityMasterStartupTests : IDisposable
{
    private readonly string? _originalConnectionString;
    private readonly string? _originalSchema;

    public SecurityMasterStartupTests()
    {
        _originalConnectionString = Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable);
        _originalSchema = Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable);
    }

    [Fact]
    public void EnsureEnvironmentDefaults_SetsSchemaDefaultWithoutInventingConnectionString()
    {
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);

        SecurityMasterStartup.EnsureEnvironmentDefaults();

        Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable)
            .Should().BeNull();
        Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable)
            .Should().Be(SecurityMasterStartup.DefaultSchema);
        SecurityMasterStartup.IsConfigured().Should().BeFalse();
    }

    [Fact]
    public void EnsureEnvironmentDefaults_PreservesExistingValues()
    {
        const string existingConnectionString = "Host=remote;Port=5432;Database=custom;Username=alice;Password=topsecret";
        const string existingSchema = "custom_schema";

        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, existingConnectionString);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, existingSchema);

        SecurityMasterStartup.EnsureEnvironmentDefaults();

        Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable)
            .Should().Be(existingConnectionString);
        Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable)
            .Should().Be(existingSchema);
        SecurityMasterStartup.IsConfigured().Should().BeTrue();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, _originalConnectionString);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, _originalSchema);
    }
}
