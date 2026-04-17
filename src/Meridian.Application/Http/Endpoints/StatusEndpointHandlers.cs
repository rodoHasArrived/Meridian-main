using System.Text;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Api;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using HealthCheckDto = Meridian.Contracts.Api.HealthCheckItem;

namespace Meridian.Application.UI;

/// <summary>
/// Shared handlers for status endpoints used by both StatusHttpServer (HttpListener)
/// and ASP.NET Core web dashboard. Contains the business logic for generating
/// health, status, and monitoring responses.
/// </summary>
public sealed class StatusEndpointHandlers
{
    private readonly DateTimeOffset _startTime;

    // Core providers
    private readonly Func<MetricsSnapshot> _metricsProvider;
    private readonly Func<PipelineStatistics> _pipelineProvider;
    private readonly Func<IReadOnlyList<DepthIntegrityEvent>> _integrityProvider;
    private readonly Func<ErrorRingBuffer?> _errorBufferProvider;

    // Optional extended providers
    private Func<Task<DetailedHealthReport>>? _detailedHealthProvider;
    private Func<BackpressureStatus>? _backpressureProvider;
    private Func<ProviderLatencySummary>? _providerLatencyProvider;
    private Func<ConnectionHealthSnapshot>? _connectionHealthProvider;

    // Health check thresholds
    public const double HighDropRateThreshold = 5.0;
    public const double CriticalDropRateThreshold = 20.0;
    public const int StaleDataThresholdSeconds = 60;

