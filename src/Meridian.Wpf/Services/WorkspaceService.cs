using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using UiServices = Meridian.Ui.Services;
using WorkspaceTemplate = Meridian.Ui.Services.WorkspaceTemplate;
using SessionState = Meridian.Ui.Services.SessionState;
using WorkspaceCategory = Meridian.Ui.Services.WorkspaceCategory;
using WorkspacePage = Meridian.Ui.Services.WorkspacePage;
using WidgetPosition = Meridian.Ui.Services.WidgetPosition;
using WindowBounds = Meridian.Ui.Services.WindowBounds;
using WorkspaceEventArgs = Meridian.Ui.Services.WorkspaceEventArgs;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for managing workspace templates and session restore.
/// Uses file-based JSON storage (WPF replacement for ApplicationData.LocalSettings).
/// </summary>
public sealed class WorkspaceService
{
    private static readonly Lazy<WorkspaceService> _instance = new(() => new WorkspaceService());
    private static string? _settingsFilePathOverride;

    private const string WorkspacesFileName = "workspace-data.json";

    private WorkspaceTemplate? _activeWorkspace;
    private SessionState? _lastSession;
    private readonly List<WorkspaceTemplate> _workspaces = new();
    private readonly Dictionary<string, SessionState> _sessionsByFundProfileId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkstationLayoutState> _workspaceLayouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private bool _isInitialized;

    public static WorkspaceService Instance => _instance.Value;

    private WorkspaceService()
    {
    }

