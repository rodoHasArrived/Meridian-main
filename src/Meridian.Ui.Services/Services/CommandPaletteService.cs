using System.Collections.Concurrent;
using System.Windows.Input;

namespace Meridian.Ui.Services;

/// <summary>
/// Service providing a command palette (Ctrl+K) for quick navigation and action execution.
/// Supports fuzzy search across all registered pages, actions, and settings.
/// Also supports contextual commands registered per ViewModel.
/// </summary>
public sealed class CommandPaletteService
{
    private static readonly Lazy<CommandPaletteService> _instance = new(() => new CommandPaletteService());
    private readonly List<PaletteCommand> _commands = new();
    private readonly List<string> _recentCommands = new();
    private readonly ConcurrentDictionary<string, Func<IReadOnlyList<CommandEntry>>> _contextualProviders = new();
    private string? _activeContextKey;
    private const int MaxRecentCommands = 10;

    public static CommandPaletteService Instance => _instance.Value;

    private CommandPaletteService()
    {
        RegisterDefaultCommands();
    }

    /// <summary>
    /// Event raised when a command is executed through the palette.
    /// </summary>
    public event EventHandler<PaletteCommandEventArgs>? CommandExecuted;

    /// <summary>
    /// Event raised when available commands change (context switch or provider registration).
    /// </summary>
    public event EventHandler? CommandsChanged;

    /// <summary>
    /// Searches commands using fuzzy matching.
    /// </summary>
    public IReadOnlyList<PaletteCommand> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetRecentAndPopularCommands();
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();

        var scored = new List<(PaletteCommand Command, int Score)>();

        foreach (var command in _commands)
        {
            var score = CalculateMatchScore(command, normalizedQuery);
            if (score > 0)
            {
                scored.Add((command, score));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Command.Title)
            .Take(15)
            .Select(s => s.Command)
            .ToList();
    }

    /// <summary>
    /// Executes a command by its ID.
    /// </summary>
    public void Execute(string commandId)
    {
        var command = _commands.FirstOrDefault(c => c.Id == commandId);
        if (command == null)
            return;

        // Track recent usage
        _recentCommands.Remove(commandId);
        _recentCommands.Insert(0, commandId);
        while (_recentCommands.Count > MaxRecentCommands)
        {
            _recentCommands.RemoveAt(_recentCommands.Count - 1);
        }

        CommandExecuted?.Invoke(this, new PaletteCommandEventArgs
        {
            CommandId = commandId,
            ActionId = command.ActionId,
            Category = command.Category
        });
    }

    /// <summary>
    /// Registers a custom command in the palette.
    /// </summary>
    public void RegisterCommand(PaletteCommand command)
    {
        var existing = _commands.FindIndex(c => c.Id == command.Id);
        if (existing >= 0)
        {
            _commands[existing] = command;
        }
        else
        {
            _commands.Add(command);
        }
    }

    /// <summary>
    /// Gets all registered commands.
    /// </summary>
    public IReadOnlyList<PaletteCommand> GetAllCommands() => _commands.AsReadOnly();

