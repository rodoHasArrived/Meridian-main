namespace Meridian.Infrastructure.CppTrader.Replay;

public interface ICppTraderReplayService
{
    Task<string> CreateReplaySessionAsync(CancellationToken ct = default);
}