    public StatusEndpointHandlers(
        Func<MetricsSnapshot> metricsProvider,
        Func<PipelineStatistics> pipelineProvider,
        Func<IReadOnlyList<DepthIntegrityEvent>> integrityProvider,
        Func<ErrorRingBuffer?>? errorBufferProvider = null,
        DateTimeOffset? startTime = null)
    {
        _metricsProvider = metricsProvider ?? throw new ArgumentNullException(nameof(metricsProvider));
        _pipelineProvider = pipelineProvider ?? throw new ArgumentNullException(nameof(pipelineProvider));
        _integrityProvider = integrityProvider ?? throw new ArgumentNullException(nameof(integrityProvider));
        _errorBufferProvider = errorBufferProvider ?? (() => null);
        _startTime = startTime ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Registers extended providers for detailed health, backpressure, and provider latency endpoints.
    /// </summary>
    public void RegisterExtendedProviders(
        Func<Task<DetailedHealthReport>>? detailedHealth = null,
        Func<BackpressureStatus>? backpressure = null,
        Func<ProviderLatencySummary>? providerLatency = null,
        Func<ConnectionHealthSnapshot>? connectionHealth = null)
    {
        _detailedHealthProvider = detailedHealth;
        _backpressureProvider = backpressure;
        _providerLatencyProvider = providerLatency;
        _connectionHealthProvider = connectionHealth;
    }

    /// <summary>
    /// Gets the current uptime.
    /// </summary>
    public TimeSpan Uptime => DateTimeOffset.UtcNow - _startTime;

    /// <summary>
    /// Generates a comprehensive health check response.
    /// </summary>
    public HealthCheckResponse GetHealthCheck()
    {
        var metrics = _metricsProvider();
        var pipeline = _pipelineProvider();
        var integrity = _integrityProvider();

        var checks = new List<HealthCheckDto>();
        var overallStatus = HealthStatus.Healthy;

        // Check 1: Drop rate
        if (metrics.DropRate >= CriticalDropRateThreshold)
        {
            checks.Add(new HealthCheckDto
            {
                Name = "drop_rate",
                Status = "unhealthy",
                Message = $"Critical drop rate: {metrics.DropRate:F2}%. Events are being lost."
            });
            overallStatus = HealthStatus.Unhealthy;
        }
        else if (metrics.DropRate >= HighDropRateThreshold)
        {
            checks.Add(new HealthCheckDto
            {
                Name = "drop_rate",
                Status = "degraded",
                Message = $"Elevated drop rate: {metrics.DropRate:F2}%. Consider reducing load."
            });
            if (overallStatus == HealthStatus.Healthy)
                overallStatus = HealthStatus.Degraded;
        }
        else
        {
            checks.Add(new HealthCheckDto
            {
                Name = "drop_rate",
                Status = "healthy",
                Message = $"Drop rate: {metrics.DropRate:F2}%"
            });
        }

        // Check 2: Queue utilization
        if (pipeline.QueueUtilization > 90)
        {
            checks.Add(new HealthCheckDto
            {
                Name = "queue",
                Status = "unhealthy",
                Message = $"Queue near capacity: {pipeline.QueueUtilization:F1}%"
            });
            overallStatus = HealthStatus.Unhealthy;
        }
        else if (pipeline.QueueUtilization > 70)
        {
            checks.Add(new HealthCheckDto
            {
                Name = "queue",
                Status = "degraded",
                Message = $"Queue filling: {pipeline.QueueUtilization:F1}%"
            });
            if (overallStatus == HealthStatus.Healthy)
                overallStatus = HealthStatus.Degraded;
        }
        else
        {
            checks.Add(new HealthCheckDto
            {
                Name = "queue",
                Status = "healthy",
                Message = $"Queue utilization: {pipeline.QueueUtilization:F1}%"
            });
        }

        // Check 3: Data freshness
        if (metrics.Published > 0 && pipeline.TimeSinceLastFlush.TotalSeconds > StaleDataThresholdSeconds)
        {
            checks.Add(new HealthCheckDto
            {
                Name = "data_freshness",
                Status = "degraded",
                Message = $"No events for {pipeline.TimeSinceLastFlush.TotalSeconds:F0}s"
            });
            if (overallStatus == HealthStatus.Healthy)
                overallStatus = HealthStatus.Degraded;
        }
        else
        {
            checks.Add(new HealthCheckDto
            {
                Name = "data_freshness",
                Status = "healthy",
                Message = $"Last flush: {pipeline.TimeSinceLastFlush.TotalSeconds:F0}s ago"
            });
        }

        // Check 4: Recent integrity issues
        var recentIntegrity = integrity.Count(e => e.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-5));
        if (recentIntegrity > 10)
        {
            checks.Add(new HealthCheckDto
            {
                Name = "integrity",
                Status = "degraded",
                Message = $"{recentIntegrity} integrity events in last 5 minutes"
            });
            if (overallStatus == HealthStatus.Healthy)
                overallStatus = HealthStatus.Degraded;
        }
        else
        {
            checks.Add(new HealthCheckDto
            {
                Name = "integrity",
                Status = "healthy",
                Message = $"{recentIntegrity} integrity events in last 5 minutes"
            });
        }

        // Check 5: Memory usage
        if (metrics.MemoryUsageMb > 1024)
        {
            checks.Add(new HealthCheckDto
            {
                Name = "memory",
                Status = "degraded",
                Message = $"High memory usage: {metrics.MemoryUsageMb:F0} MB"
            });
            if (overallStatus == HealthStatus.Healthy)
                overallStatus = HealthStatus.Degraded;
        }
        else
        {
            checks.Add(new HealthCheckDto
            {
                Name = "memory",
                Status = "healthy",
                Message = $"Memory usage: {metrics.MemoryUsageMb:F0} MB"
            });
        }

        return new HealthCheckResponse
        {
            Status = overallStatus.ToString().ToLowerInvariant(),
            Timestamp = DateTimeOffset.UtcNow,
            Uptime = Uptime,
            Checks = checks.ToArray()
        };
    }

    /// <summary>
    /// Gets the HTTP status code for a health check response.
    /// </summary>
    public int GetHealthStatusCode(HealthCheckResponse response)
    {
        return response.Status switch
        {
            "healthy" => 200,
            "degraded" => 200,
            "unhealthy" => 503,
            _ => 200
        };
    }

    /// <summary>
    /// Checks if the service is ready to receive traffic.
    /// </summary>
    public (bool IsReady, string Message) CheckReadiness()
    {
        var pipeline = _pipelineProvider();
        var isReady = pipeline.QueueUtilization < 95;
        return (isReady, isReady ? "ready" : "not ready - queue overloaded");
    }

