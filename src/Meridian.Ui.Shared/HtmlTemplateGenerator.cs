namespace Meridian.Ui.Shared;

/// <summary>
/// Generates HTML templates for the web dashboard.
/// CSS styles are in HtmlTemplateGenerator.Styles.cs, JavaScript in HtmlTemplateGenerator.Scripts.cs.
/// </summary>
public static partial class HtmlTemplateGenerator
{
    public static string Index(string configPath, string statusPath, string backfillPath) => $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
  <title>Meridian Terminal</title>
  <style>
{GetStyles()}
  </style>
</head>
<body>
  <!-- Top Navigation Bar -->
  <div class=""top-bar"">
    <div class=""logo"">
      <div class=""logo-icon"">MD</div>
      <span class=""logo-text"">Meridian Terminal</span>
      <span class=""logo-version"">v2.0</span>
    </div>

    <div class=""cmd-palette"" onclick=""openCommandPalette()"" title=""Quick actions (Ctrl+K)"">
      <span class=""cmd-palette-icon"">&#x1F50D;</span>
      <span class=""cmd-palette-text"">Search commands...</span>
      <div class=""cmd-palette-shortcut"">
        <span class=""kbd"">Ctrl</span>
        <span class=""kbd"">K</span>
      </div>
    </div>

    <div class=""top-status"">
      <div class=""status-indicator"">
        <div id=""topStatusDot"" class=""status-dot disconnected""></div>
        <span id=""topStatusText"">Disconnected</span>
      </div>
      <div class=""live-indicator"" id=""liveIndicator"" style=""display:none;"">
        LIVE
      </div>
    </div>
  </div>

  <!-- Data Freshness Status Bar -->
  <div class=""freshness-bar"" id=""freshnessBar"">
    <div class=""freshness-providers"" id=""freshnessProviders"">
      <span class=""freshness-label"">Providers:</span>
      <span class=""freshness-loading"">Loading...</span>
    </div>
    <div class=""freshness-event"">
      <span class=""freshness-label"">Last event:</span>
      <span id=""freshnessLastEvent"" class=""freshness-value"">--</span>
    </div>
    <div class=""freshness-throughput"">
      <span id=""freshnessThroughput"" class=""freshness-value"">-- evt/s</span>
    </div>
    <div id=""freshnessModeBadge"" class=""freshness-mode-badge"" style=""display:none;"">DEMO MODE</div>
  </div>

  <div class=""main-container"">
    <!-- Sidebar Navigation -->
    <nav class=""sidebar"">
      <div class=""nav-section"">
        <div class=""nav-section-title"">Overview</div>
        <div class=""nav-item active"" onclick=""scrollToSection('status')"">
          <span class=""nav-item-icon"">&#x1F4CA;</span>
          Status
        </div>
        <div class=""nav-item"" onclick=""scrollToSection('datasource')"">
          <span class=""nav-item-icon"">&#x1F517;</span>
          Provider
        </div>
      </div>

      <div class=""nav-section"">
        <div class=""nav-section-title"">Configuration</div>
        <div class=""nav-item"" onclick=""scrollToSection('storage')"">
          <span class=""nav-item-icon"">&#x1F4BE;</span>
          Storage
        </div>
        <div class=""nav-item"" onclick=""scrollToSection('datasources')"">
          <span class=""nav-item-icon"">&#x2699;</span>
          Data Sources
          <span class=""nav-item-badge"" id=""dsCount"">0</span>
        </div>
        <div class=""nav-item"" onclick=""scrollToSection('symbols')"">
          <span class=""nav-item-icon"">&#x1F4C8;</span>
          Symbols
          <span class=""nav-item-badge"" id=""symCount"">0</span>
        </div>
        <div class=""nav-item"" onclick=""scrollToSection('derivatives')"">
          <span class=""nav-item-icon"">&#x1F4C9;</span>
          Derivatives
        </div>
      </div>