    public WorkspaceTemplate? ActiveWorkspace => WithStateLock(() => _activeWorkspace);
    public SessionState? LastSession => WithStateLock(() => _lastSession);
    public IReadOnlyList<WorkspaceTemplate> Workspaces => WithStateLock(() => _workspaces.ToList().AsReadOnly());
    public string? LastSelectedFundProfileId { get; private set; }
    public string? LastSelectedOperatingContextKey => LastSelectedFundProfileId;

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await LoadWorkspacesCoreAsync(ct).ConfigureAwait(false);
    }

    private async Task WithStateLockAsync(Func<Task> action, CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task<T> WithStateLockAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private void WithStateLock(Action action)
    {
        _stateGate.Wait();
        try
        {
            action();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private T WithStateLock<T>(Func<T> action)
    {
        _stateGate.Wait();
        try
        {
            return action();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static readonly IReadOnlyDictionary<string, string> LegacyWorkspaceIdMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["monitoring"] = "research",
            ["backfill-ops"] = "trading",
            ["storage-admin"] = "data-operations",
            ["analysis-export"] = "governance"
        };

    private static readonly Lazy<IReadOnlyDictionary<string, string>> UniquePageWorkspaceOwnership =
        new(BuildUniquePageWorkspaceOwnership);

    private static string GetSettingsFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_settingsFilePathOverride))
        {
            var overrideDirectory = Path.GetDirectoryName(_settingsFilePathOverride);
            if (!string.IsNullOrWhiteSpace(overrideDirectory))
            {
                Directory.CreateDirectory(overrideDirectory);
            }

            return _settingsFilePathOverride;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, WorkspacesFileName);
    }

    public static string? InferWorkspaceIdForPageTag(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return null;
        }

        return ShellNavigationCatalog.InferWorkspaceIdForPageTag(pageTag.Trim());
    }

    /// <summary>
    /// Persisted workspace data container.
    /// </summary>
    private sealed class WorkspaceData
    {
        public List<WorkspaceTemplate> Workspaces { get; set; } = new();
        public string? ActiveWorkspaceId { get; set; }
        public SessionState? LastSession { get; set; }
        public Dictionary<string, SessionState> SessionsByFundProfileId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, WorkstationLayoutState> WorkspaceLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Per-workspace AvalonDock layout XML, keyed by workspace ID (e.g., "trading", "research").
        /// </summary>
        public Dictionary<string, string> DockLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public Task LoadWorkspacesAsync(CancellationToken ct = default)
        => WithStateLockAsync(() => LoadWorkspacesCoreAsync(ct), ct);

    private async Task LoadWorkspacesCoreAsync(CancellationToken ct = default)
    {
        try
        {
            var filePath = GetSettingsFilePath();
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                var data = JsonSerializer.Deserialize<WorkspaceData>(json, UiServices.DesktopJsonOptions.Compact);

                if (data != null)
                {
                    if (data.Workspaces.Count > 0)
                    {
                        _workspaces.Clear();
                        _workspaces.AddRange(data.Workspaces);
                    }

                    if (data.ActiveWorkspaceId != null)
                    {
                        var normalizedWorkspaceId = NormalizeWorkspaceId(data.ActiveWorkspaceId);
                        _activeWorkspace = _workspaces.FirstOrDefault(w => w.Id == normalizedWorkspaceId);
                    }

                    _lastSession = data.LastSession;
                    if (_lastSession?.WorkstationLayout is not null)
                    {
                        _lastSession.WorkstationLayout = CloneWorkstationLayoutState(_lastSession.WorkstationLayout);
                    }

                    _sessionsByFundProfileId.Clear();
                    foreach (var kvp in data.SessionsByFundProfileId)
                    {
                        _sessionsByFundProfileId[kvp.Key] = NormalizeSessionState(CloneSessionState(kvp.Value), kvp.Value.ActiveWorkspaceId);
                    }

                    if (_sessionsByFundProfileId.Count == 0 &&
                        _lastSession is not null &&
                        !string.IsNullOrWhiteSpace(LastSelectedFundProfileId))
                    {
                        foreach (var sessionScopeKey in GetEquivalentSessionScopeKeys(LastSelectedFundProfileId))
                        {
                            _sessionsByFundProfileId[sessionScopeKey] = _lastSession;
                        }
                    }

                    _dockLayouts.Clear();
                    if (data.DockLayouts.Count > 0)
                    {
                        foreach (var kvp in data.DockLayouts)
                            _dockLayouts[kvp.Key] = kvp.Value;
                    }

                    _workspaceLayouts.Clear();
                    foreach (var kvp in data.WorkspaceLayouts)
                    {
                        _workspaceLayouts[kvp.Key] = CloneWorkstationLayoutState(kvp.Value);
                    }
                }
            }

            var migratedLegacyState = MigrateLegacyWorkspaceState();

            var createdDefaults = false;
            if (_workspaces.Count == 0)
            {
                _workspaces.AddRange(GetDefaultWorkspaces());
                createdDefaults = true;
            }

            var changed = EnsureBuiltInWorkspaces();
            if (changed || createdDefaults || migratedLegacyState)
            {
                await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isInitialized = true;
        }
    }

    public Task SaveWorkspacesAsync(CancellationToken ct = default)
        => WithStateLockAsync(() => SaveWorkspacesCoreAsync(ct), ct);

    private async Task SaveWorkspacesCoreAsync(CancellationToken ct = default)
    {
        try
        {
            var data = new WorkspaceData
            {
                Workspaces = _workspaces.ToList(),
                ActiveWorkspaceId = _activeWorkspace?.Id,
                LastSession = _lastSession is null ? null : CloneSessionState(_lastSession),
                SessionsByFundProfileId = _sessionsByFundProfileId.ToDictionary(
                    static pair => pair.Key,
                    static pair => CloneSessionState(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
                WorkspaceLayouts = _workspaceLayouts.ToDictionary(
                    static pair => pair.Key,
                    static pair => CloneWorkstationLayoutState(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
                DockLayouts = new Dictionary<string, string>(_dockLayouts, StringComparer.OrdinalIgnoreCase)
            };

            var json = JsonSerializer.Serialize(data, UiServices.DesktopJsonOptions.PrettyPrint);
            await File.WriteAllTextAsync(GetSettingsFilePath(), json);
        }
        catch (Exception)
        {
        }
    }

    public Task<WorkspaceTemplate> CreateWorkspaceAsync(string name, string description, WorkspaceCategory category, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var workspace = new WorkspaceTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Description = description,
                Category = category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>(),
                WidgetLayout = new Dictionary<string, WidgetPosition>(),
                Filters = new Dictionary<string, string>()
            };

            _workspaces.Add(workspace);
            await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);

            WorkspaceCreated?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
            return workspace;
        }, ct);

    public Task UpdateWorkspaceAsync(WorkspaceTemplate workspace, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var existing = _workspaces.FirstOrDefault(w => w.Id == workspace.Id);
            if (existing != null)
            {
                var index = _workspaces.IndexOf(existing);
                workspace.UpdatedAt = DateTime.UtcNow;
                _workspaces[index] = workspace;
                await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);

                WorkspaceUpdated?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
            }
        }, ct);

    public Task DeleteWorkspaceAsync(string workspaceId, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var workspace = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
            if (workspace != null && !workspace.IsBuiltIn)
            {
                _workspaces.Remove(workspace);
                await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);

                WorkspaceDeleted?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
            }
        }, ct);

    public Task ActivateWorkspaceAsync(string workspaceId, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var workspace = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
            if (workspace != null)
            {
                var pendingSession = _lastSession is null ? null : CloneSessionState(_lastSession);
                PersistActiveWorkspaceSnapshot();
                _activeWorkspace = workspace;
                workspace.LastActivatedAt = DateTime.UtcNow;
                _lastSession = TryRestorePendingSessionForWorkspace(workspace, pendingSession)
                    ?? RestoreSessionForWorkspace(workspace, pendingSession);
                await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);

                WorkspaceActivated?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
            }
        }, ct);

    public Task<WorkspaceTemplate> CaptureCurrentStateAsync(string name, string description, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var workspace = new WorkspaceTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Description = description,
                Category = WorkspaceCategory.Custom,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PreferredPageTag = _lastSession?.ActivePageTag
                    ?? _activeWorkspace?.LastActivePageTag
                    ?? _activeWorkspace?.PreferredPageTag
                    ?? "Dashboard",
                Pages = _lastSession?.OpenPages ?? new List<WorkspacePage>(),
                WidgetLayout = _lastSession?.WidgetLayout ?? new Dictionary<string, WidgetPosition>(),
                Filters = _lastSession?.ActiveFilters ?? new Dictionary<string, string>(),
                WindowBounds = _lastSession?.WindowBounds
            };

            _workspaces.Add(workspace);
            await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);

            return workspace;
        }, ct);

    public Task SaveSessionStateAsync(SessionState state, string? fundProfileId = null, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            try
            {
                await EnsureInitializedAsync(ct).ConfigureAwait(false);
                var resolvedWorkspace = ResolveWorkspaceForSession(state, state.ActiveWorkspaceId ?? _activeWorkspace?.Id);
                if (resolvedWorkspace is not null)
                {
                    _activeWorkspace = resolvedWorkspace;
                    state.ActiveWorkspaceId = resolvedWorkspace.Id;
                }
                else
                {
                    state.ActiveWorkspaceId = NormalizeWorkspaceId(state.ActiveWorkspaceId ?? _activeWorkspace?.Id);
                }

                state.SavedAt = DateTime.UtcNow;
                _lastSession = state;
                var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
                if (!string.IsNullOrWhiteSpace(normalizedFundProfileId))
                {
                    LastSelectedFundProfileId = normalizedFundProfileId;
                    foreach (var sessionScopeKey in GetEquivalentSessionScopeKeys(normalizedFundProfileId))
                    {
                        _sessionsByFundProfileId[sessionScopeKey] = state;
                    }
                }

                PersistActiveWorkspaceSnapshot();
                await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }, ct);

    public SessionState? GetLastSessionState() => WithStateLock(() => _lastSession);

    public SessionState? GetLastSessionState(string? fundProfileId)
        => WithStateLock(() =>
        {
            var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
            if (string.IsNullOrWhiteSpace(normalizedFundProfileId))
            {
                if (_lastSession is not null)
                {
                    NormalizeSessionState(_lastSession, _lastSession.ActiveWorkspaceId ?? _activeWorkspace?.Id);
                }

                return _lastSession;
            }

            foreach (var sessionScopeKey in GetEquivalentSessionScopeKeys(normalizedFundProfileId))
            {
                if (_sessionsByFundProfileId.TryGetValue(sessionScopeKey, out var session))
                {
                    NormalizeSessionState(session, session.ActiveWorkspaceId ?? _activeWorkspace?.Id);
                    _lastSession = session;
                    LastSelectedFundProfileId = normalizedFundProfileId;

                    foreach (var equivalentKey in GetEquivalentSessionScopeKeys(normalizedFundProfileId))
                    {
                        _sessionsByFundProfileId[equivalentKey] = session;
                    }

                    return session;
                }
            }

            _lastSession = new SessionState();
            LastSelectedFundProfileId = normalizedFundProfileId;
            return null;
        });

    public Task SetLastSelectedFundProfileIdAsync(string? fundProfileId, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            LastSelectedFundProfileId = NormalizeFundProfileId(fundProfileId);
            await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task SetLastSelectedOperatingContextKeyAsync(string? operatingContextKey, CancellationToken ct = default)
        => SetLastSelectedFundProfileIdAsync(operatingContextKey, ct);

    public SessionState? GetLastSessionStateForContext(string? operatingContextKey)
        => GetLastSessionState(operatingContextKey);

    /// <summary>
    /// Saves a single named filter value for a page into the active session's
    /// <see cref="SessionState.ActiveFilters"/> dictionary.
    /// Uses the composite key format <c>&quot;{pageTag}.{filterKey}&quot;</c>.
    /// Passing <see langword="null"/> as <paramref name="value"/> removes the entry.
    /// </summary>
    public void UpdatePageFilterState(string pageTag, string filterKey, string? value)
        => WithStateLock(() =>
        {
            _lastSession ??= new SessionState();
            var key = $"{pageTag}.{filterKey}";
            if (value is null)
            {
                _lastSession.ActiveFilters.Remove(key);
            }
            else
            {
                _lastSession.ActiveFilters[key] = value;
            }
        });

    /// <summary>
    /// Retrieves a previously saved filter value for a page from the active session.
    /// Returns <see langword="null"/> when no value has been stored.
    /// </summary>
    public string? GetPageFilterState(string pageTag, string filterKey)
        => WithStateLock(() =>
        {
            if (_lastSession is null)
            {
                return null;
            }

            var key = $"{pageTag}.{filterKey}";
            return _lastSession.ActiveFilters.TryGetValue(key, out var value) ? value : null;
        });

    public Task<string> ExportWorkspaceAsync(string workspaceId)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync().ConfigureAwait(false);

            var workspace = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
            return workspace != null
                ? JsonSerializer.Serialize(workspace, UiServices.DesktopJsonOptions.PrettyPrint)
                : string.Empty;
        });

    public Task<WorkspaceTemplate?> ImportWorkspaceAsync(string json, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            try
            {
                await EnsureInitializedAsync(ct).ConfigureAwait(false);

                var workspace = JsonSerializer.Deserialize<WorkspaceTemplate>(json);
                if (workspace != null)
                {
                    workspace.Id = Guid.NewGuid().ToString();
                    workspace.IsBuiltIn = false;
                    workspace.CreatedAt = DateTime.UtcNow;
                    workspace.UpdatedAt = DateTime.UtcNow;
                    workspace.PreferredPageTag = ResolvePreferredPageTag(workspace);

                    _workspaces.Add(workspace);
                    await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);

                    return workspace;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }, ct);

    private static List<WorkspaceTemplate> GetDefaultWorkspaces()
    {
        return new List<WorkspaceTemplate>
        {
            new WorkspaceTemplate
            {
                Id = "research",
                Name = "Research",
                Description = "Backtests, experiments, charts, strategy runs, and result analysis.",
                PreferredPageTag = "Dashboard",
                Category = WorkspaceCategory.Research,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "Dashboard", Title = "Dashboard", IsDefault = true },
                    new WorkspacePage { PageTag = "Backtest", Title = "Backtest" },
                    new WorkspacePage { PageTag = "BatchBacktest", Title = "Batch Backtest" },
                    new WorkspacePage { PageTag = "QuantScript", Title = "QuantScript" },
                    new WorkspacePage { PageTag = "StrategyRuns", Title = "Strategy Runs" },
                    new WorkspacePage { PageTag = "RunDetail", Title = "Run Detail" },
                    new WorkspacePage { PageTag = "RunPortfolio", Title = "Run Portfolio" },
                    new WorkspacePage { PageTag = "RunCashFlow", Title = "Run Cash Flow" },
                    new WorkspacePage { PageTag = "LeanIntegration", Title = "Lean Integration" },
                    new WorkspacePage { PageTag = "Charts", Title = "Charts" },
                    new WorkspacePage { PageTag = "AdvancedAnalytics", Title = "Advanced Analytics" },
                    new WorkspacePage { PageTag = "RunMat", Title = "RunMat Lab" },
                    new WorkspacePage { PageTag = "Watchlist", Title = "Watchlist" },
                    new WorkspacePage { PageTag = "ResearchShell", Title = "Research Workspace" }
                }
            },
            new WorkspaceTemplate
            {
                Id = "trading",
                Name = "Trading",
                Description = "Live monitoring, order flow, and trading controls.",
                PreferredPageTag = "LiveData",
                Category = WorkspaceCategory.Trading,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "LiveData", Title = "Live Data", IsDefault = true },
                    new WorkspacePage { PageTag = "TradingHours", Title = "Trading Hours" },
                    new WorkspacePage { PageTag = "StrategyRuns", Title = "Strategy Runs" },
                    new WorkspacePage { PageTag = "RunPortfolio", Title = "Run Portfolio" },
                    new WorkspacePage { PageTag = "RunLedger", Title = "Run Ledger" },
                    new WorkspacePage { PageTag = "PositionBlotter", Title = "Position Blotter" },
                    new WorkspacePage { PageTag = "RunRisk", Title = "Run Risk" },
                    new WorkspacePage { PageTag = "OrderBook", Title = "Order Book" },
                    new WorkspacePage { PageTag = "TradingShell", Title = "Trading Workspace" }
                }
            },
            new WorkspaceTemplate
            {
                Id = "data-operations",
                Name = "Data Operations",
                Description = "Providers, symbols, backfills, storage, schedules, and exports.",
                PreferredPageTag = "Provider",
                Category = WorkspaceCategory.DataOperations,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "Provider", Title = "Provider", IsDefault = true },
                    new WorkspacePage { PageTag = "DataSources", Title = "Data Sources" },
                    new WorkspacePage { PageTag = "Symbols", Title = "Symbols" },
                    new WorkspacePage { PageTag = "SymbolMapping", Title = "Symbol Mapping" },
                    new WorkspacePage { PageTag = "SymbolStorage", Title = "Symbol Storage" },
                    new WorkspacePage { PageTag = "Backfill", Title = "Backfill" },
                    new WorkspacePage { PageTag = "Schedules", Title = "Schedules" },
                    new WorkspacePage { PageTag = "IndexSubscription", Title = "Index Subscription" },
                    new WorkspacePage { PageTag = "Storage", Title = "Storage" },
                    new WorkspacePage { PageTag = "PackageManager", Title = "Packages" },
                    new WorkspacePage { PageTag = "DataExport", Title = "Data Export" },
                    new WorkspacePage { PageTag = "ExportPresets", Title = "Export Presets" },
                    new WorkspacePage { PageTag = "AnalysisExport", Title = "Analysis Export" },
                    new WorkspacePage { PageTag = "AnalysisExportWizard", Title = "Analysis Export Wizard" },
                    new WorkspacePage { PageTag = "DataBrowser", Title = "Data Browser" },
                    new WorkspacePage { PageTag = "DataCalendar", Title = "Data Calendar" },
                    new WorkspacePage { PageTag = "DataSampling", Title = "Data Sampling" },
                    new WorkspacePage { PageTag = "TimeSeriesAlignment", Title = "Time Series Alignment" },
                    new WorkspacePage { PageTag = "EventReplay", Title = "Event Replay" },
                    new WorkspacePage { PageTag = "Options", Title = "Options / Derivatives" },
                    new WorkspacePage { PageTag = "PortfolioImport", Title = "Portfolio Import" },
                    new WorkspacePage { PageTag = "AddProviderWizard", Title = "Add Provider Wizard" }
                }
            },
            new WorkspaceTemplate
            {
                Id = "governance",
                Name = "Governance",
                Description = "Quality, audit, health, diagnostics, retention, security, and settings.",
                PreferredPageTag = "DataQuality",
                Category = WorkspaceCategory.Governance,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "DataQuality", Title = "Data Quality", IsDefault = true },
                    new WorkspacePage { PageTag = "RunLedger", Title = "Run Ledger" },
                    new WorkspacePage { PageTag = "ProviderHealth", Title = "Provider Health" },
                    new WorkspacePage { PageTag = "SystemHealth", Title = "System Health" },
                    new WorkspacePage { PageTag = "Diagnostics", Title = "Diagnostics" },
                    new WorkspacePage { PageTag = "ArchiveHealth", Title = "Archive Health" },
                    new WorkspacePage { PageTag = "ServiceManager", Title = "Service Manager" },
                    new WorkspacePage { PageTag = "CollectionSessions", Title = "Collection Sessions" },
                    new WorkspacePage { PageTag = "StorageOptimization", Title = "Storage Optimization" },
                    new WorkspacePage { PageTag = "RetentionAssurance", Title = "Retention Assurance" },
                    new WorkspacePage { PageTag = "AdminMaintenance", Title = "Admin Maintenance" },
                    new WorkspacePage { PageTag = "ActivityLog", Title = "Activity Log" },
                    new WorkspacePage { PageTag = "MessagingHub", Title = "Messaging Hub" },
                    new WorkspacePage { PageTag = "NotificationCenter", Title = "Notification Center" },
                    new WorkspacePage { PageTag = "SecurityMaster", Title = "Security Master" },
                    new WorkspacePage { PageTag = "DirectLending", Title = "Direct Lending" },
                    new WorkspacePage { PageTag = "CredentialManagement", Title = "Credential Management" },
                    new WorkspacePage { PageTag = "SetupWizard", Title = "Setup Wizard" },
                    new WorkspacePage { PageTag = "KeyboardShortcuts", Title = "Keyboard Shortcuts" },
                    new WorkspacePage { PageTag = "Settings", Title = "Settings" }
                }
            }
        };
    }

    private bool EnsureBuiltInWorkspaces()
    {
        var changed = false;
        foreach (var builtIn in GetDefaultWorkspaces())
        {
            var existing = _workspaces.FirstOrDefault(workspace => workspace.Id == builtIn.Id);
            if (existing is null)
            {
                _workspaces.Insert(0, builtIn);
                changed = true;
                continue;
            }

            if (!existing.IsBuiltIn)
            {
                continue;
            }

            if (!string.Equals(existing.Name, builtIn.Name, StringComparison.Ordinal) ||
                !string.Equals(existing.Description, builtIn.Description, StringComparison.Ordinal) ||
                existing.Category != builtIn.Category ||
                !string.Equals(existing.PreferredPageTag, builtIn.PreferredPageTag, StringComparison.Ordinal))
            {
                existing.Name = builtIn.Name;
                existing.Description = builtIn.Description;
                existing.Category = builtIn.Category;
                existing.PreferredPageTag = builtIn.PreferredPageTag;
                changed = true;
            }

            foreach (var page in builtIn.Pages)
            {
                if (existing.Pages.All(existingPage => !string.Equals(existingPage.PageTag, page.PageTag, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.Pages.Add(page);
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static IReadOnlyDictionary<string, string> BuildUniquePageWorkspaceOwnership()
    {
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        static void AddOwner(IDictionary<string, HashSet<string>> map, string pageTag, string workspaceId)
        {
            if (!map.TryGetValue(pageTag, out var pageOwners))
            {
                pageOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[pageTag] = pageOwners;
            }

            pageOwners.Add(workspaceId);
        }

        foreach (var workspace in GetDefaultWorkspaces())
        {
            foreach (var page in workspace.Pages)
            {
                AddOwner(owners, page.PageTag, workspace.Id);
            }
        }

        AddOwner(owners, "GovernanceShell", "governance");
        AddOwner(owners, "DataOperationsShell", "data-operations");

        return owners
            .Where(static pair => pair.Value.Count == 1)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Single(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeFundProfileId(string? fundProfileId)
        => string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim();

    private static IReadOnlyList<string> GetEquivalentSessionScopeKeys(string? scopeKey)
    {
        var normalizedScopeKey = NormalizeFundProfileId(scopeKey);
        if (string.IsNullOrWhiteSpace(normalizedScopeKey))
        {
            return Array.Empty<string>();
        }

        var keys = new List<string> { normalizedScopeKey };
        if (WorkstationOperatingContext.TryGetFundScopeId(normalizedScopeKey, out var fundScopeId))
        {
            if (!string.Equals(fundScopeId, normalizedScopeKey, StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(fundScopeId);
            }

            return keys;
        }

        if (!normalizedScopeKey.Contains(':', StringComparison.Ordinal))
        {
            keys.Add(WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, normalizedScopeKey));
        }

        return keys;
    }

    private bool MigrateLegacyWorkspaceState()
    {
        var changed = false;

        foreach (var workspace in _workspaces)
        {
            if (TryMapLegacyBuiltIn(workspace.Id, out var migratedBuiltIn))
            {
                workspace.Id = migratedBuiltIn.Id;
                workspace.Name = migratedBuiltIn.Name;
                workspace.Description = migratedBuiltIn.Description;
                workspace.Category = migratedBuiltIn.Category;
                workspace.IsBuiltIn = true;
                MergeMissingPages(workspace, migratedBuiltIn.Pages);
                changed = true;
                continue;
            }

            if (workspace.IsBuiltIn && !string.IsNullOrWhiteSpace(workspace.Id))
            {
                var normalizedId = NormalizeWorkspaceId(workspace.Id);
                if (!string.Equals(workspace.Id, normalizedId, StringComparison.Ordinal))
                {
                    workspace.Id = normalizedId!;
                    changed = true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_lastSession?.ActiveWorkspaceId))
        {
            var normalizedActiveWorkspaceId = NormalizeWorkspaceId(_lastSession!.ActiveWorkspaceId);
            if (!string.Equals(_lastSession.ActiveWorkspaceId, normalizedActiveWorkspaceId, StringComparison.Ordinal))
            {
                _lastSession.ActiveWorkspaceId = normalizedActiveWorkspaceId;
                changed = true;
            }
        }

        if (_activeWorkspace != null)
        {
            var normalizedWorkspaceId = NormalizeWorkspaceId(_activeWorkspace.Id);
            if (!string.Equals(_activeWorkspace.Id, normalizedWorkspaceId, StringComparison.Ordinal))
            {
                _activeWorkspace = _workspaces.FirstOrDefault(workspace => workspace.Id == normalizedWorkspaceId) ?? _activeWorkspace;
                changed = true;
            }
        }

        return changed;
    }

    private static string? NormalizeWorkspaceId(string? workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return workspaceId;
        }

        return LegacyWorkspaceIdMap.TryGetValue(workspaceId, out var mappedWorkspaceId)
            ? mappedWorkspaceId
            : workspaceId;
    }

    private static bool TryMapLegacyBuiltIn(string? workspaceId, out WorkspaceTemplate builtIn)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            builtIn = null!;
            return false;
        }

        var currentBuiltIn = GetDefaultWorkspaces()
            .FirstOrDefault(workspace => string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
        if (currentBuiltIn != null)
        {
            builtIn = currentBuiltIn;
            return false;
        }

        if (LegacyWorkspaceIdMap.TryGetValue(workspaceId, out var migratedId))
        {
            builtIn = GetDefaultWorkspaces()
                .First(workspace => string.Equals(workspace.Id, migratedId, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        builtIn = null!;
        return false;
    }

    private WorkspaceTemplate? ResolveWorkspaceForSession(SessionState? session, string? fallbackWorkspaceId = null)
    {
        var inferredWorkspaceId = InferWorkspaceIdForPageTag(session?.ActivePageTag);
        var workspaceId = NormalizeWorkspaceId(inferredWorkspaceId)
            ?? NormalizeWorkspaceId(session?.ActiveWorkspaceId)
            ?? NormalizeWorkspaceId(fallbackWorkspaceId);

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        return _workspaces.FirstOrDefault(workspace =>
            string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
    }

    private SessionState NormalizeSessionState(SessionState session, string? fallbackWorkspaceId = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var resolvedWorkspace = ResolveWorkspaceForSession(session, fallbackWorkspaceId);
        if (resolvedWorkspace is not null)
        {
            session.ActiveWorkspaceId = resolvedWorkspace.Id;
            session.ActivePageTag = ResolveActivePageTag(resolvedWorkspace, session);
        }
        else
        {
            session.ActiveWorkspaceId = NormalizeWorkspaceId(session.ActiveWorkspaceId);
        }

        if (session.RecentPages.Count == 0 && !string.IsNullOrWhiteSpace(session.ActivePageTag))
        {
            session.RecentPages.Add(session.ActivePageTag);
        }

        return session;
    }

    internal static void SetSettingsFilePathOverrideForTests(string? filePath)
    {
        _settingsFilePathOverride = filePath;
    }

    internal void ResetForTests()
        => WithStateLock(() =>
        {
            _isInitialized = false;
            _activeWorkspace = null;
            _lastSession = null;
            LastSelectedFundProfileId = null;
            _sessionsByFundProfileId.Clear();
            _workspaces.Clear();
            _dockLayouts.Clear();
            _workspaceLayouts.Clear();
            WorkspaceCreated = null;
            WorkspaceUpdated = null;
            WorkspaceDeleted = null;
            WorkspaceActivated = null;
        });

    private static void MergeMissingPages(WorkspaceTemplate workspace, IEnumerable<WorkspacePage> builtInPages)
    {
        foreach (var page in builtInPages)
        {
            if (workspace.Pages.All(existingPage => !string.Equals(existingPage.PageTag, page.PageTag, StringComparison.OrdinalIgnoreCase)))
            {
                workspace.Pages.Add(page);
            }
        }
    }

    private void PersistActiveWorkspaceSnapshot()
    {
        if (_lastSession is null)
        {
            return;
        }

        var resolvedWorkspace = ResolveWorkspaceForSession(_lastSession, _activeWorkspace?.Id);
        if (resolvedWorkspace is null)
        {
            return;
        }

        _activeWorkspace = resolvedWorkspace;
        NormalizeSessionState(_lastSession, resolvedWorkspace.Id);

        _lastSession.ActiveWorkspaceId = _activeWorkspace.Id;
        _lastSession.ActivePageTag = ResolveActivePageTag(_activeWorkspace, _lastSession);
        if (_lastSession.RecentPages.Count == 0 && !string.IsNullOrWhiteSpace(_lastSession.ActivePageTag))
        {
            _lastSession.RecentPages.Add(_lastSession.ActivePageTag);
        }

        _activeWorkspace.LastActivePageTag = _lastSession.ActivePageTag;
        _activeWorkspace.LastActivatedAt = DateTime.UtcNow;
        _activeWorkspace.RecentPageTags = _lastSession.RecentPages
            .Where(static page => !string.IsNullOrWhiteSpace(page))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        _activeWorkspace.Filters = new Dictionary<string, string>(_lastSession.ActiveFilters, StringComparer.Ordinal);
        _activeWorkspace.Context = new Dictionary<string, string>(_lastSession.WorkspaceContext, StringComparer.Ordinal);
        _activeWorkspace.WindowBounds = CloneWindowBounds(_lastSession.WindowBounds);
        _activeWorkspace.SessionSnapshot = CloneSessionState(_lastSession);
    }

    private static SessionState RestoreSessionForWorkspace(WorkspaceTemplate workspace, SessionState? previousSession)
    {
        var snapshot = workspace.SessionSnapshot is null
            ? null
            : CloneSessionState(workspace.SessionSnapshot);

        if (snapshot is null)
        {
            var preferredPageTag = ResolvePreferredPageTag(workspace);
            snapshot = new SessionState
            {
                ActivePageTag = preferredPageTag,
                ActiveWorkspaceId = workspace.Id,
                OpenPages = CreateDefaultOpenPages(workspace, preferredPageTag),
                WidgetLayout = CloneWidgetLayout(workspace.WidgetLayout),
                ActiveFilters = new Dictionary<string, string>(workspace.Filters, StringComparer.Ordinal),
                WorkspaceContext = new Dictionary<string, string>(workspace.Context, StringComparer.Ordinal),
                WindowBounds = CloneWindowBounds(workspace.WindowBounds ?? previousSession?.WindowBounds),
                RecentPages = string.IsNullOrWhiteSpace(preferredPageTag)
                    ? new List<string>()
                    : new List<string> { preferredPageTag },
                SavedAt = DateTime.UtcNow
            };
        }

        snapshot.ActiveWorkspaceId = workspace.Id;
        snapshot.ActivePageTag = ResolveActivePageTag(workspace, snapshot);
        if (snapshot.RecentPages.Count == 0 && !string.IsNullOrWhiteSpace(snapshot.ActivePageTag))
        {
            snapshot.RecentPages.Add(snapshot.ActivePageTag);
        }

        return snapshot;
    }

    private SessionState? TryRestorePendingSessionForWorkspace(WorkspaceTemplate workspace, SessionState? pendingSession)
    {
        if (pendingSession is null)
        {
            return null;
        }

        var resolvedWorkspace = ResolveWorkspaceForSession(pendingSession, pendingSession.ActiveWorkspaceId);
        if (!string.Equals(resolvedWorkspace?.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizeSessionState(pendingSession, workspace.Id);
    }

    private static string ResolvePreferredPageTag(WorkspaceTemplate workspace)
    {
        if (!string.IsNullOrWhiteSpace(workspace.PreferredPageTag))
        {
            return workspace.PreferredPageTag;
        }

        return workspace.Pages.FirstOrDefault(static page => page.IsDefault)?.PageTag
            ?? workspace.Pages.FirstOrDefault()?.PageTag
            ?? "Dashboard";
    }

    private static string ResolveActivePageTag(WorkspaceTemplate workspace, SessionState session)
    {
        if (!string.IsNullOrWhiteSpace(session.ActivePageTag) &&
            IsPageCompatibleWithWorkspace(session.ActivePageTag, workspace.Id))
        {
            return session.ActivePageTag;
        }

        if (!string.IsNullOrWhiteSpace(workspace.LastActivePageTag) &&
            IsPageCompatibleWithWorkspace(workspace.LastActivePageTag, workspace.Id))
        {
            return workspace.LastActivePageTag;
        }

        return ResolvePreferredPageTag(workspace);
    }

    private static bool IsPageCompatibleWithWorkspace(string? pageTag, string workspaceId)
    {
        var inferredWorkspaceId = InferWorkspaceIdForPageTag(pageTag);
        return inferredWorkspaceId is null ||
               string.Equals(inferredWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase);
    }

    private static List<WorkspacePage> CreateDefaultOpenPages(WorkspaceTemplate workspace, string preferredPageTag)
    {
        var preferredPage = workspace.Pages.FirstOrDefault(page =>
            string.Equals(page.PageTag, preferredPageTag, StringComparison.OrdinalIgnoreCase));
        if (preferredPage is null)
        {
            return new List<WorkspacePage>();
        }

        return new List<WorkspacePage>
        {
            CloneWorkspacePage(preferredPage)
        };
    }

    private static SessionState CloneSessionState(SessionState session)
    {
        return new SessionState
        {
            ActivePageTag = session.ActivePageTag,
            OpenPages = session.OpenPages.Select(CloneWorkspacePage).ToList(),
            RecentPages = session.RecentPages.ToList(),
            WidgetLayout = CloneWidgetLayout(session.WidgetLayout),
            ActiveFilters = new Dictionary<string, string>(session.ActiveFilters, StringComparer.Ordinal),
            WorkspaceContext = new Dictionary<string, string>(session.WorkspaceContext, StringComparer.Ordinal),
            WindowBounds = CloneWindowBounds(session.WindowBounds),
            SavedAt = session.SavedAt,
            ActiveWorkspaceId = session.ActiveWorkspaceId,
            WorkstationLayout = session.WorkstationLayout is null
                ? null
                : CloneWorkstationLayoutState(session.WorkstationLayout)
        };
    }

    private static WorkspacePage CloneWorkspacePage(WorkspacePage page)
    {
        return new WorkspacePage
        {
            PageTag = page.PageTag,
            Title = page.Title,
            IsDefault = page.IsDefault,
            ScrollPosition = page.ScrollPosition,
            PageState = new Dictionary<string, object>(page.PageState, StringComparer.Ordinal)
        };
    }

    private static Dictionary<string, WidgetPosition> CloneWidgetLayout(IReadOnlyDictionary<string, WidgetPosition> layout)
    {
        return layout.ToDictionary(
            static pair => pair.Key,
            static pair => new WidgetPosition
            {
                Row = pair.Value.Row,
                Column = pair.Value.Column,
                RowSpan = pair.Value.RowSpan,
                ColumnSpan = pair.Value.ColumnSpan,
                IsVisible = pair.Value.IsVisible,
                IsExpanded = pair.Value.IsExpanded
            },
            StringComparer.Ordinal);
    }

    private static WindowBounds? CloneWindowBounds(WindowBounds? bounds)
    {
        if (bounds is null)
        {
            return null;
        }

        return new WindowBounds
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            MonitorId = bounds.MonitorId,
            IsMaximized = bounds.IsMaximized
        };
    }

    private static WorkspaceLayoutPreset CloneWorkspaceLayoutPreset(WorkspaceLayoutPreset preset)
    {
        return new WorkspaceLayoutPreset
        {
            PresetId = preset.PresetId,
            Name = preset.Name,
            IsBuiltIn = preset.IsBuiltIn,
            Layout = CloneWorkstationLayoutState(preset.Layout),
            SavedAt = preset.SavedAt
        };
    }

    private static WorkstationLayoutState CloneWorkstationLayoutState(WorkstationLayoutState layout)
    {
        return new WorkstationLayoutState
        {
            LayoutId = layout.LayoutId,
            DisplayName = layout.DisplayName,
            ActivePaneId = layout.ActivePaneId,
            OperatingContextKey = layout.OperatingContextKey,
            WindowMode = layout.WindowMode,
            LayoutPresetId = layout.LayoutPresetId,
            DockLayoutXml = layout.DockLayoutXml,
            SavedAt = layout.SavedAt,
            LayoutContext = new Dictionary<string, string>(layout.LayoutContext, StringComparer.Ordinal),
            Panes = layout.Panes.Select(static pane => new WorkstationPaneState
            {
                PaneId = pane.PaneId,
                PageTag = pane.PageTag,
                Title = pane.Title,
                DockZone = pane.DockZone,
                IsToolPane = pane.IsToolPane,
                IsPinned = pane.IsPinned,
                IsActive = pane.IsActive,
                Order = pane.Order
            }).ToList(),
            FloatingWindows = layout.FloatingWindows.Select(static window => new FloatingWorkspaceWindowState
            {
                WindowId = window.WindowId,
                PaneId = window.PaneId,
                Title = window.Title,
                Bounds = CloneWindowBounds(window.Bounds),
                IsOpen = window.IsOpen
            }).ToList()
        };
    }

    private static string BuildWorkspaceLayoutKey(string workspaceId, string? fundProfileId)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        return string.IsNullOrWhiteSpace(normalizedFundProfileId)
            ? workspaceId
            : $"{workspaceId}::{normalizedFundProfileId}";
    }

    public event EventHandler<WorkspaceEventArgs>? WorkspaceCreated;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceUpdated;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceDeleted;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceActivated;

    // ── AvalonDock layout persistence ─────────────────────────────────────────

    /// <summary>
    /// Persists the AvalonDock layout XML string for a named workspace shell
    /// (e.g., "trading" or "research") to the local application settings file.
    /// </summary>
    public Task SaveDockLayoutAsync(string workspaceId, string layoutXml, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            _dockLayouts[workspaceId] = layoutXml;

            await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);
        }, ct);

    /// <summary>
    /// Retrieves the previously persisted AvalonDock layout XML for a workspace shell.
    /// Returns <c>null</c> if no layout has been saved yet.
    /// </summary>
    public Task<string?> GetDockLayoutAsync(string workspaceId, string? fundProfileId = null, CancellationToken ct = default)
        => WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            var layoutKey = BuildWorkspaceLayoutKey(workspaceId, fundProfileId);
            if (_dockLayouts.TryGetValue(layoutKey, out var xml))
            {
                return xml;
            }

            if (!string.Equals(layoutKey, workspaceId, StringComparison.OrdinalIgnoreCase) &&
                _dockLayouts.TryGetValue(workspaceId, out xml))
            {
                return xml;
            }

            if (_workspaceLayouts.TryGetValue(layoutKey, out var layoutState))
            {
                return layoutState.DockLayoutXml;
            }

            if (_activeWorkspace?.Id == workspaceId && _lastSession?.WorkstationLayout is not null)
            {
                return _lastSession.WorkstationLayout.DockLayoutXml;
            }

            return null;
        }, ct);

    public Task SaveWorkspaceLayoutStateAsync(
        string workspaceId,
        WorkstationLayoutState layoutState,
        string? fundProfileId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(layoutState);

        return WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var layoutKey = BuildWorkspaceLayoutKey(workspaceId, fundProfileId);
            var clonedLayout = CloneWorkstationLayoutState(layoutState);
            clonedLayout.SavedAt = DateTime.UtcNow;
            _workspaceLayouts[layoutKey] = clonedLayout;

            if (!string.IsNullOrWhiteSpace(clonedLayout.DockLayoutXml))
            {
                _dockLayouts[layoutKey] = clonedLayout.DockLayoutXml!;
            }

            if (_activeWorkspace?.Id == workspaceId)
            {
                _lastSession ??= new SessionState();
                _lastSession.WorkstationLayout = CloneWorkstationLayoutState(clonedLayout);
            }

            PersistActiveWorkspaceSnapshot();
            await SaveWorkspacesCoreAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    public Task SaveWorkspaceLayoutStateForContextAsync(
        string workspaceId,
        WorkstationLayoutState layoutState,
        string? operatingContextKey = null,
        CancellationToken ct = default)
        => SaveWorkspaceLayoutStateAsync(workspaceId, layoutState, operatingContextKey, ct);

    public Task<WorkstationLayoutState?> GetWorkspaceLayoutStateAsync(
        string workspaceId,
        string? fundProfileId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        return WithStateLockAsync(async () =>
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var layoutKey = BuildWorkspaceLayoutKey(workspaceId, fundProfileId);
            return _workspaceLayouts.TryGetValue(layoutKey, out var layoutState)
                ? CloneWorkstationLayoutState(layoutState)
                : _activeWorkspace?.Id == workspaceId && _lastSession?.WorkstationLayout is not null
                    ? CloneWorkstationLayoutState(_lastSession.WorkstationLayout)
                    : null;
        }, ct);
    }

    public Task<WorkstationLayoutState?> GetWorkspaceLayoutStateForContextAsync(
        string workspaceId,
        string? operatingContextKey = null,
        CancellationToken ct = default)
        => GetWorkspaceLayoutStateAsync(workspaceId, operatingContextKey, ct);

    private readonly Dictionary<string, string> _dockLayouts = new(StringComparer.OrdinalIgnoreCase);
}
