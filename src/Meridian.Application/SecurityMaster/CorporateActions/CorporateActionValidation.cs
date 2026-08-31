using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CorporateActions;

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

        if (!CorporateActionTypeDescriptorCatalog.TryNormalize(normalized.EventType, out var descriptor))
        {
            return $"Unsupported corporate action EventType '{action.EventType}'. Accepted event types: " +
                   $"{string.Join(", ", CorporateActionTypeDescriptorCatalog.All.Select(static d => d.CanonicalName))} " +
                   "(provider aliases and ISO 15022 CAEV codes are also accepted).";
        }

        var payloadError = CorporateActionPayloadSchemaCatalog.Validate(normalized, forAcceptance: true);
        if (payloadError is not null)
        {
            return payloadError;
        }

        if (!CorporateActionLifecycleStates.IsWriteable(normalized.LifecycleState))
        {
            return $"Corporate action LifecycleState '{normalized.LifecycleState}' is not writeable. " +
                   $"Writeable states: {CorporateActionLifecycleStates.Announced}, {CorporateActionLifecycleStates.Confirmed}, " +
                   $"{CorporateActionLifecycleStates.Cancelled}; Ex and Paid are derived from ExDate/PayDate at read time.";
        }

        if (normalized.LifecycleState == CorporateActionLifecycleStates.Cancelled && !normalized.SupersedesCorpActId.HasValue)
            return "Cancelled corporate actions must supersede the event they cancel via SupersedesCorpActId.";

        if (normalized.SupersedesCorpActId.HasValue && normalized.SupersedesCorpActId.Value == normalized.CorpActId)
            return "Corporate actions cannot supersede themselves.";

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

        if (normalized.RedemptionPricePercentOfPar.HasValue && normalized.RedemptionPricePercentOfPar.Value <= 0m)
            return "RedemptionPricePercentOfPar must be greater than 0.";

        var currencyError = ValidateCashCurrency(normalized);
        if (currencyError is not null)
            return currencyError;

        foreach (var field in descriptor.RequiredFields)
        {
            var requiredFieldError = ValidateRequiredField(normalized, descriptor, field);
            if (requiredFieldError is not null)
                return requiredFieldError;
        }

        return normalized.EventType switch
        {
            CorporateActionEventTypes.StockSplit => ValidateForwardSplit(normalized),
            CorporateActionEventTypes.ReverseStockSplit => ValidateReverseSplit(normalized),
            CorporateActionEventTypes.Dividend => ValidateDividend(normalized),
            _ => null
        };
    }

    /// <summary>
    /// Validates the event type against the security's asset class using the descriptor's
    /// validity list. A null/blank asset class skips the check (fail-open — the taxonomy
    /// check itself remains fail-closed); an unknown event type is reported by
    /// <see cref="Validate"/>, not here.
    /// </summary>
    public static string? ValidateForAssetClass(CorporateActionDto action, string? assetClass)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (string.IsNullOrWhiteSpace(assetClass)
            || !CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor)
            || descriptor.AppliesToAssetClass(assetClass))
        {
            return null;
        }

        return $"{descriptor.CanonicalName} corporate actions are not valid for asset class '{assetClass.Trim()}'. " +
               $"Valid asset classes: {string.Join(", ", descriptor.ValidSecurityMasterAssetClasses)}.";
    }

    /// <summary>
    /// Validates a superseding append (amendment or cancellation) against the security's
    /// already-recorded actions: the reference must resolve, target the chain tip, retain
    /// the event type, and never move the lifecycle backwards (except to Cancelled).
    /// </summary>
    public static string? ValidateSupersede(CorporateActionDto superseding, IReadOnlyList<CorporateActionDto> existingActions)
    {
        ArgumentNullException.ThrowIfNull(superseding);
        ArgumentNullException.ThrowIfNull(existingActions);

        if (superseding.SupersedesCorpActId is not { } supersededId)
            return null;

        var superseded = existingActions.FirstOrDefault(action => action.CorpActId == supersededId);
        if (superseded is null)
            return $"Corporate action supersedes unknown corporate action '{supersededId:D}' for this security.";

        var alreadySuperseded = existingActions.FirstOrDefault(action =>
            action.SupersedesCorpActId == supersededId && action.CorpActId != superseding.CorpActId);
        if (alreadySuperseded is not null)
        {
            return $"Corporate action '{supersededId:D}' is already superseded by '{alreadySuperseded.CorpActId:D}'; " +
                   "supersede the chain tip instead.";
        }

        var supersededType = CorporateActionEventTypes.Normalize(superseded.EventType);
        var supersedingType = CorporateActionEventTypes.Normalize(superseding.EventType);
        if (!string.Equals(supersededType, supersedingType, StringComparison.Ordinal))
        {
            return $"Superseding corporate action must retain event type '{supersededType}'; " +
                   "cancel the original and append a new announcement to reclassify.";
        }

        var newRank = CorporateActionLifecycleStates.Rank(superseding.LifecycleState);
        var oldRank = CorporateActionLifecycleStates.Rank(superseded.LifecycleState);
        if (newRank < oldRank)
        {
            return $"Superseding corporate action may not move the lifecycle backwards " +
                   $"(from '{superseded.LifecycleState ?? CorporateActionLifecycleStates.Confirmed}' " +
                   $"to '{superseding.LifecycleState ?? CorporateActionLifecycleStates.Confirmed}').";
        }

        return null;
    }

    private static string? ValidateRequiredField(
        CorporateActionDto action,
        CorporateActionTypeDescriptor descriptor,
        CorporateActionField field)
        => field switch
        {
            CorporateActionField.DividendPerShare when !action.DividendPerShare.HasValue
                => MissingField(descriptor, nameof(CorporateActionDto.DividendPerShare)),
            CorporateActionField.Currency when string.IsNullOrWhiteSpace(action.Currency)
                => MissingField(descriptor, nameof(CorporateActionDto.Currency)),
            CorporateActionField.SplitRatio when !action.SplitRatio.HasValue
                => MissingField(descriptor, nameof(CorporateActionDto.SplitRatio)),
            CorporateActionField.NewSecurityId when action.NewSecurityId is null || action.NewSecurityId == Guid.Empty
                => MissingField(descriptor, nameof(CorporateActionDto.NewSecurityId)),
            CorporateActionField.DistributionRatio when !action.DistributionRatio.HasValue
                => MissingField(descriptor, nameof(CorporateActionDto.DistributionRatio)),
            CorporateActionField.AcquirerSecurityId when action.AcquirerSecurityId is null || action.AcquirerSecurityId == Guid.Empty
                => MissingField(descriptor, nameof(CorporateActionDto.AcquirerSecurityId)),
            CorporateActionField.ExchangeRatio when action.ExchangeRatio is null or <= 0m
                => $"{descriptor.CanonicalName} corporate actions must include a positive ExchangeRatio.",
            CorporateActionField.SubscriptionPricePerShare when !action.SubscriptionPricePerShare.HasValue
                => MissingField(descriptor, nameof(CorporateActionDto.SubscriptionPricePerShare)),
            CorporateActionField.RightsPerShare when action.RightsPerShare is null or <= 0m
                => $"{descriptor.CanonicalName} corporate actions must include a positive RightsPerShare.",
            CorporateActionField.PayDate when !action.PayDate.HasValue
                => MissingField(descriptor, nameof(CorporateActionDto.PayDate)),
            CorporateActionField.RedemptionPricePercentOfPar when action.RedemptionPricePercentOfPar is null or <= 0m
                => $"{descriptor.CanonicalName} corporate actions must include a positive RedemptionPricePercentOfPar.",
            _ => null
        };

    private static string MissingField(CorporateActionTypeDescriptor descriptor, string fieldName)
        => $"{descriptor.CanonicalName} corporate actions must include {fieldName}.";

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
