using System.Net;

namespace Meridian.Ui.Services;

/// <summary>
/// Process-wide HTTP session state for the desktop workstation's API clients.
/// The named "api-client" and "backfill-client" handlers share this cookie container, so a
/// single <see cref="ApiClientService.AuthenticateAsync"/> call establishes the server login
/// session for every consumer, and mutating requests can echo the CSRF cookie the server
/// issued alongside it.
/// The cookie and header names mirror the authoritative server-side definitions
/// (LoginSessionMiddleware and CookieCsrfProtection in Meridian.Ui.Shared, which this
/// project cannot reference); LifecycleControlClient duplicates them the same way.
/// </summary>
public static class ApiClientSession
{
    public const string SessionCookieName = "mdc-session";
    public const string CsrfCookieName = "mdc-csrf";
    public const string CsrfHeaderName = "X-CSRF-Token";

    /// <summary>
    /// Cookie container shared by every "api-client"/"backfill-client" HttpClient handler.
    /// </summary>
    public static CookieContainer Cookies { get; } = new();

    /// <summary>
    /// Returns the CSRF token the server issued for the given base URL, or null when no
    /// login session has been established.
    /// </summary>
    public static string? GetCsrfToken(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        return Cookies.GetCookies(baseUri)[CsrfCookieName]?.Value;
    }

    /// <summary>
    /// Expires every cookie stored for the given base URL (desktop sign-out).
    /// </summary>
    public static void Clear(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return;
        }

        foreach (Cookie cookie in Cookies.GetCookies(baseUri))
        {
            cookie.Expired = true;
        }
    }
}