      <div class=""nav-section"">
        <div class=""nav-section-title"">Operations</div>
        <div class=""nav-item"" onclick=""scrollToSection('backfill')"">
          <span class=""nav-item-icon"">&#x23F3;</span>
          Backfill
        </div>
      </div>
    </nav>

    <!-- Main Content -->
    <main class=""content"">
      <!-- Path Display -->
      <div class=""path-display"">
        <span class=""path-label"">Config</span>
        <code class=""path-value"">{Escape(configPath)}</code>
      </div>

      <!-- Metrics Grid -->
      <section id=""status"">
        <div class=""metrics-grid"">
          <div class=""metric-card success"">
            <div class=""metric-trend up"" id=""publishedTrend""></div>
            <div class=""metric-value success"" id=""publishedValue"">0</div>
            <div class=""metric-label"">Published Events</div>
          </div>
          <div class=""metric-card danger"">
            <div class=""metric-trend"" id=""droppedTrend""></div>
            <div class=""metric-value danger"" id=""droppedValue"">0</div>
            <div class=""metric-label"">Dropped Events</div>
          </div>
          <div class=""metric-card warning"">
            <div class=""metric-trend"" id=""integrityTrend""></div>
            <div class=""metric-value warning"" id=""integrityValue"">0</div>
            <div class=""metric-label"">Integrity Events</div>
          </div>
          <div class=""metric-card info"">
            <div class=""metric-trend up"" id=""barsTrend""></div>
            <div class=""metric-value info"" id=""barsValue"">0</div>
            <div class=""metric-label"">Historical Bars</div>
          </div>
        </div>

        <!-- Terminal-style activity log -->
        <div class=""terminal"" style=""margin-bottom: 24px;"">
          <div class=""terminal-header"">
            <div class=""terminal-dot red""></div>
            <div class=""terminal-dot yellow""></div>
            <div class=""terminal-dot green""></div>
            <span class=""terminal-title"">Activity Log</span>
          </div>
          <div class=""terminal-body"" id=""activityLog"">
            <div class=""terminal-line"">
              <span class=""terminal-prompt"">$</span>
              <span class=""terminal-time"">--:--:--</span>
              <span class=""terminal-msg"">Waiting for connection...</span>
            </div>
          </div>
        </div>
      </section>

  <div class=""row"" id=""datasource"">
    <!-- Data Source Panel -->
    <div class=""card"" style=""flex:1; min-width: 400px;"">
      <h3>Data Provider</h3>
      <div class=""form-group"">
        <label>Active Provider</label>
        <select id=""dataSource"" onchange=""updateDataSource()"">
          <option value=""IB"">Interactive Brokers (IB)</option>
          <option value=""Alpaca"">Alpaca</option>
        </select>
      </div>
      <div id=""providerStatus"" class=""muted"" style=""padding: 12px; background: var(--bg-tertiary); border-radius: 6px; margin-bottom: 16px;""></div>

      <!-- Alpaca Settings -->
      <div id=""alpacaSettings"" class=""provider-section hidden"">
        <h4>Alpaca Configuration</h4>
        <div class=""form-group"">
          <label>API Key ID</label>
          <input id=""alpacaKeyId"" type=""text"" placeholder=""PKXXXXXXXXXXXXXXXX"" />
        </div>
        <div class=""form-group"">
          <label>Secret Key</label>
          <input id=""alpacaSecretKey"" type=""password"" placeholder=""Enter your secret key"" />
        </div>
        <div class=""form-row"">
          <div class=""form-group"">
            <label>Data Feed</label>
            <select id=""alpacaFeed"">
              <option value=""iex"">IEX (Free Tier)</option>
              <option value=""sip"">SIP (Paid - Full Market)</option>
              <option value=""delayed_sip"">Delayed SIP</option>
            </select>
          </div>
          <div class=""form-group"">
            <label>Environment</label>
            <select id=""alpacaSandbox"">
              <option value=""false"">Production</option>
              <option value=""true"">Sandbox (Paper)</option>
            </select>
          </div>
        </div>
        <div class=""form-group"">
          <label><input type=""checkbox"" id=""alpacaSubscribeQuotes"" /> Subscribe to Quotes (BBO)</label>
        </div>
        <button class=""btn-primary"" onclick=""saveAlpacaSettings()"">
          <span>&#x1F4BE;</span> Save Alpaca Settings
        </button>
        <div id=""alpacaMsg"" class=""muted"" style=""margin-top: 12px;""></div>
      </div>
    </div>
  </div>

