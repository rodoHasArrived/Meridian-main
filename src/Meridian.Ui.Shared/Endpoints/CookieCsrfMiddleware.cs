using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Meridian.Ui.Shared.Endpoints;

internal static class CookieCsrfProtection
{
    public const string CsrfCookieName = "mdc-csrf";
    public const string CsrfHeaderName = "X-CSRF-Token";

    public static bool ShouldValidate(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) &&
            !HttpMethods.IsPut(context.Request.Method) &&
            !HttpMethods.IsPatch(context.Request.Method) &&
            !HttpMethods.IsDelete(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Login can originate from plain HTML forms and is exempt.
        if (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // CSRF applies to cookie-authenticated requests only.
        return !string.IsNullOrWhiteSpace(context.Request.Cookies[LoginSessionMiddleware.SessionCookieName]);
    }

    public static bool TryValidate(HttpContext context)
    {
        if (!ShouldValidate(context))
        {
            return true;
        }

        var cookieToken = context.Request.Cookies[CsrfCookieName];
        var headerToken = context.Request.Headers[CsrfHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(cookieToken) || string.IsNullOrWhiteSpace(headerToken))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(cookieToken);
        var suppliedBytes = Encoding.UTF8.GetBytes(headerToken);
        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    public static void EnsureCsrfCookie(HttpContext context, bool secureCookie, TimeSpan ttl)
    {
        if (!string.IsNullOrWhiteSpace(context.Request.Cookies[CsrfCookieName]))
        {
            return;
        }

        IssueCsrfCookie(context, secureCookie, ttl);
    }

    public static void IssueCsrfCookie(HttpContext context, bool secureCookie, TimeSpan ttl)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(
            CsrfCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
                Secure = secureCookie,
                Path = "/",
                Expires = DateTimeOffset.UtcNow + ttl
            });
    }

    public static void DeleteCsrfCookie(HttpContext context, bool secureCookie)
    {
        context.Response.Cookies.Delete(
            CsrfCookieName,
            new CookieOptions
            {
                Path = "/",
                SameSite = SameSiteMode.Strict,
                Secure = secureCookie
            });
    }

    public static bool ShouldUseSecureCookies(HttpContext context)
    {
        if (context.Request.IsHttps)
        {
            return true;
        }

        var environment = context.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        return environment is null || (!environment.IsDevelopment() && !environment.IsEnvironment("Test"));
    }
}

public sealed class CookieCsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!CookieCsrfProtection.TryValidate(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"error":"CSRF validation failed."}""");
            return;
        }

        await next(context);
    }
}

public static class CookieCsrfMiddlewareExtensions
{
    public static IApplicationBuilder UseCookieCsrfProtection(this IApplicationBuilder app)
        => app.UseMiddleware<CookieCsrfMiddleware>();
}
