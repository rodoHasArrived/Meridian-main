namespace Meridian.Infrastructure.CppTrader.Providers;

public sealed class CppTraderItchIngestionService : ICppTraderItchIngestionService
{
    public Task<string> OpenFeedAsync(string sourceId, CancellationToken ct = default) =>
        throw new NotSupportedException("CppTrader ITCH ingestion is scaffolded but not yet wired to a vendored native host.");
}