  <div class=""row"" id=""storage"">
    <!-- Storage Settings Panel -->
    <div class=""card"" style=""flex:1; min-width: 500px;"">
      <h3>Storage Configuration</h3>
      <div class=""form-row"">
        <div class=""form-group"" style=""flex: 2"">
          <label>Data Root Path</label>
          <input id=""dataRoot"" type=""text"" placeholder=""./data"" />
        </div>
        <div class=""form-group"" style=""flex: 1"">
          <label>Compression</label>
          <select id=""compress"">
            <option value=""false"">Disabled</option>
            <option value=""true"">GZIP Enabled</option>
          </select>
        </div>
      </div>
      <div class=""form-row"">
        <div class=""form-group"">
          <label>Naming Convention</label>
          <select id=""namingConvention"">
            <option value=""Flat"">Flat (root/symbol_type_date.jsonl)</option>
            <option value=""BySymbol"" selected>By Symbol (root/symbol/type/date.jsonl)</option>
            <option value=""ByDate"">By Date (root/date/symbol/type.jsonl)</option>
            <option value=""ByType"">By Type (root/type/symbol/date.jsonl)</option>
          </select>
        </div>
        <div class=""form-group"">
          <label>Date Partitioning</label>
          <select id=""datePartition"">
            <option value=""None"">None (single file)</option>
            <option value=""Daily"" selected>Daily (yyyy-MM-dd)</option>
            <option value=""Hourly"">Hourly (yyyy-MM-dd_HH)</option>
            <option value=""Monthly"">Monthly (yyyy-MM)</option>
          </select>
        </div>
      </div>
      <div class=""form-row"">
        <div class=""form-group"">
          <label>File Prefix (optional)</label>
          <input id=""filePrefix"" type=""text"" placeholder=""market_"" />
        </div>
        <div class=""form-group"">
          <label>Include Provider in Path</label>
          <select id=""includeProvider"">
            <option value=""false"" selected>No</option>
            <option value=""true"">Yes</option>
          </select>
        </div>
      </div>
      <div id=""storagePreview"" style=""margin: 16px 0; padding: 16px; background: var(--bg-primary); border: 1px solid var(--border-muted); border-radius: 8px;"">
        <div style=""font-size: 11px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 8px;"">Preview Path</div>
        <code id=""previewPath"" style=""font-size: 14px;"">data/AAPL/Trade/2024-01-15.jsonl</code>
      </div>
      <button class=""btn-primary"" onclick=""saveStorageSettings()"">
        <span>&#x1F4BE;</span> Save Storage Settings
      </button>
      <div id=""storageMsg"" class=""muted"" style=""margin-top: 12px;""></div>
    </div>
  </div>

  <div class=""row"" id=""datasources"">
    <!-- Data Sources Panel -->
    <div class=""card"" style=""flex:1; min-width: 700px;"">
      <h3>Data Sources</h3>
      <p class=""muted"" style=""margin-bottom: 20px;"">Configure multiple data sources for real-time and historical data collection with automatic failover.</p>

