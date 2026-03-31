using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for monitoring and calculating provider health scores.
/// Provides detailed breakdown of health metrics and historical trends.
/// </summary>
public sealed class ProviderHealthService : IDisposable
{
    private static readonly Lazy<ProviderHealthService> _instance = new(() => new ProviderHealthService());
    private readonly ApiClientService _apiClient;
    private readonly Timer _updateTimer;
    private readonly ConcurrentDictionary<string, ProviderHealthData> _providerHealth = new();
    private readonly ConcurrentDictionary<string, List<HealthHistoryPoint>> _healthHistory = new();
    private bool _disposed;

    public static ProviderHealthService Instance => _instance.Value;

    private ProviderHealthService()
    {
        _apiClient = ApiClientService.Instance;
        _updateTimer = new Timer(5000);
        _updateTimer.Elapsed += OnTimerElapsed;
        _updateTimer.AutoReset = true;
    }

    /// <summary>
    /// Event raised when health data is updated.
    /// </summary>
    public event EventHandler<HealthUpdateEventArgs>? HealthUpdated;

    /// <summary>
    /// Event raised when a provider's health falls below threshold.
    /// </summary>
    public event EventHandler<HealthAlertEventArgs>? HealthAlert;

    /// <summary>
    /// Starts health monitoring.
    /// </summary>
    public void StartMonitoring()
    {
        _updateTimer.Start();
    }

    /// <summary>
    /// Stops health monitoring.
    /// </summary>
    public void StopMonitoring()
    {
        _updateTimer.Stop();
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed)
            return;

