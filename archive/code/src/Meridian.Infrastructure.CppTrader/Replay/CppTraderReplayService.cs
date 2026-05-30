using Meridian.Infrastructure.CppTrader.Host;
using Meridian.Infrastructure.CppTrader.Options;

namespace Meridian.Infrastructure.CppTrader.Replay;

/// <summary>
/// Creates replay sessions on the external CppTrader host process and returns the session identifier.
/// Callers use the returned session ID to correlate subsequent replay events received via the host.
/// </summary>
public sealed class CppTraderReplayService(
    ICppTraderHostManager hostManager,
    IOptionsMonitor<CppTraderOptions> optionsMonitor) : ICppTraderReplayService
{
    private readonly ICppTraderHostManager _hostManager = hostManager;
    private readonly IOptionsMonitor<CppTraderOptions> _optionsMonitor = optionsMonitor;

    /// <summary>
    /// Opens a new replay session on the CppTrader host and returns its session identifier.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session ID assigned by the host.</returns>
    /// <exception cref="InvalidOperationException">Thrown when replay is disabled in configuration.</exception>
    public async Task<string> CreateReplaySessionAsync(CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled || !options.Features.ReplayEnabled)
            throw new InvalidOperationException(
                "CppTrader replay integration is disabled. Set CppTrader.Enabled=true and CppTrader.Features.ReplayEnabled=true.");

        var session = await _hostManager.CreateSessionAsync(
            CppTraderSessionKind.Replay,
            sessionName: "meridian-replay",
            ct).ConfigureAwait(false);

        return session.SessionId;
    }
}
