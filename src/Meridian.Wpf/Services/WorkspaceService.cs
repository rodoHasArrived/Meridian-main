using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UiServices = Meridian.Ui.Services;
using WorkspaceTemplate = Meridian.Ui.Services.WorkspaceTemplate;
using SessionState = Meridian.Ui.Services.SessionState;
using WorkspaceCategory = Meridian.Ui.Services.WorkspaceCategory;
using WorkspacePage = Meridian.Ui.Services.WorkspacePage;
using WidgetPosition = Meridian.Ui.Services.WidgetPosition;
using WorkspaceEventArgs = Meridian.Ui.Services.WorkspaceEventArgs;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for managing workspace templates and session restore.
/// Uses file-based JSON storage (WPF replacement for ApplicationData.LocalSettings).
/// </summary>
public sealed class WorkspaceService
{
    private static readonly Lazy<WorkspaceService> _instance = new(() => new WorkspaceService());

    private const string WorkspacesFileName = "workspace-data.json";

    private WorkspaceTemplate? _activeWorkspace;
    private SessionState? _lastSession;
    private readonly List<WorkspaceTemplate> _workspaces = new();

    public static WorkspaceService Instance => _instance.Value;

    private WorkspaceService()
    {
        _ = LoadWorkspacesAsync();
    }

    public WorkspaceTemplate? ActiveWorkspace => _activeWorkspace;
    public SessionState? LastSession => _lastSession;
    public IReadOnlyList<WorkspaceTemplate> Workspaces => _workspaces.AsReadOnly();

