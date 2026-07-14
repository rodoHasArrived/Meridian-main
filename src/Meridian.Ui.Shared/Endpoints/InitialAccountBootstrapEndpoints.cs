using Meridian.Identity;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class InitialAccountBootstrapEndpoints
{
    public static void MapInitialAccountBootstrapEndpoints(this WebApplication app)
    {
        app.MapGet("/setup/account", (InitialAccountBootstrapService bootstrap) =>
            bootstrap.IsAvailable
                ? Results.Content(AccountPage, "text/html; charset=utf-8")
                : Results.Redirect("/login"))
            .ExcludeFromDescription();

        app.MapPost("/api/auth/bootstrap", async (HttpContext context, BootstrapAccountRequest request, InitialAccountBootstrapService bootstrap, CancellationToken ct) =>
        {
            var session = await bootstrap.CreateAsync(context.Connection.RemoteIpAddress, request.Token, request.Username, request.Password, ct).ConfigureAwait(false);
            if (session is null)
            {
                return Results.Json(new { error = "The setup link is invalid, expired, or an account already exists. Passwords must contain at least 12 characters." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var secure = CookieCsrfProtection.ShouldUseSecureCookies(context);
            context.Response.Cookies.Append(LoginSessionMiddleware.SessionCookieName, session, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = secure,
                Path = "/",
                Expires = DateTimeOffset.UtcNow + LoginSessionService.SessionDuration
            });
            CookieCsrfProtection.IssueCsrfCookie(context, secure, LoginSessionService.SessionDuration);
            return Results.Ok(new { redirect = "/workstation/setup" });
        }).ExcludeFromDescription();
    }

    private sealed record BootstrapAccountRequest(string Token, string Username, string Password);

    private const string AccountPage = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Create your Meridian login</title>
<style>body{margin:0;background:#07111f;color:#e7eef8;font:16px system-ui;display:grid;min-height:100vh;place-items:center}.card{width:min(30rem,calc(100% - 3rem));padding:2.5rem;border:1px solid #26374d;border-radius:24px;background:#0e1a2b;box-shadow:0 20px 60px #0008}h1{font-size:2rem;margin:.5rem 0}p{color:#a9b8ca;line-height:1.55}label{display:block;margin:1.25rem 0 .4rem}input{box-sizing:border-box;width:100%;padding:.8rem;border:1px solid #42546d;border-radius:10px;background:#07111f;color:white}button{width:100%;margin-top:1.5rem;padding:.9rem;border:0;border-radius:10px;background:#37c8dc;color:#04232b;font-weight:700}#error{color:#ffb4b4}</style></head><body><main class="card"><small>MERIDIAN · LOCAL SETUP</small><h1>Create your Meridian login</h1><p>This administrator stays on this computer. The one-use setup token is kept in the URL fragment and is never sent in a page request.</p><form id="form"><label for="username">Username</label><input id="username" autocomplete="username" required maxlength="80"><label for="password">Password</label><input id="password" type="password" autocomplete="new-password" minlength="12" required><p>Use at least 12 characters.</p><button>Create login and continue</button><p id="error" role="alert"></p></form></main><script>
const token=new URLSearchParams(location.hash.slice(1)).get('token')||''; history.replaceState(null,'',location.pathname); document.getElementById('form').addEventListener('submit',async e=>{e.preventDefault();const error=document.getElementById('error');error.textContent='';const response=await fetch('/api/auth/bootstrap',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({token,username:document.getElementById('username').value,password:document.getElementById('password').value})});const body=await response.json();if(response.ok)location.assign(body.redirect);else error.textContent=body.error||'Setup could not continue.'});
</script></body></html>
""";
}
