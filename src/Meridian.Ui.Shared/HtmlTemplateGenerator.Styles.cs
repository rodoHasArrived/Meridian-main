namespace Meridian.Ui.Shared;

public static partial class HtmlTemplateGenerator
{
    /// <summary>
    /// Returns the dashboard CSS styles.
    /// </summary>
    private static string GetStyles() => $@"
    :root {{
      --bg-primary: #0d1117;
      --bg-secondary: #161b22;
      --bg-tertiary: #21262d;
      --bg-hover: #30363d;
      --border-default: #30363d;
      --border-muted: #21262d;
      --text-primary: #e6edf3;
      --text-secondary: #8b949e;
      --text-muted: #6e7681;
      --accent-green: #3fb950;
      --accent-green-dim: #238636;
      --accent-blue: #58a6ff;
      --accent-purple: #a371f7;
      --accent-red: #f85149;
      --accent-orange: #d29922;
      --accent-cyan: #39c5cf;
      --glow-green: 0 0 20px rgba(63, 185, 80, 0.3);
      --glow-blue: 0 0 20px rgba(88, 166, 255, 0.3);
      --glow-red: 0 0 20px rgba(248, 81, 73, 0.3);
      --font-mono: 'JetBrains Mono', 'Fira Code', 'SF Mono', Consolas, monospace;
      --font-sans: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    }}

    * {{ box-sizing: border-box; }}

    body {{
      font-family: var(--font-sans);
      margin: 0;
      padding: 0;
      background: var(--bg-primary);
      color: var(--text-primary);
      min-height: 100vh;
    }}

    /* Scanline effect overlay */
    body::before {{
      content: '';
      position: fixed;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      background: repeating-linear-gradient(
        0deg,
        transparent,
        transparent 2px,
        rgba(0, 0, 0, 0.03) 2px,
        rgba(0, 0, 0, 0.03) 4px
      );
      pointer-events: none;
      z-index: 1000;
    }}

    /* Top Navigation Bar */
    .top-bar {{
      background: var(--bg-secondary);
      border-bottom: 1px solid var(--border-default);
      padding: 12px 24px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      position: sticky;
      top: 0;
      z-index: 100;
      backdrop-filter: blur(10px);
    }}

    /* Data Freshness Bar */
    .freshness-bar {{
      background: var(--bg-tertiary);
      border-bottom: 1px solid var(--border-default);
      padding: 6px 24px;
      display: flex;
      align-items: center;
      gap: 24px;
      font-family: var(--font-mono);
      font-size: 12px;
      color: var(--text-secondary);
    }}

    .freshness-label {{
      color: var(--text-muted);
      margin-right: 6px;
    }}

    .freshness-providers {{
      display: flex;
      align-items: center;
      gap: 8px;
    }}

    .freshness-dot {{
      width: 8px;
      height: 8px;
      border-radius: 50%;
      display: inline-block;
    }}

    .freshness-dot.green {{ background: var(--accent-green); box-shadow: 0 0 6px rgba(63,185,80,0.5); }}
    .freshness-dot.yellow {{ background: var(--accent-orange); box-shadow: 0 0 6px rgba(210,153,34,0.5); }}
    .freshness-dot.red {{ background: var(--accent-red); box-shadow: 0 0 6px rgba(248,81,73,0.5); }}
    .freshness-dot.gray {{ background: var(--text-muted); }}

    .freshness-value {{
      color: var(--text-primary);
    }}

    .freshness-value.stale {{ color: var(--accent-orange); }}
    .freshness-value.dead {{ color: var(--accent-red); }}

    .freshness-mode-badge {{
      background: var(--accent-orange);
      color: var(--bg-primary);
      padding: 2px 10px;
      border-radius: 4px;
      font-weight: 700;
      font-size: 11px;
      letter-spacing: 0.5px;
      margin-left: auto;
    }}

    .freshness-loading {{
      color: var(--text-muted);
      font-style: italic;
    }}

    .logo {{
      display: flex;
      align-items: center;
      gap: 12px;
    }}

    .logo-icon {{
      width: 32px;
      height: 32px;
      background: linear-gradient(135deg, var(--accent-green) 0%, var(--accent-cyan) 100%);
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-family: var(--font-mono);
      font-weight: 700;
      font-size: 14px;
      color: var(--bg-primary);
    }}

