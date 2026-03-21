// Meridian Dashboard JavaScript
let currentDataSource = 'IB';
let cachedSymbols = [];
let backfillProviders = [];

// Toast Notification System
function showToast(type, title, message) {
  const container = document.getElementById('toastContainer');
  const toast = document.createElement('div');
  toast.className = `toast toast-${type}`;

  const icons = {
    success: '✅',
    error: '❌',
    info: 'ℹ️'
  };

  toast.innerHTML = `
    <div class="toast-icon">${icons[type] || 'ℹ️'}</div>
    <div class="toast-content">
      <div class="toast-title">${title}</div>
      <div class="toast-message">${message}</div>
    </div>
  `;

  container.appendChild(toast);

  setTimeout(() => {
    toast.style.animation = 'slideIn 0.3s ease reverse';
    setTimeout(() => toast.remove(), 300);
  }, 5000);
}

// Help Modal
function openHelp() {
  document.getElementById('helpModal').style.display = 'block';
}

function closeHelp() {
  document.getElementById('helpModal').style.display = 'none';
}

window.onclick = function(event) {
  const modal = document.getElementById('helpModal');
  if (event.target === modal) {
    closeHelp();
  }
};

// API Functions with Error Handling and Timeout
async function apiCall(url, options = {}) {
  const timeoutMs = options.timeoutMs || 30000;
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, {
      ...options,
      signal: controller.signal
    });
    if (!response.ok) {
      let errorMsg;
      try {
        const body = await response.json();
        errorMsg = body.error || body.message || `HTTP ${response.status}`;
      } catch {
        errorMsg = `HTTP ${response.status}`;
      }
      if (response.status === 400) {
        showToast('error', 'Validation Error', errorMsg);
      } else if (response.status === 404) {
        showToast('info', 'Not Found', errorMsg);
      } else if (response.status >= 500) {
        showToast('error', 'Server Error', 'The server encountered an error. Please try again.');
      }
      throw new Error(errorMsg);
    }
    return response;
  } catch (error) {
    if (error.name === 'AbortError') {
      showToast('error', 'Timeout', `Request to ${url.split('?')[0]} timed out after ${timeoutMs / 1000}s`);
      throw new Error(`Request timed out after ${timeoutMs / 1000}s`);
    }
    if (error instanceof TypeError && error.message === 'Failed to fetch') {
      showToast('error', 'Connection Error', 'Unable to reach the server. Check your connection.');
    }
    throw error;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function loadBackfillProviders(selectedProvider) {
  try {
    const r = await apiCall('/api/backfill/providers');
    backfillProviders = await r.json();
    const select = document.getElementById('backfillProvider');
    if (!select) return;
    select.innerHTML = '';
    for (const p of backfillProviders) {
      const opt = document.createElement('option');
      opt.value = p.name;
      opt.textContent = p.displayName || p.name;
      select.appendChild(opt);
    }
    if (selectedProvider) {
      select.value = selectedProvider;
    }
  } catch (e) {
    console.warn('Unable to load backfill providers', e);
  }
}

async function loadConfig() {
  try {
    const r = await apiCall('/api/config');
    const cfg = await r.json();

    cachedSymbols = cfg.symbols || [];

    currentDataSource = cfg.dataSource || 'IB';
    document.getElementById('dataSource').value = currentDataSource;
    updateProviderUI();

    if (cfg.alpaca) {
      document.getElementById('alpacaKeyId').value = cfg.alpaca.keyId || '';
      document.getElementById('alpacaSecretKey').value = cfg.alpaca.secretKey || '';
      document.getElementById('alpacaFeed').value = cfg.alpaca.feed || 'iex';
      document.getElementById('alpacaSandbox').value = cfg.alpaca.useSandbox ? 'true' : 'false';
      document.getElementById('alpacaSubscribeQuotes').checked = cfg.alpaca.subscribeQuotes || false;
    }

    document.getElementById('dataRoot').value = cfg.dataRoot || 'data';
    document.getElementById('compress').value = cfg.compress ? 'true' : 'false';
    if (cfg.storage) {
      document.getElementById('namingConvention').value = cfg.storage.namingConvention || 'BySymbol';
      document.getElementById('datePartition').value = cfg.storage.datePartition || 'Daily';
      document.getElementById('includeProvider').value = cfg.storage.includeProvider ? 'true' : 'false';
      document.getElementById('filePrefix').value = cfg.storage.filePrefix || '';
    }
    updateStoragePreview();

    await loadBackfillProviders(cfg.backfill ? cfg.backfill.provider : null);
    if (cfg.backfill) {
      if (cfg.backfill.symbols) document.getElementById('backfillSymbols').value = cfg.backfill.symbols.join(',');
      if (cfg.backfill.from) document.getElementById('backfillFrom').value = cfg.backfill.from;
      if (cfg.backfill.to) document.getElementById('backfillTo').value = cfg.backfill.to;
      if (cfg.backfill.provider) document.getElementById('backfillProvider').value = cfg.backfill.provider;
    }

    const tbody = document.querySelector('#symbolsTable tbody');
    tbody.innerHTML = '';
    for (const s of (cfg.symbols || [])) {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td><strong>${s.symbol}</strong></td>
        <td>${s.subscribeTrades ? '<span style="color: #48bb78;">✓ Yes</span>' : '✗ No'}</td>
        <td>${s.subscribeDepth ? '<span style="color: #48bb78;">✓ Yes</span>' : '✗ No'}</td>
        <td>${s.depthLevels || 10}</td>
        <td>${s.localSymbol || '-'}</td>
        <td>${s.exchange || '-'}</td>
        <td><div style="display:flex;gap:8px;flex-wrap:wrap;"><button class="btn-secondary" onclick="editSymbol('${s.symbol}')">✏️ Edit</button><button class="btn-danger" onclick="deleteSymbol('${s.symbol}')">🗑️ Delete</button></div></td>
      `;
      tbody.appendChild(tr);
    }

    showToast('success', 'Configuration Loaded', 'Settings loaded successfully');
  } catch (error) {
    showToast('error', 'Load Failed', 'Could not load configuration: ' + error.message);
  }
}

function updateProviderUI() {
  const isAlpaca = currentDataSource === 'Alpaca';
  document.getElementById('alpacaSettings').classList.toggle('hidden', !isAlpaca);
  document.getElementById('ibFields').classList.toggle('hidden', isAlpaca);

  const statusDiv = document.getElementById('providerStatus');
  if (isAlpaca) {
    statusDiv.innerHTML = '<span class="tag tag-alpaca">Alpaca</span> WebSocket streaming for trades and quotes';
  } else {
    statusDiv.innerHTML = '<span class="tag tag-ib">Interactive Brokers</span> TWS/Gateway connection for L2 depth and trades';
  }
}

async function updateDataSource() {
  try {
    const ds = document.getElementById('dataSource').value;
    currentDataSource = ds;
    updateProviderUI();

    await apiCall('/api/config/datasource', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({ dataSource: ds })
    });

    showToast('success', 'Provider Updated', 'Data source changed. Restart collector to apply.');
  } catch (error) {
    showToast('error', 'Update Failed', error.message);
  }
}

async function saveAlpacaSettings() {
  try {
    const payload = {
      keyId: document.getElementById('alpacaKeyId').value,
      secretKey: document.getElementById('alpacaSecretKey').value,
      feed: document.getElementById('alpacaFeed').value,
      useSandbox: document.getElementById('alpacaSandbox').value === 'true',
      subscribeQuotes: document.getElementById('alpacaSubscribeQuotes').checked
    };

    await apiCall('/api/config/alpaca', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify(payload)
    });

    showToast('success', 'Settings Saved', 'Alpaca settings saved. Restart collector to apply.');
  } catch (error) {
    showToast('error', 'Save Failed', error.message);
  }
}

async function saveStorageSettings() {
  try {
    const payload = {
      dataRoot: document.getElementById('dataRoot').value,
      compress: document.getElementById('compress').value === 'true',
      namingConvention: document.getElementById('namingConvention').value,
      datePartition: document.getElementById('datePartition').value,
      includeProvider: document.getElementById('includeProvider').value === 'true',
      filePrefix: document.getElementById('filePrefix').value
    };

    await apiCall('/api/config/storage', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify(payload)
    });

    showToast('success', 'Settings Saved', 'Storage settings saved. Restart collector to apply.');
  } catch (error) {
    showToast('error', 'Save Failed', error.message);
  }
}

function updateStoragePreview() {
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
  if (naming === 'Flat') {
    path = dateStr ? `${root}/${pfx}AAPL_Trade_${dateStr}${ext}` : `${root}/${pfx}AAPL_Trade${ext}`;
  } else if (naming === 'BySymbol') {
    path = dateStr ? `${root}/AAPL/Trade/${pfx}${dateStr}${ext}` : `${root}/AAPL/Trade/${pfx}data${ext}`;
  } else if (naming === 'ByDate') {
    path = dateStr ? `${root}/${dateStr}/AAPL/${pfx}Trade${ext}` : `${root}/AAPL/${pfx}Trade${ext}`;
  } else if (naming === 'ByType') {
    path = dateStr ? `${root}/Trade/AAPL/${pfx}${dateStr}${ext}` : `${root}/Trade/AAPL/${pfx}data${ext}`;
  } else if (naming === 'BySource') {
    path = dateStr ? `${root}/alpaca/AAPL/Trade/${pfx}${dateStr}${ext}` : `${root}/alpaca/AAPL/Trade/${pfx}data${ext}`;
  } else if (naming === 'ByAssetClass') {
    path = dateStr ? `${root}/equity/AAPL/Trade/${pfx}${dateStr}${ext}` : `${root}/equity/AAPL/Trade/${pfx}data${ext}`;
  } else if (naming === 'Hierarchical') {
    path = dateStr ? `${root}/alpaca/equity/AAPL/Trade/${pfx}${dateStr}${ext}` : `${root}/alpaca/equity/AAPL/Trade/${pfx}data${ext}`;
  } else if (naming === 'Canonical') {
    path = `${root}/2024/01/15/alpaca/AAPL/${pfx}Trade${ext}`;
  }

  document.getElementById('previewPath').textContent = path;
}

['dataRoot', 'compress', 'namingConvention', 'datePartition', 'filePrefix'].forEach(id => {
  document.getElementById(id).addEventListener('change', updateStoragePreview);
  document.getElementById(id).addEventListener('input', updateStoragePreview);
});

async function loadStatus() {
  const box = document.getElementById('statusBox');
  try {
    const r = await apiCall('/api/status');
    const s = await r.json();
    const isConnected = s.isConnected !== false;

    // Calculate data freshness
    const freshnessHtml = getDataFreshnessHtml(s);

    box.innerHTML = `
      <div class="status-badge ${isConnected ? 'status-connected' : 'status-disconnected'}">
        <span class="status-dot"></span>
        ${isConnected ? 'Connected' : 'Disconnected'}
      </div>
      ${freshnessHtml}
      <div style="margin-top: 8px; font-size: 12px; color: #718096;">
        Last update: ${s.timestampUtc || 'n/a'}
      </div>
    `;

    document.getElementById('metricPublished').textContent = (s.metrics && s.metrics.published) || 0;
    document.getElementById('metricDropped').textContent = (s.metrics && s.metrics.dropped) || 0;
    document.getElementById('metricIntegrity').textContent = (s.metrics && s.metrics.integrity) || 0;
    document.getElementById('metricBars').textContent = (s.metrics && s.metrics.historicalBars) || 0;
  } catch (e) {
    box.innerHTML = `
      <div class="status-badge status-disconnected">
        <span class="status-dot"></span>
        No Status
      </div>
      <div style="margin-top: 8px; font-size: 12px; color: #718096;">
        Start collector with --http-port 8080
      </div>
    `;
  }
}

function getDataFreshnessHtml(status) {
  const timestamp = status.timestampUtc || status.lastEventUtc;
  if (!timestamp) return '';

  const lastUpdate = new Date(timestamp);
  if (Number.isNaN(lastUpdate.getTime())) {
    // Invalid timestamp format; avoid displaying misleading freshness info
    return '';
  }
  const now = new Date();
  const diffMs = now - lastUpdate;
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHr = Math.floor(diffMin / 60);

  let freshnessText, freshnessColor, freshnessIcon;

  if (diffSec < 30) {
    freshnessText = 'Live';
    freshnessColor = '#48bb78';
    freshnessIcon = '🟢';
  } else if (diffSec < 120) {
    freshnessText = `${diffSec}s ago`;
    freshnessColor = '#48bb78';
    freshnessIcon = '🟢';
  } else if (diffMin < 10) {
    freshnessText = `${diffMin}m ago`;
    freshnessColor = '#ecc94b';
    freshnessIcon = '🟡';
  } else if (diffMin < 60) {
    freshnessText = `${diffMin}m ago`;
    freshnessColor = '#ed8936';
    freshnessIcon = '🟠';
  } else if (diffHr < 24) {
    freshnessText = `${diffHr}h ${diffMin % 60}m ago`;
    freshnessColor = '#f56565';
    freshnessIcon = '🔴';
  } else {
    const diffDays = Math.floor(diffHr / 24);
    freshnessText = `${diffDays}d ago`;
    freshnessColor = '#a0aec0';
    freshnessIcon = '⚪';
  }

  return `
    <div style="margin-top: 8px; display: inline-flex; align-items: center; gap: 6px;
                padding: 4px 12px; border-radius: 8px; background: ${freshnessColor}15;
                border: 1px solid ${freshnessColor}30; font-size: 12px;">
      <span>${freshnessIcon}</span>
      <span style="font-weight: 600; color: ${freshnessColor};">Data: ${freshnessText}</span>
    </div>
  `;
}

async function loadBackfillStatus() {
  const box = document.getElementById('backfillStatus');
  try {
    const r = await apiCall('/api/backfill/status');
    const status = await r.json();
    box.innerHTML = formatBackfillStatus(status);
  } catch (e) {
    box.textContent = 'No backfill runs yet.';
  }
}

function formatBackfillStatus(status) {
  if (!status) return 'No backfill runs yet.';
  const started = status.startedUtc ? new Date(status.startedUtc).toLocaleString() : 'n/a';
  const completed = status.completedUtc ? new Date(status.completedUtc).toLocaleString() : 'n/a';
  const badge = status.success
    ? '<span style="color: #48bb78; font-weight: 600;">✅ Success</span>'
    : '<span style="color: #f56565; font-weight: 600;">❌ Failed</span>';
  const symbols = (status.symbols || []).join(', ');
  const error = status.error ? `<div style="color: #f56565; margin-top: 8px;">${status.error}</div>` : '';
  return `
    <div><strong>${badge}</strong></div>
    <div style="margin-top: 8px;">
      <strong>Provider:</strong> ${status.provider}<br/>
      <strong>Bars Written:</strong> ${status.barsWritten || 0}<br/>
      <strong>Symbols:</strong> ${symbols || 'n/a'}<br/>
      <strong>Started:</strong> ${started}<br/>
      <strong>Completed:</strong> ${completed}
    </div>
    ${error}
  `;
}

function editSymbol(symbol) {
  const match = (cachedSymbols || []).find(s => (s.symbol || '').toLowerCase() === symbol.toLowerCase());
  if (!match) {
    showToast('error', 'Not Found', `Cannot find ${symbol} in current configuration`);
    return;
  }

  document.getElementById('sym').value = match.symbol || '';
  document.getElementById('trades').value = match.subscribeTrades ? 'true' : 'false';
  document.getElementById('depth').value = match.subscribeDepth ? 'true' : 'false';
  document.getElementById('levels').value = match.depthLevels || 10;
  document.getElementById('localsym').value = match.localSymbol || '';
  document.getElementById('exch').value = match.exchange || 'SMART';
  document.getElementById('pexch').value = match.primaryExchange || '';

  showToast('info', 'Editing symbol', `Loaded ${symbol} into the form. Update fields and click Add Symbol to save.`);
}

async function addSymbol() {
  try {
    const symbol = document.getElementById('sym').value.trim();
    if (!symbol) {
      showToast('error', 'Validation Error', 'Symbol is required');
      return;
    }

    const payload = {
      symbol: symbol,
      subscribeTrades: document.getElementById('trades').value === 'true',
      subscribeDepth: document.getElementById('depth').value === 'true',
      depthLevels: parseInt(document.getElementById('levels').value || '10', 10),
      securityType: 'STK',
      exchange: document.getElementById('exch').value || 'SMART',
      currency: 'USD',
      primaryExchange: document.getElementById('pexch').value || null,
      localSymbol: document.getElementById('localsym').value || null
    };

    await apiCall('/api/config/symbols', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify(payload)
    });

    showToast('success', 'Symbol Added', `${symbol} added successfully`);

    document.getElementById('sym').value = '';
    document.getElementById('localsym').value = '';
    document.getElementById('pexch').value = '';
    await loadConfig();
  } catch (error) {
    showToast('error', 'Add Failed', error.message);
  }
}

async function deleteSymbol(symbol) {
  if (!confirm(`Delete symbol ${symbol}?`)) return;

  try {
    await apiCall(`/api/config/symbols/${encodeURIComponent(symbol)}`, {
      method: 'DELETE'
    });

    showToast('success', 'Symbol Deleted', `${symbol} removed successfully`);
    await loadConfig();
  } catch (error) {
    showToast('error', 'Delete Failed', error.message);
  }
}

// Provider health check
async function checkProviderHealth() {
  const grid = document.getElementById('providerHealthGrid');
  grid.innerHTML = '<div style="text-align: center; padding: 20px;"><div class="loading"></div> Checking providers...</div>';

  try {
    const r = await apiCall('/api/backfill/health');
    const health = await r.json();
    renderProviderHealth(health);
    showToast('success', 'Health Check', 'Provider health check complete');
  } catch (error) {
    grid.innerHTML = '<div style="color: #f56565;">Failed to check provider health</div>';
    showToast('error', 'Health Check Failed', error.message);
  }
}

function renderProviderHealth(health) {
  const grid = document.getElementById('providerHealthGrid');
  if (!health || Object.keys(health).length === 0) {
    grid.innerHTML = '<div>No providers available</div>';
    return;
  }

  grid.innerHTML = Object.entries(health).map(([name, status]) => {
    const isAvailable = status.isAvailable;
    const color = isAvailable ? '#48bb78' : '#f56565';
    const icon = isAvailable ? '✓' : '✗';
    const responseTime = status.responseTime ? `${Math.round(status.responseTime * 1000)}ms` : '';

    return `
      <div style="padding: 10px; background: white; border-radius: 6px; border-left: 3px solid ${color};">
        <div style="display: flex; align-items: center; gap: 6px;">
          <span style="color: ${color}; font-weight: bold;">${icon}</span>
          <span style="font-weight: 600; text-transform: capitalize;">${name}</span>
        </div>
        <div style="font-size: 11px; color: #718096; margin-top: 4px;">
          ${status.message || (isAvailable ? 'Available' : 'Unavailable')}
          ${responseTime ? ` (${responseTime})` : ''}
        </div>
      </div>
    `;
  }).join('');
}

// Symbol resolution
function resolveSymbol() {
  document.getElementById('symbolResolveModal').style.display = 'block';
  document.getElementById('symbolToResolve').value = '';
  document.getElementById('symbolResolutionResult').innerHTML = '';
}

function closeSymbolResolveModal() {
  document.getElementById('symbolResolveModal').style.display = 'none';
}

async function doResolveSymbol() {
  const symbol = document.getElementById('symbolToResolve').value.trim();
  if (!symbol) {
    showToast('error', 'Error', 'Please enter a symbol');
    return;
  }

  const resultDiv = document.getElementById('symbolResolutionResult');
  resultDiv.innerHTML = '<div class="loading"></div> Resolving...';

  try {
    const r = await apiCall(`/api/backfill/resolve/${encodeURIComponent(symbol)}`);
    const resolution = await r.json();

    if (!resolution) {
      resultDiv.innerHTML = '<div style="color: #f56565;">Symbol not found</div>';
      return;
    }

    resultDiv.innerHTML = `
      <div style="background: #f7fafc; padding: 16px; border-radius: 8px;">
        <h4 style="margin: 0 0 12px 0;">Resolution for ${symbol}</h4>
        <table style="width: 100%; font-size: 14px;">
          <tr><td style="padding: 4px 8px; font-weight: 500;">Ticker</td><td>${resolution.ticker || 'N/A'}</td></tr>
          <tr><td style="padding: 4px 8px; font-weight: 500;">Name</td><td>${resolution.name || 'N/A'}</td></tr>
          <tr><td style="padding: 4px 8px; font-weight: 500;">FIGI</td><td style="font-family: monospace;">${resolution.figi || 'N/A'}</td></tr>
          <tr><td style="padding: 4px 8px; font-weight: 500;">Exchange</td><td>${resolution.exchange || 'N/A'}</td></tr>
          <tr><td style="padding: 4px 8px; font-weight: 500;">Security Type</td><td>${resolution.securityType || 'N/A'}</td></tr>
        </table>
        ${resolution.providerSymbols ? `
          <h4 style="margin: 16px 0 8px 0;">Provider Mappings</h4>
          <table style="width: 100%; font-size: 13px;">
            ${Object.entries(resolution.providerSymbols).map(([provider, sym]) => `
              <tr><td style="padding: 4px 8px; font-weight: 500; text-transform: capitalize;">${provider}</td><td style="font-family: monospace;">${sym}</td></tr>
            `).join('')}
          </table>
        ` : ''}
      </div>
    `;
  } catch (error) {
    resultDiv.innerHTML = `<div style="color: #f56565;">Error: ${error.message}</div>`;
  }
}

// Enhanced backfill with progress
async function runBackfill() {
  try {
    const provider = document.getElementById('backfillProvider').value || 'composite';
    const symbols = (document.getElementById('backfillSymbols').value || '')
      .split(',')
      .map(s => s.trim())
      .filter(s => s);
    const from = document.getElementById('backfillFrom').value || null;
    const to = document.getElementById('backfillTo').value || null;
    const enableSymbolResolution = document.getElementById('backfillEnableSymbolResolution')?.checked ?? true;
    const preferAdjusted = document.getElementById('backfillPreferAdjusted')?.checked ?? true;

    if (!symbols.length) {
      showToast('error', 'Validation Error', 'Please enter at least one symbol');
      return;
    }

    // Show progress
    document.getElementById('backfillProgress').classList.remove('hidden');
    document.getElementById('progressBar').style.width = '0%';
    document.getElementById('progressPercent').textContent = '0%';
    document.getElementById('progressLabel').textContent = 'Starting backfill...';
    document.getElementById('btnStartBackfill').disabled = true;

    showToast('info', 'Backfill Started', `Downloading data for ${symbols.length} symbol(s)...`);

    const payload = {
      provider,
      symbols,
      from,
      to,
      enableSymbolResolution,
      preferAdjusted
    };

    const r = await apiCall('/api/backfill/run', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const result = await r.json();

    // Complete progress
    document.getElementById('progressBar').style.width = '100%';
    document.getElementById('progressPercent').textContent = '100%';
    document.getElementById('progressLabel').textContent = 'Complete';

    document.getElementById('backfillStatus').innerHTML = formatBackfillStatus(result);

    if (result.success) {
      showToast('success', 'Backfill Complete', `Downloaded ${result.barsWritten} bars from ${result.provider}`);
    } else {
      showToast('error', 'Backfill Failed', result.error || 'Unknown error');
    }
  } catch (error) {
    showToast('error', 'Backfill Failed', error.message);
    document.getElementById('backfillStatus').innerHTML = `<span style="color: #f56565;">${error.message}</span>`;
  } finally {
    document.getElementById('btnStartBackfill').disabled = false;
    setTimeout(() => {
      document.getElementById('backfillProgress').classList.add('hidden');
    }, 3000);
  }
}

// Load provider health on page load
async function loadProviderHealth() {
  try {
    const r = await apiCall('/api/backfill/providers');
    const providers = await r.json();
    const grid = document.getElementById('providerHealthGrid');

    grid.innerHTML = providers.map(p => `
      <div style="padding: 10px; background: white; border-radius: 6px; border-left: 3px solid #a0aec0;">
        <div style="font-weight: 600;">${p.displayName || p.name}</div>
        <div style="font-size: 11px; color: #718096; margin-top: 4px;">${p.description || ''}</div>
      </div>
    `).join('');
  } catch (e) {
    console.log('Could not load providers:', e);
  }
}

// ==========================================
// Multi-Provider Connection Functions
// ==========================================

let connectedProviders = [];
let failoverRules = [];
let symbolMappings = [];

async function loadMultiProviderStatus() {
  try {
    const r = await apiCall('/api/multiprovider/status');
    const status = await r.json();
    connectedProviders = status.providers || [];
    renderActiveConnections(connectedProviders);
    updateProviderSelects();
  } catch (e) {
    console.log('Multi-provider status not available:', e);
  }
}

function renderActiveConnections(providers) {
  const grid = document.getElementById('activeConnectionsGrid');
  if (!providers || providers.length === 0) {
    grid.innerHTML = '<div style="padding: 16px; background: #f7fafc; border-radius: 8px; text-align: center; color: #718096;">No providers connected</div>';
    return;
  }

  grid.innerHTML = providers.map(p => {
    const statusColor = p.isConnected ? '#48bb78' : '#f56565';
    const statusText = p.isConnected ? 'Connected' : 'Disconnected';
    return `
      <div style="padding: 16px; background: white; border-radius: 8px; border-left: 4px solid ${statusColor}; box-shadow: 0 1px 3px rgba(0,0,0,0.1);">
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <strong>${p.providerId}</strong>
          <span class="tag tag-${p.providerType.toLowerCase()}" style="font-size: 10px;">${p.providerType}</span>
        </div>
        <div style="font-size: 12px; color: ${statusColor}; margin-top: 4px;">${statusText}</div>
        <div style="font-size: 11px; color: #718096; margin-top: 4px;">
          Subscriptions: ${p.activeSubscriptions || 0}
        </div>
        <div style="margin-top: 8px;">
          <button class="btn-danger" style="font-size: 11px; padding: 4px 8px;" onclick="disconnectProvider('${p.providerId}')">
            Disconnect
          </button>
        </div>
      </div>
    `;
  }).join('');
}

async function addProviderConnection() {
  try {
    const providerId = document.getElementById('newProviderId').value.trim();
    const providerType = document.getElementById('newProviderType').value;
    const priority = parseInt(document.getElementById('newProviderPriority').value || '1');

    if (!providerId) {
      showToast('error', 'Validation Error', 'Provider ID is required');
      return;
    }

    const payload = {
      id: providerId,
      name: providerId,
      provider: providerType,
      priority: priority,
      enabled: true
    };

    if (providerType === 'Alpaca') {
      payload.alpaca = {
        keyId: document.getElementById('newProviderAlpacaKey').value,
        secretKey: document.getElementById('newProviderAlpacaSecret').value,
        feed: 'iex',
        useSandbox: false
      };
    }

    await apiCall('/api/multiprovider/connect', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    showToast('success', 'Provider Connected', `Successfully connected to ${providerId}`);
    await loadMultiProviderStatus();

    // Clear form
    document.getElementById('newProviderId').value = '';
    document.getElementById('newProviderAlpacaKey').value = '';
    document.getElementById('newProviderAlpacaSecret').value = '';
  } catch (error) {
    showToast('error', 'Connection Failed', error.message);
  }
}

async function disconnectProvider(providerId) {
  if (!confirm(`Disconnect provider ${providerId}?`)) return;

  try {
    await apiCall(`/api/multiprovider/disconnect/${encodeURIComponent(providerId)}`, {
      method: 'POST'
    });
    showToast('success', 'Provider Disconnected', `Disconnected from ${providerId}`);
    await loadMultiProviderStatus();
  } catch (error) {
    showToast('error', 'Disconnect Failed', error.message);
  }
}

// ==========================================
// Provider Comparison Functions
// ==========================================

async function refreshComparison() {
  try {
    const r = await apiCall('/api/multiprovider/comparison');
    const comparison = await r.json();
    renderComparison(comparison);
    showToast('success', 'Metrics Refreshed', 'Provider comparison updated');
  } catch (error) {
    showToast('error', 'Refresh Failed', error.message);
  }
}

function renderComparison(comparison) {
  const headerRow = document.getElementById('comparisonHeaders').parentElement;
  const tbody = document.getElementById('comparisonBody');

  if (!comparison.providers || comparison.providers.length === 0) {
    headerRow.innerHTML = '<th>Metric</th><th>No providers connected</th>';
    tbody.innerHTML = '<tr><td colspan="2" style="text-align: center; color: #718096; padding: 24px;">Connect providers to see comparison metrics</td></tr>';
    return;
  }

  // Build header row
  headerRow.innerHTML = '<th>Metric</th>' + comparison.providers.map(p =>
    `<th style="text-align: center;">${p.providerId}<br/><small style="font-weight: normal; color: #718096;">${p.providerType}</small></th>`
  ).join('');

  // Metrics to compare
  const metrics = [
    { key: 'dataQualityScore', label: 'Data Quality Score', format: v => `<span style="color: ${v >= 80 ? '#48bb78' : v >= 60 ? '#ecc94b' : '#f56565'}; font-weight: 600;">${v.toFixed(1)}%</span>` },
    { key: 'tradesReceived', label: 'Trades Received', format: v => v.toLocaleString() },
    { key: 'depthUpdatesReceived', label: 'Depth Updates', format: v => v.toLocaleString() },
    { key: 'quotesReceived', label: 'Quotes Received', format: v => v.toLocaleString() },
    { key: 'averageLatencyMs', label: 'Avg Latency', format: v => `${v.toFixed(2)}ms` },
    { key: 'minLatencyMs', label: 'Min Latency', format: v => `${v.toFixed(2)}ms` },
    { key: 'maxLatencyMs', label: 'Max Latency', format: v => `${v.toFixed(2)}ms` },
    { key: 'connectionSuccessRate', label: 'Connection Success', format: v => `${v.toFixed(1)}%` },
    { key: 'messagesDropped', label: 'Messages Dropped', format: v => `<span style="color: ${v > 0 ? '#f56565' : '#48bb78'}">${v.toLocaleString()}</span>` },
    { key: 'activeSubscriptions', label: 'Active Subscriptions', format: v => v.toString() }
  ];

  tbody.innerHTML = metrics.map(m => {
    const cells = comparison.providers.map(p => {
      const value = p[m.key] ?? 0;
      return `<td style="text-align: center;">${m.format(value)}</td>`;
    }).join('');
    return `<tr><td><strong>${m.label}</strong></td>${cells}</tr>`;
  }).join('');
}

async function exportComparison() {
  try {
    const r = await apiCall('/api/multiprovider/comparison');
    const comparison = await r.json();

    const headers = ['Metric', ...comparison.providers.map(p => p.providerId)];
    const rows = [
      ['Data Quality Score', ...comparison.providers.map(p => p.dataQualityScore?.toFixed(1) || '0')],
      ['Trades Received', ...comparison.providers.map(p => p.tradesReceived || 0)],
      ['Depth Updates', ...comparison.providers.map(p => p.depthUpdatesReceived || 0)],
      ['Avg Latency (ms)', ...comparison.providers.map(p => p.averageLatencyMs?.toFixed(2) || '0')],
      ['Messages Dropped', ...comparison.providers.map(p => p.messagesDropped || 0)]
    ];

    const csv = [headers.join(','), ...rows.map(r => r.join(','))].join('\n');
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `provider-comparison-${new Date().toISOString().split('T')[0]}.csv`;
    a.click();
    URL.revokeObjectURL(url);

    showToast('success', 'Export Complete', 'Comparison report exported');
  } catch (error) {
    showToast('error', 'Export Failed', error.message);
  }
}

// ==========================================
// Failover Rule Functions
// ==========================================

function updateProviderSelects() {
  const primarySelect = document.getElementById('failoverPrimaryProvider');
  primarySelect.innerHTML = '<option value="">Select primary...</option>' +
    connectedProviders.map(p => `<option value="${p.providerId}">${p.providerId}</option>`).join('');
}

async function loadFailoverRules() {
  try {
    const r = await apiCall('/api/multiprovider/failover/rules');
    failoverRules = await r.json();
    renderFailoverRules(failoverRules);
  } catch (e) {
    console.log('Failover rules not available:', e);
  }
}

function renderFailoverRules(rules) {
  const container = document.getElementById('failoverRulesList');
  if (!rules || rules.length === 0) {
    container.innerHTML = '<div style="padding: 16px; background: #f7fafc; border-radius: 8px; text-align: center; color: #718096;">No failover rules configured</div>';
    return;
  }

  container.innerHTML = rules.map(rule => {
    const statusColor = rule.isInFailoverState ? '#ecc94b' : '#48bb78';
    const statusText = rule.isInFailoverState ? 'In Failover' : 'Normal';
    return `
      <div style="padding: 16px; background: white; border-radius: 8px; border-left: 4px solid ${statusColor}; margin-bottom: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.1);">
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <strong>${rule.id}</strong>
          <span style="color: ${statusColor}; font-size: 12px; font-weight: 600;">${statusText}</span>
        </div>
        <div style="font-size: 13px; color: #4a5568; margin-top: 8px;">
          <div>Primary: <strong>${rule.primaryProviderId}</strong></div>
          <div>Backups: ${rule.backupProviderIds.join(', ') || 'None'}</div>
          <div style="margin-top: 4px; color: #718096; font-size: 12px;">
            Threshold: ${rule.failoverThreshold} failures | Recovery: ${rule.recoveryThreshold} successes
          </div>
        </div>
        <div style="margin-top: 8px; display: flex; gap: 8px;">
          <button class="btn-secondary" style="font-size: 11px; padding: 4px 8px;" onclick="testFailover('${rule.id}')">
            Test Failover
          </button>
          <button class="btn-danger" style="font-size: 11px; padding: 4px 8px;" onclick="removeFailoverRule('${rule.id}')">
            Remove
          </button>
        </div>
      </div>
    `;
  }).join('');
}

async function addFailoverRule() {
  try {
    const ruleId = document.getElementById('failoverRuleId').value.trim();
    const primaryProvider = document.getElementById('failoverPrimaryProvider').value;
    const backupProviders = document.getElementById('failoverBackupProviders').value.split(',').map(s => s.trim()).filter(s => s);
    const threshold = parseInt(document.getElementById('failoverThreshold').value || '3');
    const qualityThreshold = parseInt(document.getElementById('failoverQualityThreshold').value || '0');
    const maxLatency = parseInt(document.getElementById('failoverMaxLatency').value || '0');
    const autoRecover = document.getElementById('failoverAutoRecover').checked;

    if (!ruleId || !primaryProvider) {
      showToast('error', 'Validation Error', 'Rule ID and Primary Provider are required');
      return;
    }

    const payload = {
      id: ruleId,
      primaryProviderId: primaryProvider,
      backupProviderIds: backupProviders,
      failoverThreshold: threshold,
      recoveryThreshold: 5,
      dataQualityThreshold: qualityThreshold,
      maxLatencyMs: maxLatency,
      autoRecover: autoRecover
    };

    await apiCall('/api/multiprovider/failover/rules', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    showToast('success', 'Rule Added', `Failover rule ${ruleId} created`);
    await loadFailoverRules();

    // Clear form
    document.getElementById('failoverRuleId').value = '';
    document.getElementById('failoverBackupProviders').value = '';
  } catch (error) {
    showToast('error', 'Add Rule Failed', error.message);
  }
}

async function removeFailoverRule(ruleId) {
  if (!confirm(`Remove failover rule ${ruleId}?`)) return;

  try {
    await apiCall(`/api/multiprovider/failover/rules/${encodeURIComponent(ruleId)}`, {
      method: 'DELETE'
    });
    showToast('success', 'Rule Removed', `Failover rule ${ruleId} removed`);
    await loadFailoverRules();
  } catch (error) {
    showToast('error', 'Remove Failed', error.message);
  }
}

async function testFailover(ruleId) {
  try {
    await apiCall(`/api/multiprovider/failover/test/${encodeURIComponent(ruleId)}`, {
      method: 'POST'
    });
    showToast('info', 'Failover Test', `Testing failover for rule ${ruleId}`);
    await loadFailoverRules();
  } catch (error) {
    showToast('error', 'Test Failed', error.message);
  }
}

// ==========================================
// Symbol Mapping Functions
// ==========================================

async function loadSymbolMappings() {
  try {
    const r = await apiCall('/api/multiprovider/symbolmappings');
    symbolMappings = await r.json();
    renderSymbolMappings(symbolMappings);
  } catch (e) {
    console.log('Symbol mappings not available:', e);
  }
}

function renderSymbolMappings(mappings) {
  const tbody = document.getElementById('symbolMappingBody');
  if (!mappings || mappings.length === 0) {
    tbody.innerHTML = '<tr><td colspan="6" style="text-align: center; color: #718096; padding: 16px;">No symbol mappings configured</td></tr>';
    return;
  }

  tbody.innerHTML = mappings.map(m => `
    <tr>
      <td><strong>${m.canonicalSymbol}</strong></td>
      <td>${m.ibSymbol || '-'}</td>
      <td>${m.alpacaSymbol || '-'}</td>
      <td>${m.polygonSymbol || '-'}</td>
      <td style="font-family: monospace; font-size: 11px;">${m.figi || '-'}</td>
      <td>
        <button class="btn-danger" style="font-size: 11px; padding: 4px 8px;" onclick="removeSymbolMapping('${m.canonicalSymbol}')">
          Remove
        </button>
      </td>
    </tr>
  `).join('');
}

async function addSymbolMapping() {
  try {
    const canonical = document.getElementById('mappingCanonical').value.trim().toUpperCase();
    if (!canonical) {
      showToast('error', 'Validation Error', 'Canonical symbol is required');
      return;
    }

    const payload = {
      canonicalSymbol: canonical,
      ibSymbol: document.getElementById('mappingIB').value.trim() || null,
      alpacaSymbol: document.getElementById('mappingAlpaca').value.trim() || null,
      polygonSymbol: document.getElementById('mappingPolygon').value.trim() || null,
      figi: document.getElementById('mappingFigi').value.trim() || null,
      name: document.getElementById('mappingName').value.trim() || null
    };

    await apiCall('/api/multiprovider/symbolmappings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    showToast('success', 'Mapping Added', `Symbol mapping for ${canonical} created`);
    await loadSymbolMappings();

    // Clear form
    ['mappingCanonical', 'mappingIB', 'mappingAlpaca', 'mappingPolygon', 'mappingFigi', 'mappingName'].forEach(id => {
      document.getElementById(id).value = '';
    });
  } catch (error) {
    showToast('error', 'Add Mapping Failed', error.message);
  }
}

async function removeSymbolMapping(symbol) {
  if (!confirm(`Remove symbol mapping for ${symbol}?`)) return;

  try {
    await apiCall(`/api/multiprovider/symbolmappings/${encodeURIComponent(symbol)}`, {
      method: 'DELETE'
    });
    showToast('success', 'Mapping Removed', `Symbol mapping for ${symbol} removed`);
    await loadSymbolMappings();
  } catch (error) {
    showToast('error', 'Remove Failed', error.message);
  }
}

async function importSymbolMappings() {
  const input = document.createElement('input');
  input.type = 'file';
  input.accept = '.csv';
  input.onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;

    const formData = new FormData();
    formData.append('file', file);

    try {
      await apiCall('/api/multiprovider/symbolmappings/import', {
        method: 'POST',
        body: formData
      });
      showToast('success', 'Import Complete', 'Symbol mappings imported');
      await loadSymbolMappings();
    } catch (error) {
      showToast('error', 'Import Failed', error.message);
    }
  };
  input.click();
}

async function exportSymbolMappings() {
  try {
    const r = await apiCall('/api/multiprovider/symbolmappings/export');
    const blob = await r.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `symbol-mappings-${new Date().toISOString().split('T')[0]}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    showToast('success', 'Export Complete', 'Symbol mappings exported');
  } catch (error) {
    showToast('error', 'Export Failed', error.message);
  }
}

async function autoDetectMappings() {
  try {
    showToast('info', 'Auto-Detection', 'Detecting symbol mappings from subscribed symbols...');
    await apiCall('/api/multiprovider/symbolmappings/autodetect', {
      method: 'POST'
    });
    showToast('success', 'Detection Complete', 'Symbol mappings auto-detected');
    await loadSymbolMappings();
  } catch (error) {
    showToast('error', 'Detection Failed', error.message);
  }
}

// Show/hide provider-specific fields
document.getElementById('newProviderType').addEventListener('change', function() {
  const alpacaFields = document.getElementById('newProviderAlpacaFields');
  alpacaFields.style.display = this.value === 'Alpaca' ? 'block' : 'none';
});

// Initial load
loadConfig();
loadStatus();
loadBackfillStatus();
loadProviderHealth();
loadMultiProviderStatus();
loadFailoverRules();
loadSymbolMappings();
setInterval(loadStatus, 2000);
setInterval(loadBackfillStatus, 5000);
setInterval(loadMultiProviderStatus, 5000);
