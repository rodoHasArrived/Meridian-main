using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Host;

public sealed class CppTraderHostManager(
    IOptionsMonitor<CppTraderOptions> optionsMonitor,
    ILogger<CppTraderHostManager> logger) : ICppTraderHostManager
{
    private readonly IOptionsMonitor<CppTraderOptions> _optionsMonitor = optionsMonitor;
    private readonly ILogger<CppTraderHostManager> _logger = logger;
    private int _activeSessions;
    private long _totalFills;
    private long _totalRejects;
    private long _totalSnapshots;
    private DateTimeOffset? _lastHeartbeat;
    private DateTimeOffset? _lastFaultAt;
    private string? _lastFault;

    public async Task<ICppTraderSessionClient> CreateSessionAsync(
        CppTraderSessionKind sessionKind,
        string? sessionName = null,
        CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
            throw new InvalidOperationException("CppTrader integration is disabled.");

        if (string.IsNullOrWhiteSpace(options.AdapterExecutablePath))
            throw new InvalidOperationException("CppTrader adapter executable path is not configured.");

        var executablePath = Path.GetFullPath(options.AdapterExecutablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException($"CppTrader adapter executable was not found at '{executablePath}'.", executablePath);

        if (Volatile.Read(ref _activeSessions) >= options.MaxConcurrentSessions)
            throw new InvalidOperationException($"CppTrader session limit of {options.MaxConcurrentSessions} was reached.");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = string.Empty,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start CppTrader adapter at '{executablePath}'.");

        _ = Task.Run(async () =>
        {
            try
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(stderr))
                    _logger.LogWarning("CppTrader host stderr: {StdErr}", stderr);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read CppTrader host stderr.");
            }
        }, ct);

        var bootstrapProtocol = new LengthPrefixedProtocolStream(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);

        var requestId = Guid.NewGuid().ToString("N");
        await bootstrapProtocol.WriteAsync(
            CppTraderProtocolNames.CreateSession,
            requestId,
            sessionId: null,
            new CreateSessionRequest(sessionKind, sessionName),
            ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.StartupTimeout);
        var responseEnvelope = await bootstrapProtocol.ReadAsync(timeoutCts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("CppTrader host closed before returning a session id.");

        if (!string.Equals(responseEnvelope.MessageType, CppTraderProtocolNames.CreateSessionResponse, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected bootstrap response '{responseEnvelope.MessageType}'.");

        var session = responseEnvelope.Payload.Deserialize(CppTraderJsonContext.Default.CreateSessionResponse)
            ?? throw new InvalidDataException("CppTrader host returned an empty session response.");

        Interlocked.Increment(ref _activeSessions);
        _logger.LogInformation(
            "Started CppTrader {SessionKind} session {SessionId} via {ExecutablePath}",
            sessionKind,
            session.SessionId,
            executablePath);

        return new HostManagedSession(
            new ProcessBackedCppTraderSessionClient(process, session.SessionId, sessionKind, _logger),
            onDispose: () => Interlocked.Decrement(ref _activeSessions));
    }

    public HostHealthSnapshot GetHealthSnapshot() => new(
        Enabled: _optionsMonitor.CurrentValue.Enabled,
        HostHealthy: string.IsNullOrWhiteSpace(_lastFault),
        ActiveSessions: Volatile.Read(ref _activeSessions),
        OutstandingOrders: 0,
        TotalFills: Interlocked.Read(ref _totalFills),
        TotalRejects: Interlocked.Read(ref _totalRejects),
        TotalSnapshots: Interlocked.Read(ref _totalSnapshots),
        LastHeartbeat: _lastHeartbeat,
        LastFaultAt: _lastFaultAt,
        LastFault: _lastFault);

    public void RecordFault(string message)
    {
        _lastFault = message;
        _lastFaultAt = DateTimeOffset.UtcNow;
    }

    public void RecordHeartbeat()
    {
        _lastHeartbeat = DateTimeOffset.UtcNow;
        _lastFault = null;
    }

    public void RecordExecutionUpdate(bool rejected)
    {
        if (rejected)
            Interlocked.Increment(ref _totalRejects);
        else
            Interlocked.Increment(ref _totalFills);
    }

    public void RecordSnapshot() => Interlocked.Increment(ref _totalSnapshots);

    private sealed class HostManagedSession(ICppTraderSessionClient inner, Action onDispose) : ICppTraderSessionClient
    {
        private readonly ICppTraderSessionClient _inner = inner;
        private readonly Action _onDispose = onDispose;
        private int _disposed;

        public string SessionId => _inner.SessionId;

        public CppTraderSessionKind SessionKind => _inner.SessionKind;

        public Task<RegisterSymbolResponse> RegisterSymbolAsync(RegisterSymbolRequest request, CancellationToken ct = default) =>
            _inner.RegisterSymbolAsync(request, ct);

        public Task<SubmitOrderResponse> SubmitOrderAsync(SubmitOrderRequest request, CancellationToken ct = default) =>
            _inner.SubmitOrderAsync(request, ct);

        public Task<CancelOrderResponse> CancelOrderAsync(CancelOrderRequest request, CancellationToken ct = default) =>
            _inner.CancelOrderAsync(request, ct);

        public Task<GetSnapshotResponse> GetSnapshotAsync(GetSnapshotRequest request, CancellationToken ct = default) =>
            _inner.GetSnapshotAsync(request, ct);

        public Task<HeartbeatResponse> HeartbeatAsync(CancellationToken ct = default) =>
            _inner.HeartbeatAsync(ct);

        public IAsyncEnumerable<CppTraderEnvelope> ReadEventsAsync(CancellationToken ct = default) =>
            _inner.ReadEventsAsync(ct);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await _inner.DisposeAsync().ConfigureAwait(false);
            _onDispose();
        }
    }
}
