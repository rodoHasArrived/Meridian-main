using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.DataIntegration.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class ProductionServiceRegistrationPolicyTests
{
    [Fact]
    public void AddMarketDataServices_WhenEnvironmentIsProduction_RejectsNonProductionOnlyImplementations()
    {
        using var environment = new EnvironmentVariableScope("ASPNETCORE_ENVIRONMENT", "Production");
        var services = new ServiceCollection();

        Action act = () => services.AddMarketDataServices(CompositionOptions.Default);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*persistence-backed governance domain services*");
    }

    [Fact]
    public void Validate_WhenProductionPostureIsDeclared_RejectsInMemoryRegistrationsWithoutEnvironmentVariables()
    {
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        services.AddSingleton<InMemorySampleService>();

        Action act = () => ProductionServiceRegistrationPolicy.Validate(services);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*rejected non-production DI registrations*InMemorySampleService*");
    }

    [Fact]
    public void Validate_WhenLocalWorkstationPostureAndNonProductionEnvironment_AllowsInMemoryRegistrations()
    {
        using var quietEnvironment = new ProductionEnvironmentQuietScope();
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
        services.AddSingleton<InMemorySampleService>();

        Action act = () => ProductionServiceRegistrationPolicy.Validate(services);

        act.Should().NotThrow();
    }

    [Fact]
    public void DiagnosticsFeatureRegistration_WhenEnvironmentIsProduction_OmitsSampleDataGenerator()
    {
        using var environment = new EnvironmentVariableScope("ASPNETCORE_ENVIRONMENT", "Production");
        var services = new ServiceCollection();

        new DiagnosticsFeatureRegistration().Register(services, CompositionOptions.Default);

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(SampleDataGenerator));
    }

    [Fact]
    public void DiagnosticsFeatureRegistration_WhenEnvironmentIsNotProduction_RegistersSampleDataGenerator()
    {
        using var quietEnvironment = new ProductionEnvironmentQuietScope();
        var services = new ServiceCollection();

        new DiagnosticsFeatureRegistration().Register(services, CompositionOptions.Default);

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(SampleDataGenerator));
    }

    [Fact]
    public void ResolveDeclaredPosture_LastDeclarationWins()
    {
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);

        ProductionServiceRegistrationPolicy.ResolveDeclaredPosture(services)
            .Should().Be(MeridianDeploymentPosture.ProductionApi);
    }

    [Fact]
    public void ResolveDeclaredPosture_WithoutDeclaration_IsUnspecified()
    {
        ProductionServiceRegistrationPolicy.ResolveDeclaredPosture(new ServiceCollection())
            .Should().Be(MeridianDeploymentPosture.Unspecified);
    }

    [Fact]
    public void IsProductionEnvironment_HonorsApiDeploymentModeEnvironmentVariable()
    {
        using var quietEnvironment = new ProductionEnvironmentQuietScope();
        using var deploymentMode = new EnvironmentVariableScope("MERIDIAN_API_DEPLOYMENT_MODE", "ProductionApi");

        ProductionServiceRegistrationPolicy.IsProductionEnvironment().Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(MarkerOnlyService))]
    [InlineData(typeof(InMemorySampleService))]
    [InlineData(typeof(NullSampleStore))]
    [InlineData(typeof(NoOpSampleJob))]
    [InlineData(typeof(FakeSampleGateway))]
    [InlineData(typeof(StubSampleClient))]
    [InlineData(typeof(SampleFixtureSeeder))]
    public void IsNonProductionOnlyImplementation_FlagsMarkersAndProhibitedNamePrefixes(Type implementationType)
    {
        ProductionServiceRegistrationPolicy
            .IsNonProductionOnlyImplementation(implementationType)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(AllowedService))]
    [InlineData(typeof(NullableValueHelper))]
    [InlineData(typeof(SamplerService))]
    public void IsNonProductionOnlyImplementation_DoesNotFlagNonMatchingNames(Type implementationType)
    {
        ProductionServiceRegistrationPolicy
            .IsNonProductionOnlyImplementation(implementationType)
            .Should().BeFalse();
    }

    [Fact]
    public void IsNonProductionOnlyImplementation_HonorsProductionSafeOptOutForNameMatches()
    {
        ProductionServiceRegistrationPolicy
            .IsNonProductionOnlyImplementation(typeof(NullSafeApprovedService))
            .Should().BeFalse();
    }

    [Fact]
    public void IsNonProductionOnlyImplementation_OptOutCannotOverrideExplicitMarker()
    {
        ProductionServiceRegistrationPolicy
            .IsNonProductionOnlyImplementation(typeof(InMemoryMarkedService))
            .Should().BeTrue();
    }

    [NonProductionOnlyImplementation]
    private sealed class MarkerOnlyService;

    private sealed class InMemorySampleService;

    private sealed class NullSampleStore;

    private sealed class NoOpSampleJob;

    private sealed class FakeSampleGateway;

    private sealed class StubSampleClient;

    private sealed class SampleFixtureSeeder;

    // Prohibited prefix followed by a lowercase letter is a different word, not a policy match.
    private sealed class NullableValueHelper;

    private sealed class SamplerService;

    [ProductionSafeImplementation("test fixture: intentional production-safe null object")]
    private sealed class NullSafeApprovedService;

    [ProductionSafeImplementation("test fixture: opt-out must not override the explicit marker")]
    private sealed class InMemoryMarkedService : INonProductionOnlyService;

    private sealed class AllowedService;
}

/// <summary>Scoped environment-variable override restored on dispose.</summary>
internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _originalValue;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
}

/// <summary>
/// Clears every environment variable the production policy consults so posture-driven tests are
/// deterministic on hosts that export production-like environments.
/// </summary>
internal sealed class ProductionEnvironmentQuietScope : IDisposable
{
    private static readonly string[] VariableNames =
    [
        "ASPNETCORE_ENVIRONMENT",
        "DOTNET_ENVIRONMENT",
        "MERIDIAN_ENVIRONMENT",
        "MERIDIAN_DEPLOYMENT_ENVIRONMENT",
        "MERIDIAN_MODE",
        "MERIDIAN_API_DEPLOYMENT_MODE"
    ];

    private readonly EnvironmentVariableScope[] _scopes =
        VariableNames.Select(name => new EnvironmentVariableScope(name, null)).ToArray();

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }
    }
}
