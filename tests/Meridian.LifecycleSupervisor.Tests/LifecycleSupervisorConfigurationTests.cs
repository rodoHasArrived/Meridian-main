using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Lifecycle;
using Xunit;

namespace Meridian.LifecycleSupervisor.Tests;

public sealed class LifecycleSupervisorConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-lifecycle-supervisor-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_CreatesDedicatedManifestAndStableCurrentInstallPipeName()
    {
        var first = LifecycleSupervisorConfiguration.Load(_root);
        var second = LifecycleSupervisorConfiguration.Load(_root);

        first.Manifest.DatabaseMode.Should().Be(LifecycleDatabaseManagementMode.Dedicated);
        first.Manifest.StartupTimeoutSeconds.Should().Be(60);
        first.Manifest.ShutdownTimeoutSeconds.Should().Be(45);
        first.Manifest.DatabaseTimeoutSeconds.Should().Be(60);
        first.PipeName.Should().Be(second.PipeName);
        first.PipeName.Should().StartWith("Meridian.LifecycleSupervisor.");
        first.HostLogRoot.Should().Be(Path.Combine(first.DataRoot, "_logs"));
        File.Exists(first.ManifestPath).Should().BeTrue();
    }

    [Fact]
    public void Load_ExternalModeRequiresNamedConnectionStringButNotPostgreSqlTooling()
    {
        var serviceRoot = Path.Combine(_root, "service");
        var hostRoot = Path.Combine(_root, "host");
        Directory.CreateDirectory(serviceRoot);
        Directory.CreateDirectory(hostRoot);
        File.WriteAllText(Path.Combine(hostRoot, "Meridian.exe"), "test");
        var variable = $"MDC_LIFECYCLE_TEST_DATABASE_{Guid.NewGuid():N}";
        var manifest = new LifecycleSupervisorManifestDto
        {
            DatabaseMode = LifecycleDatabaseManagementMode.External,
            ExternalConnectionStringEnvironmentVariable = variable
        };
        File.WriteAllText(
            Path.Combine(serviceRoot, "lifecycle-supervisor.json"),
            JsonSerializer.Serialize(
                manifest,
                LifecycleContractsJsonContext.Default.LifecycleSupervisorManifestDto));

        Environment.SetEnvironmentVariable(variable, "Host=127.0.0.1;Database=meridian");
        LifecycleSupervisorPreflightResult result;
        string hostConnectionString;
        try
        {
            var configuration = LifecycleSupervisorConfiguration.Load(_root);
            result = LifecycleSupervisorPreflight.Evaluate(configuration);
            hostConnectionString = new LifecycleDatabaseController(configuration).BuildHostConnectionString();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        result.Success.Should().BeTrue(result.Message);
        hostConnectionString.Should().Be("Host=127.0.0.1;Database=meridian");
    }

    [Fact]
    public void Load_ExternalModeWithoutConnectionStringConfigurationFailsPreflight()
    {
        var serviceRoot = Path.Combine(_root, "service");
        var hostRoot = Path.Combine(_root, "host");
        Directory.CreateDirectory(serviceRoot);
        Directory.CreateDirectory(hostRoot);
        File.WriteAllText(Path.Combine(hostRoot, "Meridian.exe"), "test");
        File.WriteAllText(
            Path.Combine(serviceRoot, "lifecycle-supervisor.json"),
            JsonSerializer.Serialize(
                new LifecycleSupervisorManifestDto
                {
                    DatabaseMode = LifecycleDatabaseManagementMode.External
                },
                LifecycleContractsJsonContext.Default.LifecycleSupervisorManifestDto));

        var result = LifecycleSupervisorPreflight.Evaluate(
            LifecycleSupervisorConfiguration.Load(_root));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("externalConnectionStringEnvironmentVariable");
    }

    [Fact]
    public void Load_RejectsUnknownDatabaseMode()
    {
        var serviceRoot = Path.Combine(_root, "service");
        Directory.CreateDirectory(serviceRoot);
        File.WriteAllText(
            Path.Combine(serviceRoot, "lifecycle-supervisor.json"),
            """{"schemaVersion":1,"databaseMode":999}""");

        var act = () => LifecycleSupervisorConfiguration.Load(_root);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*database mode*");
    }

    [Fact]
    public void Load_MalformedManifestThrowsJsonExceptionForProgramBoundaryClassification()
    {
        var serviceRoot = Path.Combine(_root, "service");
        Directory.CreateDirectory(serviceRoot);
        File.WriteAllText(
            Path.Combine(serviceRoot, "lifecycle-supervisor.json"),
            "{ this is not valid JSON");

        var act = () => LifecycleSupervisorConfiguration.Load(_root);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ResolveTool_ExplicitBinPathDoesNotFallThroughToPath()
    {
        var explicitBin = Path.Combine(_root, "missing-explicit-bin");

        var resolved = LifecycleSupervisorPreflight.ResolveTool(explicitBin, "postgres.exe");

        resolved.Should().BeNull();
    }

    [Fact]
    public void ProtectedShutdownToken_RoundTripsWithoutPlaintextPersistence()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "token.dpapi");
        const string token = "this-token-must-not-appear-on-disk";

        LifecycleProtectedSecretStore.Write(path, token);

        LifecycleProtectedSecretStore.Read(path).Should().Be(token);
        System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Should().NotContain(token);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
