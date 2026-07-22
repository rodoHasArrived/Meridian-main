using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>How the preferred return capitalizes over time.</summary>
public enum PreferredReturnCompounding
{
    /// <summary>Interest accrues on contributed capital only; accrued pref never joins the base.</summary>
    Simple = 0,

    /// <summary>Accrued pref capitalizes into the base at each cash-flow boundary (compounded).</summary>
    Compound = 1,
}

/// <summary>A dated capital contribution that begins accruing preferred return.</summary>
public sealed record CapitalContribution(DateOnly Date, decimal Amount);

/// <summary>A dated return of contributed capital that reduces the preferred-return base.</summary>
public sealed record CapitalReturn(DateOnly Date, decimal Amount);

/// <summary>One accrual segment between two cash-flow boundaries.</summary>
public sealed record PreferredReturnSegment(
    DateOnly From,
    DateOnly To,
    decimal Base,
    decimal AccruedInterest);

/// <summary>Result of accruing preferred return to a valuation date.</summary>
public sealed record PreferredReturnResult(
    decimal ContributedCapital,
    decimal ReturnedCapital,
    decimal AccruedPreferredReturn,
    IReadOnlyList<PreferredReturnSegment> Segments)
{
    /// <summary>Contributed capital not yet returned.</summary>
    public decimal OutstandingCapital => ContributedCapital - ReturnedCapital;
}

/// <summary>
/// Computes a compounding (or simple) preferred return on an investor's contributed-capital
/// timeline using Actual/365 day-weighting. The pref accrues on outstanding contributed capital;
/// in compound mode accrued pref capitalizes into the base at each cash-flow boundary. Pure and
/// deterministic — this is the pref the waterfall no longer requires callers to pre-compute.
/// </summary>
public static class PreferredReturnCalculator
{
    public static PreferredReturnResult Accrue(
        IReadOnlyList<CapitalContribution> contributions,
        DateOnly asOf,
        decimal annualRate,
        PreferredReturnCompounding compounding = PreferredReturnCompounding.Compound,
        IReadOnlyList<CapitalReturn>? returnsOfCapital = null)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        if (annualRate < 0m)
            throw new ArgumentOutOfRangeException(nameof(annualRate), annualRate, "Preferred return rate cannot be negative.");

        var boundaries = BuildBoundaries(contributions, returnsOfCapital ?? [], asOf);
        var segments = new List<PreferredReturnSegment>();

        var capital = 0m;
        var pref = 0m;
        var contributed = 0m;
        var returned = 0m;
        var cursor = boundaries.Count > 0 ? boundaries[0].Date : asOf;

        foreach (var boundary in boundaries)
        {
            if (boundary.Date > cursor)
            {
                var days = boundary.Date.DayNumber - cursor.DayNumber;
                var accrualBase = compounding == PreferredReturnCompounding.Compound ? capital + pref : capital;
                var interest = RoundCurrency(accrualBase * annualRate * days / 365m);
                if (interest != 0m)
                    segments.Add(new PreferredReturnSegment(cursor, boundary.Date, accrualBase, interest));
                pref += interest;
                cursor = boundary.Date;
            }

            capital += boundary.ContributionAmount;
            contributed += boundary.ContributionAmount;

            if (boundary.ReturnAmount > 0m)
            {
                returned += boundary.ReturnAmount;
                var remaining = boundary.ReturnAmount;
                var capitalReduction = Math.Min(capital, remaining);
                capital -= capitalReduction;
                remaining -= capitalReduction;
                if (remaining > 0m)
                    pref = Math.Max(0m, pref - remaining);
            }
        }

        return new PreferredReturnResult(contributed, returned, pref, segments);
    }

    private static IReadOnlyList<PreferredReturnBoundary> BuildBoundaries(
        IReadOnlyList<CapitalContribution> contributions,
        IReadOnlyList<CapitalReturn> returnsOfCapital,
        DateOnly asOf)
    {
        var byDate = new SortedDictionary<DateOnly, (decimal Contribution, decimal Return)>();

        foreach (var contribution in contributions)
        {
            if (contribution.Amount <= 0m)
                throw new ArgumentException("Contribution amounts must be positive.", nameof(contributions));
            if (contribution.Date > asOf)
                continue;
            var current = byDate.TryGetValue(contribution.Date, out var value) ? value : default;
            byDate[contribution.Date] = (current.Contribution + contribution.Amount, current.Return);
        }

        foreach (var capitalReturn in returnsOfCapital)
        {
            if (capitalReturn.Amount <= 0m)
                throw new ArgumentException("Return-of-capital amounts must be positive.", nameof(returnsOfCapital));
            if (capitalReturn.Date > asOf)
                continue;
            var current = byDate.TryGetValue(capitalReturn.Date, out var value) ? value : default;
            byDate[capitalReturn.Date] = (current.Contribution, current.Return + capitalReturn.Amount);
        }

        var boundaries = byDate
            .Select(pair => new PreferredReturnBoundary(pair.Key, pair.Value.Contribution, pair.Value.Return))
            .ToList();

        // Ensure a final boundary at asOf so the last segment accrues up to the valuation date.
        if (boundaries.Count > 0 && boundaries[^1].Date < asOf)
            boundaries.Add(new PreferredReturnBoundary(asOf, 0m, 0m));

        return boundaries;
    }

    private sealed record PreferredReturnBoundary(DateOnly Date, decimal ContributionAmount, decimal ReturnAmount);
}
