using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Both hosts build the OMS through <see cref="OrderManagementSystemFactory"/>. These tests pin the
/// differences that the two hand-written compositions had, so routing them through one factory
/// changes no host's dependency requirements.
/// </summary>
public sealed class OrderManagementSystemFactoryTests
{
    [Fact]
    public async Task BrokerageExecution_WithoutRiskValidatorOrPortfolio_StillComposes()
    {
        await using var provider = PaperServices().BuildServiceProvider();

        var oms = OrderManagementSystemFactory.Create(provider, OrderManagementSystemComposition.BrokerageExecution);

        oms.Should().NotBeNull("the library composition leaves risk and portfolio state to its caller");
    }

    [Fact]
    public async Task HostedWorkstation_WithoutRiskValidator_RefusesToCompose()
    {
        var services = PaperServices();
        services.AddSingleton(Substitute.For<IPortfolioState>());
        services.AddSingleton(new OrderManagementSystemOptions());
        await using var provider = services.BuildServiceProvider();

        var create = () => OrderManagementSystemFactory.Create(provider, OrderManagementSystemComposition.HostedWorkstation);

        create.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(IRiskValidator)}*");
    }

    [Fact]
    public async Task HostedWorkstation_WithoutPortfolioState_RefusesToCompose()
    {
        var services = PaperServices();
        services.AddSingleton(Substitute.For<IRiskValidator>());
        services.AddSingleton(new OrderManagementSystemOptions());
        await using var provider = services.BuildServiceProvider();

        var create = () => OrderManagementSystemFactory.Create(provider, OrderManagementSystemComposition.HostedWorkstation);

        create.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(IPortfolioState)}*");
    }

    [Fact]
    public async Task HostedWorkstation_WithoutOptions_RefusesToCompose()
    {
        var services = PaperServices();
        services.AddSingleton(Substitute.For<IRiskValidator>());
        services.AddSingleton(Substitute.For<IPortfolioState>());
        await using var provider = services.BuildServiceProvider();

        var create = () => OrderManagementSystemFactory.Create(provider, OrderManagementSystemComposition.HostedWorkstation);

        create.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(OrderManagementSystemOptions)}*");
    }

    [Fact]
    public async Task HostedWorkstation_WithRequiredDependencies_Composes()
    {
        var services = PaperServices();
        services.AddSingleton(Substitute.For<IRiskValidator>());
        services.AddSingleton(Substitute.For<IPortfolioState>());
        services.AddSingleton(new OrderManagementSystemOptions());
        await using var provider = services.BuildServiceProvider();

        var oms = OrderManagementSystemFactory.Create(provider, OrderManagementSystemComposition.HostedWorkstation);

        oms.Should().NotBeNull();
    }

    [Fact]
    public async Task AddBrokerageExecution_ResolvesOrderManagerThroughFactory()
    {
        var services = PaperServices();
        services.AddBrokerageExecution(config => config.Gateway = "paper");
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOrderManager>().Should().BeOfType<OrderManagementSystem>();
    }

    [Fact]
    public void Create_UnknownComposition_Throws()
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        var create = () => OrderManagementSystemFactory.Create(provider, (OrderManagementSystemComposition)99);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ServiceCollection PaperServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IExecutionGateway>(_ => new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance));
        services.AddSingleton(new BrokerageConfiguration { Gateway = "paper" });
        return services;
    }
}
