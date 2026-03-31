using System.Text.Json;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing interactive onboarding tours that guide new users
/// through key application features. Tracks tour progress and completion state.
/// </summary>
public sealed class OnboardingTourService
{
    private static readonly Lazy<OnboardingTourService> _instance = new(() => new OnboardingTourService());
    private readonly Dictionary<string, TourDefinition> _tours = new();
    private readonly HashSet<string> _completedTours = new();
    private readonly HashSet<string> _dismissedTours = new();
    private TourSession? _activeSession;

    private static readonly string ProgressFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian",
        "onboarding-progress.json");

    public static OnboardingTourService Instance => _instance.Value;

    private OnboardingTourService()
    {
        RegisterDefaultTours();
        _ = LoadProgressAsync();
    }

    /// <summary>
    /// Event raised when a tour step changes.
    /// </summary>
    public event EventHandler<TourStepEventArgs>? StepChanged;

    /// <summary>
    /// Event raised when a tour is completed.
    /// </summary>
    public event EventHandler<TourCompletedEventArgs>? TourCompleted;

    /// <summary>
    /// Gets whether any tour is currently active.
    /// </summary>
    public bool IsTourActive => _activeSession != null;

    /// <summary>
    /// Gets the currently active tour session, if any.
    /// </summary>
    public TourSession? ActiveSession => _activeSession;

    /// <summary>
    /// Gets all available tours with their completion status.
    /// </summary>
    public IReadOnlyList<TourInfo> GetAvailableTours()
    {
        return _tours.Values.Select(t => new TourInfo
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            StepCount = t.Steps.Count,
            IsCompleted = _completedTours.Contains(t.Id),
            IsDismissed = _dismissedTours.Contains(t.Id),
            Category = t.Category,
            EstimatedDurationMinutes = t.Steps.Count
        }).ToList();
    }

    /// <summary>
    /// Gets tours that should be shown to a first-time user for a specific page.
    /// </summary>
    public TourDefinition? GetTourForPage(string pageTag)
    {
        return _tours.Values.FirstOrDefault(t =>
            t.TriggerPage == pageTag &&
            !_completedTours.Contains(t.Id) &&
            !_dismissedTours.Contains(t.Id));
    }

    /// <summary>
    /// Starts a tour by its ID.
    /// </summary>
    public bool StartTour(string tourId)
    {
        if (_activeSession != null)
            return false;
        if (!_tours.TryGetValue(tourId, out var tour))
            return false;

        _activeSession = new TourSession
        {
            TourId = tourId,
            Tour = tour,
            CurrentStepIndex = 0,
            StartedAt = DateTime.UtcNow
        };

        var step = tour.Steps[0];
        StepChanged?.Invoke(this, new TourStepEventArgs
        {
            TourId = tourId,
            StepIndex = 0,
            TotalSteps = tour.Steps.Count,
            Step = step,
            IsFirst = true,
            IsLast = tour.Steps.Count == 1
        });

        return true;
    }

    /// <summary>
    /// Advances to the next step in the active tour.
    /// </summary>
    public bool NextStep()
    {
        if (_activeSession == null)
            return false;

        var nextIndex = _activeSession.CurrentStepIndex + 1;
        if (nextIndex >= _activeSession.Tour.Steps.Count)
        {
            CompleteTour();
            return false;
        }

        _activeSession.CurrentStepIndex = nextIndex;
        var step = _activeSession.Tour.Steps[nextIndex];

        StepChanged?.Invoke(this, new TourStepEventArgs
        {
            TourId = _activeSession.TourId,
            StepIndex = nextIndex,
            TotalSteps = _activeSession.Tour.Steps.Count,
            Step = step,
            IsFirst = false,
            IsLast = nextIndex == _activeSession.Tour.Steps.Count - 1
        });

        return true;
    }

    /// <summary>
    /// Goes back to the previous step in the active tour.
    /// </summary>
    public bool PreviousStep()
    {
        if (_activeSession == null)
            return false;
        if (_activeSession.CurrentStepIndex <= 0)
            return false;

        var prevIndex = _activeSession.CurrentStepIndex - 1;
        _activeSession.CurrentStepIndex = prevIndex;
        var step = _activeSession.Tour.Steps[prevIndex];

        StepChanged?.Invoke(this, new TourStepEventArgs
        {
            TourId = _activeSession.TourId,
            StepIndex = prevIndex,
            TotalSteps = _activeSession.Tour.Steps.Count,
            Step = step,
            IsFirst = prevIndex == 0,
            IsLast = false
        });

        return true;
    }

    /// <summary>
    /// Dismisses the active tour without completing it.
    /// </summary>
    public void DismissTour()
    {
        if (_activeSession == null)
            return;

        _dismissedTours.Add(_activeSession.TourId);
        _activeSession = null;
        _ = SaveProgressAsync();
    }

    /// <summary>
    /// Resets a dismissed tour so it can be shown again.
    /// </summary>
    public void ResetTour(string tourId)
    {
        _completedTours.Remove(tourId);
        _dismissedTours.Remove(tourId);
        _ = SaveProgressAsync();
    }

    /// <summary>
    /// Resets all tours for a fresh onboarding experience.
    /// </summary>
    public void ResetAllTours()
    {
        _completedTours.Clear();
        _dismissedTours.Clear();
        _ = SaveProgressAsync();
    }

    private void CompleteTour()
    {
        if (_activeSession == null)
            return;

        _completedTours.Add(_activeSession.TourId);

        TourCompleted?.Invoke(this, new TourCompletedEventArgs
        {
            TourId = _activeSession.TourId,
            StepsCompleted = _activeSession.Tour.Steps.Count,
            Duration = DateTime.UtcNow - _activeSession.StartedAt
        });

        _activeSession = null;
        _ = SaveProgressAsync();
    }

    private async Task LoadProgressAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(ProgressFilePath))
                return;

            var json = await File.ReadAllTextAsync(ProgressFilePath);
            var progress = JsonSerializer.Deserialize<OnboardingProgress>(json);
            if (progress == null)
                return;

            foreach (var id in progress.CompletedTours)
            {
                _completedTours.Add(id);
            }

            foreach (var id in progress.DismissedTours)
            {
                _dismissedTours.Add(id);
            }
        }
        catch (Exception ex)
        {
        }
    }

    private async Task SaveProgressAsync(CancellationToken ct = default)
    {
        try
        {
            var progress = new OnboardingProgress
            {
                CompletedTours = _completedTours.ToList(),
                DismissedTours = _dismissedTours.ToList(),
                SavedAt = DateTime.UtcNow
            };

            var dir = Path.GetDirectoryName(ProgressFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(progress, DesktopJsonOptions.PrettyPrint);
            await File.WriteAllTextAsync(ProgressFilePath, json);
        }
        catch (Exception ex)
        {
        }
    }

    private void RegisterDefaultTours()
    {
        _tours["welcome"] = new TourDefinition
        {
            Id = "welcome",
            Title = "Welcome to Meridian",
            Description = "Learn the basics of the application in a quick overview.",
            Category = TourCategory.GettingStarted,
            TriggerPage = "Dashboard",
            Steps = new List<TourStep>
            {
                new()
                {
                    Title = "Welcome!",
                    Content = "Meridian captures real-time and historical market data from multiple providers. Let's take a quick tour of the key features.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                },
                new()
                {
                    Title = "Dashboard Overview",
                    Content = "The Dashboard shows your collector status, active connections, data freshness, and recent activity at a glance.",
                    TargetElement = "DashboardPage",
                    Placement = TooltipPlacement.Center
                },
                new()
                {
                    Title = "Navigation",
                    Content = "Use the sidebar to navigate between pages. You can also use Ctrl+K to open the Command Palette for quick navigation.",
                    TargetElement = "NavigationSidebar",
                    Placement = TooltipPlacement.Right
                },
                new()
                {
                    Title = "Keyboard Shortcuts",
                    Content = "Press Ctrl+D for Dashboard, Ctrl+B for Backfill, Ctrl+Y for Symbols, and F1 for Help. View all shortcuts in Settings.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                }
            }
        };

        _tours["data-collection"] = new TourDefinition
        {
            Id = "data-collection",
            Title = "Setting Up Data Collection",
            Description = "Learn how to configure providers and start collecting market data.",
            Category = TourCategory.GettingStarted,
            TriggerPage = "Provider",
            Steps = new List<TourStep>
            {
                new()
                {
                    Title = "Choose a Provider",
                    Content = "Meridian supports multiple data providers including Alpaca, Polygon, Interactive Brokers, and StockSharp. Select a provider to configure.",
                    TargetElement = "ProviderList",
                    Placement = TooltipPlacement.Right
                },
                new()
                {
                    Title = "Configure Credentials",
                    Content = "Enter your API credentials. They are securely stored using environment variables and never logged.",
                    TargetElement = "CredentialForm",
                    Placement = TooltipPlacement.Left
                },
                new()
                {
                    Title = "Test Connection",
                    Content = "Use the Test Connection button to verify your credentials work before starting collection.",
                    TargetElement = "TestConnectionButton",
                    Placement = TooltipPlacement.Bottom
                },
                new()
                {
                    Title = "Add Symbols",
                    Content = "Navigate to Symbols (Ctrl+Y) to add the tickers you want to monitor. You can search across multiple exchanges.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                }
            }
        };

        _tours["backfill"] = new TourDefinition
        {
            Id = "backfill",
            Title = "Historical Data Backfill",
            Description = "Learn how to download historical data to fill gaps.",
            Category = TourCategory.DataManagement,
            TriggerPage = "Backfill",
            Steps = new List<TourStep>
            {
                new()
                {
                    Title = "Backfill Overview",
                    Content = "Backfill downloads historical market data from providers. It can fill gaps in your data and seed initial datasets.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                },
                new()
                {
                    Title = "Select Provider",
                    Content = "Choose which historical data provider to use. Different providers offer different data types and date ranges.",
                    TargetElement = "ProviderSelector",
                    Placement = TooltipPlacement.Bottom
                },
                new()
                {
                    Title = "Configure Date Range",
                    Content = "Set the start and end dates for the data you want. Backfill will automatically handle pagination and rate limits.",
                    TargetElement = "DateRangeSelector",
                    Placement = TooltipPlacement.Bottom
                },
                new()
                {
                    Title = "Resume & Recovery",
                    Content = "If a backfill is interrupted, you can resume from where it left off. Progress is saved automatically.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                }
            }
        };

        _tours["data-quality"] = new TourDefinition
        {
            Id = "data-quality",
            Title = "Data Quality Monitoring",
            Description = "Understand how data quality is tracked and how to resolve issues.",
            Category = TourCategory.Monitoring,
            TriggerPage = "DataQuality",
            Steps = new List<TourStep>
            {
                new()
                {
                    Title = "Quality Dashboard",
                    Content = "The Data Quality page shows completeness scores, gap analysis, and anomaly detection for all monitored symbols.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                },
                new()
                {
                    Title = "Completeness Scores",
                    Content = "Each symbol has a completeness percentage showing how much of the expected data has been received.",
                    TargetElement = "CompletenessPanel",
                    Placement = TooltipPlacement.Right
                },
                new()
                {
                    Title = "Gap Analysis",
                    Content = "Gaps show periods of missing data. You can click on a gap to run a targeted backfill to fill it.",
                    TargetElement = "GapAnalysisPanel",
                    Placement = TooltipPlacement.Right
                }
            }
        };

        _tours["export"] = new TourDefinition
        {
            Id = "export",
            Title = "Exporting Data",
            Description = "Learn how to export collected data for analysis.",
            Category = TourCategory.DataManagement,
            TriggerPage = "DataExport",
            Steps = new List<TourStep>
            {
                new()
                {
                    Title = "Export Options",
                    Content = "Export your collected data to CSV, Parquet, Arrow, or XLSX formats for use in Python, R, or Excel.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                },
                new()
                {
                    Title = "Export Presets",
                    Content = "Use built-in presets for Python/Pandas, R, QuantConnect, Excel, or PostgreSQL. Each includes optimized settings and helper scripts.",
                    TargetElement = "PresetSelector",
                    Placement = TooltipPlacement.Bottom
                },
                new()
                {
                    Title = "Custom Exports",
                    Content = "Create custom export configurations with specific date ranges, symbols, and output formats. Save them as presets for reuse.",
                    TargetElement = null,
                    Placement = TooltipPlacement.Center
                }
            }
        };
    }

    private sealed class OnboardingProgress
    {
        public List<string> CompletedTours { get; set; } = new();
        public List<string> DismissedTours { get; set; } = new();
        public DateTime SavedAt { get; set; }
    }
}

/// <summary>
/// A tour definition with steps.
/// </summary>
public sealed class TourDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TourCategory Category { get; init; }
    public string? TriggerPage { get; init; }
    public List<TourStep> Steps { get; init; } = new();
}

/// <summary>
/// A single step in a tour.
/// </summary>
public sealed class TourStep
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? TargetElement { get; init; }
    public TooltipPlacement Placement { get; init; }
}

/// <summary>
/// Tour category for organization.
/// </summary>
public enum TourCategory : byte
{
    GettingStarted,
    DataManagement,
    Monitoring,
    Advanced
}

/// <summary>
/// Tooltip placement relative to the target element.
/// </summary>
public enum TooltipPlacement : byte
{
    Top,
    Bottom,
    Left,
    Right,
    Center
}

/// <summary>
/// Active tour session state.
/// </summary>
public sealed class TourSession
{
    public string TourId { get; init; } = string.Empty;
    public TourDefinition Tour { get; init; } = null!;
    public int CurrentStepIndex { get; set; }
    public DateTime StartedAt { get; init; }
}

/// <summary>
/// Tour information for listing.
/// </summary>
public sealed class TourInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int StepCount { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsDismissed { get; init; }
    public TourCategory Category { get; init; }
    public int EstimatedDurationMinutes { get; init; }
}

/// <summary>
/// Event args for tour step changes.
/// </summary>
public sealed class TourStepEventArgs : EventArgs
{
    public string TourId { get; init; } = string.Empty;
    public int StepIndex { get; init; }
    public int TotalSteps { get; init; }
    public TourStep Step { get; init; } = null!;
    public bool IsFirst { get; init; }
    public bool IsLast { get; init; }
}

/// <summary>
/// Event args for tour completion.
/// </summary>
public sealed class TourCompletedEventArgs : EventArgs
{
    public string TourId { get; init; } = string.Empty;
    public int StepsCompleted { get; init; }
    public TimeSpan Duration { get; init; }
}
