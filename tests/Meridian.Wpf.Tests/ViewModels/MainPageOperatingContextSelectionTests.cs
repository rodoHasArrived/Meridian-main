using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

[Collection("DesktopAuthenticationEnvironment")]
public sealed class MainPageOperatingContextSelectionTests
{
    [Fact]
    public void SelectedOperatingContext_WhenLateFirstSelectionCompletes_KeepsLatestPickerContext()
    {
        WpfTestThread.Run(async () =>
        {
            var selector = new ControllableOperatingContextSelector();
            using var viewModel = CreateViewModel(selector);
            var contextA = CreateContext("account-a", "Picker A");
            var contextB = CreateContext("account-b", "Picker B");

            viewModel.SelectedOperatingContext = contextA;
            var selectionA = viewModel.OperatingContextSelectionTask;
            var requestA = await selector.WaitForRequestAsync(contextA.ContextKey);

            viewModel.SelectedOperatingContext = contextB;
            var selectionB = viewModel.OperatingContextSelectionTask;
            var requestB = await selector.WaitForRequestAsync(contextB.ContextKey);

            requestB.Complete(contextB with { DisplayName = "Effective B" });
            await selectionB;
            await DrainDispatcherAsync();
            viewModel.SelectedOperatingContext!.DisplayName.Should().Be("Effective B");

            requestA.Complete(contextA with { DisplayName = "Late A" });
            await selectionA;
            await DrainDispatcherAsync();

            viewModel.SelectedOperatingContext.ContextKey.Should().Be(contextB.ContextKey);
            viewModel.SelectedOperatingContext.DisplayName.Should().Be("Effective B");
        });
    }

    [Fact]
    public void SelectedOperatingContext_WhenSuperseded_CancelsPreviousSelectionToken()
    {
        WpfTestThread.Run(async () =>
        {
            var selector = new ControllableOperatingContextSelector();
            using var viewModel = CreateViewModel(selector);
            var contextA = CreateContext("account-a", "Picker A");
            var contextB = CreateContext("account-b", "Picker B");

            viewModel.SelectedOperatingContext = contextA;
            var selectionA = viewModel.OperatingContextSelectionTask;
            var requestA = await selector.WaitForRequestAsync(contextA.ContextKey);

            viewModel.SelectedOperatingContext = contextB;
            var selectionB = viewModel.OperatingContextSelectionTask;
            var requestB = await selector.WaitForRequestAsync(contextB.ContextKey);

            requestA.CancellationToken.IsCancellationRequested.Should().BeTrue();

            requestA.Complete(contextA);
            requestB.Complete(contextB);
            await Task.WhenAll(selectionA, selectionB);
            await DrainDispatcherAsync();
        });
    }

    [Fact]
    public void Dispose_CancelsSelectionAndRejectsLateObservableCommit()
    {
        WpfTestThread.Run(async () =>
        {
            var selector = new ControllableOperatingContextSelector();
            var viewModel = CreateViewModel(selector);
            var contextA = CreateContext("account-a", "Picker A");

            viewModel.SelectedOperatingContext = contextA;
            var selectionA = viewModel.OperatingContextSelectionTask;
            var requestA = await selector.WaitForRequestAsync(contextA.ContextKey);

            viewModel.Dispose();
            requestA.CancellationToken.IsCancellationRequested.Should().BeTrue();

            requestA.Complete(contextA with { DisplayName = "Late after disposal" });
            await selectionA;
            await DrainDispatcherAsync();

            viewModel.SelectedOperatingContext.Should().BeSameAs(contextA);
            viewModel.SelectedOperatingContext!.DisplayName.Should().Be("Picker A");
        });
    }

    private static MainPageViewModel CreateViewModel(ControllableOperatingContextSelector selector)
    {
        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        navigationService.Initialize(new Frame());

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        var fundContextService = new FundContextService(Path.Combine(
            Path.GetTempPath(),
            "meridian-main-page-context-race-tests",
            $"{Guid.NewGuid():N}.json"));

        return new MainPageViewModel(
            navigationService,
            fixtureModeDetector,
            fundContextService,
            selector.SelectAsync);
    }

    private static WorkstationOperatingContext CreateContext(string scopeId, string displayName)
        => new()
        {
            ScopeKind = OperatingContextScopeKind.Account,
            ScopeId = scopeId,
            AccountId = scopeId,
            DisplayName = displayName,
            DefaultWorkspaceId = "trading",
            DefaultLandingPageTag = "TradingShell"
        };

    private static async Task DrainDispatcherAsync()
    {
        await Task.Yield();
        // Fully qualified: this file's enclosing namespace chain reaches `Meridian`, where the
        // `Meridian.Application` namespace shadows the `System.Windows.Application` type that the
        // using directive imports.
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(
            static () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private sealed class ControllableOperatingContextSelector
    {
        private readonly ConcurrentDictionary<string, SelectionRequest> _requests =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<WorkstationOperatingContext?> SelectAsync(string contextKey, CancellationToken ct)
        {
            var request = new SelectionRequest(ct);
            if (!_requests.TryAdd(contextKey, request))
            {
                throw new InvalidOperationException($"A selection request already exists for {contextKey}.");
            }

            return request.Completion.Task;
        }

        public async Task<SelectionRequest> WaitForRequestAsync(string contextKey)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (_requests.TryGetValue(contextKey, out var request))
                {
                    return request;
                }

                await Task.Yield();
            }

            throw new TimeoutException($"No operating-context selection request was captured for {contextKey}.");
        }
    }

    private sealed class SelectionRequest(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;

        public TaskCompletionSource<WorkstationOperatingContext?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(WorkstationOperatingContext context) => Completion.TrySetResult(context);
    }
}