    /// <summary>
    /// Registers a contextual command provider for a specific context key.
    /// </summary>
    public void RegisterContextualProvider(string contextKey, Func<IReadOnlyList<CommandEntry>> provider)
    {
        _contextualProviders[contextKey] = provider;
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Unregisters a contextual command provider.
    /// </summary>
    public void UnregisterContextualProvider(string contextKey)
    {
        if (_contextualProviders.TryRemove(contextKey, out _))
        {
            if (_activeContextKey == contextKey)
            {
                _activeContextKey = null;
            }
            CommandsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sets the active context, enabling contextual commands from that context's provider.
    /// </summary>
    public void SetActiveContext(string contextKey)
    {
        _activeContextKey = contextKey;
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears the active context, disabling contextual commands.
    /// </summary>
    public void ClearActiveContext()
    {
        _activeContextKey = null;
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<PaletteCommand> GetRecentAndPopularCommands()
    {
        var result = new List<PaletteCommand>();

        // Add recent commands first
        foreach (var recentId in _recentCommands)
        {
            var cmd = _commands.FirstOrDefault(c => c.Id == recentId);
            if (cmd != null)
            {
                result.Add(cmd);
            }
        }

        // Fill with popular/default commands
        var defaultIds = new[] { "nav-dashboard", "nav-backtest", "nav-live-data", "nav-backfill", "nav-symbols", "nav-data-quality", "nav-provider", "nav-settings" };
        foreach (var id in defaultIds)
        {
            if (result.All(c => c.Id != id))
            {
                var cmd = _commands.FirstOrDefault(c => c.Id == id);
                if (cmd != null)
                {
                    result.Add(cmd);
                }
            }
        }

        return result.Take(10).ToList();
    }

    private static int CalculateMatchScore(PaletteCommand command, string query)
    {
        var score = 0;
        var title = command.Title.ToLowerInvariant();
        var keywords = command.Keywords.ToLowerInvariant();
        var description = command.Description.ToLowerInvariant();

        // Exact title match
        if (title == query)
            return 1000;

        // Title starts with query
        if (title.StartsWith(query, StringComparison.Ordinal))
            score += 100;

        // Title contains query
        if (title.Contains(query, StringComparison.Ordinal))
            score += 50;

        // Keywords match
        if (keywords.Contains(query, StringComparison.Ordinal))
            score += 40;

        // Description match
        if (description.Contains(query, StringComparison.Ordinal))
            score += 20;

        // Fuzzy matching: check if all query characters appear in order
        if (score == 0 && FuzzyMatch(title, query))
        {
            score += 10;
        }

        if (score == 0 && FuzzyMatch(keywords, query))
        {
            score += 5;
        }

        return score;
    }

    private static bool FuzzyMatch(string text, string query)
    {
        var textIndex = 0;
        var queryIndex = 0;

        while (textIndex < text.Length && queryIndex < query.Length)
        {
            if (text[textIndex] == query[queryIndex])
            {
                queryIndex++;
            }
            textIndex++;
        }

        return queryIndex == query.Length;
    }

    private void RegisterDefaultCommands()
    {
        // Navigation commands
        RegisterNavigationCommand("nav-dashboard", "Open Research: Dashboard", "Dashboard", "research dash home main overview workstation", "\uE80F", "Ctrl+D");
        RegisterNavigationCommand("nav-watchlist", "Navigate to Watchlist", "Watchlist", "watch list favorites", "\uE728", "Ctrl+W");
        RegisterNavigationCommand("nav-symbols", "Navigate to Symbols", "Symbols", "symbol ticker stock add remove", "\uE71B", "Ctrl+Y");
        RegisterNavigationCommand("nav-backfill", "Open Data Operations: Backfill", "Backfill", "data operations backfill historical download import", "\uE896", "Ctrl+B");
        RegisterNavigationCommand("nav-live-data", "Open Trading: Live Data", "LiveData", "trading live data streaming real time cockpit", "\uE9F5", "");
        RegisterNavigationCommand("nav-runmat", "Navigate to RunMat Lab", "RunMat", "runmat matlab script research gpu analysis", "\uE943", "");
        RegisterNavigationCommand("nav-strategy-runs", "Open Research: Strategy Runs", "StrategyRuns", "strategy runs history browser workstation portfolio ledger", "\uE8FD", "");
        RegisterNavigationCommand("nav-run-detail", "Navigate to Run Detail", "RunDetail", "run detail strategy execution summary", "\uE946", "");
        RegisterNavigationCommand("nav-run-portfolio", "Open Trading: Run Portfolio", "RunPortfolio", "trading portfolio positions exposure workstation", "\uE8B5", "");
        RegisterNavigationCommand("nav-run-ledger", "Open Governance: Run Ledger", "RunLedger", "governance ledger journal trial balance audit workstation", "\uE9D5", "");
        RegisterNavigationCommand("nav-data-quality", "Open Governance: Data Quality", "DataQuality", "governance quality metrics completeness gaps", "\uE9D5", "Ctrl+Q");
        RegisterNavigationCommand("nav-storage", "Open Data Operations: Storage", "Storage", "data operations storage disk files size", "\uEDA2", "");
        RegisterNavigationCommand("nav-provider", "Open Data Operations: Providers", "Provider", "data operations provider data source alpaca polygon ib", "\uE774", "");
        RegisterNavigationCommand("nav-provider-health", "Navigate to Provider Health", "ProviderHealth", "provider health status connection", "\uE95E", "");
        RegisterNavigationCommand("nav-data-sources", "Navigate to Data Sources", "DataSources", "data sources configure", "\uE943", "");
        RegisterNavigationCommand("nav-data-browser", "Navigate to Data Browser", "DataBrowser", "data browser explore files", "\uEC50", "");
        RegisterNavigationCommand("nav-data-calendar", "Navigate to Data Calendar", "DataCalendar", "data calendar coverage date", "\uE787", "");
        RegisterNavigationCommand("nav-charts", "Navigate to Charts", "Charts", "chart graph visualization candlestick", "\uE9D9", "");
        RegisterNavigationCommand("nav-order-book", "Navigate to Order Book", "OrderBook", "order book depth level 2 l2", "\uE8C1", "");
        RegisterNavigationCommand("nav-settings", "Navigate to Settings", "Settings", "settings configuration preferences", "\uE713", "Ctrl+0");
        RegisterNavigationCommand("nav-help", "Navigate to Help", "Help", "help documentation guide faq", "\uE897", "F1");
        RegisterNavigationCommand("nav-diagnostics", "Navigate to Diagnostics", "Diagnostics", "diagnostics debug troubleshoot", "\uE9D9", "");
        RegisterNavigationCommand("nav-system-health", "Navigate to System Health", "SystemHealth", "system health monitor cpu memory", "\uE95E", "");
        RegisterNavigationCommand("nav-collection-sessions", "Navigate to Collection Sessions", "CollectionSessions", "collection session active running", "\uE768", "");
        RegisterNavigationCommand("nav-archive-health", "Navigate to Archive Health", "ArchiveHealth", "archive health integrity checksum", "\uE8B7", "");
        RegisterNavigationCommand("nav-service-manager", "Navigate to Service Manager", "ServiceManager", "service manager logs process", "\uE912", "");
        RegisterNavigationCommand("nav-data-export", "Navigate to Data Export", "DataExport", "export data csv parquet download", "\uE78B", "");
        RegisterNavigationCommand("nav-export-presets", "Navigate to Export Presets", "ExportPresets", "export preset template format", "\uE8B7", "");
        RegisterNavigationCommand("nav-analysis-export", "Navigate to Analysis Export", "AnalysisExport", "analysis export pandas python", "\uE9D9", "");
        RegisterNavigationCommand("nav-event-replay", "Navigate to Event Replay", "EventReplay", "event replay playback simulate", "\uE768", "");
        RegisterNavigationCommand("nav-package-manager", "Navigate to Package Manager", "PackageManager", "package zip import share", "\uE7B8", "");
        RegisterNavigationCommand("nav-schedules", "Navigate to Schedule Manager", "Schedules", "schedule cron job timer", "\uE823", "");
        RegisterNavigationCommand("nav-backtest", "Open Research: Backtest", "Backtest", "research backtest strategy simulation run test historical replay", "\uE9D9", "Ctrl+Shift+B");
        RegisterNavigationCommand("nav-lean", "Open Research: Lean Integration", "LeanIntegration", "research lean quantconnect backtest engine algorithm", "\uE943", "");
        RegisterNavigationCommand("nav-admin-maintenance", "Navigate to Admin Maintenance", "AdminMaintenance", "admin maintenance cleanup", "\uE90F", "");
        RegisterNavigationCommand("nav-storage-optimization", "Navigate to Storage Optimization", "StorageOptimization", "storage optimize compress tier", "\uEDA2", "");
        RegisterNavigationCommand("nav-keyboard-shortcuts", "Navigate to Keyboard Shortcuts", "KeyboardShortcuts", "keyboard shortcut hotkey bind", "\uE765", "");
        RegisterNavigationCommand("nav-setup-wizard", "Navigate to Setup Wizard", "SetupWizard", "setup wizard configure first run", "\uE74C", "");
        RegisterNavigationCommand("nav-activity-log", "Navigate to Activity Log", "ActivityLog", "activity log events history", "\uE81C", "");
        RegisterNavigationCommand("nav-symbol-mapping", "Navigate to Symbol Mapping", "SymbolMapping", "symbol mapping alias convert", "\uE8AB", "");
        RegisterNavigationCommand("nav-portfolio-import", "Navigate to Portfolio Import", "PortfolioImport", "portfolio import csv bulk ledger positions trades", "\uE8B5", "");
        RegisterNavigationCommand("nav-trading-hours", "Navigate to Trading Hours", "TradingHours", "trading hours market calendar sessions open close backtest", "\uE823", "");
        RegisterNavigationCommand("nav-workspaces", "Navigate to Workspaces", "Workspaces", "workspace layout save restore", "\uE737", "");
        RegisterNavigationCommand("nav-advanced-analytics", "Navigate to Advanced Analytics", "AdvancedAnalytics", "analytics advanced statistics", "\uE9D9", "");
        RegisterNavigationCommand("nav-retention", "Navigate to Retention Assurance", "RetentionAssurance", "retention policy assurance audit", "\uE74C", "");
        RegisterNavigationCommand("nav-notification-center", "Navigate to Notification Center", "NotificationCenter", "notification alert message", "\uEA8F", "");
        RegisterNavigationCommand("nav-messaging-hub", "Navigate to Messaging Hub", "MessagingHub", "messaging webhook slack discord", "\uE8F2", "");
        RegisterNavigationCommand("nav-data-sampling", "Navigate to Data Sampling", "DataSampling", "data sampling preview inspect", "\uE9D9", "");
        RegisterNavigationCommand("nav-time-series", "Navigate to Time Series Alignment", "TimeSeriesAlignment", "time series alignment sync", "\uE916", "");
        RegisterNavigationCommand("nav-index-subscription", "Navigate to Index Subscription", "IndexSubscription", "index subscription sp500 nasdaq", "\uE71B", "");
        RegisterNavigationCommand("nav-symbol-storage", "Navigate to Symbol Storage", "SymbolStorage", "symbol storage files size", "\uEDA2", "");
        RegisterNavigationCommand("nav-options", "Navigate to Options / Derivatives", "Options", "options derivatives contracts puts calls configuration", "\uE945", "");
        RegisterNavigationCommand("nav-analysis-export-wizard", "Open Analysis Export Wizard", "AnalysisExportWizard", "analysis export wizard pandas python arrow parquet", "\uE9D9", "");
        RegisterNavigationCommand("nav-research-shell", "Open Research Workspace", "ResearchShell", "research workspace shell home strategy runs backtest overview", "\uE9D9", "");
        RegisterNavigationCommand("nav-trading-shell", "Open Trading Workspace", "TradingShell", "trading workspace shell live positions portfolio risk rail overview", "\uE9F5", "");
        RegisterNavigationCommand("nav-quantscript", "Open Research: QuantScript", "QuantScript", "research quant script prototype calculation ide code", "\uE943", "");
        RegisterNavigationCommand("nav-batch-backtest", "Open Research: Batch Backtest", "BatchBacktest", "research batch backtest bulk run multi strategy sweep", "\uE8EF", "");
        RegisterNavigationCommand("nav-run-cashflow", "Open Research: Run Cash Flow", "RunCashFlow", "research run cash flow funding impact projection", "\uE8C8", "");
        RegisterNavigationCommand("nav-security-master", "Open Governance: Security Master", "SecurityMaster", "governance security master reference data listings identifiers", "\uE8F1", "");
        RegisterNavigationCommand("nav-direct-lending", "Open Governance: Direct Lending", "DirectLending", "governance direct lending portfolio workflow contracts accrual", "\uE8C8", "");
        RegisterNavigationCommand("nav-credential-management", "Open Governance: Credential Management", "CredentialManagement", "governance credential management provider access secure api key", "\uE8D7", "");
        RegisterNavigationCommand("nav-add-provider-wizard", "Open Data Operations: Add Provider Wizard", "AddProviderWizard", "data operations add provider wizard configure setup onboard", "\uE710", "");

        // Action commands
        RegisterActionCommand("action-start-collector", "Start Data Collector", "StartCollector", "start collect begin run", "\uE768", "Ctrl+Shift+S");
        RegisterActionCommand("action-stop-collector", "Stop Data Collector", "StopCollector", "stop collect halt end", "\uE71A", "Ctrl+Shift+Q");
        RegisterActionCommand("action-run-backfill", "Run Backfill", "RunBackfill", "run backfill start historical", "\uE896", "Ctrl+R");
        RegisterActionCommand("action-refresh", "Refresh Status", "RefreshStatus", "refresh reload update", "\uE72C", "F5");
        RegisterActionCommand("action-add-symbol", "Add Symbol", "AddSymbol", "add symbol new ticker", "\uE710", "Ctrl+N");
        RegisterActionCommand("action-toggle-theme", "Toggle Dark/Light Theme", "ToggleTheme", "theme dark light mode toggle", "\uE771", "Ctrl+Shift+T");
        RegisterActionCommand("action-save", "Save Current", "Save", "save persist write", "\uE74E", "Ctrl+S");
        RegisterActionCommand("action-search", "Search Symbols", "SearchSymbols", "search find filter symbol", "\uE721", "Ctrl+F");
    }

    private void RegisterNavigationCommand(string id, string title, string pageTag, string keywords, string icon, string shortcut)
    {
        _commands.Add(new PaletteCommand
        {
            Id = id,
            Title = title,
            Description = BuildNavigationDescription(pageTag),
            Category = PaletteCommandCategory.Navigation,
            ActionId = pageTag,
            Keywords = keywords,
            Icon = icon,
            Shortcut = shortcut
        });
    }

    private static string BuildNavigationDescription(string pageTag)
    {
        return pageTag switch
        {
            // Research workspace: analysis, strategy runs, charting, quant tooling
            "Dashboard" or "Backtest" or "BatchBacktest" or "LeanIntegration" or "RunMat"
                or "Charts" or "QuantScript" or "Watchlist" or "OrderBook"
                or "StrategyRuns" or "RunDetail" or "RunCashFlow" or "RunPortfolio"
                or "AdvancedAnalytics" or "ResearchShell"
                => $"Research workspace — {pageTag}",

            // Trading workspace: live execution, positions, hours
            "LiveData" or "TradingHours" or "TradingShell"
                => $"Trading workspace — {pageTag}",

            // Data Operations workspace: ingest, symbols, storage, tools
            "Provider" or "DataSources" or "Symbols" or "Backfill" or "Storage"
                or "DataExport" or "PackageManager" or "Schedules" or "DataBrowser"
                or "DataCalendar" or "EventReplay" or "DataSampling" or "TimeSeriesAlignment"
                or "AnalysisExport" or "AnalysisExportWizard" or "ExportPresets"
                or "IndexSubscription" or "SymbolMapping" or "SymbolStorage" or "Options"
                or "PortfolioImport" or "AddProviderWizard"
                => $"Data Operations workspace — {pageTag}",

            // Governance workspace: quality, audit, ledger, health, admin, setup
            "DataQuality" or "ProviderHealth" or "SystemHealth" or "Diagnostics"
                or "Settings" or "AdminMaintenance" or "RetentionAssurance"
                or "NotificationCenter" or "Help" or "RunLedger" or "ArchiveHealth"
                or "ServiceManager" or "CollectionSessions" or "StorageOptimization"
                or "ActivityLog" or "MessagingHub" or "SecurityMaster" or "DirectLending"
                or "CredentialManagement" or "SetupWizard" or "KeyboardShortcuts"
                => $"Governance workspace — {pageTag}",

            _ => $"Navigate to {pageTag}"
        };
    }

    private void RegisterActionCommand(string id, string title, string actionId, string keywords, string icon, string shortcut)
    {
        _commands.Add(new PaletteCommand
        {
            Id = id,
            Title = title,
            Description = $"Execute: {title}",
            Category = PaletteCommandCategory.Action,
            ActionId = actionId,
            Keywords = keywords,
            Icon = icon,
            Shortcut = shortcut
        });
    }
}

/// <summary>
/// A command available in the command palette.
/// </summary>
public sealed class PaletteCommand
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public PaletteCommandCategory Category { get; init; }
    public string ActionId { get; init; } = string.Empty;
    public string Keywords { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Shortcut { get; init; } = string.Empty;
}

/// <summary>
/// Category of a palette command.
/// </summary>
public enum PaletteCommandCategory : byte
{
    Navigation,
    Action,
    Setting,
    Recent
}

/// <summary>
/// Event args for command palette execution.
/// </summary>
public sealed class PaletteCommandEventArgs : EventArgs
{
    public string CommandId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public PaletteCommandCategory Category { get; init; }
}

/// <summary>
/// A contextual command entry with title, description, category, and command.
/// </summary>
public sealed record CommandEntry(
    string Title,
    string? Description,
    string? Category,
    ICommand Command,
    string? Shortcut = null);

