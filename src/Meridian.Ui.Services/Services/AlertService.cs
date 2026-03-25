namespace Meridian.Ui.Services;

/// <summary>
/// Unified alert management service with classification, grouping, deduplication,
/// playbook-based remediation, and smart suppression.
/// Reduces alert fatigue by intelligently consolidating and prioritizing alerts.
/// </summary>
public sealed class AlertService
{
    private static readonly Lazy<AlertService> _instance = new(() => new AlertService());
    private readonly List<Alert> _activeAlerts = new();
    private readonly List<Alert> _resolvedAlerts = new();
    private readonly List<AlertSuppressionRule> _suppressionRules = new();
    private readonly Dictionary<string, AlertPlaybook> _playbooks = new();
    private readonly object _alertLock = new();
    private const int MaxResolvedAlerts = 500;
    private const int FlappingThreshold = 3;
    private static readonly TimeSpan FlappingWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TransientThreshold = TimeSpan.FromSeconds(30);

    public static AlertService Instance => _instance.Value;

    private AlertService()
    {
        RegisterDefaultPlaybooks();
    }

    /// <summary>
    /// Event raised when a new alert is created or an existing alert is updated.
    /// </summary>
    public event EventHandler<AlertEventArgs>? AlertRaised;

    /// <summary>
    /// Event raised when an alert is resolved.
    /// </summary>
    public event EventHandler<AlertEventArgs>? AlertResolved;

    /// <summary>
    /// Raises a new alert or updates an existing one if a matching alert is already active.
    /// Handles deduplication, grouping, and smart suppression automatically.
    /// </summary>
    public Alert RaiseAlert(
        string title,
        string description,
        AlertSeverity severity,
        BusinessImpact impact,
        string category,
        IReadOnlyList<string>? affectedResources = null,
        string? playbookId = null)
    {
        lock (_alertLock)
        {
            // Check for existing active alert with same title and category (deduplication)
            var existing = _activeAlerts.FirstOrDefault(a =>
                a.Title == title &&
                a.Category == category &&
                !a.IsResolved);

            if (existing != null)
            {
                // Update existing alert
                existing.LastOccurred = DateTime.UtcNow;
                existing.OccurrenceCount++;

                if (affectedResources != null)
                {
                    foreach (var resource in affectedResources)
                    {
                        if (!existing.AffectedResources.Contains(resource))
                        {
                            existing.AffectedResources.Add(resource);
                        }
                    }
                }

                // Escalate severity if it keeps recurring
                if (existing.OccurrenceCount > 5 && existing.Severity < AlertSeverity.Error)
                {
                    existing.Severity = AlertSeverity.Error;
                }

                AlertRaised?.Invoke(this, new AlertEventArgs { Alert = existing, IsUpdate = true });
                return existing;
            }

            // Check smart suppression
            if (ShouldSuppress(title, category, severity, impact))
            {
                return CreateSuppressedAlert(title, description, severity, impact, category);
            }

            // Create new alert
            var alert = new Alert
            {
                Id = $"alert-{Guid.NewGuid():N}",
                Title = title,
                Description = description,
                Severity = severity,
                Impact = impact,
                Category = category,
                FirstOccurred = DateTime.UtcNow,
                LastOccurred = DateTime.UtcNow,
                OccurrenceCount = 1,
                AffectedResources = affectedResources?.ToList() ?? new List<string>()
            };

            // Attach playbook if available
            if (playbookId != null && _playbooks.TryGetValue(playbookId, out var playbook))
            {
                alert.Playbook = playbook;
            }
            else
            {
                // Try to find a matching playbook by category
                alert.Playbook = FindPlaybookForCategory(category);
            }

            _activeAlerts.Add(alert);
            AlertRaised?.Invoke(this, new AlertEventArgs { Alert = alert, IsUpdate = false });
            return alert;
        }
    }

    /// <summary>
    /// Resolves an active alert by its ID.
    /// </summary>
    public void ResolveAlert(string alertId)
    {
        lock (_alertLock)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert == null)
                return;

