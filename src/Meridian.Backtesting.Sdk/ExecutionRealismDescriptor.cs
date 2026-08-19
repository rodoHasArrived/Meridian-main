using System.Globalization;
using System.Text;

namespace Meridian.Backtesting.Sdk;

/// <summary>
/// The complete set of execution-realism settings that determine what numbers a backtest
/// produces, captured as a single comparable value.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because strategy-run identity was previously computed from strategy identity,
/// dataset, engine, and parameters alone. Two runs that differed only in fill timing or cost model
/// — and therefore produced materially different P&amp;L — carried the same input hash and were
/// treated as the same experiment by run diffing, sweep lineage, and promotion evidence. Folding
/// these settings into the hash closes that hole.
/// </para>
/// <para>
/// <see cref="ToCanonicalString"/> is the serialization used for hashing. Numeric formatting is
/// deliberately normalized so that values which are equal as numbers never hash differently
/// because of decimal scale.
/// </para>
/// </remarks>
public sealed record ExecutionRealismDescriptor(
    ExecutionModel DefaultExecutionModel,
    FillTiming FillTiming,
    FillConservatism FillConservatism,
    DelistingPolicy DelistingPolicy,
    decimal DelistingHaircutPercent,
    int DelistingGraceDays,
    BacktestCommissionKind CommissionKind,
    decimal CommissionRate,
    decimal CommissionMinimum,
    decimal CommissionMaximum,
    decimal SlippageBasisPoints,
    decimal MaxParticipationRate,
    decimal MarketImpactCoefficient,
    decimal OrderBookQueueAheadFraction,
    bool AdjustForCorporateActions,
    double RiskFreeRate)
{
    /// <summary>
    /// Field-order-stable canonical form used for hashing and for human-readable disclosure.
    /// Each field is emitted with its name so that adding a field in a future revision cannot
    /// silently alias an existing one.
    /// </summary>
    public string ToCanonicalString()
    {
        var builder = new StringBuilder();
        Append(builder, nameof(DefaultExecutionModel), DefaultExecutionModel.ToString());
        Append(builder, nameof(FillTiming), FillTiming.ToString());
        Append(builder, nameof(FillConservatism), FillConservatism.ToString());
        Append(builder, nameof(DelistingPolicy), DelistingPolicy.ToString());
        Append(builder, nameof(DelistingHaircutPercent), Canonical(DelistingHaircutPercent));
        Append(builder, nameof(DelistingGraceDays), DelistingGraceDays.ToString(CultureInfo.InvariantCulture));
        Append(builder, nameof(CommissionKind), CommissionKind.ToString());
        Append(builder, nameof(CommissionRate), Canonical(CommissionRate));
        Append(builder, nameof(CommissionMinimum), Canonical(CommissionMinimum));
        Append(builder, nameof(CommissionMaximum), Canonical(CommissionMaximum));
        Append(builder, nameof(SlippageBasisPoints), Canonical(SlippageBasisPoints));
        Append(builder, nameof(MaxParticipationRate), Canonical(MaxParticipationRate));
        Append(builder, nameof(MarketImpactCoefficient), Canonical(MarketImpactCoefficient));
        Append(builder, nameof(OrderBookQueueAheadFraction), Canonical(OrderBookQueueAheadFraction));
        Append(builder, nameof(AdjustForCorporateActions), AdjustForCorporateActions ? "true" : "false");
        Append(builder, nameof(RiskFreeRate), RiskFreeRate.ToString("R", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append('=').Append(value).Append('|');

    /// <summary>
    /// Formats a decimal without trailing zeros. <see cref="decimal"/> preserves scale, so
    /// <c>1.0m</c> and <c>1.00m</c> render differently under the default formatter even though
    /// they are equal numbers — which would otherwise produce two different hashes for one
    /// configuration.
    /// </summary>
    private static string Canonical(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