    .logo-text {{
      font-family: var(--font-mono);
      font-size: 18px;
      font-weight: 600;
      background: linear-gradient(90deg, var(--accent-green), var(--accent-cyan));
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }}

    .logo-version {{
      font-size: 11px;
      color: var(--text-muted);
      background: var(--bg-tertiary);
      padding: 2px 8px;
      border-radius: 4px;
      font-family: var(--font-mono);
    }}

    /* Command Palette */
    .cmd-palette {{
      display: flex;
      align-items: center;
      background: var(--bg-tertiary);
      border: 1px solid var(--border-default);
      border-radius: 8px;
      padding: 8px 16px;
      min-width: 400px;
      cursor: pointer;
      transition: all 0.2s ease;
    }}

    .cmd-palette:hover {{
      border-color: var(--accent-blue);
      box-shadow: var(--glow-blue);
    }}

    .cmd-palette-icon {{
      color: var(--text-muted);
      margin-right: 12px;
    }}

    .cmd-palette-text {{
      color: var(--text-muted);
      flex: 1;
      font-size: 14px;
    }}

    .cmd-palette-shortcut {{
      display: flex;
      gap: 4px;
    }}

    .kbd {{
      background: var(--bg-secondary);
      border: 1px solid var(--border-default);
      border-radius: 4px;
      padding: 2px 6px;
      font-family: var(--font-mono);
      font-size: 11px;
      color: var(--text-secondary);
    }}

    /* Status indicators in top bar */
    .top-status {{
      display: flex;
      align-items: center;
      gap: 16px;
    }}

    .status-indicator {{
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 6px 12px;
      background: var(--bg-tertiary);
      border-radius: 6px;
      font-size: 13px;
    }}

    .status-dot {{
      width: 8px;
      height: 8px;
      border-radius: 50%;
      animation: pulse 2s infinite;
    }}

    .status-dot.connected {{ background: var(--accent-green); box-shadow: 0 0 10px var(--accent-green); }}
    .status-dot.disconnected {{ background: var(--accent-red); box-shadow: 0 0 10px var(--accent-red); }}
    .status-dot.warning {{ background: var(--accent-orange); box-shadow: 0 0 10px var(--accent-orange); }}

    @keyframes pulse {{
      0%, 100% {{ opacity: 1; }}
      50% {{ opacity: 0.5; }}
    }}

    /* Main container */
    .main-container {{
      display: flex;
      min-height: calc(100vh - 60px);
    }}

    /* Sidebar */
    .sidebar {{
      width: 240px;
      background: var(--bg-secondary);
      border-right: 1px solid var(--border-default);
      padding: 16px 0;
      display: flex;
      flex-direction: column;
    }}

    .nav-section {{
      padding: 0 12px;
      margin-bottom: 24px;
    }}

    .nav-section-title {{
      font-size: 11px;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.5px;
      padding: 0 12px;
      margin-bottom: 8px;
    }}

    .nav-item {{
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      border-radius: 6px;
      color: var(--text-secondary);
      cursor: pointer;
      transition: all 0.15s ease;
      font-size: 14px;
    }}

    .nav-item:hover {{
      background: var(--bg-tertiary);
      color: var(--text-primary);
    }}

    .nav-item.active {{
      background: var(--bg-tertiary);
      color: var(--accent-green);
      border-left: 2px solid var(--accent-green);
      margin-left: -2px;
    }}

    .nav-item-icon {{
      width: 18px;
      text-align: center;
    }}

    .nav-item-badge {{
      margin-left: auto;
      background: var(--accent-green-dim);
      color: var(--accent-green);
      padding: 2px 8px;
      border-radius: 10px;
      font-size: 11px;
      font-weight: 600;
    }}

    /* Main content */
    .content {{
      flex: 1;
      padding: 24px;
      overflow-y: auto;
    }}

    /* Section headers */
    .section-header {{
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 16px;
    }}

