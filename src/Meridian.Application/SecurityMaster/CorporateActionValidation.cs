using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

internal static class CorporateActionValidation
{
    public static CorporateActionDto Normalize(CorporateActionDto action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action with
        {
            EventType = CorporateActionEventTypes.Normalize(action.EventType),
            Currency = NormalizeCurrency(action.Currency)
        };
    }

    public static string? Validate(CorporateActionDto action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var normalized = Normalize(action);
        if (string.IsNullOrWhiteSpace(normalized.EventType))
            return "Corporate actions must include EventType.";

        if (!CorporateActionEventTypes.IsKnown(normalized.EventType))
            return $"Unsupported corporate action EventType '{action.EventType}'.";

        if (normalized.RecordDate.HasValue && normalized.RecordDate.Value < normalized.ExDate)
            return "Corporate action RecordDate must be on or after ExDate.";

        if (normalized.PayDate.HasValue && normalized.PayDate.Value < normalized.ExDate)
            return "Corporate action PayDate must be on or after ExDate.";

        if (normalized.RecordDate.HasValue &&
            normalized.PayDate.HasValue &&
            normalized.PayDate.Value < normalized.RecordDate.Value)
        {
            return "Corporate action PayDate must be on or after RecordDate.";
        }

        if (normalized.DividendPerShare.HasValue && normalized.DividendPerShare.Value < 0m)
            return "DividendPerShare must be greater than or equal to 0.";

        if (normalized.SubscriptionPricePerShare.HasValue && normalized.SubscriptionPricePerShare.Value < 0m)
            return "SubscriptionPricePerShare must be greater than or equal to 0.";

        if (normalized.DistributionRatio.HasValue && normalized.DistributionRatio.Value < 0m)
            return "DistributionRatio must be greater than or equal to 0.";

        var currencyError = ValidateCashCurrency(normalized);
        if (currencyError is not null)
            return currencyError;

        return normalized.EventType switch
        {
            CorporateActionEventTypes.StockSplit => ValidateForwardSplit(normalized),
            CorporateActionEventTypes.ReverseStockSplit => ValidateReverseSplit(normalized),
            CorporateActionEventTypes.Dividend => ValidateDividend(normalized),
            _ => null
        };
    }

    private static string? ValidateForwardSplit(CorporateActionDto action)
    {
        if (!action.SplitRatio.HasValue)
            return "StockSplit corporate actions must include SplitRatio.";

        if (action.SplitRatio.Value <= 0m || action.SplitRatio.Value > 1_000m)
            return "StockSplit SplitRatio must be greater than 0 and less than or equal to 1000.";

        if (action.SplitRatio.Value < 1m)
            return "Reverse splits must use ReverseStockSplit event type.";

        return null;
    }

    private static string? ValidateReverseSplit(CorporateActionDto action)
    {
        if (!action.SplitRatio.HasValue)
            return "ReverseStockSplit corporate actions must include SplitRatio.";

        if (action.SplitRatio.Value <= 0m || action.SplitRatio.Value >= 1m)
            return "ReverseStockSplit SplitRatio must be greater than 0 and less than 1.";

        return null;
    }

    private static string? ValidateDividend(CorporateActionDto action)
    {
        if (!action.DividendPerShare.HasValue)
            return "Dividend corporate actions must include DividendPerShare.";

        return null;
    }

    private static string? ValidateCashCurrency(CorporateActionDto action)
    {
        var requiresCurrency = action.DividendPerShare.HasValue || action.SubscriptionPricePerShare.HasValue;
        if (!requiresCurrency)
            return null;

        if (string.IsNullOrWhiteSpace(action.Currency))
            return "Corporate action cash amounts must include Currency.";

        return action.Currency.Length == 3 && action.Currency.All(char.IsAsciiLetter)
            ? null
            : "Corporate action Currency must be a three-letter ISO currency code.";
    }

    private static string? NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
}
