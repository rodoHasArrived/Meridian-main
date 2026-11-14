namespace Meridian.Core.Config;

/// <summary>Explicit vendor account limits shared by every capability using the same API credential.</summary>
public sealed record ProviderQuotasConfig(
    TwelveDataQuotaConfig? TwelveData = null,
    AlphaVantageQuotaConfig? AlphaVantage = null);

/// <summary>
/// Twelve Data Basic defaults. Daily credits reset at midnight UTC. A paid account must
/// explicitly declare its limits and fundamentals entitlement; numeric capacity alone grants none.
/// NoDailyLimit explicitly declares an account without a daily credit cap.
/// </summary>
public sealed record TwelveDataQuotaConfig(
    int CreditsPerMinute = 8,
    int DailyCredits = 800,
    bool FundamentalsEntitled = false,
    bool NoDailyLimit = false);

/// <summary>
/// Alpha Vantage conservative free-account limits. The daily cap uses a rolling 24-hour
/// window because the vendor publishes no reset boundary. NoDailyLimit declares an uncapped account.
/// </summary>
public sealed record AlphaVantageQuotaConfig(
    int RequestsPerMinute = 5,
    int DailyRequests = 25,
    bool NoDailyLimit = false);
