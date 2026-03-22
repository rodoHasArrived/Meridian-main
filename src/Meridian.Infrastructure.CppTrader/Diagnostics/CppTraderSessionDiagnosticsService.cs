namespace Meridian.Infrastructure.CppTrader.Diagnostics;

public sealed class CppTraderSessionDiagnosticsService : ICppTraderSessionDiagnosticsService
{
    private readonly ConcurrentDictionary<string, CppTraderSessionDiagnostic> _sessions = new(StringComparer.Ordinal);

    public IReadOnlyList<CppTraderSessionDiagnostic> GetSessions() =>
        _sessions.Values.OrderBy(session => session.CreatedAt).ToList();

    public void TrackSession(string sessionId, CppTraderSessionDiagnostic diagnostic) =>
        _sessions[sessionId] = diagnostic;

    public void RemoveSession(string sessionId) =>
        _sessions.TryRemove(sessionId, out _);
}
