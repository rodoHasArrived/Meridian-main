using Meridian.Contracts.Api;

namespace Meridian.Core.Config;

/// <summary>Shared presets and validation for vendor account quotas and physical request pacing.</summary>
public static class ProviderQuotaCatalog
{
    public const string TwelveData = "twelvedata";
    public const string AlphaVantage = "alphavantage";

    public static bool IsEditable(string providerId) => providerId is TwelveData or AlphaVantage;

    public static IReadOnlyList<ProviderQuotaTierDto> GetTiers(string providerId)
        => IsEditable(providerId)
            ? BackfillProviderCatalog.Get(providerId).Tiers.Select(ToQuotaTier).ToArray()
            : [];

    public static ProviderQuotaSettingsDto Read(AppConfig config, string providerId)
    {
        ArgumentNullException.ThrowIfNull(config);
        var (unitsPerMinute, dailyLimit) = providerId switch
        {
            TwelveData => Read(config.ProviderQuotas?.TwelveData ?? new()),
            AlphaVantage => Read(config.ProviderQuotas?.AlphaVantage ?? new()),
            _ => throw new ArgumentException("This provider has no editable account quota.", nameof(providerId))
        };
        var pacing = BackfillProviderCatalog.Read(config.Backfill?.Providers ?? new(), providerId);
        var tier = GetTiers(providerId).FirstOrDefault(candidate => candidate.Id == pacing.TierId);
        var tierId = tier is not null &&
            unitsPerMinute is > 0 && unitsPerMinute <= tier.MinuteCeiling &&
            dailyLimit is > 0 && dailyLimit <= tier.DailyCeiling
                ? tier.Id
                : null;
        return new(tierId, unitsPerMinute, dailyLimit, pacing.RateLimitPerMinute!.Value);
    }

    public static AppConfig Apply(AppConfig config, string providerId, ProviderQuotaEditDto edit)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(edit);
        var tier = GetTiers(providerId).FirstOrDefault(candidate => candidate.Id == edit.TierId)
            ?? throw new ArgumentException("Select a supported quota tier.", nameof(edit));
        if (edit.UnitsPerMinute < 1 || edit.UnitsPerMinute > tier.MinuteCeiling)
            throw new ArgumentException("Minute allowance must be positive and within the selected tier ceiling.", nameof(edit));
        if (edit.RequestsPerMinute < 1 || edit.RequestsPerMinute > tier.MinuteCeiling)
            throw new ArgumentException("Request pacing must be positive and within the selected tier ceiling.", nameof(edit));
        var dailyLimit = edit.DailyLimit ?? tier.DefaultDailyLimit;
        if (dailyLimit is not > 0 || dailyLimit > tier.DailyCeiling)
            throw new ArgumentException("Enter a positive finite daily cap within the selected tier ceiling.", nameof(edit));

        var backfill = config.Backfill ?? new();
        var providers = backfill.Providers ?? new();
        var pacing = BackfillProviderCatalog.Read(providers, providerId);
        var nextProviders = BackfillProviderCatalog.Apply(providers, providerId,
            new BackfillProviderEdit(pacing.Enabled, pacing.Priority, tier.Id, edit.RequestsPerMinute));

        var quotas = config.ProviderQuotas ?? new();
        var nextQuotas = providerId == TwelveData
            ? quotas with
            {
                TwelveData = (quotas.TwelveData ?? new()) with
                {
                    CreditsPerMinute = edit.UnitsPerMinute, DailyCredits = dailyLimit.Value, NoDailyLimit = false
                }
            }
            : quotas with { AlphaVantage = new(edit.UnitsPerMinute, dailyLimit.Value) };
        if (nextProviders == providers && nextQuotas == quotas)
            return config;
        return config with { Backfill = backfill with { Providers = nextProviders }, ProviderQuotas = nextQuotas };
    }

    private static ProviderQuotaTierDto ToQuotaTier(BackfillVendorTier tier)
    {
        var minute = tier.RateLimitPerMinute!.Value;
        return new(tier.Id, tier.Label, minute, tier.DailyQuotaLimit ?? minute * 24 * 60, tier.DailyQuotaLimit);
    }

    private static (int, int?) Read(TwelveDataQuotaConfig quota)
        => (quota.CreditsPerMinute, quota.NoDailyLimit ? null : quota.DailyCredits);

    private static (int, int?) Read(AlphaVantageQuotaConfig quota)
        => (quota.RequestsPerMinute, quota.NoDailyLimit ? null : quota.DailyRequests);
}
