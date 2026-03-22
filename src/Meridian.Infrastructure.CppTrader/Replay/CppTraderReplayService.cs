namespace Meridian.Infrastructure.CppTrader.Replay;

public sealed class CppTraderReplayService : ICppTraderReplayService
{
    public Task<string> CreateReplaySessionAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("CppTrader replay integration is scaffolded but not yet wired to a vendored native host.");
}
