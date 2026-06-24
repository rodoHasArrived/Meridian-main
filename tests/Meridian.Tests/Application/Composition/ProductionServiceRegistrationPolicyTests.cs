using FluentAssertions;
using Meridian.Application.Composition;
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
    public void IsNonProductionOnlyImplementation_MatchesMarkerAndNamingPolicy()
    {
        ProductionServiceRegistrationPolicy
            .IsNonProductionOnlyImplementation(typeof(MarkerOnlyService))
            .Should().BeTrue();

        ProductionServiceRegistrationPolicy
            .IsNonProductionOnlyImplementation(typeof(InMemorySampleService))
            .Should().BeTrue();

        ProductionServiceRegistrationPolicy
            .IsNonProductionOnlyImplementation(typeof(AllowedService))
            .Should().BeFalse();
    }

    [NonProductionOnlyImplementation]
    private sealed class MarkerOnlyService;

    private sealed class InMemorySampleService;

    private sealed class AllowedService;

    private sealed class EnvironmentVariableScope : IDisposable
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
}
