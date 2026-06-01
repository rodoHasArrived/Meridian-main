using Meridian.Infrastructure.CppTrader.Host;
using Meridian.Infrastructure.CppTrader.Options;

namespace Meridian.Infrastructure.CppTrader.Providers;

/// <summary>
/// Opens ITCH ingestion sessions on the external CppTrader host process and returns the session identifier.
/// The host will begin streaming market data for the configured symbols once the session is active.
/// </summary>
public sealed class CppTraderItchIngestionService(
    ICppTraderHostManager hostManager,
    IOptionsMonitor<CppTraderOptions> optionsMonitor) : ICppTraderItchIngestionService
{
    private readonly ICppTraderHostManager _hostManager = hostManager;
    private readonly IOptionsMonitor<CppTraderOptions> _optionsMonitor = optionsMonitor;

    /// <summary>
    /// Opens a new ITCH ingestion session for the specified source and returns its session identifier.
    /// </summary>
    /// <param name="sourceId">Logical source identifier (e.g. feed name or venue code) passed as the session name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session ID assigned by the host.</returns>
    /// <exception cref="InvalidOperationException">Thrown when ITCH ingestion is disabled in configuration.</exception>
    public async Task<string> OpenFeedAsync(string sourceId, CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled || !options.Features.ItchIngestionEnabled)
            throw new InvalidOperationException(
                "CppTrader ITCH ingestion is disabled. Set CppTrader.Enabled=true and CppTrader.Features.ItchIngestionEnabled=true.");

        var session = await _hostManager.CreateSessionAsync(
            CppTraderSessionKind.Ingest,
            sessionName: sourceId,
            ct).ConfigureAwait(false);

        return session.SessionId;
    }
}