        try
        {
            await RefreshHealthDataAsync();
        }
        catch (Exception ex)
        {
            // Log error but don't crash - timer callbacks must handle their own exceptions
            System.Diagnostics.Debug.WriteLine($"Error refreshing health data: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets health data for all providers.
    /// </summary>
    public async Task<List<ProviderHealthData>> GetAllProviderHealthAsync(CancellationToken ct = default)
    {
        await RefreshHealthDataAsync(ct);
        return _providerHealth.Values.ToList();
    }

    /// <summary>
    /// Gets health data for a specific provider.
    /// </summary>
    public async Task<ProviderHealthData?> GetProviderHealthAsync(string providerId, CancellationToken ct = default)
    {
        await RefreshHealthDataAsync(ct);
        return _providerHealth.TryGetValue(providerId, out var health) ? health : null;
    }

    /// <summary>
    /// Gets health history for a provider.
    /// </summary>
    public List<HealthHistoryPoint> GetHealthHistory(string providerId, TimeSpan duration)
    {
        if (_healthHistory.TryGetValue(providerId, out var history))
        {
            var cutoff = DateTime.UtcNow - duration;
            lock (history)
            {
                return history.Where(h => h.Timestamp >= cutoff).ToList();
            }
        }
        return new List<HealthHistoryPoint>();
    }

    /// <summary>
    /// Gets failover thresholds configuration.
    /// </summary>
    public async Task<FailoverThresholds> GetFailoverThresholdsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<FailoverThresholdsResponse>(
            "/api/providers/failover-thresholds",
            ct);

        if (response.Success && response.Data != null)
        {
            return new FailoverThresholds
            {
                MinHealthScore = response.Data.MinHealthScore,
                MaxLatencyMs = response.Data.MaxLatencyMs,
                MaxReconnectsPerHour = response.Data.MaxReconnectsPerHour,
                MinDataCompletenessPercent = response.Data.MinDataCompletenessPercent,
                AutoFailoverEnabled = response.Data.AutoFailoverEnabled
            };
        }

        // Return defaults
        return new FailoverThresholds
        {
            MinHealthScore = 70,
            MaxLatencyMs = 500,
            MaxReconnectsPerHour = 5,
            MinDataCompletenessPercent = 95,
            AutoFailoverEnabled = true
        };
    }

    /// <summary>
    /// Updates failover thresholds.
    /// </summary>
    public async Task<bool> UpdateFailoverThresholdsAsync(FailoverThresholds thresholds, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<FailoverThresholdsResponse>(
            "/api/providers/failover-thresholds",
            new
            {
                minHealthScore = thresholds.MinHealthScore,
                maxLatencyMs = thresholds.MaxLatencyMs,
                maxReconnectsPerHour = thresholds.MaxReconnectsPerHour,
                minDataCompletenessPercent = thresholds.MinDataCompletenessPercent,
                autoFailoverEnabled = thresholds.AutoFailoverEnabled
            },
            ct);

        return response.Success;
    }

    /// <summary>
    /// Compares health metrics across providers.
    /// </summary>
    public async Task<ProviderHealthComparison> CompareProvidersAsync(CancellationToken ct = default)
    {
        var healthData = await GetAllProviderHealthAsync(ct);

        return new ProviderHealthComparison
        {
            Providers = healthData,
            BestOverall = healthData.OrderByDescending(p => p.OverallScore).FirstOrDefault()?.ProviderId,
            BestLatency = healthData.OrderBy(p => p.Metrics.AverageLatencyMs).FirstOrDefault()?.ProviderId,
            BestCompleteness = healthData.OrderByDescending(p => p.Metrics.DataCompletenessPercent).FirstOrDefault()?.ProviderId,
            BestStability = healthData.OrderByDescending(p => p.Metrics.ConnectionStabilityScore).FirstOrDefault()?.ProviderId
        };
    }

    private async Task RefreshHealthDataAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ProviderHealthResponse>(
            "/api/providers/health",
            ct);

        if (response.Success && response.Data?.Providers != null)
        {
            foreach (var provider in response.Data.Providers)
            {
                var healthData = new ProviderHealthData
                {
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.ProviderName,
                    IsConnected = provider.IsConnected,
                    LastUpdated = DateTime.UtcNow,
                    OverallScore = CalculateOverallScore(provider),
                    Metrics = new HealthMetrics
                    {
                        ConnectionStabilityScore = provider.ConnectionStabilityScore,
                        AverageLatencyMs = provider.AverageLatencyMs,
                        LatencyP99Ms = provider.LatencyP99Ms,
                        LatencyConsistencyScore = provider.LatencyConsistencyScore,
                        DataCompletenessPercent = provider.DataCompletenessPercent,
                        ReconnectsLastHour = provider.ReconnectsLastHour,
                        ReconnectionScore = CalculateReconnectionScore(provider.ReconnectsLastHour),
                        UptimePercent = provider.UptimePercent,
                        MessagesPerSecond = provider.MessagesPerSecond,
                        ErrorsLastHour = provider.ErrorsLastHour
                    },
                    Breakdown = CalculateBreakdown(provider)
                };

                _providerHealth[provider.ProviderId] = healthData;

                // Update history
                var history = _healthHistory.GetOrAdd(provider.ProviderId, _ => new List<HealthHistoryPoint>());

                lock (history)
                {
                    history.Add(new HealthHistoryPoint
                    {
                        Timestamp = DateTime.UtcNow,
                        OverallScore = healthData.OverallScore,
                        LatencyMs = provider.AverageLatencyMs,
                        CompletenessPercent = provider.DataCompletenessPercent
                    });

                    // Keep only last 24 hours
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    history.RemoveAll(h => h.Timestamp < cutoff);
                }

                // Check for alerts
                CheckHealthAlerts(healthData);
            }
        }
        else
        {
            // Generate mock data for demo
            GenerateMockHealthData();
        }

        HealthUpdated?.Invoke(this, new HealthUpdateEventArgs
        {
            Providers = _providerHealth.Values.ToList()
        });
    }

