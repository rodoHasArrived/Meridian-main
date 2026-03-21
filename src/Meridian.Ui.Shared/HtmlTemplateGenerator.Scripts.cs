namespace Meridian.Ui.Shared;

public static partial class HtmlTemplateGenerator
{
    /// <summary>
    /// Returns the dashboard JavaScript.
    /// </summary>
    private static string GetScripts() => $@"
let currentDataSource = 'IB';
let backfillProviders = [];
let dataSources = [];
let activityLogs = [];
let prevMetrics = {{ published: 0, dropped: 0, integrity: 0, bars: 0 }};

// Utility Functions
function formatNumber(num) {{
  if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
  if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
  return num.toString();
}}

function getCurrentTime() {{
  return new Date().toLocaleTimeString('en-US', {{ hour12: false }});
}}

// Toast Notification System
function showToast(message, type = 'info') {{
  const container = document.getElementById('toastContainer');
  const toast = document.createElement('div');
  toast.className = `toast ${{type}}`;

  const icons = {{ success: '&#x2705;', error: '&#x274C;', warning: '&#x26A0;', info: '&#x2139;' }};
  toast.innerHTML = `<span>${{icons[type] || icons.info}}</span><span>${{message}}</span>`;

  container.appendChild(toast);
  setTimeout(() => toast.remove(), 4000);
}}

// Activity Log
function addLog(message, type = '') {{
  const logBody = document.getElementById('activityLog');
  const time = getCurrentTime();

  activityLogs.push({{ time, message, type }});
  if (activityLogs.length > 50) activityLogs.shift();

  const line = document.createElement('div');
  line.className = 'terminal-line';
  line.innerHTML = `
    <span class=""terminal-prompt"">&#36;</span>
    <span class=""terminal-time"">${{time}}</span>
    <span class=""terminal-msg ${{type}}"">${{message}}</span>
  `;

  logBody.appendChild(line);
  logBody.scrollTop = logBody.scrollHeight;

  // Keep only last 20 lines visible
  while (logBody.children.length > 20) {{
    logBody.removeChild(logBody.firstChild);
  }}
}}

// Navigation
function scrollToSection(sectionId) {{
  const section = document.getElementById(sectionId);
  if (section) {{
    section.scrollIntoView({{ behavior: 'smooth', block: 'start' }});

    // Update active nav item
    document.querySelectorAll('.nav-item').forEach(item => item.classList.remove('active'));
    const navItem = document.querySelector(`.nav-item[onclick*=""${{sectionId}}""]`);
    if (navItem) navItem.classList.add('active');
  }}
}}

// Command Palette
function openCommandPalette() {{
  const commands = [
    {{ name: 'Go to Status', action: () => scrollToSection('status') }},
    {{ name: 'Go to Provider', action: () => scrollToSection('datasource') }},
    {{ name: 'Go to Storage', action: () => scrollToSection('storage') }},
    {{ name: 'Go to Data Sources', action: () => scrollToSection('datasources') }},
    {{ name: 'Go to Symbols', action: () => scrollToSection('symbols') }},
    {{ name: 'Go to Derivatives', action: () => scrollToSection('derivatives') }},
    {{ name: 'Go to Backfill', action: () => scrollToSection('backfill') }},
    {{ name: 'Refresh Status', action: () => loadStatus() }},
    {{ name: 'Save All Settings', action: () => {{ saveStorageSettings(); saveAlpacaSettings(); }} }},
  ];

  const cmd = prompt('Command (type to search):\n' + commands.map((c, i) => `${{i+1}}. ${{c.name}}`).join('\n'));
  if (cmd) {{
    const idx = parseInt(cmd) - 1;
    if (idx >= 0 && idx < commands.length) {{
      commands[idx].action();
    }}
  }}
}}

// Keyboard shortcuts
document.addEventListener('keydown', (e) => {{
  if (e.ctrlKey && e.key === 'k') {{
    e.preventDefault();
    openCommandPalette();
  }}
  if (e.key === '1' && e.altKey) scrollToSection('status');
  if (e.key === '2' && e.altKey) scrollToSection('storage');
  if (e.key === '3' && e.altKey) scrollToSection('symbols');
  if (e.key === 'r' && e.ctrlKey && !e.shiftKey) {{
    e.preventDefault();
    loadStatus();
    showToast('Status refreshed', 'info');
  }}
}});

// Data Sources Management
async function loadDataSources() {{
  try {{
    const r = await fetch('/api/config/datasources');
    if (!r.ok) return;
    const data = await r.json();

    document.getElementById('enableFailover').checked = data.enableFailover !== false;
    document.getElementById('failoverTimeout').value = data.failoverTimeoutSeconds || 30;

    dataSources = data.sources || [];
    document.getElementById('dsCount').textContent = dataSources.length;
    renderDataSourcesTable();
  }} catch (e) {{
    console.warn('Unable to load data sources', e);
  }}
}}

function renderDataSourcesTable() {{
  const tbody = document.querySelector('#dataSourcesTable tbody');
  tbody.innerHTML = '';

  if (dataSources.length === 0) {{
    tbody.innerHTML = '<tr><td colspan=""6"" class=""muted"" style=""text-align: center; padding: 24px;"">No data sources configured. Add one below.</td></tr>';
    return;
  }}

  for (const ds of dataSources) {{
    const tr = document.createElement('tr');
    const tagClass = ds.provider === 'IB' ? 'tag-ib' : (ds.provider === 'Alpaca' ? 'tag-alpaca' : 'tag-polygon');
    const statusColor = ds.enabled ? 'var(--accent-green)' : 'var(--text-muted)';
    tr.innerHTML = `
      <td>
        <label style=""display: flex; align-items: center; gap: 8px; cursor: pointer;"">
          <input type=""checkbox"" ${{ds.enabled ? 'checked' : ''}} onchange=""toggleDataSource('${{ds.id}}', this.checked)"" />
          <span style=""width: 8px; height: 8px; border-radius: 50%; background: ${{statusColor}};""></span>
        </label>
      </td>
      <td><span style=""font-weight: 600; font-family: var(--font-mono);"">${{ds.name}}</span></td>
      <td><span class=""tag ${{tagClass}}"">${{ds.provider}}</span></td>
      <td style=""color: var(--text-secondary);"">${{ds.type}}</td>
      <td style=""font-family: var(--font-mono);"">${{ds.priority}}</td>
      <td>
        <button class=""btn-secondary"" onclick=""editDataSource('${{ds.id}}')"" style=""padding: 6px 12px; font-size: 12px; margin-right: 4px;"">Edit</button>
        <button class=""btn-danger"" onclick=""deleteDataSource('${{ds.id}}')"">Delete</button>
      </td>
    `;
    tbody.appendChild(tr);
  }}
}}

function updateDsProviderUI() {{
  const provider = document.getElementById('dsProvider').value;
  document.getElementById('dsIbSettings').classList.toggle('hidden', provider !== 'IB');
  document.getElementById('dsAlpacaSettings').classList.toggle('hidden', provider !== 'Alpaca');
  document.getElementById('dsPolygonSettings').classList.toggle('hidden', provider !== 'Polygon');
}}

function clearDsForm() {{
  document.getElementById('dsId').value = '';
  document.getElementById('dsName').value = '';
  document.getElementById('dsProvider').value = 'IB';
  document.getElementById('dsType').value = 'RealTime';
  document.getElementById('dsPriority').value = '100';
  document.getElementById('dsDescription').value = '';
  document.getElementById('dsSymbols').value = '';

  // IB defaults
  document.getElementById('dsIbHost').value = '127.0.0.1';
  document.getElementById('dsIbPort').value = '7496';
  document.getElementById('dsIbClientId').value = '0';
  document.getElementById('dsIbPaper').checked = false;
  document.getElementById('dsIbDepth').checked = true;
  document.getElementById('dsIbTick').checked = true;

  // Alpaca defaults
  document.getElementById('dsAlpacaFeed').value = 'iex';
  document.getElementById('dsAlpacaSandbox').value = 'false';
  document.getElementById('dsAlpacaQuotes').checked = false;

  // Polygon defaults
  document.getElementById('dsPolygonKey').value = '';
  document.getElementById('dsPolygonFeed').value = 'stocks';
  document.getElementById('dsPolygonDelayed').checked = false;
  document.getElementById('dsPolygonTrades').checked = true;
  document.getElementById('dsPolygonQuotes').checked = false;
  document.getElementById('dsPolygonAggs').checked = false;

  updateDsProviderUI();
}}

function editDataSource(id) {{
  const ds = dataSources.find(s => s.id === id);
  if (!ds) return;

  document.getElementById('dsId').value = ds.id;
  document.getElementById('dsName').value = ds.name;
  document.getElementById('dsProvider').value = ds.provider;
  document.getElementById('dsType').value = ds.type;
  document.getElementById('dsPriority').value = ds.priority;
  document.getElementById('dsDescription').value = ds.description || '';
  document.getElementById('dsSymbols').value = (ds.symbols || []).join(', ');

  if (ds.ib) {{
    document.getElementById('dsIbHost').value = ds.ib.host || '127.0.0.1';
    document.getElementById('dsIbPort').value = ds.ib.port || 7496;
    document.getElementById('dsIbClientId').value = ds.ib.clientId || 0;
    document.getElementById('dsIbPaper').checked = ds.ib.usePaperTrading || false;
    document.getElementById('dsIbDepth').checked = ds.ib.subscribeDepth !== false;
    document.getElementById('dsIbTick').checked = ds.ib.tickByTick !== false;
  }}

  if (ds.alpaca) {{
    document.getElementById('dsAlpacaFeed').value = ds.alpaca.feed || 'iex';
    document.getElementById('dsAlpacaSandbox').value = ds.alpaca.useSandbox ? 'true' : 'false';
    document.getElementById('dsAlpacaQuotes').checked = ds.alpaca.subscribeQuotes || false;
  }}

  if (ds.polygon) {{
    document.getElementById('dsPolygonKey').value = ds.polygon.apiKey || '';
    document.getElementById('dsPolygonFeed').value = ds.polygon.feed || 'stocks';
    document.getElementById('dsPolygonDelayed').checked = ds.polygon.useDelayed || false;
    document.getElementById('dsPolygonTrades').checked = ds.polygon.subscribeTrades !== false;
    document.getElementById('dsPolygonQuotes').checked = ds.polygon.subscribeQuotes || false;
    document.getElementById('dsPolygonAggs').checked = ds.polygon.subscribeAggregates || false;
  }}

  updateDsProviderUI();
}}

async function saveDataSource() {{
  const name = document.getElementById('dsName').value.trim();
  if (!name) {{
    document.getElementById('dsMsg').textContent = 'Name is required.';
    return;
  }}

  const provider = document.getElementById('dsProvider').value;
  const symbols = document.getElementById('dsSymbols').value
    .split(',')
    .map(s => s.trim().toUpperCase())
    .filter(s => s);

  const payload = {{
    id: document.getElementById('dsId').value || null,
    name: name,
    provider: provider,
    type: document.getElementById('dsType').value,
    priority: parseInt(document.getElementById('dsPriority').value) || 100,
    description: document.getElementById('dsDescription').value || null,
    symbols: symbols.length ? symbols : null,
    enabled: true
  }};

  if (provider === 'IB') {{
    payload.ib = {{
      host: document.getElementById('dsIbHost').value || '127.0.0.1',
      port: parseInt(document.getElementById('dsIbPort').value) || 7496,
      clientId: parseInt(document.getElementById('dsIbClientId').value) || 0,
      usePaperTrading: document.getElementById('dsIbPaper').checked,
      subscribeDepth: document.getElementById('dsIbDepth').checked,
      tickByTick: document.getElementById('dsIbTick').checked
    }};
  }} else if (provider === 'Alpaca') {{
    payload.alpaca = {{
      feed: document.getElementById('dsAlpacaFeed').value,
      useSandbox: document.getElementById('dsAlpacaSandbox').value === 'true',
      subscribeQuotes: document.getElementById('dsAlpacaQuotes').checked
    }};
  }} else if (provider === 'Polygon') {{
    payload.polygon = {{
      apiKey: document.getElementById('dsPolygonKey').value || null,
      feed: document.getElementById('dsPolygonFeed').value,
      useDelayed: document.getElementById('dsPolygonDelayed').checked,
      subscribeTrades: document.getElementById('dsPolygonTrades').checked,
      subscribeQuotes: document.getElementById('dsPolygonQuotes').checked,
      subscribeAggregates: document.getElementById('dsPolygonAggs').checked
    }};
  }}

  const r = await fetch('/api/config/datasources', {{
    method: 'POST',
    headers: {{ 'Content-Type': 'application/json' }},
    body: JSON.stringify(payload)
  }});

  const msg = document.getElementById('dsMsg');
  if (r.ok) {{
    msg.textContent = 'Data source saved successfully.';
    clearDsForm();
    await loadDataSources();
  }} else {{
    msg.textContent = 'Error saving data source.';
  }}
}}

async function deleteDataSource(id) {{
  if (!confirm('Delete this data source?')) return;

  const r = await fetch(`/api/config/datasources/${{encodeURIComponent(id)}}`, {{
    method: 'DELETE'
  }});

  if (r.ok) {{
    await loadDataSources();
  }}
}}

async function toggleDataSource(id, enabled) {{
  await fetch(`/api/config/datasources/${{encodeURIComponent(id)}}/toggle`, {{
    method: 'POST',
    headers: {{ 'Content-Type': 'application/json' }},
    body: JSON.stringify({{ enabled }})
  }});
}}

async function saveFailoverSettings() {{
  const r = await fetch('/api/config/datasources/failover', {{
    method: 'POST',
    headers: {{ 'Content-Type': 'application/json' }},
    body: JSON.stringify({{
      enableFailover: document.getElementById('enableFailover').checked,
      failoverTimeoutSeconds: parseInt(document.getElementById('failoverTimeout').value) || 30
    }})
  }});

  const msg = document.getElementById('dsMsg');
  msg.textContent = r.ok ? 'Failover settings saved.' : 'Error saving failover settings.';
}}

async function loadBackfillProviders(selectedProvider) {{
  try {{
    const r = await fetch('/api/backfill/providers');
    if (!r.ok) return;
    backfillProviders = await r.json();
    const select = document.getElementById('backfillProvider');
    if (!select) return;
    select.innerHTML = '';
    for (const p of backfillProviders) {{
      const opt = document.createElement('option');
      opt.value = p.name;
      opt.textContent = p.displayName || p.name;
      select.appendChild(opt);
    }}
    if (selectedProvider) {{
      select.value = selectedProvider;
    }}
    const help = document.getElementById('backfillHelp');
    if (help && backfillProviders.length) {{
      help.textContent = backfillProviders.map(p => `${{p.displayName || p.name}}: ${{p.description || ''}}`).join(' • ');
    }}
  }} catch (e) {{
    console.warn('Unable to load backfill providers', e);
  }}
}}

async function loadConfig() {{
  const r = await fetch('/api/config');
  const cfg = await r.json();

  // Update data source selector
  currentDataSource = cfg.dataSource || 'IB';
  document.getElementById('dataSource').value = currentDataSource;
  updateProviderUI();

  // Load Alpaca settings if available
  if (cfg.alpaca) {{
    document.getElementById('alpacaKeyId').value = cfg.alpaca.keyId || '';
    document.getElementById('alpacaSecretKey').value = cfg.alpaca.secretKey || '';
    document.getElementById('alpacaFeed').value = cfg.alpaca.feed || 'iex';
    document.getElementById('alpacaSandbox').value = cfg.alpaca.useSandbox ? 'true' : 'false';
    document.getElementById('alpacaSubscribeQuotes').checked = cfg.alpaca.subscribeQuotes || false;
  }}

  // Load storage settings
  document.getElementById('dataRoot').value = cfg.dataRoot || 'data';
  document.getElementById('compress').value = cfg.compress ? 'true' : 'false';
  if (cfg.storage) {{
    document.getElementById('namingConvention').value = cfg.storage.namingConvention || 'BySymbol';
    document.getElementById('datePartition').value = cfg.storage.datePartition || 'Daily';
    document.getElementById('includeProvider').value = cfg.storage.includeProvider ? 'true' : 'false';
    document.getElementById('filePrefix').value = cfg.storage.filePrefix || '';
  }}
  updateStoragePreview();

  await loadBackfillProviders(cfg.backfill ? cfg.backfill.provider : null);
  if (cfg.backfill) {{
    if (cfg.backfill.symbols) document.getElementById('backfillSymbols').value = cfg.backfill.symbols.join(',');
    if (cfg.backfill.from) document.getElementById('backfillFrom').value = cfg.backfill.from;
    if (cfg.backfill.to) document.getElementById('backfillTo').value = cfg.backfill.to;
    if (cfg.backfill.provider) document.getElementById('backfillProvider').value = cfg.backfill.provider;
  }}

  // Update symbols table
  const symbols = cfg.symbols || [];
  document.getElementById('symCount').textContent = symbols.length;

  const tbody = document.querySelector('#symbolsTable tbody');
  tbody.innerHTML = '';

  if (symbols.length === 0) {{
    tbody.innerHTML = '<tr><td colspan=""8"" class=""muted"" style=""text-align: center; padding: 24px;"">No symbols configured. Add one below.</td></tr>';
  }} else {{
    for (const s of symbols) {{
      const tr = document.createElement('tr');
      const secType = s.securityType || 'STK';
      const optionTypes = ['OPT', 'IND_OPT', 'FOP'];
      const futureTypes = ['FUT', 'SSF'];
      const typeColor = optionTypes.includes(secType) ? 'var(--accent-purple)' : (futureTypes.includes(secType) ? 'var(--accent-orange)' : 'var(--text-secondary)');
      tr.innerHTML = `
        <td><span style=""font-weight: 600; font-family: var(--font-mono); color: var(--accent-cyan);"">${{s.symbol}}</span></td>
        <td><span style=""font-family: var(--font-mono); font-size: 11px; color: ${{typeColor}};"">${{secType}}</span></td>
        <td>${{s.subscribeTrades ? '<span class=""good"">ON</span>' : '<span class=""muted"">OFF</span>'}}</td>
        <td>${{s.subscribeDepth ? '<span class=""good"">ON</span>' : '<span class=""muted"">OFF</span>'}}</td>
        <td style=""font-family: var(--font-mono);"">${{s.depthLevels || 10}}</td>
        <td style=""color: var(--text-secondary);"">${{s.localSymbol || '-'}}</td>
        <td style=""color: var(--text-secondary);"">${{s.exchange || '-'}}</td>
        <td><button class=""btn-danger"" onclick=""deleteSymbol('${{s.symbol}}')"">Delete</button></td>
      `;
      tbody.appendChild(tr);
    }}
  }}

  addLog('Configuration loaded successfully', 'success');
}}

function updateProviderUI() {{
  const isAlpaca = currentDataSource === 'Alpaca';

  // Show/hide Alpaca settings
  document.getElementById('alpacaSettings').classList.toggle('hidden', !isAlpaca);

  // Show/hide IB-specific fields
  document.getElementById('ibFields').classList.toggle('hidden', isAlpaca);

  // Update provider status message
  const statusDiv = document.getElementById('providerStatus');
  if (isAlpaca) {{
    statusDiv.innerHTML = '<span class=""tag tag-alpaca"">Alpaca</span> WebSocket streaming for trades and quotes';
  }} else {{
    statusDiv.innerHTML = '<span class=""tag tag-ib"">Interactive Brokers</span> TWS/Gateway connection for L2 depth and trades';
  }}
}}

async function updateDataSource() {{
  const ds = document.getElementById('dataSource').value;
  currentDataSource = ds;
  updateProviderUI();

  const r = await fetch('/api/config/datasource', {{
    method: 'POST',
    headers: {{'Content-Type': 'application/json'}},
    body: JSON.stringify({{ dataSource: ds }})
  }});

  if (r.ok) {{
    document.getElementById('msg').textContent = 'Data source updated. Restart collector to apply changes.';
  }}
}}

async function saveAlpacaSettings() {{
  const payload = {{
    keyId: document.getElementById('alpacaKeyId').value,
    secretKey: document.getElementById('alpacaSecretKey').value,
    feed: document.getElementById('alpacaFeed').value,
    useSandbox: document.getElementById('alpacaSandbox').value === 'true',
    subscribeQuotes: document.getElementById('alpacaSubscribeQuotes').checked
  }};

  const r = await fetch('/api/config/alpaca', {{
    method: 'POST',
    headers: {{'Content-Type': 'application/json'}},
    body: JSON.stringify(payload)
  }});

  const msgDiv = document.getElementById('alpacaMsg');
  msgDiv.textContent = r.ok ? 'Alpaca settings saved. Restart collector to apply.' : 'Error saving settings.';
}}

async function saveStorageSettings() {{
  const payload = {{
    dataRoot: document.getElementById('dataRoot').value,
    compress: document.getElementById('compress').value === 'true',
    namingConvention: document.getElementById('namingConvention').value,
    datePartition: document.getElementById('datePartition').value,
    includeProvider: document.getElementById('includeProvider').value === 'true',
    filePrefix: document.getElementById('filePrefix').value
  }};

  const r = await fetch('/api/config/storage', {{
    method: 'POST',
    headers: {{'Content-Type': 'application/json'}},
    body: JSON.stringify(payload)
  }});

  const msgDiv = document.getElementById('storageMsg');
  msgDiv.textContent = r.ok ? 'Storage settings saved. Restart collector to apply.' : 'Error saving settings.';
}}

function updateStoragePreview() {{
  const root = document.getElementById('dataRoot').value || 'data';
  const compress = document.getElementById('compress').value === 'true';
  const naming = document.getElementById('namingConvention').value;
  const partition = document.getElementById('datePartition').value;
  const prefix = document.getElementById('filePrefix').value;
  const ext = compress ? '.jsonl.gz' : '.jsonl';
  const pfx = prefix ? prefix + '_' : '';

  let dateStr = '';
  if (partition === 'Daily') dateStr = '2024-01-15';
  else if (partition === 'Hourly') dateStr = '2024-01-15_14';
  else if (partition === 'Monthly') dateStr = '2024-01';

  let path = '';
  if (naming === 'Flat') {{
    path = dateStr ? `${{root}}/${{pfx}}AAPL_Trade_${{dateStr}}${{ext}}` : `${{root}}/${{pfx}}AAPL_Trade${{ext}}`;
  }} else if (naming === 'BySymbol') {{
    path = dateStr ? `${{root}}/AAPL/Trade/${{pfx}}${{dateStr}}${{ext}}` : `${{root}}/AAPL/Trade/${{pfx}}data${{ext}}`;
  }} else if (naming === 'ByDate') {{
    path = dateStr ? `${{root}}/${{dateStr}}/AAPL/${{pfx}}Trade${{ext}}` : `${{root}}/AAPL/${{pfx}}Trade${{ext}}`;
  }} else if (naming === 'ByType') {{
    path = dateStr ? `${{root}}/Trade/AAPL/${{pfx}}${{dateStr}}${{ext}}` : `${{root}}/Trade/AAPL/${{pfx}}data${{ext}}`;
  }}

  document.getElementById('previewPath').textContent = path;
}}

// Add event listeners for live preview updates
['dataRoot', 'compress', 'namingConvention', 'datePartition', 'filePrefix'].forEach(id => {{
  document.getElementById(id).addEventListener('change', updateStoragePreview);
  document.getElementById(id).addEventListener('input', updateStoragePreview);
}});

async function loadStatus() {{
  try {{
    const r = await fetch('/api/status');
    const topDot = document.getElementById('topStatusDot');
    const topText = document.getElementById('topStatusText');
    const liveIndicator = document.getElementById('liveIndicator');

    if (!r.ok) {{
      topDot.className = 'status-dot disconnected';
      topText.textContent = 'Disconnected';
      liveIndicator.style.display = 'none';
      return;
    }}

    const s = await r.json();
    const isConnected = s.isConnected !== false;

    // Update top status
    topDot.className = isConnected ? 'status-dot connected' : 'status-dot disconnected';
    topText.textContent = isConnected ? 'Connected' : 'Disconnected';
    liveIndicator.style.display = isConnected ? 'flex' : 'none';

    // Update metric cards
    const metrics = s.metrics || {{}};
    const published = metrics.published || 0;
    const dropped = metrics.dropped || 0;
    const integrity = metrics.integrity || 0;
    const bars = metrics.historicalBars || 0;

    document.getElementById('publishedValue').textContent = formatNumber(published);
    document.getElementById('droppedValue').textContent = formatNumber(dropped);
    document.getElementById('integrityValue').textContent = formatNumber(integrity);
    document.getElementById('barsValue').textContent = formatNumber(bars);

    // Update trends
    if (published > prevMetrics.published) {{
      document.getElementById('publishedTrend').innerHTML = `<span>&#x2191;</span> +${{formatNumber(published - prevMetrics.published)}}`;
      document.getElementById('publishedTrend').className = 'metric-trend up';
    }}

    if (dropped > prevMetrics.dropped) {{
      document.getElementById('droppedTrend').innerHTML = `<span>&#x2191;</span> +${{dropped - prevMetrics.dropped}}`;
      document.getElementById('droppedTrend').className = 'metric-trend down';
      addLog(`Dropped events increased: +${{dropped - prevMetrics.dropped}}`, 'warning');
    }}

    prevMetrics = {{ published, dropped, integrity, bars }};

    // Log connection changes
    if (isConnected && topText.textContent !== 'Connected') {{
      addLog('Connection established', 'success');
    }}

  }} catch (e) {{
    document.getElementById('topStatusDot').className = 'status-dot disconnected';
    document.getElementById('topStatusText').textContent = 'Error';
    document.getElementById('liveIndicator').style.display = 'none';
  }}
}}

async function loadBackfillStatus() {{
  const box = document.getElementById('backfillStatus');
  try {{
    const r = await fetch('/api/backfill/status');
    if (!r.ok) {{
      box.innerHTML = `<div class=""terminal-line""><span class=""terminal-prompt"">&#36;</span><span class=""terminal-msg"">Ready to start backfill operation...</span></div>`;
      return;
    }}
    const status = await r.json();
    box.innerHTML = formatBackfillStatus(status);
  }} catch (e) {{
    box.innerHTML = `<div class=""terminal-line""><span class=""terminal-prompt"">&#36;</span><span class=""terminal-msg error"">Unable to load backfill status</span></div>`;
  }}
}}

function formatBackfillStatus(status) {{
  if (!status) return `<div class=""terminal-line""><span class=""terminal-prompt"">&#36;</span><span class=""terminal-msg"">No backfill runs yet.</span></div>`;

  const started = status.startedUtc ? new Date(status.startedUtc).toLocaleString() : 'n/a';
  const completed = status.completedUtc ? new Date(status.completedUtc).toLocaleString() : 'n/a';
  const statusClass = status.success ? 'success' : 'error';
  const statusText = status.success ? 'SUCCESS' : 'FAILED';
  const symbols = (status.symbols || []).join(', ');

  let html = `
    <div class=""terminal-line"">
      <span class=""terminal-prompt"">&#36;</span>
      <span class=""terminal-msg ${{statusClass}}"">[${{statusText}}] Backfill completed</span>
    </div>
    <div class=""terminal-line"">
      <span class=""terminal-prompt"">&nbsp;</span>
      <span class=""terminal-msg"">Provider: ${{status.provider}} | Bars written: ${{status.barsWritten || 0}}</span>
    </div>
    <div class=""terminal-line"">
      <span class=""terminal-prompt"">&nbsp;</span>
      <span class=""terminal-msg"">Symbols: ${{symbols || 'n/a'}}</span>
    </div>
    <div class=""terminal-line"">
      <span class=""terminal-prompt"">&nbsp;</span>
      <span class=""terminal-msg"" style=""color: var(--text-muted);"">Started: ${{started}} | Completed: ${{completed}}</span>
    </div>
  `;

  if (status.error) {{
    html += `<div class=""terminal-line""><span class=""terminal-prompt"">!</span><span class=""terminal-msg error"">${{status.error}}</span></div>`;
  }}

  return html;
}}

async function runBackfill() {{
  const statusBox = document.getElementById('backfillStatus');
  const btn = document.getElementById('backfillBtn');
  const provider = document.getElementById('backfillProvider').value || 'stooq';
  const symbols = (document.getElementById('backfillSymbols').value || '')
    .split(',')
    .map(s => s.trim().toUpperCase())
    .filter(s => s);
  const from = document.getElementById('backfillFrom').value || null;
  const to = document.getElementById('backfillTo').value || null;

  if (!symbols.length) {{
    statusBox.innerHTML = `<div class=""terminal-line""><span class=""terminal-prompt"">!</span><span class=""terminal-msg warning"">Please enter at least one symbol to backfill.</span></div>`;
    showToast('Please enter at least one symbol', 'warning');
    return;
  }}

  // Show loading state
  btn.disabled = true;
  btn.innerHTML = '<span class=""spinner""></span> Running...';

  statusBox.innerHTML = `
    <div class=""terminal-line""><span class=""terminal-prompt"">&#36;</span><span class=""terminal-msg"">Initializing backfill for ${{symbols.join(', ')}}...</span></div>
    <div class=""terminal-line""><span class=""terminal-prompt"">&#36;</span><span class=""terminal-msg"">Provider: ${{provider}}</span></div>
  `;

  addLog(`Starting backfill: ${{symbols.join(', ')}}`, 'success');

  const payload = {{ provider, symbols, from, to }};
  try {{
    const r = await fetch('/api/backfill/run', {{
      method: 'POST',
      headers: {{ 'Content-Type': 'application/json' }},
      body: JSON.stringify(payload)
    }});

    if (!r.ok) {{
      const msg = await r.text();
      statusBox.innerHTML = `<div class=""terminal-line""><span class=""terminal-prompt"">!</span><span class=""terminal-msg error"">${{msg || 'Backfill failed'}}</span></div>`;
      showToast('Backfill failed', 'error');
      addLog('Backfill failed: ' + (msg || 'Unknown error'), 'error');
      return;
    }}

    const result = await r.json();
    statusBox.innerHTML = formatBackfillStatus(result);
    showToast(`Backfill completed: ${{result.barsWritten || 0}} bars written`, result.success ? 'success' : 'error');
    addLog(`Backfill completed: ${{result.barsWritten || 0}} bars written`, result.success ? 'success' : 'error');
  }} finally {{
    btn.disabled = false;
    btn.innerHTML = '<span>&#x23F3;</span> Start Backfill';
  }}
}}

function toggleOptionsFields() {{
  const secType = document.getElementById('secType').value;
  const isOption = ['OPT', 'IND_OPT', 'FOP'].includes(secType);
  document.getElementById('optionFields').classList.toggle('hidden', !isOption);
  if (isOption && secType === 'IND_OPT') {{
    document.getElementById('optStyle').value = 'European';
  }}
}}

function clearSymbolForm() {{
  document.getElementById('sym').value = '';
  document.getElementById('secType').value = 'STK';
  document.getElementById('localsym').value = '';
  document.getElementById('pexch').value = '';
  document.getElementById('optStrike').value = '';
  document.getElementById('optRight').value = 'Call';
  document.getElementById('optExpiry').value = '';
  document.getElementById('optStyle').value = 'American';
  document.getElementById('optMultiplier').value = '100';
  toggleOptionsFields();
}}

function toggleDerivativesFields() {{
  const enabled = document.getElementById('derivEnabled').checked;
  document.getElementById('derivFields').style.opacity = enabled ? '1' : '0.5';
  document.getElementById('derivFields').style.pointerEvents = enabled ? 'auto' : 'none';
}}

async function loadDerivativesConfig() {{
  try {{
    const r = await fetch('/api/config/derivatives');
    if (!r.ok) return;
    const cfg = await r.json();

    document.getElementById('derivEnabled').checked = cfg.enabled || false;
    document.getElementById('derivUnderlyings').value = (cfg.underlyings || []).join(', ');
    document.getElementById('derivMaxDte').value = cfg.maxDaysToExpiration || 90;
    document.getElementById('derivStrikeRange').value = cfg.strikeRange || 20;
    document.getElementById('derivGreeks').checked = cfg.captureGreeks !== false;
    document.getElementById('derivOI').checked = cfg.captureOpenInterest !== false;
    document.getElementById('derivChainSnap').checked = cfg.captureChainSnapshots || false;
    document.getElementById('derivChainInterval').value = cfg.chainSnapshotIntervalSeconds || 300;

    const expFilter = cfg.expirationFilter || ['Weekly', 'Monthly'];
    document.getElementById('derivExpWeekly').checked = expFilter.includes('Weekly');
    document.getElementById('derivExpMonthly').checked = expFilter.includes('Monthly');
    document.getElementById('derivExpQuarterly').checked = expFilter.includes('Quarterly');
    document.getElementById('derivExpLeaps').checked = expFilter.includes('LEAPS');

    if (cfg.indexOptions) {{
      document.getElementById('derivIdxEnabled').checked = cfg.indexOptions.enabled || false;
      document.getElementById('derivIdxIndices').value = (cfg.indexOptions.indices || []).join(', ');
      document.getElementById('derivIdxWeeklies').checked = cfg.indexOptions.includeWeeklies !== false;
      document.getElementById('derivIdxAmSettled').checked = cfg.indexOptions.includeAmSettled !== false;
      document.getElementById('derivIdxPmSettled').checked = cfg.indexOptions.includePmSettled !== false;
    }}

    toggleDerivativesFields();
  }} catch (e) {{
    console.warn('Unable to load derivatives config', e);
  }}
}}

async function saveDerivativesConfig() {{
  const expFilter = [];
  if (document.getElementById('derivExpWeekly').checked) expFilter.push('Weekly');
  if (document.getElementById('derivExpMonthly').checked) expFilter.push('Monthly');
  if (document.getElementById('derivExpQuarterly').checked) expFilter.push('Quarterly');
  if (document.getElementById('derivExpLeaps').checked) expFilter.push('LEAPS');

  const underlyings = document.getElementById('derivUnderlyings').value
    .split(',').map(s => s.trim().toUpperCase()).filter(s => s);

  const indices = document.getElementById('derivIdxIndices').value
    .split(',').map(s => s.trim().toUpperCase()).filter(s => s);

  const payload = {{
    enabled: document.getElementById('derivEnabled').checked,
    underlyings: underlyings.length ? underlyings : null,
    maxDaysToExpiration: parseInt(document.getElementById('derivMaxDte').value) || 90,
    strikeRange: parseInt(document.getElementById('derivStrikeRange').value) || 20,
    captureGreeks: document.getElementById('derivGreeks').checked,
    captureChainSnapshots: document.getElementById('derivChainSnap').checked,
    chainSnapshotIntervalSeconds: parseInt(document.getElementById('derivChainInterval').value) || 300,
    captureOpenInterest: document.getElementById('derivOI').checked,
    expirationFilter: expFilter.length ? expFilter : null,
    indexOptions: {{
      enabled: document.getElementById('derivIdxEnabled').checked,
      indices: indices.length ? indices : null,
      includeWeeklies: document.getElementById('derivIdxWeeklies').checked,
      includeAmSettled: document.getElementById('derivIdxAmSettled').checked,
      includePmSettled: document.getElementById('derivIdxPmSettled').checked
    }}
  }};

  const r = await fetch('/api/config/derivatives', {{
    method: 'POST',
    headers: {{ 'Content-Type': 'application/json' }},
    body: JSON.stringify(payload)
  }});

  const msg = document.getElementById('derivMsg');
  if (r.ok) {{
    msg.innerHTML = '<span class=""good"">Derivatives settings saved. Restart collector to apply.</span>';
    showToast('Derivatives settings saved', 'success');
    addLog('Derivatives configuration updated', 'success');
  }} else {{
    msg.innerHTML = '<span class=""bad"">Error saving derivatives settings.</span>';
    showToast('Failed to save derivatives settings', 'error');
  }}
}}

async function refreshOptionsSummary() {{
  try {{
    const r = await fetch('/api/options/summary');
    if (r.ok) {{
      const data = await r.json();
      document.getElementById('optContracts').textContent = data.trackedContracts ?? '--';
      document.getElementById('optChains').textContent = data.trackedChains ?? '--';
      document.getElementById('optUnderlyings').textContent = data.trackedUnderlyings ?? '--';
      document.getElementById('optGreeks').textContent = data.contractsWithGreeks ?? '--';
      document.getElementById('optProviderStatus').textContent =
        'Provider: ' + (data.providerAvailable ? 'Connected' : 'Not configured');
    }} else {{
      document.getElementById('optProviderStatus').textContent = 'Provider: unavailable (API returned ' + r.status + ')';
    }}

    const u = await fetch('/api/options/underlyings');
    if (u.ok) {{
      const data = await u.json();
      const syms = data && Array.isArray(data.underlyings) ? data.underlyings : [];
      document.getElementById('optTrackedList').textContent =
        syms.length > 0 ? syms.join(', ') : 'None';
    }}
  }} catch (e) {{
    document.getElementById('optProviderStatus').textContent = 'Provider: error - ' + e.message;
  }}
}}

async function addSymbol() {{
  const symbol = document.getElementById('sym').value.trim().toUpperCase();
  if (!symbol) {{
    document.getElementById('msg').textContent = 'Symbol is required.';
    showToast('Symbol is required', 'warning');
    return;
  }}

  const secType = document.getElementById('secType').value;
  const isOption = ['OPT', 'IND_OPT', 'FOP'].includes(secType);

  const payload = {{
    symbol: symbol,
    subscribeTrades: document.getElementById('trades').value === 'true',
    subscribeDepth: document.getElementById('depth').value === 'true',
    depthLevels: parseInt(document.getElementById('levels').value || '10', 10),
    securityType: secType,
    exchange: document.getElementById('exch').value || 'SMART',
    currency: 'USD',
    primaryExchange: document.getElementById('pexch').value || null,
    localSymbol: document.getElementById('localsym').value || null
  }};

  if (isOption) {{
    const strike = parseFloat(document.getElementById('optStrike').value);
    const expiry = document.getElementById('optExpiry').value;
    if (!strike || !expiry) {{
      document.getElementById('msg').textContent = 'Strike price and expiration are required for options.';
      showToast('Strike and expiration required for options', 'warning');
      return;
    }}
    payload.strike = strike;
    payload.right = document.getElementById('optRight').value;
    payload.lastTradeDateOrContractMonth = expiry;
    payload.optionStyle = document.getElementById('optStyle').value;
    payload.multiplier = parseInt(document.getElementById('optMultiplier').value) || 100;
  }}

  const r = await fetch('/api/config/symbols', {{
    method: 'POST',
    headers: {{'Content-Type': 'application/json'}},
    body: JSON.stringify(payload)
  }});

  const msg = document.getElementById('msg');
  if (r.ok) {{
    msg.innerHTML = `<span class=""good"">Symbol ${{symbol}} added successfully.</span>`;
    showToast(`Symbol ${{symbol}} added`, 'success');
    addLog(`Symbol added: ${{symbol}}`, 'success');
    clearSymbolForm();
    await loadConfig();
  }} else {{
    msg.innerHTML = '<span class=""bad"">Error adding symbol.</span>';
    showToast('Failed to add symbol', 'error');
  }}
}}

async function deleteSymbol(symbol) {{
  if (!confirm(`Delete symbol ${{symbol}}?`)) return;

  const r = await fetch(`/api/config/symbols/${{encodeURIComponent(symbol)}}`, {{
    method: 'DELETE'
  }});

  if (r.ok) {{
    await loadConfig();
  }}
}}

// SSE real-time updates with polling fallback
let sseConnection = null;
let pollingInterval = null;

function startSSE() {{
  if (typeof EventSource === 'undefined') {{
    startPolling();
    return;
  }}

  sseConnection = new EventSource('/api/events/stream');

  sseConnection.onmessage = function(event) {{
    try {{
      const data = JSON.parse(event.data);
      if (data.status) updateStatusFromSSE(data.status);
      if (data.backpressure) updateBackpressureFromSSE(data.backpressure);
    }} catch (e) {{
      console.warn('SSE parse error', e);
    }}
  }};

  sseConnection.onerror = function() {{
    sseConnection.close();
    sseConnection = null;
    addLog('SSE connection lost, falling back to polling', 'warning');
    startPolling();
    // Try to reconnect SSE after 10 seconds
    setTimeout(() => {{
      if (!sseConnection) {{
        stopPolling();
        startSSE();
      }}
    }}, 10000);
  }};

  sseConnection.onopen = function() {{
    stopPolling();
    addLog('SSE connection established', 'success');
  }};
}}

function updateStatusFromSSE(s) {{
  const isConnected = s.isConnected !== false;
  const topDot = document.getElementById('topStatusDot');
  const topText = document.getElementById('topStatusText');
  const liveIndicator = document.getElementById('liveIndicator');

  topDot.className = isConnected ? 'status-dot connected' : 'status-dot disconnected';
  topText.textContent = isConnected ? 'Connected' : 'Disconnected';
  liveIndicator.style.display = isConnected ? 'flex' : 'none';

  const metrics = s.metrics || {{}};
  const published = metrics.published || 0;
  const dropped = metrics.dropped || 0;
  const integrity = metrics.integrity || 0;
  const bars = metrics.historicalBars || 0;

  document.getElementById('publishedValue').textContent = formatNumber(published);
  document.getElementById('droppedValue').textContent = formatNumber(dropped);
  document.getElementById('integrityValue').textContent = formatNumber(integrity);
  document.getElementById('barsValue').textContent = formatNumber(bars);

  if (published > prevMetrics.published) {{
    document.getElementById('publishedTrend').innerHTML = `<span>&#x2191;</span> +${{formatNumber(published - prevMetrics.published)}}`;
    document.getElementById('publishedTrend').className = 'metric-trend up';
  }}

  if (dropped > prevMetrics.dropped) {{
    document.getElementById('droppedTrend').innerHTML = `<span>&#x2191;</span> +${{dropped - prevMetrics.dropped}}`;
    document.getElementById('droppedTrend').className = 'metric-trend down';
    addLog(`Dropped events increased: +${{dropped - prevMetrics.dropped}}`, 'warning');
  }}

  prevMetrics = {{ published, dropped, integrity, bars }};
}}

function updateBackpressureFromSSE(bp) {{
  // Backpressure data available for monitoring
  if (bp && bp.level && bp.level !== 'None') {{
    addLog(`Backpressure: ${{bp.level}}`, bp.level === 'High' ? 'error' : 'warning');
  }}
}}

function startPolling() {{
  if (pollingInterval) return;
  pollingInterval = setInterval(loadStatus, 2000);
}}

function stopPolling() {{
  if (pollingInterval) {{
    clearInterval(pollingInterval);
    pollingInterval = null;
  }}
}}

// --- Data Freshness Bar ---
let lastEventTimestamp = null;
let eventCountSinceLastCheck = 0;

async function updateFreshnessBar() {{
  const container = document.getElementById('freshnessProviders');
  const lastEventEl = document.getElementById('freshnessLastEvent');
  const throughputEl = document.getElementById('freshnessThroughput');

  try {{
    const r = await fetch('/api/providers/status');
    if (!r.ok) {{
      container.innerHTML = `<span class=""freshness-label"">Providers:</span><span class=""freshness-loading"">Unavailable</span>`;
      return;
    }}
    const data = await r.json();
    const providers = data.providers || data || [];

    // Build provider dots
    let html = `<span class=""freshness-label"">Providers:</span>`;
    if (Array.isArray(providers) && providers.length > 0) {{
      providers.forEach(p => {{
        const name = p.name || p.id || 'Unknown';
        const isConnected = p.isConnected || p.status === 'Connected' || p.status === 'Active';
        const isEnabled = p.isEnabled !== false;
        const dotClass = isConnected ? 'green' : (isEnabled ? 'red' : 'gray');
        const title = `${{name}}: ${{isConnected ? 'Connected' : (isEnabled ? 'Disconnected' : 'Disabled')}}`;
        html += `<span title=""${{title}}"" class=""freshness-dot ${{dotClass}}""></span><span style=""margin-right:8px"">${{name}}</span>`;
      }});
    }} else {{
      html += `<span class=""freshness-dot gray""></span><span>No providers</span>`;
    }}
    container.innerHTML = html;

    // Update last event time from status
    const sr = await fetch('/api/status');
    if (sr.ok) {{
      const status = await sr.json();
      const ts = status.timestampUtc || status.lastEventUtc;
      if (ts) {{
        const eventDate = new Date(ts);
        const ageSeconds = (Date.now() - eventDate.getTime()) / 1000;
        lastEventTimestamp = eventDate;

        if (ageSeconds < 30) {{
          lastEventEl.textContent = 'Just now';
          lastEventEl.className = 'freshness-value';
        }} else if (ageSeconds < 120) {{
          lastEventEl.textContent = `${{Math.floor(ageSeconds)}}s ago`;
          lastEventEl.className = 'freshness-value';
        }} else if (ageSeconds < 3600) {{
          lastEventEl.textContent = `${{Math.floor(ageSeconds / 60)}}m ago`;
          lastEventEl.className = 'freshness-value stale';
        }} else {{
          lastEventEl.textContent = `${{Math.floor(ageSeconds / 3600)}}h ago`;
          lastEventEl.className = 'freshness-value dead';
        }}
      }} else {{
        lastEventEl.textContent = '--';
        lastEventEl.className = 'freshness-value';
      }}

      // Throughput from metrics
      const metrics = status.metrics || {{}};
      const published = metrics.published || 0;
      const rate = published > prevMetrics.published
        ? ((published - prevMetrics.published) / 3).toFixed(1)
        : '0';
      throughputEl.textContent = `${{rate}} evt/s`;
    }}
  }} catch (e) {{
    // Silently handle network errors
  }}
}}

// Initial load
loadConfig();
loadStatus();
loadBackfillStatus();
loadDataSources();
loadDerivativesConfig();
refreshOptionsSummary();
updateFreshnessBar();
startSSE();
setInterval(loadBackfillStatus, 5000);
setInterval(refreshOptionsSummary, 10000);
setInterval(updateFreshnessBar, 3000);
";
}
