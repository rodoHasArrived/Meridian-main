using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.ProviderSdk;

/// <summary>Provider-neutral request for an account or model-account P&amp;L stream.</summary>
public sealed record ProviderPnlStreamRequest(string AccountId, string? ModelAccountId = null);

/// <summary>Timestamped account P&amp;L update emitted by a provider-neutral stream.</summary>
public sealed record ProviderPnlUpdate(DateTimeOffset ObservedAt, ProviderAccountPnl Pnl);

/// <summary>Optional provider capability for streaming account and model-account P&amp;L.</summary>
public interface IProviderPnlStream : IProviderMetadata
{
    IAsyncEnumerable<ProviderPnlUpdate> StreamAsync(ProviderPnlStreamRequest request, CancellationToken ct = default);
}