      <!-- Failover Settings -->
      <div style=""background: var(--bg-tertiary); padding: 16px; border-radius: 8px; margin-bottom: 20px;"">
        <div class=""form-row"" style=""align-items: flex-end;"">
          <div class=""form-group"" style=""flex: 1; margin-bottom: 0;"">
            <label style=""display: flex; align-items: center; gap: 8px; cursor: pointer;"">
              <input type=""checkbox"" id=""enableFailover"" checked />
              <span>Enable Automatic Failover</span>
            </label>
          </div>
          <div class=""form-group"" style=""flex: 1; margin-bottom: 0;"">
            <label>Failover Timeout</label>
            <input id=""failoverTimeout"" type=""number"" value=""30"" min=""5"" max=""300"" style=""width: 80px;"" />
            <span class=""muted"" style=""margin-left: 8px;"">seconds</span>
          </div>
          <div class=""form-group"" style=""flex: 0; margin-bottom: 0;"">
            <button class=""btn-secondary"" onclick=""saveFailoverSettings()"">
              <span>&#x2699;</span> Save
            </button>
          </div>
        </div>
      </div>

      <table id=""dataSourcesTable"">
        <thead>
          <tr>
            <th style=""width: 60px;"">Status</th>
            <th>Name</th>
            <th style=""width: 100px;"">Provider</th>
            <th style=""width: 100px;"">Type</th>
            <th style=""width: 80px;"">Priority</th>
            <th style=""width: 120px;"">Actions</th>
          </tr>
        </thead>
        <tbody></tbody>
      </table>

      <h4 style=""margin-top: 24px"">Add/Edit Data Source</h4>
      <input type=""hidden"" id=""dsId"" />
      <div class=""form-row"">
        <div class=""form-group"">
          <label>Name *</label>
          <input id=""dsName"" placeholder=""My Data Source"" />
        </div>
        <div class=""form-group"">
          <label>Provider *</label>
          <select id=""dsProvider"" onchange=""updateDsProviderUI()"">
            <option value=""IB"">Interactive Brokers (IB)</option>
            <option value=""Alpaca"">Alpaca</option>
            <option value=""Polygon"">Polygon.io</option>
          </select>
        </div>
        <div class=""form-group"">
          <label>Type *</label>
          <select id=""dsType"">
            <option value=""RealTime"">Real-Time</option>
            <option value=""Historical"">Historical</option>
            <option value=""Both"">Both</option>
          </select>
        </div>
        <div class=""form-group"">
          <label>Priority</label>
          <input id=""dsPriority"" type=""number"" value=""100"" min=""1"" max=""1000"" />
        </div>
      </div>
      <div class=""form-row"">
        <div class=""form-group"" style=""flex: 2"">
          <label>Description</label>
          <input id=""dsDescription"" placeholder=""Optional description"" />
        </div>
        <div class=""form-group"" style=""flex: 1"">
          <label>Symbols (comma separated)</label>
          <input id=""dsSymbols"" placeholder=""AAPL, MSFT"" />
        </div>
      </div>

      <div id=""dsIbSettings"" class=""provider-section"">
        <p class=""muted"">IB Settings</p>
        <div class=""form-row"">
          <div class=""form-group""><label>Host</label><input id=""dsIbHost"" value=""127.0.0.1"" /></div>
          <div class=""form-group""><label>Port</label><input id=""dsIbPort"" type=""number"" value=""7497"" /></div>
            <div class=""form-group""><label>Client ID</label><input id=""dsIbClientId"" type=""number"" value=""1"" /></div>
        </div>
        <div class=""form-group"">
          <label><input type=""checkbox"" id=""dsIbPaper"" /> Paper Trading</label>
          <label style=""margin-left: 16px;""><input type=""checkbox"" id=""dsIbDepth"" checked /> Subscribe Depth</label>
          <label style=""margin-left: 16px;""><input type=""checkbox"" id=""dsIbTick"" checked /> Tick-by-Tick</label>
        </div>
      </div>