    private static string GetSettingsFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, WorkspacesFileName);
    }

    /// <summary>
    /// Persisted workspace data container.
    /// </summary>
    private sealed class WorkspaceData
    {
        public List<WorkspaceTemplate> Workspaces { get; set; } = new();
        public string? ActiveWorkspaceId { get; set; }
        public SessionState? LastSession { get; set; }
    }

    public async Task LoadWorkspacesAsync(CancellationToken ct = default)
    {
        try
        {
            var filePath = GetSettingsFilePath();
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                var data = JsonSerializer.Deserialize<WorkspaceData>(json);

                if (data != null)
                {
                    if (data.Workspaces.Count > 0)
                    {
                        _workspaces.Clear();
                        _workspaces.AddRange(data.Workspaces);
                    }

                    if (data.ActiveWorkspaceId != null)
                    {
                        _activeWorkspace = _workspaces.FirstOrDefault(w => w.Id == data.ActiveWorkspaceId);
                    }

                    _lastSession = data.LastSession;
                }
            }

            if (_workspaces.Count == 0)
            {
                _workspaces.AddRange(GetDefaultWorkspaces());
                await SaveWorkspacesAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceService] Error loading workspaces: {ex.Message}");
        }
    }

    public async Task SaveWorkspacesAsync(CancellationToken ct = default)
    {
        try
        {
            var data = new WorkspaceData
            {
                Workspaces = _workspaces.ToList(),
                ActiveWorkspaceId = _activeWorkspace?.Id,
                LastSession = _lastSession
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(GetSettingsFilePath(), json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceService] Error saving workspaces: {ex.Message}");
        }
    }

    public async Task<WorkspaceTemplate> CreateWorkspaceAsync(string name, string description, WorkspaceCategory category, CancellationToken ct = default)
    {
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
        await SaveWorkspacesAsync();

        WorkspaceCreated?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
        return workspace;
    }

    public async Task UpdateWorkspaceAsync(WorkspaceTemplate workspace, CancellationToken ct = default)
    {
        var existing = _workspaces.FirstOrDefault(w => w.Id == workspace.Id);
        if (existing != null)
        {
            var index = _workspaces.IndexOf(existing);
            workspace.UpdatedAt = DateTime.UtcNow;
            _workspaces[index] = workspace;
            await SaveWorkspacesAsync();

            WorkspaceUpdated?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
        }
    }

    public async Task DeleteWorkspaceAsync(string workspaceId, CancellationToken ct = default)
    {
        var workspace = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (workspace != null && !workspace.IsBuiltIn)
        {
            _workspaces.Remove(workspace);
            await SaveWorkspacesAsync();

            WorkspaceDeleted?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
        }
    }

    public async Task ActivateWorkspaceAsync(string workspaceId, CancellationToken ct = default)
    {
        var workspace = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (workspace != null)
        {
            _activeWorkspace = workspace;
            await SaveWorkspacesAsync();

            WorkspaceActivated?.Invoke(this, new WorkspaceEventArgs { Workspace = workspace });
        }
    }

    public async Task<WorkspaceTemplate> CaptureCurrentStateAsync(string name, string description, CancellationToken ct = default)
    {
        var workspace = new WorkspaceTemplate
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = description,
            Category = WorkspaceCategory.Custom,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Pages = _lastSession?.OpenPages ?? new List<WorkspacePage>(),
            WidgetLayout = _lastSession?.WidgetLayout ?? new Dictionary<string, WidgetPosition>(),
            Filters = _lastSession?.ActiveFilters ?? new Dictionary<string, string>(),
            WindowBounds = _lastSession?.WindowBounds
        };

        _workspaces.Add(workspace);
        await SaveWorkspacesAsync();

        return workspace;
    }

    public async Task SaveSessionStateAsync(SessionState state, CancellationToken ct = default)
    {
        try
        {
            _lastSession = state;
            state.SavedAt = DateTime.UtcNow;
            await SaveWorkspacesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceService] Error saving session state: {ex.Message}");
        }
    }

    public SessionState? GetLastSessionState()
    {
        return _lastSession;
    }

    /// <summary>
    /// Saves a single named filter value for a page into the active session's
    /// <see cref="SessionState.ActiveFilters"/> dictionary.
    /// Uses the composite key format <c>&quot;{pageTag}.{filterKey}&quot;</c>.
    /// Passing <see langword="null"/> as <paramref name="value"/> removes the entry.
    /// </summary>
    public void UpdatePageFilterState(string pageTag, string filterKey, string? value)
    {
        _lastSession ??= new SessionState();
        var key = $"{pageTag}.{filterKey}";
        if (value is null)
            _lastSession.ActiveFilters.Remove(key);
        else
            _lastSession.ActiveFilters[key] = value;
    }

    /// <summary>
    /// Retrieves a previously saved filter value for a page from the active session.
    /// Returns <see langword="null"/> when no value has been stored.
    /// </summary>
    public string? GetPageFilterState(string pageTag, string filterKey)
    {
        if (_lastSession is null) return null;
        var key = $"{pageTag}.{filterKey}";
        return _lastSession.ActiveFilters.TryGetValue(key, out var value) ? value : null;
    }

    public Task<string> ExportWorkspaceAsync(string workspaceId)
    {
        var workspace = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (workspace != null)
        {
            return Task.FromResult(JsonSerializer.Serialize(workspace, new JsonSerializerOptions { WriteIndented = true }));
        }
        return Task.FromResult(string.Empty);
    }

    public async Task<WorkspaceTemplate?> ImportWorkspaceAsync(string json, CancellationToken ct = default)
    {
        try
        {
            var workspace = JsonSerializer.Deserialize<WorkspaceTemplate>(json);
            if (workspace != null)
            {
                workspace.Id = Guid.NewGuid().ToString();
                workspace.IsBuiltIn = false;
                workspace.CreatedAt = DateTime.UtcNow;
                workspace.UpdatedAt = DateTime.UtcNow;

                _workspaces.Add(workspace);
                await SaveWorkspacesAsync();

                return workspace;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceService] Error importing workspace: {ex.Message}");
        }
        return null;
    }

    private static List<WorkspaceTemplate> GetDefaultWorkspaces()
    {
        return new List<WorkspaceTemplate>
        {
            new WorkspaceTemplate
            {
                Id = "monitoring",
                Name = "Monitoring",
                Description = "Real-time monitoring and data quality overview",
                Category = WorkspaceCategory.Monitoring,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "Dashboard", Title = "Dashboard", IsDefault = true },
                    new WorkspacePage { PageTag = "DataQuality", Title = "Data Quality" },
                    new WorkspacePage { PageTag = "SystemHealth", Title = "System Health" },
                    new WorkspacePage { PageTag = "LiveData", Title = "Live Data" }
                }
            },
            new WorkspaceTemplate
            {
                Id = "backfill-ops",
                Name = "Backfill Operations",
                Description = "Historical data backfill and gap filling",
                Category = WorkspaceCategory.Backfill,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "Backfill", Title = "Backfill", IsDefault = true },
                    new WorkspacePage { PageTag = "DataCalendar", Title = "Data Calendar" },
                    new WorkspacePage { PageTag = "ArchiveHealth", Title = "Archive Health" },
                    new WorkspacePage { PageTag = "Schedules", Title = "Schedules" }
                }
            },
            new WorkspaceTemplate
            {
                Id = "storage-admin",
                Name = "Storage Admin",
                Description = "Storage management and maintenance",
                Category = WorkspaceCategory.Storage,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "Storage", Title = "Storage", IsDefault = true },
                    new WorkspacePage { PageTag = "AdminMaintenance", Title = "Maintenance" },
                    new WorkspacePage { PageTag = "PackageManager", Title = "Packages" },
                    new WorkspacePage { PageTag = "DataBrowser", Title = "Data Browser" }
                }
            },
            new WorkspaceTemplate
            {
                Id = "analysis-export",
                Name = "Analysis & Export",
                Description = "Data analysis and export workflows",
                Category = WorkspaceCategory.Analysis,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = new List<WorkspacePage>
                {
                    new WorkspacePage { PageTag = "AdvancedAnalytics", Title = "Analytics", IsDefault = true },
                    new WorkspacePage { PageTag = "AnalysisExportWizard", Title = "Export Wizard" },
                    new WorkspacePage { PageTag = "DataExport", Title = "Data Export" },
                    new WorkspacePage { PageTag = "LeanIntegration", Title = "Lean Integration" }
                }
            }
        };
    }

    public event EventHandler<WorkspaceEventArgs>? WorkspaceCreated;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceUpdated;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceDeleted;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceActivated;
}
