using Meridian.Wpf.Contracts;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class PageActivationLifetimeContractTests
{
    [Fact]
    public void IPageActivationLifetime_ShouldExposeExplicitCancellationMethod()
    {
        typeof(IPageActivationLifetime)
            .GetMethod(nameof(IPageActivationLifetime.CancelActivation), Type.EmptyTypes)
            .Should()
            .NotBeNull("visible-page lifetimes must expose cancellation separately from deactivation");
    }

    [Fact]
    public void IPageActivationLifetime_ShouldExposeExplicitDeactivationMethod()
    {
        typeof(IPageActivationLifetime)
            .GetMethod(nameof(IPageActivationLifetime.Deactivate), Type.EmptyTypes)
            .Should()
            .NotBeNull("visible-page lifetimes must stop timers, polling loops, and subscriptions when hidden");
    }

    [Fact]
    public void PageActivationLifetimeImplementers_ShouldDeclareCancellationMethod()
    {
        var implementers = new[]
        {
            typeof(OrderBookViewModel),
            typeof(ProviderHealthViewModel),
            typeof(LiveDataViewerViewModel),
            typeof(BacktestViewModel),
            typeof(BackfillViewModel),
            typeof(SymbolsPageViewModel)
        };

        foreach (var implementer in implementers)
        {
            implementer
                .GetMethod(nameof(IPageActivationLifetime.CancelActivation), Type.EmptyTypes)
                .Should()
                .NotBeNull($"{implementer.Name} must cancel in-flight activation work without relying on disposal");
        }
    }

    [Fact]
    public void PageActivationLifetimeImplementers_ShouldDeclareDeactivationMethod()
    {
        var implementers = new[]
        {
            typeof(OrderBookViewModel),
            typeof(ProviderHealthViewModel),
            typeof(LiveDataViewerViewModel),
            typeof(BacktestViewModel),
            typeof(BackfillViewModel),
            typeof(SymbolsPageViewModel)
        };

        foreach (var implementer in implementers)
        {
            implementer
                .GetMethod(nameof(IPageActivationLifetime.Deactivate), Type.EmptyTypes)
                .Should()
                .NotBeNull($"{implementer.Name} must stop visible-page timers, polling, and subscriptions when hidden");
        }
    }
}