    /// <summary>
    /// Gets the full status response.
    /// </summary>
    public StatusResponse GetStatus()
    {
        var metrics = _metricsProvider();
        var pipeline = _pipelineProvider();

        return new StatusResponse
        {
            IsConnected = true,
            TimestampUtc = DateTimeOffset.UtcNow,
            Uptime = Uptime,
            Metrics = new MetricsData
            {
                Published = metrics.Published,
                Dropped = metrics.Dropped,
                Integrity = metrics.Integrity,
                HistoricalBars = metrics.HistoricalBars,
                EventsPerSecond = (float)metrics.EventsPerSecond,
                DropRate = (float)metrics.DropRate,
                Trades = metrics.Trades,
                DepthUpdates = metrics.DepthUpdates,
                Quotes = metrics.Quotes
            },
            Pipeline = new PipelineData
            {
                PublishedCount = pipeline.PublishedCount,
                DroppedCount = pipeline.DroppedCount,
                ConsumedCount = pipeline.ConsumedCount,
                CurrentQueueSize = pipeline.CurrentQueueSize,
                PeakQueueSize = pipeline.PeakQueueSize,
                QueueCapacity = pipeline.QueueCapacity,
                QueueUtilization = (float)pipeline.QueueUtilization,
                AverageProcessingTimeUs = (float)pipeline.AverageProcessingTimeUs
            }
        };
    }

    /// <summary>
    /// Gets Prometheus metrics in text format.
    /// </summary>
    public string GetPrometheusMetrics()
    {
        var m = _metricsProvider();
        var sb = new StringBuilder();

        sb.AppendLine("# HELP mdc_published Total events published");
        sb.AppendLine("# TYPE mdc_published counter");
        sb.AppendLine($"mdc_published {m.Published}");

        sb.AppendLine("# HELP mdc_dropped Total events dropped");
        sb.AppendLine("# TYPE mdc_dropped counter");
        sb.AppendLine($"mdc_dropped {m.Dropped}");

        sb.AppendLine("# HELP mdc_integrity Integrity events");
        sb.AppendLine("# TYPE mdc_integrity counter");
        sb.AppendLine($"mdc_integrity {m.Integrity}");

        sb.AppendLine("# HELP mdc_trades Trades processed");
        sb.AppendLine("# TYPE mdc_trades counter");
        sb.AppendLine($"mdc_trades {m.Trades}");

        sb.AppendLine("# HELP mdc_depth_updates Depth updates processed");
        sb.AppendLine("# TYPE mdc_depth_updates counter");
        sb.AppendLine($"mdc_depth_updates {m.DepthUpdates}");

        sb.AppendLine("# HELP mdc_quotes Quotes processed");
        sb.AppendLine("# TYPE mdc_quotes counter");
        sb.AppendLine($"mdc_quotes {m.Quotes}");

        sb.AppendLine("# HELP mdc_historical_bars Historical bar events processed");
        sb.AppendLine("# TYPE mdc_historical_bars counter");
        sb.AppendLine($"mdc_historical_bars {m.HistoricalBars}");

        sb.AppendLine("# HELP mdc_events_per_second Current event rate");
        sb.AppendLine("# TYPE mdc_events_per_second gauge");
        sb.AppendLine($"mdc_events_per_second {m.EventsPerSecond:F4}");

        sb.AppendLine("# HELP mdc_drop_rate Drop rate percent");
        sb.AppendLine("# TYPE mdc_drop_rate gauge");
        sb.AppendLine($"mdc_drop_rate {m.DropRate:F4}");

        sb.AppendLine("# HELP mdc_historical_bars_per_second Historical bar rate");
        sb.AppendLine("# TYPE mdc_historical_bars_per_second gauge");
        sb.AppendLine($"mdc_historical_bars_per_second {m.HistoricalBarsPerSecond:F4}");

        return sb.ToString();
    }