      <div id=""dsAlpacaSettings"" class=""provider-section hidden"">
        <p class=""muted"">Alpaca Settings</p>
        <div class=""form-row"">
          <div class=""form-group"">
            <label>Feed</label>
            <select id=""dsAlpacaFeed""><option value=""iex"">IEX (free)</option><option value=""sip"">SIP (paid)</option><option value=""delayed_sip"">Delayed SIP</option></select>
          </div>
          <div class=""form-group"">
            <label>Environment</label>
            <select id=""dsAlpacaSandbox""><option value=""false"">Production</option><option value=""true"">Sandbox</option></select>
          </div>
        </div>
        <div class=""form-group""><label><input type=""checkbox"" id=""dsAlpacaQuotes"" /> Subscribe to Quotes</label></div>
      </div>

      <div id=""dsPolygonSettings"" class=""provider-section hidden"">
        <p class=""muted"">Polygon.io Settings</p>
        <div class=""form-row"">
          <div class=""form-group""><label>API Key</label><input id=""dsPolygonKey"" type=""password"" /></div>
          <div class=""form-group"">
            <label>Feed</label>
            <select id=""dsPolygonFeed""><option value=""stocks"">Stocks</option><option value=""options"">Options</option><option value=""forex"">Forex</option><option value=""crypto"">Crypto</option></select>
          </div>
        </div>
        <div class=""form-group"">
          <label><input type=""checkbox"" id=""dsPolygonDelayed"" /> Use Delayed Data</label>
          <label style=""margin-left: 16px;""><input type=""checkbox"" id=""dsPolygonTrades"" checked /> Trades</label>
          <label style=""margin-left: 16px;""><input type=""checkbox"" id=""dsPolygonQuotes"" /> Quotes</label>
          <label style=""margin-left: 16px;""><input type=""checkbox"" id=""dsPolygonAggs"" /> Aggregates</label>
        </div>
      </div>

      <div style=""margin-top: 16px;"">
        <button class=""btn-primary"" onclick=""saveDataSource()"">Save Data Source</button>
        <button onclick=""clearDsForm()"" style=""margin-left: 8px;"">Clear</button>
      </div>
      <div id=""dsMsg"" class=""muted"" style=""margin-top: 8px;""></div>
    </div>
  </div>

  <div class=""row"" id=""backfill"">
    <div class=""card"" style=""flex:1; min-width: 500px;"">
      <h3>Historical Backfill</h3>
      <p id=""backfillHelp"" class=""muted"" style=""margin-bottom: 20px;"">Download historical EOD bars to fill data gaps from free providers.</p>

      <div class=""form-row"">
        <div class=""form-group"">
          <label>Data Provider</label>
          <select id=""backfillProvider""></select>
        </div>
        <div class=""form-group"" style=""flex: 2"">
          <label>Symbols (comma separated)</label>
          <input id=""backfillSymbols"" placeholder=""AAPL, MSFT, GOOGL"" />
        </div>
      </div>
      <div class=""form-row"">
        <div class=""form-group"">
          <label>Start Date (UTC)</label>
          <input id=""backfillFrom"" type=""date"" />
        </div>
        <div class=""form-group"">
          <label>End Date (UTC)</label>
          <input id=""backfillTo"" type=""date"" />
        </div>
      </div>

      <button class=""btn-primary"" onclick=""runBackfill()"" id=""backfillBtn"">
        <span>&#x23F3;</span> Start Backfill
      </button>

      <!-- Backfill Status Terminal -->
      <div class=""terminal"" style=""margin-top: 16px;"">
        <div class=""terminal-header"">
          <div class=""terminal-dot red""></div>
          <div class=""terminal-dot yellow""></div>
          <div class=""terminal-dot green""></div>
          <span class=""terminal-title"">Backfill Status</span>
        </div>
        <div class=""terminal-body"" id=""backfillStatus"" style=""min-height: 60px;"">
          <div class=""terminal-line"">
            <span class=""terminal-prompt"">$</span>
            <span class=""terminal-msg"">Ready to start backfill operation...</span>
          </div>
        </div>
      </div>
    </div>
  </div>

