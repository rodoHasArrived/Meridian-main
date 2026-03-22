namespace Meridian.Infrastructure.CppTrader.Providers;

public interface ICppTraderItchIngestionService
{
    Task<string> OpenFeedAsync(string sourceId, CancellationToken ct = default);
}
