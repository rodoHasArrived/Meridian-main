using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring.Core;

/// <summary>
/// Runtime registry of Service Level Objectives (SLOs) per subsystem.
/// Provides programmatic SLO checking, compliance scoring, and alert threshold mapping.
/// </summary>
/// <remarks>
/// Addresses P0: "SLOs not consistently documented per subsystem — hard to calibrate alerts".
/// Makes SLO definitions available at runtime for automated compliance checking
/// and maps each SLO to the relevant alert rule and runbook section.
/// </remarks>
public sealed class SloDefinitionRegistry
{
    private readonly ConcurrentDictionary<string, SloDefinition> _definitions = new();
    private readonly ILogger _log = LoggingSetup.ForContext<SloDefinitionRegistry>();

    private static readonly Lazy<SloDefinitionRegistry> _instance = new(() =>
    {
        var registry = new SloDefinitionRegistry();
        registry.RegisterDefaults();
        return registry;
    });

    public static SloDefinitionRegistry Instance => _instance.Value;

    private SloDefinitionRegistry() { }

    /// <summary>
    /// Registers the default SLO definitions for all subsystems.
    /// </summary>
    private void RegisterDefaults()
    {
        // --- Ingestion Plane ---
        Register(new SloDefinition
        {
            Id = "SLO-ING-001",
            Subsystem = SloSubsystem.Ingestion,
            Name = "End-to-End Ingestion Latency",
            Description = "P95 end-to-end latency from provider to storage",
            MetricName = "mdc_provider_latency_seconds",
            TargetValue = 2.0,
            CriticalThreshold = 5.0,
            Unit = "seconds",
            MeasurementWindow = TimeSpan.FromMinutes(5),
            ErrorBudgetPercent = 0.1,
            ErrorBudgetWindow = TimeSpan.FromDays(30),
            AlertRuleName = "MeridianHighProviderLatency",
            RunbookSection = "docs/operations/operator-runbook.md#high-latency",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-ing-001"
        });

        Register(new SloDefinition
        {
            Id = "SLO-ING-002",
            Subsystem = SloSubsystem.Ingestion,
            Name = "Event Drop Rate",
            Description = "Percentage of events dropped due to pipeline capacity",
            MetricName = "mdc_pipeline_events_dropped_total",
            TargetValue = 0.001, // 0.1%
            CriticalThreshold = 0.01, // 1%
            Unit = "ratio",
            MeasurementWindow = TimeSpan.FromHours(24),
            ErrorBudgetPercent = 0.1,
            ErrorBudgetWindow = TimeSpan.FromDays(30),
            AlertRuleName = "MeridianHighDropRate",
            RunbookSection = "docs/operations/operator-runbook.md#high-drop-rate",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-ing-002"
        });

        // --- Data Completeness Plane ---
        Register(new SloDefinition
        {
            Id = "SLO-DC-001",
            Subsystem = SloSubsystem.DataCompleteness,
            Name = "Daily Data Completeness",
            Description = "Percentage of expected data points received per symbol per day",
            MetricName = "mdc_data_quality_score",
            TargetValue = 0.95, // 95%
            CriticalThreshold = 0.80, // 80%
            Unit = "ratio",
            MeasurementWindow = TimeSpan.FromHours(24),
            ErrorBudgetPercent = 5.0,
            ErrorBudgetWindow = TimeSpan.FromDays(30),
            AlertRuleName = "MeridianLowDataQuality",
            RunbookSection = "docs/operations/operator-runbook.md#low-data-quality",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-dc-001"
        });

        Register(new SloDefinition
        {
            Id = "SLO-DC-002",
            Subsystem = SloSubsystem.DataCompleteness,
            Name = "Maximum Data Gap Duration",
            Description = "No single gap longer than 5 minutes during market hours",
            MetricName = "mdc_data_gap_duration_seconds",
            TargetValue = 300.0, // 5 minutes max
            CriticalThreshold = 600.0, // 10 minutes
            Unit = "seconds",
            MeasurementWindow = TimeSpan.FromHours(1),
            AlertRuleName = "MeridianNoEventsPublished",
            RunbookSection = "docs/operations/operator-runbook.md#no-events",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-dc-002"
        });

        // --- Availability Plane ---
        Register(new SloDefinition
        {
            Id = "SLO-AV-001",
            Subsystem = SloSubsystem.Availability,
            Name = "Service Uptime",
            Description = "Application uptime during market hours",
            MetricName = "up",
            TargetValue = 0.999, // 99.9%
            CriticalThreshold = 0.995, // 99.5%
            Unit = "ratio",
            MeasurementWindow = TimeSpan.FromDays(30),
            ErrorBudgetPercent = 0.1,
            ErrorBudgetWindow = TimeSpan.FromDays(30),
            AlertRuleName = "MeridianDown",
            RunbookSection = "docs/operations/operator-runbook.md#application-down",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-av-001"
        });

        // --- Data Freshness Plane ---
        Register(new SloDefinition
        {
            Id = "SLO-DF-001",
            Subsystem = SloSubsystem.DataFreshness,
            Name = "Data Freshness P95",
            Description = "P95 data age since last event per symbol",
            MetricName = "mdc_data_freshness_age_seconds",
            TargetValue = 60.0, // 60 seconds
            CriticalThreshold = 300.0, // 5 minutes
            Unit = "seconds",
            MeasurementWindow = TimeSpan.FromMinutes(5),
            AlertRuleName = "MeridianDataFreshnessViolation",
            RunbookSection = "docs/operations/operator-runbook.md#freshness-sla-violation",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-df-001"
        });

        // --- Storage Plane ---
        Register(new SloDefinition
        {
            Id = "SLO-ST-001",
            Subsystem = SloSubsystem.Storage,
            Name = "Zero Write Errors",
            Description = "No storage write errors during normal operation",
            MetricName = "mdc_storage_write_errors_total",
            TargetValue = 0.0,
            CriticalThreshold = 1.0,
            Unit = "count",
            MeasurementWindow = TimeSpan.FromMinutes(5),
            AlertRuleName = "MeridianStorageWriteErrors",
            RunbookSection = "docs/operations/operator-runbook.md#storage-write-errors",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-st-001"
        });

        // --- Provider Connectivity Plane ---
        Register(new SloDefinition
        {
            Id = "SLO-PC-001",
            Subsystem = SloSubsystem.ProviderConnectivity,
            Name = "Provider Availability",
            Description = "At least one provider connected during market hours",
            MetricName = "mdc_provider_connected",
            TargetValue = 0.995, // 99.5%
            CriticalThreshold = 0.99, // 99%
            Unit = "ratio",
            MeasurementWindow = TimeSpan.FromDays(30),
            AlertRuleName = "MeridianProviderDisconnected",
            RunbookSection = "docs/operations/operator-runbook.md#provider-disconnected",
            SloDocSection = "docs/operations/service-level-objectives.md#slo-pc-001"
        });

        _log.Information("Registered {Count} default SLO definitions", _definitions.Count);
    }