  <div class=""row"" id=""derivatives"">
    <!-- Derivatives Configuration Panel -->
    <div class=""card"" style=""flex:1; min-width: 600px;"">
      <h3>Derivatives Tracking</h3>
      <p class=""muted"" style=""margin-bottom: 20px;"">Configure options and derivatives data collection for equity and index options.</p>

      <div class=""form-group"">
        <label><input type=""checkbox"" id=""derivEnabled"" onchange=""toggleDerivativesFields()"" /> Enable Derivatives Tracking</label>
      </div>

      <div id=""derivFields"">
        <div class=""form-row"">
          <div class=""form-group"" style=""flex: 2"">
            <label>Underlying Symbols (comma separated)</label>
            <input id=""derivUnderlyings"" placeholder=""SPY, QQQ, AAPL, MSFT"" />
          </div>
          <div class=""form-group"" style=""flex: 1"">
            <label>Max Days to Expiry</label>
            <input id=""derivMaxDte"" type=""number"" value=""90"" min=""0"" max=""730"" />
          </div>
          <div class=""form-group"" style=""flex: 1"">
            <label>Strike Range (+/-)</label>
            <input id=""derivStrikeRange"" type=""number"" value=""20"" min=""0"" max=""100"" />
          </div>
        </div>

        <div class=""form-row"">
          <div class=""form-group"">
            <label><input type=""checkbox"" id=""derivGreeks"" checked /> Capture Greeks (delta, gamma, theta, vega, rho, IV)</label>
          </div>
          <div class=""form-group"">
            <label><input type=""checkbox"" id=""derivOI"" checked /> Capture Open Interest (daily updates)</label>
          </div>
        </div>

        <div class=""form-row"">
          <div class=""form-group"">
            <label><input type=""checkbox"" id=""derivChainSnap"" /> Capture Chain Snapshots</label>
          </div>
          <div class=""form-group"">
            <label>Snapshot Interval (seconds)</label>
            <input id=""derivChainInterval"" type=""number"" value=""300"" min=""30"" max=""3600"" />
          </div>
        </div>

        <div class=""form-group"">
          <label>Expiration Filter</label>
          <div style=""display: flex; gap: 16px; flex-wrap: wrap;"">
            <label><input type=""checkbox"" id=""derivExpWeekly"" checked /> Weekly</label>
            <label><input type=""checkbox"" id=""derivExpMonthly"" checked /> Monthly</label>
            <label><input type=""checkbox"" id=""derivExpQuarterly"" /> Quarterly</label>
            <label><input type=""checkbox"" id=""derivExpLeaps"" /> LEAPS</label>
          </div>
        </div>

        <!-- Index Options Sub-Section -->
        <div style=""background: var(--bg-tertiary); padding: 16px; border-radius: 8px; margin-top: 16px;"">
          <div style=""display: flex; align-items: center; gap: 8px; margin-bottom: 16px;"">
            <span style=""color: var(--accent-purple);"">&#x1F4CA;</span>
            <span class=""muted"">Index Options (SPX, NDX, RUT, VIX)</span>
          </div>
          <div class=""form-group"">
            <label><input type=""checkbox"" id=""derivIdxEnabled"" /> Enable Index Options</label>
          </div>
          <div class=""form-group"">
            <label>Index Symbols (comma separated)</label>
            <input id=""derivIdxIndices"" placeholder=""SPX, NDX, RUT, VIX"" />
          </div>
          <div style=""display: flex; gap: 16px; flex-wrap: wrap;"">
            <label><input type=""checkbox"" id=""derivIdxWeeklies"" checked /> Include Weeklies (0DTE)</label>
            <label><input type=""checkbox"" id=""derivIdxAmSettled"" checked /> AM-Settled</label>
            <label><input type=""checkbox"" id=""derivIdxPmSettled"" checked /> PM-Settled</label>
          </div>
        </div>
      </div>

