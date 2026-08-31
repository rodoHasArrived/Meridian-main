using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Forced-order option selection scenarios. Completion sources control every response so stale
/// success, stale failure, stop, and disposal behavior never depend on sleeps or scheduler timing.
/// </summary>
public sealed class OptionsViewModelConcurrencyTests
{
    [Fact]
    public async Task RapidSymbolChange_StaleSuccessCannotReplaceNewerExpirations()
    {
        var first = NewCompletion<OptionsExpirationsResponse?>();
        var second = NewCompletion<OptionsExpirationsResponse?>();
        var api = new ControllableOptionsApiClient
        {
            Expirations = (symbol, _) => symbol == "AAA" ? first.Task : second.Task
        };
        await using var viewModel = OptionsViewModel.CreateForTesting(LoggingService.Instance, api);

        var oldLoad = viewModel.SelectUnderlyingAsync("aaa");
        var newLoad = viewModel.SelectUnderlyingAsync("bbb");
        second.SetResult(ExpirationResponse("BBB", new DateOnly(2027, 2, 19)));
        await newLoad;
        first.SetResult(ExpirationResponse("AAA", new DateOnly(2026, 1, 16)));
        await oldLoad;

        viewModel.Expirations.Should().Equal("2027-02-19");
        viewModel.ExpirationsHeader.Should().Be("Expirations for BBB (1)");
        viewModel.StatusText.Should().NotContain("AAA");
    }

    [Fact]
    public async Task RapidSymbolChange_StaleErrorCannotReplaceNewerSuccess()
    {
        var first = NewCompletion<OptionsExpirationsResponse?>();
        var api = new ControllableOptionsApiClient
        {
            Expirations = (symbol, _) => symbol == "OLD"
                ? first.Task
                : Task.FromResult<OptionsExpirationsResponse?>(
                    ExpirationResponse("NEW", new DateOnly(2028, 3, 17)))
        };
        await using var viewModel = OptionsViewModel.CreateForTesting(LoggingService.Instance, api);

        var oldLoad = viewModel.SelectUnderlyingAsync("OLD");
        await viewModel.SelectUnderlyingAsync("NEW");
        first.SetException(new InvalidOperationException("stale provider error"));
        await oldLoad;

        viewModel.Expirations.Should().Equal("2028-03-17");
        viewModel.ExpirationsHeader.Should().Contain("NEW");
        viewModel.StatusText.Should().NotContain("stale provider error");
        viewModel.IsStatusVisible.Should().BeFalse();
    }

