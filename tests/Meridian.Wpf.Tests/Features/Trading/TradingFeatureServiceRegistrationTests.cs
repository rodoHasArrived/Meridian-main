using Meridian.Wpf.Features;
using Meridian.Wpf.Features.Trading;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Trading;

public sealed class TradingFeatureServiceRegistrationTests
{
    [Fact]
    public void Register_ShouldOwnTradingFeatureServicesWithConfiguredLifetimes()
    {
        var services = new ServiceCollection();

        new TradingFeatureModule().Register(services);

        services.SingleDescriptor<RunRiskViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
    }
}
