using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Validates the opt-in servicing policies on direct-lending terms: the PIK capitalization rule and
/// the floating-rate reset schedule. Terms without either policy are always valid here.
/// </summary>
public static class DirectLendingServicingPolicyValidator
{
    /// <summary>Returns the first validation message, or <see langword="null"/> when the policies are valid.</summary>
    public static string? Validate(DirectLendingTermsDto terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return ValidatePikCapitalization(terms) ?? ValidateRateResetSchedule(terms);
    }

    private static string? ValidatePikCapitalization(DirectLendingTermsDto terms)
    {
        if (terms.PikCapitalization is not { } policy)
        {
            return null;
        }

        if (!Enum.IsDefined(policy.Mode))
        {
            return $"PIK capitalization mode '{(int)policy.Mode}' is not supported.";
        }

        if (!Enum.IsDefined(policy.Frequency))
        {
            return $"PIK capitalization frequency '{(int)policy.Frequency}' is not supported.";
        }

        var share = RateUnitNormalizer.Normalize(policy.PikShare, DirectLendingRateField.PikShare);
        if (!share.IsSuccess)
        {
            return share.Error;
        }

        if (policy.AnchorDate < terms.OriginationDate || policy.AnchorDate > terms.MaturityDate)
        {
            return "PIK capitalization anchor date must fall between the origination and maturity dates.";
        }

        return null;
    }

    private static string? ValidateRateResetSchedule(DirectLendingTermsDto terms)
    {
        if (terms.RateResetSchedule is not { } schedule)
        {
            return null;
        }

        if (terms.RateTypeKind != RateTypeKind.Floating)
        {
            return "A rate reset schedule requires floating-rate terms.";
        }

        if (string.IsNullOrWhiteSpace(schedule.IndexName) ||
            !string.Equals(schedule.IndexName.Trim(), terms.InterestIndexName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"Rate reset schedule index '{schedule.IndexName}' must match the terms interest index '{terms.InterestIndexName}'.";
        }

        if (!Enum.IsDefined(schedule.Frequency))
        {
            return $"Rate reset frequency '{(int)schedule.Frequency}' is not supported.";
        }

        if (!Enum.IsDefined(schedule.LookbackDayKind) || !Enum.IsDefined(schedule.FloorCapAppliesTo))
        {
            return "Rate reset lookback day kind and floor/cap application must be supported values.";
        }

        if (schedule.LookbackDays < 0 || schedule.LookbackDays > 31)
        {
            return "Rate reset lookback must be between 0 and 31 days.";
        }

        if (schedule.FirstResetDate < terms.OriginationDate || schedule.FirstResetDate > terms.MaturityDate)
        {
            return "The first reset date must fall between the origination and maturity dates.";
        }

        if (schedule.ResetDates is { Count: > 0 } explicitDates &&
            explicitDates.Any(date => date < terms.OriginationDate || date > terms.MaturityDate))
        {
            return "Every explicit reset date must fall between the origination and maturity dates.";
        }

        var spread = RateUnitNormalizer.Normalize(schedule.Spread, DirectLendingRateField.Spread);
        if (!spread.IsSuccess)
        {
            return spread.Error;
        }

        decimal? floor = null;
        if (schedule.Floor is not null)
        {
            var normalizedFloor = RateUnitNormalizer.Normalize(schedule.Floor, DirectLendingRateField.Floor);
            if (!normalizedFloor.IsSuccess)
            {
                return normalizedFloor.Error;
            }

            floor = normalizedFloor.Fraction;
        }

        if (schedule.Cap is not null)
        {
            var normalizedCap = RateUnitNormalizer.Normalize(schedule.Cap, DirectLendingRateField.Cap);
            if (!normalizedCap.IsSuccess)
            {
                return normalizedCap.Error;
            }

            if (floor is { } floorValue && normalizedCap.Fraction < floorValue)
            {
                return "Rate reset cap must be greater than or equal to the floor.";
            }
        }

        return null;
    }
}
