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
            ? """
              <div class="login-error" role="alert">
                <strong>Sign-in failed</strong>
                <span>Invalid username or password. Please try again.</span>
              </div>
              """
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

    body {{
      background:
        radial-gradient(ellipse at 6% 0%, rgba(42, 178, 212, 0.08) 0%, transparent 40%),
        radial-gradient(ellipse at 96% 96%, rgba(96, 165, 250, 0.05) 0%, transparent 38%),
        linear-gradient(180deg, #08101a 0%, #08101a 100%);
    }}

    .login-page {{
      min-height: 100vh;
      padding: 28px;
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(360px, 440px);
      align-items: center;
      gap: 48px;
      max-width: 1180px;
      margin: 0 auto;
    }}

    .login-brief {{
      max-width: 560px;
    }}

    .login-eyebrow {{
      color: #2ab2d4;
      font-family: var(--font-mono);
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0.18em;
      margin-bottom: 18px;
      text-transform: uppercase;
    }}

    .login-title {{
      color: #dee6ef;
      font-size: clamp(40px, 7vw, 72px);
      font-weight: 700;
      line-height: 0.95;
      margin: 0 0 20px;
    }}

    .login-copy {{
      color: #9caabd;
      font-size: 16px;
      line-height: 1.65;
      margin: 0;
      max-width: 520px;
    }}

    .login-status-grid {{
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 12px;
      margin-top: 32px;
    }}

    .login-status {{
      background: rgba(13, 23, 34, 0.82);
      border: 1px solid #1f344c;
      border-radius: 8px;
      padding: 14px;
      box-shadow: 0 1px 0 rgba(255, 255, 255, 0.02) inset, 0 1px 2px rgba(0, 0, 0, 0.3);
    }}

    .login-status span {{
      color: #7c8a9b;
      display: block;
      font-size: 11px;
      margin-bottom: 6px;
    }}

    .login-status strong {{
      color: #dee6ef;
      display: block;
      font-family: var(--font-mono);
      font-size: 13px;
      font-weight: 600;
    }}

    .login-card {{
      background: rgba(13, 23, 34, 0.92);
      border: 1px solid #1f344c;
      border-radius: 10px;
      padding: 32px;
      width: 100%;
      box-shadow:
        0 1px 0 rgba(255, 255, 255, 0.03) inset,
        0 20px 54px rgba(0, 0, 0, 0.35);
      backdrop-filter: blur(10px);
    }}

    .login-logo {{
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 28px;
    }}

    .login-logo .logo-icon {{
      background: rgba(42, 178, 212, 0.14);
      border: 1px solid rgba(42, 178, 212, 0.38);
      color: #2ab2d4;
      font-family: var(--font-mono);
      font-weight: 700;
      font-size: 14px;
      width: 40px;
      height: 40px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
    }}

    .login-logo .logo-text {{
      font-size: 18px;
      font-weight: 600;
      color: #dee6ef;
    }}

    .login-logo .logo-sub {{
      font-size: 13px;
      color: #7c8a9b;
    }}

    .login-error {{
      background: rgba(222, 88, 120, 0.12);
      border: 1px solid rgba(222, 88, 120, 0.42);
      border-radius: 6px;
      color: #ffd7df;
      display: grid;
      gap: 4px;
      font-size: 13px;
      margin-bottom: 20px;
      padding: 12px 14px;
    }}

    .login-error strong {{
      color: #de5878;
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }}

    .login-form .form-group {{
      margin-bottom: 18px;
    }}

    .login-form label {{
      display: block;
      font-size: 13px;
      font-weight: 500;
      color: #b8c4d3;
      margin-bottom: 6px;
    }}

    .login-form input[type=""text""],
    .login-form input[type=""password""] {{
      width: 100%;
      background: rgba(22, 35, 52, 0.86);
      border: 1px solid #1f344c;
      border-radius: 6px;
      color: #dee6ef;
      font-size: 14px;
      padding: 12px 14px;
      transition: border-color 0.2s ease, box-shadow 0.2s ease, background 0.2s ease;
      box-sizing: border-box;
    }}

    .login-form input[type=""text""]:focus,
    .login-form input[type=""password""]:focus {{
      outline: none;
      background: rgba(27, 42, 60, 0.94);
      border-color: #2ab2d4;
      box-shadow: 0 0 0 3px rgba(42, 178, 212, 0.16);
    }}

    .login-form .btn-signin {{
      width: 100%;
      background: #2ab2d4;
      color: #06111c;
      border: 1px solid #2ab2d4;
      border-radius: 6px;
      font-size: 14px;
      font-weight: 700;
      padding: 12px;
      cursor: pointer;
      transition: background 0.2s ease, border-color 0.2s ease, box-shadow 0.2s ease;
      margin-top: 8px;
    }}

    .login-form .btn-signin:hover,
    .login-form .btn-signin:focus {{
      background: #5ac7df;
      border-color: #5ac7df;
      box-shadow: 0 0 0 3px rgba(42, 178, 212, 0.18);
      outline: none;
    }}

    .login-footnote {{
      border-top: 1px solid rgba(31, 52, 76, 0.82);
      color: #7c8a9b;
      font-size: 12px;
      line-height: 1.5;
      margin-top: 24px;
      padding-top: 16px;
    }}

    .login-footnote code {{
      color: #b8c4d3;
      font-family: var(--font-mono);
    }}

    @media (max-width: 860px) {{
      .login-page {{
        grid-template-columns: 1fr;
        gap: 28px;
        padding: 20px;
      }}

      .login-title {{
        font-size: 40px;
      }}

      .login-status-grid {{
        grid-template-columns: 1fr;
      }}
    }}

    @media (max-width: 520px) {{
      .login-card {{
        padding: 24px;
      }}
    }}
  </style>
</head>
<body>
  <div class=""login-page"">
    <section class=""login-brief"" aria-labelledby=""login-title"">
      <div class=""login-eyebrow"">Operator workstation</div>
      <h1 id=""login-title"" class=""login-title"">Meridian Terminal</h1>
      <p class=""login-copy"">
        Evidence-backed trading, portfolio, accounting, reporting, strategy, data, and settings workflows start from a verified local session.
      </p>

      <div class=""login-status-grid"" aria-label=""Workspace scope"">
        <div class=""login-status"">
          <span>Surface</span>
          <strong>Web workstation</strong>
        </div>
        <div class=""login-status"">
          <span>Mode</span>
          <strong>Local operator</strong>
        </div>
        <div class=""login-status"">
          <span>Access</span>
          <strong>Session required</strong>
        </div>
      </div>
    </section>

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

      <div class=""login-footnote"">
        Use the configured <code>MDC_USERNAME</code> and <code>MDC_PASSWORD</code> for required local authentication.
      </div>
    </div>
  </div>
</body>
</html>";
    }
}
