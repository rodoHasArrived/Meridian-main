using Meridian.Core.Config;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Vendor daily boundaries are explicit; they are not inferred from another vendor's plan.</summary>
public enum DailyQuotaResetMode
{
    Rolling24Hours,
    UtcMidnight
}

/// <summary>Immutable effective limits for one credential and vendor quota group.</summary>
public sealed record RequestBudgetConfiguration(
    int Maximum,
    TimeSpan Window,
    TimeSpan MinimumDelay,
    int? DailyMaximum = null,
    DailyQuotaResetMode DailyReset = DailyQuotaResetMode.Rolling24Hours,
    string Unit = "requests")
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Maximum, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumDelay, TimeSpan.Zero);
        if (DailyMaximum is { } maximum)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximum, 0, nameof(DailyMaximum));
        if (!Enum.IsDefined(DailyReset))
            throw new ArgumentOutOfRangeException(nameof(DailyReset));
        if (Unit is not ("requests" or "credits"))
            throw new ArgumentException("A request budget unit must be requests or credits.", nameof(Unit));
    }
}

/// <summary>A coherent admission snapshot. Units are credits for weighted vendor APIs.</summary>
public sealed record RequestBudgetStatus(
    string Unit,
    int UnitsInWindow,
    int Maximum,
    TimeSpan Window,
    int? DailyUnitsUsed,
    int? DailyMaximum,
    DateTimeOffset? DailyResetsAt,
    DailyQuotaResetMode DailyReset,
    DateTimeOffset? NextAvailableAt)
{
    public bool IsLimited => NextAvailableAt.HasValue;
}

/// <summary>One account profile is used across history, search and corporate actions.</summary>
public static class ProviderQuotaBudgets
{
    public static RequestBudgetConfiguration TwelveData(TwelveDataQuotaConfig? profile = null)
    {
        profile ??= new();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(profile.CreditsPerMinute, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(profile.DailyCredits, 0);
        var budget = new RequestBudgetConfiguration(profile.CreditsPerMinute, TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1) / profile.CreditsPerMinute, profile.NoDailyLimit ? null : profile.DailyCredits, DailyQuotaResetMode.UtcMidnight, "credits");
        budget.Validate();
        return budget;
    }

    public static RequestBudgetConfiguration AlphaVantage(AlphaVantageQuotaConfig? profile = null)
    {
        profile ??= new();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(profile.RequestsPerMinute, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(profile.DailyRequests, 0);
        var budget = new RequestBudgetConfiguration(profile.RequestsPerMinute, TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1) / profile.RequestsPerMinute, profile.NoDailyLimit ? null : profile.DailyRequests);
        budget.Validate();
        return budget;
    }
}
