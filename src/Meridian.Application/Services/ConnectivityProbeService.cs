using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Application.Logging;
using Meridian.Contracts.Services;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Sealed implementation that probes connectivity every 60 seconds using HTTP GET to Google's connectivity check endpoint.
/// </summary>
public sealed class ConnectivityProbeService : IConnectivityProbeService, IDisposable
{
    private const string ConnectivityCheckUrl = "https://connectivitycheck.gstatic.com/generate_204";
    private const int ProbeIntervalSeconds = 60;
    private const int HttpTimeoutSeconds = 10;

    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly HttpClient? _httpClient;
    private readonly ILogger _log;
    private PeriodicTimer? _probeTimer;
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
    /// Initializes ConnectivityProbeService, optionally using IHttpClientFactory for HTTP calls.
    /// </summary>
    public ConnectivityProbeService(
        IHttpClientFactory? httpClientFactory = null,
        ILogger? log = null)
    {
        _httpClientFactory = httpClientFactory;
        _log = log ?? LoggingSetup.ForContext<ConnectivityProbeService>();

        // If no factory available, create a dedicated HttpClient with timeout
        if (_httpClientFactory == null)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
        }

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

        // Fire an immediate probe and then start the periodic loop
        _ = ProbeOnceAsync();
        _ = RunProbeLoopAsync();
    }

    /// <summary>
    /// Runs the periodic probe loop.
    /// </summary>
    private async Task RunProbeLoopAsync()
    {
        if (_probeTimer == null)
            return;

        try
        {
            while (await _probeTimer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                await ProbeOnceAsync().ConfigureAwait(false);
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
    private async Task ProbeOnceAsync()
    {
        try
        {
            using var client = _httpClientFactory?.CreateClient() ?? _httpClient;
            if (client == null)
                return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(HttpTimeoutSeconds));
            var response = await client.GetAsync(ConnectivityCheckUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token)
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

        _probeTimer?.Dispose();
        if (_httpClientFactory == null)
        {
            _httpClient?.Dispose();
        }

        _disposed = true;
    }
}