      <div style=""margin-top: 20px;"">
        <button class=""btn-primary"" onclick=""saveDerivativesConfig()"">
          <span>&#x1F4BE;</span> Save Derivatives Settings
        </button>
      </div>
      <div id=""derivMsg"" class=""muted"" style=""margin-top: 12px;""></div>
    </div>

    <!-- Options Live Data Panel -->
    <div class=""card"" style=""flex:1; min-width: 400px;"">
      <h3>Options Live Data</h3>
      <p class=""muted"" style=""margin-bottom: 16px;"">Real-time options data summary and tracked underlyings.</p>
      <div id=""optionsSummary"">
        <div style=""display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px; margin-bottom: 16px;"">
          <div class=""metric-card"">
            <span class=""metric-value"" id=""optContracts"">--</span>
            <span class=""metric-label"">Tracked Contracts</span>
          </div>
          <div class=""metric-card"">
            <span class=""metric-value"" id=""optChains"">--</span>
            <span class=""metric-label"">Chains</span>
          </div>
          <div class=""metric-card"">
            <span class=""metric-value"" id=""optUnderlyings"">--</span>
            <span class=""metric-label"">Underlyings</span>
          </div>
          <div class=""metric-card"">
            <span class=""metric-value"" id=""optGreeks"">--</span>
            <span class=""metric-label"">With Greeks</span>
          </div>
        </div>
        <div id=""optProviderStatus"" class=""muted"" style=""margin-bottom: 12px;"">Provider status: checking...</div>
        <div style=""margin-bottom: 12px;"">
          <label class=""muted"" style=""font-size: 12px;"">Tracked Underlyings:</label>
          <div id=""optTrackedList"" class=""muted"" style=""margin-top: 4px;"">--</div>
        </div>
        <button class=""btn-secondary"" onclick=""refreshOptionsSummary()"">Refresh Options Data</button>
      </div>
    </div>
  </div>

  <div class=""row"" id=""symbols"">
    <!-- Symbols Panel -->
    <div class=""card"" style=""flex:1; min-width: 700px;"">
      <h3>Subscribed Symbols</h3>
      <table id=""symbolsTable"">
        <thead>
          <tr>
            <th>Symbol</th>
            <th>Type</th>
            <th>Trades</th>
            <th>Depth</th>
            <th>Levels</th>
            <th>LocalSymbol <span class=""provider-badge ib-only"">IB</span></th>
            <th>Exchange <span class=""provider-badge ib-only"">IB</span></th>
            <th style=""width: 80px;"">Actions</th>
          </tr>
        </thead>
        <tbody></tbody>
      </table>

      <h4 style=""margin-top:24px"">Add New Symbol</h4>
      <div class=""form-row"">
        <div class=""form-group"">
          <label>Symbol *</label>
          <input id=""sym"" placeholder=""AAPL"" style=""text-transform: uppercase;"" />
        </div>
        <div class=""form-group"">
          <label>Security Type</label>
          <select id=""secType"" onchange=""toggleOptionsFields()"">
            <option value=""STK"" selected>Stock (STK)</option>
            <option value=""ETF"">ETF (ETF)</option>
            <option value=""OPT"">Equity Option (OPT)</option>
            <option value=""IND_OPT"">Index Option (IND_OPT)</option>
            <option value=""FOP"">Futures Option (FOP)</option>
            <option value=""FUT"">Future (FUT)</option>
            <option value=""SSF"">Single Stock Future (SSF)</option>
            <option value=""IND"">Index (IND)</option>
            <option value=""CASH"">Forex / Cash (CASH)</option>
            <option value=""CMDTY"">Commodity (CMDTY)</option>
            <option value=""CRYPTO"">Crypto (CRYPTO)</option>
            <option value=""CFD"">CFD (CFD)</option>
            <option value=""BOND"">Bond (BOND)</option>
            <option value=""FUND"">Fund (FUND)</option>
            <option value=""WAR"">Warrant (WAR)</option>
            <option value=""BAG"">Combination / Spread (BAG)</option>
            <option value=""MARGIN"">Margin Product (MARGIN)</option>
          </select>
        </div>
        <div class=""form-group"">
          <label>Trades Stream</label>
          <select id=""trades"">
            <option value=""true"" selected>Enabled</option>
            <option value=""false"">Disabled</option>
          </select>
        </div>
        <div class=""form-group"">
          <label>Depth Stream</label>
          <select id=""depth"">
            <option value=""true"">Enabled</option>
            <option value=""false"" selected>Disabled</option>
          </select>
        </div>
        <div class=""form-group"">
          <label>Depth Levels</label>
          <input id=""levels"" value=""10"" type=""number"" min=""1"" max=""50"" />
        </div>
      </div>

