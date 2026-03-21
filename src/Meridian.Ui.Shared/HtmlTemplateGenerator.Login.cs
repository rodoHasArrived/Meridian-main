namespace Meridian.Ui.Shared;

public static partial class HtmlTemplateGenerator
{
    /// <summary>
    /// Generates the login page HTML.
    /// Reuses the same dark terminal theme as the main dashboard.
    /// </summary>
    /// <param name="returnUrl">URL to redirect to after a successful login.</param>
    /// <param name="hasError">When true, an invalid-credentials message is displayed.</param>
    public static string Login(string? returnUrl = null, bool hasError = false)
    {
        var errorHtml = hasError
            ? """<div class="login-error">Invalid username or password. Please try again.</div>"""
            : string.Empty;

        var safeReturnUrl = Escape(returnUrl ?? string.Empty);

        return $@"<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
  <title>Meridian Terminal &ndash; Sign In</title>
  <style>
{GetStyles()}

    .login-page {{
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      padding: 24px;
    }}

    .login-card {{
      background: var(--bg-secondary);
      border: 1px solid var(--border-default);
      border-radius: 12px;
      padding: 40px 48px;
      width: 100%;
      max-width: 400px;
      box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
    }}

    .login-logo {{
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 32px;
    }}

    .login-logo .logo-icon {{
      background: var(--accent-green-dim);
      color: var(--accent-green);
      font-family: var(--font-mono);
      font-weight: 700;
      font-size: 14px;
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      letter-spacing: 0.5px;
    }}

    .login-logo .logo-text {{
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary);
    }}

    .login-logo .logo-sub {{
      font-size: 13px;
      color: var(--text-muted);
    }}

    .login-error {{
      background: rgba(248, 81, 73, 0.12);
      border: 1px solid rgba(248, 81, 73, 0.4);
      color: var(--accent-red);
      padding: 12px 16px;
      border-radius: 6px;
      font-size: 14px;
      margin-bottom: 20px;
    }}

    .login-form .form-group {{
      margin-bottom: 18px;
    }}

    .login-form label {{
      display: block;
      font-size: 13px;
      font-weight: 500;
      color: var(--text-secondary);
      margin-bottom: 6px;
    }}

    .login-form input[type=""text""],
    .login-form input[type=""password""] {{
      width: 100%;
      background: var(--bg-tertiary);
      border: 1px solid var(--border-default);
      border-radius: 6px;
      color: var(--text-primary);
      font-size: 14px;
      padding: 10px 14px;
      transition: border-color 0.15s;
      box-sizing: border-box;
    }}

    .login-form input[type=""text""]:focus,
    .login-form input[type=""password""]:focus {{
      outline: none;
      border-color: var(--accent-blue);
      box-shadow: 0 0 0 3px rgba(88, 166, 255, 0.15);
    }}

    .login-form .btn-signin {{
      width: 100%;
      background: var(--accent-green-dim);
      color: var(--accent-green);
      border: 1px solid var(--accent-green-dim);
      border-radius: 6px;
      font-size: 14px;
      font-weight: 600;
      padding: 11px;
      cursor: pointer;
      transition: background 0.15s, box-shadow 0.15s;
      margin-top: 8px;
    }}

    .login-form .btn-signin:hover {{
      background: rgba(63, 185, 80, 0.25);
      box-shadow: var(--glow-green);
    }}
  </style>
</head>
<body>
  <div class=""login-page"">
    <div class=""login-card"">
      <div class=""login-logo"">
        <div class=""logo-icon"">MD</div>
        <div>
          <div class=""logo-text"">Meridian Terminal</div>
          <div class=""logo-sub"">Meridian</div>
        </div>
      </div>

      {errorHtml}

      <form class=""login-form"" action=""/api/auth/login"" method=""post"">
        <input type=""hidden"" name=""returnUrl"" value=""{safeReturnUrl}"" />

        <div class=""form-group"">
          <label for=""username"">Username</label>
          <input id=""username"" type=""text"" name=""username""
                 autocomplete=""username"" autofocus required />
        </div>

        <div class=""form-group"">
          <label for=""password"">Password</label>
          <input id=""password"" type=""password"" name=""password""
                 autocomplete=""current-password"" required />
        </div>

        <button type=""submit"" class=""btn-signin"">Sign In</button>
      </form>
    </div>
  </div>
</body>
</html>";
    }
}
