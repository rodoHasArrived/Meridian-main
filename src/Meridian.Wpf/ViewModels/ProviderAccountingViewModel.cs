using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Ui.Services.ProviderDiagnostics;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Projects provider registration and current rate-limit evidence for the desktop Data workspace.
/// Network refresh is operator-driven; a local timer updates only reset countdown labels.
/// </summary>
public sealed class ProviderAccountingViewModel : BindableBase
{
    public const string HistoryUnavailableText = "Unavailable — runtime rate-limit history is not retained.";

    private readonly IProviderDiagnosticsApiClient _apiClient;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _countdownTimer;
    private bool _isRefreshing;
    private string _statusMessage = "Provider runtime accounting has not loaded.";
    private string _registrationTitle = "Registration report unavailable";
    private string _registrationSummary = "Provider discovery evidence has not loaded.";
    private string _rateLimitSummary = "Current provider rate-limit state has not loaded.";
    private string? _errorMessage;

    public ProviderAccountingViewModel(
        IProviderDiagnosticsApiClient apiClient,
        TimeProvider? timeProvider = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _countdownTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdateCountdowns(),
            Dispatcher.CurrentDispatcher);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
    }

    public ObservableCollection<ProviderRegistrationFailurePresentation> RegistrationFailures { get; } = new();
    public ObservableCollection<ProviderRateLimitPresentation> RateLimits { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
                RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string RegistrationTitle
    {
        get => _registrationTitle;
        private set => SetProperty(ref _registrationTitle, value);
    }

    public string RegistrationSummary
    {
        get => _registrationSummary;
        private set => SetProperty(ref _registrationSummary, value);
    }

    public string RateLimitSummary
    {
        get => _rateLimitSummary;
        private set => SetProperty(ref _rateLimitSummary, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasRegistrationFailures => RegistrationFailures.Count > 0;
    public bool HasRateLimits => RateLimits.Count > 0;
    public string HistoryPosture => HistoryUnavailableText;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        _countdownTimer.Start();
    }

    public void Stop() => _countdownTimer.Stop();

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsRefreshing)
            return;

        IsRefreshing = true;
        ErrorMessage = null;
        StatusMessage = "Loading provider registration and current rate-limit state.";
        try
        {
            var catalogTask = _apiClient.GetCatalogAsync(ct);
            var rateLimitsTask = _apiClient.GetRateLimitsAsync(ct);
            var connectionHealthTask = _apiClient.GetConnectionHealthAsync(ct);
            await Task.WhenAll(catalogTask, rateLimitsTask, connectionHealthTask);
            ApplyRegistrationReport((await catalogTask).RegistrationReport);
            ApplyRateLimits(await rateLimitsTask, await connectionHealthTask, _timeProvider.GetUtcNow());
            StatusMessage = "Provider registration and current rate-limit state loaded.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusMessage = "Provider runtime accounting refresh was cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            RegistrationFailures.Clear();
            RateLimits.Clear();
            NotifyCollectionStateChanged();
            RegistrationTitle = "Registration report unavailable";
            RegistrationSummary = "Provider discovery health cannot be inferred because the current report did not load.";
            RateLimitSummary = "Current provider request capacity is unavailable.";
            ErrorMessage = ex.Message;
            StatusMessage = "Provider runtime accounting is unavailable.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyRegistrationReport(ProviderRegistrationReportDto? report)
    {
        RegistrationFailures.Clear();
        if (report is null)
        {
            RegistrationTitle = "Registration report unavailable";
            RegistrationSummary = "The provider catalog did not include discovery evidence; registration health cannot be inferred.";
            OnPropertyChanged(nameof(HasRegistrationFailures));
            return;
        }

        foreach (var failure in report.Failures)
        {
            RegistrationFailures.Add(new ProviderRegistrationFailurePresentation(
                FormatToken(failure.Stage),
                failure.Subject,
                failure.ModuleId ?? "Module unavailable",
                $"{failure.ErrorType}: {failure.ErrorMessage}"));
        }

        RegistrationTitle = report.IsHealthy
            ? "Provider registration healthy"
            : $"{RegistrationFailures.Count} provider registration failure{(RegistrationFailures.Count == 1 ? string.Empty : "s")}";
        RegistrationSummary = $"{report.RegisteredModuleCount} registered, {report.SkippedModuleCount} skipped, {report.DiscoveredSourceCount} discovered sources. Reported {report.GeneratedAt:yyyy-MM-dd HH:mm:ss 'UTC'}.";
        OnPropertyChanged(nameof(HasRegistrationFailures));
    }

    private void ApplyRateLimits(
        ProviderRateLimitsResponse response,
        ProviderConnectionHealthResponse connectionHealth,
        DateTimeOffset now)
    {
        var connections = connectionHealth.Providers.ToDictionary(
            provider => NormalizeProviderId(provider.ProviderId),
            StringComparer.OrdinalIgnoreCase);
        RateLimits.Clear();
        foreach (var provider in response.Providers)
        {
            connections.TryGetValue(NormalizeProviderId(provider.Provider), out var connection);
            RateLimits.Add(BuildRateLimitPresentation(provider, now, connection));
        }

        RateLimitSummary = RateLimits.Count == 0
            ? "No provider runtime exposed a current rate-limit snapshot."
            : $"{RateLimits.Count} current provider surface{(RateLimits.Count == 1 ? string.Empty : "s")} observed {response.Timestamp:yyyy-MM-dd HH:mm:ss 'UTC'}.";
        OnPropertyChanged(nameof(HasRateLimits));
    }

    private void UpdateCountdowns()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var row in RateLimits)
            row.UpdateCountdown(now);
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasRegistrationFailures));
        OnPropertyChanged(nameof(HasRateLimits));
    }

    public static ProviderRateLimitPresentation BuildRateLimitPresentation(
        ProviderRateLimitSnapshotDto provider,
        DateTimeOffset now,
        ProviderConnectionHealthSnapshotDto? connection = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var reason = NormalizeReason(provider.Reason);
        var resetCountdown = FormatResetCountdown(provider.ResetAt, now);
        var failureText = provider.IsRateLimited
            ? $"Current rate-limit reason: {reason ?? "reason unavailable"}."
            : reason is not null
                ? $"Current runtime reason: {reason}."
                : "Last rate-limit failure unavailable — history is not retained.";
        var retryPosture = !provider.StateAvailable
            ? "Retry posture unavailable until runtime diagnostics are exposed."
            : !provider.IsRateLimited
                ? "Requests may proceed within the reported window."
                : provider.ResetAt is { } resetAt
                    ? BuildLimitedRetryPosture(resetAt, now)
                    : "Retry is blocked; the provider did not report a reset time.";
        var connectionPosture = BuildConnectionPosture(connection);

        return new ProviderRateLimitPresentation(
            provider.DisplayName,
            FormatToken(provider.Surface),
            provider.StateAvailable ? provider.IsRateLimited ? "Rate limited" : "Available" : "State unavailable",
            provider.RequestsInWindow is { } requests
                ? $"{requests} / {provider.MaxRequestsPerWindow}"
                : $"Unavailable / {provider.MaxRequestsPerWindow}",
            provider.RemainingRequests?.ToString() ?? "Unavailable",
            provider.ResetAt,
            resetCountdown,
            failureText,
            retryPosture,
            connectionPosture,
            HistoryUnavailableText);
    }

    public static string FormatResetCountdown(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is null)
            return "No reset pending";

        var remaining = resetAt.Value - now;
        if (remaining <= TimeSpan.Zero)
            return "Reset due";

        var totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours}h {minutes}m {seconds}s"
            : minutes > 0 ? $"{minutes}m {seconds}s" : $"{seconds}s";
    }

    private static string BuildLimitedRetryPosture(DateTimeOffset resetAt, DateTimeOffset now)
        => resetAt <= now
            ? "Reset is due; refresh current state before retrying."
            : $"Retry after {FormatResetCountdown(resetAt, now).ToLowerInvariant()}.";

    private static string BuildConnectionPosture(ProviderConnectionHealthSnapshotDto? connection)
    {
        if (connection is null)
            return "Unknown — reachability unavailable; no runtime diagnostics.";
        if (!connection.IsEnabled || connection.ConnectionState.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            return "Disabled — provider runtime is not enabled.";
        if (!connection.DiagnosticsAvailable || connection.IsConnected is null)
            return "Unknown — reachability unavailable; no runtime diagnostics.";

        var failure = string.IsNullOrWhiteSpace(connection.LastFailureKind)
            ? string.Empty
            : $" ({NormalizeReason(connection.LastFailureKind)})";
        return connection.ConnectionState.Trim().ToLowerInvariant() switch
        {
            "reconnecting" => $"Reconnecting — attempt {connection.ReconnectAttempts ?? 0}; runtime is recovering{failure}.",
            "degraded" => $"Degraded — runtime lost healthy reachability{failure}.",
            "connecting" => "Connecting — runtime handshake is in progress.",
            "disconnecting" => "Disconnecting — runtime shutdown is in progress.",
            "failed" => $"Failed — runtime connection could not recover{failure}.",
            "connected" when connection.IsConnected == true => "Connected — runtime probe reports reachable.",
            _ => $"Disconnected — runtime probe reports unreachable{failure}."
        };
    }

    private static string? NormalizeReason(string? reason)
        => string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Replace("-", " ", StringComparison.Ordinal).Replace(":", ": ", StringComparison.Ordinal);

    private static string FormatToken(string value)
    {
        var normalized = value.Trim().Replace("-", " ", StringComparison.Ordinal);
        return normalized.Length == 0 ? "Unavailable" : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static string NormalizeProviderId(string value)
        => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public sealed record ProviderRegistrationFailurePresentation(
    string Stage,
    string Subject,
    string Module,
    string Error);

public sealed class ProviderRateLimitPresentation : BindableBase
{
    private string _resetCountdown;

    public ProviderRateLimitPresentation(
        string provider,
        string surface,
        string status,
        string requestUsage,
        string remaining,
        DateTimeOffset? resetAt,
        string resetCountdown,
        string failureReason,
        string retryPosture,
        string connectionPosture,
        string historyPosture)
    {
        Provider = provider;
        Surface = surface;
        Status = status;
        RequestUsage = requestUsage;
        Remaining = remaining;
        ResetAt = resetAt;
        _resetCountdown = resetCountdown;
        FailureReason = failureReason;
        RetryPosture = retryPosture;
        ConnectionPosture = connectionPosture;
        HistoryPosture = historyPosture;
    }

    public string Provider { get; }
    public string Surface { get; }
    public string Status { get; }
    public string RequestUsage { get; }
    public string Remaining { get; }
    public DateTimeOffset? ResetAt { get; }
    public string ResetCountdown
    {
        get => _resetCountdown;
        private set => SetProperty(ref _resetCountdown, value);
    }
    public string FailureReason { get; }
    public string RetryPosture { get; }
    public string ConnectionPosture { get; }
    public string HistoryPosture { get; }

    public void UpdateCountdown(DateTimeOffset now)
        => ResetCountdown = ProviderAccountingViewModel.FormatResetCountdown(ResetAt, now);
}
