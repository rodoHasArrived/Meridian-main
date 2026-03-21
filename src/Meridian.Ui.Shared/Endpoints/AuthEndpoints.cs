using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering login/logout authentication endpoints.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps the login page, login API, and logout API endpoints.
    /// <list type="bullet">
    ///   <item>GET  /login            – returns the login page HTML.</item>
    ///   <item>POST /api/auth/login   – validates credentials, sets a session cookie.</item>
    ///   <item>POST /api/auth/logout  – invalidates the session cookie.</item>
    /// </list>
    /// </summary>
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // GET /login – serve the login page
        app.MapGet(UiApiRoutes.AuthLoginPage, (HttpContext context) =>
        {
            var returnUrl = context.Request.Query["returnUrl"].ToString();
            var hasError = context.Request.Query.ContainsKey("error");
            var html = HtmlTemplateGenerator.Login(returnUrl, hasError);
            return Results.Content(html, "text/html");
        }).ExcludeFromDescription();

        // POST /api/auth/login – authenticate and issue a session cookie
        app.MapPost(UiApiRoutes.AuthApiLogin, async (HttpContext context, LoginSessionService sessionService) =>
        {
            string? username, password, returnUrl;

            if (context.Request.HasJsonContentType())
            {
                var body = await context.Request.ReadFromJsonAsync<LoginRequest>();
                username = body?.Username;
                password = body?.Password;
                returnUrl = body?.ReturnUrl;
            }
            else
            {
                var form = await context.Request.ReadFormAsync();
                username = form["username"].ToString();
                password = form["password"].ToString();
                returnUrl = form["returnUrl"].ToString();
            }

            if (!sessionService.IsConfigured && !sessionService.AllowAnonymousWhenUnconfigured)
            {
                if (context.Request.HasJsonContentType())
                {
                    return Results.Json(
                        new
                        {
                            error = "Authentication is required but not configured. Set MDC_USERNAME and MDC_PASSWORD or configure MDC_AUTH_MODE=optional for local development."
                        },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Text(
                    "Authentication is required but not configured. Set MDC_USERNAME and MDC_PASSWORD or configure MDC_AUTH_MODE=optional for local development.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                if (context.Request.HasJsonContentType())
                    return Results.BadRequest(new { error = "Username and password are required." });

                var errorRedirect = BuildLoginRedirect(returnUrl, error: true);
                return Results.Redirect(errorRedirect);
            }

            var token = sessionService.CreateSession(username, password);
            if (token is null)
            {
                if (context.Request.HasJsonContentType())
                    return Results.Json(
                        new { error = "Invalid username or password." },
                        statusCode: StatusCodes.Status401Unauthorized);

                var errorRedirect = BuildLoginRedirect(returnUrl, error: true);
                return Results.Redirect(errorRedirect);
            }

            context.Response.Cookies.Append(
                LoginSessionMiddleware.SessionCookieName,
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow + LoginSessionService.SessionDuration
                });

            if (context.Request.HasJsonContentType())
                return Results.Ok(new { success = true });

            var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
            return Results.Redirect(redirect);
        }).ExcludeFromDescription();

        // POST /api/auth/logout – invalidate session and redirect to login
        app.MapPost(UiApiRoutes.AuthApiLogout, (HttpContext context, LoginSessionService sessionService) =>
        {
            var token = context.Request.Cookies[LoginSessionMiddleware.SessionCookieName];
            if (!string.IsNullOrWhiteSpace(token))
                sessionService.RemoveSession(token);

            context.Response.Cookies.Delete(LoginSessionMiddleware.SessionCookieName);
            return Results.Redirect(UiApiRoutes.AuthLoginPage);
        }).ExcludeFromDescription();
    }

    private static string BuildLoginRedirect(string? returnUrl, bool error)
    {
        var url = "/login";
        if (error)
            url += "?error=1";
        if (!string.IsNullOrWhiteSpace(returnUrl))
            url += $"{(error ? "&" : "?")}returnUrl={Uri.EscapeDataString(returnUrl)}";
        return url;
    }
}

/// <summary>Login request body for JSON clients.</summary>
internal sealed record LoginRequest(string? Username, string? Password, string? ReturnUrl);