    /// <summary>
    /// Gets the errors response with optional filtering.
    /// </summary>
    public ErrorsResponseDto GetErrors(int count = 10, string? levelFilter = null, string? symbolFilter = null)
    {
        count = Math.Clamp(count, 1, 100);

        var errorBuffer = _errorBufferProvider();
        if (errorBuffer == null)
        {
            return new ErrorsResponseDto
            {
                Errors = Array.Empty<ErrorEntryDto>(),
                Stats = new ErrorStatsDto { TotalErrors = 0 },
                Message = "Error buffer not configured"
            };
        }

        IReadOnlyList<ErrorEntry> errors;

        if (!string.IsNullOrEmpty(symbolFilter))
        {
            errors = errorBuffer.GetBySymbol(symbolFilter, count);
        }
        else if (!string.IsNullOrEmpty(levelFilter) && Enum.TryParse<ErrorLevel>(levelFilter, ignoreCase: true, out var level))
        {
            errors = errorBuffer.GetByLevel(level, count);
        }
        else
        {
            errors = errorBuffer.GetRecent(count);
        }

        var stats = errorBuffer.GetStats();

        return new ErrorsResponseDto
        {
            Errors = errors.Select(e => new ErrorEntryDto
            {
                Id = e.Id,
                Timestamp = e.Timestamp,
                Level = e.Level.ToString().ToLowerInvariant(),
                Source = e.Source,
                Message = e.Message,
                ExceptionType = e.ExceptionType,
                Context = e.Context,
                Symbol = e.Symbol,
                Provider = e.Provider
            }).ToList(),
            Stats = new ErrorStatsDto
            {
                TotalErrors = stats.TotalErrors,
                ErrorsInLastMinute = stats.ErrorsInLastMinute,
                ErrorsInLastHour = stats.ErrorsInLastHour,
                WarningCount = stats.WarningCount,
                ErrorCount = stats.ErrorCount,
                CriticalCount = stats.CriticalCount,
                LastErrorTime = stats.LastErrorTime
            }
        };
    }

    /// <summary>
    /// Gets the backpressure status.
    /// </summary>
    public BackpressureStatusDto GetBackpressure()
    {
        var pipeline = _pipelineProvider();

        if (_backpressureProvider != null)
        {
            var status = _backpressureProvider();
            return new BackpressureStatusDto
            {
                IsActive = status.IsActive,
                Level = status.Level.ToString().ToLowerInvariant(),
                QueueUtilization = (float)Math.Round(status.QueueUtilization, 2),
                DroppedEvents = status.DroppedEvents,
                DropRate = (float)Math.Round(status.DropRate, 2),
                DurationSeconds = (float)status.Duration.TotalSeconds,
                Message = status.Message,
                QueueDepthWarning = pipeline.HighWaterMarkWarned
            };
        }

        // Return basic backpressure info from pipeline stats
        var dropRate = pipeline.PublishedCount > 0
            ? (double)pipeline.DroppedCount / pipeline.PublishedCount * 100
            : 0;

        var isActive = dropRate > 5 || pipeline.QueueUtilization > 70;
        var level = dropRate > 20 || pipeline.QueueUtilization > 90 ? "critical" :
                   dropRate > 5 || pipeline.QueueUtilization > 70 ? "warning" : "none";

        return new BackpressureStatusDto
        {
            IsActive = isActive,
            Level = level,
            QueueUtilization = (float)Math.Round(pipeline.QueueUtilization, 2),
            DroppedEvents = pipeline.DroppedCount,
            DropRate = (float)Math.Round(dropRate, 2),
            Message = $"Queue: {pipeline.QueueUtilization:F1}%, Drop rate: {dropRate:F2}%",
            QueueDepthWarning = pipeline.HighWaterMarkWarned
        };
    }

    /// <summary>
    /// Gets provider latency summary.
    /// </summary>
    public (ProviderLatencySummaryDto? Summary, string? Error) GetProviderLatency()
    {
        if (_providerLatencyProvider == null)
        {
            return (null, "Provider latency tracking not configured");
        }

        var summary = _providerLatencyProvider();
        var globalAvgMs = summary.Providers.Any()
            ? summary.Providers.Average(p => p.MeanMs)
            : 0.0;

        return (new ProviderLatencySummaryDto
        {
            Timestamp = summary.CalculatedAt,
            Providers = summary.Providers.Select(p => new ProviderLatencyStatsDto
            {
                Provider = p.Provider,
                AverageMs = (float)Math.Round(p.MeanMs, 2),
                MinMs = (float)Math.Round(p.MinMs, 2),
                MaxMs = (float)Math.Round(p.MaxMs, 2),
                P50Ms = (float)Math.Round(p.P50Ms, 2),
                P95Ms = (float)Math.Round(p.P95Ms, 2),
                P99Ms = (float)Math.Round(p.P99Ms, 2),
                SampleCount = p.SampleCount,
                IsHealthy = p.P99Ms < 1000.0 // Consider healthy if p99 latency is under 1 second
            }).ToList(),
            GlobalAverageMs = (float)Math.Round(globalAvgMs, 2),
            GlobalP99Ms = (float)Math.Round(summary.GlobalP99Ms, 2)
        }, null);
    }

