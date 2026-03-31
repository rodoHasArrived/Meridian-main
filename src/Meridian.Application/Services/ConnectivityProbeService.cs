using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Application.Logging;
using Meridian.Contracts.Services;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Sealed implementation that probes connectivity every 60 seconds using HTTP GET to Google's connectivity check endpoint.
/// </summary>
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory and the configured connectivity-test client for HTTP lifecycle management")]
public sealed class ConnectivityProbeService : IConnectivityProbeService, IDisposable
{
    private const string ConnectivityCheckUrl = "https://connectivitycheck.gstatic.com/generate_204";
    private const int ProbeIntervalSeconds = 60;
    private const int HttpTimeoutSeconds = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _log;
    private PeriodicTimer? _probeTimer;
    private CancellationTokenSource? _probeCancellation;
    private bool _isOnline;
    private bool _disposed;

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (_isOnline != value)
            {
                _isOnline = value;
                ConnectivityChanged?.Invoke(this, _isOnline);
            }
        }
    }

    public event EventHandler<bool>? ConnectivityChanged;

    /// <summary>
    /// Initializes ConnectivityProbeService using <see cref="IHttpClientFactory"/> for HTTP calls.
    /// Per ADR-010, <see cref="HttpClient"/> is never instantiated directly.
    /// </summary>
    public ConnectivityProbeService(
        IHttpClientFactory httpClientFactory,
        ILogger? log = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _log = log ?? LoggingSetup.ForContext<ConnectivityProbeService>();

        // Start offline; first probe will update state
        _isOnline = false;
    }

    /// <summary>
    /// Starts the periodic connectivity probe.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ConnectivityProbeService));

        _probeTimer ??= new PeriodicTimer(TimeSpan.FromSeconds(ProbeIntervalSeconds));
        _probeCancellation ??= new CancellationTokenSource();

        // Fire an immediate probe and then start the periodic loop
        _ = ProbeOnceAsync(_probeCancellation.Token);
        _ = RunProbeLoopAsync(_probeCancellation.Token);
    }

    /// <summary>
    /// Runs the periodic probe loop.
    /// </summary>
    private async Task RunProbeLoopAsync(CancellationToken ct)
    {
        if (_probeTimer == null)
            return;

        try
        {
            while (await _probeTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await ProbeOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Timer was disposed
        }
    }

    /// <summary>
    /// Performs a single connectivity probe and updates IsOnline.
    /// </summary>
    private async Task ProbeOnceAsync(CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientNames.ConnectivityTest);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(HttpTimeoutSeconds));
            var response = await client.GetAsync(ConnectivityCheckUrl, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            // Success = 2xx status or 204 No Content
            IsOnline = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent;

            if (IsOnline)
            {
                _log.Debug("Connectivity probe succeeded");
            }
            else
            {
                _log.Debug("Connectivity probe returned non-success status: {StatusCode}", response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Service is stopping or the probe loop was canceled.
        }
        catch (OperationCanceledException)
        {
            IsOnline = false;
            _log.Debug("Connectivity probe timed out");
        }
        catch (HttpRequestException ex)
        {
            IsOnline = false;
            _log.Debug(ex, "Connectivity probe failed with HTTP error");
        }
        catch (Exception ex)
        {
            IsOnline = false;
            _log.Warning(ex, "Connectivity probe failed unexpectedly");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _probeCancellation?.Cancel();
        _probeCancellation?.Dispose();
        _probeTimer?.Dispose();
        _disposed = true;
    }
}
