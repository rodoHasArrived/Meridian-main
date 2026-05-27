using Serilog;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Coordinates process-level startup and shutdown for CLI, desktop-local host,
/// and browser-workstation sessions.
/// </summary>
public interface IApplicationLifecycleCoordinator : IDisposable
{
    DateTimeOffset StartedAtUtc { get; }
    bool IsShutdownRequested { get; }
    string? ShutdownReason { get; }
    CancellationToken ShutdownToken { get; }
    string? LocalShutdownToken { get; }

    Task RequestShutdownAsync(string reason, string? detail = null, CancellationToken ct = default);
}

/// <summary>
/// Single process lifetime owner used by shared startup orchestration.
/// </summary>
public sealed class ApplicationLifecycleCoordinator : IApplicationLifecycleCoordinator
{
    public const string LocalShutdownTokenEnvironmentVariable = "MDC_SHUTDOWN_TOKEN";

    private readonly ILogger _log;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly CancellationTokenRegistration _externalShutdownRegistration;
    private int _shutdownRequested;
    private bool _disposed;

    private ApplicationLifecycleCoordinator(
        ILogger log,
        CancellationToken externalToken,
        string? localShutdownToken)
    {
        _log = log.ForContext<ApplicationLifecycleCoordinator>();
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        LocalShutdownToken = string.IsNullOrWhiteSpace(localShutdownToken)
            ? null
            : localShutdownToken;
        StartedAtUtc = DateTimeOffset.UtcNow;

        if (externalToken.CanBeCanceled)
        {
            _externalShutdownRegistration = externalToken.Register(() =>
            {
                if (Interlocked.CompareExchange(ref _shutdownRequested, 1, 0) == 0)
                {
                    ShutdownReason = "external-cancellation";
                    _log.Information("Application shutdown requested by external cancellation");
                }
            });
        }

        Console.CancelKeyPress += OnConsoleCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public DateTimeOffset StartedAtUtc { get; }

    public bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0 || _shutdownCts.IsCancellationRequested;

    public string? ShutdownReason { get; private set; }

    public CancellationToken ShutdownToken => _shutdownCts.Token;

    public string? LocalShutdownToken { get; }

    public static ApplicationLifecycleCoordinator Create(ILogger log, CancellationToken externalToken = default)
        => new(log, externalToken, Environment.GetEnvironmentVariable(LocalShutdownTokenEnvironmentVariable));

    public Task RequestShutdownAsync(string reason, string? detail = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Interlocked.CompareExchange(ref _shutdownRequested, 1, 0) == 0)
        {
            ShutdownReason = reason;
            if (string.IsNullOrWhiteSpace(detail))
            {
                _log.Information("Application shutdown requested ({Reason})", reason);
            }
            else
            {
                _log.Information("Application shutdown requested ({Reason}): {Detail}", reason, detail);
            }

            try
            {
                _shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                _log.Debug("Application lifecycle cancellation source was already disposed");
            }
        }
        else
        {
            _log.Debug("Application shutdown request ignored because shutdown is already in progress ({Reason})", reason);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Console.CancelKeyPress -= OnConsoleCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _externalShutdownRegistration.Dispose();
        _shutdownCts.Dispose();
    }

    private void OnConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _ = RequestShutdownAsync("console-cancel", "Ctrl+C or console close requested shutdown");
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        _ = RequestShutdownAsync("process-exit", "ProcessExit requested shutdown");
    }
}