    private void GenerateMockHealthData()
    {
        var providers = new[]
        {
            ("alpaca", "Alpaca Markets", true, 95.2, 45.0, 99.8, 0),
            ("polygon", "Polygon.io", true, 88.5, 62.0, 97.2, 2),
            ("ib", "Interactive Brokers", false, 72.1, 120.0, 94.5, 5),
            ("nyse", "NYSE", true, 91.0, 55.0, 98.1, 1)
        };

        foreach (var (id, name, connected, stability, latency, completeness, reconnects) in providers)
        {
            var metrics = new HealthMetrics
            {
                ConnectionStabilityScore = stability,
                AverageLatencyMs = latency,
                LatencyP99Ms = latency * 2.5,
                LatencyConsistencyScore = Math.Max(0, 100 - latency / 2),
                DataCompletenessPercent = completeness,
                ReconnectsLastHour = reconnects,
                ReconnectionScore = CalculateReconnectionScore(reconnects),
                UptimePercent = stability,
                MessagesPerSecond = connected ? 1500 + new Random().Next(500) : 0,
                ErrorsLastHour = reconnects * 2
            };

            var breakdown = new HealthScoreBreakdown
            {
                ConnectionStability = new ScoreComponent { Weight = 30, Score = stability, WeightedScore = stability * 0.3 },
                LatencyConsistency = new ScoreComponent { Weight = 25, Score = metrics.LatencyConsistencyScore, WeightedScore = metrics.LatencyConsistencyScore * 0.25 },
                DataCompleteness = new ScoreComponent { Weight = 25, Score = completeness, WeightedScore = completeness * 0.25 },
                ReconnectionFrequency = new ScoreComponent { Weight = 20, Score = metrics.ReconnectionScore, WeightedScore = metrics.ReconnectionScore * 0.2 }
            };

            var healthData = new ProviderHealthData
            {
                ProviderId = id,
                ProviderName = name,
                IsConnected = connected,
                LastUpdated = DateTime.UtcNow,
                OverallScore = breakdown.ConnectionStability.WeightedScore +
                               breakdown.LatencyConsistency.WeightedScore +
                               breakdown.DataCompleteness.WeightedScore +
                               breakdown.ReconnectionFrequency.WeightedScore,
                Metrics = metrics,
                Breakdown = breakdown
            };

            _providerHealth[id] = healthData;

            // Update history with some variation
            var history = _healthHistory.GetOrAdd(id, _ =>
            {
                var list = new List<HealthHistoryPoint>();
                // Generate historical data
                var rnd = new Random();
                for (int i = 24; i >= 0; i--)
                {
                    list.Add(new HealthHistoryPoint
                    {
                        Timestamp = DateTime.UtcNow.AddHours(-i),
                        OverallScore = healthData.OverallScore + rnd.Next(-5, 6),
                        LatencyMs = latency + rnd.Next(-10, 20),
                        CompletenessPercent = Math.Min(100, completeness + rnd.Next(-2, 2))
                    });
                }
                return list;
            });

            lock (history)
            {
                history.Add(new HealthHistoryPoint
                {
                    Timestamp = DateTime.UtcNow,
                    OverallScore = healthData.OverallScore,
                    LatencyMs = latency,
                    CompletenessPercent = completeness
                });

                var cutoff = DateTime.UtcNow.AddHours(-24);
                history.RemoveAll(h => h.Timestamp < cutoff);
            }
        }
    }

    private double CalculateOverallScore(ProviderHealthInfo provider)
    {
        var breakdown = CalculateBreakdown(provider);
        return breakdown.ConnectionStability.WeightedScore +
               breakdown.LatencyConsistency.WeightedScore +
               breakdown.DataCompleteness.WeightedScore +
               breakdown.ReconnectionFrequency.WeightedScore;
    }

    private HealthScoreBreakdown CalculateBreakdown(ProviderHealthInfo provider)
    {
        var latencyScore = Math.Max(0, 100 - provider.AverageLatencyMs / 5);
        var reconnectionScore = CalculateReconnectionScore(provider.ReconnectsLastHour);

        return new HealthScoreBreakdown
        {
            ConnectionStability = new ScoreComponent
            {
                Weight = 30,
                Score = provider.ConnectionStabilityScore,
                WeightedScore = provider.ConnectionStabilityScore * 0.3
            },
            LatencyConsistency = new ScoreComponent
            {
                Weight = 25,
                Score = latencyScore,
                WeightedScore = latencyScore * 0.25
            },
            DataCompleteness = new ScoreComponent
            {
                Weight = 25,
                Score = provider.DataCompletenessPercent,
                WeightedScore = provider.DataCompletenessPercent * 0.25
            },
            ReconnectionFrequency = new ScoreComponent
            {
                Weight = 20,
                Score = reconnectionScore,
                WeightedScore = reconnectionScore * 0.2
            }
        };
    }

    private static double CalculateReconnectionScore(int reconnectsLastHour)
    {
        return reconnectsLastHour switch
        {
            0 => 100,
            1 => 90,
            2 => 75,
            3 => 60,
            4 => 40,
            5 => 20,
            _ => Math.Max(0, 100 - reconnectsLastHour * 15)
        };
    }