    /// <summary>
    /// Registers or updates an SLO definition.
    /// </summary>
    public void Register(SloDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions[definition.Id] = definition;
    }

    /// <summary>
    /// Removes an SLO definition by its ID.
    /// </summary>
    /// <returns>True if the definition was removed; false if it was not found.</returns>
    public bool Unregister(string sloId)
    {
        return _definitions.TryRemove(sloId, out _);
    }

    /// <summary>
    /// Gets an SLO definition by its ID.
    /// </summary>
    public SloDefinition? Get(string sloId)
    {
        return _definitions.TryGetValue(sloId, out var def) ? def : null;
    }

    /// <summary>
    /// Gets all SLO definitions, optionally filtered by subsystem.
    /// </summary>
    public IReadOnlyList<SloDefinition> GetAll(SloSubsystem? subsystem = null)
    {
        var query = _definitions.Values.AsEnumerable();
        if (subsystem.HasValue)
            query = query.Where(d => d.Subsystem == subsystem.Value);

        return query.OrderBy(d => d.Id).ToList();
    }

    /// <summary>
    /// Evaluates a metric value against its SLO definition.
    /// </summary>
    public SloComplianceResult Evaluate(string sloId, double currentValue)
    {
        var definition = Get(sloId);
        if (definition == null)
            return new SloComplianceResult(sloId, SloComplianceState.Unknown, 0, "SLO not found");

        var state = EvaluateState(definition, currentValue);
        var score = CalculateScore(definition, currentValue);

        return new SloComplianceResult(sloId, state, score,
            $"{definition.Name}: {currentValue:F2} {definition.Unit} (target: {definition.TargetValue:F2})");
    }

    /// <summary>
    /// Gets a compliance dashboard for all SLOs.
    /// </summary>
    public SloComplianceDashboard GetDashboard()
    {
        var definitions = GetAll();
        return new SloComplianceDashboard
        {
            Timestamp = DateTimeOffset.UtcNow,
            TotalSlos = definitions.Count,
            Subsystems = definitions.GroupBy(d => d.Subsystem)
                .Select(g => new SloSubsystemSummary
                {
                    Subsystem = g.Key.ToString(),
                    SloCount = g.Count(),
                    Definitions = g.Select(d => new SloDefinitionSummary
                    {
                        Id = d.Id,
                        Name = d.Name,
                        MetricName = d.MetricName,
                        TargetValue = d.TargetValue,
                        CriticalThreshold = d.CriticalThreshold,
                        Unit = d.Unit,
                        AlertRuleName = d.AlertRuleName,
                        RunbookSection = d.RunbookSection
                    }).ToList()
                }).ToList()
        };
    }