    [Fact]
    public async Task RapidExpirationChange_StaleChainCannotReplaceNewerSelection()
    {
        var oldChain = NewCompletion<OptionsChainResponse?>();
        var newChain = NewCompletion<OptionsChainResponse?>();
        var api = new ControllableOptionsApiClient
        {
            Expirations = (_, _) => Task.FromResult<OptionsExpirationsResponse?>(
                new OptionsExpirationsResponse(
                    "SPY",
                    [new DateOnly(2027, 1, 15), new DateOnly(2027, 2, 19)],
                    2,
                    DateTimeOffset.UtcNow)),
            Chain = (_, expiration, _) => expiration == "2027-01-15"
                ? oldChain.Task
                : newChain.Task
        };
        await using var viewModel = OptionsViewModel.CreateForTesting(LoggingService.Instance, api);
        await viewModel.SelectUnderlyingAsync("SPY");

        var oldLoad = viewModel.SelectExpirationAsync("2027-01-15");
        var newLoad = viewModel.SelectExpirationAsync("2027-02-19");
        newChain.SetResult(ChainResponse("SPY", new DateOnly(2027, 2, 19), 602m));
        await newLoad;
        oldChain.SetResult(ChainResponse("SPY", new DateOnly(2027, 1, 15), 401m));
        await oldLoad;

        viewModel.ChainHeader.Should().Be("Option Chain: SPY 2027-02-19");
        viewModel.ChainUnderlyingPrice.Should().Be("Underlying: $602.00");
        viewModel.ChainPanelVisible.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_CancelsAndDrainsCurrentLoads_WithoutSurfacingFailure()
    {
        var started = NewCompletion();
        var exited = NewCompletion();
        var api = new ControllableOptionsApiClient
        {
            Expirations = (_, ct) => WaitForCancellationAsync<OptionsExpirationsResponse>(
                started,
                exited,
                ct)
        };
        await using var viewModel = OptionsViewModel.CreateForTesting(LoggingService.Instance, api);

        var load = viewModel.SelectUnderlyingAsync("SPY");
        await started.Task;
        await viewModel.StopAsync();
        await load;

        exited.Task.IsCompletedSuccessfully.Should().BeTrue();
        viewModel.IsLoadingVisible.Should().BeFalse();
        viewModel.StatusText.Should().NotContain("Failed");

        api.Expirations = (_, _) => Task.FromResult<OptionsExpirationsResponse?>(
            ExpirationResponse("QQQ", new DateOnly(2029, 1, 19)));
        await viewModel.SelectUnderlyingAsync("QQQ");
        viewModel.Expirations.Should().Equal("2029-01-19");
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndDrains_ThenRejectsNewLoads()
    {
        var started = NewCompletion();
        var exited = NewCompletion();
        var api = new ControllableOptionsApiClient
        {
            Expirations = (_, ct) => WaitForCancellationAsync<OptionsExpirationsResponse>(
                started,
                exited,
                ct)
        };
        var viewModel = OptionsViewModel.CreateForTesting(LoggingService.Instance, api);

        var load = viewModel.SelectUnderlyingAsync("IWM");
        await started.Task;
        await viewModel.DisposeAsync();
        await load;

        exited.Task.IsCompletedSuccessfully.Should().BeTrue();
        Action startAfterDispose = () => _ = viewModel.SelectUnderlyingAsync("DIA");
        startAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    private static OptionsExpirationsResponse ExpirationResponse(string symbol, DateOnly expiration)
        => new(symbol, [expiration], 1, DateTimeOffset.UtcNow);

    private static OptionsChainResponse ChainResponse(
        string symbol,
        DateOnly expiration,
        decimal underlyingPrice)
        => new(
            symbol,
            underlyingPrice,
            expiration,
            30,
            "Option",
            underlyingPrice,
            1.25m,
            1.10m,
            [],
            [],
            0,
            DateTimeOffset.UtcNow);

    private static async Task<T?> WaitForCancellationAsync<T>(
        TaskCompletionSource started,
        TaskCompletionSource exited,
        CancellationToken ct) where T : class
    {
        started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        }
        finally
        {
            exited.TrySetResult();
        }
    }

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> NewCompletion<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ControllableOptionsApiClient : IOptionsApiClient
    {
        public Func<CancellationToken, Task<OptionsSummaryResponse?>> Summary { get; set; } =
            _ => Task.FromResult<OptionsSummaryResponse?>(null);
        public Func<CancellationToken, Task<IReadOnlyList<string>?>> Underlyings { get; set; } =
            _ => Task.FromResult<IReadOnlyList<string>?>([]);
        public Func<string, CancellationToken, Task<OptionsExpirationsResponse?>> Expirations { get; set; } =
            (_, _) => Task.FromResult<OptionsExpirationsResponse?>(null);
        public Func<string, string, CancellationToken, Task<OptionsChainResponse?>> Chain { get; set; } =
            (_, _, _) => Task.FromResult<OptionsChainResponse?>(null);

        public Task<OptionsSummaryResponse?> GetOptionsSummaryAsync(CancellationToken ct)
            => Summary(ct);

        public Task<IReadOnlyList<string>?> GetOptionsTrackedUnderlyingsAsync(CancellationToken ct)
            => Underlyings(ct);

        public Task<OptionsExpirationsResponse?> GetOptionsExpirationsAsync(
            string symbol,
            CancellationToken ct)
            => Expirations(symbol, ct);

        public Task<OptionsChainResponse?> GetOptionsChainAsync(
            string symbol,
            string expiration,
            CancellationToken ct)
            => Chain(symbol, expiration, ct);
    }
}
