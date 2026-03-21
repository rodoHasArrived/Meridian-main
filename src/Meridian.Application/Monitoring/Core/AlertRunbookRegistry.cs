using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring.Core;

/// <summary>
/// Provides explicit mapping between alert rules and operator runbook sections.
/// Each entry includes the alert name, severity, runbook URL, probable causes,
/// and immediate mitigation steps.
/// </summary>
/// <remarks>
/// Addresses P0: "Alert-to-runbook linkage is implicit — slower incident triage".
/// This registry makes the linkage programmatic so that alerts dispatched via
/// <see cref="AlertDispatcher"/> can automatically include remediation guidance.
/// </remarks>
public sealed class AlertRunbookRegistry
{
    private readonly ConcurrentDictionary<string, AlertRunbookEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _log = LoggingSetup.ForContext<AlertRunbookRegistry>();

    private static readonly Lazy<AlertRunbookRegistry> _instance = new(() =>
    {
        var registry = new AlertRunbookRegistry();
        registry.RegisterDefaults();
        return registry;
    });

    public static AlertRunbookRegistry Instance => _instance.Value;

    private AlertRunbookRegistry() { }

    /// <summary>
    /// Registers the default alert-to-runbook mappings matching deploy/monitoring/alert-rules.yml.
    /// </summary>
    private void RegisterDefaults()
    {
        // Health alerts
        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianDown",
            Severity = "critical",
            IncidentPriority = "P1",
            Summary = "Meridian is down",
            RunbookUrl = "docs/operations/operator-runbook.md#application-down",
            SloId = "SLO-AV-001",
            ProbableCauses = new[]
            {
                "Process crashed",
                "Host unreachable",
                "Port blocked by firewall",
                "OOM killed by OS"
            },
            ImmediateActions = new[]
            {
                "Check process status: systemctl status meridian",
                "Check system logs: journalctl -u meridian",
                "Restart service if crashed",
                "Check disk space and memory usage"
            },
            RollbackCriteria = "Health endpoint returns 200 within 30 seconds of restart"
        });

        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianUnhealthy",
            Severity = "warning",
            IncidentPriority = "P2",
            Summary = "Meridian reports unhealthy",
            RunbookUrl = "docs/operations/operator-runbook.md#unhealthy-status",
            ProbableCauses = new[]
            {
                "Provider disconnected",
                "Storage write failures",
                "Pipeline backpressure",
                "Dependency timeout"
            },
            ImmediateActions = new[]
            {
                "Check /health/detailed for specific failing checks",
                "Review recent error logs",
                "Verify provider connectivity"
            }
        });

        // Pipeline alerts
        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianHighDropRate",
            Severity = "warning",
            IncidentPriority = "P2",
            Summary = "High pipeline event drop rate",
            RunbookUrl = "docs/operations/operator-runbook.md#high-drop-rate",
            SloId = "SLO-ING-002",
            ProbableCauses = new[]
            {
                "Storage sink blocking (slow disk I/O)",
                "Pipeline queue at capacity",
                "Too many subscriptions for processing capacity"
            },
            ImmediateActions = new[]
            {
                "Check /api/backpressure for queue utilization",
                "Monitor disk I/O latency",
                "Consider reducing symbol subscriptions",
                "Check EventPipeline channel capacity"
            }
        });

        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianPipelineBackpressure",
            Severity = "warning",
            IncidentPriority = "P2",
            Summary = "Pipeline queue near capacity",
            RunbookUrl = "docs/operations/operator-runbook.md#pipeline-backpressure",
            ProbableCauses = new[]
            {
                "Consumer slower than producer",
                "Storage write latency",
                "Burst of market data events"
            },
            ImmediateActions = new[]
            {
                "Check storage write latency metrics",
                "Verify disk health",
                "Consider pausing non-critical subscriptions"
            }
        });

        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianNoEventsPublished",
            Severity = "warning",
            IncidentPriority = "P2",
            Summary = "No events published in 10 minutes",
            RunbookUrl = "docs/operations/operator-runbook.md#no-events",
            SloId = "SLO-DC-002",
            ProbableCauses = new[]
            {
                "All providers disconnected",
                "Market closed (check TradingCalendar)",
                "Subscription failure",
                "Network outage"
            },
            ImmediateActions = new[]
            {
                "Check /api/providers/status for connection state",
                "Verify market hours via TradingCalendar",
                "Test provider connectivity"
            }
        });

        // Provider alerts
        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianProviderDisconnected",
            Severity = "warning",
            IncidentPriority = "P2",
            Summary = "Data provider disconnected",
            RunbookUrl = "docs/operations/operator-runbook.md#provider-disconnected",
            SloId = "SLO-PC-001",
            ProbableCauses = new[]
            {
                "API key expired or invalid",
                "Provider service outage",
                "Network connectivity issue",
                "Rate limit exceeded"
            },
            ImmediateActions = new[]
            {
                "Check provider status page",
                "Verify API credentials",
                "Check rate limit counters",
                "Trigger manual reconnect or failover"
            }
        });

        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianHighProviderLatency",
            Severity = "warning",
            IncidentPriority = "P3",
            Summary = "High latency on data provider",
            RunbookUrl = "docs/operations/operator-runbook.md#high-latency",
            SloId = "SLO-ING-001",
            ProbableCauses = new[]
            {
                "Provider under load",
                "Network congestion",
                "DNS resolution delays",
                "WebSocket reconnection overhead"
            },
            ImmediateActions = new[]
            {
                "Check provider latency trends at /api/providers/latency",
                "Consider switching to backup provider",
                "Verify network path quality"
            }
        });

        // Storage alerts
        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianStorageWriteErrors",
            Severity = "critical",
            IncidentPriority = "P1",
            Summary = "Storage write errors detected",
            RunbookUrl = "docs/operations/operator-runbook.md#storage-write-errors",
            SloId = "SLO-ST-001",
            ProbableCauses = new[]
            {
                "Disk full",
                "Filesystem permissions issue",
                "I/O errors on disk",
                "WAL corruption",
                "Storage path misconfigured"
            },
            ImmediateActions = new[]
            {
                "Check disk space immediately",
                "Verify storage path permissions",
                "Check WAL integrity",
                "Review storage error logs for root cause"
            },
            RollbackCriteria = "Write error rate drops to 0 for 5 consecutive minutes"
        });

        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianLowDataQuality",
            Severity = "warning",
            IncidentPriority = "P3",
            Summary = "Low data quality score",
            RunbookUrl = "docs/operations/operator-runbook.md#low-data-quality",
            SloId = "SLO-DC-001",
            ProbableCauses = new[]
            {
                "Data gaps from provider outage",
                "Stale quotes",
                "Sequence errors",
                "Bad tick data from provider"
            },
            ImmediateActions = new[]
            {
                "Check /api/quality/gaps for gap analysis",
                "Compare across providers at /api/quality/comparison",
                "Consider triggering gap-fill backfill"
            }
        });

        // SLA alerts
        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianDataFreshnessViolation",
            Severity = "critical",
            IncidentPriority = "P1",
            Summary = "Data freshness SLA violation",
            RunbookUrl = "docs/operations/operator-runbook.md#freshness-sla-violation",
            SloId = "SLO-DF-001",
            ProbableCauses = new[]
            {
                "Provider stream stalled",
                "Subscription dropped",
                "Processing pipeline blocked"
            },
            ImmediateActions = new[]
            {
                "Check provider connection status",
                "Verify subscription is active",
                "Check pipeline queue utilization",
                "Re-subscribe if needed"
            },
            RollbackCriteria = "Freshness age drops below configured threshold for the symbol"
        });

        Register(new AlertRunbookEntry
        {
            AlertName = "MeridianSlaComplianceLow",
            Severity = "warning",
            IncidentPriority = "P2",
            Summary = "SLA compliance below 95%",
            RunbookUrl = "docs/operations/operator-runbook.md#sla-compliance",
            ProbableCauses = new[]
            {
                "Multiple provider degradations",
                "Systematic processing delays",
                "Infrastructure issues"
            },
            ImmediateActions = new[]
            {
                "Review /api/sla/violations for affected symbols",
                "Check /api/sla/metrics for trends",
                "Evaluate provider health across all active providers"
            }
        });

        _log.Information("Registered {Count} alert-runbook entries", _entries.Count);
    }

    /// <summary>
    /// Registers or updates an alert-runbook entry.
    /// </summary>
    public void Register(AlertRunbookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries[entry.AlertName] = entry;
    }

    /// <summary>
    /// Gets the runbook entry for an alert by name.
    /// </summary>
    public AlertRunbookEntry? GetByAlertName(string alertName)
    {
        return _entries.TryGetValue(alertName, out var entry) ? entry : null;
    }

    /// <summary>
    /// Gets the runbook URL for an alert, or null if not mapped.
    /// </summary>
    public string? GetRunbookUrl(string alertName)
    {
        return _entries.TryGetValue(alertName, out var entry) ? entry.RunbookUrl : null;
    }

    /// <summary>
    /// Gets all registered entries.
    /// </summary>
    public IReadOnlyList<AlertRunbookEntry> GetAll()
    {
        return _entries.Values.OrderBy(e => e.IncidentPriority).ThenBy(e => e.AlertName).ToList();
    }

    /// <summary>
    /// Gets entries filtered by severity.
    /// </summary>
    public IReadOnlyList<AlertRunbookEntry> GetBySeverity(string severity)
    {
        return _entries.Values
            .Where(e => string.Equals(e.Severity, severity, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.AlertName)
            .ToList();
    }

    /// <summary>
    /// Enriches a <see cref="MonitoringAlert"/> with runbook information by returning
    /// a copy with augmented context.
    /// </summary>
    public MonitoringAlert EnrichWithRunbook(MonitoringAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var entry = GetByAlertName(alert.Title);
        if (entry == null)
            return alert;

        // Build augmented context with runbook info
        var augmented = new Dictionary<string, object>(
            alert.Context ?? new Dictionary<string, object>())
        {
            ["runbookUrl"] = entry.RunbookUrl,
            ["incidentPriority"] = entry.IncidentPriority
        };

        if (entry.SloId != null)
            augmented["sloId"] = entry.SloId;
        if (entry.RollbackCriteria != null)
            augmented["rollbackCriteria"] = entry.RollbackCriteria;

        return alert with { Context = augmented };
    }
}

/// <summary>
/// Maps a Prometheus alert rule to its operator runbook entry.
/// </summary>
public sealed class AlertRunbookEntry
{
    /// <summary>Alert rule name (matches Prometheus alert name).</summary>
    public string AlertName { get; set; } = string.Empty;

    /// <summary>Alert severity (critical, warning, info).</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Incident priority (P1, P2, P3).</summary>
    public string IncidentPriority { get; set; } = string.Empty;

    /// <summary>Short summary of the alert condition.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Path to the runbook section for this alert.</summary>
    public string RunbookUrl { get; set; } = string.Empty;

    /// <summary>SLO ID this alert maps to, if any.</summary>
    public string? SloId { get; set; }

    /// <summary>Probable causes of this alert condition.</summary>
    public string[] ProbableCauses { get; set; } = Array.Empty<string>();

    /// <summary>Immediate mitigation actions.</summary>
    public string[] ImmediateActions { get; set; } = Array.Empty<string>();

    /// <summary>Criteria for determining when a rollback is needed.</summary>
    public string? RollbackCriteria { get; set; }
}
