using System.Windows.Controls;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Services;

public sealed class ViewModelViewResolverTests
{
    [Fact]
    public void ResolveViewModelType_ShouldUsePageNameViewModelConvention()
    {
        var resolver = new ViewModelViewResolver();

        resolver.ResolveViewModelType(typeof(ConventionProbePage))
            .Should()
            .Be(typeof(ConventionProbeViewModel));
    }

    [Fact]
    public void AutoWire_ShouldSetDataContextWhenConventionViewModelIsRegistered()
    {
        WpfTestThread.Run(() =>
        {
            var services = new ServiceCollection()
                .AddTransient<ConventionProbeViewModel>()
                .BuildServiceProvider();
            var resolver = new ViewModelViewResolver(services.GetRequiredService<IServiceProviderIsService>());
            var page = new ConventionProbePage();

            resolver.AutoWire(page, services);

            page.DataContext.Should().BeOfType<ConventionProbeViewModel>();
        });
    }

    [Fact]
    public void AutoWire_ShouldSkipPagesWithExistingDataContext()
    {
        WpfTestThread.Run(() =>
        {
            var existingDataContext = new object();
            var services = new ServiceCollection()
                .AddTransient<ConventionProbeViewModel>()
                .BuildServiceProvider();
            var resolver = new ViewModelViewResolver(services.GetRequiredService<IServiceProviderIsService>());
            var page = new ConventionProbePage
            {
                DataContext = existingDataContext
            };

            resolver.AutoWire(page, services);

            page.DataContext.Should().BeSameAs(existingDataContext);
        });
    }

    private sealed class ConventionProbePage : Page
    {
    }

    private sealed class ConventionProbeViewModel
    {
    }
}
