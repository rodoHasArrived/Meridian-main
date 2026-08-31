using Meridian.Ui.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class LiveDataViewerViewModelTests
{
    [Fact]
    public void ActivationLifetime_DeactivateCancelsPollingLifetimeWithoutClearingLoadedState()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            viewModel.Should().BeAssignableTo<IPageActivationLifetime>();
            viewModel.AddSymbolToList("SPY");
            viewModel.SelectSymbol("SPY");
            viewModel.LiveEvents.Add(new LiveDataEventModel
            {
                Id = "evt-1",
                Symbol = "SPY",
                Type = "TRD",
                Price = "500.00",
                Size = "100"
            });
            using var activationCts = new CancellationTokenSource();
            activationCts.Cancel();

            viewModel.ActivateAsync(activationCts.Token).GetAwaiter().GetResult();
            var token = viewModel.ActivationToken;

            viewModel.IsActive.Should().BeTrue();
            token.CanBeCanceled.Should().BeTrue();

            viewModel.Deactivate();

            viewModel.IsActive.Should().BeFalse();
            token.IsCancellationRequested.Should().BeTrue();
            viewModel.SelectedSymbol.Should().Be("SPY");
            viewModel.AvailableSymbols.Should().ContainSingle(symbol => symbol == "SPY");
            viewModel.LiveEvents.Should().ContainSingle(evt => evt.Id == "evt-1");
        });
    }

    [Fact]
    public void LiveDataViewerPageSource_UsesActivationLifetimeInsteadOfDisposingOnUnload()
    {
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\LiveDataViewerPage.xaml.cs"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\LiveDataViewerViewModel.cs"));

        codeBehind.Should().Contain("await _vm.ActivateAsync()");
        codeBehind.Should().Contain("_vm.Deactivate()");
        codeBehind.Should().NotContain("_vm.Dispose()");
        viewModel.Should().Contain("IPageActivationLifetime");
        viewModel.Should().Contain("public bool IsActive");
        viewModel.Should().Contain("public CancellationToken ActivationToken");
    }

    [Fact]
    public void LastTradeTime_ObservedTimestamps_ShowsTheLatestTradeNotTheLastEnumerated()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();

            // Session stats accumulate during raw response enumeration, before the display loop
            // sorts by RawTimestamp. A newest-first batch must still leave the latest trade here.
            viewModel.ApplySessionEventsForTests(
                CreateTrade("evt-2", new DateTime(2026, 8, 1, 14, 30, 45, DateTimeKind.Utc), 101.50m),
                CreateTrade("evt-1", new DateTime(2026, 8, 1, 14, 30, 12, DateTimeKind.Utc), 100.25m));

            viewModel.LastTradeTimeText.Should().Be("14:30:45");
            viewModel.LastTradeText.Should().Be("101.50", "the price and the time describe one trade");
        });
    }

    [Fact]
    public void LastTradeTime_BatchMixesObservedAndUnobserved_KeepsTheObservedTradeIntact()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();

            var observed = CreateTrade("evt-1", new DateTime(2026, 8, 1, 14, 30, 45, DateTimeKind.Utc), 101.50m);
            // Receipt time, not an observation - it must not displace a trade we can actually time.
            var unobserved = CreateTrade("evt-2", new DateTime(2026, 8, 1, 14, 31, 00, DateTimeKind.Utc), 99.00m);
            unobserved.HasObservedTimestamp = false;

            viewModel.ApplySessionEventsForTests(observed, unobserved);

            viewModel.LastTradeTimeText.Should().Be("14:30:45");
            viewModel.LastTradeText.Should().Be("101.50");
        });
    }

    [Fact]
    public void LastTradeTime_PayloadOmittedTheTimestamp_StaysUnknownRatherThanShowingReceiptTime()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();

            // ParseLiveEvent falls back to receipt time when a payload carries no timestamp, so
            // ordering keeps working. That fallback is not an observation and must not reach a
            // field the operator reads as one.
            var withoutObservation = CreateTrade(
                "evt-1", new DateTime(2026, 8, 1, 14, 30, 12, DateTimeKind.Utc), 100.25m);
            withoutObservation.HasObservedTimestamp = false;

            viewModel.ApplySessionEventsForTests(withoutObservation);

            viewModel.LastTradeText.Should().Be("100.25", "the price itself is still real");
            viewModel.LastTradeTimeText.Should().Be("--");
        });
    }

    [Fact]
    public void LastTradeTime_SessionReset_ReturnsToUnknown()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            viewModel.ApplySessionEventsForTests(
                CreateTrade("evt-1", new DateTime(2026, 8, 1, 14, 30, 12, DateTimeKind.Utc), 100.25m));
            viewModel.LastTradeTimeText.Should().Be("14:30:12");

            viewModel.ResetSessionStatsForTests();

            viewModel.LastTradeTimeText.Should().Be("--");
        });
    }

    private static LiveDataEventModel CreateTrade(string id, DateTime observedAt, decimal price) =>
        new()
        {
            Id = id,
            Type = "TRD",
            Symbol = "SPY",
            RawPrice = price,
            Price = price.ToString("F2"),
            Size = "100",
            RawTimestamp = observedAt,
            HasObservedTimestamp = true,
            Timestamp = observedAt.ToString("HH:mm:ss.fff"),
        };

    private static LiveDataViewerViewModel CreateViewModel()
        => new(
            WpfServices.StatusService.Instance,
            WpfServices.ConnectionService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            SymbolManagementService.Instance,
            WpfServices.TearOffPanelService.Instance,
            WpfServices.ConfigService.Instance);
}