    private static SloComplianceState EvaluateState(SloDefinition def, double value)
    {
        // For "lower is better" metrics (latency, errors, drop rate)
        if (def.Unit is "seconds" or "count" or "ratio" && def.TargetValue < def.CriticalThreshold)
        {
            if (value <= def.TargetValue)
                return SloComplianceState.Healthy;
            if (value <= def.CriticalThreshold)
                return SloComplianceState.Warning;
            return SloComplianceState.Violation;
        }

        // For "higher is better" metrics (uptime, completeness)
        if (value >= def.TargetValue)
            return SloComplianceState.Healthy;
        if (value >= def.CriticalThreshold)
            return SloComplianceState.Warning;
        return SloComplianceState.Violation;
    }

    private static double CalculateScore(SloDefinition def, double value)
    {
        if (def.TargetValue < def.CriticalThreshold)
        {
            // Lower is better
            if (value <= def.TargetValue)
                return 100.0;
            if (value >= def.CriticalThreshold)
                return 0.0;
            return 100.0 * (1.0 - (value - def.TargetValue) / (def.CriticalThreshold - def.TargetValue));
        }
        else
        {
            // Higher is better
            if (value >= def.TargetValue)
                return 100.0;
            if (value <= def.CriticalThreshold)
                return 0.0;
            return 100.0 * (value - def.CriticalThreshold) / (def.TargetValue - def.CriticalThreshold);
        }
    }
}

/// <summary>
/// Subsystem classification for SLO grouping.
/// </summary>
public enum SloSubsystem : byte
{
    Ingestion,
    DataCompleteness,
    Availability,
    DataFreshness,
    Storage,
    ProviderConnectivity
}

/// <summary>
/// Defines a single Service Level Objective with targets, thresholds, and linkage.
/// </summary>
public sealed class SloDefinition
{
    /// <summary>Unique SLO identifier (e.g., SLO-ING-001).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Subsystem this SLO belongs to.</summary>
    public SloSubsystem Subsystem { get; set; }

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of what this SLO measures.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Prometheus metric name used for measurement.</summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>Target value (the SLO threshold).</summary>
    public double TargetValue { get; set; }

    /// <summary>Critical threshold beyond which the SLO is violated.</summary>
    public double CriticalThreshold { get; set; }

    /// <summary>Unit of measurement.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Window over which the metric is measured.</summary>
    public TimeSpan MeasurementWindow { get; set; }

    /// <summary>Error budget as a percentage.</summary>
    public double ErrorBudgetPercent { get; set; }

    /// <summary>Error budget window.</summary>
    public TimeSpan ErrorBudgetWindow { get; set; }

    /// <summary>Prometheus alert rule name linked to this SLO.</summary>
    public string AlertRuleName { get; set; } = string.Empty;

    /// <summary>Link to the operator runbook section.</summary>
    public string RunbookSection { get; set; } = string.Empty;

    /// <summary>Link to the SLO documentation section.</summary>
    public string SloDocSection { get; set; } = string.Empty;
}

/// <summary>
/// Result of evaluating a metric against its SLO.
/// </summary>
public sealed record SloComplianceResult(
    string SloId,
    SloComplianceState State,
    double Score,
    string Message);

/// <summary>
/// SLO compliance state.
/// </summary>
public enum SloComplianceState : byte
{
    Healthy,
    Warning,
    Violation,
    Unknown
}

/// <summary>
/// Dashboard of all SLO definitions grouped by subsystem.
/// </summary>
public sealed class SloComplianceDashboard
{
    public DateTimeOffset Timestamp { get; set; }
    public int TotalSlos { get; set; }
    public List<SloSubsystemSummary> Subsystems { get; set; } = new();
}

/// <summary>
/// SLO summary for a subsystem.
/// </summary>
public sealed class SloSubsystemSummary
{
    public string Subsystem { get; set; } = string.Empty;
    public int SloCount { get; set; }
    public List<SloDefinitionSummary> Definitions { get; set; } = new();
}

/// <summary>
/// Compact SLO definition for API responses.
/// </summary>
public sealed class SloDefinitionSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public double TargetValue { get; set; }
    public double CriticalThreshold { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string AlertRuleName { get; set; } = string.Empty;
    public string RunbookSection { get; set; } = string.Empty;
}
