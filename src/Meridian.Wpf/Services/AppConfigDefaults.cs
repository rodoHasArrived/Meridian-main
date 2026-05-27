using Meridian.Contracts.Configuration;

namespace Meridian.Wpf.Services;

/// <summary>
/// Canonical desktop defaults for first-run bootstrap and Settings reset flows.
/// </summary>
public static class AppConfigDefaults
{
    public static AppConfigDto CreateDefaultAppConfig()
    {
        return new AppConfigDto
        {
            DataRoot = MeridianPathDefaults.DefaultDataRoot,
            DataSource = "NoOp",
            Symbols = [],
            Storage = new StorageConfigDto
            {
                NamingConvention = "BySymbol",
                Profile = "Standard"
            },
            Backfill = new BackfillConfigDto
            {
                Enabled = false,
                Provider = "stooq",
                EnableFallback = true,
                EnableSymbolResolution = true,
                Providers = new BackfillProvidersConfigDto
                {
                    Alpaca = new BackfillProviderOptionsDto { Enabled = true, Priority = 5, RateLimitPerMinute = 200 },
                    Polygon = new BackfillProviderOptionsDto { Enabled = true, Priority = 12, RateLimitPerMinute = 5 },
                    Tiingo = new BackfillProviderOptionsDto { Enabled = true, Priority = 15, RateLimitPerHour = 50 },
                    Finnhub = new BackfillProviderOptionsDto { Enabled = true, Priority = 18, RateLimitPerMinute = 60 },
                    Stooq = new BackfillProviderOptionsDto { Enabled = true, Priority = 20 },
                    Yahoo = new BackfillProviderOptionsDto { Enabled = true, Priority = 22, RateLimitPerHour = 2000 },
                    AlphaVantage = new BackfillProviderOptionsDto { Enabled = false, Priority = 25, RateLimitPerMinute = 5 },
                    NasdaqDataLink = new BackfillProviderOptionsDto { Enabled = true, Priority = 30 }
                }
            },
            Settings = new AppSettingsDto
            {
                Theme = "System",
                AccentColor = "System",
                CompactMode = false,
                NotificationsEnabled = true,
                StatusRefreshIntervalSeconds = 2
            }
        };
    }
}