    /// <summary>
    /// Gets connection health snapshot.
    /// </summary>
    public (ConnectionHealthSnapshotDto? Snapshot, string? Error) GetConnectionHealth()
    {
        if (_connectionHealthProvider == null)
        {
            return (null, "Connection health monitoring not configured");
        }

        var snapshot = _connectionHealthProvider();
        return (new ConnectionHealthSnapshotDto
        {
            Timestamp = snapshot.Timestamp,
            TotalConnections = snapshot.TotalConnections,
            HealthyConnections = snapshot.HealthyConnections,
            UnhealthyConnections = snapshot.UnhealthyConnections,
            GlobalAverageLatencyMs = (float)Math.Round(snapshot.GlobalAverageLatencyMs, 2),
            GlobalMinLatencyMs = (float)Math.Round(snapshot.GlobalMinLatencyMs, 2),
            GlobalMaxLatencyMs = (float)Math.Round(snapshot.GlobalMaxLatencyMs, 2),
            Connections = snapshot.Connections.Select(c => new ConnectionHealthDto
            {
                ConnectionId = c.ConnectionId,
                ProviderName = c.ProviderName,
                IsConnected = c.IsConnected,
                IsHealthy = c.IsHealthy,
                LastHeartbeatTime = c.LastHeartbeatTime,
                MissedHeartbeats = c.MissedHeartbeats,
                UptimeSeconds = (float)c.UptimeDuration.TotalSeconds,
                AverageLatencyMs = (float)Math.Round(c.AverageLatencyMs, 2)
            }).ToList()
        }, null);
    }

    /// <summary>
    /// Gets detailed health report (async).
    /// </summary>
    public async Task<(DetailedHealthReport? Report, string? Error)> GetDetailedHealthAsync(CancellationToken ct = default)
    {
        if (_detailedHealthProvider == null)
        {
            return (null, "Detailed health check not configured");
        }

        try
        {
            var report = await _detailedHealthProvider();
            return (report, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Gets the list of available backfill providers (static info).
    /// </summary>
    public static IReadOnlyList<BackfillProviderInfo> GetBackfillProviderInfo()
    {
        return new[]
        {
            new BackfillProviderInfo
            {
                Name = "alpaca",
                DisplayName = "Alpaca Markets",
                Description = "Real-time and historical data with adjustments",
                IsAvailable = true,
                RequiresApiKey = true,
                SupportsIntraday = false,
                SupportedGranularities = ["Daily"]
            },
            new BackfillProviderInfo
            {
                Name = "yahoo",
                DisplayName = "Yahoo Finance",
                Description = "Free unofficial daily and regular-hours intraday data for most listed equities and ETFs",
                IsAvailable = true,
                RequiresApiKey = false,
                SupportsIntraday = true,
                SupportedGranularities = ["Daily", "1Min", "5Min", "15Min", "30Min", "Hourly", "4Hour"]
            },
            new BackfillProviderInfo
            {
                Name = "stooq",
                DisplayName = "Stooq",
                Description = "Free EOD data for global markets",
                IsAvailable = true,
                RequiresApiKey = false,
                SupportsIntraday = false,
                SupportedGranularities = ["Daily"]
            },
            new BackfillProviderInfo
            {
                Name = "nasdaq",
                DisplayName = "Nasdaq Data Link",
                Description = "Historical data (API key may be required)",
                IsAvailable = true,
                RequiresApiKey = true,
                SupportsIntraday = false,
                SupportedGranularities = ["Daily"]
            }
        };
    }

    private enum HealthStatus { Healthy, Degraded, Unhealthy }
}
