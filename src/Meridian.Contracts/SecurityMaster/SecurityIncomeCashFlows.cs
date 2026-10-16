using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>What a declared income cash flow represents to the holder.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityIncomeCashFlowKind>))]
public enum SecurityIncomeCashFlowKind
{
    /// <summary>Regular cash dividend on an equity-like class.</summary>
    Dividend,

    /// <summary>Extraordinary cash dividend, tax-distinct from regular dividends.</summary>
    SpecialDividend,

    /// <summary>Cash distribution from a fund class (money market, investment fund, fund interest).</summary>
    FundDistribution,

    /// <summary>Capital returned to the holder; reduces cost basis and is not income.</summary>
    ReturnOfCapital
}

/// <summary>Tax component of a fund distribution, read from the <c>distributionComponent</c> payload key.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityIncomeComponent>))]
public enum SecurityIncomeComponent
{
    Income,
    TaxExemptIncome,
    ShortTermCapitalGain,
    LongTermCapitalGain,
    ReturnOfCapital
}

/// <summary>
/// One declared income cash flow, per unit held, derived from an effective (amendment-folded,
/// non-cancelled) corporate action. Amounts are per share or fund unit in <see cref="Currency"/>;
/// holders multiply by their position at <see cref="RecordDate"/> (or <see cref="ExDate"/>).
/// </summary>
public sealed record SecurityIncomeCashFlow(
    Guid CorpActId,
    string EventType,
    SecurityIncomeCashFlowKind Kind,
    SecurityIncomeComponent Component,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly PaymentDate,
    decimal AmountPerUnit,
    string Currency,
    bool IsIncome);

/// <summary>A corporate action that is a cash distribution but could not be placed on the schedule.</summary>
public sealed record SecurityIncomeCashFlowGap(
    Guid CorpActId,
    string EventType,
    DateOnly ExDate,
    string Reason);

/// <summary>
/// Declared income schedule for a security: the cash flows for the asset classes whose
/// <see cref="SecurityCashFlowBehavior"/> is recorded rather than calculated
/// (<see cref="SecurityCashFlowBehavior.Dividends"/>, <see cref="SecurityCashFlowBehavior.FundDistributions"/>).
/// Distributions that cannot be scheduled (no pay date, no amount, unknown component) are listed in
/// <see cref="Gaps"/> instead of being silently dropped.
/// </summary>
public sealed record SecurityIncomeCashFlowScheduleDto(
    Guid SecurityId,
    string AssetClass,
    SecurityCashFlowBehavior CashFlowBehavior,
    DateTimeOffset AsOf,
    IReadOnlyList<SecurityIncomeCashFlow> Flows,
    IReadOnlyList<SecurityIncomeCashFlowGap> Gaps);

/// <summary>
/// Builds the declared income schedule from a security's corporate actions. Pure: the caller loads
/// the actions; amendments and cancellations are folded by
/// <see cref="CorporateActionEffectiveStateProjector"/> first, so only effective terms produce flows.
/// </summary>
public static class SecurityIncomeCashFlowProjector
{
    public static SecurityIncomeCashFlowScheduleDto Build(
        Guid securityId,
        string assetClass,
        IReadOnlyList<CorporateActionDto> actions,
        DateTimeOffset asOf,
        DateOnly? paymentFrom = null,
        DateOnly? paymentTo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetClass);
        ArgumentNullException.ThrowIfNull(actions);

        var behavior = SecurityAssetClassRegistry.TryGet(assetClass, out var definition)
            ? definition.Descriptor.CashFlowBehavior
            : SecurityCashFlowBehavior.None;
        var isFund = behavior == SecurityCashFlowBehavior.FundDistributions;

        var flows = new List<SecurityIncomeCashFlow>();
        var gaps = new List<SecurityIncomeCashFlowGap>();
        foreach (var action in CorporateActionEffectiveStateProjector.ProjectEffectiveActions(actions, asOf))
        {
            if (action.SecurityId != securityId)
            {
                continue;
            }

            var kind = action.EventType switch
            {
                CorporateActionEventTypes.Dividend => isFund ? SecurityIncomeCashFlowKind.FundDistribution : SecurityIncomeCashFlowKind.Dividend,
                CorporateActionEventTypes.SpecialDividend => SecurityIncomeCashFlowKind.SpecialDividend,
                CorporateActionEventTypes.ReturnOfCapital => SecurityIncomeCashFlowKind.ReturnOfCapital,
                _ => (SecurityIncomeCashFlowKind?)null
            };
            if (kind is null)
            {
                continue;
            }

            if (!TryResolveComponent(action, kind.Value, out var component))
            {
                gaps.Add(Gap(action, $"Unknown {CorporateActionPayloads.DistributionComponent} '{CorporateActionPayloads.ReadString(action.Payload, CorporateActionPayloads.DistributionComponent)}'."));
                continue;
            }

            var amount = kind == SecurityIncomeCashFlowKind.ReturnOfCapital
                ? action.DividendPerShare ?? CorporateActionPayloads.ReadDecimal(action.Payload, CorporateActionPayloads.CapitalReturnPerShare)
                : action.DividendPerShare;
            if (amount is not > 0m)
            {
                gaps.Add(Gap(action, "No positive amount per unit is recorded."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(action.Currency))
            {
                gaps.Add(Gap(action, "No currency is recorded."));
                continue;
            }

            if (action.PayDate is not { } payDate)
            {
                gaps.Add(Gap(action, "No pay date is recorded, so the cash flow cannot be dated."));
                continue;
            }

            if ((paymentFrom is { } from && payDate < from) || (paymentTo is { } to && payDate > to))
            {
                continue;
            }

            flows.Add(new SecurityIncomeCashFlow(
                action.CorpActId,
                action.EventType,
                kind.Value,
                component,
                action.ExDate,
                action.RecordDate,
                payDate,
                amount.Value,
                action.Currency.Trim().ToUpperInvariant(),
                IsIncome: component is not SecurityIncomeComponent.ReturnOfCapital));
        }

        return new SecurityIncomeCashFlowScheduleDto(
            securityId,
            assetClass,
            behavior,
            asOf,
            flows.OrderBy(static flow => flow.PaymentDate).ThenBy(static flow => flow.CorpActId).ToArray(),
            gaps);
    }

    private static bool TryResolveComponent(CorporateActionDto action, SecurityIncomeCashFlowKind kind, out SecurityIncomeComponent component)
    {
        if (kind == SecurityIncomeCashFlowKind.ReturnOfCapital)
        {
            component = SecurityIncomeComponent.ReturnOfCapital;
            return true;
        }

        var declared = CorporateActionPayloads.ReadString(action.Payload, CorporateActionPayloads.DistributionComponent);
        if (declared is null)
        {
            component = SecurityIncomeComponent.Income;
            return true;
        }

        return Enum.TryParse(declared.Trim(), ignoreCase: true, out component) && Enum.IsDefined(component);
    }

    private static SecurityIncomeCashFlowGap Gap(CorporateActionDto action, string reason)
        => new(action.CorpActId, action.EventType, action.ExDate, reason);
}
