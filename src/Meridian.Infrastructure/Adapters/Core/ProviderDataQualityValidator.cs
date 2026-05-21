using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Shared provider-boundary validation for normalized market-data updates before
/// they enter collectors and downstream pipelines.
/// </summary>
public static class ProviderDataQualityValidator
{
    private static readonly TimeSpan DefaultFutureSkew = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<ProviderDataQualityIssue> ValidateTrade(
        string providerName,
        MarketTradeUpdate update,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(update);

        var issues = new List<ProviderDataQualityIssue>(capacity: 2);
        ValidateCommon(providerName, update.Symbol, update.Timestamp, now, issues);

        if (update.Price < 0)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "price",
                update.Symbol,
                "Trade price must be non-negative.",
                "Drop the event and inspect provider mapping or source feed quality."));
        }

        if (update.Size < 0)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "size",
                update.Symbol,
                "Trade size must be non-negative.",
                "Drop the event and inspect provider mapping or source feed quality."));
        }

        return issues.Count == 0 ? Array.Empty<ProviderDataQualityIssue>() : issues;
    }

    public static IReadOnlyList<ProviderDataQualityIssue> ValidateQuote(
        string providerName,
        MarketQuoteUpdate update,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(update);

        var issues = new List<ProviderDataQualityIssue>(capacity: 2);
        ValidateCommon(providerName, update.Symbol, update.Timestamp, now, issues);

        if (update.BidPrice < 0)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "bidPrice",
                update.Symbol,
                "Bid price must be non-negative.",
                "Drop the event and inspect provider mapping or source feed quality."));
        }

        if (update.AskPrice < 0)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "askPrice",
                update.Symbol,
                "Ask price must be non-negative.",
                "Drop the event and inspect provider mapping or source feed quality."));
        }

        if (update.BidSize < 0)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "bidSize",
                update.Symbol,
                "Bid size must be non-negative.",
                "Drop the event and inspect provider mapping or source feed quality."));
        }

        if (update.AskSize < 0)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "askSize",
                update.Symbol,
                "Ask size must be non-negative.",
                "Drop the event and inspect provider mapping or source feed quality."));
        }

        if (update.BidPrice > 0 && update.AskPrice > 0 && update.AskPrice < update.BidPrice)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "askPrice",
                update.Symbol,
                "Ask price is below bid price.",
                "Drop the event unless the provider supplies a documented crossed-market condition."));
        }

        return issues.Count == 0 ? Array.Empty<ProviderDataQualityIssue>() : issues;
    }

    private static void ValidateCommon(
        string providerName,
        string? symbol,
        DateTimeOffset timestamp,
        DateTimeOffset? now,
        List<ProviderDataQualityIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "symbol",
                null,
                "Symbol is required.",
                "Drop the event and inspect provider symbol mapping."));
        }

        if (timestamp == default)
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "timestamp",
                symbol,
                "Timestamp is required.",
                "Drop the event and inspect provider timestamp mapping."));
            return;
        }

        var referenceTime = now ?? DateTimeOffset.UtcNow;
        if (timestamp > referenceTime.Add(DefaultFutureSkew))
        {
            issues.Add(ProviderDataQualityIssue.Error(
                providerName,
                "timestamp",
                symbol,
                "Timestamp is too far in the future.",
                "Drop the event and inspect provider clock, timezone, or timestamp unit mapping."));
        }
    }
}

public enum ProviderDataQualitySeverity
{
    Warning = 0,
    Error = 1
}

public sealed record ProviderDataQualityIssue(
    ProviderDataQualitySeverity Severity,
    string ProviderName,
    string FieldPath,
    string? Entity,
    string Message,
    string SuggestedRecovery)
{
    public static ProviderDataQualityIssue Error(
        string providerName,
        string fieldPath,
        string? entity,
        string message,
        string suggestedRecovery)
        => new(
            ProviderDataQualitySeverity.Error,
            providerName,
            fieldPath,
            entity,
            message,
            suggestedRecovery);
}
