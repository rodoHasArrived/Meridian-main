using Meridian.Contracts.Domain.Enums;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Provides monitoring thresholds derived from a symbol's <see cref="LiquidityProfile"/>.
/// Centralises the mapping so that GapAnalyzer, CompletenessScoreCalculator,
/// AnomalyDetector, and DataFreshnessSlaMonitor all use consistent values.
/// </summary>
public static class LiquidityProfileProvider
{
    /// <summary>
    /// Monitoring thresholds for a given liquidity tier.
    /// </summary>
    public sealed record LiquidityThresholds(
        // <summary>Minimum gap duration (seconds) before it is flagged as a data gap.</summary>
        int GapThresholdSeconds,

        // <summary>Expected events per hour for completeness scoring.</summary>
        long ExpectedEventsPerHour,

        // <summary>Data freshness SLA threshold (seconds) before a warning/violation fires.</summary>
        int FreshnessThresholdSeconds,

        // <summary>Stale data threshold (seconds) for anomaly detection.</summary>
        int StaleDataThresholdSeconds,

        // <summary>Acceptable bid-ask spread (basis points) before flagging.</summary>
        double SpreadThresholdBps,

        // <summary>Minimum samples needed before enabling statistical anomaly detection.</summary>
        int MinSamplesForStatistics
    );

    /// <summary>
    /// Returns monitoring thresholds for the given liquidity profile.
    /// </summary>
    public static LiquidityThresholds GetThresholds(LiquidityProfile profile)
    {
        return profile switch
        {
            LiquidityProfile.High => new LiquidityThresholds(
                GapThresholdSeconds: 60,
                ExpectedEventsPerHour: 1000,
                FreshnessThresholdSeconds: 60,
                StaleDataThresholdSeconds: 60,
                SpreadThresholdBps: 10,
                MinSamplesForStatistics: 100),

            LiquidityProfile.Normal => new LiquidityThresholds(
                GapThresholdSeconds: 120,
                ExpectedEventsPerHour: 200,
                FreshnessThresholdSeconds: 120,
                StaleDataThresholdSeconds: 120,
                SpreadThresholdBps: 50,
                MinSamplesForStatistics: 50),

            LiquidityProfile.Low => new LiquidityThresholds(
                GapThresholdSeconds: 600,
                ExpectedEventsPerHour: 20,
                FreshnessThresholdSeconds: 600,
                StaleDataThresholdSeconds: 600,
                SpreadThresholdBps: 500,
                MinSamplesForStatistics: 20),

            LiquidityProfile.VeryLow => new LiquidityThresholds(
                GapThresholdSeconds: 1800,
                ExpectedEventsPerHour: 5,
                FreshnessThresholdSeconds: 1800,
                StaleDataThresholdSeconds: 1800,
                SpreadThresholdBps: 1000,
                MinSamplesForStatistics: 10),

            LiquidityProfile.Minimal => new LiquidityThresholds(
                GapThresholdSeconds: 3600,
                ExpectedEventsPerHour: 1,
                FreshnessThresholdSeconds: 3600,
                StaleDataThresholdSeconds: 3600,
                SpreadThresholdBps: 2000,
                MinSamplesForStatistics: 5),

            _ => GetThresholds(LiquidityProfile.High)
        };
    }

    /// <summary>
    /// Returns the effective liquidity profile for a symbol config,
    /// falling back to <see cref="LiquidityProfile.High"/> when unspecified.
    /// </summary>
    public static LiquidityProfile Resolve(LiquidityProfile? configured)
    {
        return configured ?? LiquidityProfile.High;
    }

    /// <summary>
    /// Classifies gap severity using liquidity-adjusted thresholds instead of fixed minute ranges.
    /// </summary>
    public static GapSeverity ClassifyGapSeverity(TimeSpan duration, LiquidityProfile profile)
    {
        var thresholds = GetThresholds(profile);
        var gapSeconds = duration.TotalSeconds;
        var baseThreshold = thresholds.GapThresholdSeconds;

        return gapSeconds switch
        {
            _ when gapSeconds < baseThreshold => GapSeverity.Minor,
            _ when gapSeconds < baseThreshold * 5 => GapSeverity.Moderate,
            _ when gapSeconds < baseThreshold * 30 => GapSeverity.Significant,
            _ when gapSeconds < baseThreshold * 60 => GapSeverity.Major,
            _ => GapSeverity.Critical
        };
    }

    /// <summary>
    /// Infers a likely cause for a data gap, taking the symbol's liquidity into account.
    /// </summary>
    public static string? InferGapCause(TimeSpan duration, DateTimeOffset start, DateTimeOffset end, LiquidityProfile profile)
    {
        var startHour = start.Hour;
        var endHour = end.Hour;

        // Market closed overnight
        if ((startHour >= 20 || startHour < 13) && (endHour >= 13 && endHour < 20))
        {
            return "Market closed overnight";
        }

        var thresholds = GetThresholds(profile);

        // If the gap is within the expected quiet range for this liquidity tier, say so
        if (duration.TotalSeconds <= thresholds.GapThresholdSeconds * 3)
        {
            return profile >= LiquidityProfile.Low
                ? "Normal quiet period for illiquid instrument"
                : "Brief data delay";
        }

        if (duration.TotalMinutes >= 30 && duration.TotalMinutes <= 120)
        {
            return "Possible connection interruption";
        }

        return "Unknown cause - investigate provider";
    }
}
