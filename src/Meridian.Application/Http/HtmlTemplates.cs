using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.UI;

/// <summary>
/// Manages HTML templates for the web dashboard.
/// Coordinates template loading from external files with fallback to legacy inline templates.
/// Renamed from HtmlTemplates for clarity - manages template lifecycle, not just generation.
/// </summary>
public static class HtmlTemplateManager
{
    private static readonly Lazy<HtmlTemplateLoader> _loader = new(() =>
    {
        var logger = NullLogger<HtmlTemplateLoader>.Instance;
        return new HtmlTemplateLoader(logger, enableCaching: true);
    });

    /// <summary>
    /// Gets the main dashboard HTML page.
    /// </summary>
    public static string Index(string configPath, string statusPath, string backfillPath)
    {
        try
        {
            if (_loader.Value.TemplateExists("index.html"))
            {
                return _loader.Value.LoadIndexTemplate(configPath, statusPath, backfillPath);
            }
        }
        catch (IOException)
        {
            // Template file not readable - fall through to legacy template
        }
        catch (InvalidOperationException)
        {
            // Template loading/parsing failed - fall through to legacy template
        }

        return LegacyTemplates.Index(configPath, statusPath, backfillPath);
    }

    /// <summary>
    /// Gets the credentials dashboard HTML page.
    /// </summary>
    public static string CredentialsDashboard(
        Config.AppConfig config,
        IReadOnlyDictionary<string, Config.Credentials.StoredCredentialStatus> statuses)
    {
        try
        {
            if (_loader.Value.TemplateExists("credentials.html"))
            {
                return _loader.Value.LoadCredentialsTemplate(config);
            }
        }
        catch (IOException)
        {
            // Template file not readable - fall through to legacy template
        }
        catch (InvalidOperationException)
        {
            // Template loading/parsing failed - fall through to legacy template
        }

        return LegacyTemplates.CredentialsDashboard(config, statuses);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Legacy inline templates for backwards compatibility.
    /// These are used when external template files are not available.
    /// </summary>
    private static class LegacyTemplates
    {
        public static string Index(string configPath, string statusPath, string backfillPath) => $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
  <title>Meridian</title>
  <style>
    * {{ box-sizing: border-box; }}
    body {{
      font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
      margin: 0;
      padding: 0;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      min-height: 100vh;
    }}
    .header {{
      background: white;
      padding: 16px 24px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.1);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }}
    .header h1 {{ margin: 0; font-size: 24px; color: #333; }}
    .container {{ max-width: 1400px; margin: 0 auto; padding: 24px; }}
    .card {{
      background: white;
      border-radius: 12px;
      padding: 24px;
      box-shadow: 0 4px 6px rgba(0,0,0,0.1);
      margin-bottom: 24px;
    }}
    .card h2 {{ margin: 0 0 16px 0; color: #333; font-size: 20px; }}
    .btn-primary {{
      background: #667eea;
      color: white;
      padding: 10px 20px;
      border: none;
      border-radius: 6px;
      cursor: pointer;
    }}
    .btn-primary:hover {{ background: #5a67d8; }}
    .status-badge {{
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 12px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 600;
    }}
    .status-connected {{ background: #c6f6d5; color: #22543d; }}
    .status-disconnected {{ background: #fed7d7; color: #742a2a; }}
    .hidden {{ display: none !important; }}
  </style>
</head>
<body>
  <div class=""header"">
    <h1>Meridian</h1>
  </div>
  <div class=""container"">
    <div class=""card"">
      <h2>System Status</h2>
      <div id=""statusBox"">
        <div class=""status-badge status-disconnected"">Loading...</div>
      </div>
    </div>
    <div class=""card"">
      <h2>Configuration</h2>
      <p>Config: <code>{Escape(configPath)}</code></p>
      <p><strong>Note:</strong> External templates not found. Using minimal fallback UI.</p>
      <p>Deploy templates to <code>wwwroot/templates/</code> for full dashboard functionality.</p>
    </div>
  </div>
  <script>
    async function loadStatus() {{
      try {{
        const r = await fetch('/api/status');
        const s = await r.json();
        const isConnected = s.isConnected !== false;
        document.getElementById('statusBox').innerHTML = `
          <div class=""status-badge ${{isConnected ? 'status-connected' : 'status-disconnected'}}"">
            ${{isConnected ? 'Connected' : 'Disconnected'}}
          </div>
          <div style=""margin-top: 8px; font-size: 12px; color: #718096;"">
            Last update: ${{s.timestampUtc || 'n/a'}}
          </div>
        `;
      }} catch (e) {{
        document.getElementById('statusBox').innerHTML = '<div class=""status-badge status-disconnected"">No Status</div>';
      }}
    }}
    loadStatus();
    setInterval(loadStatus, 2000);
  </script>
</body>
</html>";

        public static string CredentialsDashboard(
            Config.AppConfig config,
            IReadOnlyDictionary<string, Config.Credentials.StoredCredentialStatus> statuses)
        {
            var alpacaConfigured = config.Alpaca != null && !string.IsNullOrWhiteSpace(config.Alpaca.KeyId);
            var polygonConfigured = config.Backfill?.Providers?.Polygon != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Polygon.ApiKey);
            var tiingoConfigured = config.Backfill?.Providers?.Tiingo != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Tiingo.ApiToken);
            var finnhubConfigured = config.Backfill?.Providers?.Finnhub != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Finnhub.ApiKey);
            var alphaVantageConfigured = config.Backfill?.Providers?.AlphaVantage != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.AlphaVantage.ApiKey);
            var nasdaqConfigured = config.Backfill?.Providers?.Nasdaq != null && !string.IsNullOrWhiteSpace(config.Backfill.Providers.Nasdaq.ApiKey);

            return $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
  <title>Credential Management - Meridian</title>
  <style>
    * {{ box-sizing: border-box; }}
    body {{
      font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
      margin: 0;
      padding: 0;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      min-height: 100vh;
    }}
    .header {{
      background: white;
      padding: 16px 24px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.1);
    }}
    .header h1 {{ margin: 0; font-size: 24px; color: #333; }}
    .container {{ max-width: 1200px; margin: 0 auto; padding: 24px; }}
    .card {{
      background: white;
      border-radius: 12px;
      padding: 24px;
      box-shadow: 0 4px 6px rgba(0,0,0,0.1);
      margin-bottom: 24px;
    }}
    .card h2 {{ margin: 0 0 16px 0; color: #333; font-size: 20px; }}
    .provider-grid {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 16px;
    }}
    .provider-card {{
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      padding: 16px;
    }}
    .provider-card.configured {{ border-left: 4px solid #10b981; }}
    .provider-card.not-configured {{ border-left: 4px solid #9ca3af; }}
    .provider-name {{ font-weight: 600; font-size: 16px; color: #333; }}
    .provider-status {{ font-size: 12px; color: #666; margin-top: 4px; }}
  </style>
</head>
<body>
  <div class=""header"">
    <h1>Credential Management</h1>
  </div>
  <div class=""container"">
    <div class=""card"">
      <h2>Provider Credentials</h2>
      <p><strong>Note:</strong> External templates not found. Using minimal fallback UI.</p>
      <div class=""provider-grid"">
        <div class=""provider-card {(alpacaConfigured ? "configured" : "not-configured")}"">
          <div class=""provider-name"">Alpaca</div>
          <div class=""provider-status"">{(alpacaConfigured ? "Configured" : "Not Configured")}</div>
        </div>
        <div class=""provider-card {(polygonConfigured ? "configured" : "not-configured")}"">
          <div class=""provider-name"">Polygon</div>
          <div class=""provider-status"">{(polygonConfigured ? "Configured" : "Not Configured")}</div>
        </div>
        <div class=""provider-card {(tiingoConfigured ? "configured" : "not-configured")}"">
          <div class=""provider-name"">Tiingo</div>
          <div class=""provider-status"">{(tiingoConfigured ? "Configured" : "Not Configured")}</div>
        </div>
        <div class=""provider-card {(finnhubConfigured ? "configured" : "not-configured")}"">
          <div class=""provider-name"">Finnhub</div>
          <div class=""provider-status"">{(finnhubConfigured ? "Configured" : "Not Configured")}</div>
        </div>
        <div class=""provider-card {(alphaVantageConfigured ? "configured" : "not-configured")}"">
          <div class=""provider-name"">AlphaVantage</div>
          <div class=""provider-status"">{(alphaVantageConfigured ? "Configured" : "Not Configured")}</div>
        </div>
        <div class=""provider-card {(nasdaqConfigured ? "configured" : "not-configured")}"">
          <div class=""provider-name"">NasdaqDataLink</div>
          <div class=""provider-status"">{(nasdaqConfigured ? "Configured" : "Not Configured")}</div>
        </div>
      </div>
    </div>
  </div>
</body>
</html>";
        }
    }
}