    private void CheckHealthAlerts(ProviderHealthData healthData)
    {
        if (healthData.OverallScore < 70)
        {
            HealthAlert?.Invoke(this, new HealthAlertEventArgs
            {
                ProviderId = healthData.ProviderId,
                ProviderName = healthData.ProviderName,
                AlertType = HealthAlertType.LowOverallScore,
                CurrentValue = healthData.OverallScore,
                Threshold = 70,
                Message = $"Provider health score ({healthData.OverallScore:F1}) is below threshold"
            });
        }

        if (healthData.Metrics.AverageLatencyMs > 200)
        {
            HealthAlert?.Invoke(this, new HealthAlertEventArgs
            {
                ProviderId = healthData.ProviderId,
                ProviderName = healthData.ProviderName,
                AlertType = HealthAlertType.HighLatency,
                CurrentValue = healthData.Metrics.AverageLatencyMs,
                Threshold = 200,
                Message = $"Average latency ({healthData.Metrics.AverageLatencyMs:F0}ms) exceeds threshold"
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true; // Set before stopping so in-flight callbacks exit early
        _updateTimer.Stop();
        _updateTimer.Elapsed -= OnTimerElapsed;
        _updateTimer.Dispose();
    }
}


public sealed class HealthUpdateEventArgs : EventArgs
{
    public List<ProviderHealthData> Providers { get; set; } = new();
}

public sealed class HealthAlertEventArgs : EventArgs
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public HealthAlertType AlertType { get; set; }
    public double CurrentValue { get; set; }
    public double Threshold { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum HealthAlertType : byte
{
    LowOverallScore,
    HighLatency,
    LowCompleteness,
    FrequentReconnects,
    ConnectionLost
}



public sealed class ProviderHealthData
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public DateTime LastUpdated { get; set; }
    public double OverallScore { get; set; }
    public HealthMetrics Metrics { get; set; } = new();
    public HealthScoreBreakdown Breakdown { get; set; } = new();
}

public sealed class HealthMetrics
{
    public double ConnectionStabilityScore { get; set; }
    public double AverageLatencyMs { get; set; }
    public double LatencyP99Ms { get; set; }
    public double LatencyConsistencyScore { get; set; }
    public double DataCompletenessPercent { get; set; }
    public int ReconnectsLastHour { get; set; }
    public double ReconnectionScore { get; set; }
    public double UptimePercent { get; set; }
    public double MessagesPerSecond { get; set; }
    public int ErrorsLastHour { get; set; }
}

public sealed class HealthScoreBreakdown
{
    public ScoreComponent ConnectionStability { get; set; } = new();
    public ScoreComponent LatencyConsistency { get; set; } = new();
    public ScoreComponent DataCompleteness { get; set; } = new();
    public ScoreComponent ReconnectionFrequency { get; set; } = new();
}

public sealed class ScoreComponent
{
    public int Weight { get; set; }
    public double Score { get; set; }
    public double WeightedScore { get; set; }
}

public sealed class HealthHistoryPoint
{
    public DateTime Timestamp { get; set; }
    public double OverallScore { get; set; }
    public double LatencyMs { get; set; }
    public double CompletenessPercent { get; set; }
}

public sealed class FailoverThresholds
{
    public double MinHealthScore { get; set; } = 70;
    public double MaxLatencyMs { get; set; } = 500;
    public int MaxReconnectsPerHour { get; set; } = 5;
    public double MinDataCompletenessPercent { get; set; } = 95;
    public bool AutoFailoverEnabled { get; set; } = true;
}

// NOTE: ProviderComparison is defined in AdvancedAnalyticsModels.cs for cross-provider comparison
// ProviderHealthComparison below is for overall provider ranking

public sealed class ProviderHealthComparison
{
    public List<ProviderHealthData> Providers { get; set; } = new();
    public string? BestOverall { get; set; }
    public string? BestLatency { get; set; }
    public string? BestCompleteness { get; set; }
    public string? BestStability { get; set; }
}



public sealed class ProviderHealthResponse
{
    public List<ProviderHealthInfo>? Providers { get; set; }
}

public sealed class ProviderHealthInfo
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public double ConnectionStabilityScore { get; set; }
    public double AverageLatencyMs { get; set; }
    public double LatencyP99Ms { get; set; }
    public double LatencyConsistencyScore { get; set; }
    public double DataCompletenessPercent { get; set; }
    public int ReconnectsLastHour { get; set; }
    public double UptimePercent { get; set; }
    public double MessagesPerSecond { get; set; }
    public int ErrorsLastHour { get; set; }
}

public sealed class FailoverThresholdsResponse
{
    public double MinHealthScore { get; set; }
    public double MaxLatencyMs { get; set; }
    public int MaxReconnectsPerHour { get; set; }
    public double MinDataCompletenessPercent { get; set; }
    public bool AutoFailoverEnabled { get; set; }
}

