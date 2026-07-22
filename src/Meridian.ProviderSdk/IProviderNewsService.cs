using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.ProviderSdk;

/// <summary>Provider-neutral request for news associated with instruments or topics.</summary>
public sealed record ProviderNewsRequest(
    IReadOnlyList<string> Symbols,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<string>? Topics = null,
    int? Limit = null);

/// <summary>Provider-neutral news item suitable for routing and workstation consumption.</summary>
public sealed record ProviderNewsArticle(
    string Headline,
    DateTimeOffset PublishedAt,
    string Source,
    string? Summary,
    string? Url,
    IReadOnlyList<string>? Symbols,
    string? ProviderArticleId,
    ProviderDataProvenance Provenance);

/// <summary>Optional provider capability for retrieving market news.</summary>
public interface IProviderNewsService : IProviderMetadata
{
    Task<IReadOnlyList<ProviderNewsArticle>> GetNewsAsync(ProviderNewsRequest request, CancellationToken ct = default);
}