            alert.IsResolved = true;
            alert.ResolvedAt = DateTime.UtcNow;

            _activeAlerts.Remove(alert);
            _resolvedAlerts.Insert(0, alert);

            // Trim resolved history
            while (_resolvedAlerts.Count > MaxResolvedAlerts)
            {
                _resolvedAlerts.RemoveAt(_resolvedAlerts.Count - 1);
            }

            AlertResolved?.Invoke(this, new AlertEventArgs { Alert = alert, IsUpdate = false });
        }
    }

    /// <summary>
    /// Snoozes an alert for the specified duration.
    /// </summary>
    public void SnoozeAlert(string alertId, TimeSpan duration)
    {
        lock (_alertLock)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert == null)
                return;

            alert.IsSnoozed = true;
            alert.SnoozedUntil = DateTime.UtcNow + duration;
        }
    }

    /// <summary>
    /// Adds a suppression rule to automatically suppress similar alerts.
    /// </summary>
    public void AddSuppressionRule(string category, string? titlePattern, TimeSpan duration)
    {
        lock (_alertLock)
        {
            _suppressionRules.Add(new AlertSuppressionRule
            {
                Category = category,
                TitlePattern = titlePattern,
                SuppressUntil = DateTime.UtcNow + duration,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Gets all active (non-resolved) alerts, grouped by category and sorted by severity.
    /// </summary>
    public IReadOnlyList<AlertGroup> GetGroupedAlerts()
    {
        lock (_alertLock)
        {
            // Remove expired snoozes
            var now = DateTime.UtcNow;
            foreach (var alert in _activeAlerts)
            {
                if (alert.IsSnoozed && alert.SnoozedUntil.HasValue && alert.SnoozedUntil.Value <= now)
                {
                    alert.IsSnoozed = false;
                    alert.SnoozedUntil = null;
                }
            }

            var visibleAlerts = _activeAlerts
                .Where(a => !a.IsSnoozed && !a.IsSuppressed)
                .ToList();

            return visibleAlerts
                .GroupBy(a => new { a.Category, a.Title, a.Impact })
                .Select(g => new AlertGroup
                {
                    Category = g.Key.Category,
                    Title = g.Key.Title,
                    Impact = g.Key.Impact,
                    Count = g.Sum(a => a.OccurrenceCount),
                    AffectedResources = g.SelectMany(a => a.AffectedResources).Distinct().ToList(),
                    FirstOccurred = g.Min(a => a.FirstOccurred),
                    LastOccurred = g.Max(a => a.LastOccurred),
                    RepresentativeAlert = g.OrderByDescending(a => a.Severity).First(),
                    Severity = g.Max(a => a.Severity)
                })
                .OrderByDescending(g => g.Severity)
                .ThenByDescending(g => g.LastOccurred)
                .ToList();
        }
    }

    /// <summary>
    /// Gets all active alerts without grouping.
    /// </summary>
    public IReadOnlyList<Alert> GetActiveAlerts()
    {
        lock (_alertLock)
        {
            return _activeAlerts
                .Where(a => !a.IsResolved)
                .OrderByDescending(a => a.Severity)
                .ThenByDescending(a => a.LastOccurred)
                .ToList();
        }
    }

    /// <summary>
    /// Gets resolved alert history.
    /// </summary>
    public IReadOnlyList<Alert> GetResolvedAlerts()
    {
        lock (_alertLock)
        {
            return _resolvedAlerts.ToList();
        }
    }

    /// <summary>
    /// Registers a remediation playbook.
    /// </summary>
    public void RegisterPlaybook(string id, AlertPlaybook playbook)
    {
        _playbooks[id] = playbook;
    }

    /// <summary>
    /// Gets the count of active alerts by severity.
    /// </summary>
    public AlertSummary GetSummary()
    {
        lock (_alertLock)
        {
            return new AlertSummary
            {
                CriticalCount = _activeAlerts.Count(a => a.Severity >= AlertSeverity.Critical && !a.IsSnoozed && !a.IsSuppressed),
                ErrorCount = _activeAlerts.Count(a => a.Severity == AlertSeverity.Error && !a.IsSnoozed && !a.IsSuppressed),
                WarningCount = _activeAlerts.Count(a => a.Severity == AlertSeverity.Warning && !a.IsSnoozed && !a.IsSuppressed),
                InfoCount = _activeAlerts.Count(a => a.Severity == AlertSeverity.Info && !a.IsSnoozed && !a.IsSuppressed),
                SnoozedCount = _activeAlerts.Count(a => a.IsSnoozed),
                SuppressedCount = _activeAlerts.Count(a => a.IsSuppressed),
                TotalActive = _activeAlerts.Count(a => !a.IsResolved)
            };
        }
    }

    private bool ShouldSuppress(string title, string category, AlertSeverity severity, BusinessImpact impact)
    {
        // Never suppress critical or emergency alerts
        if (severity >= AlertSeverity.Critical)
            return false;

        // Check user-defined suppression rules
        var now = DateTime.UtcNow;
        foreach (var rule in _suppressionRules)
        {
            if (rule.SuppressUntil > now &&
                rule.Category == category &&
                (rule.TitlePattern == null || title.Contains(rule.TitlePattern, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Suppress flapping alerts (same alert occurred and resolved 3+ times in the window)
        var flappingCount = _resolvedAlerts.Count(a =>
            a.Title == title &&
            a.Category == category &&
            a.ResolvedAt.HasValue &&
            a.ResolvedAt.Value > now - FlappingWindow);

        if (flappingCount >= FlappingThreshold)
            return true;

        // Suppress transient low-impact alerts
        if (impact == BusinessImpact.None)
        {
            var recentSame = _resolvedAlerts.FirstOrDefault(a =>
                a.Title == title &&
                a.Category == category &&
                a.ResolvedAt.HasValue &&
                a.ResolvedAt.Value > now - TransientThreshold);

            if (recentSame != null)
                return true;
        }

        return false;
    }

    private static Alert CreateSuppressedAlert(string title, string description, AlertSeverity severity, BusinessImpact impact, string category)
    {
        return new Alert
        {
            Id = $"suppressed-{Guid.NewGuid():N}",
            Title = title,
            Description = description,
            Severity = severity,
            Impact = impact,
            Category = category,
            FirstOccurred = DateTime.UtcNow,
            LastOccurred = DateTime.UtcNow,
            OccurrenceCount = 1,
            IsSuppressed = true
        };
    }

    private AlertPlaybook? FindPlaybookForCategory(string category)
    {
        return _playbooks.Values.FirstOrDefault(p =>
            p.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
    }

    private void RegisterDefaultPlaybooks()
    {
        RegisterPlaybook("connection-lost", new AlertPlaybook
        {
            Title = "Provider Connection Lost",
            WhatHappened = "Connection to the data provider was lost. No new data is being received.",
            Categories = new[] { "Connection", "Provider" },
            PossibleCauses = new[]
            {
                "Network connectivity issue",
                "API key expired or revoked",
                "Rate limit exceeded",
                "Provider service outage"
            },
            RemediationSteps = new[]
            {
                new RemediationStep(1, "Check network", "Verify internet connection is stable", "TestConnectivity", null),
                new RemediationStep(2, "Test API key", "Navigate to Settings and click Test Connection", "TestConnection", "Settings"),
                new RemediationStep(3, "Check rate limits", "View rate limit usage in Provider Health page", null, "ProviderHealth"),
                new RemediationStep(4, "Check provider status", "Visit the provider's status page for outage info", null, null),
                new RemediationStep(5, "Switch provider", "Configure failover to a backup provider", null, "DataSources")
            },
            WhatHappensIfIgnored = "No new data will be collected. Existing data is safe but growing stale."
        });

        RegisterPlaybook("data-gap", new AlertPlaybook
        {
            Title = "Data Gap Detected",
            WhatHappened = "Missing data was detected in the collection stream.",
            Categories = new[] { "DataQuality", "Gap" },
            PossibleCauses = new[]
            {
                "Provider downtime during collection",
                "Network interruption during streaming",
                "Rate limiting caused missed data",
                "Market was closed (holidays, weekends)"
            },
            RemediationSteps = new[]
            {
                new RemediationStep(1, "Review gap details", "Check the Data Quality page for gap timeframes", null, "DataQuality"),
                new RemediationStep(2, "Run targeted backfill", "Navigate to Backfill and fill the gap with historical data", "RunBackfill", "Backfill"),
                new RemediationStep(3, "Verify with backup provider", "Compare data across providers to confirm gap", null, "DataQuality"),
                new RemediationStep(4, "Schedule gap-fill", "Set up automatic gap detection and fill", null, "ScheduleManager")
            },
            WhatHappensIfIgnored = "Analysis and backtesting may produce inaccurate results due to missing data."
        });

        RegisterPlaybook("storage-warning", new AlertPlaybook
        {
            Title = "Storage Space Low",
            WhatHappened = "Available disk space is running low for data storage.",
            Categories = new[] { "Storage", "DiskSpace" },
            PossibleCauses = new[]
            {
                "Large amount of collected data without compression",
                "Archive retention policy not configured",
                "Multiple backup copies consuming space",
                "Other applications consuming disk space"
            },
            RemediationSteps = new[]
            {
                new RemediationStep(1, "Review storage usage", "Check the Storage page for usage breakdown by symbol", null, "Storage"),
                new RemediationStep(2, "Enable compression", "Navigate to Storage Optimization and enable archive compression", null, "StorageOptimization"),
                new RemediationStep(3, "Configure retention", "Set up automatic cleanup of old data in Admin Maintenance", null, "AdminMaintenance"),
                new RemediationStep(4, "Move to external storage", "Export older data to external drives or cloud storage", null, "AnalysisExport")
            },
            WhatHappensIfIgnored = "Collection may stop when disk is full, potentially losing real-time data."
        });

        RegisterPlaybook("rate-limit", new AlertPlaybook
        {
            Title = "Rate Limit Exceeded",
            WhatHappened = "API rate limit has been reached for a data provider.",
            Categories = new[] { "Provider", "RateLimit" },
            PossibleCauses = new[]
            {
                "Too many symbols being monitored simultaneously",
                "Backfill and live collection running concurrently",
                "Multiple applications sharing the same API key",
                "Provider tier limits reached"
            },
            RemediationSteps = new[]
            {
                new RemediationStep(1, "Reduce symbol count", "Remove less important symbols from the watchlist", null, "Symbols"),
                new RemediationStep(2, "Stagger requests", "Adjust polling intervals in Settings", null, "Settings"),
                new RemediationStep(3, "Upgrade provider tier", "Consider upgrading to a higher API tier", null, null),
                new RemediationStep(4, "Use backup provider", "Switch to an alternative provider temporarily", null, "DataSources")
            },
            WhatHappensIfIgnored = "Data collection will be throttled, potentially missing time-sensitive market data."
        });

        RegisterPlaybook("schema-mismatch", new AlertPlaybook
        {
            Title = "Schema Mismatch Detected",
            WhatHappened = "Data format has changed and may not be compatible with stored data.",
            Categories = new[] { "Schema", "Compatibility" },
            PossibleCauses = new[]
            {
                "Provider updated their API format",
                "Application update changed internal schema",
                "Imported data from a different version"
            },
            RemediationSteps = new[]
            {
                new RemediationStep(1, "Review schema changes", "Navigate to Diagnostics to see schema comparison", null, "Diagnostics"),
                new RemediationStep(2, "Run schema migration", "Use Admin Maintenance to migrate existing data", "RunMigration", "AdminMaintenance"),
                new RemediationStep(3, "Validate data integrity", "Run data validation on affected symbols", "ValidateData", "DataQuality"),
                new RemediationStep(4, "Contact support", "If migration fails, export data and reimport after resolution", null, null)
            },
            WhatHappensIfIgnored = "Queries and exports may fail or produce incorrect results."
        });
    }
}

/// <summary>
/// Alert severity levels.
/// </summary>
public enum AlertSeverity : byte
{
    Info,
    Warning,
    Error,
    Critical,
    Emergency
}

/// <summary>
/// Business impact of an alert.
/// </summary>
public enum BusinessImpact : byte
{
    None,
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// An alert raised by the system.
/// </summary>
public sealed class Alert
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public BusinessImpact Impact { get; init; }
    public string Category { get; init; } = string.Empty;
    public DateTime FirstOccurred { get; init; }
    public DateTime LastOccurred { get; set; }
    public int OccurrenceCount { get; set; }
    public List<string> AffectedResources { get; init; } = new();
    public AlertPlaybook? Playbook { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsSnoozed { get; set; }
    public DateTime? SnoozedUntil { get; set; }
    public bool IsSuppressed { get; set; }
}

/// <summary>
/// A group of related alerts consolidated for display.
/// </summary>
public sealed class AlertGroup
{
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public BusinessImpact Impact { get; init; }
    public AlertSeverity Severity { get; init; }
    public int Count { get; init; }
    public List<string> AffectedResources { get; init; } = new();
    public DateTime FirstOccurred { get; init; }
    public DateTime LastOccurred { get; init; }
    public Alert RepresentativeAlert { get; init; } = null!;
}

/// <summary>
/// Remediation playbook for an alert type.
/// </summary>
public sealed class AlertPlaybook
{
    public string Title { get; init; } = string.Empty;
    public string WhatHappened { get; init; } = string.Empty;
    public string[] Categories { get; init; } = Array.Empty<string>();
    public string[] PossibleCauses { get; init; } = Array.Empty<string>();
    public RemediationStep[] RemediationSteps { get; init; } = Array.Empty<RemediationStep>();
    public string WhatHappensIfIgnored { get; init; } = string.Empty;
}

/// <summary>
/// A step in a remediation playbook.
/// Supports optional action routing via ActionId and NavigationTarget.
/// </summary>
public sealed class RemediationStep
{
    public int Priority { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }

    /// <summary>
    /// Optional action identifier for automated execution (e.g., "TestConnection", "RunBackfill").
    /// When set, the UI can offer a one-click button to execute this step.
    /// </summary>
    public string? ActionId { get; init; }

    /// <summary>
    /// Optional navigation target page tag (e.g., "Settings", "Backfill", "ProviderHealth").
    /// When set, the remediation step can navigate the user directly to the relevant page.
    /// </summary>
    public string? NavigationTarget { get; init; }

    public RemediationStep(int priority, string title, string description)
    {
        Priority = priority;
        Title = title;
        Description = description;
    }

    public RemediationStep(int priority, string title, string description, string? actionId, string? navigationTarget)
    {
        Priority = priority;
        Title = title;
        Description = description;
        ActionId = actionId;
        NavigationTarget = navigationTarget;
    }
}

/// <summary>
/// Rule for suppressing specific types of alerts.
/// </summary>
public sealed class AlertSuppressionRule
{
    public string Category { get; init; } = string.Empty;
    public string? TitlePattern { get; init; }
    public DateTime SuppressUntil { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Summary of active alert counts by severity.
/// </summary>
public sealed class AlertSummary
{
    public int CriticalCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public int SnoozedCount { get; init; }
    public int SuppressedCount { get; init; }
    public int TotalActive { get; init; }
}

/// <summary>
/// Event args for alert events.
/// </summary>
public sealed class AlertEventArgs : EventArgs
{
    public Alert Alert { get; init; } = null!;
    public bool IsUpdate { get; init; }
}