      <!-- Options-specific fields -->
      <div id=""optionFields"" class=""hidden"" style=""background: var(--bg-tertiary); padding: 16px; border-radius: 8px; margin-top: 16px;"">
        <div style=""display: flex; align-items: center; gap: 8px; margin-bottom: 16px;"">
          <span style=""color: var(--accent-purple);"">&#x1F4C9;</span>
          <span class=""muted"">Options Contract Details</span>
        </div>
        <div class=""form-row"">
          <div class=""form-group"">
            <label>Strike Price</label>
            <input id=""optStrike"" type=""number"" step=""0.01"" min=""0"" placeholder=""150.00"" />
          </div>
          <div class=""form-group"">
            <label>Right</label>
            <select id=""optRight"">
              <option value=""Call"">Call</option>
              <option value=""Put"">Put</option>
            </select>
          </div>
          <div class=""form-group"">
            <label>Expiration</label>
            <input id=""optExpiry"" type=""date"" />
          </div>
        </div>
        <div class=""form-row"">
          <div class=""form-group"">
            <label>Option Style</label>
            <select id=""optStyle"">
              <option value=""American"">American</option>
              <option value=""European"">European</option>
            </select>
          </div>
          <div class=""form-group"">
            <label>Multiplier</label>
            <input id=""optMultiplier"" type=""number"" value=""100"" min=""1"" />
          </div>
        </div>
      </div>

      <!-- IB-specific fields -->
      <div id=""ibFields"" style=""background: var(--bg-tertiary); padding: 16px; border-radius: 8px; margin-top: 16px;"">
        <div style=""display: flex; align-items: center; gap: 8px; margin-bottom: 16px;"">
          <span style=""color: var(--accent-blue);"">&#x1F517;</span>
          <span class=""muted"">Interactive Brokers Options</span>
          <span class=""provider-badge ib-only"">IB only</span>
        </div>
        <div class=""form-row"">
          <div class=""form-group"">
            <label>LocalSymbol</label>
            <input id=""localsym"" placeholder=""PCG PRA"" />
          </div>
          <div class=""form-group"">
            <label>Exchange</label>
            <input id=""exch"" value=""SMART"" />
          </div>
          <div class=""form-group"">
            <label>Primary Exchange</label>
            <input id=""pexch"" placeholder=""NYSE"" />
          </div>
        </div>
      </div>

      <div style=""margin-top: 20px; display: flex; gap: 12px;"">
        <button class=""btn-primary"" onclick=""addSymbol()"">
          <span>&#x2795;</span> Add Symbol
        </button>
        <button class=""btn-secondary"" onclick=""clearSymbolForm()"">
          Clear Form
        </button>
      </div>

      <div id=""msg"" class=""muted"" style=""margin-top:12px""></div>
    </div>
  </div>

    </main>
  </div>

  <!-- Toast Container -->
  <div class=""toast-container"" id=""toastContainer""></div>


<script>
{GetScripts()}
</script>
</body>
</html>";

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
