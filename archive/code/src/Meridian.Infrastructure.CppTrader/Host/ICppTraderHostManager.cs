using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Host;

public interface ICppTraderHostManager
{
    Task<ICppTraderSessionClient> CreateSessionAsync(
        CppTraderSessionKind sessionKind,
        string? sessionName = null,
        CancellationToken ct = default);

    HostHealthSnapshot GetHealthSnapshot();

    void RecordFault(string message);

    void RecordHeartbeat();

    void RecordExecutionUpdate(bool rejected);

    void RecordSnapshot();
}
