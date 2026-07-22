using Meridian.Contracts.Api;
using Meridian.Ui.Services.ProviderDiagnostics;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderAccountingViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshAsync_ProjectsRegistrationFailuresCurrentWindowAndRetryPosture()
    {
        var viewModel = new ProviderAccountingViewModel(
            new StubProviderDiagnosticsApiClient(BuildCatalog(), BuildRateLimits(), BuildConnectionHealth(null)),
            new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        viewModel.RegistrationTitle.Should().Be("1 provider registration failure");
        viewModel.RegistrationFailures.Should().ContainSingle()
            .Which.Error.Should().Be("InvalidOperationException: Provider construction failed.");
        viewModel.RateLimits.Should().ContainSingle();
        var rateLimit = viewModel.RateLimits[0];
        rateLimit.RequestUsage.Should().Be("8 / 10");
        rateLimit.Remaining.Should().Be("2");
        rateLimit.ResetCountdown.Should().Be("1m 5s");
        rateLimit.FailureReason.Should().Be("Current rate-limit reason: provider response.");
        rateLimit.RetryPosture.Should().Be("Retry after 1m 5s.");
        rateLimit.ConnectionPosture.Should().Be("Unknown — reachability unavailable; no runtime diagnostics.");
        viewModel.HistoryPosture.Should().Contain("not retained");
    }

    [Fact]
    public async Task RefreshAsync_TypeLoadFailureDoesNotRenderAsZeroFailures()
    {
        var catalog = BuildCatalog();
        catalog = catalog with
        {
            RegistrationReport = catalog.RegistrationReport! with
            {
                FailedModuleCount = 0,
                Failures =
                [
                    new ProviderRegistrationFailureDto(
                        "type-load",
                        "Meridian.Providers.BrokenAssembly",
                        null,
                        nameof(TypeLoadException),
                        "Provider type could not load.")
                ]
            }
        };
        var viewModel = new ProviderAccountingViewModel(
            new StubProviderDiagnosticsApiClient(catalog, BuildRateLimits(), BuildConnectionHealth(null)),
            new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        viewModel.RegistrationTitle.Should().Be("1 provider registration failure");
        viewModel.RegistrationFailures.Should().ContainSingle();
    }

    [Fact]
    public void BuildRateLimitPresentation_WithUnavailableRuntime_DoesNotInferCapacityOrHistory()
    {
        var snapshot = BuildRateLimits().Providers[0] with
        {
            StateAvailable = false,
            RequestsInWindow = null,
            RemainingRequests = null,
            IsRateLimited = false,
            ResetAt = null,
            Reason = null
        };

        var row = ProviderAccountingViewModel.BuildRateLimitPresentation(
            snapshot,
            Now,
            BuildConnectionHealth(false).Providers[0]);

        row.Status.Should().Be("State unavailable");
        row.RequestUsage.Should().Be("Unavailable / 10");
        row.Remaining.Should().Be("Unavailable");
        row.FailureReason.Should().Contain("history is not retained");
        row.RetryPosture.Should().Contain("unavailable");
        row.ConnectionPosture.Should().Be("Disconnected — runtime probe reports unreachable (socket closed).");
        row.HistoryPosture.Should().Contain("not retained");
    }

    [Theory]
    [InlineData("disabled", false, true, 0, "Disabled — provider runtime is not enabled.")]
    [InlineData("reconnecting", true, false, 3, "Reconnecting — attempt 3; runtime is recovering.")]
    [InlineData("degraded", true, false, 0, "Degraded — runtime lost healthy reachability.")]
    [InlineData("connected", true, true, 0, "Connected — runtime probe reports reachable.")]
    [InlineData("disconnected", true, false, 0, "Disconnected — runtime probe reports unreachable.")]
    public void BuildRateLimitPresentation_DistinguishesRuntimeConnectionStates(
        string connectionState,
        bool isEnabled,
        bool isConnected,
        int reconnectAttempts,
        string expected)
    {
        var connection = BuildConnectionHealth(isConnected).Providers[0] with
        {
            IsEnabled = isEnabled,
            ConnectionState = connectionState,
            LastFailureKind = null,
            ReconnectAttempts = reconnectAttempts
        };

        var row = ProviderAccountingViewModel.BuildRateLimitPresentation(
            BuildRateLimits().Providers[0],
            Now,
            connection);

        row.ConnectionPosture.Should().Be(expected);
    }

    [Fact]
    public void ProviderPageSource_BindsTruthfulRuntimeAccountingFields()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ProviderPage.xaml"));

        xaml.Should().Contain("ProviderRuntimeAccountingTab");
        xaml.Should().Contain("{Binding ProviderAccounting.RegistrationFailures}");
        xaml.Should().Contain("{Binding ProviderAccounting.RateLimits}");
        xaml.Should().Contain("{Binding ResetCountdown}");
        xaml.Should().Contain("{Binding FailureReason}");
        xaml.Should().Contain("{Binding RetryPosture}");
        xaml.Should().Contain("{Binding ConnectionPosture}");
        xaml.Should().Contain("{Binding ProviderAccounting.HistoryPosture");
    }

    private static ProviderCatalogResponse BuildCatalog()
        => new(
            Array.Empty<ProviderCatalogEntry>(),
            TotalCount: 0,
            Timestamp: Now,
            Source: "registry",
            RegistrationReport: new ProviderRegistrationReportDto(
                GeneratedAt: Now,
                DiscoveredSourceCount: 4,
                ModuleCandidateCount: 3,
                ModuleActivationAttemptCount: 3,
                ModuleRegistrationAttemptCount: 2,
                RegisteredModuleCount: 1,
                SkippedModuleCount: 1,
                FailedModuleCount: 1,
                IsHealthy: false,
                Failures:
                [
                    new ProviderRegistrationFailureDto(
                        "activate",
                        "Meridian.Infrastructure.Adapters.NYSE.NyseProviderModule",
                        "nyse-module",
                        nameof(InvalidOperationException),
                        "Provider construction failed.")
                ]));

    private static ProviderRateLimitsResponse BuildRateLimits()
        => new(
            [
                new ProviderRateLimitSnapshotDto(
                    Provider: "nyse",
                    Name: "nyse",
                    DisplayName: "NYSE",
                    Priority: 1,
                    Capabilities: new ProviderRateLimitCapabilitiesDto(
                        AdjustedPrices: true,
                        Intraday: true,
                        Dividends: true,
                        Splits: true,
                        Quotes: true,
                        Trades: true,
                        Auctions: true,
                        SupportedMarkets: ["US"]),
                    Surface: "historical",
                    StateAvailable: true,
                    ObservedAt: Now,
                    RequestsInWindow: 8,
                    MaxRequestsPerWindow: 10,
                    RemainingRequests: 2,
                    WindowSeconds: 60,
                    UsageRatio: 0.8,
                    IsRateLimited: true,
                    ResetAt: Now.AddSeconds(65),
                    Reason: "provider-response")
            ],
            Timestamp: Now);

    private static ProviderConnectionHealthResponse BuildConnectionHealth(bool? isConnected)
        => new(
            [
                new ProviderConnectionHealthSnapshotDto(
                    ProviderId: "nyse",
                    DisplayName: "NYSE",
                    IsEnabled: true,
                    IsConnected: isConnected,
                    ConnectionState: isConnected is null ? "unknown" : isConnected.Value ? "connected" : "disconnected",
                    DiagnosticsAvailable: isConnected is not null,
                    LastFailureKind: isConnected == false ? "socket-closed" : null)
            ],
            Timestamp: Now);

    private sealed class StubProviderDiagnosticsApiClient(
        ProviderCatalogResponse catalog,
        ProviderRateLimitsResponse rateLimits,
        ProviderConnectionHealthResponse connectionHealth) : IProviderDiagnosticsApiClient
    {
        public Task<ProviderCatalogResponse> GetCatalogAsync(CancellationToken ct = default)
            => Task.FromResult(catalog);

        public Task<ProviderRateLimitsResponse> GetRateLimitsAsync(CancellationToken ct = default)
            => Task.FromResult(rateLimits);

        public Task<ProviderConnectionHealthResponse> GetConnectionHealthAsync(CancellationToken ct = default)
            => Task.FromResult(connectionHealth);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
