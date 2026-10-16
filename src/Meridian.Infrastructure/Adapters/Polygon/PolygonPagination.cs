namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Keeps Polygon continuation requests on the trusted REST origin and gives cursors
/// a stable identity independent of credentials and query parameter order.
/// </summary>
internal static class PolygonPagination
{
    private static readonly Uri RestOrigin = new(PolygonEndpoints.RestBase);

    public static Uri GetAuthenticatedPageUri(string url, string apiKey)
    {
        if (!Uri.TryCreate(RestOrigin, url, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, RestOrigin.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != RestOrigin.Port
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Polygon returned an invalid pagination URL.");
        }

        var parameters = GetNonCredentialParameters(uri).ToList();
        parameters.Add($"apiKey={Uri.EscapeDataString(apiKey)}");
        return new UriBuilder(uri) { Query = string.Join("&", parameters) }.Uri;
    }

    public static string GetPageIdentity(Uri uri)
        => new UriBuilder(uri)
        {
            Query = string.Join("&", GetNonCredentialParameters(uri).OrderBy(value => value, StringComparer.Ordinal))
        }.Uri.AbsoluteUri;

    private static IEnumerable<string> GetNonCredentialParameters(Uri uri)
        => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !string.Equals(
                Uri.UnescapeDataString(parameter.Split('=', 2)[0]), "apiKey", StringComparison.OrdinalIgnoreCase));
}
