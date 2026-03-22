namespace Meridian.Infrastructure.CppTrader.Diagnostics;

public interface ICppTraderSessionDiagnosticsService
{
    IReadOnlyList<CppTraderSessionDiagnostic> GetSessions();

    void TrackSession(string sessionId, CppTraderSessionDiagnostic diagnostic);

    void RemoveSession(string sessionId);
}