    .section-title {{
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary);
      display: flex;
      align-items: center;
      gap: 10px;
    }}

    .section-title-icon {{
      color: var(--accent-green);
    }}

    /* Card styles - Console inspired */
    .row {{ display: flex; gap: 20px; flex-wrap: wrap; margin-bottom: 24px; }}

    .card {{
      background: var(--bg-secondary);
      border: 1px solid var(--border-default);
      border-radius: 12px;
      padding: 20px;
      min-width: 320px;
      position: relative;
      overflow: hidden;
      transition: all 0.2s ease;
    }}

    .card:hover {{
      border-color: var(--accent-blue);
      box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
    }}

    .card::before {{
      content: '';
      position: absolute;
      top: 0;
      left: 0;
      right: 0;
      height: 2px;
      background: linear-gradient(90deg, var(--accent-green), var(--accent-cyan));
      opacity: 0;
      transition: opacity 0.2s ease;
    }}

    .card:hover::before {{
      opacity: 1;
    }}

    .card-header {{
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 16px;
    }}

    .card-title {{
      font-size: 14px;
      font-weight: 600;
      color: var(--text-primary);
      display: flex;
      align-items: center;
      gap: 8px;
    }}

    .card-title-icon {{
      color: var(--accent-green);
    }}

    h2, h3, h4 {{
      color: var(--text-primary);
      margin: 0 0 16px 0;
      font-weight: 600;
    }}

    h3 {{
      font-size: 16px;
      display: flex;
      align-items: center;
      gap: 10px;
    }}

    h3::before {{
      content: '>';
      color: var(--accent-green);
      font-family: var(--font-mono);
    }}

    h4 {{ font-size: 14px; color: var(--text-secondary); margin-top: 20px; }}

    /* Metric cards */
    .metrics-grid {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 16px;
      margin-bottom: 24px;
    }}

    .metric-card {{
      background: var(--bg-secondary);
      border: 1px solid var(--border-default);
      border-radius: 12px;
      padding: 20px;
      position: relative;
      overflow: hidden;
    }}

    .metric-card.success {{ border-left: 3px solid var(--accent-green); }}
    .metric-card.danger {{ border-left: 3px solid var(--accent-red); }}
    .metric-card.warning {{ border-left: 3px solid var(--accent-orange); }}
    .metric-card.info {{ border-left: 3px solid var(--accent-blue); }}

    .metric-value {{
      font-family: var(--font-mono);
      font-size: 32px;
      font-weight: 700;
      line-height: 1;
      margin-bottom: 8px;
    }}

    .metric-value.success {{ color: var(--accent-green); }}
    .metric-value.danger {{ color: var(--accent-red); }}
    .metric-value.warning {{ color: var(--accent-orange); }}
    .metric-value.info {{ color: var(--accent-blue); }}

    .metric-label {{
      font-size: 12px;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }}

    .metric-trend {{
      position: absolute;
      top: 16px;
      right: 16px;
      font-size: 12px;
      display: flex;
      align-items: center;
      gap: 4px;
    }}

    .metric-trend.up {{ color: var(--accent-green); }}
    .metric-trend.down {{ color: var(--accent-red); }}

    /* Terminal-style log display */
    .terminal {{
      background: var(--bg-primary);
      border: 1px solid var(--border-default);
      border-radius: 8px;
      font-family: var(--font-mono);
      font-size: 13px;
      overflow: hidden;
    }}

    .terminal-header {{
      background: var(--bg-tertiary);
      padding: 10px 16px;
      display: flex;
      align-items: center;
      gap: 8px;
      border-bottom: 1px solid var(--border-default);
    }}

    .terminal-dot {{
      width: 12px;
      height: 12px;
      border-radius: 50%;
    }}

    .terminal-dot.red {{ background: #ff5f56; }}
    .terminal-dot.yellow {{ background: #ffbd2e; }}
    .terminal-dot.green {{ background: #27c93f; }}

    .terminal-title {{
      color: var(--text-muted);
      font-size: 12px;
      margin-left: 8px;
    }}

    .terminal-body {{
      padding: 16px;
      max-height: 200px;
      overflow-y: auto;
    }}

    .terminal-line {{
      display: flex;
      gap: 12px;
      padding: 2px 0;
    }}

    .terminal-prompt {{
      color: var(--accent-green);
    }}

    .terminal-time {{
      color: var(--text-muted);
      min-width: 80px;
    }}

    .terminal-msg {{
      color: var(--text-secondary);
    }}

    .terminal-msg.success {{ color: var(--accent-green); }}
    .terminal-msg.error {{ color: var(--accent-red); }}
    .terminal-msg.warning {{ color: var(--accent-orange); }}

    /* Tables */
    table {{
      border-collapse: collapse;
      width: 100%;
      font-size: 13px;
    }}

    th, td {{
      padding: 12px 16px;
      text-align: left;
      border-bottom: 1px solid var(--border-muted);
    }}

    th {{
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      font-size: 11px;
      letter-spacing: 0.5px;
      background: var(--bg-tertiary);
    }}

    tr:hover td {{
      background: var(--bg-tertiary);
    }}

    /* Form elements */
    .form-group {{
      margin-bottom: 16px;
    }}

    .form-group label {{
      display: block;
      margin-bottom: 6px;
      font-size: 13px;
      font-weight: 500;
      color: var(--text-secondary);
    }}

    .form-row {{
      display: flex;
      gap: 16px;
    }}

    .form-row > div {{
      flex: 1;
    }}

    input, select, textarea {{
      width: 100%;
      padding: 10px 14px;
      font-size: 14px;
      font-family: var(--font-mono);
      background: var(--bg-primary);
      border: 1px solid var(--border-default);
      border-radius: 8px;
      color: var(--text-primary);
      transition: all 0.15s ease;
    }}

    input:focus, select:focus, textarea:focus {{
      outline: none;
      border-color: var(--accent-blue);
      box-shadow: 0 0 0 3px rgba(88, 166, 255, 0.15);
    }}

    input::placeholder {{
      color: var(--text-muted);
    }}

    input[type=""checkbox""] {{
      width: auto;
      margin-right: 8px;
      accent-color: var(--accent-green);
    }}

    select {{
      cursor: pointer;
      appearance: none;
      background-image: url(""data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' fill='%238b949e' viewBox='0 0 16 16'%3E%3Cpath d='M8 11L3 6h10l-5 5z'/%3E%3C/svg%3E"");
      background-repeat: no-repeat;
      background-position: right 12px center;
      padding-right: 36px;
    }}

    /* Buttons */
    button {{
      padding: 10px 20px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      border-radius: 8px;
      border: 1px solid transparent;
      transition: all 0.15s ease;
      display: inline-flex;
      align-items: center;
      gap: 8px;
    }}

    .btn-primary {{
      background: linear-gradient(135deg, var(--accent-green-dim), var(--accent-green));
      color: white;
      border: none;
    }}

    .btn-primary:hover {{
      box-shadow: var(--glow-green);
      transform: translateY(-1px);
    }}

    .btn-secondary {{
      background: var(--bg-tertiary);
      color: var(--text-primary);
      border: 1px solid var(--border-default);
    }}

    .btn-secondary:hover {{
      background: var(--bg-hover);
      border-color: var(--accent-blue);
    }}

    .btn-danger {{
      background: rgba(248, 81, 73, 0.15);
      color: var(--accent-red);
      border: 1px solid var(--accent-red);
      padding: 6px 12px;
      font-size: 12px;
    }}

    .btn-danger:hover {{
      background: var(--accent-red);
      color: white;
      box-shadow: var(--glow-red);
    }}

    .btn-icon {{
      background: transparent;
      border: none;
      color: var(--text-muted);
      padding: 8px;
      border-radius: 6px;
    }}

    .btn-icon:hover {{
      background: var(--bg-tertiary);
      color: var(--text-primary);
    }}

    /* Tags and badges */
    .tag {{
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
      font-family: var(--font-mono);
    }}

    .tag-ib {{
      background: rgba(88, 166, 255, 0.15);
      color: var(--accent-blue);
      border: 1px solid rgba(88, 166, 255, 0.3);
    }}

    .tag-alpaca {{
      background: rgba(210, 153, 34, 0.15);
      color: var(--accent-orange);
      border: 1px solid rgba(210, 153, 34, 0.3);
    }}

    .tag-polygon {{
      background: rgba(163, 113, 247, 0.15);
      color: var(--accent-purple);
      border: 1px solid rgba(163, 113, 247, 0.3);
    }}

    .provider-badge {{
      font-size: 10px;
      padding: 2px 6px;
      border-radius: 4px;
      font-family: var(--font-mono);
      margin-left: 6px;
    }}

    .ib-only {{
      background: rgba(88, 166, 255, 0.15);
      color: var(--accent-blue);
    }}

    .alpaca-only {{
      background: rgba(210, 153, 34, 0.15);
      color: var(--accent-orange);
    }}

    /* Status indicators */
    .muted {{ color: var(--text-muted); font-size: 13px; }}
    .good {{ color: var(--accent-green); font-weight: 600; }}
    .bad {{ color: var(--accent-red); font-weight: 600; }}

    /* Code blocks */
    code {{
      font-family: var(--font-mono);
      background: var(--bg-tertiary);
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 12px;
      color: var(--accent-cyan);
    }}

    /* Sections */
    .provider-section {{
      border-top: 1px solid var(--border-muted);
      margin-top: 20px;
      padding-top: 20px;
    }}

    .hidden {{ display: none !important; }}

    /* Collapsible sections */
    .collapsible-header {{
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px 16px;
      background: var(--bg-tertiary);
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.15s ease;
    }}

    .collapsible-header:hover {{
      background: var(--bg-hover);
    }}

    .collapsible-icon {{
      transition: transform 0.2s ease;
    }}

    .collapsible-icon.open {{
      transform: rotate(180deg);
    }}

    /* Toast notifications */
    .toast-container {{
      position: fixed;
      bottom: 24px;
      right: 24px;
      z-index: 1001;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }}

    .toast {{
      background: var(--bg-secondary);
      border: 1px solid var(--border-default);
      border-radius: 8px;
      padding: 12px 20px;
      display: flex;
      align-items: center;
      gap: 12px;
      box-shadow: 0 8px 30px rgba(0, 0, 0, 0.4);
      animation: slideIn 0.3s ease;
      min-width: 300px;
    }}

    .toast.success {{ border-left: 3px solid var(--accent-green); }}
    .toast.error {{ border-left: 3px solid var(--accent-red); }}
    .toast.warning {{ border-left: 3px solid var(--accent-orange); }}
    .toast.info {{ border-left: 3px solid var(--accent-blue); }}

    @keyframes slideIn {{
      from {{ transform: translateX(100%); opacity: 0; }}
      to {{ transform: translateX(0); opacity: 1; }}
    }}

    /* Loading spinner */
    .spinner {{
      width: 20px;
      height: 20px;
      border: 2px solid var(--border-default);
      border-top-color: var(--accent-green);
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }}

    @keyframes spin {{
      to {{ transform: rotate(360deg); }}
    }}

    /* Progress bar */
    .progress-bar {{
      height: 4px;
      background: var(--bg-tertiary);
      border-radius: 2px;
      overflow: hidden;
    }}

    .progress-bar-fill {{
      height: 100%;
      background: linear-gradient(90deg, var(--accent-green), var(--accent-cyan));
      border-radius: 2px;
      transition: width 0.3s ease;
    }}

    /* Path display */
    .path-display {{
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      background: var(--bg-primary);
      border: 1px solid var(--border-muted);
      border-radius: 8px;
      margin-bottom: 20px;
    }}

    .path-label {{
      color: var(--text-muted);
      font-size: 12px;
      min-width: 60px;
    }}

    .path-value {{
      font-family: var(--font-mono);
      font-size: 12px;
      color: var(--accent-cyan);
    }}

    /* Responsive */
    @media (max-width: 1024px) {{
      .sidebar {{ display: none; }}
      .cmd-palette {{ min-width: 200px; }}
    }}

    @media (max-width: 768px) {{
      .top-bar {{ padding: 12px 16px; }}
      .content {{ padding: 16px; }}
      .form-row {{ flex-direction: column; }}
      .cmd-palette {{ display: none; }}
    }}

    /* Keyboard shortcuts tooltip */
    .shortcut-hint {{
      position: absolute;
      bottom: 12px;
      right: 12px;
      display: flex;
      gap: 4px;
      opacity: 0;
      transition: opacity 0.2s ease;
    }}

    .card:hover .shortcut-hint {{
      opacity: 1;
    }}

    /* Live indicator */
    .live-indicator {{
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: var(--accent-green);
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }}

    .live-indicator::before {{
      content: '';
      width: 6px;
      height: 6px;
      background: var(--accent-green);
      border-radius: 50%;
      animation: pulse 1.5s infinite;
    }}
";
}
