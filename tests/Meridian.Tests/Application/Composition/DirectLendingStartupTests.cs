using FluentAssertions;
using Meridian.Application.Composition;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class DirectLendingStartupTests : IDisposable
{
    private readonly string? _originalConnectionString;
    private readonly string? _originalSchema;
    private readonly string? _originalSecurityMasterConnectionString;
    private readonly string? _originalSecurityMasterSchema;

    public DirectLendingStartupTests()
    {
        _originalConnectionString = Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable);
        _originalSchema = Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable);
        _originalSecurityMasterConnectionString = Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable);
        _originalSecurityMasterSchema = Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable);
    }

    [Fact]
    public void EnsureEnvironmentDefaults_SetsSchemaDefaultWithoutInventingConnectionString()
    {
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);

        DirectLendingStartup.EnsureEnvironmentDefaults();

        Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable)
            .Should().BeNull();
        Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable)
            .Should().Be(DirectLendingStartup.DefaultSchema);
        DirectLendingStartup.IsConfigured().Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_InheritsSecurityMasterConnectionAndSchema()
    {
        const string securityMasterConnectionString = "Host=security-master;Port=5432;Database=master;Username=alice;Password=topsecret";
        const string securityMasterSchema = "security_master_ops";

        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, securityMasterConnectionString);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, securityMasterSchema);

        DirectLendingStartup.EnsureEnvironmentDefaults();

        DirectLendingStartup.IsConfigured().Should().BeTrue();
        DirectLendingStartup.HasDedicatedConfiguration().Should().BeFalse();
        DirectLendingStartup.GetEffectiveConnectionString().Should().Be(securityMasterConnectionString);
        DirectLendingStartup.GetEffectiveSchema().Should().Be(securityMasterSchema);
        Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable)
            .Should().BeNull();
    }

    [Fact]
    public void EnsureEnvironmentDefaults_PreservesExistingValues()
    {
        const string existingConnectionString = "Host=remote;Port=5432;Database=custom;Username=alice;Password=topsecret";
        const string existingSchema = "custom_schema";

        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, existingConnectionString);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, existingSchema);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, "Host=security-master;Port=5432;Database=master;Username=alice;Password=topsecret");
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, "security_master_ops");

        DirectLendingStartup.EnsureEnvironmentDefaults();

        Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable)
            .Should().Be(existingConnectionString);
        Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable)
            .Should().Be(existingSchema);
        DirectLendingStartup.IsConfigured().Should().BeTrue();
        DirectLendingStartup.HasDedicatedConfiguration().Should().BeTrue();
        DirectLendingStartup.GetEffectiveConnectionString().Should().Be(existingConnectionString);
        DirectLendingStartup.GetEffectiveSchema().Should().Be(existingSchema);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, _originalConnectionString);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, _originalSchema);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, _originalSecurityMasterConnectionString);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, _originalSecurityMasterSchema);
    }
}
